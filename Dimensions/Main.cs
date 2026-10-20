using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Timers;
using System.Linq;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.DB;
using TShockAPI.Hooks;
using Rests;
using MaxMind;

namespace Dimensions
{
    [ApiVersion(2, 1)]
    public class Dimensions : TerrariaPlugin
    {
        public GeoIPCountry Geo;
        public Config Config = new Config();
        public GetDataHandlers GetDataHandlers;
        private const string WaitingForCharacter = "Dimensions.WaitingForCharacter";
        /// <summary>
        /// Identifies the character locks of this server in a database shared with other servers
        /// </summary>
        private readonly string LockOwner = Guid.NewGuid().ToString();
        private DateTime LastLockUpdate;

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
                return new Version(1, 5, 1);
            }
        }

        public Dimensions(Main game) : base(game)
        {
            Order = 1;
        }

        /// <summary>
        /// Initializese the get data handlers, loads the config and sets up the GeoIP
        /// </summary>
        public override void Initialize()
        {
            ServerApi.Hooks.NetGetData.Register(this, GetData);
            GetDataHandlers = new GetDataHandlers(this);
            var geoippath = "Dimensions-GeoIP.dat";
            string path = Path.Combine(TShock.SavePath, "Dimensions.json");
            if (!File.Exists(path))
                Config.WriteTemplates(path);
            Config = Config.Read(path);

            if (Config.EnableGeoIP && File.Exists(geoippath))
                Geo = new GeoIPCountry(geoippath);

            // With server side characters shared between dimensions, each server locks the characters of its players until
            // they leave, so the next dimension does not load a character before the previous one saves it
            TShock.DB.Query("CREATE TABLE IF NOT EXISTS DimensionsCharacterLock (Account INT PRIMARY KEY, Owner VARCHAR(36), Heartbeat BIGINT)");
            PlayerHooks.PlayerPostLogin += OnPlayerPostLogin;
            PlayerHooks.PlayerLogout += OnPlayerLogout;
            ServerApi.Hooks.GameUpdate.Register(this, OnGameUpdate);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ServerApi.Hooks.NetGetData.Deregister(this, GetData);
                PlayerHooks.PlayerPostLogin -= OnPlayerPostLogin;
                PlayerHooks.PlayerLogout -= OnPlayerLogout;
                ServerApi.Hooks.GameUpdate.Deregister(this, OnGameUpdate);
                base.Dispose(disposing);
            }
        }

        /// <summary>
        /// Locks the character of a player to this server and loads it again, as TShock loads it before the lock is checked
        /// </summary>
        /// <param name="player">The logged in player</param>
        /// <returns>False if another server still holds the character</returns>
        private bool TryLockCharacter(TSPlayer player)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            using (var reader = TShock.DB.QueryReader("SELECT Owner, Heartbeat FROM DimensionsCharacterLock WHERE Account = @0", player.Account.ID))
            {
                // A server that stopped refreshing its locks for 10 seconds is no longer running
                if (reader.Read() && reader.Get<string>("Owner") != LockOwner && now - reader.Get<long>("Heartbeat") < 10000)
                    return false;
            }
            TShock.DB.Query("DELETE FROM DimensionsCharacterLock WHERE Account = @0", player.Account.ID);
            TShock.DB.Query("INSERT INTO DimensionsCharacterLock (Account, Owner, Heartbeat) VALUES (@0, @1, @2)", player.Account.ID, LockOwner, now);
            player.PlayerData = TShock.CharacterDB.GetPlayerData(player, player.Account.ID);
            player.PlayerData.RestoreCharacter(player);
            return true;
        }

        /// <summary>
        /// Locks the character of a player who logged in, or keeps them disabled until the server holding it releases it
        /// </summary>
        /// <param name="args">The player post login event arguments</param>
        private void OnPlayerPostLogin(PlayerPostLoginEventArgs args)
        {
            if (!Main.ServerSideCharacter || args.Player.HasPermission(Permissions.bypassssc) || args.Player.IsDisabledPendingTrashRemoval || TryLockCharacter(args.Player))
                return;

            // Being disabled this way also keeps TShock from saving the outdated character
            args.Player.IsDisabledPendingTrashRemoval = true;
            args.Player.SetData(WaitingForCharacter, true);
            args.Player.SendInfoMessage("Waiting for the previous dimension to save your character...");
        }

        /// <summary>
        /// Releases the lock of a player who leaves or logs out, which TShock fires next to saving the character
        /// </summary>
        /// <param name="args">The player logout event arguments</param>
        private void OnPlayerLogout(PlayerLogoutEventArgs args)
        {
            TShock.DB.Query("DELETE FROM DimensionsCharacterLock WHERE Account = @0 AND Owner = @1", args.Player.Account.ID, LockOwner);
        }

        /// <summary>
        /// Refreshes the locks of this server and loads the characters released by other servers, twice a second
        /// </summary>
        /// <param name="args">The event arguments</param>
        private void OnGameUpdate(EventArgs args)
        {
            if (!Main.ServerSideCharacter || (DateTime.UtcNow - LastLockUpdate).TotalMilliseconds < 500)
                return;

            LastLockUpdate = DateTime.UtcNow;
            TShock.DB.Query("UPDATE DimensionsCharacterLock SET Heartbeat = @0 WHERE Owner = @1", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), LockOwner);
            foreach (var player in TShock.Players)
            {
                if (player != null && player.IsLoggedIn && player.ContainsData(WaitingForCharacter) && TryLockCharacter(player))
                {
                    player.RemoveData(WaitingForCharacter);
                    player.IsDisabledPendingTrashRemoval = false;
                    player.SendSuccessMessage("Your character has been loaded.");
                }
            }
        }

        /// <summary>
        /// Reloads the config
        /// </summary>
        /// <param name="e">The command arguments</param>
        private void Reload(CommandArgs e)
        {
            string path = Path.Combine(TShock.SavePath, "Dimensions.json");
            if (!File.Exists(path))
                Config.WriteTemplates(path);
            Config = Config.Read(path);
            e.Player.SendSuccessMessage("Reloaded Dimensions config.");
        }

        /// <summary>
        /// Passes on packets to the data handlers
        /// </summary>
        /// <param name="args">The Get Data Event arguments</param>
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

