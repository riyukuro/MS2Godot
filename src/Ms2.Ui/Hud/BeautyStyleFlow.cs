using Ms2.Net;
using Ms2.Net.Packets;
using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Ui.Hud;

/// <summary>
/// The host side of the saved-styles dialog (UIBeautyShopStyleDialog, <see cref="BeautyStyleDialogController"/>): when it opens, where
/// inbound 0x31 goes and how it ends. As with <see cref="BeautyShopFlow"/> it needs the own-avatar <see cref="IBeautyCustomize"/>
/// and stays shut without one.
/// <para>STILL-OPEN: the client opens it from an NPC talk command ("BeautyStyleDialog", uitalkcinematic.cpp:1190, then
/// OpenBeautyStyleDialog @0x141b1fa20 :1341: dialog 0x4f03, talk mode 4); the talk-dialog dispatch is not ported, so the host has
/// no caller of <see cref="Open"/> yet. The close path FUN_140f082d0 is not recovered either: <see cref="Shutdown"/> restores the worn
/// slot (FUN_140f08f70) and closes.</para>
/// </summary>
public sealed class BeautyStyleFlow
{
    public Func<IBeautyCustomize> CreateCustomize = () => null;
    public Func<BeautyStyleDialogController> CreateController;
    public Action<BeautyStyleDialogController> Show;
    public Action<string> Log = _ => { };

    public BeautyStyleDialogController Current { get; private set; }
    public bool IsOpen => Current != null;

    /// <summary>Opens the dialog. False when it is already open or there is no Customize.</summary>
    public bool Open()
    {
        if (IsOpen || CreateController == null) return false;
        var customize = CreateCustomize?.Invoke();
        if (customize == null) { Log("beauty style: no own-avatar customize, the dialog stays shut"); return false; }
        var c = CreateController();
        c.Customize = customize;
        c.Closed += () => { if (Current == c) Current = null; };
        Current = c;
        Show?.Invoke(c);
        return true;
    }

    /// <summary>The scene goes away with the dialog open: the worn slot is restored (FUN_140f08f70), then it closes.</summary>
    public void Shutdown()
    {
        var c = Current;
        if (c == null) return;
        c.DropSelection();
        c.Close();
        Current = null;
    }

    public void OnPacket(BeautyStylePacket p)
    {
        if (IsOpen) Current.OnPacket(p);
        else Log($"beauty style: mode {p.Mode} arrived with the dialog closed");
    }
}

/// <summary>
/// Inbound 0x31 has one opcode and two readers: UIBeautyShopDialog::OnBeautyShopPacket (<see cref="BeautyShopPacket"/>) and
/// UIBeautyShopStyleDialog's FUN_140f05a80 (<see cref="BeautyStylePacket"/>), and UIItemColoringDialog's (<see cref="BeautyColoringPacket"/>). Each dialog registers its handler at open and removes
/// it at close, so the body goes to the dialog that is open, and the style dialog first when both are.
/// </summary>
public static class BeautyPacketRouter
{
    public static void Route(byte[] body, BeautyShopFlow shop, BeautyStyleFlow style, Action<string> log, ItemColoringFlow coloring = null, ProtocolProfile profile = null)
    {
        profile ??= ProtocolProfile.Kms2525;
        if (coloring is { IsOpen: true })
        {
            var cp = BeautyColoringPacket.Read(new ByteReader(body), profile);
            log($"live: item coloring mode {cp.Mode}");
            coloring.OnPacket(cp);
        }
        else if (style.IsOpen)
        {
            var sp = BeautyStylePacket.Read(new ByteReader(body), profile);
            log($"live: beauty style mode {sp.Mode}");
            style.OnPacket(sp);
        }
        else
        {
            var bp = BeautyShopPacket.Read(new ByteReader(body), profile);
            log($"live: beauty shop mode {bp.Mode}");
            shop.OnPacket(bp);
        }
    }
}
