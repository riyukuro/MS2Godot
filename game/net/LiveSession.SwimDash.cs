using Godot;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Ui;

namespace Ms2Godot.Net;

// The own pc's StateSwimDash dash effect, StateStun's face, StateTalk's talk animations and talk end and the stun buff's checks, the parts
// of the server-set states (Ms2.Ui.OwnStateExit) that need the field.
public partial class LiveSession
{
    int swimDashEffect;          // StateSwimDash m_nEffect
    object swimDashEffectScope;  // the field's region skill node the id is of
    static SwimGems swimGems;
    bool? gemSwimFeature;

    /// <summary>The live run's count of the dash effects started, and the last one's name.</summary>
    internal int SwimDashEffects;
    internal string LastSwimDashEffect;
    internal int SwimDashEffectId => swimDashEffect;
    internal int SwimDashEffectsStopped;
    /// <summary>The dash effect's name for the own pc now (PC::GetSwimEffectName(0x32)), for the checks.</summary>
    internal string SwimDashEffectName() => SwimEffectName(SwimGems.DashAction);
    /// <summary>The live run's count of the faces the server-set states played, the last name and whether it played.</summary>
    internal int OwnStateFaces;
    internal string LastOwnStateFace;
    internal bool LastOwnStateFacePlayed;
    /// <summary>The live run's count of the stun buff's entries refused by its checks, and the last reason.</summary>
    internal int BuffStunsRefused;
    internal string LastBuffStunRefusal;

    /// <summary>
    /// StateSwimDash::FUN_1417fbec0 (stateswimdash.cpp:221-233): StopEffect, then PC::GetSwimEffectName(0x32) started at the
    /// own pc (SetActor) and kept as m_nEffect, until the next entry's StopEffect (the nested ChangeState's vfunc_5,
    /// stateswimdash.cpp:102-108).
    /// </summary>
    void StartSwimDashEffect()
    {
        if (swimDashEffect != 0 && ReferenceEquals(swimDashEffectScope, remote?.KeptEffectScope)) { StopKeptEffect(swimDashEffect); SwimDashEffectsStopped++; }
        swimDashEffect = 0;
        swimDashEffectScope = null;
        if (player == null || !IsInstanceValid(player) || player.Visual == null) return;
        string name = SwimEffectName(SwimGems.DashAction);
        swimDashEffect = SpawnKeptEffect(name, player.Visual, Ms2.Audio.SoundMusicEffectState.CasterMyPc);
        swimDashEffectScope = remote?.KeptEffectScope;
        SwimDashEffects++;
        LastSwimDashEffect = name;
        if (LiveCheck) GD.Print($"live: swim dash effect {name}{(swimDashEffect == 0 ? " did not spawn" : "")}");
    }

    /// <summary>PC::GetSwimEffectName (pc.cpp:3575-3592) of the own pc: its swim gem's effect, else the gender's tube.</summary>
    string SwimEffectName(int action) => SwimGems.EffectName(action, OwnSwimGem(), Me.User?.Gender ?? 0);

    /// <summary>
    /// PC::GetSwimGem (pc.cpp:3015-3025) of the own pc with its data (CGemSwim::GetData): feature 0x68 GemSwim on, the gem
    /// equipment's usable gem of type 6 (FUN_141270740, CanUseItem) and its row of table/itemgemproperty_swim.xml; null
    /// without one.
    /// </summary>
    SwimGems.Gem OwnSwimGem()
    {
        var packs = PackSet.Shared(Settings.DataDir);
        if (gemSwimFeature == null)
        {
            try { gemSwimFeature = LobbyFeatures.IsEnabledByName(packs, FeatureTable.Names[SwimGems.Feature - 1]); }
            catch (System.Exception e) { GD.Print($"live: feature table unavailable ({e.Message})"); gemSwimFeature = false; }
        }
        if (gemSwimFeature != true) return null;
        int id = World.NpcEmoticons.Live?.WornGemId(selfObject, SwimGems.GemType) ?? 0;
        if (id == 0) return null;
        swimGems ??= SwimGems.Load(packs);
        return swimGems.Find(id);
    }

    /// <summary>PCBase::FUN_14166ef10(face, 1, 0, false) on the own pc for the face a server-set state's enter played.</summary>
    void PlayOwnStateFace()
    {
        if (ownStateExit.TakeFace() is not { } face || player == null || !IsInstanceValid(player)) return;
        OwnStateFaces++;
        LastOwnStateFace = face;
        LastOwnStateFacePlayed = player.PlayFace(face, false);
        if (LiveCheck) GD.Print($"live: own face {face} {(LastOwnStateFacePlayed ? "played" : $"not played (now {player.FaceEmotion?.Current ?? "no face"})")}");
    }

    /// <summary>StateTalk's bTalk, bFirst and bSecond (statetalk.cpp:100-123): the open talk's npc has talkAni, the own body
    /// has TALK_UP and TALK_IDLE.</summary>
    bool OwnTalkUp()
    {
        var row = TalkNpcId != 0 && IsTalking() ? remote?.NpcRowOf(TalkNpcId) : null;   // GetTalkNpc: the open talk's npc
        bool up = player != null && IsInstanceValid(player)
            && OwnStateExit.ChoosesTalkUp(row?.TalkAni == true, player.HasTalkUp, player.HasTalkIdle);
        if (LiveCheck) GD.Print($"live: talk state with npc {TalkNpcId} ({(row == null ? "none" : $"row {row.Id} talkAni {row.TalkAni}")}): {(up ? OwnStateExit.TalkUpAction : OwnStateExit.TalkAction)}");
        return up;
    }

    /// <summary>The live run's count of the open talks StateTalk's leave or hit ended (NpcTalk 0).</summary>
    internal int OwnTalkEnds;
    internal int OwnTalkPhase => ownStateExit.TalkPhase;

    /// <summary>The own pc's chat balloon entity (PCBase::GetChatBalloonEntity, pc.cpp:1586-1590) is active (m_4c), for
    /// StateTalk's phase 0x48.</summary>
    internal bool OwnBalloonActive() =>
        World.NpcEmoticons.Live?.Balloons?.Entities is { } e && e.TryGetValue(selfObject, out var b) && b.Active;

    /// <summary>StateTalk::vfunc_5 and vfunc_6 (statetalk.cpp:142-150, 202-206) for the own pc: with a talk open
    /// (UITalkManager::GetTalking), SendNpcTalkMode0.</summary>
    void EndTalkOfOwnState()
    {
        if (!ownStateExit.TakeTalkEnds() || !IsTalking()) return;
        OwnTalkEnds++;
        if (LiveCheck) GD.Print("live: StateTalk ended with the talk open: NpcTalk 0");
        EndTalk();
    }

    /// <summary>
    /// AdditionalCliList::FUN_1409713b0's checks (additionalcli_2.cpp:786-797) for the own pc: current health (Health of
    /// the Stat packet), the current state's vfunc_17 (0 in StateGrabTarget only) and PC::FUN_141660120's hold by a riding
    /// PC with event buff 2 (<see cref="OwnStun.HeldByRidingPc"/>).
    /// </summary>
    bool OwnStunMayEnter()
    {
        long hp = Me.Stats2[PlayerStat.Health];
        int state = ((IHoldPcWorld)this).HoldPcState;
        bool held = false;
        if (state == OwnStun.StateHold && remote != null)
        {
            var h = remote.HolderOf(player.HoldTargetId, 2);
            held = OwnStun.HeldByRidingPc(state, h.IsPc, h.Rides, h.EventBuff);
        }
        if (OwnStun.BuffMayEnter(hp, state, held)) { LastBuffStunRefusal = null; return true; }
        string why = hp == 0 ? "no health" : state == OwnStun.StateGrabTarget ? "StateGrabTarget" : "held by a riding pc";
        if (why != LastBuffStunRefusal && LiveCheck) GD.Print($"live: stun buff refused: {why}");
        BuffStunsRefused++;
        LastBuffStunRefusal = why;
        return false;
    }
}
