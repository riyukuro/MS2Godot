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

    /// <summary>The controller's one wrapper, made on its first lookup and kept (null for null): the arrows ask for their
    /// dialog every frame one shows, so the lookup allocates nothing after the first.</summary>
    public static GuideControllerDialog Of(UiController controller) =>
        controller == null ? null : controller.GuideDialog is { } g && g.host == controller.Host ? g : controller.GuideDialog = new GuideControllerDialog(controller);

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

    /// <summary>The movie's entity as a node, kept while the entity stays the same (a reload makes a new one).</summary>
    public override GuideNode Root
    {
        get
        {
            if (host?.Entity is not { } e || Rt.IsNullish(e)) return null;
            if (root == null || !ReferenceEquals(root.Target, e)) root = new GuideObjectNode(host, e);
            return root;
        }
    }

    GuideObjectNode root;
}

/// <summary>
/// A registered dialog that is not open: CUIService::FindObjectByName answers it (vfunc_55 true), so a dialog condition with
/// isOpen 0 is met by it (CGuideConditionDialog::vfunc_3 @0x1413a6140). The port frees a dialog's movie when it closes and
/// builds it on first open; this is the answer for the registered ones without a movie.
/// </summary>
public sealed class GuideClosedDialog : GuideDialog
{
    readonly string typeName;
    public GuideClosedDialog(string typeName) { this.typeName = typeName; }
    public override string TypeName => typeName;
    public override bool Hidden => true;
    public override int SubIndex => -1;     // UIDialog::vfunc_82 (uidialog.cpp:913)
}

public static class GuideDialogLookup
{
    static readonly Dictionary<string, GuideClosedDialog> closed = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The open dialog of the name, else a closed stand-in when the client registers the name (<see cref="RegisteredDialogs"/>:
    /// CreateAllDialogs registers each once, RegisterObject puts it in m_mapC until ClearAllUI, uiservice.cpp:495), else null.
    /// One stand-in per name, kept: a guide tick's lookup allocates nothing.
    /// </summary>
    public static GuideDialog Resolve(string name, Func<string, GuideDialog> open)
    {
        var d = open(name);
        if (d != null || !RegisteredDialogs.Contains(name)) return d;
        if (!closed.TryGetValue(name, out var c)) closed[name] = c = new GuideClosedDialog(name);
        return c;
    }
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
