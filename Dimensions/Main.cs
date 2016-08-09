using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Timers;
using System.Linq;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.Hooks;
using Rests;
using Newtonsoft.Json;

namespace Dimensions
{
	[ApiVersion(1,23)]
	public class Dimensions : TerrariaPlugin
    {
        public static string[] RealIPs = new string[256];
		public Timer OnSecondUpdate;
        public static Config Config = new Config();

        public override string Author
		{
			get
			{
				return "popstarfreas";
			}
		}

		public override string Description
		{
			get
			{
				return "Adds more Dimensions to Terraria Travel";
			}
		}

		public override string Name
		{
			get
			{
				return "Dimensions";
			}
		}

		public override Version Version
		{
			get
			{
				return new Version(1, 2, 0);
			}
		}

		public Dimensions(Main game) : base(game)
        {
            Order = 1;
        }
		
		public override void Initialize()
		{
			ServerApi.Hooks.NetGetData.Register(this, GetData);
            TShock.RestApi.Register(new SecureRestCommand("/d/ban", BanUser, "dimensions.rest.ban"));
            //TShock.RestApi.Register(new SecureRestCommand("/d/tempban/{username}/{reason}", TempBanUser, "dimensions.rest.tempban"));
            //TShock.RestApi.Register(new SecureRestCommand("/d/offlinetempban/{username}/{reason}", OfflineTempBanUser, "dimensions.rest.tempban"));
            TShockAPI.Commands.ChatCommands.Add(new Command("dimensions.reload", Reload, "dimreload"));
            TShockAPI.Hooks.PlayerHooks.PlayerPostLogin += OnPlayerLogin;
            GetDataHandlers.InitGetDataHandler();

            string path = Path.Combine(TShock.SavePath, "Dimensions.json");
            if (!File.Exists(path))
                Config.WriteTemplates(path);
            Config = Config.Read(path);
        }

        private void OnPlayerLogin(PlayerPostLoginEventArgs args)
        {
            List<String> KnownIps = new List<string>();
            if (!string.IsNullOrWhiteSpace(args.Player.User.KnownIps))
            {
                KnownIps = JsonConvert.DeserializeObject<List<String>>(args.Player.User.KnownIps);
            }

            if (RealIPs[args.Player.Index] != "") {
                KnownIps.Where(ip => ip == Config.RoutingIP).ToList().ForEach(p => p = RealIPs[args.Player.Index]);
                args.Player.User.KnownIps = JsonConvert.SerializeObject(KnownIps, Formatting.Indented);
                TShock.Users.UpdateLogin(args.Player.User);
            }
        }

        private object BanUser(RestRequestArgs args)
        {
            var username = args.Parameters["username"];
            Console.WriteLine("Username: " + username);
            var reason = args.Parameters["reason"];
            var player = TShock.Players.FirstOrDefault(p => p != null && p.Name == username);
            RestObject ret;
            if (player == null)
            {
                ret = new RestObject()
                {
                    {"success", false}
                };
            } else {
                bool success = TShock.Bans.AddBan(RealIPs[player.Index], player.User != null ? player.User.Name : player.Name, player.UUID, reason);
                player.Disconnect("Banned: "+reason);
                ret = new RestObject()
                {
                    {"success", success},
                };
            }
            return ret;
        }

        void Reload(CommandArgs e)
        {
            string path = Path.Combine(TShock.SavePath, "Dimensions.json");
            if (!File.Exists(path))
                Config.WriteTemplates(path);
            Config = Config.Read(path);
            e.Player.SendSuccessMessage("Reloaded Dimensions config.");
        }

        protected override void Dispose(bool disposing)
		{
			if (disposing) {
                ServerApi.Hooks.NetGetData.Deregister(this, GetData);
                TShockAPI.Hooks.PlayerHooks.PlayerPostLogin -= OnPlayerLogin;
                base.Dispose (disposing);
			}
        }

		private void GetData(GetDataEventArgs args)
		{
			var type = args.MsgID;
			var player = TShock.Players[args.Msg.whoAmI];

			if (player == null)
			{
				args.Handled = true;
				return;
			}

			if (!player.ConnectionAlive)
			{
				args.Handled = true;
				return;
			}

			using (var data = new MemoryStream(args.Msg.readBuffer, args.Index, args.Length))
			{
				try
				{
					if (GetDataHandlers.HandlerGetData(type, player, data))
						args.Handled = true;
				}
				catch (Exception ex)
				{
					TShock.Log.ConsoleError(ex.ToString());
				}
			}
		}
	}
}

