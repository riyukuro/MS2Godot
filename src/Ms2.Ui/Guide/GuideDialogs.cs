using Ms2.Avm2;

namespace Ms2.Ui.Guide;

/// <summary>
/// A dialog of the game's HUD as the guide reads it (UIDialog): its controller with the movie it runs. The game's
/// dialog registry (CUIService::FindObjectByName / FindObject over the open movies) wraps each open dialog in one.
/// </summary>
public sealed class GuideControllerDialog : GuideDialog
{
    readonly UiController controller;
    readonly UiHost host;

    public GuideControllerDialog(UiController controller)
    {
        this.controller = controller;
        host = controller?.Host;
    }

    /// <summary>GetTypeName: what the guide's name lookups match (the client's FindObjectByName compares it).</summary>
    public override string TypeName => controller?.TypeName;

    /// <summary>vfunc_55 (uidialog.cpp:487): closed, the close finished (a HUD movie that is not loaded reads closed
    /// too, as the client's unloaded dialog does).</summary>
    public override bool Hidden => controller == null || controller.IsClosed;

    /// <summary>vfunc_82, the dialog's sub index (its tab); STILL-OPEN: the dialogs do not report one, so the guide's
    /// subIndex conditions read 0.</summary>
    public override int SubIndex => 0;

    /// <summary>FUN_141924680 opens it; the port's dialogs open on their own toggles, so the OpenDialog action does
    /// nothing with one closed (the client's opens a dialog that is loaded but hidden).</summary>
    public override void Open() { }

    public override GuideNode Root => host?.Entity is { } e && !Rt.IsNullish(e) ? new GuideObjectNode(host, e) : null;
}

/// <summary>A display object of a dialog's movie (ASDisplayObject) as the guide walks it: its place and its named
/// children (GetChild&lt;ASDisplayObject&gt;).</summary>
public sealed class GuideObjectNode : GuideNode
{
    readonly UiHost host;
    readonly object obj;

    public GuideObjectNode(UiHost host, object obj)
    {
        this.host = host;
        this.obj = obj;
    }

    /// <summary>The AS object itself (a movie call the guide's arrows make on the clip it walks).</summary>
    public object Target => obj;
    /// <summary>The movie's host (the call's runtime).</summary>
    public UiHost Host => host;

    public override bool Visible => Rt.ToBoolean(host.Get(obj, "visible"));
    public override bool Selected => Rt.ToBoolean(host.Get(obj, "selected"));

    /// <summary>ASDisplayObject's x, y and z.</summary>
    public override (double X, double Y, double Z) Position =>
        (Rt.ToNumber(host.Get(obj, "x")), Rt.ToNumber(host.Get(obj, "y")), Rt.ToNumber(host.Get(obj, "z")));

    /// <summary>ASDisplayObject::GetWidthCeil (asdisplayobject.cpp:539).</summary>
    public override double WidthCeil => Math.Ceiling(Rt.ToNumber(host.Get(obj, "width")));

    /// <summary>
    /// GetChild&lt;ASDisplayObject&gt;(name): the child of the name. The movie names two kinds — instances the timeline
    /// placed, which answer as members, and clips the movie made itself, which only the display list holds — so the
    /// display list is walked first, then the member.
    /// </summary>
    public override GuideNode Child(string name)
    {
        if (obj is Ms2.Avm2.Flash.AsContainer c)
            foreach (var k in c.Children)
                if (k.InstanceName == name) return new GuideObjectNode(host, k);
        var m = host.Child(name, obj);
        return m == null || Rt.IsNullish(m) ? null : new GuideObjectNode(host, m);
    }
}
