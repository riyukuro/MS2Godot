using System;
using System.Collections.Generic;
using Godot;
using Ms2.Net.Packets;
using Ms2.Ui.Skills;

namespace Ms2Godot.Ui;

// The skill system's compact auto-cast (CompactCast): a compact control tab's key held casts the tab's skills through the own
// cast path (MyPC::vfunc_104's first ask, mypc.cpp:1682).
public partial class HudScene
{
    readonly CompactCast compact = new();
    IReadOnlyList<SkillMacroTab> compactFed;           // the configs last given to SetCompactConfigs
    SlotWorld compactWorld;                            // the pick's world, taken again when the book, job or field changes
    SkillBook compactWorldBook;
    int compactWorldSubJob;
    int? compactWorldField;
    WeaponAction compactHeld;
    Func<long, bool> compactActionDown;
    Action<int> compactNotice;
    uint compactRefused;                               // the pick the idle poll's request refused, until the pick changes

    bool CompactOn => hudModel?.IsFeatureEnabled(0x10) == true;   // CompactControl

    /// <summary>CKeyTable::IsActionKeyDown (keytable.cpp:1037) over the physical keys (IsKeyDownGated: not while typing).</summary>
    bool CompactActionDown(long action)
    {
        pollKeyDown ??= CastKeyDown;
        return hudModel?.Keys is { } keys && keys.IsActionKeyDown(action, pollKeyDown);
    }

    /// <summary>The pick's world (<see cref="QuickSlotWorld"/>) kept while the live book, the sub job and the field stay, with
    /// the level, hp, sp, ep and meso read again each call (the cast check, FUN_14176a380). 0 B while kept.</summary>
    SlotWorld CompactWorld()
    {
        var live = Net.LiveSession.Instance;
        var book = live?.SkillBook;
        int subJob = hudModel.Player.SubJob;
        int? field = live?.FieldId;
        if (compactWorld == null || (object)compactWorldBook != book || compactWorldSubJob != subJob || compactWorldField != field)
        {
            compactWorld = QuickSlotWorld();
            compactWorldBook = book; compactWorldSubJob = subJob; compactWorldField = field;
            compactHeld = compactWorld.Weapon is { } h ? weaponActions.FindAction(h.Right, h.Left) : null;
            return compactWorld;
        }
        var p = hudModel.Player;
        compactWorld.Level = p.Level; compactWorld.Hp = p.HpCur; compactWorld.Sp = p.SpCur; compactWorld.Ep = p.EpCur;
        compactWorld.Meso = p.Mesos;
        return compactWorld;
    }

    /// <summary>FUN_1417674a0(<paramref name="any"/>) (skillslot.cpp:2446-2508): the compact tab's skill to cast now, 0 for none.
    /// <paramref name="castAllowed"/> is m_nCastAllowed (1 inside the own skill state's poll and hold check, 0 outside).
    /// The refusal notice (FUN_141769440) is logged (STILL-OPEN: its text). 0 B.</summary>
    uint CompactPick(bool any, bool castAllowed)
    {
        if (compact.Tab == null || hudModel == null) return 0;
        compactActionDown ??= CompactActionDown;
        compactNotice ??= why => GD.Print($"compact: the held tab's skill refused, reason {why}");
        return compact.Pick(any, castAllowed, CompactOn, compactActionDown, CompactWorld(), (uint)Time.GetTicksMsec(), compactNotice);
    }

    /// <summary>
    /// Each frame: the configs (SetCompactConfigs @0x141767d90, skillslot.cpp:2551-2556: the live session's SkillMacro mode 2
    /// or the compact window's close) when they changed, then CSkillSystem::Update's FUN_141766ec0 (:2339, :2384-2442) and
    /// State::vfunc_8 @0x1417b7fb0 (state.cpp:634-639) outside the skill state: vfunc_104(0, 0, 0), whose first ask is the
    /// compact pick (mypc.cpp:1682), then vfunc_102 (<see cref="RouteOwn"/>). Without bAuto the key pick takes only a key
    /// just pressed, which comes as an event here (<see cref="CastKeys"/>), so only the compact pick is polled. 0 B/frame
    /// while no tab is held.
    /// vfunc_104's refusals (:1671-1676): cube mode holds the pick (<see cref="CubeModeHoldsPick"/>); the instrument state (0x2f)
    /// and the character list scene (0x620000d, no HUD) are not ported. STILL-OPEN: the idle poll runs in the idle, walk, jump and fall states
    /// (the states without their own vfunc_8 here).
    /// </summary>
    void TickCompact()
    {
        if (hudModel == null) return;
        var live = Net.LiveSession.Instance;
        if (live != null && (object)live.CompactConfigs != compactFed) FeedCompactConfigs(live.CompactConfigs, false);   // mode 2
        if (!CompactOn || compactFed == null || compactFed.Count == 0) return;
        compactActionDown ??= CompactActionDown;
        var w = CompactWorld();
        compact.Update(false, true, compactActionDown, w.Jobs?.JobGroupId ?? 0, w, hudModel.Tables.SkillCompactControl);
        if (OwnSkillStateRuns() || player == null || CubeModeHoldsPick()) return;   // mypc.cpp:1673: before the compact pick
        if (player.State is not (Player.MoveState.Idle or Player.MoveState.Walk or Player.MoveState.Jump or Player.MoveState.Fall)) return;
        uint id = CompactPick(false, false);
        if (id == 0 || id != compactRefused) compactRefused = 0;
        if (id == 0 || id == compactRefused) return;
        if (RequestOwn(w, id, compactHeld, 0) is { } r) RouteOwn(r, false);   // the per-frame poll: a held key re-press is dropped silently, as the client does (FUN_1417eed70)
        else compactRefused = id;
    }

    /// <summary>New tabs: with <paramref name="force"/> SetCompactConfigs @0x141767d90 (skillslot.cpp:2551-2556, the window's
    /// close: FUN_141766ec0 with force follows), without the SkillMacro mode 2 read (FUN_141767ae0, :2530-2547: only the
    /// tabs change).</summary>
    void FeedCompactConfigs(IReadOnlyList<SkillMacroTab> tabs, bool force)
    {
        compactFed = tabs;
        compactActionDown ??= CompactActionDown;
        var w = CompactWorld();
        compact.SetConfigs(tabs, force, CompactOn, compactActionDown, w.Jobs?.JobGroupId ?? 0, w, hudModel.Tables.SkillCompactControl);
    }

    /// <summary>FUN_141767990 (skillslot.cpp:2512-2526) after a cast went: one chain entry of its origin id is used.</summary>
    void CompactUsed(CastRecord r) => compact.Used(CompactOn, r.Skill, r.Level.Level);

    /// <summary>The compact pick as a press while the own skill state runs (vfunc_104's first ask in FUN_1417eeb10's poll,
    /// stateskill.cpp:638-639, and FUN_1417ee120's end poll): the record with key 0, null for none. A pick already the state's
    /// next cast, or refused in the same window, is not asked again (0 B while held).</summary>
    CastRecord CompactPress(in SkillFollow.Window window, out bool picked)
    {
        picked = false;
        if (CubeModeHoldsPick()) return null;                // mypc.cpp:1673: before the compact pick
        uint id = CompactPick(false, true);
        if (id == 0) return null;
        picked = true;
        var w = CompactWorld();
        var s = w.Tables.GetSkillData(id);
        if (s == null) return null;
        if (ownNext != null && ownNext.Key == 0 && (object)ownNext.Skill == s) return null;
        if (ownQueued != null && ownQueued.Key == 0 && (object)ownQueued.Skill == s) return ownQueued;
        if (PollRefused(0, s, window)) return null;
        var r = RequestOwn(w, id, compactHeld, 0);
        if (r == null) RefusePoll(0, s, window);
        return r;
    }
}
