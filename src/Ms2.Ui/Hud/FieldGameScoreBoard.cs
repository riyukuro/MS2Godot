using Ms2.Net.Packets;

namespace Ms2.Ui.Hud;

/// <summary>
/// FieldGameScoreBoardCli (uishadowexpeditionbossbar.cpp, ctor @0x140f9c1e0), the listener of the ShadowExpedition packet: it keeps the mode
/// of the last open (m_nMode) and the board it drives (m_pDlg). Open (@0x140f9c3a0) reads the packet, translates the title through
/// the trigger string group and runs FUN_140f9c610 (@0x140f9c610): mode 1 finds the boss bar dialog (id 0x4f0e) and gives it the points and
/// the title; the dialog then opens (FUN_141924680) when it is not open (FUN_141924630 = 0), else refreshes (vfunc_85). The update (@0x140f9c520)
/// sets the current points of the stored dialog and refreshes it; the close (@0x140f9c5b0) closes the stored dialog when it is open.
/// STILL-OPEN: modes 2 and 3 drive the dark stream score dialog (UIDarkStreamScoreDialog, id 0x4ee4; a different window), which is not
/// ported here, so those modes find no dialog and change nothing (the client's own result when the dialog is not found); where the client
/// creates the boss bar's instance for FindObject (a dialog nothing opens by itself) is not traced, the owner's <see cref="FindBossBar"/> makes it.
/// </summary>
public sealed class FieldGameScoreBoard
{
    /// <summary>The mode of the last open packet (m_nMode, 0 from the constructor).</summary>
    public int Mode { get; private set; }

    /// <summary>The stored dialog (m_pDlg).</summary>
    public ShadowExpeditionBossBarController Dialog { get; private set; }

    /// <summary>GetShadowExpeditionBossBar(0x4f0e) (@0x140f9d940): the dialog's instance, null when there is none.</summary>
    public Func<ShadowExpeditionBossBarController> FindBossBar = () => null;
    /// <summary>CServerStringGroup::GetString of the trigger group (the title's translation).</summary>
    public Func<string, string> ServerString = s => s;
    /// <summary>FUN_141924680: open the dialog (a no-op without a local player; the owner decides).</summary>
    public Action<ShadowExpeditionBossBarController> Open = _ => { };

    /// <summary>FUN_140f9c300 (@0x140f9c300): the type picks open, update or close; another type does nothing.</summary>
    public void OnPacket(FieldGameScoreBoardPacket p)
    {
        switch (p.Type)
        {
            case FieldGameScoreBoardPacket.Open: OnOpen(p); break;
            case FieldGameScoreBoardPacket.SetCurrent: OnUpdate(p); break;
            case FieldGameScoreBoardPacket.Close: OnClose(); break;
        }
    }

    void OnOpen(FieldGameScoreBoardPacket p)
    {
        Mode = p.Mode;
        string title = ServerString(p.Title) ?? "";
        if (Mode == 1 && FindBossBar() is { } bar)
        {
            bar.SetCurMax(p.Cur, p.Max);
            bar.SetTitle(title);
            Dialog = bar;
        }
        if (Dialog == null) return;
        if (Dialog.IsClosed) Open(Dialog);
        else Dialog.Refresh();
    }

    void OnUpdate(FieldGameScoreBoardPacket p)
    {
        if (Dialog == null) return;
        Dialog.SetCur(p.Cur);
        Dialog.Refresh();
    }

    void OnClose()
    {
        if (Dialog == null || Dialog.IsClosed) return;
        Dialog.Close();
        Dialog = null;
    }
}
