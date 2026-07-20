using System;
using System.Collections.Concurrent;
using System.Threading;
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Helpers;
using Archipelago.MultiClient.Net.Models;
using Archipelago.MultiClient.Net.Packets;
using WtgArchipelago.Mapping;

namespace WtgArchipelago;

/// <summary>
/// Owns the Archipelago session: connect/login, send location checks, receive
/// items. All AP callbacks arrive OFF Unity's main thread, so effects that
/// touch the game are queued and drained by Tick() (called from a Harmony
/// postfix on a game Update -- see GamePatches). Unity "notoriously hates
/// threading", so never touch game objects directly in a callback.
/// </summary>
public class ArchipelagoClient
{
    public ArchipelagoData Data { get; } = new();
    public ArchipelagoSession Session { get; private set; }
    public DeathLinkHandler DeathLink { get; private set; }
    public bool Connected { get; private set; }

    private readonly ConcurrentQueue<Action> _mainThread = new();

    /// <summary>Drain queued game-side effects. Call ONLY from the main thread.</summary>
    public void Tick()
    {
        while (_mainThread.TryDequeue(out var action))
        {
            try { action(); }
            catch (Exception e) { Plugin.Log.LogError($"main-thread action failed: {e}"); }
        }
    }

    public void Connect(string host, int port, string slot, string password = null)
    {
        Data.Host = host; Data.Port = port; Data.SlotName = slot; Data.Password = password;

        // Connect off-thread so we never block Unity's main loop.
        ThreadPool.QueueUserWorkItem(_ => ConnectImpl());
    }

    private void ConnectImpl()
    {
        try
        {
            Session = ArchipelagoSessionFactory.CreateSession(Data.Host, Data.Port);

            // Wire events BEFORE logging in.
            Session.Items.ItemReceived += OnItemReceived;
            Session.MessageLog.OnMessageReceived += m => Plugin.Log.LogInfo(m.ToString());
            Session.Socket.ErrorReceived += (e, msg) => Plugin.Log.LogError($"AP socket: {msg}");
            Session.Socket.SocketClosed += reason => { Connected = false; Plugin.Log.LogWarning($"AP closed: {reason}"); };

            LoginResult result = Session.TryConnectAndLogin(
                Plugin.GameName,
                Data.SlotName,
                ItemsHandlingFlags.AllItems,
                new Version(0, 6, 7),
                password: Data.Password,
                requestSlotData: true);

            if (result is LoginFailure failure)
            {
                Plugin.Log.LogError("AP login failed: " + string.Join("; ", failure.Errors));
                return;
            }

            var success = (LoginSuccessful)result;
            ReadSlotData(success.SlotData);

            DeathLink = new DeathLinkHandler(this, Data.DeathLinkEnabled);
            Connected = true;
            Plugin.Log.LogInfo($"AP connected as {Data.SlotName}.");
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"AP connect error: {e}");
        }
    }

    private void ReadSlotData(System.Collections.Generic.Dictionary<string, object> slotData)
    {
        if (slotData == null) return;
        if (slotData.TryGetValue("death_link", out var dl)) Data.DeathLinkEnabled = Convert.ToBoolean(dl);
        if (slotData.TryGetValue("goal", out var g)) Data.Goal = Convert.ToInt32(g);
        if (slotData.TryGetValue("area_access", out var aa)) Data.AreaAccess = Convert.ToString(aa);
        if (slotData.TryGetValue("boss_keys", out var bk)) Data.BossKeysEnabled = Convert.ToBoolean(bk);
        BossGate.SetEnabled(Data.BossKeysEnabled);
    }

    // --- Location checks -----------------------------------------------------

    /// <summary>Report an AP location as checked (safe to call from any thread).</summary>
    public void SendCheck(long locationId)
    {
        if (Session == null || locationId < 0) return;
        if (!Data.CheckedLocations.Add(locationId)) return;   // already sent
        Session.Locations.CompleteLocationChecks(locationId);
        Plugin.Log.LogInfo($"AP check sent: {locationId}");
    }

    /// <summary>Resolve a level's scene name to its Clear location and send it.</summary>
    public void SendClear(string scene) => SendCheck(LocationMap.ClearId(scene));

    /// <summary>Send the Crown check for a scene (all challenges mastered).</summary>
    public void SendCrown(string scene) => SendCheck(LocationMap.CrownId(scene));

    /// <summary>Tell the server this slot reached its goal (campaign complete).</summary>
    public void SendVictory()
    {
        if (Session == null) return;
        Session.Socket.SendPacket(new StatusUpdatePacket { Status = ArchipelagoClientState.ClientGoal });
        Plugin.Log.LogInfo("AP goal reported.");
    }

    // --- Item receipt --------------------------------------------------------

    private void OnItemReceived(ReceivedItemsHelper helper)
    {
        // Replayed history guard: skip anything already applied.
        ItemInfo item = helper.DequeueItem();
        if (helper.Index <= Data.ItemIndex) return;
        Data.ItemIndex = helper.Index;

        // Apply on the main thread.
        _mainThread.Enqueue(() => ItemApplier.Apply(item));
    }
}
