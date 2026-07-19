using Archipelago.MultiClient.Net.BounceFeatures.DeathLink;

namespace WtgArchipelago;

/// <summary>
/// Thin wrapper over the AP DeathLink service. Decide what a "death" means in a
/// golf game -- e.g. ball out of bounds / in water / a failed challenge -- then
/// call SendDeath() when it happens, and handle incoming deaths in the queued
/// callback (kill/reset the player on the main thread).
/// </summary>
public class DeathLinkHandler
{
    private readonly ArchipelagoClient _client;
    private readonly DeathLinkService _service;
    public bool Pending { get; private set; }   // set by a received death; consumed on main thread

    public DeathLinkHandler(ArchipelagoClient client, bool enabled)
    {
        _client = client;
        _service = client.Session.CreateDeathLinkService();
        if (enabled) _service.EnableDeathLink();

        _service.OnDeathLinkReceived += _ => Pending = true;   // handle in GamePatches/Tick
    }

    public void SendDeath(string cause = "fell in a bunker")
    {
        _service.SendDeathLink(new DeathLink(_client.Data.SlotName, cause));
    }

    public bool ConsumePending()
    {
        if (!Pending) return false;
        Pending = false;
        return true;
    }
}
