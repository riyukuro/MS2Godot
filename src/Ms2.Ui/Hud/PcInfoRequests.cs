using Ms2.Net.Packets;

namespace Ms2.Ui.Hud;

/// <summary>
/// The other player's character window's request state, which UIPCInfoDialog (UI id 0x4ec8, uipcinfodialog.cpp) keeps
/// between opens: m_188 (the character of the last request), m_1d8 (the request still unanswered), m_1e0 (an
/// Unk_IntervalTimer, unit_140268010_sharedstatemap.cpp:179) and m_spInfo (the shown record, cleared by vfunc_67).
/// The window itself is a <see cref="MyInfoController"/> with <see cref="MyInfoController.PcInfoDialog"/>.
/// </summary>
public sealed class PcInfoRequests
{
    /// <summary>GetTickCountMs2: the timer's millisecond clock (32 bits, compared unsigned).</summary>
    public Func<uint> TickMs = () => (uint)Environment.TickCount;

    long requested;        // m_188
    long pending;          // m_1d8
    uint timerLast;        // m_1e0.m_nLast (m_nInterval is 0: Init)

    /// <summary>m_spInfo: the record the window shows, null while it shows none.</summary>
    public PcInfo Info { get; set; }
    public long Requested => requested;
    public long Pending => pending;

    /// <summary>The answer's record is to be shown in the closed window: CUIService::OpenDialog(0x4ec8).</summary>
    public event Action Open;
    /// <summary>The answer's record is to be shown in the loaded window (FUN_1422dc430, m_54): vfunc_84, 85, 86, 88, 98
    /// (<see cref="MyInfoController.ShowRecord"/>).</summary>
    public event Action Refresh;
    /// <summary>FUN_140d73bc0(0x26, 0, 0, ""): the common string 0x26 box (<see cref="MessageBoxQueue.ShowCommon"/>).</summary>
    public event Action<int> CommonBox;

    /// <summary>
    /// TryBeginRequest (@0x14132c080): 1 while the same character's request is unanswered and younger than 5000 ms
    /// (m_1e0.Check(5000) fails); 2 while the window is open (FUN_141924630: vfunc_55 == 0) on that character; else the
    /// request is begun (m_188 = m_1d8 = the id, m_1e0.Init) and 0.
    /// </summary>
    public int TryBeginRequest(long characterId, bool windowOpen)
    {
        if (characterId == pending && !Check(5000)) return 1;
        if (windowOpen && requested == characterId) return 2;
        requested = pending = characterId;
        timerLast = TickMs();
        return 0;
    }

    /// <summary>Unk_IntervalTimer::Check(n): true, and the timer restarted, when it never ran or n ms passed.</summary>
    bool Check(uint ms)
    {
        uint now = TickMs();
        if (timerLast != 0 && now - timerLast < ms) return false;
        timerLast = now;
        return true;
    }

    /// <summary>
    /// vfunc_79 (@0x14132d630) for RecvOp 0x78: dropped unless for m_188; then m_1d8 = 0. Result 1: nothing while a record
    /// is shown, else the record (FUN_14132c330) and <see cref="Open"/> for a closed window (vfunc_55 == 1) or
    /// <see cref="Refresh"/> for a loaded one. Another result: the 0x26 box, unless a record is shown.
    /// </summary>
    public void OnAnswer(CharacterInfoAnswer answer, bool windowClosed, bool windowLoaded)
    {
        if (answer.CharacterId != requested) return;
        pending = 0;
        if (Info != null) return;
        if (answer.Result == 1)
        {
            Info = answer.Info;
            if (windowClosed) Open?.Invoke();
            else if (windowLoaded) Refresh?.Invoke();
        }
        else CommonBox?.Invoke(0x26);
    }
}
