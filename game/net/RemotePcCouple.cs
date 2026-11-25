using System;
using Ms2.Net.Replay;
using Ms2.Ui.Skills;

namespace Ms2Godot.Net;

/// <summary>
/// StateCoupleEmotion (0x3a, statecoupleemotion.cpp:399-760) of another player's PC, driven by its records
/// (<see cref="RemotePcStates.CoupleEntered"/>). The parts that are MyPC's (Stop and its BuddyEmote mode 6, the held item's
/// SendRequestCube) do nothing here: FUN_1417b84b0 is null for this PC, so a partner gone, too far or out of the state
/// leaves the PC in the state until its next record.
/// STILL-OPEN: the loop effects on the path's centre node (AddEffect / RemoveEffect from the text keys) and the
/// teleport effect.
/// </summary>
public sealed partial class RemotePc
{
    /// <summary>Any PC by object id as the couple state reads its partner (the own PC included); null when none.</summary>
    public Func<int, CouplePartnerView?> CouplePartnerOf;
    /// <summary>A PC's gender by object id (PC::GetGender), -1 when none.</summary>
    public Func<int, int> CoupleGenderOf;
    /// <summary>SkillDataTableCli::GetSkillData.</summary>
    public Func<uint, SkillData> CoupleSkillRow;
    public Func<CoupleEmotionTable> CoupleTable;
    /// <summary>CoupleEmotionAnimInfo::Load's nif: the path, the owner and the peer node names.</summary>
    public Func<string, string, string, CouplePath> CouplePathLoader;
    /// <summary>This PC's object id (IsRequester) and gender.</summary>
    public int CoupleObjectId, CoupleGender;

    CoupleEmotionStart coupleData;
    CoupleEmotionTable.AnimInfo coupleInfo;
    CouplePath couplePath;
    double coupleNifStart, coupleLeft = -1, couplePeriod = -1;
    int coupleAni = -1;
    string coupleSeq;

    /// <summary>The couple state's hooks on this PC's state machine.</summary>
    public void HookCouple()
    {
        States.CoupleEntered = CoupleEnter;
        States.CoupleLeft = CoupleExit;
        States.CoupleFrame = CoupleFrame;
        States.CoupleAniOf = () => coupleData != null ? coupleAni : -1;
    }

    /// <summary>
    /// StateCoupleEmotion::vfunc_4 @0x140b808a0 (:415-450): the record's data (StateCoupleEmotionData::vfunc_2; none goes to
    /// StateIdle), the saved angles; with the partner in the field and the row a main type 0xf skill, the name tag offset,
    /// the animation set (Init) and the nif of m_nAni (Load); entered in 0x96, the first animation.
    /// </summary>
    void CoupleEnter()
    {
        coupleInfo = null; couplePath = null; coupleSeq = null; coupleLeft = -1;
        coupleData = CoupleEmotionState.ReadTail(unchecked((uint)States.CoupleEmotion), States.CoupleTail, out int ani);
        if (coupleData == null) { States.ChangeState(RemotePcStates.StateIdle); return; }
        coupleAni = ani;
        if (CouplePartnerOf?.Invoke(coupleData.PartnerId) == null) return;
        var row = CoupleSkillRow?.Invoke(coupleData.Emotion);
        if (row == null || row.MainType != EmotionCast.MainTypeCouple) return;
        NameTagOffset?.Invoke(row.OffsetNameTag);
        coupleInfo = CoupleEmotionTable.Init(CoupleTable?.Invoke()?.GetData(unchecked((uint)row.Param1)), CoupleGender,
            CoupleGenderOf?.Invoke(coupleData.PartnerId) ?? -1, CoupleObjectId == coupleData.RequesterId);
        if (coupleInfo == null) return;
        if (!LoadCouplePath(coupleAni)) return;
        if (States.Sub == CoupleEmotionState.Playing) PlayCoupleAni(coupleAni);
    }

    /// <summary>vfunc_5 @0x140b80d10 (:453-484): the name tag offset back to 0 (the saved angles are the state machine's,
    /// <see cref="RemotePcStates.CoupleLeft"/>).</summary>
    void CoupleExit()
    {
        NameTagOffset?.Invoke(0f);
        coupleData = null; coupleInfo = null; couplePath = null; coupleSeq = null; coupleLeft = -1; coupleAni = -1;
    }

    bool LoadCouplePath(int ani)
    {
        var path = coupleInfo?.NifPath(ani);
        if (path == null) return false;
        couplePath = CouplePathLoader?.Invoke(path, coupleInfo.OwnerNode, coupleInfo.PeerNode);
        coupleNifStart = gameTime;
        return couplePath != null;
    }

    /// <summary>PlayAni @0x140b83ce0: m_nAni, the index's nif, the animation forced.</summary>
    bool PlayCoupleAni(int ani)
    {
        if (coupleInfo == null) return false;
        coupleAni = ani;
        coupleSeq = coupleInfo.AniName(ani);
        LoadCouplePath(ani);
        var play = Play(coupleSeq, true);
        coupleLeft = play.Played ? play.Length : -1;
        couplePeriod = play.Played && play.Loops ? play.Length : -1;
        return true;
    }

    /// <summary>
    /// vfunc_3 @0x140b80f00 (:487-532) for this PC: the stops are MyPC's (nothing here); in 0x95 a partner in 0x95 or 0x96
    /// moves it to 0x96 (FUN_14166cc40, whose vfunc_2 plays the first animation). vfunc_9 @0x140b810d0 (:536-557): in 0x93
    /// the stand (battle) idle unless it plays, then SyncWithPartner. EndOfSequence @0x140b81670: the current animation's
    /// end plays the next (the last one ends in Stop, MyPC's).
    /// </summary>
    void CoupleFrame(float dt)
    {
        var partner = CouplePartnerOf?.Invoke(coupleData?.PartnerId ?? 0);
        if (coupleData != null && partner is { } p && States.Sub == CoupleEmotionState.Started
            && p.Sub is CoupleEmotionState.Playing or CoupleEmotionState.Started && States.SetCoupleSub(CoupleEmotionState.Playing))
            PlayCoupleAni(coupleAni);
        if (States.Sub == CoupleEmotionState.Waiting)
        {
            int key = BattleStance ? RemotePcStates.ActBattleIdle : RemotePcStates.ActIdle;
            if (!IsAction(key)) PlayAction(key, AnimSpeed, false);
        }
        if (partner is { } q) SyncWithPartner(q);
        if (coupleSeq != null && coupleLeft >= 0)
        {
            coupleLeft -= dt;
            if (coupleLeft <= 0)
            {
                coupleLeft = couplePeriod > 0 ? Math.Max(coupleLeft + couplePeriod, 1e-3) : -1;
                if (coupleInfo != null && (coupleInfo.Loop || coupleAni < coupleInfo.LastAni))
                    PlayCoupleAni(Math.Min(coupleAni + 1, coupleInfo.LastAni));
                else if (coupleInfo != null) States.SaveCoupleAngles();
            }
        }
    }

    /// <summary>SyncWithPartner @0x140b82c40 (:940-1008): in 0x96 with the partner in 0x3a at the same sub and animation
    /// index, the PC put where the nif's owner node is and turned to its angles.</summary>
    void SyncWithPartner(CouplePartnerView p)
    {
        if (couplePath == null || States.Sub != CoupleEmotionState.Playing) return;
        if (p.StateId != CoupleEmotionCast.StateCoupleEmotion || p.Sub != States.Sub || p.Ani != coupleAni) return;
        couplePath.Update(gameTime - coupleNifStart);
        var (pos, angles) = couplePath.OwnerPose(coupleData.PosStart, coupleData.PosEnd);
        Position = pos;
        if (angles is { } a) RotationZ = a.Z;
    }
}
