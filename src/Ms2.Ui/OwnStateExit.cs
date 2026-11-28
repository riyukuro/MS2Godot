using System;
using System.Collections.Generic;

namespace Ms2.Ui;

/// <summary>How the client leaves one of its state classes when it was entered with no state data, as OnUserState enters
/// it (scenestategame.cpp:5759-5770, vfunc_59(state, 0, 0, 0)), and where the port runs that exit.</summary>
public enum StateExitCause
{
    /// <summary>The enter or the first frame finds no state data and goes on to StateIdle.</summary>
    AtOnce,
    /// <summary>StateMoveSkill's enter finds no state data (vfunc_38, statemanager.cpp:770-779) and enters the class's
    /// fallback state at once (FUN_1417df370, :720-723), here not StateIdle: StateSwimDash's StateSwim.</summary>
    Fallback,
    /// <summary>The input step leaves when a key is not held.</summary>
    InputStep,
    /// <summary>A timer of the state's own.</summary>
    Timer,
    /// <summary>The end of the sequence the state plays.</summary>
    SequenceEnd,
    /// <summary>No exit of the state's own: input, a cancel or a packet only.</summary>
    NoSelfExit,
    /// <summary>The casting bar the enter starts: its finish (OnCastingFinished sets m_n48) enters StateIdle on the next
    /// frame; leaving the state stops the bar (FUN_1418e1390).</summary>
    CastingBar,
    /// <summary>The body's move state of the same id runs the exit (Me.State is left with it, LeaveForBodyState).</summary>
    Body,
    /// <summary>Kept apart from Me.State: a controller of its own runs the state.</summary>
    Apart,
    /// <summary>Not a state of the own pc (an NPC, pet or doll state, or a base class).</summary>
    NotOwn,
    /// <summary>The state's feature is under Later; listed, not run.</summary>
    Later,
}

/// <summary>One state class: its id (-1 for a class the pc never registers), the client's exit and the cite.</summary>
public sealed record StateExitRow(string Class, int Id, StateExitCause Cause, string Cite, string Note);

/// <summary>What a state class plays on the body when it is entered with no state data.</summary>
public enum StateEnterPlay
{
    /// <summary>Nothing the body shows (the class leaves at once, or plays nothing without data).</summary>
    Nothing,
    /// <summary>The body's move state of the same id plays it (Player.EnterServerState).</summary>
    BodyState,
    /// <summary>A sequence of the class's own, played on the body while the state runs.</summary>
    Sequence,
    /// <summary>The body hidden while the state runs.</summary>
    Hide,
    /// <summary>An effect only.</summary>
    Effect,
    /// <summary>The casting bar only.</summary>
    CastingBar,
    /// <summary>A controller of its own plays it.</summary>
    Apart,
    /// <summary>Not a state of the own pc.</summary>
    NotOwn,
    /// <summary>The state's feature is under Later; listed, not run.</summary>
    Later,
}

/// <summary>The body's entry for a server-set state (<see cref="OwnStateExit.BodyEntryOf"/>).</summary>
public enum BodyEntry
{
    /// <summary>The body is left as it is (held, or run by a controller of its own).</summary>
    None,
    Idle,
    /// <summary>StateWalk: with no move key held the next input step stands, so the body is left as it is.</summary>
    Walk,
    Land,
    Fall,
    Jump,
    /// <summary>A ladder in reach (side 3), else StateIdle (stateladder.cpp:153-201).</summary>
    Ladder,
    /// <summary>A rope in reach (side 3), else StateIdle (statemanager.cpp:1236, 1260).</summary>
    Rope,
    Hit,
    Dash,
    Revival,
    /// <summary>StateSwim; the body swims only on water.</summary>
    Swim,
    /// <summary>StateClimb with no data: StateIdle at once (StateMoveSkill's fallback 1), then the rest of the enter hides
    /// the shadow (SetShadowVisible(false), state.cpp:1038).</summary>
    Climb,
    FallDamage,
    /// <summary>No StateTriggerEmotionData: no emotion list, nothing plays.</summary>
    TriggerEmotion,
}

/// <summary>One state class and what its enter plays with no state data (<see cref="StateEnterPlay"/>), with the cite.</summary>
public sealed record StateEnterRow(string Class, int Id, StateEnterPlay Play, string What, string Cite);

/// <summary>
/// The own pc's exit from a state the server put it into (OnUserState, no state data) for the states the body has no move
/// state for, and what the state plays on the body meanwhile. The pc's classes are the ones PC's constructor registers
/// (pc.cpp:668-721). Each frame the client's own exit is run: at once, on the input step, on a timer or at the end of the
/// played sequence; the states whose input step reads no move key hold the body while they run. StateStun is the
/// <see cref="OwnStun"/> kept across entries, which the stun buff also enters (<see cref="EnterBuffStun"/>).
/// StateSummon, StateRecall, StateTreeWatering and StateAutoInteraction start the casting bar (<see cref="TakeCast"/>), and
/// its finish leaves them (<see cref="CastFinished"/>); a hit leaves the first three (<see cref="Hit"/>).
/// StateTalk chooses TALK_UP or IDLE (<see cref="TalkUp"/>), after TALK_UP the talk or listen idle (<see cref="TalkPhase"/>),
/// holds the body and ends the open talk when left or hit (<see cref="TakeTalkEnds"/>); StateSwimDash enters StateSwim inside its enter and keeps
/// the dash effect (<see cref="TakeFallback"/>, <see cref="TakeDashEffect"/>); StateStun's enter plays the "Stun" face
/// (<see cref="TakeFace"/>).
/// </summary>
public sealed class OwnStateExit
{
    /// <summary>Every state class of the client, with its exit (the 61 classes of the own-state table).</summary>
    public static readonly IReadOnlyList<StateExitRow> Classes = new StateExitRow[]
    {
        new("StateIdle", 0x1, StateExitCause.Body, "stateidle.cpp:308", "the body's Idle runs the exit"),
        new("StateIdlePet", 0x1, StateExitCause.NotOwn, "", "an NPC, pet or doll state"),
        new("StateWalk", 0x2, StateExitCause.Body, "statewalk.cpp:329", "the body's Walk runs the exit"),
        new("StateWalkNPC", 0x2, StateExitCause.NotOwn, "", "an NPC, pet or doll state"),
        new("StateCrawl", 0x3, StateExitCause.InputStep, "state.cpp:1687-1688", "the input step enters StateIdle when the crawl key (0x493e5) is not held; the port binds no crawl key, so the first step leaves"),
        new("StateLand", 0x4, StateExitCause.Body, "stateland.cpp:158", "the body's Land runs the exit"),
        new("StateFall", 0x5, StateExitCause.Body, "statefall.cpp:213", "the body's Fall runs the exit"),
        new("StateJump", 0x6, StateExitCause.Body, "statejump.cpp:212", "the body's Jump runs the exit"),
        new("StateJumpTo", 0x7, StateExitCause.AtOnce, "statejumpto.cpp:76-77, 102-104", "no StateJumpToData: no arc is started (sub 0x29), so the first frame enters StateIdle"),
        new("StateLadder", 0x8, StateExitCause.Body, "stateladder.cpp:343", "the body's Ladder runs the exit (no ladder in reach: StateIdle, the shadow left hidden, stateladder.cpp:198-202)"),
        new("StateRope", 0x9, StateExitCause.Body, "statemanager.cpp:1472", "the body's Rope runs the exit (no rope in reach: StateIdle, the shadow left hidden, statemanager.cpp:1257-1262)"),
        new("StateEmotion", 0xb, StateExitCause.AtOnce, "stateemotionidle.cpp:218-220", "no StateEmotionData: the enter goes on to StateIdle"),
        new("StateHit", 0xd, StateExitCause.Body, "statehit.cpp:129", "the body's Hit runs the exit"),
        new("StateHitNPC", 0xd, StateExitCause.NotOwn, "statehit.cpp:290", "an NPC, pet or doll state"),
        new("StateEmotionIdle", 0xf, StateExitCause.AtOnce, "stateemotionidle.cpp:663-666", "no StateEmotionIdleData: the enter goes on to StateIdle"),
        new("StateSkill", 0x10, StateExitCause.Apart, "stateskill.cpp:1982", "the HUD cast (skillStateRuns)"),
        new("StateSpawn", 0x11, StateExitCause.Timer, "statespawn.cpp:73-75, 98-130", "hidden for the object id seeded random % 300 + 100 ms; no StateSpawnData, so no sequence (duration 0) and StateIdle at the timer"),
        new("StateStun", 0x12, StateExitCause.SequenceEnd, "statestun.cpp:93-96, 143-156, 167-170", "no StateStunData and no stun buff: Stnd_Up_A or Stnd_Up_B (rand() % 2) plays and its end enters StateIdle"),
        new("StateDash", 0x14, StateExitCause.Body, "state.cpp:1821", "the body's Dash runs the exit"),
        new("StateTalk", 0x16, StateExitCause.NoSelfExit, "", "input, cancel or a packet only"),
        new("StateRegen", 0x17, StateExitCause.NotOwn, "pc.cpp:668-721", "not registered on the pc (an NPC state)"),
        new("StateRevival", 0x18, StateExitCause.Body, "statedead.cpp:337", "the body's Revival runs the exit"),
        new("StateInteraction", 0x19, StateExitCause.Apart, "stateinteractdata.cpp:424", "the interaction casts (InteractKey)"),
        new("StateInteractionNpc", 0x1a, StateExitCause.NotOwn, "", "an NPC, pet or doll state"),
        new("StateSwim", 0x1b, StateExitCause.Body, "", "the body's Swim runs the exit"),
        new("StateSwimDash", 0x1c, StateExitCause.Fallback, "stateswimdash.cpp:77-83, 92-99, statemanager.cpp:291-314, 670-676, 720-723, 770-779", "no StateSkillData: StateMoveSkill's enter enters the fallback StateSwim (0x1b) at once, inside the enter (PCBase::vfunc_59 changes the state now, pcbase.cpp:1098-1113)"),
        new("StateClimb", 0x1d, StateExitCause.AtOnce, "state.cpp:1004-1008, 1029-1057, statemanager.cpp:670-676, 720-723, 770-779", "no StateSkillData: StateMoveSkill's enter enters the fallback StateIdle at once; the rest of the enter then hides the shadow"),
        new("StateGlide", 0x1e, StateExitCause.AtOnce, "stateglide.cpp:86-96, 109-116, 290-301, statemanager.cpp:670-676, 720-723", "no StateGlideData: StateMoveSkill's enter enters the fallback StateFall (5) at once, then the missing glide row enters StateIdle"),
        new("StateFallDamage", 0x1f, StateExitCause.Body, "statefalldamage.cpp:108", "the body's FallDamage runs the exit"),
        new("StateHold", 0x20, StateExitCause.AtOnce, "stateholddata.cpp:144-151, 161-169, statemanager.cpp:670-676, 720-723", "no StateHoldData: StateMoveSkill's enter enters the fallback state 6 (sub 0x22) at once, then the missing hold data enters StateIdle"),
        new("StateRide", 0x21, StateExitCause.Apart, "", "the mount"),
        new("StateTransform", 0x22, StateExitCause.SequenceEnd, "statetransform.cpp:63, 79-86", "Equip_Change_Mantle_A plays and its end enters StateIdle"),
        new("StateTransformDoll", 0x22, StateExitCause.NotOwn, "fittingdollslot_1.cpp:146", "the fitting doll's 0x22 (the pc registers StateTransform, pc.cpp:698)"),
        new("StatePuppetNpc", 0x23, StateExitCause.NotOwn, "stateidle.cpp:1034", "an NPC, pet or doll state"),
        new("StateTaxiCall", 0x25, StateExitCause.AtOnce, "statetaxicalldata.cpp:171-174", "no StateTaxiCallData: the enter goes on to StateIdle (TaxiCalling runs a call the own pc starts)"),
        new("StateSummon", 0x26, StateExitCause.CastingBar, "statesummon.cpp:199-272, 283-294, 297-373, 390-400", "no StateSummonData: the casting bar (string 0, SummonRideeDuration); its finish enters StateIdle, as a hit does (vfunc_6); the summon the own pc starts with an item is rideSummon's"),
        new("StatePvPWinLose", 0x28, StateExitCause.NoSelfExit, "statepvpwinlose.cpp:46-53, 65-70", "sub 0 (the server's state): no win or lose sequence plays, so the sequence end never leaves; Pvp 'e' enters it with sub 0x73 / 0x74 (EnterPvpWinLose), whose sequence end leaves; the input step is State's (a queued skill only)"),
        new("StateUseFurniture", 0x29, StateExitCause.Later, "stateusefurnituredata.cpp:281", "its feature (furniture/home) is under Later; client exit: the frame at stateusefurnituredata.cpp:281 -> 1"),
        new("StateCashCall", 0x2a, StateExitCause.AtOnce, "statecashcalldata.cpp:159-162", "no StateCashCallData: the enter goes on to StateIdle"),
        new("StateGrabTarget", 0x2b, StateExitCause.AtOnce, "statedatagrabtarget.cpp:87-89", "no StateDataGrabTarget: the enter goes on to StateIdle"),
        new("StateRecall", 0x2c, StateExitCause.CastingBar, "statesummon.cpp:489-517, 520-531, 534-575, 577-587", "the casting bar (string 0x657, RecallCastingTime); its finish enters StateIdle (no StateRecallData: no party packet), as a hit does (vfunc_6)"),
        new("StateFishing", 0x2d, StateExitCause.AtOnce, "statefishingdata.cpp:206-209", "no StateFishingData: the enter goes on to StateIdle"),
        new("StateFishingFloat", 0x2e, StateExitCause.NotOwn, "cubecraft_1.cpp:928", "the fishing float's state (CFishingGuideObject), not the pc's"),
        new("StatePlayInstrument", 0x2f, StateExitCause.Later, "stateplayinstrumentdata.cpp:427", "its feature (instruments) is under Later; client exit: the frame at stateplayinstrumentdata.cpp:427 -> 5"),
        new("StateSummonRevive", 0x30, StateExitCause.NotOwn, "", "an NPC, pet or doll state"),
        new("StateTriggerEmotion", 0x35, StateExitCause.Body, "stateemotionidle.cpp:438", "the body's TriggerEmotion runs the exit"),
        new("StateHomeConvenient", 0x36, StateExitCause.AtOnce, "statehomeconvenient.cpp:245-248", "no StateHomeConvenientData: the enter goes on to StateIdle"),
        new("StateHomemade", 0x37, StateExitCause.Later, "statehomemade.cpp:239", "its feature (furniture/home) is under Later; client exit: the frame at statehomemade.cpp:239 -> 1"),
        new("StateAutoInteraction", 0x39, StateExitCause.CastingBar, "stateinteractdata.cpp:684-718, 721-743, 759, 768-803", "no StateAutoInteractionData: the casting param's defaults (duration 0, no text), which the movie completes at once; the next frame enters StateIdle (no SmartPush packet). It has no hit exit"),
        new("StateCoupleEmotion", 0x3a, StateExitCause.AtOnce, "statecoupleemotion.cpp:422-425", "no StateCoupleEmotionData: the enter goes on to StateIdle"),
        new("StateBanner", 0x3b, StateExitCause.AtOnce, "statebannerdata.cpp:115-117", "no StateBannerData: the enter goes on to StateIdle"),
        new("StateMicroGameRps", 0x3c, StateExitCause.Later, "statemicrogame.cpp:262", "its feature (minigames) is under Later; client exit: the sequence end at statemicrogame.cpp:262 -> 1"),
        new("StateReact", 0x3d, StateExitCause.Apart, "statereact.cpp:271", "reactState (SequenceEnded = EndOfSequence)"),
        new("StateTreeWatering", 0x3e, StateExitCause.CastingBar, "unit_140ad5280_statetreewateringcontext.cpp:97-125, 148-158, 161-190, 209-219", "sub 0: the casting bar (string 0, SummonRideeDuration), no sequence; its finish enters StateIdle (no TreeWatering packet), as a hit does (vfunc_6)"),
        new("StateNurturing", 0x40, StateExitCause.Later, "statenurturing.cpp:286", "its feature (nurturing) is under Later; client exit: the frame at statenurturing.cpp:286 -> 1"),
        new("StateMicroGameCoupleDance", 0x45, StateExitCause.Later, "statemicrogame.cpp:862", "its feature (minigames) is under Later; client exit: statemicrogame.cpp:862 -> 1"),
        new("StateTriggerFollowNpc", 0x46, StateExitCause.AtOnce, "stateemotionidle.cpp:508-510", "no StateDataTriggerFollowNpc: the enter goes on to StateIdle"),
        new("StateWeddingEmotion", 0x47, StateExitCause.Later, "stateweddingemotiondata.cpp:199", "its feature (wedding) is under Later; client exit: the sequence end at stateweddingemotiondata.cpp:199 -> 1"),
        new("StateGoToHome", -1, StateExitCause.NotOwn, "", "no object registers it"),
        new("StateMoveSkill", -1, StateExitCause.NotOwn, "pc.cpp:668-721", "not registered on the pc: StateGlide's base only"),
        new("StateSummonIdle", -1, StateExitCause.NotOwn, "", "an NPC, pet or doll state"),
    };

    /// <summary>What each class plays on the body when it is entered with no state data (the same 61 classes, the same
    /// order as <see cref="Classes"/>).</summary>
    public static readonly IReadOnlyList<StateEnterRow> Enters = new StateEnterRow[]
    {
        new("StateIdle", 0x1, StateEnterPlay.BodyState, "Idle", "stateidle.cpp:167-200"),
        new("StateIdlePet", 0x1, StateEnterPlay.NotOwn, "", ""),
        new("StateWalk", 0x2, StateEnterPlay.BodyState, "Walk: with no move key held the next input step stands", "statewalk.cpp:101-155"),
        new("StateWalkNPC", 0x2, StateEnterPlay.NotOwn, "", ""),
        new("StateCrawl", 0x3, StateEnterPlay.Nothing, "the rest face; the crawl sequence's one frame (the update runs before the input step leaves)", "state.cpp:1640-1646, statemanager.cpp:232-233, mypc.cpp:2966"),
        new("StateLand", 0x4, StateEnterPlay.BodyState, "Land", "stateland.cpp:74-104"),
        new("StateFall", 0x5, StateEnterPlay.BodyState, "Fall", "statefall.cpp:93-131"),
        new("StateJump", 0x6, StateEnterPlay.BodyState, "Jump", "statejump.cpp:95-146"),
        new("StateJumpTo", 0x7, StateEnterPlay.Nothing, "no arc (sub 0x29)", "statejumpto.cpp:52-78"),
        new("StateLadder", 0x8, StateEnterPlay.BodyState, "Ladder on a ladder in reach (side 3), else Idle; the shadow hidden either way", "stateladder.cpp:153-202"),
        new("StateRope", 0x9, StateEnterPlay.BodyState, "Rope on a rope in reach (side 3), else Idle; the shadow hidden either way", "statemanager.cpp:1236, 1240-1262"),
        new("StateEmotion", 0xb, StateEnterPlay.Nothing, "Idle at once", "stateemotionidle.cpp:218-220"),
        new("StateHit", 0xd, StateEnterPlay.BodyState, "Hit: HIT with no lift", "statehit.cpp:65-110"),
        new("StateHitNPC", 0xd, StateEnterPlay.NotOwn, "", ""),
        new("StateEmotionIdle", 0xf, StateEnterPlay.Nothing, "Idle at once", "stateemotionidle.cpp:663-666"),
        new("StateSkill", 0x10, StateEnterPlay.Apart, "the cast of the pc's record", "stateskill.cpp:392, 426-429"),
        new("StateSpawn", 0x11, StateEnterPlay.Hide, "hide reason 10 and the shadow off until the timer", "statespawn.cpp:65-75, 98-130"),
        new("StateStun", 0x12, StateEnterPlay.Sequence, "Down_A or Down_B, then the first frame's Stnd_Up_A or Stnd_Up_B", "statestun.cpp:77-113, 123-159"),
        new("StateDash", 0x14, StateEnterPlay.BodyState, "Dash", "state.cpp:1782-1798"),
        new("StateTalk", 0x16, StateEnterPlay.Sequence, "TALK_UP forced with a talk npc whose data has talkAni and the own pc's TALK_UP and TALK_IDLE, then TALK_IDLE while the own chat balloon shows, else LISTEN_IDLE; else IDLE", "statetalk.cpp:101-137, 162-178, 215-222"),
        new("StateRegen", 0x17, StateEnterPlay.NotOwn, "", ""),
        new("StateRevival", 0x18, StateEnterPlay.BodyState, "Revival", "statedead.cpp:325-330"),
        new("StateInteraction", 0x19, StateEnterPlay.Nothing, "sub 0: no branch plays", "stateinteractdata.cpp:257-318"),
        new("StateInteractionNpc", 0x1a, StateEnterPlay.NotOwn, "", ""),
        new("StateSwim", 0x1b, StateEnterPlay.BodyState, "Swim on water", "stateswim.cpp:89-130"),
        new("StateSwimDash", 0x1c, StateEnterPlay.Effect, "StateSwim at once (the body's Swim on water), then the dash effect PC::GetSwimEffectName(0x32) at the pc, kept until the next StateSwimDash stops it", "stateswimdash.cpp:92-99, 102-108, 214-233, pc.cpp:3575-3592"),
        new("StateClimb", 0x1d, StateEnterPlay.Nothing, "Idle at once, then the shadow hidden", "state.cpp:1029-1057, statemanager.cpp:670-676, 720-723"),
        new("StateGlide", 0x1e, StateEnterPlay.Nothing, "Idle at once (StateFall's enter between)", "stateglide.cpp:109-116"),
        new("StateFallDamage", 0x1f, StateEnterPlay.BodyState, "FallDamage", "statefalldamage.cpp:51-70"),
        new("StateHold", 0x20, StateEnterPlay.Nothing, "Idle at once (state 6's enter between)", "stateholddata.cpp:161-169"),
        new("StateRide", 0x21, StateEnterPlay.Apart, "the mount; the shadow off", "statemanager.cpp:1087-1098"),
        new("StateTransform", 0x22, StateEnterPlay.Sequence, "Equip_Change_Mantle_A and the effect CH/Common/Eff_FittingDoll.xml", "statetransform.cpp:54-64"),
        new("StateTransformDoll", 0x22, StateEnterPlay.NotOwn, "", ""),
        new("StatePuppetNpc", 0x23, StateEnterPlay.NotOwn, "", ""),
        new("StateTaxiCall", 0x25, StateEnterPlay.Nothing, "Idle at once (Object_React_B only with data)", "statetaxicalldata.cpp:171-202"),
        new("StateSummon", 0x26, StateEnterPlay.CastingBar, "the casting bar (string 0, SummonRideeDuration); no sequence (sub 0)", "statesummon.cpp:199-272, 375-388"),
        new("StatePvPWinLose", 0x28, StateEnterPlay.Nothing, "sub 0: no win or lose sequence (Pvp 'e''s subs play MODEPVP_WIN / MODEPVP_DEAD, EnterPvpWinLose)", "statepvpwinlose.cpp:46-53"),
        new("StateUseFurniture", 0x29, StateEnterPlay.Later, "", "stateusefurnituredata.cpp:167"),
        new("StateCashCall", 0x2a, StateEnterPlay.Nothing, "Idle at once", "statecashcalldata.cpp:159-162"),
        new("StateGrabTarget", 0x2b, StateEnterPlay.Nothing, "Idle at once", "statedatagrabtarget.cpp:87-89"),
        new("StateRecall", 0x2c, StateEnterPlay.Sequence, "Object_React_A, and the casting bar (string 0x657, RecallCastingTime)", "statesummon.cpp:503-516"),
        new("StateFishing", 0x2d, StateEnterPlay.Nothing, "Idle at once", "statefishingdata.cpp:206-209"),
        new("StateFishingFloat", 0x2e, StateEnterPlay.NotOwn, "", ""),
        new("StatePlayInstrument", 0x2f, StateEnterPlay.Later, "", "stateplayinstrumentdata.cpp:227"),
        new("StateSummonRevive", 0x30, StateEnterPlay.NotOwn, "", ""),
        new("StateTriggerEmotion", 0x35, StateEnterPlay.Nothing, "no emotion list, nothing plays", "stateemotionidle.cpp:384-416"),
        new("StateHomeConvenient", 0x36, StateEnterPlay.Nothing, "Idle at once", "statehomeconvenient.cpp:245-248"),
        new("StateHomemade", 0x37, StateEnterPlay.Later, "", "statehomemade.cpp:151"),
        new("StateAutoInteraction", 0x39, StateEnterPlay.Sequence, "Object_React_C, and the casting bar with the param's defaults (no text, 0 ms)", "stateinteractdata.cpp:684-705, 762-803"),
        new("StateCoupleEmotion", 0x3a, StateEnterPlay.Nothing, "Idle at once", "statecoupleemotion.cpp:422-425"),
        new("StateBanner", 0x3b, StateEnterPlay.Nothing, "Idle at once", "statebannerdata.cpp:115-117"),
        new("StateMicroGameRps", 0x3c, StateEnterPlay.Later, "", "statemicrogame.cpp:262"),
        new("StateReact", 0x3d, StateEnterPlay.Nothing, "no StateReactData: the assert, nothing plays", "statereact.cpp:139-144"),
        new("StateTreeWatering", 0x3e, StateEnterPlay.CastingBar, "the casting bar (string 0, SummonRideeDuration); no sequence (sub 0)", "unit_140ad5280_statetreewateringcontext.cpp:97-125, 193-206"),
        new("StateNurturing", 0x40, StateEnterPlay.Later, "", "statenurturing.cpp:286"),
        new("StateMicroGameCoupleDance", 0x45, StateEnterPlay.Later, "", "statemicrogame.cpp:862"),
        new("StateTriggerFollowNpc", 0x46, StateEnterPlay.Nothing, "Idle at once", "stateemotionidle.cpp:508-510"),
        new("StateWeddingEmotion", 0x47, StateEnterPlay.Later, "", "stateweddingemotiondata.cpp:199"),
        new("StateGoToHome", -1, StateEnterPlay.NotOwn, "", ""),
        new("StateMoveSkill", -1, StateEnterPlay.NotOwn, "", ""),
        new("StateSummonIdle", -1, StateEnterPlay.NotOwn, "", ""),
    };

    /// <summary>StateIdle's id: every exit here enters it (vfunc_59(1)).</summary>
    public const int StateIdle = 1;

    /// <summary>The own pc's class for a state id: the one PC's constructor registers (not an NPC, pet or doll class).</summary>
    public static StateExitRow OfOwnState(int id)
    {
        foreach (var r in Classes)
            if (r.Id == id && r.Cause != StateExitCause.NotOwn) return r;
        return null;
    }

    /// <summary>What the own pc's class for a state id plays when entered with no data.</summary>
    public static StateEnterRow EnterOfOwnState(int id)
    {
        foreach (var r in Enters)
            if (r.Id == id && r.Play != StateEnterPlay.NotOwn) return r;
        return null;
    }

    /// <summary>
    /// The body's move state a server-set state leaves it in, as ChangeState runs the old state's vfunc_5 and the class's
    /// enter with no data: an at-once class and StateCrawl (whose first input step leaves) go on to StateIdle; a
    /// <see cref="StateEnterPlay.BodyState"/> class enters the body's own state; a state run here (<see cref="Enter"/>: its
    /// sequence, hide or timer) stands the body while it runs; a class a controller of its own runs, and StateSwimDash,
    /// leave the body as it is (<see cref="BodyEntry.None"/>). StateClimb goes on to StateIdle with the shadow hidden
    /// (<see cref="BodyEntry.Climb"/>); StateSwimDash's fallback StateSwim is the body's Swim.
    /// </summary>
    public static BodyEntry BodyEntryOf(int id)
    {
        var row = OfOwnState(id);
        if (row == null) return BodyEntry.None;
        if (id == 0x1d) return BodyEntry.Climb;
        if (row.Cause == StateExitCause.Fallback) return BodyEntry.Swim;
        if (row.Cause is StateExitCause.AtOnce or StateExitCause.InputStep) return BodyEntry.Idle;
        if (id is 0x11 or 0x12 or 0x16 or 0x22 or 0x28) return BodyEntry.Idle;
        if (row.Cause == StateExitCause.CastingBar) return BodyEntry.Idle;   // StateSummon / StateTreeWatering's vfunc_3 moves by a zero vector
        if (row.Cause != StateExitCause.Body) return BodyEntry.None;
        return id switch
        {
            0x1 => BodyEntry.Idle,
            0x2 => BodyEntry.Walk,
            0x4 => BodyEntry.Land,
            0x5 => BodyEntry.Fall,
            0x6 => BodyEntry.Jump,
            0x8 => BodyEntry.Ladder,
            0x9 => BodyEntry.Rope,
            0xd => BodyEntry.Hit,
            0x14 => BodyEntry.Dash,
            0x18 => BodyEntry.Revival,
            0x1b => BodyEntry.Swim,
            0x1f => BodyEntry.FallDamage,
            0x35 => BodyEntry.TriggerEmotion,
            _ => BodyEntry.None,
        };
    }

    /// <summary>The cast a casting-bar state's enter started, once per entry; the caller starts it on the bar (UICastingBar
    /// 0x4e72, StartCastingText or StartCasting) with a listener whose OnCastingFinished calls <see cref="CastFinished"/>.</summary>
    public CastRequest Cast { get; private set; }
    /// <summary>The casting-bar state was left (its vfunc_5: FUN_1418e1390 on the bar); the caller stops the bar, once.</summary>
    public bool BarStopped { get; private set; }
    bool castDone;   // m_n48

    /// <summary>StateTransform's sequence and effect (statetransform.cpp:57-63).</summary>
    public const string TransformSequence = "Equip_Change_Mantle_A";
    public const string TransformEffect = "CH/Common/Eff_FittingDoll.xml";
    /// <summary>StateRecall's sequence (statesummon.cpp:504) and StateAutoInteraction's (FUN_1417d36b0,
    /// stateinteractdata.cpp:762-765).</summary>
    public const string RecallSequence = "Object_React_A";
    public const string AutoInteractionSequence = "Object_React_C";
    /// <summary>StateTalk's sequence with no talkable NPC open: vfunc_123(0), the action table's IDLE (statetalk.cpp:133-135).</summary>
    public const string TalkAction = "IDLE";
    /// <summary>StateTalk's action with the talk npc's talkAni and the own pc's TALK_UP and TALK_IDLE: vfunc_123(0x2d), played
    /// with bForce (PlayAnimation(name, 1.0f, true), statetalk.cpp:125-130). Its sequence (ChatUp_A) clamps: it plays once.</summary>
    public const string TalkUpAction = "TALK_UP";
    /// <summary>StateTalk's actions after TALK_UP ended (vfunc_9, statetalk.cpp:162-178): vfunc_123(0x2e) TALK_IDLE while the
    /// own pc's chat balloon is active (PCBase+0xaa8, GetChatBalloonEntity, pc.cpp:1586-1590; UIHudEntity::Entity m_4c,
    /// FUN_140f48d40), else vfunc_123(0x2f) LISTEN_IDLE. Both without bForce, so a frame asking for the one playing keeps it.</summary>
    public const string TalkIdleAction = "TALK_IDLE", ListenIdleAction = "LISTEN_IDLE";
    /// <summary>StateTalk's m_n10: 0x47 TALK_UP plays, 0x48 the talk or listen idle, 0x49 IDLE (no talk animation).</summary>
    public const int TalkPhaseUp = 0x47, TalkPhaseIdle = 0x48, TalkPhaseNone = 0x49;
    /// <summary>The action StateTalk's phase 0x48 plays: TALK_IDLE with the own chat balloon active, else LISTEN_IDLE.</summary>
    public static string TalkIdleOf(bool ownBalloonActive) => ownBalloonActive ? TalkIdleAction : ListenIdleAction;
    /// <summary>
    /// StateTalk::vfunc_4 (statetalk.cpp:101-137): bTalk, UITalkManager::GetTalkNpc() (the npc of the open talk,
    /// uitalkcinematic.cpp:1776) with its NpcData's m_bTalkAni ("talkAni", npcdata.cpp:1463); bFirst and bSecond, the own
    /// pc's sequences of vfunc_123(0x2d) TALK_UP and vfunc_123(0x2e) TALK_IDLE. All three: TALK_UP forced (m_n10 0x47);
    /// else IDLE (0x49).
    /// </summary>
    public static bool ChoosesTalkUp(bool npcTalkAni, bool hasTalkUp, bool hasTalkIdle) => npcTalkAni && hasTalkUp && hasTalkIdle;
    /// <summary>The caller's <see cref="ChoosesTalkUp"/> for the open talk at StateTalk's enter; false when unset.</summary>
    public Func<bool> TalkUp = () => false;
    /// <summary>The own pc's chat balloon is active (UIChatBalloon::Entity m_4c), read by StateTalk's phase 0x48; false when
    /// unset.</summary>
    public Func<bool> OwnBalloonActive = () => false;
    /// <summary>An action's body sequence length in seconds by action name (vfunc_123); null when the body has none.</summary>
    public Func<string, double?> ActionLength;
    /// <summary>StateTalk's phase (m_n10), 0 outside StateTalk.</summary>
    public int TalkPhase { get; private set; }
    /// <summary>StateTalk was left or hit with the own pc (vfunc_5, vfunc_6, statetalk.cpp:142-150, 202-206): the caller sends
    /// NpcTalk 0 when a talk is open (UITalkManager::GetTalking, SendNpcTalkMode0), once.</summary>
    public bool TalkEnds { get; private set; }
    /// <summary>StateSwimDash's fallback state (m_nFallbackState, stateswimdash.cpp:77-83).</summary>
    public const int SwimDashFallback = 0x1b;
    /// <summary>StateSpawn's hide reason (AddHideReason(10), statespawn.cpp:65-75).</summary>
    public const int SpawnHideReason = 10;

    /// <summary>StateSpawn's hide time (statespawn.cpp:73-75): Unk_Random seeded with the object id, its first number
    /// % 300 + 100 ms.</summary>
    public static int SpawnDelayMs(int objectId) => (int)(new Ms2.Quest.QuestRandom(objectId).Next() % 300) + 100;

    /// <summary>The casting bar's caption (a GetStringCommon id) and time in ms. StateAutoInteraction with no data starts the
    /// param's defaults: no caption (<see cref="NoCaption"/>) and 0 ms.</summary>
    public sealed record CastRequest(int StringCommonId, int DurationMs);
    /// <summary>The caption of a cast whose param keeps the ctor's empty string (no GetStringCommon).</summary>
    public const int NoCaption = -1;
    /// <summary>StateRecall's caption (statesummon.cpp:513).</summary>
    public const int RecallCaption = 0x657;
    /// <summary>The constants SummonRideeDuration and RecallCastingTime in ms (Define, constantsblocks.cpp:880, 887: 5000
    /// each when the tables do not set them); the caller sets them from the tables.</summary>
    public int SummonRideeDurationMs = 5000;
    public int RecallCastingTimeMs = 5000;

    /// <summary>StateStun, kept across entries (its sub and kind).</summary>
    public readonly OwnStun Stun = new();
    /// <summary>A body sequence's length in seconds by name; null when the body has none (it plays nothing and never ends,
    /// as PCBase::PlayAnimation of a missing name).</summary>
    public Func<string, double?> SequenceLength;

    /// <summary>The state whose exit runs (0 none).</summary>
    public int State { get; private set; }
    /// <summary>Its cause.</summary>
    public StateExitCause Cause { get; private set; }
    /// <summary>The sequence whose end the exit waits for, if any.</summary>
    public string Sequence { get; private set; }
    /// <summary>Seconds to the exit; below 0 none comes.</summary>
    public double Left { get; private set; } = -1;

    /// <summary>The sequence the running state plays on the body (null none: the body's own), whether it loops, whether it
    /// is an action name (<see cref="TalkAction"/>) rather than a sequence, and a count that rises with each new play.</summary>
    public string Played { get; private set; }
    public bool PlayedLoops { get; private set; }
    public bool PlayedIsAction { get; private set; }
    public int Plays { get; private set; }
    /// <summary>The body hidden for StateSpawn (hide reason 10, the shadow off).</summary>
    public bool Hidden => State == 0x11;
    /// <summary>The body's animation paused (StateStun sub 0x41).</summary>
    public bool Paused => State == 0x12 && Stun.Paused;
    /// <summary>An effect the entry started (StateTransform's), once per entry; the caller takes it.</summary>
    public string Effect { get; private set; }
    /// <summary>The face StateStun's enter plays (FUN_14166ef10("Stun", 1, 0, false), statestun.cpp:112), once per enter.</summary>
    public string Face { get; private set; }
    /// <summary>StateSwimDash's enter entered its fallback (<see cref="SwimDashFallback"/>) at once, once; the caller enters
    /// it before the dash effect starts.</summary>
    public int Fallback { get; private set; }
    /// <summary>StateSwimDash's enter starts the dash effect (FUN_1417fbec0, stateswimdash.cpp:221-233: the old one stopped,
    /// PC::GetSwimEffectName(0x32) started at the pc and kept), once.</summary>
    public bool DashEffect { get; private set; }
    /// <summary>The buff's stun animation for StateStun sub 0x46 (m_motion.m_strStunAnimation).</summary>
    public string StunAnimation { get; private set; }

    /// <summary>The running state's input step reads no move key: StateStun's and StateTalk's are empty (statestun.cpp:197-199,
    /// statetalk.cpp:209-211); StateSpawn, StateTransform and StatePvPWinLose keep State's, which only casts a queued skill
    /// (state.cpp:634-640).</summary>
    public bool HoldsBody => State is 0x11 or 0x12 or 0x16 or 0x22 or 0x28;

    /// <summary>
    /// The server entered <paramref name="id"/> (Me.StateEntered). CStateManager::ChangeState (statemanager.cpp:297) keeps
    /// the running state when it is asked again with no sub (State::vfunc_25, state.cpp:751-756; none of these classes
    /// overrides it but StateTriggerFollowNpc, which leaves at once). Returns true when the state is left at once.
    /// </summary>
    public bool Enter(int id, int objectId)
    {
        if (id == State && State != 0) return false;
        Clear();
        var row = OfOwnState(id);
        if (row == null) return false;
        switch (row.Cause)
        {
            case StateExitCause.AtOnce:
                Cause = row.Cause;
                return true;
            case StateExitCause.Fallback:
                // StateMoveSkill::vfunc_4: vfunc_38 fails, FUN_1417df370 enters m_nFallbackState now; then FUN_1417fbec0
                // and SendRequestCube (stateswimdash.cpp:92-99)
                Cause = row.Cause;
                Fallback = SwimDashFallback;
                DashEffect = true;
                return true;
            case StateExitCause.InputStep:
                Left = 0;
                break;
            case StateExitCause.Timer:
                Left = SpawnDelayMs(objectId) / 1000.0;
                break;
            case StateExitCause.SequenceEnd when id == 0x12:
                Stun.Enter(null, null);
                Face = OwnStun.Face;
                TakeStunPlay();
                Sequence = Stun.Sub == OwnStun.SubStandUp ? Stun.Played : Stun.Set?[2 + (Stun.Kind == 6 ? 5 : 0)];
                Left = Sequence != null ? SequenceLength?.Invoke(Sequence) ?? -1 : -1;
                break;
            case StateExitCause.SequenceEnd:
                Play(TransformSequence, false, false);
                Effect = TransformEffect;
                Sequence = TransformSequence;
                Left = SequenceLength?.Invoke(Sequence) ?? -1;
                break;
            case StateExitCause.NoSelfExit when id is 0x28 or 0x16:
                if (id == 0x16)
                {
                    // the held item went first (the caller's HeldItem.UserState, statetalk.cpp:121-123)
                    if (TalkUp())
                    {
                        TalkPhase = TalkPhaseUp;
                        Play(TalkUpAction, false, true);
                        Left = ActionLength?.Invoke(TalkUpAction) ?? -1;
                    }
                    else
                    {
                        TalkPhase = TalkPhaseNone;
                        Play(TalkAction, true, true);
                    }
                }
                break;
            case StateExitCause.CastingBar:
                // the held item goes first (PC::SendRequestCube, the caller's HeldItem.UserState), then the sequence and the bar
                switch (id)
                {
                    case 0x26: Cast = new(0, SummonRideeDurationMs); break;                   // statesummon.cpp:204, 259-271
                    case 0x2c:                                                              // statesummon.cpp:503-516
                        Play(RecallSequence, false, false);
                        Cast = new(RecallCaption, RecallCastingTimeMs);
                        break;
                    case 0x39:                                                              // stateinteractdata.cpp:688-704
                        Play(AutoInteractionSequence, false, false);
                        Cast = new(NoCaption, 0);
                        break;
                    case 0x3e: Cast = new(0, SummonRideeDurationMs); break;                   // statetreewateringcontext.cpp:101-125
                }
                break;
            default:
                return false;
        }
        State = id; Cause = row.Cause;
        return false;
    }

    /// <summary>
    /// The stun buff's entry this frame (AdditionalCliList::FUN_1409713b0): vfunc_59(0x12, StateStunData(stun), sub). A
    /// running StateStun only takes the sub (State::vfunc_25 -> StateStun::vfunc_2); else ChangeState sets the sub, then
    /// StateStun::vfunc_4 runs with the stun as the kind. Returns true when StateStun was entered now.
    /// </summary>
    public bool EnterBuffStun(int stun, int sub, string stunAnimation)
    {
        StunAnimation = stunAnimation;
        if (State == 0x12) { Stun.SetSub(sub); return false; }
        Clear();
        Stun.SetSub(sub);
        Stun.Enter(stun, stunAnimation);
        Face = OwnStun.Face;
        TakeStunPlay();
        State = 0x12; Cause = StateExitCause.SequenceEnd;
        return true;
    }

    /// <summary>StatePvPWinLose's subs from Pvp 'e' (scenestategame.cpp:5006-5013): 0x73 win, 0x74 lose.</summary>
    public const int PvpWinSub = 0x73, PvpLoseSub = 0x74;
    /// <summary>The pcactiondata types the subs play (statepvpwinlose.cpp:46-53): 0x41 MODEPVP_WIN, 0x40 MODEPVP_DEAD.</summary>
    public const int PvpWinAction = 0x41, PvpLoseAction = 0x40;
    /// <summary>PCBase::FUN_141679170(type, index): the body's sequence of the action list's index-th name; null when the list
    /// has no such entry or the body lacks it (PlayAnimation then plays nothing).</summary>
    public Func<int, int, string> ActionNameAt;

    /// <summary>
    /// Pvp 'e' (scenestategame.cpp:5006-5013): MyPC::vfunc_59(0x28, 0, sub, 0). The caller has entered the state (PC::vfunc_124
    /// and ChangeState's checks). A running StatePvPWinLose keeps running with nothing replayed (State::vfunc_25, state.cpp:751-756,
    /// only sets the sub); else StatePvPWinLose::vfunc_4 (statepvpwinlose.cpp:37-54) plays action 0x41 (sub 0x73) or 0x40 (sub
    /// 0x74) at the index of the local PC's CPvPInfo team, once at speed 1; EndOfSequence (65-70) enters StateIdle when it
    /// ends. A sequence the body lacks plays nothing and the state stays. Returns true when the state was entered now.
    /// </summary>
    public bool EnterPvpWinLose(int sub, int team)
    {
        if (State == 0x28) return false;
        Clear();
        int action = sub == PvpWinSub ? PvpWinAction : sub == PvpLoseSub ? PvpLoseAction : 0;
        string name = action != 0 ? ActionNameAt?.Invoke(action, team) : null;
        if (name != null)
        {
            Play(name, false, false);
            Sequence = name;
            Left = SequenceLength?.Invoke(name) ?? -1;
        }
        State = 0x28; Cause = StateExitCause.NoSelfExit;
        return true;
    }

    /// <summary>
    /// One frame of the running state. <paramref name="current"/> is Me.State: another state (the body's, the server's)
    /// has replaced it and nothing runs. StateStun runs its frame (<paramref name="stunned"/>: a buff holds it,
    /// AdditionalCliList::FUN_1409709b0) and its sequence ends. Returns true when the state's own exit enters StateIdle now.
    /// </summary>
    public bool Advance(double dt, int current, bool crawlKeyDown = false, bool stunned = false)
    {
        if (State == 0) return false;
        if (current != State) { Clear(); return false; }
        if (State == 0x12) return AdvanceStun(dt, stunned);
        if (State == 0x16) { AdvanceTalk(dt); return false; }
        if (Cause == StateExitCause.CastingBar)
        {
            // StateSummon / StateRecall / StateTreeWatering::vfunc_8 and StateAutoInteraction::vfunc_3: m_n48 enters StateIdle
            if (!castDone) return false;
        }
        else if (Cause == StateExitCause.InputStep) { if (crawlKeyDown) return false; }
        else
        {
            if (Left < 0) return false;
            Left -= dt;
            if (Left > 0) return false;
        }
        Clear();
        return true;
    }

    /// <summary>The cast to start on the bar, once.</summary>
    public CastRequest TakeCast()
    {
        var c = Cast;
        Cast = null;
        return c;
    }

    /// <summary>The bar was stopped for a left casting-bar state, once.</summary>
    public bool TakeBarStopped()
    {
        bool b = BarStopped;
        BarStopped = false;
        return b;
    }

    /// <summary>OnCastingFinished of the running casting-bar state (statesummon.cpp:397-400, 584-587,
    /// statetreewateringcontext.cpp:216-219, stateinteractdata.cpp:759): m_n48 = 1. A finish for a state no longer running
    /// does nothing (the bar's listener was the state left).</summary>
    public void CastFinished()
    {
        if (Cause == StateExitCause.CastingBar && State != 0) castDone = true;
    }

    /// <summary>
    /// State::vfunc_6 of the running state, which PC's hit reaction calls (pc.cpp:952, 1075) after any push state: StateSummon,
    /// StateRecall and StateTreeWatering enter StateIdle for the own pc (statesummon.cpp:390-394, 577-581,
    /// statetreewateringcontext.cpp:209-213); StateAutoInteraction keeps State's empty one. Returns true when it leaves.
    /// </summary>
    public bool Hit()
    {
        if (State == 0x16) { TalkEnds = true; return false; }   // StateTalk::vfunc_6: SendNpcTalkMode0, the state kept
        if (State is not (0x26 or 0x2c or 0x3e)) return false;
        Clear();
        return true;
    }

    /// <summary>The effect the entry started, once.</summary>
    public string TakeEffect()
    {
        var e = Effect;
        Effect = null;
        return e;
    }

    /// <summary>The face the enter played, once.</summary>
    public string TakeFace()
    {
        var f = Face;
        Face = null;
        return f;
    }

    /// <summary>The fallback state StateSwimDash's enter entered (0 none), once.</summary>
    public int TakeFallback()
    {
        int f = Fallback;
        Fallback = 0;
        return f;
    }

    /// <summary>Whether StateSwimDash's enter started its dash effect, once.</summary>
    public bool TakeDashEffect()
    {
        bool d = DashEffect;
        DashEffect = false;
        return d;
    }

    /// <summary>Whether StateTalk was left or hit, once (<see cref="TalkEnds"/>).</summary>
    public bool TakeTalkEnds()
    {
        bool t = TalkEnds;
        TalkEnds = false;
        return t;
    }

    /// <summary>
    /// StateTalk's frame: EndOfSequence (statetalk.cpp:215-222) moves 0x47 to 0x48 when TALK_UP's sequence ends; vfunc_9
    /// (162-178) in 0x48 asks for TALK_IDLE or LISTEN_IDLE each frame, which starts only when it is not the one playing.
    /// STILL-OPEN: the phases 0x4a and 0x4b of SelectDialogueBranch (only the enchant dialog's, uiitemenchantdialog.cpp:863)
    /// and their CommonResource effects (vfunc_9, 179-197).
    /// </summary>
    void AdvanceTalk(double dt)
    {
        if (TalkPhase == TalkPhaseUp)
        {
            if (Left < 0) return;
            Left -= dt;
            if (Left > 0) return;
            Left = -1;
            TalkPhase = TalkPhaseIdle;
        }
        if (TalkPhase != TalkPhaseIdle) return;
        string a = TalkIdleOf(OwnBalloonActive());
        if (a != Played) Play(a, true, true);
    }

    bool AdvanceStun(double dt, bool stunned)
    {
        int plays = Stun.Plays;
        if (Stun.Frame(stunned)) { Clear(); return true; }
        if (Stun.Plays != plays) { TakeStunPlay(); return false; }   // a sequence started this frame runs from the next
        if (Left < 0) return false;
        Left -= dt;
        if (Left > 0) return false;
        plays = Stun.Plays;
        if (Stun.SequenceEnd(StunAnimation)) { Clear(); return true; }
        if (Stun.Plays != plays) TakeStunPlay();
        else Left = -1;
        return false;
    }

    void TakeStunPlay()
    {
        if (Stun.Played == null) { Left = -1; return; }
        Play(Stun.Played, Stun.Loops, false);
        Sequence = Stun.Played;
        Left = Stun.Loops ? -1 : SequenceLength?.Invoke(Sequence) ?? -1;
    }

    void Play(string name, bool loop, bool action)
    {
        Played = name;
        PlayedLoops = loop;
        PlayedIsAction = action;
        Plays++;
    }

    void Clear()
    {
        if (State == 0x12) Stun.Leave();
        if (State == 0x16) TalkEnds = true;   // StateTalk::vfunc_5
        if (Cause == StateExitCause.CastingBar && State != 0) BarStopped = true;   // vfunc_5: FUN_1418e1390
        castDone = false;
        Cast = null;
        State = 0; Sequence = null; Left = -1; Played = null; PlayedLoops = false; PlayedIsAction = false; Effect = null;
        Face = null; Fallback = 0; DashEffect = false; TalkPhase = 0;
    }
}
