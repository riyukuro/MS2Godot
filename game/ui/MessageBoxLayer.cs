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
    /// PostQuitMessage(0).
    /// STILL-OPEN: when the game has not begun (DAT_143ab2942) the client first calls GetClientPlatform()->vfunc_22(0x3f2, "GameEnd")
    /// (uimessagebox.cpp:1229-1230; UICharacterListDialog::QuitGame :1284-1285 does the same).</summary>
    public static event Action GameExit;

    MessageBoxController controller;
    MovieDialog movie;
    As3View view;   // the box movie's view, found once when the movie is made
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
        instance.Build(new UiAssets(PackSet.Shared(dataDir)), any.GetTree().Root);
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
        controller.AnswerPartyInvite = a => Ms2Godot.Net.LiveSession.Instance?.AnswerPartyInvite(a);
        controller.AnswerGuildInvite = a => Ms2Godot.Net.LiveSession.Instance?.Guilds.AnswerInvite(a);
        controller.UnportedTag += (b, t) => GD.Print($"message box: button {b} of tag {t}: its handler is not ported");
        movie = new MovieDialog(assets, controller, new UiModel(GameTables.Shared(assets.Packs, assets.Language)), ScreenMode.PixelSize(root));
        AddChild(movie);
        // UIMessageBox::vfunc_13 @0x1419f7560 (uimessagebox.cpp:435) returns 0 with no box shown (FUN_1419f8270: m_count == 0),
        // and the typed characters (WM_CHAR, gfxmovieeventhandler.cpp:257) go to the focused field of the scene's dialog: the
        // idle layer, the root's last child, lets every key by
        view = movie.GetChildren().OfType<As3View>().FirstOrDefault();
        if (view != null) view.TakesKeys = () => Shown;
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

    /// <summary>The layer's movie (CUIService's one message box movie), while it is made.</summary>
    public static MovieDialog LayerMovie => instance != null && IsInstanceValid(instance) ? instance.movie : null;

    /// <summary>
    /// GFxManager::ResizeMovies covers this movie as every other (gfxmanager.cpp:158): under the HUD the HUD's resize
    /// restores it with its own movies (<see cref="MovieDialog.SceneResizes"/>); in another scene the layer runs the same
    /// restore on its movie alone (<see cref="MovieDialog.Restore"/>).
    /// </summary>
    void Relayout()
    {
        if (movie == null || MovieDialog.SceneResizes) return;
        MovieDialog.Restore(new[] { movie }, GetViewport().GetVisibleRect().Size);
    }

    public bool Shown => controller?.IsIdle == false;

    /// <summary>UIMessageBox::vfunc_13 @0x1419f7560 while a box is shown: Enter, Esc, the arrows and Tab go to the movie.
    /// The layer is under the root after the scene, so it sees the keys first. Enter is the main Return key only:
    /// CInputKeyHandler::IsEnterPressed @0x14116d890 reads DIK 0x1c or action 0x49408 (no default key has it; the keypad's
    /// Enter, DIK 0x9c, is unbound in DefaultKey.xml), and every dialog under the box reads DIK 0x1c too.</summary>
    public override void _Input(InputEvent e)
    {
        if (controller == null || controller.IsIdle) return;
        if (e is InputEventMouseButton or InputEventMouseMotion) { ForwardMouse(e); return; }
        if (e is not InputEventKey { Pressed: true, Echo: false } k) return;
        string key = k.Keycode switch
        {
            Key.Enter => "enter",
            Key.Escape => "escape",
            Key.Up => "up",
            Key.Left => "left",
            Key.Down => "down",
            Key.Right => "right",
            Key.Tab => "tab",
            _ => null,
        };
        var vp = GetViewport();   // taken before the dispatch, which can change the scene
        var box = controller.CurrentBox;     // read before the key, which can close it
        if (key != null && controller.ProcessKey(key))
        {
            if (key == "enter") LogEnter(box);
            if (IsInstanceValid(vp)) vp.SetInputAsHandled();
        }
    }

    /// <summary>The box that took an Enter, so a run shows when a typed chat line went to a box instead (the layer sees the
    /// keys before the scene's chat input).</summary>
    void LogEnter(MessageBoxData box)
    {
        string text = box?.Msg ?? "";
        if (text.Length > 40) text = text.Substring(0, 40) + "...";
        GD.Print(box == null
            ? "message box: Enter taken with no current box"
            : $"message box: Enter taken by box {box.Id} type {box.Type} tag \"{box.Extra}\" listener {box.Listener?.GetType().Name ?? "-"} callback {box.HasCallback} modaless {box.Modaless} drawn {view?.IsVisibleInTree() ?? false} text \"{text}\"");
    }

    /// <summary>
    /// The movie takes the mouse where its shapes are, before the scene's Controls (a Godot-built page such as the launcher,
    /// whose Stop controls would consume the click in the GUI stage and the box's buttons would never see it). The client hands
    /// the mouse to the movies top first and stops at the movie whose shapes are hit (gfxmanager.cpp:954, gfxmovie.cpp:357): a
    /// modal box's dim backdrop covers the screen, a modaless box (party invite, trade request) only its own area, and a
    /// point it does not cover goes on to the scene and the HUD. A miss is not forwarded: the view still gets the event once
    /// through its own _UnhandledInput.
    /// </summary>
    void ForwardMouse(InputEvent e)
    {
        if (view == null) return;
        var at = e is InputEventMouseButton b ? b.Position : ((InputEventMouseMotion)e).Position;
        if (!view.HitsShapesAt(at)) return;
        var vp = GetViewport();
        view._UnhandledInput(e);
        if (IsInstanceValid(vp)) vp.SetInputAsHandled();
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
