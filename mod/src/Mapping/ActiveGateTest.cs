using System;
using System.Collections.Generic;

namespace WtgArchipelago.Mapping;

/// <summary>
/// Within-chamber hard-lock experiment (spike), take 2. The leak: connector
/// buttons (OverworldButton2D) open on ball-contact because canOpen=true, letting
/// you walk into un-keyed sibling sub-areas. Each connector's OverworldID.ID is
/// its section's unlockTriggerId (door_space_00, VJ69W, ...).
///
/// So every frame we force canOpen=FALSE on every connector whose id is a section
/// trigger the AP seed has NOT unlocked. On a fresh save (doors start closed) they
/// should then refuse to open on touch. Test: fresh save, one section keyed, try to
/// walk into a sibling -> its door should no longer open. Toggle Mod.ActiveGateTest.
/// </summary>
public static class ActiveGateTest
{
    private static HashSet<string> _triggers;
    private static readonly HashSet<string> _loggedBlock = new();

    public static void Tick()
    {
        try
        {
            _triggers ??= ChamberUnlock.AllTriggers();
            if (_triggers.Count == 0) return;

            var btns = UnityEngine.Resources.FindObjectsOfTypeAll<Il2Cpp.OverworldButton2D>();
            for (int i = 0; i < btns.Length; i++)
            {
                var b = btns[i];
                if (b == null) continue;

                string id = null;
                try { var oid = b.gameObject.GetComponent<Il2Cpp.OverworldID>(); if (oid != null) id = oid.ID; }
                catch { }
                if (string.IsNullOrEmpty(id)) continue;

                if (!_triggers.Contains(id)) continue;   // not a section connector

                if (ChamberUnlock.IsTriggerUnlocked(id))
                {
                    // Keyed section: make sure its connector CAN open (restore, in
                    // case we forced it shut earlier while it was locked).
                    if (!b.canOpen)
                    {
                        b.canOpen = true;
                        Plugin.Log.LogInfo($"[ACTIVEGATE] restored connector '{id}' (canOpen=true — section unlocked)");
                    }
                }
                else if (b.canOpen)
                {
                    // Locked section: hold its connector shut.
                    b.canOpen = false;
                    if (_loggedBlock.Add(id))
                        Plugin.Log.LogInfo($"[ACTIVEGATE] holding connector '{id}' shut (canOpen=false — section locked)");
                }
            }
        }
        catch (Exception e) { Plugin.Log.LogError($"ActiveGateTest: {e}"); }
    }
}
