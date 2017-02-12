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

	public class GetDataHandlers
    {
        private static Dictionary<PacketTypes, GetDataHandlerDelegate> _getDataHandlerDelegates;
        private Dimensions Dimensions;

		public GetDataHandlers(Dimensions Dimensions)
		{
            this.Dimensions = Dimensions;

			_getDataHandlerDelegates = new Dictionary<PacketTypes, GetDataHandlerDelegate>
			{
				{PacketTypes.Placeholder, HandleDimensionsMessage},
			};
		}

		public bool HandlerGetData(PacketTypes type, TSPlayer player, MemoryStream data)
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

		private bool HandleDimensionsMessage(GetDataHandlerArgs args)
		{
			if (args.Player == null) return false;
			var index = args.Player.Index;
            var joinType = args.Data.ReadInt16();
			var joinInfo = args.Data.ReadString();
            var handled = false;

            switch (joinType)
            {
                case 0:
                    handled = HandleIpInformation(joinInfo, args.Player);
                    break;
                // case 1 is handled by GameModes
            }
            return handled;
		}

        private bool HandleIpInformation(string joinInfo, TSPlayer player)
        {
            string remoteAddress = joinInfo;
            typeof(TSPlayer).GetField("CacheIP", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                            .SetValue(player, remoteAddress);

            // This needs to be handled as Geo check for proxy in TShock runs before the IP is updated to the correct one
            if (Dimensions.Geo != null)
            {
                var code = Dimensions.Geo.TryGetCountryCode(System.Net.IPAddress.Parse(remoteAddress));
                player.Country = code == null ? "N/A" : GeoIPCountry.GetCountryNameByCode(code);
                if (code == "A1")
                {
                    if (TShock.Config.KickProxyUsers)
                    {
                        TShock.Utils.ForceKick(player, "Proxies are not allowed.", true, false);
                        return false;
                    }
                }
            }

            return true;
        }
	}
}