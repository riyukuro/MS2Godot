using System;
using System.Collections.Generic;
using System.Reflection;
using Godot;
using Ms2.Avm2;
using Ms2.Avm2.Flash;
using FlashPlayer = Ms2.Avm2.Flash.Player;
using Ms2Godot.Ui;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/dialog_layer_order.tscn): the HUD's dialogs are kept in their CommonDepth layers' order on
/// screen, as the client's one stage does (each layer a sprite of the depth root, CommonDepth.addLayer, CommonDepth.as:143-150;
/// a click reorders only inside its layer, CommonDepth.topmostLayer's setChildIndex, :446). On the HUD (offline) it opens the
/// inventory (layer 0x19), the item enchant dialog (0x1b, uiitemenchantdialog.cpp:332) and the mail send dialog (0x19), then
/// <list type="bullet">
/// <item>the enchant dialog, opened before the mail send dialog, is still after both 0x19 dialogs in the tree (drawn and hit over them);</item>
/// <item>a mouse down on the inventory's window (Window.onSortDepthControl -> OnTopMostLayer) moves it after the mail send
/// dialog and keeps it before the enchant dialog, and its hit order stays under the enchant dialog's;</item>
/// <item>opening the mail send dialog again while it is up (vfunc_65, uimaillistdialog.cpp:841: the movie's setTopMostLayer)
/// moves it after the inventory, still before the enchant dialog;</item>
/// <item>every dialog of the HUD's root, the game menu (0xe, added later in place of its slot) among them, runs in layer order.</item>
/// </list>
/// Quits with 0 when all hold, else 1.
/// </summary>
public partial class DialogLayerOrderCheck : Node
{
    public override void _Ready()
    {
        GetTree().Root.Size = new Vector2I(1280, 720);   // a headless window is 64x64
        GetTree().Root.CallDeferred(Node.MethodName.AddChild, new DialogLayerOrderDriver());
        GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToFile, "res://ui/hud.tscn");
    }
}

/// <summary>The check's driver, at the tree root so it outlives the scene change to the HUD.</summary>
public partial class DialogLayerOrderDriver : Node
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    const string WindowClass = "scaleform.ms2.controls.dialog.Window";
    HudScene hud;
    MovieDialog inv, enchant, mail;
    int stage, wait, failures;
    readonly ulong startMs = Time.GetTicksMsec();

    static object Get(object o, string name) => o?.GetType().GetField(name, Private)?.GetValue(o);
    static void Call(object o, string name, params object[] args)
    {
        foreach (var m in o.GetType().GetMethods(Private))
            if (m.Name == name && m.GetParameters().Length == args.Length) { m.Invoke(o, args); return; }
        throw new MissingMethodException(o.GetType().Name, name);
    }
    void Fail(string what) { failures++; GD.Print("dialog layer order: FAIL " + what); }

    void Finish(string what)
    {
        GD.Print("dialog layer order: " + (failures == 0 ? "OK " : "FAIL ") + what);
        GetTree().Quit(failures == 0 ? 0 : 1);
        SetProcess(false);
    }

    static int DrawOrder(MovieDialog m) => (int)typeof(MovieDialog).GetMethod("DrawOrder", Private)!.Invoke(m, null)!;

    static AsDisplayObject FindWindow(FlashPlayer p)
    {
        if (p.RT.GetClass(WindowClass) is not { } w) return null;
        AsDisplayObject Walk(AsDisplayObject o)
        {
            if (p.RT.IsType(o, w)) return o;
            if (o is AsContainer c) foreach (var ch in c.Children.ToArray()) if (Walk(ch) is { } f) return f;
            return null;
        }
        return Walk(p.Stage);
    }

    /// <summary>The tree indexes of the three dialogs must run as <paramref name="want"/> (lowest first).</summary>
    void Order(string step, params MovieDialog[] want)
    {
        var got = new List<string>();
        for (int i = 1; i < want.Length; i++)
            if (want[i - 1].GetIndex() >= want[i].GetIndex())
                Fail($"{step}: {Name(want[i - 1])} (index {want[i - 1].GetIndex()}) is not before {Name(want[i])} (index {want[i].GetIndex()})");
        foreach (var m in want) got.Add($"{Name(m)}@{m.GetIndex()} layer {m.Player.HostLayer}");
        GD.Print($"dialog layer order: {step}: " + string.Join(", ", got));
    }

    /// <summary>Every dialog under the HUD's root, in tree order, must have a layer no lower than the one before it (nodes that are
    /// not dialogs, and movies without a layer, are skipped), as the depth root's layer sprites are drawn.</summary>
    void Sorted(string step, Node parent)
    {
        int last = -1; string lastName = null, list = "";
        for (int i = 0; i < parent.GetChildCount(); i++)
        {
            if (parent.GetChild(i) is not MovieDialog d || MovieDialog.LayerOf(d) is var layer && layer < 0) continue;
            list += $" {d.Host?.Id ?? d.Name}:{layer}";
            if (layer < last) Fail($"{step}: {d.Host?.Id ?? d.Name} (layer {layer}, index {i}) is after {lastName} (layer {last})");
            last = layer; lastName = d.Host?.Id ?? d.Name;
        }
        GD.Print($"dialog layer order: {step}: layers" + list);
    }

    string Name(MovieDialog m) => m == inv ? "inventory" : m == enchant ? "enchant" : m == mail ? "mail send" : m.Name;

    public override void _Process(double delta)
    {
        if (Time.GetTicksMsec() - startMs > 300_000) { Fail($"timed out at stage {stage}"); Finish("timed out"); return; }
        switch (stage)
        {
            case 0:
                if (GetTree().CurrentScene is not HudScene h || !Field.LoadComplete) return;
                if (++wait < 30) return;
                hud = h;
                Call(hud, "ToggleInventory");
                Call(hud, "OpenItemEnchantShot");
                Call(hud, "OpenMailSend", new object[] { null });
                inv = (MovieDialog)Get(hud, "inventoryMovie");
                enchant = (MovieDialog)Get(hud, "itemEnchantMovie");
                mail = (MovieDialog)Get(hud, "mailSendMovie");
                if (inv == null || enchant == null || mail == null) { Fail($"a dialog did not open (inventory {inv != null}, enchant {enchant != null}, mail send {mail != null})"); Finish("no dialogs"); return; }
                if (inv.GetParent() != enchant.GetParent() || mail.GetParent() != enchant.GetParent()) { Fail("the three dialogs have different parents"); Finish("parents"); return; }
                wait = 0; stage = 1;
                return;
            case 1:
            {
                if (++wait < 20) return;
                Order("opened", inv, mail, enchant);
                if (Get(hud, "gameMenuMovie") is not MovieDialog) Fail("the game menu did not open");
                Sorted("opened", inv.GetParent());
                var w = FindWindow(inv.Player);
                if (w == null) { Fail("the inventory has no window"); Finish("no window"); return; }
                inv.Player.Call(w, "dispatchEvent", new object[] { inv.Player.RT.GetClass("flash.events.MouseEvent").Construct("mouseDown", true, false) });
                Order("after a click on the inventory", mail, inv, enchant);
                if (DrawOrder(inv) >= DrawOrder(enchant)) Fail($"the inventory's hit order {DrawOrder(inv)} is not under the enchant dialog's {DrawOrder(enchant)}");
                if (DrawOrder(inv) <= DrawOrder(mail)) Fail($"the inventory's hit order {DrawOrder(inv)} is not over the mail send dialog's {DrawOrder(mail)}");
                Call(hud, "OpenMailSend", new object[] { null });
                Order("after the mail send dialog was opened again", inv, mail, enchant);
                Sorted("at the end", inv.GetParent());
                Finish("inventory, mail send and enchant kept in their layers");
                return;
            }
        }
    }
}
