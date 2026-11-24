using System.Numerics;
using Ms2.Net.Packets;
using Ms2.Net.Wire;

namespace Ms2.Ui;

/// <summary>What <see cref="AutoFindTaxiStation"/> reads and drives: the field, the taxi lists, the local player and CameraMgr.</summary>
public interface IAutoFindTaxiHost
{
    /// <summary>GetFieldData()-&gt;m_nAutoFindTaxiStation (CFieldData +0xb8, fielddata.h:123; 1 unless &lt;ui autoFindTaxiStation&gt; says
    /// otherwise, fielddata.cpp:549, fielddatacli.cpp:237) is not 0.</summary>
    bool FieldAutoFind { get; }
    /// <summary>WorldService::FUN_141bd0420(0x85359406): the field's MS2TaxiStation entities, as their position (0xfd95ca52) in MS2 axes.</summary>
    IReadOnlyList<Vector3> Stations { get; }
    /// <summary>DAT_143ab2870, the current map id.</summary>
    int FieldId { get; }
    /// <summary>DAT_143ab3078 (the found taxis) holds <paramref name="field"/>.</summary>
    bool Found(int field);
    /// <summary>DAT_143ab3098 (the fields a taxi request went out for) holds <paramref name="field"/>.</summary>
    bool Requested(int field);
    /// <summary>DAT_143ab3098.Add(field).</summary>
    void AddRequested(int field);
    /// <summary>CTaxiManager::FUN_14085f730 (taximanager.cpp:119): the field meta table's GetTaxiHomePrivilege(field) == 1.</summary>
    bool HomeTaxiField(int field);
    /// <summary>FUN_140710590.</summary>
    void Send(IOutPacket packet);
    /// <summary>PCBase::AddStateFlags(0x100) (true) or RemoveStateFlags(0x100) (false) on the local player, when there is one.</summary>
    void SetPlayerStateFlag(bool on);

    /// <summary>CameraMgr::GetModeSetting: false without a current mode; else the setting's position (m_c).</summary>
    bool ModePosition(out Vector3 position);
    /// <summary>FUN_1409f2430: the current mode's vfunc_19 (m_50, its distance), 0 without a mode.</summary>
    float ModeDistance { get; }
    /// <summary>CameraMgr::FUN_140af0c70: the current mode's m_68 (yaw, radians), 0 without a mode.</summary>
    float ModeYaw { get; }
    /// <summary>CameraMgr::FUN_140af0d30: the current mode's m_6c (the angle from the up axis, radians), 0 without a mode.</summary>
    float ModePitch { get; }
    /// <summary>GetTargetMode()-&gt;FUN_1409f2540(): the follow mode's position (its m_setting.m_c).</summary>
    Vector3 TargetModePosition { get; }
    /// <summary>MakeFlyBy(<paramref name="seconds"/>, the current mode's setting with m_c = <paramref name="position"/>) with
    /// m_nKind = 2, SetFlyBy(&amp;fly-by, 1, 1.0f), then SetMode(<paramref name="mode"/>, <paramref name="keep"/>).</summary>
    void FlyTo(Vector3 position, float seconds, int mode, bool keep);
    /// <summary>CameraMgr::SetPendingMode.</summary>
    void SetPendingMode(int mode);
}

/// <summary>
/// CAutoFindTaxiStation (autofindtaxistation.cpp): on entering a field whose taxi station is neither found nor asked for, the
/// camera flies over the station, the taxi request (RequestTaxi mode 5) goes out, and the camera flies back when the server's
/// OnTaxi answers (or is set back to the follow mode when 5 s pass with no answer). Esc ends it at once.
/// Callers: MyPC::vfunc_12 (@0x141521560, mypc.cpp:1016, the own pc's actor added) <see cref="StartAutoFind"/>; OnTaxi
/// (scenestategame.cpp:7923) <see cref="OnTaxiFound"/>; WorldService::FUN_141bcd960's per-frame tail (worldservice_1.cpp:5743)
/// <see cref="EscapeTick"/>; the field leave (worldservice_1.cpp:9988-9989) <see cref="FieldLeave"/>, which also clears the
/// OnceCallbackTimer; the cutscene movie (uicutscenemovie.cpp:451, :122) <see cref="Pause"/> and <see cref="Resume"/>.
/// Its OnceCallbackTimer callbacks are kept here (<see cref="Advance"/>).
/// </summary>
public sealed class AutoFindTaxiStation
{
    /// <summary>The PCBase state flag held while the camera is away (AddStateFlags(0x100)).</summary>
    public const uint StateFlag = 0x100;
    /// <summary>StartAutoFind's first delay (:223).</summary>
    public const int StartDelayMs = 1000;
    /// <summary>SendRequestTaxiMode5's wait for the answer (:302).</summary>
    public const int AnswerWaitMs = 5000;
    /// <summary>FUN_1409f1970's delay before the fly back (:310, 0x5dc).</summary>
    public const int ReturnDelayMs = 0x5dc;
    /// <summary>The camera modes (camera.h): the follow mode and the lock mode.</summary>
    public const int TargetMode = 2, LockMode = 4;

    static readonly RequestTaxiAutoFind Discover = new();

    readonly IAutoFindTaxiHost host;
    readonly List<(double At, Action Run)> timers = new();
    double now;

    public AutoFindTaxiStation(IAutoFindTaxiHost host) { this.host = host; }

    /// <summary>m_nActive.</summary>
    public bool Active { get; private set; }
    /// <summary>m_nDone.</summary>
    public bool Done { get; private set; }
    /// <summary>m_nPaused.</summary>
    public bool Paused { get; private set; }
    /// <summary>m_nPending.</summary>
    public bool Pending { get; private set; }
    /// <summary>The callbacks waiting on the OnceCallbackTimer.</summary>
    public int TimerCount => timers.Count;

    /// <summary>StartAutoFind (@0x1409f1240, :203-228).</summary>
    public void StartAutoFind()
    {
        if (!host.FieldAutoFind) return;
        if (host.Stations is not { Count: > 0 }) return;
        if (Done) return;
        if (Paused) { Pending = true; return; }
        int field = host.FieldId;
        if (!host.Found(field) && !host.Requested(field))
        {
            Active = true;
            After(StartDelayMs, Fly);
            host.SetPlayerStateFlag(true);
        }
    }

    /// <summary>FUN_1409f1450 (:231-291): for each station, the camera flies to the station's position at the current mode's
    /// distance, yaw and pitch (fDist sin yaw, fDist cos yaw, fDist cos pitch from it) over 0.8 ms per unit and locks there, and
    /// the request goes out when it arrives; without a camera mode the request goes out at once. A found, asked or house-taxi
    /// field only drops the state flag.</summary>
    void Fly()
    {
        if (!Active || Done) return;
        int field = host.FieldId;
        if (host.Found(field) || host.Requested(field) || host.HomeTaxiField(field))
        {
            host.SetPlayerStateFlag(false);
            return;
        }
        foreach (var station in host.Stations)
        {
            if (host.ModePosition(out var from))
            {
                float dist = host.ModeDistance, yaw = host.ModeYaw, pitch = host.ModePitch;
                var to = new Vector3(dist * MathF.Sin(yaw) + station.X, dist * MathF.Cos(yaw) + station.Y, dist * MathF.Cos(pitch) + station.Z);
                float ms = (from - to).Length() * 0.8f;
                host.FlyTo(to, ms / 1000.0f, LockMode, false);
                After((uint)ms, SendRequestTaxiMode5);
                host.SetPlayerStateFlag(true);
            }
            else SendRequestTaxiMode5();
        }
    }

    /// <summary>SendRequestTaxiMode5 (@0x1409f1890, :294-304): the field joins the requested list, RequestTaxi mode 5 goes out,
    /// and the answer is waited for 5 s.</summary>
    void SendRequestTaxiMode5()
    {
        if (!Active) return;
        host.AddRequested(host.FieldId);
        host.Send(Discover);
        After(AnswerWaitMs, CancelUnlessFound);
    }

    /// <summary>FUN_1409f1970 (@0x1409f1970, :307-311), OnTaxi's last call: the fly back 1.5 s later.</summary>
    public void OnTaxiFound()
    {
        if (Active && !Done) After(ReturnDelayMs, FlyBack);
    }

    /// <summary>FUN_1409f1a00 (:314-326): with the field still not found, the follow mode is set back and the state reset.
    /// The request's answer wait ends here, and so does a hit on the own pc (PC::vfunc_33, pc.cpp:895-896: the record's
    /// object, its m_nCasterId, is the own pc).</summary>
    public void CancelUnlessFound()
    {
        if (!Active || Done) return;
        if (!host.Found(host.FieldId))
        {
            Done = true;
            host.SetPendingMode(TargetMode);
            Reset();
        }
    }

    /// <summary>Reset (@0x1409f1a70, :329-340).</summary>
    void Reset()
    {
        if (!Active) return;
        host.SetPlayerStateFlag(false);
        Active = false;
        Done = false;
        Paused = false;
        Pending = false;
    }

    /// <summary>FUN_1409f1af0 (:343-363): the camera flies back to the follow mode's position over 0.5 ms per unit (the follow
    /// mode keeps its rotation), then the state resets; without a camera mode the follow mode is set back at once.</summary>
    void FlyBack()
    {
        if (host.ModePosition(out var from))
        {
            var target = host.TargetModePosition;
            float ms = (from - target).Length() * 0.5f;
            host.FlyTo(target, ms / 1000.0f, TargetMode, true);
            After((uint)ms, Reset);
        }
        else
        {
            host.SetPendingMode(TargetMode);
            Reset();
        }
    }

    /// <summary>FUN_1409f1cb0 (@0x1409f1cb0, :366-399), each frame: while active, Esc asks for the taxi now (when not found, not
    /// asked and not a house-taxi field) and ends the auto-find as the 5 s wait does; while not active, a start held back by a
    /// pause runs. True when Esc was taken.</summary>
    public bool EscapeTick(bool escapePressed)
    {
        if (Done) return false;
        if (Active)
        {
            if (!escapePressed) return false;
            int field = host.FieldId;
            if (!host.Found(field) && !host.Requested(field) && !host.HomeTaxiField(field))
            {
                host.AddRequested(field);
                host.Send(Discover);
            }
            CancelUnlessFound();
            return true;
        }
        if (Pending && !Paused)
        {
            StartAutoFind();
            Pending = false;
        }
        return false;
    }

    /// <summary>FUN_1409f1de0 (@0x1409f1de0, :402-408) and the OnceCallbackTimer's Clear at the field leave (worldservice_1.cpp:9988-9989).</summary>
    public void FieldLeave()
    {
        Active = false;
        Done = false;
        Paused = false;
        Pending = false;
        timers.Clear();
    }

    /// <summary>Pause (@0x1409f1e20, :411): a cutscene movie plays.</summary>
    public void Pause() => Paused = true;
    /// <summary>Resume (@0x1409f1e40, :414).</summary>
    public void Resume() => Paused = false;

    /// <summary>OnceCallbackTimer::Update (@0x140ca7100, unit_140ca6fc0_oncecallbacktimer.cpp:43): <paramref name="ms"/>
    /// milliseconds pass, then one pass in the order the callbacks were added (the map's ids) runs and drops each one due. One
    /// added by a callback in the pass is reached in the same pass.</summary>
    public void Advance(double ms)
    {
        now += ms;
        for (int i = 0; i < timers.Count;)
        {
            if (timers[i].At > now) { i++; continue; }
            var run = timers[i].Run;
            timers.RemoveAt(i);
            run();
        }
    }

    /// <summary>OnceCallbackTimer::Add (@0x140ca7230, :70).</summary>
    void After(double ms, Action run) => timers.Add((now + ms, run));
}
