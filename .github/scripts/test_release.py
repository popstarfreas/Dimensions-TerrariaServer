"""Exercise release decisions and recovery without contacting GitHub."""

import copy
import io
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import Mock, patch
from urllib.error import HTTPError, URLError

import release


CURRENT = "a" * 40
ORIGINAL = "b" * 40
TAG = "v1.5.1"
DLL = {"name": "Dimensions.dll", "state": "uploaded", "size": 100}


def draft(commit=ORIGINAL, assets=()):
    return {"tag_name": TAG, "draft": True, "target_commitish": commit,
            "assets": list(assets)}


class FakeGitHub:
    def __init__(self, existing=None, commit=None):
        self.existing = existing
        self.commit = commit
        self.commands = []
        self.fail = None
        self.notes = None

    def release(self, tag):
        assert tag == TAG
        return copy.deepcopy(self.existing)

    def tag_commit(self, tag):
        assert tag == TAG
        return self.commit

    def gh(self, *args):
        self.commands.append(args)
        if args[1] == self.fail:
            raise subprocess.CalledProcessError(1, ["gh", *args])
        if args[1] == "create":
            commit = args[args.index("--target") + 1]
            self.existing = draft(commit)
            self.notes = Path(args[args.index("--notes-file") + 1]).read_text()
        elif args[1] == "upload":
            assert self.existing["draft"], "Must not replace published assets"
            assert Path(args[3]).name == "Dimensions.dll"
            self.existing["assets"] = [copy.deepcopy(DLL)]
        elif args[1] == "edit":
            assert self.existing["assets"]
            self.existing["draft"] = False
            self.commit = self.existing["target_commitish"]


class ReleaseTests(unittest.TestCase):
    def setUp(self):
        directory = tempfile.TemporaryDirectory()
        self.addCleanup(directory.cleanup)
        self.root = Path(directory.name)
        self.project = self.root / "Dimensions.csproj"
        self.source = self.root / "Main.cs"
        self.artifact = self.root / "Dimensions.dll"
        self.source.write_text("return new Version(1, 5, 1);")
        self.write_project("1.5.1")
        self.artifact.write_bytes(b"plugin dll")
        paths = patch.multiple(release, PROJECT=self.project, SOURCE=self.source,
                               ARTIFACT=self.artifact)
        paths.start()
        self.addCleanup(paths.stop)
        head = patch.object(release.subprocess, "check_output", return_value=CURRENT + "\n")
        self.head = head.start()
        self.addCleanup(head.stop)
        stdout = patch("sys.stdout", new_callable=io.StringIO)
        stdout.start()
        self.addCleanup(stdout.stop)

    def write_project(self, version):
        self.project.write_text(f"""<Project>
          <PropertyGroup><Version>{version}</Version></PropertyGroup>
          <ItemGroup>
            <PackageReference Include="TShock" Version="6.2.1" />
            <PackageReference Include="TSAPI" Version="6.2.1" />
            <PackageReference Include="OTAPI.Upcoming" Version="3.3.14" />
          </ItemGroup>
        </Project>""")

    def state(self, github, commit=CURRENT):
        return release.release_state(github, "1.5.1", commit)[0]

    def publish(self, github, state):
        with patch.object(release, "gh", side_effect=github.gh):
            release.publish(github, state)

    def test_source_version_and_malformed_versions(self):
        self.assertEqual(release.source_version(), "1.5.1")
        for version in ["1.5", "1.5.1.0", "v1.5.1", "1.5.1-beta", "01.5.1", "1.5.1\n"]:
            with self.subTest(version=version):
                self.write_project(version)
                with self.assertRaises(release.ReleaseError):
                    release.source_version()

    def test_mismatched_plugin_version_fails_before_github_lookup(self):
        self.source.write_text("return new Version(1, 5, 0);")
        github = Mock()
        with self.assertRaisesRegex(release.ReleaseError, "must match"):
            release.prepare(github, self.root / "state.json")
        github.release.assert_not_called()

    def test_prepare_writes_pinned_state_and_workflow_outputs(self):
        state_path = self.root / "state.json"
        output_path = self.root / "outputs"
        with patch.dict(os.environ, {"GITHUB_SHA": CURRENT, "GITHUB_OUTPUT": str(output_path)}):
            release.prepare(FakeGitHub(), state_path)
        self.assertEqual(json.loads(state_path.read_text()), {
            "version": "1.5.1", "tag": TAG, "commit": CURRENT, "publish": True})
        self.assertEqual(output_path.read_text(), f"publish=true\ncommit={CURRENT}\n")

    def test_complete_release_is_skipped(self):
        existing = draft(assets=[DLL])
        existing["draft"] = False
        github = FakeGitHub(existing)
        github.tag_commit = Mock(side_effect=AssertionError("No tag lookup needed"))
        state = self.state(github)
        self.assertFalse(state["publish"])

    def test_published_release_without_valid_dll_fails(self):
        for assets in [[], [{**DLL, "size": 0}], [{**DLL, "state": "starter"}]]:
            with self.subTest(assets=assets):
                existing = draft(assets=assets)
                existing["draft"] = False
                with self.assertRaisesRegex(release.ReleaseError, "Repair it manually"):
                    self.state(FakeGitHub(existing))

    def test_existing_tag_uses_original_commit(self):
        self.assertEqual(self.state(FakeGitHub(commit=ORIGINAL))["commit"], ORIGINAL)

    def test_draft_without_tag_uses_original_commit(self):
        self.assertEqual(self.state(FakeGitHub(draft()))["commit"], ORIGINAL)

    def test_unpinned_draft_without_tag_fails(self):
        with self.assertRaisesRegex(release.ReleaseError, "pinned commit"):
            self.state(FakeGitHub(draft("master")))

    def test_conflicting_tag_and_draft_fail(self):
        with self.assertRaisesRegex(release.ReleaseError, "different commits"):
            self.state(FakeGitHub(draft(CURRENT), ORIGINAL))

    def test_verify_rejects_different_version_or_commit(self):
        state = self.state(FakeGitHub())
        self.write_project("1.5.2")
        self.source.write_text("return new Version(1, 5, 2);")
        with self.assertRaisesRegex(release.ReleaseError, "different source version"):
            release.verify(state)
        self.write_project("1.5.1")
        self.source.write_text("return new Version(1, 5, 1);")
        state["commit"] = ORIGINAL
        with self.assertRaisesRegex(release.ReleaseError, "HEAD"):
            release.verify(state)

    def test_new_release_creates_draft_uploads_only_dll_then_publishes(self):
        github = FakeGitHub()
        self.publish(github, self.state(github))
        self.assertEqual([command[1] for command in github.commands], ["create", "upload", "edit"])
        create = github.commands[0]
        self.assertEqual(create[create.index("--target") + 1], CURRENT)
        self.assertIn("--draft", create)
        self.assertIn("--generate-notes", create)
        self.assertIn("Terraria 1.4.5.8", github.notes)
        self.assertIn("TShock 6.2.1", github.notes)
        self.assertIn("ServerPlugins/", github.notes)
        self.assertFalse(github.existing["draft"])
        self.assertEqual(github.commit, CURRENT)

    def test_failed_build_has_no_artifact_and_cannot_create_release(self):
        self.artifact.unlink()
        github = FakeGitHub()
        with self.assertRaisesRegex(release.ReleaseError, "missing or empty"):
            self.publish(github, self.state(github))
        self.assertEqual(github.commands, [])

    def test_upload_failure_retains_draft_and_resumes_original_commit(self):
        github = FakeGitHub()
        github.fail = "upload"
        with self.assertRaises(subprocess.CalledProcessError):
            self.publish(github, self.state(github))
        self.assertTrue(github.existing["draft"])
        self.assertEqual([command[1] for command in github.commands], ["create", "upload"])
        github.fail = None
        resumed = self.state(github, ORIGINAL)
        self.assertEqual(resumed["commit"], CURRENT)
        self.publish(github, resumed)
        self.assertFalse(github.existing["draft"])
        self.assertEqual([command[1] for command in github.commands].count("create"), 1)

    def test_publication_failure_resumes_uploaded_draft(self):
        github = FakeGitHub(draft(CURRENT, [DLL]))
        github.fail = "edit"
        with self.assertRaises(subprocess.CalledProcessError):
            self.publish(github, self.state(github))
        self.assertTrue(github.existing["draft"])
        github.fail = None
        self.publish(github, self.state(github))
        self.assertFalse(github.existing["draft"])

    def test_existing_tag_without_release_is_published_from_tagged_commit(self):
        github = FakeGitHub(commit=ORIGINAL)
        self.head.return_value = ORIGINAL + "\n"
        self.publish(github, self.state(github))
        create = github.commands[0]
        self.assertEqual(create[create.index("--target") + 1], ORIGINAL)

    def test_changed_remote_target_prevents_publication(self):
        github = FakeGitHub()
        state = self.state(github)
        github.commit = ORIGINAL
        with self.assertRaisesRegex(release.ReleaseError, "changed while building"):
            self.publish(github, state)
        self.assertEqual(github.commands, [])

    def test_release_published_during_build_is_not_overwritten(self):
        github = FakeGitHub()
        state = self.state(github)
        github.existing = {**draft(CURRENT, [DLL]), "draft": False}
        self.publish(github, state)
        self.assertEqual(github.commands, [])

    def test_incomplete_upload_is_not_published(self):
        github = FakeGitHub(draft(CURRENT))
        def incomplete_upload(*args):
            github.commands.append(args)
        with patch.object(release, "gh", side_effect=incomplete_upload):
            with self.assertRaisesRegex(release.ReleaseError, "no completed DLL upload"):
                release.publish(github, self.state(github))
        self.assertEqual([command[1] for command in github.commands], ["upload"])


class GitHubTests(unittest.TestCase):
    def setUp(self):
        env = patch.dict(os.environ, {"GH_REPO": "owner/repo", "GH_TOKEN": "test-token"})
        env.start()
        self.addCleanup(env.stop)
        self.github = release.GitHub()

    def test_only_explicitly_allowed_404_is_treated_as_missing(self):
        for code in [401, 403, 404, 429, 500]:
            with self.subTest(code=code):
                error = HTTPError("https://api.github.com/test", code, "error", {}, None)
                with patch.object(release, "urlopen", side_effect=error):
                    if code == 404:
                        self.assertIsNone(self.github.get("git/ref/tags/absent", missing_ok=True))
                    else:
                        with self.assertRaises(release.ReleaseError):
                            self.github.get("git/ref/tags/absent", missing_ok=True)
                    with self.assertRaises(release.ReleaseError):
                        self.github.get("releases")

    def test_network_failure_is_not_treated_as_missing(self):
        with patch.object(release, "urlopen", side_effect=URLError("offline")):
            with self.assertRaisesRegex(release.ReleaseError, "offline"):
                self.github.get("git/ref/tags/absent", missing_ok=True)

    def test_release_lookup_finds_drafts_across_pages(self):
        first_page = [{"tag_name": f"v0.0.{number}"} for number in range(100)]
        with patch.object(self.github, "get", side_effect=[first_page, [draft()]]) as get:
            self.assertTrue(self.github.release(TAG)["draft"])
        self.assertEqual(get.call_count, 2)
        self.assertEqual(get.call_args.args[0], "releases?per_page=100&page=2")

    def test_lightweight_and_annotated_tags_resolve_to_commits(self):
        commit = {"object": {"type": "commit", "sha": ORIGINAL}}
        annotated = {"object": {"type": "tag", "sha": CURRENT}}
        for replies in [[commit], [annotated, commit]]:
            with self.subTest(replies=replies):
                with patch.object(self.github, "get", side_effect=replies):
                    self.assertEqual(self.github.tag_commit(TAG), ORIGINAL)


if __name__ == "__main__":
    unittest.main()
