using System;
using System.Threading;
using Archipelago.MultiClient.Net.BounceFeatures.DeathLink;

namespace WtgArchipelago;

/// <summary>
/// DeathLink for WHAT THE GOLF?. "Death" here = a level FAILURE (ball out of
/// bounds / in water / lost), which the game reports via GameAnalytics.OnLevelReset
/// -- NOT a manual restart (OnLevelManualReset) or quit (OnLevelAbort), which are
/// deliberately excluded.
///
/// Because wiping is CONSTANT in this game, outgoing deaths use a count-based
/// throttle: we broadcast one DeathLink only every Nth local wipe (N = the apworld's
/// death_link_amnesty option, 1..30, delivered via slot data). A wipe caused by an
/// INCOMING DeathLink is suppressed from the count (see BeginInducedDeath) so received
/// deaths can never feed back into our own broadcast -- no ping-pong loops.
///
/// Incoming deaths are consumed on the main thread by Mod.OnUpdate: if we're in a
/// hole we restart it; in the overworld there's nothing to kill, so we drop it.
/// </summary>
public class DeathLinkHandler
{
    private readonly ArchipelagoClient _client;
    private readonly DeathLinkService _service;
    private bool _enabled;               // toggleable at runtime via SetEnabled (F8 panel)
    private readonly int _threshold;

    private int _wipeCount;                 // local wipes since the last broadcast
    private int _suppressResetsUntilFrame;  // ignore OnLevelReset while <= this frame

    public bool Pending { get; private set; }   // set by a received death; consumed on main thread

    // Read-only view for the on-screen HUD (see ConnectionUI.DrawHud).
    public bool Enabled => _enabled;
    public int Threshold => _threshold;
    public int WipeCount => _wipeCount;   // local wipes since the last broadcast; loops 0..Threshold

    public DeathLinkHandler(ArchipelagoClient client, bool enabled)
    {
        _client = client;
        _enabled = enabled;
        // Threshold = the apworld's death_link_amnesty option (slot data, 1..30),
        // read into ArchipelagoData before this handler is constructed on connect.
        _threshold = client.Data.DeathLinkAmnesty;

        _service = client.Session.CreateDeathLinkService();
        if (enabled) _service.EnableDeathLink();

        // Runs on the network receive thread -> just flag; Mod.OnUpdate consumes it.
        _service.OnDeathLinkReceived += _ => Pending = true;

        Plugin.Log.LogInfo(enabled
            ? $"DeathLink ON (1 death broadcast per {_threshold} wipes)"
            : "DeathLink off (slot data)");
    }

    /// <summary>A local level failure (auto-reset). Counts toward the throttle and
    /// broadcasts once the threshold is reached -- unless we're currently applying an
    /// incoming death (loop suppression) or outgoing is disabled.</summary>
    public void OnLocalWipe()
    {
        if (!_enabled || _threshold <= 0) return;

        // Ignore the reset that our own incoming-death handling induces.
        if (UnityEngine.Time.frameCount <= _suppressResetsUntilFrame)
            return;

        _wipeCount++;
        if (_wipeCount < _threshold) return;

        int total = _wipeCount;
        _wipeCount = 0;
        SendDeath($"{_client.Data.SlotName} wiped {total} times and dragged you down with them");
    }

    /// <summary>Suppress wipe-counting for a short window, so the level reset we
    /// trigger in response to an INCOMING death is not re-broadcast. Call right
    /// before restarting the hole on the main thread.</summary>
    public void BeginInducedDeath()
    {
        // A few frames covers the reset that Restart() fires synchronously plus any
        // one-frame-later analytics callback.
        _suppressResetsUntilFrame = UnityEngine.Time.frameCount + 30;
    }

    public void SendDeath(string cause)
    {
        if (!_enabled || _client == null || !_client.Connected) return;

        // Send off the main thread (a dead socket can block the caller -- see
        // ArchipelagoClient.SendCheck). MultiClient.Net's send path is thread-safe.
        var service = _service;
        var slot = _client.Data.SlotName;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                service.SendDeathLink(new DeathLink(slot, cause));
                Plugin.Log.LogInfo($"DeathLink sent: {cause}");
            }
            catch (Exception e) { Plugin.Log.LogError($"DeathLink not sent: {e.Message}"); }
        });
    }

    public bool ConsumePending()
    {
        if (!Pending) return false;
        Pending = false;
        return true;
    }

    /// <summary>Enable/disable DeathLink for this client at runtime (F8 panel). Adds
    /// or removes the "DeathLink" tag via the AP service so incoming deaths stop/start
    /// and our wipes stop/start broadcasting. Per-session: a reconnect re-applies the
    /// seed's death_link value. No-op if unchanged.</summary>
    public void SetEnabled(bool on)
    {
        if (on == _enabled) return;
        _enabled = on;
        try
        {
            if (on) _service.EnableDeathLink();
            else _service.DisableDeathLink();
            Plugin.Log.LogInfo($"DeathLink {(on ? "enabled" : "disabled")} (runtime toggle)");
        }
        catch (Exception e) { Plugin.Log.LogError($"DeathLink toggle failed: {e.Message}"); }
    }
}
