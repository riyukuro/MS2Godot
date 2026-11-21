using System;
using System.Collections.Generic;
using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using Ms2.Ui;
using Ms2.Ui.Hud;

namespace Ms2Godot.Net;

/// <summary>
/// The field's reactors: CSceneStateGame::OnReactor (scenestategame.cpp:6699-6770), CReactor (reactor.cpp) and ReactorAction
/// (key 0x1a, actionkeyobject.cpp:2861-2995), with the game rule's reactor context (GameEventManagerCli).
/// </summary>
public partial class LiveSession
{
    /// <summary>The game rule's reactor context, filled from GameEvent and cleared with the game scene.</summary>
    public readonly ReactorRule ReactorRules = new();
    /// <summary>OnReactor mode 4, FUN_141abf6e0(itemId, grade): the reward toast.</summary>
    public Action<int, int> ReactorRewardSink;
    /// <summary>A reactor was added (or replaced) or removed: the field's reactor layer follows.</summary>
    public event Action<FieldReactors.Reactor> ReactorAdded, ReactorRemoved;

    FieldReactors reactors;
    bool reactorTableTried;
    // ReactorAction::m_nReactorId: the polled reactor's object id, 0 when none
    int polledReactor;
    ReactorUser reactorUser;

    /// <summary>The field's reactors; null when the reactor tables do not load.</summary>
    public FieldReactors Reactors
    {
        get
        {
            if (reactors != null || reactorTableTried) return reactors;
            reactorTableTried = true;
            try { reactors = new FieldReactors(new ReactorTable(PackSet.Shared(Settings.DataDir))); }
            catch (Exception e) { GD.PushWarning($"live: no reactor table ({e.Message})"); }
            return reactors;
        }
    }

    /// <summary>The own player for the reactor checks (CReactor::CanUse @0x140b79c30).</summary>
    sealed class ReactorUser : IReactorUser
    {
        readonly LiveSession s;
        public ReactorUser(LiveSession s) => this.s = s;
        /// <summary>FUN_140b7a5e0 is 0: the player's state is 1. STILL-OPEN: FUN_141675ed0 and the sub scene check.</summary>
        public bool CanReact => s.InteractPlayerStateId() == 1;
        public long CharacterId => s.CharacterId;
        public int ItemCount(int code, int rank)
        {
            int n = 0;
            if (s.Items.All is Dictionary<long, EquipItem>.ValueCollection items)
                foreach (var it in items)
                    if (it.Id == code && it.Rarity == rank) n += it.Amount;
            return n;
        }
        public int ItemRank(int code) => s.ItemRank?.Invoke(code) ?? 1;
    }

    /// <summary>CSceneStateGame::OnReactor @0x14173c870.</summary>
    void OnReactor(ReactorMessage m)
    {
        var all = Reactors;
        switch (m.Mode)
        {
            case 0:
                if (all == null) return;
                var r = all.Add(m.Add, out var replaced);
                if (replaced != null) ReactorRemoved?.Invoke(replaced);
                if (r == null) { GD.Print($"live: reactor {m.Add.ObjectId} '{m.Add.Name}' id {m.Add.ReactorId} has no data or state {m.Add.State.StateIndex}"); return; }
                ReactorAdded?.Invoke(r);
                break;
            case 1:
                if (all?.Remove(m.ObjectId) is { } gone)
                {
                    if (polledReactor == gone.ObjectId) polledReactor = 0;
                    ReactorRemoved?.Invoke(gone);
                }
                break;
            case 3:
                all?.Update(m.ObjectId, m.State);
                break;
            case 4:
                foreach (var (itemId, grade) in m.Rewards) ReactorRewardSink?.Invoke(itemId, grade);
                break;
            case 5:
                GD.Print($"live: STILL-OPEN reactor {m.ReactorId}: the own PC's StateReact create animation (vfunc_59(0x3d, ..., 0x9e, 1))");
                break;
        }
    }

    /// <summary>The field's reactors go with the field (the world's objects are freed on its unload).</summary>
    void NewFieldReactors()
    {
        polledReactor = 0;
        reactors?.Clear();
    }

    /// <summary>ReactorAction::vfunc_1 @0x14095f130 with FUN_14095fcb0: the nearest usable reactor from
    /// <paramref name="at"/> (the ridee-aware point). STILL-OPEN: the highlight (CReactor::FUN_140b78b40) and vfunc_13.</summary>
    public bool PollReactor(Vec3 at)
    {
        polledReactor = 0;
        if (selfObject < 0 || Reactors is not { } all) return false;
        reactorUser ??= new ReactorUser(this);
        var r = all.Nearest(new System.Numerics.Vector3((float)at.X, (float)at.Y, (float)at.Z), reactorUser);
        if (r == null) return false;
        polledReactor = r.ObjectId;
        return true;
    }

    /// <summary>ReactorAction::vfunc_2 @0x14095f2f0: the polled reactor's game event check, then the cast, the ride-off or
    /// the StateReact send.</summary>
    public void UseReactor()
    {
        if (polledReactor == 0 || reactors?.Get(polledReactor) is not { } r || selfObject < 0) return;
        int check = FieldReactors.CheckGameEvent(r.Data, ReactorRules.Current, (uint)(FieldId ?? 0));
        bool riding = player != null && IsInstanceValid(player) && player.Mount != null;
        switch (FieldReactors.Use(r.Data, check, InteractPlayerStateId(), riding))
        {
            case FieldReactors.UseOutcome.FailedRewardCount:
                SystemNoticeSink?.Invoke(MessageBoxData.CommonByKey("s_reactor_action_failed_reward_count"));
                break;
            case FieldReactors.UseOutcome.Failed:
                SystemNoticeSink?.Invoke(MessageBoxData.CommonByKey("s_reactor_action_failed"));
                break;
            case FieldReactors.UseOutcome.Cast:
                GD.Print($"live: STILL-OPEN reactor {r.ObjectId}: the react cast (vfunc_59(0x3d, StateReactData({r.Data.Id}, '{r.Name}'), 0x9f, 0))");
                break;
            case FieldReactors.UseOutcome.RideOff:
                GD.Print($"live: STILL-OPEN reactor {r.ObjectId}: the ride off first (RideOffActionReact, SendRequestRideMode1)");
                break;
            case FieldReactors.UseOutcome.Send:
                // FUN_14152e0c0 (STILL-OPEN), then CReactor::SendStateReact
                Send(new StateReactRequest(r.ObjectId));
                break;
        }
    }
}
