using System.Collections.Generic;
using System.Reflection;
using Godot;
using System.Threading.Tasks;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using Ms2.Ui.Shop;
using Ms2Godot.Ui;
using Ms2Godot.World;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/shop_talk_end.tscn) of the talk state around the npc shop, in the HUD with a LiveSession that has no
/// connection and the field's NpcTalk. The shop's close while talking is UITalkManager::SendNpcTalkMode0 (uitalkcinematic.cpp:1441-1452):
/// it sends NpcTalk 0 and also clears m_nTalking and resets the talk, so
/// - closing the shop (Esc, the X) clears the talk state, raises TalkOver, and a second talk to the same npc is accepted;
/// - an open answer that had no header waits for it, and every way the talk ends (the close, the npc leaving, the field's end)
///   drops the wait, so a later header does not open a shop over another talk.
/// Quits with 0 when all hold, else 1.
/// </summary>
public partial class ShopTalkEndCheck : Node
{
    public override void _Ready()
    {
        GetTree().Root.Size = new Vector2I(1280, 720);
        var session = new Net.LiveSession();
        typeof(Net.LiveSession).GetProperty("Instance")!.SetValue(null, session);
        GetTree().Root.CallDeferred(Node.MethodName.AddChild, new ShopTalkEndDriver(session));
        GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToFile, "res://ui/hud.tscn");
    }
}

public partial class ShopTalkEndDriver : Node
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    readonly Net.LiveSession session;
    readonly ulong startMs = Time.GetTicksMsec();
    HudScene hud;
    NpcTalk talk;
    FieldNpcs.Placed npc;
    ShopController shop;
    ShopOpenGate gate;
    int wait, overs;
    bool started;

    public ShopTalkEndDriver(Net.LiveSession s) { session = s; }

    void Finish(bool ok, string what)
    {
        typeof(Net.LiveSession).GetProperty("Instance")!.SetValue(null, null);
        if (!session.IsInsideTree()) session.Free();
        GD.Print("shop talk end: " + (ok ? "OK " : "FAIL ") + what);
        GetTree().Quit(ok ? 0 : 1);
        SetProcess(false);
    }

    async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }

    /// <summary>The server's order: the header (and items) first, then the talk window's answer; the HUD opens the dialog from the answer.</summary>
    async Task<bool> OpenShop()
    {
        talk.LiveTalkingForCheck = npc;
        shop.OnShopPacket(ShopPacket.Read(new Maple2.PacketLib.Tools.ByteReader(ShopPacket.WriteInfo(new ShopInfo
            { NpcId = 11000004, ShopId = 106, ProductCount = 0, TabShopId = 5, ResetEnable = false, HideReset = true, Icon = "" }))));
        session.RaiseTalkUiOpen(ShopController.UiId);
        await Frames(60);
        return !shop.IsClosed && shop.ShopView != null;
    }

    async Task Run()
    {
        talk = new NpcTalk(null, new List<FieldNpcs.Placed>(), null, null, session);
        AddChild(talk);
        npc = new FieldNpcs.Placed { Row = new NpcRow { Id = 11000004 }, Root = new Node3D(), ObjectId = 50000170 };
        session.TalkOver += () => overs++;
        shop = (ShopController)typeof(HudScene).GetField("liveShop", Private)!.GetValue(hud);
        gate = (ShopOpenGate)typeof(HudScene).GetField("openGate", Private)!.GetValue(hud);

        // 1. the close while talking (Esc, the X) clears the talk; the same npc can be talked to again
        if (!await OpenShop()) { Finish(false, "the shop did not open from the answer"); return; }
        if (!session.IsTalking()) { Finish(false, "the talk state is not set after the server opened a talk"); return; }
        overs = 0;
        shop.CloseShop();
        if (session.IsTalking()) { Finish(false, "closing the shop left the talk state set (every later talk is refused)"); return; }
        if (talk.LiveTalkingForCheck != null || overs != 1) { Finish(false, $"closing the shop: talk npc {talk.LiveTalkingForCheck}, TalkOver {overs}"); return; }
        if (!talk.RequestLiveTalkForCheck(npc)) { Finish(false, "a second talk to the same npc was refused after the shop closed"); return; }
        await Frames(60);                               // the close animation

        // the second talk opens the shop again
        if (!await OpenShop()) { Finish(false, "the shop did not open from the second talk's answer"); return; }
        overs = 0;
        talk.LiveTalkingForCheck = null;                // the talk ends away from the dialog (the npc left range): the shop closes by itself
        await Frames(60);
        if (!shop.IsClosed) { Finish(false, "the shop stayed open after the talk ended"); return; }

        // 2. an answer with no header waits for it; the talk ending drops the wait
        talk.LiveTalkingForCheck = npc;
        session.RaiseTalkUiOpen(ShopController.UiId);
        if (!gate.Wanted) { Finish(false, "the answer with no header did not wait for it"); return; }
        talk.LiveTalkingForCheck = null;                // the talk ends (walk away, the npc gone, Esc, the server's close)
        if (gate.Wanted) { Finish(false, "the talk ended and the answer still waits for a header"); return; }

        // 3. the field's end (the talk window's owner leaves the tree) drops it too
        talk.LiveTalkingForCheck = npc;
        session.RaiseTalkUiOpen(ShopController.UiId);
        if (!gate.Wanted) { Finish(false, "the second answer with no header did not wait for it"); return; }
        RemoveChild(talk);
        talk.Free();
        if (gate.Wanted || session.IsTalking()) { Finish(false, $"the field's end left the wait {gate.Wanted} / the talk {session.IsTalking()}"); return; }
        Finish(true, "the close cleared the talk and a second talk was accepted; the walk away and the field's end dropped the header wait");
    }

    public override void _Process(double delta)
    {
        if (Time.GetTicksMsec() - startMs > 300_000) { Finish(false, "timed out"); return; }
        if (started || GetTree().CurrentScene is not HudScene h || !Field.LoadComplete) return;
        if (++wait < 30) return;
        if (typeof(HudScene).GetField("liveShop", Private)!.GetValue(h) == null) { if (wait > 900) Finish(false, "the HUD wired no shop controller"); return; }
        hud = h; started = true;
        _ = Run();
    }
}
