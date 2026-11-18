using Godot;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Ui;
using Ms2.Ui.Hud;

namespace Ms2Godot.Ui;

/// <summary>
/// The client's one message box layer, UIMessageBox (0x4e48), run from its movie (uimessagebox). The client registers it
/// once for the whole run (CUIService::CreateAllDialogs @0x141adf580, uiservice.cpp:2345), so it serves the login,
/// character select, create and field scenes alike: here it is a node under the tree's root that outlives scene changes,
/// over every scene's UI. Boxes reach it through <see cref="MessageBoxQueue"/>; the keys of a shown box go to it before
/// the scene sees them (UIMessageBox::vfunc_13).
/// </summary>
public partial class MessageBoxLayer : CanvasLayer
{
    static MessageBoxLayer instance;

    /// <summary>The exit box's yes, UIMessageBox::HandleGameexitButton(0) @0x1419fb480: SendRequestQuitAndDisconnect, then
    /// PostQuitMessage(0).</summary>
    public static event Action GameExit;

    MessageBoxController controller;
    MovieDialog movie;
    Viewport sizeSource;

    public static MessageBoxLayer Instance => instance;
    public MessageBoxController Controller => controller;

    /// <summary>The layer, made under the tree's root the first time it is asked for (null without client data).</summary>
    public static MessageBoxLayer Ensure(Node any)
    {
        if (instance != null && IsInstanceValid(instance)) return instance;
        var dataDir = Settings.DataDir;
        if (string.IsNullOrEmpty(dataDir)) return null;
        instance = new MessageBoxLayer { Name = "MessageBoxLayer", Layer = 12, ProcessMode = ProcessModeEnum.Always };
        instance.Build(new UiAssets(new PackSet(dataDir)), any.GetTree().Root);
        any.GetTree().Root.CallDeferred(Node.MethodName.AddChild, instance);   // the movie runs from now; the node joins the tree next
        return instance;
    }

    void Build(UiAssets assets, Window root)
    {
        if (!UiCode.Load(Settings.DataDir)) GD.PushWarning("UI code is not built for this data: the message box runs interpreted");
        var strings = new GameStrings(assets.Packs, assets.Language);
        MessageBoxData.CommonByKey = strings.Common();
        controller = new MessageBoxController();
        controller.GameExit += () => GameExit?.Invoke();
        controller.UnportedTag += (b, t) => GD.Print($"message box: button {b} of tag {t}: its handler is not ported");
        movie = new MovieDialog(assets, controller, new UiModel(new GameTables(assets.Packs, "en")), root.Size);
        AddChild(movie);
        MessageBoxQueue.Layer = controller;
    }

    public override void _EnterTree()
    {
        sizeSource = GetViewport();
        sizeSource.SizeChanged += Relayout;
        GetTree().NodeAdded += KeepLast;
        Relayout();
    }

    /// <summary>A new scene under the root would see the keys first (input goes from the root's last child back), so the
    /// layer moves after it.</summary>
    void KeepLast(Node n)
    {
        if (n.GetParent() == GetParent() && n != this) CallDeferred(MethodName.MoveLast);
    }

    void MoveLast()
    {
        if (GetParent() is { } p) p.MoveChild(this, -1);
    }

    public override void _ExitTree()
    {
        if (sizeSource != null) { sizeSource.SizeChanged -= Relayout; sizeSource = null; }
        GetTree().NodeAdded -= KeepLast;
        if (MessageBoxQueue.Layer == controller) MessageBoxQueue.Layer = null;
        if (instance == this) instance = null;
    }

    /// <summary>GFxManager::ResizeMovies: the new stage, then the layer's alignment.</summary>
    void Relayout() => movie?.Relayout(GetViewport().GetVisibleRect().Size);

    public bool Shown => controller?.IsIdle == false;

    /// <summary>UIMessageBox::vfunc_13 @0x1419f7560 while a box is shown: Enter, Esc, the arrows and Tab go to the movie.
    /// The layer is under the root after the scene, so it sees the keys first.</summary>
    public override void _Input(InputEvent e)
    {
        if (controller == null || controller.IsIdle) return;
        if (e is not InputEventKey { Pressed: true, Echo: false } k) return;
        string key = k.Keycode switch
        {
            Key.Enter or Key.KpEnter => "enter",
            Key.Escape => "escape",
            Key.Up => "up",
            Key.Left => "left",
            Key.Down => "down",
            Key.Right => "right",
            Key.Tab => "tab",
            _ => null,
        };
        if (key != null && controller.ProcessKey(key)) GetViewport().SetInputAsHandled();
    }

    /// <summary>Scripted runs: text into the shown box's m_textInput, as typed.</summary>
    public void Type(string text)
    {
        if (movie == null || !Shown) return;
        var h = movie.Host;
        var input = h.Get(controller.MovieBox(), "m_textInput");
        if (!Ms2.Avm2.Rt.IsNullish(input)) h.Set(input, "text", text);
    }

    /// <summary>Scripted runs: the shown box's m_checkbox<paramref name="n"/> ticked (its select event enables OK).</summary>
    public void Tick(int n)
    {
        if (movie == null || !Shown) return;
        var h = movie.Host;
        var check = h.Get(controller.MovieBox(), "m_checkbox" + n);
        if (!Ms2.Avm2.Rt.IsNullish(check)) h.Set(check, "selected", true);
    }

    /// <summary>A scripted click on the shown box's button <paramref name="n"/> (m_button0..2, the centre of its bounds).</summary>
    public void Click(int n)
    {
        if (movie == null || !Shown) return;
        var h = movie.Host;
        if (h.Child("m_button" + n, controller.MovieBox()) is not Ms2.Avm2.Flash.AsDisplayObject d) { GD.PushWarning($"message box: no m_button{n}"); return; }
        var b = d.GetBounds(null);
        movie.Player.Click(b.X + b.W / 2, b.Y + b.H / 2);
    }
}
