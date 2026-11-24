using System;

namespace Ms2.Ui;

/// <summary>
/// StateStun (statestun.cpp) of the own pc: the one state object the pc's state manager keeps, so its sub (m_n10, set only by
/// a nonzero sub, State::vfunc_2 state.cpp:580-583 and StateStun::vfunc_2 statestun.cpp:202-210), its kind (m_nKind, set only by a
/// StateStunData, statestun.cpp:81-82) and its lie set (m_pAnims) stay from one entry to the next. The server's UserState enters it with
/// no data and sub 0; the stun buff (AdditionalCliList::FUN_1409713b0, additionalcli_2.cpp:786-833) enters it each frame
/// with StateStunData(stun) and the stun's sub, which a later entry of the running state only sets (State::vfunc_25,
/// state.cpp:751-756).
/// </summary>
public sealed class OwnStun
{
    /// <summary>The phase sequences of the "A" and "B" lie sets and the plain set (statestun.cpp:56-58), by
    /// <see cref="PhaseIndex"/>.</summary>
    public static readonly string[] SetA = { "Down_A", "Down_Idle_A", "Stnd_Up_A", "Snare_A", "Vomit_A", "Down_C", "Down_Idle_C", "Stnd_Up_C", "StunFrozen_A", "Stuck_A" };
    public static readonly string[] SetB = { "Down_B", "Down_Idle_B", "Stnd_Up_B", "Snare_A", "Vomit_A", "Down_C", "Down_Idle_C", "Stnd_Up_C", "StunFrozen_A", "Stuck_A" };
    public static readonly string[] SetPlain = { "Stun_A", "Stun_A", "Stun_A", "Stun_A", "Stun_A", "Stun_A", "Stun_A", "Stun_A", "Stun_A", "Stun_A" };

    /// <summary>The subs: 0x3d lie down, 0x3e lie, 0x3f stand up, 0x40 the plain set, 0x41 the paused body, 0x42 snare,
    /// 0x43 vomit, 0x44 frozen, 0x45 stuck, 0x46 the buff's own stun animation.</summary>
    public const int SubDown = 0x3d, SubLie = 0x3e, SubStandUp = 0x3f, SubPlain = 0x40, SubPause = 0x41, SubSnare = 0x42,
        SubVomit = 0x43, SubFrozen = 0x44, SubStuck = 0x45, SubOwnAnimation = 0x46;

    /// <summary>
    /// The sub the stun buff enters with (additionalcli_2.cpp:806-828): a stun of 2 or 6 lies the pc down (0x3d), or
    /// lying already (0x3e) for an effect the field entry's list brought (m_b40, DecodeListPacket :730); 3 to 8 and 99
    /// their own; any other the plain set (0x40). An NPC owner takes the plain set for 2 and 6 too.
    /// </summary>
    public static int BuffSub(int stun, bool ownerIsPc, bool fromList) => stun switch
    {
        2 or 6 => ownerIsPc ? (fromList ? SubLie : SubDown) : SubPlain,
        3 => SubPause,
        4 => SubSnare,
        5 => SubVomit,
        7 => SubFrozen,
        8 => SubStuck,
        99 => SubOwnAnimation,
        _ => SubPlain,
    };

    /// <summary>StateGrabTarget, the one state class whose vfunc_17 is 0 (statedatagrabtarget.cpp:164-167; State::vfunc_17,
    /// state.cpp:698-701, is 1).</summary>
    public const int StateGrabTarget = 0x2b;
    /// <summary>StateHold, the state PC::FUN_141660120 asks for (pc.cpp:2970).</summary>
    public const int StateHold = 0x20;

    /// <summary>
    /// AdditionalCliList::FUN_1409713b0's checks before it looks for a stun (additionalcli_2.cpp:786-797): the owner has health
    /// (FUN_141670a00(4), the current Health stat), its current state's vfunc_17 holds (every state but StateGrabTarget) and
    /// it is not held by a riding PC with an event buff of type 2 (<see cref="HeldByRidingPc"/>). The portrait copy's check
    /// (IsOwnerPortraitPC) is not the field pc's.
    /// </summary>
    public static bool BuffMayEnter(long health, int currentState, bool heldByRidingPc) =>
        health != 0 && currentState != StateGrabTarget && !heldByRidingPc;

    /// <summary>
    /// PC::FUN_141660120 (pc.cpp:2967-2988): the pc is in StateHold, its holder (StateHold::FUN_1417cd0d0, the hold data's
    /// target id, found by WorldService::FUN_141bd1200) is a PC (class 0x6200003 or 0x6200004) that rides a mount
    /// (GetRidee) and whose buffs have an enabled event buff of type 2 (HasEnabledWithEventBuffType(2)).
    /// </summary>
    public static bool HeldByRidingPc(int currentState, bool holderIsPc, bool holderRides, bool holderEventBuff2) =>
        currentState == StateHold && holderIsPc && holderRides && holderEventBuff2;

    /// <summary>The face StateStun::vfunc_4 plays at its end (FUN_14166ef10("Stun", 1, 0, false), statestun.cpp:112): not
    /// forced, so a face already on "Stun" does not start over.</summary>
    public const string Face = "Stun";

    /// <summary>The CRT rand(): rand() % 2 != 0 takes the "A" set (statestun.cpp:93-96).</summary>
    public Func<int> Rand = () => System.Random.Shared.Next(0x8000);

    /// <summary>m_n10.</summary>
    public int Sub { get; private set; }
    /// <summary>m_nKind: the stun of the last StateStunData; 6 takes the C lie phases.</summary>
    public int Kind { get; private set; }
    string[] anims;

    /// <summary>The sequence PlayAnimation was last asked for (null none yet), whether it loops, and a count that rises
    /// with each ask (a new ask of the same name starts it over).</summary>
    public string Played { get; private set; }
    public bool Loops { get; private set; }
    public int Plays { get; private set; }
    /// <summary>GameObject::FUN_14123d7a0("stun", 1) (gameobject.cpp:902-909): the body's animation paused, for sub 0x41.</summary>
    public bool Paused { get; private set; }

    /// <summary>The lie set this entry plays from (null for 0x41 and 0x46).</summary>
    public string[] Set => anims;

    /// <summary>StateStun::vfunc_2 (statestun.cpp:202-210): a new sub; leaving 0x41 lets the body play on.</summary>
    public void SetSub(int sub)
    {
        if (Sub == sub) return;
        if (Sub == SubPause) Paused = false;
        Sub = sub;
    }

    /// <summary>
    /// StateStun::vfunc_4 (statestun.cpp:77-113), after ChangeState's vfunc_2(sub): the kind from the data, the set by the sub
    /// (the plain set for 0x40, none for 0x41 and 0x46, else "A" or "B" by rand()), then 0x41 pauses the body, 0x46 plays
    /// the buff's stun animation and the others their phase; last the "Stun" face (<see cref="Face"/>, which
    /// <see cref="OwnStateExit.TakeFace"/> hands the caller). <paramref name="kind"/> is null without StateStunData.
    /// </summary>
    public void Enter(int? kind, string buffAnimation)
    {
        if (kind is { } k) Kind = k;
        anims = Sub switch
        {
            SubPlain => SetPlain,
            SubPause or SubOwnAnimation => null,
            _ => Rand() % 2 != 0 ? SetA : SetB,
        };
        if (Sub == SubPause) Paused = true;
        else if (Sub == SubOwnAnimation) Play(buffAnimation ?? "", false);
        else if (anims != null) Play(anims[PhaseIndex()], false);
    }

    /// <summary>StateStun::vfunc_5 (statestun.cpp:116-120): the body plays on.</summary>
    public void Leave() => Paused = false;

    /// <summary>
    /// StateStun::vfunc_3 (statestun.cpp:123-159), each frame: nothing while a buff stuns the pc (FUN_1409709b0); else 0x40
    /// and 0x42 to 0x46 go to StateIdle, 0x41 plays on and goes to StateIdle, and the lie subs stand up once (0x3f, its
    /// phase played). Returns true when it enters StateIdle.
    /// </summary>
    public bool Frame(bool stunned)
    {
        if (stunned) return false;
        switch (Sub)
        {
            case SubPlain: case SubSnare: case SubVomit: case SubFrozen: case SubStuck: case SubOwnAnimation:
                return true;
            case SubPause:
                Paused = false;
                return true;
            default:
                if (Sub != SubStandUp)
                {
                    Sub = SubStandUp;
                    anims ??= Rand() % 2 != 0 ? SetA : SetB;
                    Play(anims[PhaseIndex()], false);
                }
                return false;
        }
    }

    /// <summary>
    /// StateStun::EndOfSequence (statestun.cpp:162-195) for the played sequence: 0x3f, 0x42 and 0x43 go to StateIdle; 0x3d
    /// becomes 0x3e and, as 0x44 and 0x45, plays its phase again; 0x46 plays the buff's stun animation looped. Returns true
    /// when it enters StateIdle.
    /// </summary>
    public bool SequenceEnd(string buffAnimation)
    {
        switch (Sub)
        {
            case SubStandUp: case SubSnare: case SubVomit:
                return true;
            case SubDown: case SubFrozen: case SubStuck:
                if (Sub == SubDown) Sub = SubLie;
                anims ??= Rand() % 2 != 0 ? SetA : SetB;
                Play(anims[PhaseIndex()], false);
                return false;
            case SubOwnAnimation:
                Play(buffAnimation ?? "", true);
                return false;
        }
        return false;
    }

    /// <summary>FUN_1417f7140 (statestun.cpp:212-240): the phase of the sub; kind 6 lies on the C phases.</summary>
    public int PhaseIndex() => Sub switch
    {
        SubDown => Kind == 6 ? 5 : 0,
        SubLie => Kind == 6 ? 6 : 1,
        SubStandUp => Kind == 6 ? 7 : 2,
        SubSnare => 3,
        SubVomit => 4,
        SubFrozen => 8,
        SubStuck => 9,
        _ => 0,
    };

    void Play(string name, bool loop)
    {
        Played = name;
        Loops = loop;
        Plays++;
    }
}
