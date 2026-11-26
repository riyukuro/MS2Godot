namespace Ms2.Ui.Shop;

/// <summary>
/// When the npc shop dialog opens. The client's shop header handler (UIShopDialog::ReadShopInfo, uishopdialog.cpp:1580-1628) never
/// opens the dialog: the talk window's npc ui (CNpc::OpenTalkUi, uitalkcinematic.cpp:1474) or the system shop answer
/// (CSceneStateGame::OnSystemShop, scenestategame.cpp:7943) does. The server sends the header and the items before its answer, but
/// the packets are drained once per frame, so the answer may land in the same frame or a later one; the answer may also come first.
/// The dialog opens once both the answer and the header are in, whichever came last, and never from the header alone (a shop opened
/// by the header with no answer yet is closed by its first frame, as no talk is open, and sends a stray talk end).
/// </summary>
public sealed class ShopOpenGate
{
    bool wanted;

    /// <summary>The talk ui or system shop answer arrived: true to open the dialog now (the header is read and it is not open).
    /// Without the header the open waits for it.</summary>
    public bool OnAnswer(bool headerRead, bool open)
    {
        if (open) { wanted = false; return false; }
        if (headerRead) { wanted = false; return true; }
        wanted = true;
        return false;
    }

    /// <summary>The shop header was read: true to open the dialog when an answer is waiting for it.</summary>
    public bool OnHeader(bool open)
    {
        bool w = wanted && !open;
        wanted = false;
        return w;
    }

    /// <summary>The talk ended (or the shop closed) with no header: the waiting answer is dropped.</summary>
    public void Cancel() => wanted = false;
}
