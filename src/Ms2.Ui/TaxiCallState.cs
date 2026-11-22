using System;
using Ms2.Net.Packets;
using Ms2.Net.Wire;

namespace Ms2.Ui;

/// <summary>
/// StateTaxiCall (actor state 0x25, client/ms2/statetaxicalldata.cpp) for the local player: the phases of a taxi ride. The state
/// starts in phase 0x65 for the call duration (TaxiCallDuration, or TaxiCallBestDriverDuration from the best driver level), and
/// vfunc_3 (@0x1417fd690, :266-310) moves it on whenever the elapsed time reaches the end: 0x65 -> 0x66 (+1.9 s), 0x66 -> 0x67
/// (+0.45 s), 0x67 -> 0x68 (+0.45 s), 0x68 -> 0x69 (+1.0 s), 0x69 -> 0x6a (+3.0 s) and, in 0x69, RequestTaxi mode 1 goes out
/// once (the own player only); at the end of 0x6a the state returns to idle (state 1). The server moves the player.
/// STILL-OPEN: the animation Object_React_B / suprise_A (m_bSurprise = 0.9 &lt; FUN_140768090()), the call / arrive / leave taxi
/// effects (SpawnTaxiEffect @0x1417fdc00), the casting bar 0x4e72 text 0x64b, the s_strTaxiCall sound and the camera / cube
/// calls (vfunc_23, SendRequestCube) are not ported; the phase times, the send and the idle return are.
/// </summary>
public sealed class TaxiCallState
{
    /// <summary>The actor state id (TaxiStationAction.StateTaxiCall).</summary>
    public const int StateId = TaxiStationAction.StateTaxiCall;
    public const int Call = 0x65, Arrive = 0x66, Board = 0x67, Ride = 0x68, Leave = 0x69, Away = 0x6a;

    /// <summary>The packet sink (FUN_140710590).</summary>
    public Action<IOutPacket> Send;

    /// <summary>m_n10: the phase, 0 before <see cref="Begin"/>.</summary>
    public int Phase { get; private set; }
    /// <summary>The state returned to idle (vfunc_3's default case, or vfunc_4 without data).</summary>
    public bool Finished { get; private set; }
    /// <summary>StateTaxiCallData::m_nTaxiId, the station field to ride to.</summary>
    public int TaxiId { get; private set; }
    /// <summary>FUN_1417b8560: the owner is the local player (the send and the casting bar apply to it only).</summary>
    public bool Local { get; private set; }

    long elapsed, end;      // m_nElapsed, m_nEnd (ms)

    /// <summary>FUN_14028d260 (commonutil.cpp:610): seconds to milliseconds, (long)(f * 1000.0f).</summary>
    static long Ms(float seconds) => (long)(seconds * 1000.0f);

    /// <summary>vfunc_4 (@0x1417fd1d0, :201-236): phase 0x65 ending after <paramref name="durationMs"/>. <paramref name="hasData"/>
    /// false (the data is not a StateTaxiCallData): the state returns to idle.</summary>
    public void Begin(bool hasData, int taxiId, int durationMs, bool local)
    {
        if (!hasData) { Finished = true; return; }
        Phase = Call;
        TaxiId = taxiId;
        Local = local;
        elapsed = 0;
        end = durationMs;
        Finished = false;
    }

    /// <summary>The duration by the player's level (:218-226): TaxiCallBestDriverDuration from TaxiCallBestDriverLevel (FUN_141b71c00),
    /// else TaxiCallDuration.</summary>
    public static int Duration(int level, int bestDriverLevel, int callMs, int bestDriverMs) => level >= bestDriverLevel ? bestDriverMs : callMs;

    /// <summary>vfunc_6 (@0x1417fdae0, :318-324): back to idle (state 1) unless the phase is 0x69. Run when the owner leaves the state
    /// for any reason: a move input in 0x65, death, a knockback, a skill, another state set.</summary>
    public void Cancel()
    {
        if (Phase != Leave) Finished = true;
    }

    /// <summary>vfunc_8 (@0x1417d9e0, :302-315) with the move direction: in phase 0x65 a move input sends the owner to the walk state
    /// (state 2), which leaves this one through <see cref="Cancel"/>, and the input goes on to the walk; in the other phases the
    /// state holds the body. True: the move input is held this frame.</summary>
    public bool HoldsMove(bool moveInput)
    {
        if (Finished || Phase == 0) return false;
        if (moveInput && Phase == Call) { Cancel(); return false; }
        return true;
    }

    /// <summary>vfunc_3: <paramref name="seconds"/> of game time.</summary>
    public void Tick(double seconds)
    {
        if (Finished || Phase == 0) return;
        elapsed += Ms((float)seconds);
        if (elapsed < end) return;
        switch (Phase)
        {
            case Call: Next(Arrive, 1.9f); break;
            case Arrive: Next(Board, 0.45f); break;
            case Board: Next(Ride, 0.45f); break;
            case Ride: Next(Leave, 1.0f); break;
            case Leave:
                Next(Away, 3.0f);
                if (Local) Send?.Invoke(new RequestTaxiRide(TaxiId));
                break;
            default: Finished = true; break;
        }
    }

    void Next(int phase, float addSeconds)
    {
        Phase = phase;       // FUN_1417fdbc0 (:337-341)
        end += Ms(addSeconds);
    }
}
