using Godot;
using Ms2.Net.Packets;
using Ms2.Net.Replay;

namespace Ms2Godot.Net;

/// <summary>
/// The other players' mounts: PCBase::vfunc_120 / vfunc_121 (pcbase.cpp:3289-3420) of a remote PC. The mount is a PC body
/// of its own (<see cref="RemotePc.CreateRidee"/>) that replays the PC's RideSync records (CSceneStateGame::OnRideSync,
/// scenestategame.cpp:2472: the PC's ridee, nothing without one) through its own states; the rider sits in StateRide on its
/// seat and plays StateRide::vfunc_9's sequence by the mount's state (<see cref="StateRideAnim"/>).
/// The ride-on and ride-off effects (CommonResource +0x2b0 / +0x2b8) and an UnRideSkill ride off's rideOffBySkillEffect play
/// at the PC (<see cref="PlayRideEffect"/>); a hideRider row hides the rider (<see cref="RemotePc.SetHideReason"/>).
/// The ridee takes the rider's stats and their changes (<see cref="SetStat(Remote, int, int, long)"/>).
/// The ridee has its own simple shadow (the row's &lt;shadow scale&gt;); in StateRide the rider's is off.
/// The ride off's StateFallData (the ridee's fall start) only reaches MyPC::FUN_141676060 (statefall.cpp:104-106), so a
/// remote PC's fall ignores it.
/// A remote ridee never sets m_unk_b80 (the RunX SP idle): only Ridee::UpdateRunEffects sets it, from StateRide::vfunc_8
/// (statemanager.cpp:1108-1116), and State::vfunc_8 runs only from MyPC's update (mypc.cpp:2966) and the own ridee's
/// (ridee.cpp:712).
/// The mount's step keys play through <see cref="RemotePc.TextKeys"/> from <see cref="Mount.TextKeys"/>. Mode 6 also writes
/// the kept ride-on action's m_b28, which only a later PCBase::vfunc_120 of that action reads; a remote PC's ride is re-sent
/// with it.
/// The ride off gives the rider the ridee's touched entity (pcbase.cpp:3364-3366, <see cref="RemotePc.TakeHitReport"/>).
/// STILL-OPEN: the rest of the ride off's field object anchor (pcbase.cpp:3367-3376). When that entity is a field object's
/// marker (WorldService::FindMarkerByEntity), the rider goes to the marker's top + 10 if the ridee stood on the ground, and
/// takes the ridee's moving platform anchor (m_unk_ae0 / m_posAE8). It needs the field's breakable markers to have
/// colliders that map back to their entity, and the remote platform carry (see <see cref="RemotePc"/>).
/// </summary>
public sealed partial class RemoteLayer
{
    /// <summary>OnResponseRide (scenestategame.cpp:12026) for another player's PC: modes 0-2 (<see cref="Ms2.Ui.OwnRide.ForActor"/>),
    /// the owner's passenger map of modes 3-5 (<see cref="Ms2.Ui.RidePassengers"/>) and mode 6's PC::FUN_1416642c0 (m_b1224).</summary>
    public bool OnRide(ResponseRide r)
    {
        if (r == null || !users.TryGetValue(r.ObjectId, out var u)) return false;
        if (Ms2.Ui.RidePassengers.Apply(r, u.Passengers)) return true;
        if (r.Mode == 6) { u.NoBoarding = r.Flag; return true; }   // and GetRideOnAction()->m_b28 (:12119-12122)
        switch (Ms2.Ui.OwnRide.ForActor(r, u.Ridee != null))
        {
            case Ms2.Ui.OwnRide.Step.RideOn: RideOn(u, r.RideOn); break;
            case Ms2.Ui.OwnRide.Step.RideOff: RideOff(u, r.RideOff); break;
        }
        return true;
    }

    /// <summary>ResponseRide mode 3 (scenestategame.cpp:12076-12082): the owner's ridee's GetData()->GetPassenger(seat)
    /// m_strRideBone, "" without a ridee or that seat.</summary>
    public string SeatBone(int ownerId, int seat) =>
        users.TryGetValue(ownerId, out var u) ? u.Ridee?.Mount?.Data.Passenger(seat)?.RideBone ?? "" : "";

    /// <summary>
    /// PCBase::vfunc_120 (@0x141676b90): nothing for a null action or with a ridee set; Ridee::FUN_1417043a0 loads the row
    /// (an unknown riding id makes no ridee); the ridee takes the rider's position and direction, Ridee::vfunc_10 raises it by
    /// 25 and asks the rider's vfunc_59(0x21), which PC::vfunc_124 refuses for a dead or 0-health pc (the ridee is made
    /// all the same).
    /// </summary>
    void RideOn(Remote r, RideOnAction a)
    {
        if (a == null || r.Ridee != null) return;
        Mount m;
        try { m = Mount.Load(packs, builder, a.RideId); }
        catch (Exception ex) { GD.PushWarning($"live: riding {a.RideId} of {r.Name}: {ex.Message}"); return; }
        var ridee = RemotePc.CreateRidee($"Ridee{r.ObjectId}", m, gravity, r.Pc.Body.Position + Vector3.Up * 25f, r.Pc.RotationZ, faceGrids);
        AddChild(ridee.Body);
        // Ridee::vfunc_49 (ridee.cpp:848-858): its own CObjectShadow of the PC row, the row's shadow scale, lifted 1
        FieldShadows.Character(ridee.Body, m.Visual, Ms2.Formats.Options.GraphicOptionTable.PC, m.Data.ShadowScale, 1f);
        ridee.Spawn();
        // the rider's stats, every kind and id (pcbase.cpp:3317-3320): the ridee's move-speed rate reads its mount speed
        for (int k = 0; k < 3; k++)
            for (int s = 0; s < MoveSpeedRate.StatCount; s++) ridee.SetStat(k, s, r.Pc.Stat(k, s));
        // Ridee::GetRunSpeed's and FUN_1417075e0's rider (PC::GetEquipment, FUN_1416749a0)
        ridee.RiderAbilityRate = type => RiderAbilityRate(r, type);
        ridee.RiderEventBuff = type => r.Buffs?.HasEnabledWithEventBuffType(type) == true;
        r.Ridee = ridee;
        r.Pc.Ridee = ridee;
        r.Pc.States.RideOn(ridee.States, r.HpCur, r.HpMax);
        PlayRideEffect(r, CommonPath(ref rideOnPath, LiveSession.RideOnEffectKey));   // pcbase.cpp:3324-3330
        if (m.Data.HideRider) r.Pc.SetHideReason(RemotePc.HideReasonRide, true);       // :3331-3333
        r.NoBoarding = a.Flag;            // PC::FUN_1416642c0(m_b28), pcbase.cpp:3341-3343
    }

    /// <summary>
    /// PCBase::vfunc_121 (@0x1416773f0): the rider takes the ridee's position and direction, the ridee is released, the
    /// jump stopped, then vfunc_59 of StateIdle when the ridee stood on the ground (FUN_141671a30) or StateFall, through
    /// PC::vfunc_124; then the ride-off effect, and for an UnRideSkill action (type 24) the row's rideOffBySkillEffect.
    /// </summary>
    void RideOff(Remote r, RideOffAction action)
    {
        var ridee = r.Ridee;
        if (ridee == null) return;
        string skillEffect = action?.Type == UnRideSkillType ? ridee.Mount?.Data?.RideOffBySkillEffect : null;
        if (ridee.Mount?.Data?.HideRider == true) r.Pc.SetHideReason(RemotePc.HideReasonRide, false);   // pcbase.cpp:3402-3404
        Dismount(r.Pc, r.Visual, ridee, r.HpCur, r.HpMax);
        FreeRidee(r);
        PlayRideEffect(r, CommonPath(ref rideOffPath, LiveSession.RideOffEffectKey));   // pcbase.cpp:3416-3422
        PlayRideEffect(r, skillEffect);                                                  // :3423-3429
    }

    /// <summary>
    /// CEquipment::GetAbilityRate (equipment.cpp:731-746) of another player: the worn items' part (kept like m_abilities,
    /// <see cref="Ms2.Ui.Items.WornEquipment.Abilities"/> of its level and worn items, usable at the server time), its
    /// buffs' and the hot time sum (with the PC bang events for a PC bang grade above 0), over 1000. The set items' term is
    /// 0, as for the own (see the STILL-OPEN at <see cref="Ms2.Ui.Items.WornEquipment.AbilityTotal(int, int, Ms2.Ui.Skills.AdditionalBuffList, Ms2.Ui.SpecialAbilityHotTimes, bool)"/>).
    /// </summary>
    float RiderAbilityRate(Remote r, int type)
    {
        if (r.WornAbilities == null)
        {
            var t = Ms2.Ui.GameTables.Shared(packs, Ms2Godot.GameOptions.StringLanguage);
            long now = ServerNow();
            r.WornAbilities = Ms2.Ui.Items.WornEquipment.Abilities(new Ms2.Net.Packets.PcInfo { Level = (ushort)r.Level, Equips = r.Equips },
                t.Items, t.ItemModels, it => Ms2.Ui.Items.ItemLevels.Usable(it, now)) ?? new Dictionary<int, int>();
        }
        int worn = r.WornAbilities.TryGetValue(type, out int v) ? v : 0;
        return Ms2.Ui.Items.WornEquipment.AbilityRate(Ms2.Ui.Items.WornEquipment.AbilityTotal(worn, type, r.Buffs, HotTimes, r.PcBangGrade > 0));
    }

    /// <summary>The game rule's special ability hot times (CGameRule::FUN_1404774a0); null adds none.</summary>
    public Ms2.Ui.SpecialAbilityHotTimes HotTimes;

    /// <summary>OnLevelUp (scenestategame.cpp:9511) for another player: its level, and its worn abilities again.</summary>
    /// <summary>OnLevelUp for another PC: vfunc_131(level). Returns its old level (vfunc_78), null when it is not here.</summary>
    public int? LevelUp(int objectId, int level)
    {
        if (!users.TryGetValue(objectId, out var r)) return null;
        int old = r.Level;
        r.Level = (short)level;
        r.WornAbilities = null;
        return old;
    }

    /// <summary>RideOffAction type 24, UnRideSkill.</summary>
    public const int UnRideSkillType = 24;

    string rideOnPath, rideOffPath;
    string CommonPath(ref string path, string key) =>
        path ??= Ms2.Ui.GameTables.Shared(packs, Ms2Godot.GameOptions.StringLanguage).CommonResource(key) ?? "";

    /// <summary>
    /// CEffectService::InsertEffect at a remote PC (the actor set, its object kind): played once at its feet and facing.
    /// PCBase::vfunc_120 / vfunc_121 test vfunc_25 first (GameObject m_unk_40, set by PC's hide option, pc.cpp:2735); the
    /// other players are always shown here, so it reads 0. STILL-OPEN: the effect does not follow the actor.
    /// </summary>
    void PlayRideEffect(Remote r, string path)
    {
        if (string.IsNullOrEmpty(path) || r.Pc?.Body == null) return;
        RideEffectsPlayed?.Invoke(r.ObjectId, path);
        combat.PlayEffect(path, r.Pc.Body.GlobalPosition, r.Visual?.Rotation.Y ?? 0, Ms2.Audio.SoundMusicEffectState.CasterOtherPc);   // PCBase 3 (pcbase.cpp:1092)
    }

    /// <summary>The riding row of a remote PC's ridee, null without one.</summary>
    /// <summary>The ridee another player rides, or null.</summary>
    public RemotePc RideeOf(int objectId) => users.TryGetValue(objectId, out var u) ? u.Ridee : null;

    /// <summary>
    /// PC::FUN_141660120's test of the own pc's holder (pc.cpp:2967-2988): FUN_141bd1200's actor of the id is a PC (class
    /// 0x6200003 / 0x6200004, another player here), it rides (GetRidee) and its buffs have an enabled effect of event buff
    /// type <paramref name="eventBuffType"/> (HasEnabledWithEventBuffType). All false for an id that is no player here.
    /// </summary>
    public (bool IsPc, bool Rides, bool EventBuff) HolderOf(int objectId, int eventBuffType) =>
        users.TryGetValue(objectId, out var u)
            ? (true, u.Ridee != null, u.Buffs?.HasEnabledWithEventBuffType(eventBuffType) == true)
            : (false, false, false);

    /// <summary>WorldService::FindNpcById's npc row: the field npc of the object id, null when none is here.</summary>
    public Ms2.Formats.World.NpcRow NpcRowOf(int objectId) => npcs.Placed(objectId)?.Row;

    public Ms2Godot.Movement.RideData RideeRow(int objectId) => users.TryGetValue(objectId, out var u) ? u.Ridee?.Mount?.Data : null;

    /// <summary>The move-speed rate (vfunc_96) of a remote PC, or of its ridee; null without that body.</summary>
    public float? MoveSpeedRateOf(int objectId, bool ridee) =>
        users.TryGetValue(objectId, out var u) ? (ridee ? u.Ridee : u.Pc)?.MoveSpeedRate : null;

    /// <summary>The object id and effect xml of each ride effect played at a remote PC (for the checks).</summary>
    public Action<int, string> RideEffectsPlayed;

    /// <summary>The rider's side of <see cref="RideOff"/>, before the ridee is freed; <paramref name="hp"/> and
    /// <paramref name="hpMax"/> are the rider's health for PC::vfunc_124.</summary>
    public static void Dismount(RemotePc rider, Node3D riderVisual, RemotePc ridee, long hp, long hpMax)
    {
        bool ground = ridee.OnGround;
        rider.Body.Position = ridee.Body.Position;
        rider.TakeHitReport(ridee);
        riderVisual.Transform = Transform3D.Identity;
        rider.RotationZ = ridee.RotationZ;
        rider.States.TargetYaw = ridee.RotationZ;
        rider.StopJump();
        rider.States.RideOff(ground, hp, hpMax);
    }

    /// <summary>PCBase::SetStat for a remote PC: FUN_1416708a0, FUN_1416705c0 and SetCurrentStat each go on to the ridee
    /// (m_spB10, pcbase.cpp:2369-2394), which holds the value to its own total, the same as the PC's.</summary>
    static void SetStat(Remote r, int kind, int stat, long value)
    {
        r.Pc.SetStat(kind, stat, value);
        r.Ridee?.SetStat(kind, stat, value);
    }

    static void FreeRidee(Remote r)
    {
        if (r.Ridee == null) return;
        r.Ridee.Free();
        r.Ridee = null;
        if (r.Pc != null) r.Pc.Ridee = null;
        r.Passengers.Clear();               // PCBase::vfunc_121's ClearPassengers (pcbase.cpp:3430)
    }

    /// <summary>
    /// Ridee::vfunc_53 (@0x1417079f0): the ridee's own frame, then the rider at its position and direction; RideeInterpolator
    /// puts the rider's model on the seat, and StateRide::vfunc_9 plays the rider's sequence at the ridee's AnimSpeed.
    /// </summary>
    static void TickRide(Remote r, float dt) => TickRide(r.Pc, r.Visual, r.Ridee, dt);

    public static void TickRide(RemotePc rider, Node3D riderVisual, RemotePc ridee, float dt)
    {
        ridee.Tick(dt);
        rider.Body.Position = ridee.Body.Position;
        rider.RotationZ = ridee.RotationZ;
        riderVisual.GlobalTransform = ridee.Mount.Seat();
        var st = ridee.States;
        rider.PlaySequence(StateRideAnim.For(st.Cur, st.Sub, ridee.Mount.Data.Rider), ridee.AnimSpeed);
    }
}
