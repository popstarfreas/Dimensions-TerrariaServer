using System;
using System.IO;
using System.IO.Streams;
using System.Collections.Generic;
using TShockAPI;

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
                    var ban = TShock.Bans.GetBanByIp(remoteAddress);
                    if (ban != null)
                    {
                        args.Player.Disconnect("Banned: " + ban.Reason);
                    }
                    break;
                // case 1 is handled by GameModes
            }
            return false;
		}
	}
}