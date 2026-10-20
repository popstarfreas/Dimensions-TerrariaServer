"""Publish the source version, retaining drafts and their commits for retries."""

import argparse
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import tempfile
from urllib.error import HTTPError, URLError
from urllib.request import Request, urlopen
import xml.etree.ElementTree as ET


PROJECT = Path("Dimensions/Dimensions.csproj")
SOURCE = Path("Dimensions/Main.cs")
ARTIFACT = Path("Dimensions/bin/Release/net9.0/Dimensions.dll")
VERSION_PATTERN = r"(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)"
COMMIT_PATTERN = r"[0-9a-f]{40}"


class ReleaseError(Exception):
    pass


def source_version():
    version = ET.parse(PROJECT).getroot().findtext("./PropertyGroup/Version", "")
    if not re.fullmatch(VERSION_PATTERN, version):
        raise ReleaseError("Project Version must be a stable major.minor.patch value.")
    matches = re.findall(r"return\s+new\s+Version\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*\)\s*;", SOURCE.read_text())
    if len(matches) != 1 or tuple(map(int, matches[0])) != tuple(map(int, version.split("."))):
        raise ReleaseError("The plugin Version in Main.cs must match the project Version.")
    return version


class GitHub:
    def __init__(self):
        self.repo = os.environ["GH_REPO"]
        self.token = os.environ["GH_TOKEN"]
        self.base = os.environ.get("GITHUB_API_URL", "https://api.github.com").rstrip("/")

    def get(self, path, missing_ok=False):
        request = Request(f"{self.base}/repos/{self.repo}/{path}", headers={
            "Authorization": f"Bearer {self.token}",
            "Accept": "application/vnd.github+json",
            "X-GitHub-Api-Version": "2026-03-10",
        })
        try:
            with urlopen(request, timeout=30) as response:
                return json.load(response)
        except HTTPError as error:
            if error.code == 404 and missing_ok:
                return None
            raise ReleaseError(f"GitHub lookup {path} failed: HTTP {error.code}.") from error
        except URLError as error:
            raise ReleaseError(f"GitHub lookup {path} failed: {error.reason}.") from error

    def release(self, tag):
        # Drafts can have an internal REST tag_name of "untagged-...". GraphQL
        # resolves their pending tag, as GitHub CLI's draft lookup does.
        owner, name = self.repo.split("/", 1)
        query = """query($owner: String!, $name: String!, $tagName: String!) {
          repository(owner: $owner, name: $name) {
            release(tagName: $tagName) { databaseId }
          }
        }"""
        result = json.loads(subprocess.check_output([
            "gh", "api", "graphql", "-f", f"query={query}",
            "-f", f"owner={owner}", "-f", f"name={name}", "-f", f"tagName={tag}",
        ], text=True))
        if result.get("errors"):
            raise ReleaseError("GitHub release lookup failed: " + "; ".join(
                error["message"] for error in result["errors"]))
        repository = (result.get("data") or {}).get("repository")
        if repository is None:
            raise ReleaseError("GitHub release lookup could not access the repository.")
        found = repository["release"]
        if found is None:
            return None
        return self.get(f"releases/{found['databaseId']}")

    def tag_commit(self, tag):
        ref = self.get(f"git/ref/tags/{tag}", missing_ok=True)
        if ref is None:
            return None
        target = ref["object"]
        while target["type"] == "tag":
            target = self.get(f"git/tags/{target['sha']}")["object"]
        if target["type"] != "commit":
            raise ReleaseError(f"{tag} must point to a commit.")
        return target["sha"]


def has_dll(release):
    return any(asset["name"] == ARTIFACT.name and asset["state"] == "uploaded"
               and asset["size"] > 0 for asset in release["assets"])


def release_state(github, version, fallback_commit):
    tag = f"v{version}"
    release = github.release(tag)
    if release is not None and not release["draft"]:
        if not has_dll(release):
            raise ReleaseError(f"Published release {tag} has no uploaded Dimensions.dll. Repair it manually or release a new version.")
        return {"version": version, "tag": tag, "commit": None, "publish": False}, release

    commit = github.tag_commit(tag)
    if release is not None:
        target = release["target_commitish"]
        if commit is None:
            if not re.fullmatch(COMMIT_PATTERN, target):
                raise ReleaseError(f"Draft {tag} has no tag or pinned commit. Pin its target to a full commit SHA before retrying.")
            commit = target
        elif re.fullmatch(COMMIT_PATTERN, target) and target != commit:
            raise ReleaseError(f"Draft {tag} and its tag point to different commits. Resolve the conflict before retrying.")
    commit = commit or fallback_commit
    if not re.fullmatch(COMMIT_PATTERN, commit):
        raise ReleaseError("The release target must be a full commit SHA.")
    return {"version": version, "tag": tag, "commit": commit, "publish": True}, release


def prepare(github, state_path):
    state, _ = release_state(github, source_version(), os.environ["GITHUB_SHA"])
    state_path.write_text(json.dumps(state))
    with open(os.environ["GITHUB_OUTPUT"], "a") as output:
        output.write(f"publish={str(state['publish']).lower()}\ncommit={state['commit'] or ''}\n")
    print(f"{state['tag']}: " + (f"build {state['commit']}" if state["publish"] else "already published; skipping"))


def verify(state):
    if source_version() != state["version"]:
        raise ReleaseError("The selected release commit has a different source version. Release a new version instead of moving the tag.")
    head = subprocess.check_output(["git", "rev-parse", "HEAD"], text=True).strip()
    if head != state["commit"]:
        raise ReleaseError("Checked-out HEAD does not match the pinned release commit.")


def gh(*args):
    subprocess.run(["gh", *args], check=True)


def publish(github, state):
    verify(state)
    if not ARTIFACT.is_file() or ARTIFACT.stat().st_size == 0:
        raise ReleaseError(f"Release artifact is missing or empty: {ARTIFACT}")
    current, release = release_state(github, state["version"], state["commit"])
    if not current["publish"]:
        print(f"{state['tag']} is already published; skipping.")
        return
    if current["commit"] != state["commit"]:
        raise ReleaseError("The release target changed while building. Retry to build the pinned commit.")

    tag = state["tag"]
    if release is None:
        packages = {item.attrib["Include"]: item.attrib["Version"]
                    for item in ET.parse(PROJECT).getroot().findall(".//PackageReference")}
        notes = (f"For Terraria 1.4.5.8, TShock {packages['TShock']}, TSAPI {packages['TSAPI']}, "
                 f"and OTAPI {packages['OTAPI.Upcoming']}. Requires .NET 9.\n\n"
                 "Install by copying Dimensions.dll into the server's ServerPlugins/ directory and restarting the server.\n")
        with tempfile.TemporaryDirectory() as directory:
            notes_file = Path(directory) / "notes.md"
            notes_file.write_text(notes)
            gh("release", "create", tag, "--draft", "--target", state["commit"],
               "--title", f"Dimensions {state['version']} for Terraria 1.4.5.8",
               "--generate-notes", "--notes-file", str(notes_file))

    # Recheck after draft creation; a failed upload leaves a resumable draft.
    release = github.release(tag)
    if release is None or not release["draft"]:
        raise ReleaseError(f"Expected a draft for {tag}; refusing to replace published assets.")
    gh("release", "upload", tag, str(ARTIFACT), "--clobber")
    release = github.release(tag)
    if release is None or not release["draft"] or not has_dll(release):
        raise ReleaseError(f"Draft {tag} has no completed DLL upload; it has not been published.")
    gh("release", "edit", tag, "--tag", tag, "--draft=false")
    print(f"Published {tag} from {state['commit']}.")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=["prepare", "verify", "publish"])
    parser.add_argument("--state", type=Path, required=True)
    args = parser.parse_args()
    try:
        if args.command == "prepare":
            prepare(GitHub(), args.state)
        else:
            state = json.loads(args.state.read_text())
            if args.command == "verify":
                verify(state)
            else:
                publish(GitHub(), state)
    except (ReleaseError, subprocess.CalledProcessError) as error:
        print(f"Release failed: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
