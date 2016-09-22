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
    [ApiVersion(1, 24)]
    public class Dimensions : TerrariaPlugin
    {
        public static string[] RealIPs = new string[256];

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
                return new Version(1, 3, 0);
            }
        }

        public Dimensions(Main game) : base(game)
        {
            Order = 1;
        }

        public override void Initialize()
        {
            ServerApi.Hooks.NetGetData.Register(this, GetData);
            GetDataHandlers.InitGetDataHandler();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ServerApi.Hooks.NetGetData.Deregister(this, GetData);
                base.Dispose(disposing);
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

