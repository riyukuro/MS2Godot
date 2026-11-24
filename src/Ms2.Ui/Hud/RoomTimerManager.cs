using Ms2.Net.Packets;
using Ms2.Quest;

namespace Ms2.Ui.Hud;

/// <summary>A room timer window (UITimerBase, uitimergaugedialog.cpp): the timer dialog (0x4e3a) or the gauge (0x4f61).</summary>
public interface IRoomTimerWindow
{
    /// <summary>vfunc_84: the start server tick, the duration in ms and the offset in seconds.</summary>
    void SetTime(uint start, uint duration, int offset);
    /// <summary>vfunc_85 / vfunc_86: the time added / removed effect.</summary>
    void ShowIncreaseEffect();
    void ShowDecreaseEffect();
    /// <summary>vfunc_87: a lap's time (the gauge's).</summary>
    void SetLap(int id, int time);
    /// <summary>FUN_141924680: open the dialog.</summary>
    void Open();
    /// <summary>vfunc_68: close the dialog without its tween.</summary>
    void CloseNow();
}

/// <summary>
/// RoomTimerManager (client/ms2/roomtimermanager.cpp): the room's one timer, which the RoomTimer packet drives
/// (<see cref="Dispatch"/>), shown in the window of its type (<see cref="GetTimer"/>).
/// </summary>
public sealed class RoomTimerManager
{
    /// <summary>The common strings of Update's notices (BlockFormat 0x99f / 0x9a1 with the seconds, GetStringCommon 0x9a0).</summary>
    public const int AddedNotice = 0x99f, FixedNotice = 0x9a0, RemovedNotice = 0x9a1;

    /// <summary>GetTimer (@0x140b8cd20): type 1 the timer dialog (0x4e3a), 2 the gauge (0x4f61), else none (the client asserts).</summary>
    public Func<int, IRoomTimerWindow> GetTimer = _ => null;
    /// <summary>GetStringCommon of an id.</summary>
    public Func<int, string> Common = _ => "";
    /// <summary>ShowSystemNotice(text, type, 1).</summary>
    public Action<string, int> Notice;
    /// <summary>A packet whose window is not here (the gauge, STILL-OPEN) or whose type has none.</summary>
    public Action<RoomTimerMessage> Unapplied;

    int type, start, duration, offset;          // m_type, m_start, m_duration, m_offset (ctor: 0)
    IRoomTimerWindow timer;                     // m_timer
    readonly SortedDictionary<int, int> values = new();   // m_values (std::map, by id)

    public (int Type, int Start, int Duration, int Offset) State => (type, start, duration, offset);
    public IRoomTimerWindow Timer => timer;

    /// <summary>Dispatch (@0x140b8c260): the command's handler.</summary>
    public void Dispatch(RoomTimerMessage m)
    {
        switch (m.Command)
        {
            case RoomTimerMessage.Start: Start(m); break;
            case RoomTimerMessage.Update: Update(m); break;
            case RoomTimerMessage.Restart: Restart(m); break;
            case RoomTimerMessage.Values: ReadValues(m); break;
            case RoomTimerMessage.Stop:
            case RoomTimerMessage.Cancel:
                Close();
                Reset();
                break;
        }
    }

    /// <summary>Start (@0x140b8c350): the timer kept, the open window closed; a duration above 0 and a type other than 0 open
    /// the type's window with the time.</summary>
    void Start(RoomTimerMessage m)
    {
        type = m.Type; start = m.StartTick; duration = m.Duration; offset = m.Offset;
        Close();
        if (duration <= 0) return;
        if (type == 0) return;
        OpenWindow(m);
    }

    /// <summary>Restart (@0x140b8c840): as Start, without the type 0 check.</summary>
    void Restart(RoomTimerMessage m)
    {
        type = m.Type; start = m.StartTick; duration = m.Duration; offset = m.Offset;
        Close();
        if (duration <= 0) return;
        OpenWindow(m);
    }

    void OpenWindow(RoomTimerMessage m)
    {
        timer = GetTimer(type);
        if (timer == null) { Unapplied?.Invoke(m); return; }
        timer.SetTime((uint)start, (uint)duration, offset);
        timer.Open();
    }

    /// <summary>
    /// Update (@0x140b8c550): the new start and duration; with an open window a duration of 0 or less closes it, else the
    /// time goes in, and notice 1 shows the seconds added (0x99f, notice type 4, the increase effect) or removed (0x9a1, type
    /// 2, the decrease effect), notice 2 the fixed text (0x9a0, type 4, the increase effect).
    /// </summary>
    void Update(RoomTimerMessage m)
    {
        start = m.StartTick;
        duration = m.Duration;
        if (timer == null) return;
        if (duration <= 0) { Close(); return; }
        timer.SetTime((uint)start, (uint)duration, offset);
        if (m.NoticeType == 1)
        {
            if (m.Delta > 0)
            {
                Notice?.Invoke(ClientFormat.Format(Common(AddedNotice) ?? "", m.Delta / 1000), 4);
                timer.ShowIncreaseEffect();
            }
            else if (m.Delta < 0)
            {
                Notice?.Invoke(ClientFormat.Format(Common(RemovedNotice) ?? "", Math.Abs(m.Delta / 1000)), 2);
                timer.ShowDecreaseEffect();
            }
        }
        else if (m.NoticeType == 2)
        {
            Notice?.Invoke(Common(FixedNotice) ?? "", 4);
            timer.ShowIncreaseEffect();
        }
    }

    /// <summary>ReadValues (@0x140b8ca30): the values replaced (the first of an id kept, as std::map insert), then each in id
    /// order to the type's window.</summary>
    void ReadValues(RoomTimerMessage m)
    {
        values.Clear();
        foreach (var (id, time) in m.Laps) values.TryAdd(id, time);
        var t = GetTimer(type);
        if (t == null) return;
        foreach (var (id, time) in values) t.SetLap(id, time);
    }

    /// <summary>Show (@0x140b8cbe0): an active timer opens its type's window again.</summary>
    public void Show()
    {
        if (!IsActive) return;
        timer = GetTimer(type);
        timer?.Open();
    }

    /// <summary>IsActive (@0x140b8cc90): a type, a start and a duration.</summary>
    public bool IsActive => type != 0 && start != 0 && duration != 0;

    /// <summary>Reset (@0x140b8ccd0): type, start and duration 0, no window (the offset is kept); also at the world's map
    /// change (WorldService::FUN_141bbc1d0, worldservice_1.cpp:3373), which leaves the window as it is.</summary>
    public void Reset() { type = 0; start = 0; duration = 0; timer = null; }

    /// <summary>Close (@0x140b8cdf0): the window closed at once (vfunc_68) and dropped.</summary>
    void Close()
    {
        if (timer == null) return;
        timer.CloseNow();
        timer = null;
    }
}
