using System;
using System.IO;
using System.IO.Streams;
using System.Collections.Generic;
using TShockAPI;
using MaxMind;

namespace Dimensions
{
	internal delegate bool GetDataHandlerDelegate(GetDataHandlerArgs args);

	internal class GetDataHandlerArgs : EventArgs
	{
		public TSPlayer Player { get; private set; }
		public MemoryStream Data { get; private set; }

		public GetDataHandlerArgs(TSPlayer player, MemoryStream data)
		{
			Player = player;
			Data = data;
		}
	}

	internal static class GetDataHandlers
    {
        static Random rnd = new Random();
        private static Dictionary<PacketTypes, GetDataHandlerDelegate> _getDataHandlerDelegates;

		public static void InitGetDataHandler()
		{
			_getDataHandlerDelegates = new Dictionary<PacketTypes, GetDataHandlerDelegate>
			{
				{PacketTypes.Placeholder, HandleJoinInformation},
			};
		}

		public static bool HandlerGetData(PacketTypes type, TSPlayer player, MemoryStream data)
		{
			GetDataHandlerDelegate handler;
			if (_getDataHandlerDelegates.TryGetValue(type, out handler))
			{
				try
				{
					return handler(new GetDataHandlerArgs(player, data));
				}
				catch (Exception ex)
				{
					TShock.Log.Error(ex.ToString());
				}
			}
			return false;
		}

		private static bool HandleJoinInformation(GetDataHandlerArgs args)
		{
			if (args.Player == null) return false;
			var index = args.Player.Index;
            var joinType = args.Data.ReadInt16();
			var joinInfo = args.Data.ReadString();

            switch (joinType)
            {
                case 0:
                    string remoteAddress = joinInfo;
                    Dimensions.RealIPs[args.Player.Index] = remoteAddress;
                    typeof(TSPlayer).GetField("CacheIP", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                                    .SetValue(args.Player, remoteAddress);

                    if (Dimensions.Geo != null)
                    {
                        var code = Dimensions.Geo.TryGetCountryCode(System.Net.IPAddress.Parse(remoteAddress));
                        args.Player.Country = code == null ? "N/A" : GeoIPCountry.GetCountryNameByCode(code);
                        if (code == "A1")
                        {
                            if (TShock.Config.KickProxyUsers)
                            {
                                TShock.Utils.ForceKick(args.Player, "Proxies are not allowed.", true, false);
                                return false;
                            }
                        }
                    }
                    break;
                // case 1 is handled by GameModes
            }
            return false;
		}
	}
}