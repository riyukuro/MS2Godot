using System;
using System.Collections.Generic;

namespace Ms2.Ui;

/// <summary>How the client leaves one of its state classes when it was entered with no state data, as OnUserState enters
/// it (scenestategame.cpp:5759-5770, vfunc_59(state, 0, 0, 0)), and where the port runs that exit.</summary>
public enum StateExitCause
{
    /// <summary>The enter or the first frame finds no state data and goes on to StateIdle.</summary>
    AtOnce,
    /// <summary>The input step leaves when a key is not held.</summary>
    InputStep,
    /// <summary>A timer of the state's own.</summary>
    Timer,
    /// <summary>The end of the sequence the state plays.</summary>
    SequenceEnd,
    /// <summary>No exit of the state's own: input, a cancel or a packet only.</summary>
    NoSelfExit,
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

/// <summary>
/// The own pc's exit from a state the server put it into (OnUserState, no state data) for the states the body has no move
/// state for. The pc's classes are the ones PC's constructor registers (pc.cpp:668-721). Each frame the client's own exit
/// is run: at once, on the input step, on a timer or at the end of the played sequence; the states whose input step reads
/// no move key hold the body while they run.
/// STILL-OPEN: the played sequence itself (Stnd_Up_A / B, Equip_Change_Mantle_A) and StateSpawn's hide (AddHideReason 10)
/// are not shown on the body; a stun buff holding StateStun (AdditionalCliList::FUN_1409709b0) and an earlier stun's sub
/// and kind (m_n10, m_nKind kept across entries) are not modelled, as the port keeps no buff motion.
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
        new("StateLadder", 0x8, StateExitCause.Body, "stateladder.cpp:343", "the body's Ladder runs the exit"),
        new("StateRope", 0x9, StateExitCause.Body, "statemanager.cpp:1472", "the body's Rope runs the exit"),
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
        new("StateSwimDash", 0x1c, StateExitCause.NoSelfExit, "", "input, cancel or a packet only"),
        new("StateClimb", 0x1d, StateExitCause.Body, "state.cpp:1077", "the body's Climb runs the exit"),
        new("StateGlide", 0x1e, StateExitCause.AtOnce, "stateglide.cpp:111-115, statemanager.cpp:770-775", "no StateGlideData: the skill row is not found (StateMoveSkill's fallback state 0), then the enter goes on to StateIdle"),
        new("StateFallDamage", 0x1f, StateExitCause.Body, "statefalldamage.cpp:108", "the body's FallDamage runs the exit"),
        new("StateHold", 0x20, StateExitCause.Body, "", "the body's Hold runs the exit"),
        new("StateRide", 0x21, StateExitCause.Apart, "", "the mount"),
        new("StateTransform", 0x22, StateExitCause.SequenceEnd, "statetransform.cpp:63, 79-86", "Equip_Change_Mantle_A plays and its end enters StateIdle"),
        new("StateTransformDoll", 0x22, StateExitCause.NotOwn, "fittingdollslot_1.cpp:146", "the fitting doll's 0x22 (the pc registers StateTransform, pc.cpp:698)"),
        new("StatePuppetNpc", 0x23, StateExitCause.NotOwn, "stateidle.cpp:1034", "an NPC, pet or doll state"),
        new("StateTaxiCall", 0x25, StateExitCause.Apart, "statetaxicalldata.cpp:295", "TaxiCalling (its own LeaveState)"),
        new("StateSummon", 0x26, StateExitCause.Apart, "", "rideSummon (its Tick)"),
        new("StatePvPWinLose", 0x28, StateExitCause.NoSelfExit, "statepvpwinlose.cpp:46-53, 65-70", "sub 0: no win or lose sequence plays, so the sequence end never leaves; the input step is State's (a queued skill only)"),
        new("StateUseFurniture", 0x29, StateExitCause.Later, "stateusefurnituredata.cpp:281", "its feature (furniture/home) is under Later; client exit: the frame at stateusefurnituredata.cpp:281 -> 1"),
        new("StateCashCall", 0x2a, StateExitCause.NoSelfExit, "", "input, cancel or a packet only"),
        new("StateGrabTarget", 0x2b, StateExitCause.AtOnce, "statedatagrabtarget.cpp:87-89", "no StateDataGrabTarget: the enter goes on to StateIdle"),
        new("StateRecall", 0x2c, StateExitCause.NoSelfExit, "", "input, cancel or a packet only"),
        new("StateFishing", 0x2d, StateExitCause.NoSelfExit, "", "input, cancel or a packet only"),
        new("StateFishingFloat", 0x2e, StateExitCause.NotOwn, "cubecraft_1.cpp:928", "the fishing float's state (CFishingGuideObject), not the pc's"),
        new("StatePlayInstrument", 0x2f, StateExitCause.Later, "stateplayinstrumentdata.cpp:427", "its feature (instruments) is under Later; client exit: the frame at stateplayinstrumentdata.cpp:427 -> 5"),
        new("StateSummonRevive", 0x30, StateExitCause.NotOwn, "", "an NPC, pet or doll state"),
        new("StateTriggerEmotion", 0x35, StateExitCause.Body, "stateemotionidle.cpp:438", "the body's TriggerEmotion runs the exit"),
        new("StateHomeConvenient", 0x36, StateExitCause.NoSelfExit, "", "input, cancel or a packet only"),
        new("StateHomemade", 0x37, StateExitCause.Later, "statehomemade.cpp:239", "its feature (furniture/home) is under Later; client exit: the frame at statehomemade.cpp:239 -> 1"),
        new("StateAutoInteraction", 0x39, StateExitCause.Apart, "stateinteractdata.cpp:741", "the interaction casts (InteractKey)"),
        new("StateCoupleEmotion", 0x3a, StateExitCause.NoSelfExit, "", "input, cancel or a packet only"),
        new("StateBanner", 0x3b, StateExitCause.NoSelfExit, "", "input, cancel or a packet only"),
        new("StateMicroGameRps", 0x3c, StateExitCause.Later, "statemicrogame.cpp:262", "its feature (minigames) is under Later; client exit: the sequence end at statemicrogame.cpp:262 -> 1"),
        new("StateReact", 0x3d, StateExitCause.Apart, "statereact.cpp:271", "reactState (SequenceEnded = EndOfSequence)"),
        new("StateTreeWatering", 0x3e, StateExitCause.NoSelfExit, "", "input, cancel or a packet only"),
        new("StateNurturing", 0x40, StateExitCause.Later, "statenurturing.cpp:286", "its feature (nurturing) is under Later; client exit: the frame at statenurturing.cpp:286 -> 1"),
        new("StateMicroGameCoupleDance", 0x45, StateExitCause.Later, "statemicrogame.cpp:862", "its feature (minigames) is under Later; client exit: statemicrogame.cpp:862 -> 1"),
        new("StateTriggerFollowNpc", 0x46, StateExitCause.AtOnce, "stateemotionidle.cpp:508-510", "no StateDataTriggerFollowNpc: the enter goes on to StateIdle"),
        new("StateWeddingEmotion", 0x47, StateExitCause.Later, "stateweddingemotiondata.cpp:199", "its feature (wedding) is under Later; client exit: the sequence end at stateweddingemotiondata.cpp:199 -> 1"),
        new("StateGoToHome", -1, StateExitCause.NotOwn, "", "no object registers it"),
        new("StateMoveSkill", -1, StateExitCause.NotOwn, "pc.cpp:668-721", "not registered on the pc: StateGlide's base only"),
        new("StateSummonIdle", -1, StateExitCause.NotOwn, "", "an NPC, pet or doll state"),
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

    /// <summary>StateStun's sequences without state data: the "A" or "B" lie set's stand-up (statestun.cpp:56-57, phase
    /// index 2 of sub 0x3f with kind 0, FUN_1417f7140 :226-231).</summary>
    public static readonly string[] StunStandUp = { "Stnd_Up_A", "Stnd_Up_B" };
    /// <summary>StateTransform's sequence (statetransform.cpp:63).</summary>
    public const string TransformSequence = "Equip_Change_Mantle_A";

    /// <summary>StateSpawn's hide time (statespawn.cpp:73-75): Unk_Random seeded with the object id, its first number
    /// % 300 + 100 ms.</summary>
    public static int SpawnDelayMs(int objectId) => (int)(new Ms2.Quest.QuestRandom(objectId).Next() % 300) + 100;

    /// <summary>The CRT rand() for StateStun's set (rand() % 2 != 0 picks "A", statestun.cpp:93-96).</summary>
    public Func<int> Rand = () => System.Random.Shared.Next(0x8000);
    /// <summary>A body sequence's length in seconds by name; null when the body has none (it plays nothing and never ends,
    /// as PCBase::PlayAnimation of a missing name).</summary>
    public Func<string, double?> SequenceLength;

    /// <summary>The state whose exit runs (0 none).</summary>
    public int State { get; private set; }
    /// <summary>Its cause.</summary>
    public StateExitCause Cause { get; private set; }
    /// <summary>The sequence it plays, if any.</summary>
    public string Sequence { get; private set; }
    /// <summary>Seconds to the exit; below 0 none comes.</summary>
    public double Left { get; private set; } = -1;

    /// <summary>The running state's input step reads no move key: StateStun's is empty (statestun.cpp:197-199); StateSpawn,
    /// StateTransform and StatePvPWinLose keep State's, which only casts a queued skill (state.cpp:634-640).</summary>
    public bool HoldsBody => State is 0x11 or 0x12 or 0x22 or 0x28;

    /// <summary>
    /// The server entered <paramref name="id"/> (Me.StateEntered). CStateManager::ChangeState (statemanager.cpp:297) keeps
    /// the running state when it is asked again with no sub (State::vfunc_25, state.cpp:751-756; none of these classes
    /// overrides it but StateTriggerFollowNpc, which leaves at once). Returns true when the state is left at once.
    /// </summary>
    public bool Enter(int id, int objectId)
    {
        if (id == State && State != 0) return false;
        State = 0; Sequence = null; Left = -1;
        var row = OfOwnState(id);
        if (row == null) return false;
        switch (row.Cause)
        {
            case StateExitCause.AtOnce:
                Cause = row.Cause;
                return true;
            case StateExitCause.InputStep:
                Left = 0;
                break;
            case StateExitCause.Timer:
                Left = SpawnDelayMs(objectId) / 1000.0;
                break;
            case StateExitCause.SequenceEnd:
                Sequence = id == 0x12 ? StunStandUp[Rand() % 2 != 0 ? 0 : 1] : TransformSequence;
                Left = SequenceLength?.Invoke(Sequence) ?? -1;
                break;
            case StateExitCause.NoSelfExit when id == 0x28:
                break;
            default:
                return false;
        }
        State = id; Cause = row.Cause;
        return false;
    }

    /// <summary>
    /// One frame of the running state. <paramref name="current"/> is Me.State: another state (the body's, the server's)
    /// has replaced it and nothing runs. Returns true when the state's own exit enters StateIdle now.
    /// </summary>
    public bool Advance(double dt, int current, bool crawlKeyDown = false)
    {
        if (State == 0) return false;
        if (current != State) { State = 0; Sequence = null; Left = -1; return false; }
        if (Cause == StateExitCause.InputStep) { if (crawlKeyDown) return false; }
        else
        {
            if (Left < 0) return false;
            Left -= dt;
            if (Left > 0) return false;
        }
        State = 0; Sequence = null; Left = -1;
        return true;
    }
}
