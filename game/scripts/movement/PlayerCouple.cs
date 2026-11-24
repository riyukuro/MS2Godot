using System;
using Godot;
using Ms2.Ui.Skills;
using Act = Ms2Godot.Movement.ActionAnims.Act;

namespace Ms2Godot;

/// <summary>What the own couple state reads of its partner (a remote PC, by object id).</summary>
public readonly record struct CouplePartnerView(System.Numerics.Vector3 Position, int StateId, int Sub, int Ani);

/// <summary>
/// The own pc's StateCoupleEmotion (0x3a, statecoupleemotion.cpp:399-760): entered by the couple cast (sub 0x93, the asker
/// waiting) or the accept (sub 0x94); the server's start (BuddyEmote mode 5) moves it to 0x95, and when the partner is in
/// 0x95 or 0x96 it plays (0x96): the row's animations one after another, the body put each frame where the path nif's
/// own node is (SyncWithPartner). The stop (<see cref="CoupleStop"/>, StateCoupleEmotion::Stop) is the HUD's: it sends
/// mode 6 and the notice.
/// STILL-OPEN: the loop effects on the path's centre node (the text keys loopstart_effect_ / loopend_effect_ /
/// loopreset_effect_, AddEffect / RemoveEffect), the teleport effect and the ground drop of vfunc_24 (whose caller is not
/// found), SendRequestCube with a held item on the enter, and the base cast poll (FUN_1417b9460) in vfunc_8.
/// </summary>
public partial class Player
{
    /// <summary>The couple state's data and sub (FUN_14166ccb0's StateCoupleEmotion).</summary>
    public readonly CoupleEmotionState Couple = new();
    CoupleEmotionTable.AnimInfo coupleInfo;        // m_spAnimInfo's table part
    CouplePath couplePath;                         // m_spAnimInfo's loaded nif (m_spNifRoot and its nodes)
    double coupleNifStart;                         // m_fStartTime (CMainSystem::GetGameTime at the load)
    float coupleSavedRotZ;                         // m_posSaved (vfunc_20 at the enter)
    string coupleAni;                              // the sequence PlayAni played (for EndOfSequence's name test)

    /// <summary>The partner by object id (FindPCById), null when not in the field.</summary>
    public Func<int, CouplePartnerView?> CouplePartner;
    /// <summary>StateCoupleEmotion::Stop(bNotify): the HUD leaves the state, sends mode 6 and, with true, notice 0xcee.</summary>
    public event Action<bool> CoupleStop;
    /// <summary>CoupleEmotionAnimInfo::Load: the path nif of an animation index (Data/Resource/Model/Path/...), null when
    /// it fails to load or lacks the owner / peer node.</summary>
    public Func<string, string, string, CouplePath> CouplePathLoader;
    /// <summary>CoupleEmotionMaxDistance.</summary>
    public int CoupleMaxDistance = CoupleEmotionCast.DefaultMaxDistance;

    /// <summary>What another PC's couple state reads of this one: the feet (MS2 axes), GetCurStateId, the sub, m_nAni.</summary>
    public CouplePartnerView CoupleView
    {
        get
        {
            var g = GlobalPosition;
            return new CouplePartnerView(new System.Numerics.Vector3(g.X, -g.Z, g.Y), ClientStateId,
                State == MoveState.CoupleEmotion ? Couple.Sub : 0, State == MoveState.CoupleEmotion ? Couple.Ani : -1);
        }
    }

    /// <summary>The couple emotion of the state (the sync record's m_n64), else 0.</summary>
    public int CoupleEmotionId => State == MoveState.CoupleEmotion && Couple.Active ? (int)Couple.Emotion : 0;

    /// <summary>
    /// vfunc_59(0x3a, data, sub, 1), then StateCoupleEmotion::vfunc_4 @0x140b808a0 (:415-450): the data, the saved angles,
    /// and with the partner in the field and the row a main type 0xf skill: the name tag offset (FUN_141670330 of
    /// offsetNameTag), the animation set (Init with param1, the two genders and IsRequester) and its first path nif
    /// (Load(m_nAni)); entered already in 0x96, the first animation.
    /// </summary>
    public void EnterCouple(CoupleEmotionStart data, int sub, SkillData row, int partnerGender, CoupleEmotionTable table, int ownObjectId)
    {
        if (DeadFlag || Mount != null || data == null) return;
        CancelSkillSequence();
        StopInteractAnim();
        Enter(MoveState.CoupleEmotion);
        Couple.Enter(data, sub);
        coupleInfo = null;
        couplePath = null;
        coupleAni = null;
        coupleSavedRotZ = FacingRotZ;
        if (CouplePartner?.Invoke(data.PartnerId) == null) return;
        if (row == null || row.MainType != EmotionCast.MainTypeCouple) return;
        NameTagOffset = row.OffsetNameTag;           // FUN_141670330 sets m_f170 only
        coupleInfo = CoupleEmotionTable.Init(table?.GetData((uint)row.Param1), Gender, partnerGender, Couple.IsRequester(ownObjectId));
        if (coupleInfo == null) return;
        if (!LoadCouplePath(Couple.Ani)) return;
        if (sub == CoupleEmotionState.Playing) PlayFirstCoupleAni();
        if (Ms2Godot.Net.LiveSession.LiveCheck)
            GD.Print($"couple: enter {data.Emotion} sub 0x{sub:x} partner {data.PartnerId} requester {data.RequesterId} node {coupleInfo.OwnerNode} anis {string.Join(",", coupleInfo.AniNames)}");
    }

    /// <summary>CoupleEmotionAnimInfo::Load: the nif of the index and its clock started now.</summary>
    bool LoadCouplePath(int ani)
    {
        var path = coupleInfo?.NifPath(ani);
        if (path == null) return false;
        couplePath = CouplePathLoader?.Invoke(path, coupleInfo.OwnerNode, coupleInfo.PeerNode);
        coupleNifStart = gameTime;
        return couplePath != null;
    }

    /// <summary>FUN_14166cc40 on the own state: the sub, and State::vfunc_2's 0x96 starts the first animation.</summary>
    public void SetCoupleSub(int sub)
    {
        if (State != MoveState.CoupleEmotion) return;
        int old = Couple.Sub;
        Couple.SetSub(sub);
        if (old != sub && sub == CoupleEmotionState.Playing) PlayFirstCoupleAni();
    }

    /// <summary>The state left to StateIdle (vfunc_59(1, 0, 0, 0)).</summary>
    public void LeaveCouple()
    {
        if (State == MoveState.CoupleEmotion) Enter(MoveState.Idle);
    }

    /// <summary>vfunc_5 @0x140b80d10 (:453-484): the effects removed (none here), the saved angles back only when 0x3a is
    /// entered again, the name tag offset back to 0.</summary>
    void CoupleExit(MoveState next)
    {
        Couple.Exit();
        if (next == MoveState.CoupleEmotion) yawDeg = targetYawDeg = coupleSavedRotZ;
        NameTagOffset = 0;
        couplePath = null;
    }

    /// <summary>PlayFirstAni @0x140b83b80: PlayAni(m_nAni), its failure stops the state.</summary>
    void PlayFirstCoupleAni()
    {
        if (!PlayCoupleAni(Couple.Ani)) CoupleStop?.Invoke(false);
    }

    /// <summary>PlayNextAni @0x140b83c20: the next index (clamped to the last; a row that does not loop ends after it).</summary>
    bool PlayNextCoupleAni()
    {
        if (coupleInfo == null) return false;
        int last = coupleInfo.LastAni;
        if (!coupleInfo.Loop && Couple.Ani >= last) return false;
        return PlayCoupleAni(Math.Min(Couple.Ani + 1, last));
    }

    /// <summary>PlayAni @0x140b83ce0: m_nAni, the index's nif loaded with its clock now, the animation with bForce.</summary>
    bool PlayCoupleAni(int ani)
    {
        if (coupleInfo == null) return false;
        Couple.Ani = ani;
        string name = coupleInfo.AniName(ani);
        LoadCouplePath(ani);
        coupleAni = name;
        PlayEmotion(name, true);
        return true;
    }

    /// <summary>
    /// The state's frame. vfunc_8 @0x140b81830 (:655-672): the held move stops the state and walks. vfunc_3 @0x140b80f00
    /// (:487-532): no partner stops it; in 0x93 a partner beyond CoupleEmotionMaxDistance stops it with the notice; in 0x96
    /// a partner out of 0x3a or below 0x95 stops it; in 0x95 a partner in 0x95 or 0x96 moves it to 0x96. vfunc_9
    /// @0x140b810d0 (:536-557): in 0x93 the idle (battle idle in battle) without a restart; then SyncWithPartner.
    /// EndOfSequence @0x140b81670 (:627-652): the end of the current animation plays the next, or stops the state.
    /// </summary>
    void CoupleUpdate(bool hasInput, float dt)
    {
        kVel = Vector3.Zero;
        if (hasInput)
        {
            CoupleStop?.Invoke(false);
            Enter(MoveState.Walk);
            return;
        }
        var partner = CouplePartner?.Invoke(Couple.PartnerId);
        if (partner is not { } p) { CoupleStop?.Invoke(false); return; }
        var feet = new System.Numerics.Vector3(GlobalPosition.X, -GlobalPosition.Z, GlobalPosition.Y);
        switch (Couple.Sub)
        {
            case CoupleEmotionState.Waiting:
                if (!CoupleEmotionCast.WithinDistance(feet, p.Position, CoupleMaxDistance)) { CoupleStop?.Invoke(true); return; }
                break;
            case CoupleEmotionState.Playing:
                if (p.StateId != CoupleEmotionCast.StateCoupleEmotion || p.Sub < CoupleEmotionState.Started) { CoupleStop?.Invoke(false); return; }
                break;
        }
        if (Couple.Sub == CoupleEmotionState.Started && p.Sub is CoupleEmotionState.Playing or CoupleEmotionState.Started)
            SetCoupleSub(CoupleEmotionState.Playing);
        if (State != MoveState.CoupleEmotion) return;
        // vfunc_9's 0x93: PlayAnimationById(vfunc_65() ? 1 : 0) when it is not the current action
        // STILL-OPEN: FUN_14166f8f0(0, 1) after it
        if (Couple.Sub == CoupleEmotionState.Waiting) PlayAct(InBattle ? Act.BattleIdle : Act.Idle);
        SyncWithPartner(p);
        if (coupleAni != null && EmotionSequenceEnded(dt) && Anims?.Sequence(coupleAni, false) == CurrentAnim)
        {
            if (!PlayNextCoupleAni())
            {
                coupleSavedRotZ = FacingRotZ;
                CoupleStop?.Invoke(false);
            }
        }
    }

    /// <summary>FindPlacement @0x140b82230 on the state just entered: the path nif from the start point and the own
    /// angles (vfunc_20); false without a path or a spot.</summary>
    public bool FindCouplePlacement(Func<System.Numerics.Vector3, bool> placeable, Func<System.Numerics.Vector3, System.Numerics.Vector3, float, bool> rayHits,
        out System.Numerics.Vector3 posA, out System.Numerics.Vector3 posB, out float yaw)
    {
        posA = posB = default;
        yaw = 0;
        if (State != MoveState.CoupleEmotion || !Couple.Active || couplePath == null) return false;
        return couplePath.FindPlacement(Couple.PosStart, new System.Numerics.Vector3(0f, 0f, FacingRotZ), placeable, rayHits, out posA, out posB, out yaw);
    }

    /// <summary>
    /// SyncWithPartner @0x140b82c40 (:940-1008): in 0x96 with the partner in 0x3a at the same sub and animation index, the
    /// nif brought to now (UpdateNif), the owner node's world transform with the parent's rotation replaced by the end
    /// angles (EulerToMatrix), the feet put at the start plus its translation and the body turned to its angles.
    /// </summary>
    void SyncWithPartner(CouplePartnerView p)
    {
        if (Couple.Sub != CoupleEmotionState.Playing || couplePath == null) return;
        if (p.StateId != CoupleEmotionCast.StateCoupleEmotion || p.Sub != Couple.Sub || p.Ani != Couple.Ani) return;
        couplePath.Update(gameTime - coupleNifStart);
        var (pos, angles) = couplePath.OwnerPose(Couple.PosStart, Couple.PosEnd);
        GlobalPosition = new Vector3(pos.X, pos.Z, -pos.Y);
        if (angles is { } a) { yawDeg = targetYawDeg = a.Z; Visual.Rotation = new Vector3(0, Mathf.DegToRad(yawDeg), 0); }
    }
}
