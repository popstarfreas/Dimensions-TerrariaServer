using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using TShockAPI;
using Newtonsoft.Json;
using TShockAPI.DB;

namespace Dimensions
{
    public static class Commands
    {
        public static string Specifier
        {
            get { return string.IsNullOrWhiteSpace(TShock.Config.CommandSpecifier) ? "/" : TShock.Config.CommandSpecifier; }
        }

        public static string SilentSpecifier
        {
            get { return string.IsNullOrWhiteSpace(TShock.Config.CommandSilentSpecifier) ? "." : TShock.Config.CommandSilentSpecifier; }
        }

        public static void Ban(TSPlayer banner, List<string> parameters, bool silent)
        {
            string subcmd = parameters.Count == 0 ? "help" : parameters[0].ToLower();
            switch (subcmd)
            {
                case "add":
                    BanAdd(banner, parameters, silent);
                    return;
                case "addip":
                    BanAddIP(banner, parameters, silent);
                    return;
                case "addtemp":
                    BanAddTemp(banner, parameters, silent);
                    return;
                case "del":
                    BanDel(banner, parameters, silent);
                    return;
                case "delip":
                    BanDelIP(banner, parameters, silent);
                    return;
                case "help":
                    BanHelp(banner, parameters, silent);
                    return;
                case "list":
                    BanList(banner, parameters, silent);
                    return;
                case "listip":
                    BanListIP(banner, parameters, silent);
                    return;
                default:
                    banner.SendErrorMessage("Invalid subcommand!", Specifier);
                    BanHelp(banner, parameters, silent);
                    return;
            }
        }

        private static void BanAdd(TSPlayer banner, List<string> parameters, bool silent)
        {
            if (parameters.Count < 2)
            {
                banner.SendErrorMessage("Invalid syntax! Proper syntax: {0}ban add <player> [reason]", Specifier);
                return;
            }

            List<TSPlayer> players = TShock.Utils.FindPlayer(parameters[1]);
            string reason = parameters.Count > 2 ? String.Join(" ", parameters.Skip(2)) : "Misbehavior.";
            if (players.Count == 0)
            {
                var user = TShock.Users.GetUserByName(parameters[1]);
                if (user != null)
                {
                    bool force = !banner.RealPlayer;

                    if (user.Name == banner.Name && !force)
                    {
                        banner.SendErrorMessage("You can't ban yourself!");
                        return;
                    }

                    if (TShock.Groups.GetGroupByName(user.Group).HasPermission(Permissions.immunetoban) && !force)
                        banner.SendErrorMessage("You can't ban {0}!", user.Name);
                    else
                    {
                        if (user.KnownIps == null)
                        {
                            banner.SendErrorMessage("Cannot ban {0} because they have no IPs to ban.", user.Name);
                            return;
                        }
                        var knownIps = JsonConvert.DeserializeObject<List<string>>(user.KnownIps);
                        TShock.Bans.AddBan(knownIps.Last(), user.Name, user.UUID, reason, false, banner.User.Name);
                        if (String.IsNullOrWhiteSpace(banner.User.Name))
                        {
                            if (silent)
                            {
                                banner.SendInfoMessage("{0} was {1}banned for '{2}'.", user.Name, force ? "Force " : "", reason);
                            }
                            else
                            {
                                TSPlayer.All.SendInfoMessage("{0} was {1}banned for '{2}'.", user.Name, force ? "Force " : "", reason);
                            }
                        }
                        else
                        {
                            if (silent)
                            {
                                banner.SendInfoMessage("{0}banned {1} for '{2}'.", force ? "Force " : "", user.Name, reason);
                            }
                            else
                            {
                                TSPlayer.All.SendInfoMessage("{0} {1}banned {2} for '{3}'.", banner.Name, force ? "Force " : "", user.Name, reason);
                            }
                        }
                    }
                }
                else
                    banner.SendErrorMessage("Invalid player or account!");
            }
            else if (players.Count > 1)
                TShock.Utils.SendMultipleMatchError(banner, players.Select(p => p.Name));
            else
            {
                bool force = !players[0].RealPlayer;
				bool success = TShock.Bans.AddBan(Dimensions.RealIPs[players[0].Index], players[0].User != null ? players[0].User.Name : players[0].Name, players[0].UUID, reason);
                players[0].Disconnect("Banned: " + reason);
                if (success)
                {
					if (players[0].User == null || String.IsNullOrWhiteSpace(players[0].User.Name))
                    {
                        if (silent)
                        {
							banner.SendInfoMessage("{0} was {1}banned for '{2}'.", players[0].Name, force ? "Force " : "", reason);
                        }
                        else
                        {
							TSPlayer.All.SendInfoMessage("{0} was {1}banned for '{2}'.", players[0].Name, force ? "Force " : "", reason);
                        }
                    }
                    else
                    {
                        if (silent)
                        {
							banner.SendInfoMessage("{0}banned {1} for '{2}'.", force ? "Force " : "", players[0].User.Name, reason);
                        }
                        else
                        {
							TSPlayer.All.SendInfoMessage("{0} {1}banned {2} for '{3}'.", players[0].Name, force ? "Force " : "", players[0].User.Name, reason);
                        }
                    }
                }
                else
                {
                    banner.SendErrorMessage("You can't ban {0}!", players[0].Name);
                }
            }
        }

        private static void BanAddIP(TSPlayer banner, List<string> parameters, bool silent)
        {
            if (parameters.Count < 2)
            {
                banner.SendErrorMessage("Invalid syntax! Proper syntax: {0}ban addip <ip> [reason]", Specifier);
                return;
            }

            string ip = parameters[1];
            string reason = parameters.Count > 2
                                ? String.Join(" ", parameters.GetRange(2, parameters.Count - 2))
                                : "Manually added IP address ban.";
            TShock.Bans.AddBan(ip, "", "", reason, false, banner.User.Name);
            banner.SendSuccessMessage("Banned IP {0}.", ip);
        }

        private static void BanAddTemp(TSPlayer banner, List<string> parameters, bool silent)
        {
            if (parameters.Count < 3)
            {
                banner.SendErrorMessage("Invalid syntax! Proper syntax: {0}ban addtemp <player> <time> [reason]", Specifier);
                return;
            }

            int time;
            if (!TShock.Utils.TryParseTime(parameters[2], out time))
            {
                banner.SendErrorMessage("Invalid time string! Proper format: _d_h_m_s, with at least one time specifier.");
                banner.SendErrorMessage("For example, 1d and 10h-30m+2m are both valid time strings, but 2 is not.");
                return;
            }

            string reason = parameters.Count > 3
                                ? String.Join(" ", parameters.Skip(3))
                                : "Misbehavior.";

            List<TSPlayer> players = TShock.Utils.FindPlayer(parameters[1]);
            if (players.Count == 0)
            {
                var user = TShock.Users.GetUserByName(parameters[1]);
                if (user != null)
                {
                    bool force = !banner.RealPlayer;
                    if (TShock.Groups.GetGroupByName(user.Group).HasPermission(Permissions.immunetoban) && !force)
                        banner.SendErrorMessage("You can't ban {0}!", user.Name);
                    else
                    {
                        var knownIps = JsonConvert.DeserializeObject<List<string>>(user.KnownIps);
                        TShock.Bans.AddBan(knownIps.Last(), user.Name, user.UUID, reason, false, banner.User.Name, DateTime.UtcNow.AddSeconds(time).ToString("s"));
                        if (String.IsNullOrWhiteSpace(banner.User.Name))
                        {
                            if (silent)
                            {
                                banner.SendInfoMessage("{0} was {1}banned for '{2}'.", user.Name, force ? "force " : "", reason);
                            }
                            else
                            {
                                TSPlayer.All.SendInfoMessage("{0} was {1}banned for '{2}'.", user.Name, force ? "force " : "", reason);
                            }
                        }
                        else
                        {
                            if (silent)
                            {
                                banner.SendInfoMessage("{0} was {1}banned for '{2}'.", user.Name, force ? "force " : "", reason);
                            }
                            else
                            {
                                TSPlayer.All.SendInfoMessage("{0} {1}banned {2} for '{3}'.", banner.Name, force ? "force " : "", user.Name, reason);
                            }
                        }
                    }
                }
                else
                {
                    banner.SendErrorMessage("Invalid player or account!");
                }
            }
            else if (players.Count > 1)
                TShock.Utils.SendMultipleMatchError(banner, players.Select(p => p.Name));
            else
            {
                if (banner.RealPlayer && players[0].HasPermission(Permissions.immunetoban))
                {
                    banner.SendErrorMessage("You can't ban {0}!", players[0].Name);
                    return;
                }

                if (TShock.Bans.AddBan(players[0].IP, players[0].Name, players[0].UUID, reason,
                    false, banner.Name, DateTime.UtcNow.AddSeconds(time).ToString("s")))
                {
                    players[0].Disconnect(String.Format("Banned: {0}", reason));
                    string verb = banner.RealPlayer ? "Force " : "";
                    if (banner.RealPlayer)
                        if (silent)
                        {
                            banner.SendSuccessMessage("{0}banned {1} for '{2}'", verb, players[0].Name, reason);
                        }
                        else
                        {
                            TSPlayer.All.SendSuccessMessage("{0} {1}banned {2} for '{3}'", banner.Name, verb, players[0].Name, reason);
                        }
                    else
                    {
                        if (silent)
                        {
                            banner.SendSuccessMessage("{0}banned {1} for '{2}'", verb, players[0].Name, reason);
                        }
                        else
                        {
                            TSPlayer.All.SendSuccessMessage("{0} was {1}banned for '{2}'", players[0].Name, verb, reason);
                        }
                    }
                }
                else
                    banner.SendErrorMessage("Failed to ban {0}, check logs.", players[0].Name);
            }
        }

        private static void BanDel(TSPlayer banner, List<string> parameters, bool silent)
        {
            if (parameters.Count != 2)
            {
                banner.SendErrorMessage("Invalid syntax! Proper syntax: {0}ban del <player>", Specifier);
                return;
            }

            string plStr = parameters[1];
            Ban ban = TShock.Bans.GetBanByName(plStr, false);
            if (ban != null)
            {
                if (TShock.Bans.RemoveBan(ban.Name, true))
                    banner.SendSuccessMessage("Unbanned {0} ({1}).", ban.Name, ban.IP);
                else
                    banner.SendErrorMessage("Failed to unban {0} ({1}), check logs.", ban.Name, ban.IP);
            }
            else
                banner.SendErrorMessage("No bans for {0} exist.", plStr);
        }

        private static void BanDelIP(TSPlayer banner, List<string> parameters, bool silent)
        {
            if (parameters.Count != 2)
            {
                banner.SendErrorMessage("Invalid syntax! Proper syntax: {0}ban delip <ip>", Specifier);
                return;
            }

            string ip = parameters[1];
            Ban ban = TShock.Bans.GetBanByIp(ip);
            if (ban != null)
            {
                if (TShock.Bans.RemoveBan(ban.IP, false))
                    banner.SendSuccessMessage("Unbanned IP {0} ({1}).", ban.IP, ban.Name);
                else
                    banner.SendErrorMessage("Failed to unban IP {0} ({1}), check logs.", ban.IP, ban.Name);
            }
            else
                banner.SendErrorMessage("IP {0} is not banned.", ip);
        }

        private static void BanHelp(TSPlayer banner, List<string> parameters, bool silent)
        {
            int pageNumber;
            if (!PaginationTools.TryParsePageNumber(parameters, 1, banner, out pageNumber))
                return;

            var lines = new List<string>
                        {
                            "add <player> [reason] - Bans a player or user account if the player is not online.",
                            "addip <ip> [reason] - Bans an IP.",
                            "addtemp <player> <time> [reason] - Temporarily bans a player.",
                            "del <player> - Unbans a player.",
                            "delip <ip> - Unbans an IP.",
                            "list [page] - Lists all player bans.",
                            "listip [page] - Lists all IP bans."
                        };

            PaginationTools.SendPage(banner, pageNumber, lines,
                new PaginationTools.Settings
                {
                    HeaderFormat = "Ban Sub-Commands ({0}/{1}):",
                    FooterFormat = "Type {0}ban help {{0}} for more sub-commands.".SFormat(Specifier)
                }
            );
        }

        private static void BanList(TSPlayer banner, List<string> parameters, bool silent)
        {
            int pageNumber;
            if (!PaginationTools.TryParsePageNumber(parameters, 1, banner, out pageNumber))
            {
                return;
            }

            List<Ban> bans = TShock.Bans.GetBans();

            var nameBans = from ban in bans
                           where !String.IsNullOrEmpty(ban.Name)
                           select ban.Name;

            PaginationTools.SendPage(banner, pageNumber, PaginationTools.BuildLinesFromTerms(nameBans),
                new PaginationTools.Settings
                {
                    HeaderFormat = "Bans ({0}/{1}):",
                    FooterFormat = "Type {0}ban list {{0}} for more.".SFormat(Specifier),
                    NothingToDisplayString = "There are currently no bans."
                });
        }

        private static void BanListIP(TSPlayer banner, List<string> parameters, bool silent)
        {
            int pageNumber;
            if (!PaginationTools.TryParsePageNumber(parameters, 1, banner, out pageNumber))
            {
                return;
            }

            List<Ban> bans = TShock.Bans.GetBans();

            var ipBans = from ban in bans
                         where String.IsNullOrEmpty(ban.Name)
                         select ban.IP;

            PaginationTools.SendPage(banner, pageNumber, PaginationTools.BuildLinesFromTerms(ipBans),
                new PaginationTools.Settings
                {
                    HeaderFormat = "IP Bans ({0}/{1}):",
                    FooterFormat = "Type {0}ban listip {{0}} for more.".SFormat(Specifier),
                    NothingToDisplayString = "There are currently no IP bans."
                });
        }

        public static void UserInfo(TSPlayer user, List<string> parameters, bool silent)
        {
            if (parameters.Count < 1)
            {
                user.SendErrorMessage("Invalid syntax! Proper syntax: {0}userinfo <player>", Specifier);
                return;
            }

            var players = TShock.Utils.FindPlayer(parameters[0]);
            if (players.Count < 1)
                user.SendErrorMessage("Invalid player.");
            else if (players.Count > 1)
                TShock.Utils.SendMultipleMatchError(user, players.Select(p => p.Name));
            else
            {
                var message = new StringBuilder();
                message.Append("IP Address: ").Append(Dimensions.RealIPs[players[0].Index]);
                if (players[0].User != null && players[0].IsLoggedIn)
                    message.Append(" | Logged in as: ").Append(players[0].User.Name).Append(" | Group: ").Append(players[0].Group.Name);
                user.SendSuccessMessage(message.ToString());
            }
        }
    }
}
