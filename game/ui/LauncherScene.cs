using System.Diagnostics;
using Godot;
using Ms2.Ui.Launch;

namespace Ms2Godot.Ui;

/// <summary>
/// MS2Godot's own pre-login page: single player (the offline flow, unchanged) or a saved login server. The client has no
/// such page, so there is nothing to cite; this screen is native Godot Controls, not a client movie.
/// The list is user://servers.json, edited in the Add/Edit dialog and checked on open and on Refresh by a TCP connect
/// off the main thread with a short timeout. Enter activates the focused control; Esc closes the dialog, or quits here.
/// Args after `--`: --launcher-shots DIR saves the empty list, a two-entry list and the add dialog, then quits.
/// </summary>
public partial class LauncherScene : Control
{
    /// <summary>Single player was pressed: the flow starts the offline client.</summary>
    public static event Action SinglePlayer;
    /// <summary>A saved entry's Connect was pressed: the flow starts the live mode against its address.</summary>
    public static event Action<string, int> Connect;

    /// <summary>A connect attempt gives up after this long; the check is a reachability probe, not a login.</summary>
    const int ProbeMilliseconds = 800;

    // The game's UI fallback font list (UiAssets.Fallback); the launcher drew no client movie, so it borrows neither
    // the movie font nor its art.
    static readonly string[] FontNames = { "Noto Sans CJK KR", "Noto Sans CJK JP", "Noto Sans", "DejaVu Sans", "sans-serif" };

    static readonly Color Background = new("14181f");
    static readonly Color PanelColor = new("1e242e");
    static readonly Color MutedColor = new("98a4b5");
    static readonly Color ErrorColor = new("ff7a7a");
    static readonly Color ReachableColor = new("7fd8a2");
    static readonly Color UnreachableColor = new("f0a35e");

    sealed class Row
    {
        public Label Status;
        public Button Connect;
        public Button Edit;
        public Button Remove;
    }

    ServerList store;
    string storePath;
    VBoxContainer rowsBox;
    Label empty, summary;
    Button single, add, refresh;
    readonly List<Row> rows = new();

    Control overlay;
    Label dialogTitle, dialogError;
    LineEdit nameEdit, addressEdit;
    Button dialogOk;
    int editingIndex = -1;

    string shotDir;
    int generation;
    // owned by the scene: cancelled when it leaves the tree, so a pending probe stops and writes nothing to freed controls
    readonly CancellationTokenSource life = new();

    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--launcher-shots") shotDir = args[i + 1];

        storePath = ProjectSettings.GlobalizePath("user://" + ServerList.DefaultFileName);
        store = ServerList.Load(storePath);

        SetAnchorsPreset(LayoutPreset.FullRect);
        Theme = MakeTheme();
        Build();
        RebuildRows();
        single.GrabFocus();
        if (shotDir != null) { DirAccess.MakeDirRecursiveAbsolute(shotDir); _ = ShotScript(); }
    }

    // ---- layout ------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The game's UI loads its font from the client's Gfx font library into the AS3 movies; there is no native-UI font
    /// theme, so the launcher uses the system font list that backs those movies and sizes its own Controls in a theme.
    /// </summary>
    static Theme MakeTheme()
    {
        var theme = new Theme
        {
            DefaultFont = new SystemFont
            {
                FontNames = FontNames,
                Antialiasing = TextServer.FontAntialiasing.Gray,
            },
            DefaultFontSize = 17,
        };
        var panel = new StyleBoxFlat { BgColor = PanelColor, CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8, CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8 };
        panel.SetContentMarginAll(28);
        theme.SetStylebox("panel", "PanelContainer", panel);
        return theme;
    }

    void Build()
    {
        var backdrop = new ColorRect { Color = Background, MouseFilter = MouseFilterEnum.Ignore };
        backdrop.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(backdrop);

        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var panel = new PanelContainer { CustomMinimumSize = new Vector2(940, 0) };
        center.AddChild(panel);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 12);
        panel.AddChild(box);

        var title = new Label { Text = "MS2Godot" };
        title.AddThemeFontSizeOverride("font_size", 30);
        box.AddChild(title);

        var subtitle = Muted("Choose a login server, or play offline. The server list is kept in your user data folder.");
        box.AddChild(subtitle);

        single = new Button { Text = "Single player" };
        single.CustomMinimumSize = new Vector2(0, 42);
        single.Pressed += () => SinglePlayer?.Invoke();
        box.AddChild(single);

        box.AddChild(new HSeparator());

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 8);
        var headerLabel = new Label { Text = "Servers", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        headerLabel.AddThemeFontSizeOverride("font_size", 20);
        header.AddChild(headerLabel);
        add = new Button { Text = "Add server" };
        add.Pressed += OpenAddDialog;
        header.AddChild(add);
        refresh = new Button { Text = "Refresh" };
        refresh.Pressed += RefreshStatuses;
        header.AddChild(refresh);
        box.AddChild(header);

        empty = Muted("No servers saved yet. Add one with Add server.");
        box.AddChild(empty);

        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(0, 286), SizeFlagsVertical = SizeFlags.ExpandFill };
        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        box.AddChild(scroll);
        rowsBox = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        rowsBox.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(rowsBox);

        summary = Muted("");
        box.AddChild(summary);

        var hint = Muted("Tab: move between controls  ·  Enter: connect or start  ·  Esc: quit");
        hint.AddThemeFontSizeOverride("font_size", 14);
        box.AddChild(hint);

        BuildDialog();
    }

    void BuildDialog()
    {
        overlay = new Control { Visible = false, MouseFilter = MouseFilterEnum.Stop };
        overlay.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(overlay);

        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.62f), MouseFilter = MouseFilterEnum.Stop };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        overlay.AddChild(dim);

        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        overlay.AddChild(center);

        var panel = new PanelContainer { CustomMinimumSize = new Vector2(520, 0) };
        center.AddChild(panel);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 10);
        panel.AddChild(box);

        dialogTitle = new Label { Text = "Add server" };
        dialogTitle.AddThemeFontSizeOverride("font_size", 22);
        box.AddChild(dialogTitle);
        box.AddChild(Muted("The address is the login server, as host:port."));

        box.AddChild(new Label { Text = "Name" });
        nameEdit = new LineEdit { PlaceholderText = "Name" };
        nameEdit.CustomMinimumSize = new Vector2(0, 38);
        box.AddChild(nameEdit);

        box.AddChild(new Label { Text = "Address" });
        addressEdit = new LineEdit { PlaceholderText = "host:port" };
        addressEdit.CustomMinimumSize = new Vector2(0, 38);
        addressEdit.TextSubmitted += _ => OnDialogOk();
        box.AddChild(addressEdit);

        dialogError = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        dialogError.AddThemeColorOverride("font_color", ErrorColor);
        box.AddChild(dialogError);

        var buttons = new HBoxContainer();
        buttons.Alignment = BoxContainer.AlignmentMode.End;
        buttons.AddThemeConstantOverride("separation", 8);
        var cancel = new Button { Text = "Cancel" };
        cancel.Pressed += CloseDialog;
        dialogOk = new Button { Text = "Add" };
        dialogOk.Pressed += OnDialogOk;
        buttons.AddChild(cancel);
        buttons.AddChild(dialogOk);
        box.AddChild(buttons);

        nameEdit.TextSubmitted += _ => addressEdit.GrabFocus();
    }

    static Label Muted(string text)
    {
        var l = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        l.AddThemeColorOverride("font_color", MutedColor);
        return l;
    }

    // ---- the list ----------------------------------------------------------------------------------------------------

    void RebuildRows()
    {
        generation++;                          // results from an older check belong to rows that are gone
        for (int i = 0; i < rowsBox.GetChildCount(); i++) rowsBox.GetChild(i).QueueFree();
        rows.Clear();
        for (int i = 0; i < store.Count; i++) rows.Add(AddRow(i, store.Entries[i]));
        empty.Visible = store.Count == 0;
        summary.Text = store.Count == 0 ? "" : $"{store.Count} server{(store.Count == 1 ? "" : "s")} saved.";
        RefreshStatuses();
    }

    Row AddRow(int index, ServerEntry entry)
    {
        var row = new Row();
        var box = new HBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        rowsBox.AddChild(box);

        var name = new Label { Text = entry.Name, ClipText = true, CustomMinimumSize = new Vector2(200, 0), TooltipText = entry.Name, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        box.AddChild(name);

        var address = new Label { Text = entry.Address, ClipText = true, CustomMinimumSize = new Vector2(170, 0) };
        address.AddThemeColorOverride("font_color", MutedColor);
        box.AddChild(address);

        row.Status = new Label { Text = "", CustomMinimumSize = new Vector2(180, 0) };
        row.Status.AddThemeColorOverride("font_color", MutedColor);
        box.AddChild(row.Status);

        row.Connect = new Button { Text = "Connect" };
        row.Connect.CustomMinimumSize = new Vector2(92, 34);
        row.Connect.Pressed += () => ConnectPressed(index);
        box.AddChild(row.Connect);

        row.Edit = new Button { Text = "Edit" };
        row.Edit.CustomMinimumSize = new Vector2(70, 34);
        row.Edit.Pressed += () => OpenEditDialog(index);
        box.AddChild(row.Edit);

        row.Remove = new Button { Text = "Remove" };
        row.Remove.CustomMinimumSize = new Vector2(96, 34);
        row.Remove.Pressed += () => RemovePressed(index);
        box.AddChild(row.Remove);
        return row;
    }

    void ConnectPressed(int index)
    {
        if (index < 0 || index >= store.Count) return;
        var entry = store.Entries[index];
        Connect?.Invoke(entry.Host, entry.Port);
    }

    void RemovePressed(int index)
    {
        if (!store.RemoveAt(index)) return;
        bool saved = store.Save(storePath, out string problem);
        RebuildRows();
        if (!saved) summary.Text = problem;
    }

    // ---- reachability ------------------------------------------------------------------------------------------------

    /// <summary>
    /// One TCP connect per entry, all at once, off the main thread with <see cref="ProbeMilliseconds"/>. Only a saved
    /// entry is ever contacted, and only the connect: no login and no packet. Runs on open and on Refresh, never per frame.
    /// </summary>
    async void RefreshStatuses()
    {
        int gen = ++generation;
        refresh.Disabled = true;
        foreach (var row in rows) { row.Status.Text = "checking"; row.Status.AddThemeColorOverride("font_color", MutedColor); }
        var tasks = new List<Task>(rows.Count);
        for (int i = 0; i < rows.Count; i++) tasks.Add(Probe(i, gen));
        await Task.WhenAll(tasks);
        if (gen != generation || !IsInstanceValid(refresh) || !IsInstanceValid(summary)) return;
        refresh.Disabled = false;
        summary.Text = rows.Count == 0 ? "" : $"{rows.Count} server{(rows.Count == 1 ? "" : "s")} saved.";
    }

    public override void _ExitTree()
    {
        generation++;
        life.Cancel();
    }

    async Task Probe(int index, int gen)
    {
        if (index < 0 || index >= store.Count) return;
        var entry = store.Entries[index];
        var watch = Stopwatch.StartNew();
        bool reachable = false;
        try
        {
            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(life.Token);
            cancel.CancelAfter(ProbeMilliseconds);
            using var client = new System.Net.Sockets.TcpClient();
            await client.ConnectAsync(entry.Host, entry.Port, cancel.Token).ConfigureAwait(true);
            reachable = client.Connected;
        }
        catch (Exception)
        {
            reachable = false;
        }
        watch.Stop();
        if (gen != generation || index >= rows.Count) return;
        var row = rows[index];
        if (!IsInstanceValid(row.Status)) return;
        // the time of a failed attempt is only how long the timeout ran, so it is shown for a reachable entry alone
        row.Status.Text = reachable ? "reachable · " + watch.ElapsedMilliseconds + " ms" : "unreachable";
        row.Status.AddThemeColorOverride("font_color", reachable ? ReachableColor : UnreachableColor);
    }

    // ---- the add/edit dialog -----------------------------------------------------------------------------------------

    void OpenAddDialog()
    {
        editingIndex = -1;
        dialogTitle.Text = "Add server";
        dialogOk.Text = "Add";
        nameEdit.Text = "";
        addressEdit.Text = "";
        dialogError.Text = "";
        overlay.Visible = true;
        nameEdit.GrabFocus();
    }

    void OpenEditDialog(int index)
    {
        if (index < 0 || index >= store.Count) return;
        var entry = store.Entries[index];
        editingIndex = index;
        dialogTitle.Text = "Edit server";
        dialogOk.Text = "Save";
        nameEdit.Text = entry.Name;
        addressEdit.Text = entry.Address;
        dialogError.Text = "";
        overlay.Visible = true;
        nameEdit.GrabFocus();
    }

    void CloseDialog() => overlay.Visible = false;

    void OnDialogOk()
    {
        bool ok = editingIndex < 0
            ? store.Add(nameEdit.Text, addressEdit.Text, out string problem)
            : store.Edit(editingIndex, nameEdit.Text, addressEdit.Text, out problem);
        if (!ok)
        {
            dialogError.Text = problem;             // the dialog stays open with the message
            return;
        }
        if (!store.Save(storePath, out string saveProblem)) summary.Text = saveProblem;
        CloseDialog();
        RebuildRows();
        add.GrabFocus();
    }

    /// <summary>Esc backs out: it closes the dialog, and on the screen itself it quits (there is no page before this one).</summary>
    public override void _UnhandledInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }) return;
        var viewport = GetViewport();
        if (overlay.Visible) CloseDialog();
        else GetTree().Quit(0);
        if (IsInstanceValid(viewport)) viewport.SetInputAsHandled();
    }

    // ---- scripted shots ----------------------------------------------------------------------------------------------

    async Task ShotScript()
    {
        await Frames(15);
        Shot("01_launcher_empty.png");
        // both addresses are reserved for documentation and tests, so no run ever reaches a real server
        store.Add("Test range A", "192.0.2.1:6553", out _);
        store.Add("Test range B", "198.51.100.7:6553", out _);
        RebuildRows();
        await Until(() => !refresh.Disabled, 900);
        await Frames(5);
        Shot("02_launcher_two_servers.png");
        OpenAddDialog();
        nameEdit.Text = "Test range C";
        addressEdit.Text = "192.0.2.9:6553";
        await Frames(5);
        Shot("03_launcher_add.png");
        GetTree().Quit(0);
    }

    async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    async Task Until(Func<bool> done, int maxFrames)
    {
        for (int i = 0; i < maxFrames && !done(); i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    void Shot(string file)
    {
        GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(shotDir, file));
        GD.Print("launcher: shot " + file);
    }
}
