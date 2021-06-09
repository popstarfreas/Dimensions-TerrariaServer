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

        /// <summary>
        /// Handles a Dimensions message (such as real IP of a new client)
        /// </summary>
        /// <param name="args">The get data handler arguments</param>
        /// <returns>Whether this packet was handled</returns>
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

        /// <summary>
        /// Handles ip information on a new client
        /// </summary>
        /// <param name="remoteAddress">The IP address of the user</param>
        /// <param name="player">The player bound to the client with this IP</param>
        /// <returns>Whether or not the update was made successfully</returns>
        private bool HandleIpInformation(string remoteAddress, TSPlayer player)
        {
            typeof(TSPlayer).GetField("CacheIP", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                            .SetValue(player, remoteAddress);

            // This needs to be handled as Geo check for proxy in TShock runs before the IP is updated to the correct one
            if (Dimensions.Geo != null)
            {
                var code = Dimensions.Geo.TryGetCountryCode(System.Net.IPAddress.Parse(remoteAddress));
                player.Country = code == null ? "N/A" : GeoIPCountry.GetCountryNameByCode(code);
                if (code == "A1")
                {
                    if (TShock.Config.Settings.KickProxyUsers)
                    {
                        player.Kick("Proxies are not allowed.", true, true);
                        return false;
                    }
                }
            }

            return true;
        }
    }
}