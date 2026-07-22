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
    public enum ConnState { Disconnected, Connecting, Connected, Failed }

    public ArchipelagoData Data { get; } = new();
    public ArchipelagoSession Session { get; private set; }
    public DeathLinkHandler DeathLink { get; private set; }

    /// <summary>Current connection state (drives the UI + the passive-until-connected gate).</summary>
    public ConnState State { get; private set; } = ConnState.Disconnected;
    /// <summary>Human-readable status for the connection UI.</summary>
    public string StatusMessage { get; private set; } = "Not connected";
    public bool Connected => State == ConnState.Connected;

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
        if (State == ConnState.Connecting || State == ConnState.Connected)
        {
            Plugin.Log.LogWarning("AP already connected/connecting — disconnect first.");
            return;
        }
        Data.Host = host; Data.Port = port; Data.SlotName = slot;
        Data.Password = string.IsNullOrEmpty(password) ? null : password;

        State = ConnState.Connecting;
        StatusMessage = $"Connecting to {host}:{port} as {slot}...";

        // Connect off-thread so we never block Unity's main loop.
        ThreadPool.QueueUserWorkItem(_ => ConnectImpl());
    }

    /// <summary>Drop the AP session and return the mod to passive/vanilla behaviour.</summary>
    public void Disconnect()
    {
        try
        {
            if (Session != null)
                try { Session.Socket.DisconnectAsync(); } catch { }
        }
        finally
        {
            State = ConnState.Disconnected;
            StatusMessage = "Disconnected";
            // Forget what GoalWatcher has sent so a reconnect re-reconciles node state.
            Mapping.GoalWatcher.Reset();
            Mapping.PercentGate.Reset();
            Plugin.Log.LogInfo("AP disconnected — mod is passive (vanilla) until you reconnect.");
        }
    }

    private void ConnectImpl()
    {
        try
        {
            Session = ArchipelagoSessionFactory.CreateSession(Data.Host, Data.Port);

            // Wire events BEFORE logging in.
            Session.Items.ItemReceived += OnItemReceived;
            Session.MessageLog.OnMessageReceived += m =>
            {
                Plugin.Log.LogInfo(m.ToString());
                MessageFeed.Ingest(m);   // mirror the AP client's feed on-screen
                ConsoleUI.Ingest(m);     // full scrollback for the in-game console
            };
            Session.Socket.ErrorReceived += (e, msg) => Plugin.Log.LogError($"AP socket: {msg}");
            Session.Socket.SocketClosed += reason =>
            {
                State = ConnState.Disconnected;
                StatusMessage = $"Disconnected: {reason}";
                Plugin.Log.LogWarning($"AP closed: {reason}");
            };

            LoginResult result = Session.TryConnectAndLogin(
                Plugin.GameName,
                Data.SlotName,
                ItemsHandlingFlags.AllItems,
                new Version(0, 6, 7),
                password: Data.Password,
                requestSlotData: true);

            if (result is LoginFailure failure)
            {
                string errs = string.Join("; ", failure.Errors);
                State = ConnState.Failed;
                StatusMessage = "Login failed: " + errs;
                Plugin.Log.LogError("AP login failed: " + errs);
                return;
            }

            var success = (LoginSuccessful)result;
            ReadSlotData(success.SlotData);

            // Re-count bosses already beaten in a prior session (all_bosses goal),
            // on the main thread. A boss's Clear check = its defeat.
            var checkedIds = new System.Collections.Generic.List<long>(
                Session.Locations.AllLocationsChecked);
            _mainThread.Enqueue(() =>
            {
                try { BossGoal.Reconcile(checkedIds); }
                catch (Exception e) { Plugin.Log.LogError($"BossGoal.Reconcile: {e}"); }
            });

            DeathLink = new DeathLinkHandler(this, Data.DeathLinkEnabled);
            State = ConnState.Connected;
            StatusMessage = $"Connected as {Data.SlotName}";
            Plugin.Log.LogInfo($"AP connected as {Data.SlotName}.");
        }
        catch (Exception e)
        {
            State = ConnState.Failed;
            StatusMessage = "Connect error: " + e.Message;
            Plugin.Log.LogError($"AP connect error: {e}");
        }
    }

    private void ReadSlotData(System.Collections.Generic.Dictionary<string, object> slotData)
    {
        if (slotData == null) return;
        if (slotData.TryGetValue("death_link", out var dl)) Data.DeathLinkEnabled = Convert.ToBoolean(dl);
        if (slotData.TryGetValue("death_link_amnesty", out var dla)) Data.DeathLinkAmnesty = Convert.ToInt32(dla);
        if (slotData.TryGetValue("goal", out var g)) Data.Goal = Convert.ToInt32(g);
        BossGoal.SetEnabled(Data.Goal == ArchipelagoData.GoalAllBosses);
        PercentGate.SetGoal(Data.Goal);   // arm the % completion-door goal (door_50/75/100)
        if (slotData.TryGetValue("boss_keys", out var bk)) Data.BossKeysEnabled = Convert.ToBoolean(bk);
        BossGate.SetEnabled(Data.BossKeysEnabled);
        if (slotData.TryGetValue("hard_sections", out var hs)) Data.HardSectionsEnabled = Convert.ToBoolean(hs);
        SectionGate.SetEnabled(Data.HardSectionsEnabled);
        if (slotData.TryGetValue("crowns", out var cr)) Data.CrownsEnabled = Convert.ToBoolean(cr);
        ChestGate.SetEnabled(Data.CrownsEnabled);
        // Flag target for the door_* goals (0 otherwise) -> drives the Flag HUD.
        Data.FlagGoal = slotData.TryGetValue("flag_goal", out var fg) ? Convert.ToInt32(fg) : 0;
    }

    // --- Location checks -----------------------------------------------------

    /// <summary>Report an AP location as checked (safe to call from any thread).</summary>
    public void SendCheck(long locationId)
    {
        // Gate on Connected, NOT just a non-null Session: after a socket close the
        // Session object lingers but its socket is dead. Sending into a dead socket
        // can BLOCK the calling thread -- and this is called from a game postfix on
        // Unity's main thread, so that would freeze the game. (Dedup on this thread.)
        if (Session == null || !Connected || locationId < 0) return;
        if (!Data.CheckedLocations.Add(locationId)) return;   // already sent

        // Do the actual network send OFF the main thread so a slow/dead socket can
        // never stall the game. MultiClient.Net's send path is thread-safe.
        var session = Session;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                session.Locations.CompleteLocationChecks(locationId);
                Plugin.Log.LogInfo($"AP check sent: {locationId}");
            }
            catch (Exception e) { Plugin.Log.LogError($"AP check {locationId} not sent: {e.Message}"); }
        });
    }

    /// <summary>Resolve a level's scene name to its Clear location and send it.</summary>
    public void SendClear(string scene) => SendCheck(LocationMap.ClearId(scene));

    /// <summary>Send the Crown check for a scene (all challenges mastered).</summary>
    public void SendCrown(string scene) => SendCheck(LocationMap.CrownId(scene));

    /// <summary>Send a chat line / server command (`!hint`, `!countdown`, `!admin ...`)
    /// to the server -- the input side of the in-game console (see ConsoleUI). The
    /// server interprets `!`-prefixed text; responses come back through MessageLog.
    /// Sent off the main thread so a dead socket can't stall the game.</summary>
    public void Say(string text)
    {
        if (Session == null || !Connected || string.IsNullOrWhiteSpace(text)) return;
        var session = Session;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try { session.Say(text); }
            catch (Exception e) { Plugin.Log.LogError($"AP say failed: {e.Message}"); }
        });
    }

    /// <summary>Tell the server this slot reached its goal (campaign complete).</summary>
    public void SendVictory()
    {
        if (Session == null || !Connected) return;
        var session = Session;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                session.Socket.SendPacket(new StatusUpdatePacket { Status = ArchipelagoClientState.ClientGoal });
                Plugin.Log.LogInfo("AP goal reported.");
            }
            catch (Exception e) { Plugin.Log.LogError($"AP victory not sent: {e.Message}"); }
        });
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
