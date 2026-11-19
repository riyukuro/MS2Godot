using Godot;

namespace Ms2Godot;

/// <summary>
/// Main scene. When the data folder and nif.xml are configured it hands over to the field scene; otherwise it asks for the
/// client's Data folder and for nif.xml (download it from the niftools nifxml repository, or point to a local file) and
/// saves the choice to user://settings.cfg. Then, when the UI code for this client's Gfx.m2d is not built yet, it builds it
/// with a progress bar (<see cref="UiCode"/>), loads it and goes on.
/// </summary>
public partial class SetupScreen : Control
{
    const string NifXmlTag = "970a623";   // a master commit of niftools/nifxml; the v0.9.x releases lack types the MS2 files need
    const string NifXmlRef = "970a6238218a106daaeb89a61bcda0eeaf9d08c4";
    const string NifXmlUrl = "https://raw.githubusercontent.com/niftools/nifxml/" + NifXmlRef + "/nif.xml";
    const string NifLicenseUrl = "https://raw.githubusercontent.com/niftools/nifxml/" + NifXmlRef + "/LICENSE";
    const string NifNotice =
        "nif.xml is the niftools project's description of the NIF file format (https://github.com/niftools/nifxml). " +
        "It is not part of MS2Godot. Downloading fetches it from that repository (commit " + NifXmlTag + ") together with the " +
        "repository's LICENSE file; both are saved next to the game's user data, and their terms apply to that copy.";

    string dataDir, nifPath;
    Label dataLabel, nifLabel, status;
    Button start, download;
    FileDialog dirDialog, fileDialog;
    HttpRequest http;
    string pending;
    System.Threading.Tasks.Task<Ms2.UiGen.UiGenerator.Result> uiBuild;
    ProgressBar uiBar;
    Label uiLabel;
    volatile string uiPhase = "Checking the UI code";
    double uiFrac;

    public override void _Ready()
    {
        if (Settings.Ready) { CallDeferred(nameof(PrepareUiCode)); return; }
        dataDir = Settings.DataDir;
        nifPath = Settings.NifXml;
        SetAnchorsPreset(LayoutPreset.FullRect);

        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(760, 0) };
        box.AddThemeConstantOverride("separation", 10);
        center.AddChild(box);

        box.AddChild(new Label { Text = "MS2Godot setup" });
        box.AddChild(new Label { Text = "MS2Godot contains no game data. Point it at your own MapleStory 2 client.", AutowrapMode = TextServer.AutowrapMode.WordSmart });

        box.AddChild(new Label { Text = "1. The client's Data folder (the one holding Xml.m2d and Resource/Model):" });
        var row1 = new HBoxContainer();
        dataLabel = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true };
        var b1 = new Button { Text = "Choose folder..." };
        b1.Pressed += () => dirDialog.PopupCenteredRatio(0.7f);
        row1.AddChild(dataLabel); row1.AddChild(b1);
        box.AddChild(row1);

        box.AddChild(new Label { Text = "2. nif.xml (the NIF format description the model reader uses):" });
        box.AddChild(new Label { Text = NifNotice, AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = new Color(1, 1, 1, 0.75f) });
        var row2 = new HBoxContainer();
        nifLabel = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true };
        download = new Button { Text = "Download " + NifXmlTag };
        download.Pressed += StartDownload;
        var b2 = new Button { Text = "Choose local file..." };
        b2.Pressed += () => fileDialog.PopupCenteredRatio(0.7f);
        row2.AddChild(nifLabel); row2.AddChild(download); row2.AddChild(b2);
        box.AddChild(row2);

        status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        box.AddChild(status);
        start = new Button { Text = "Start", Disabled = true };
        start.Pressed += OnStart;
        box.AddChild(start);

        dirDialog = new FileDialog { FileMode = FileDialog.FileModeEnum.OpenDir, Access = FileDialog.AccessEnum.Filesystem, UseNativeDialog = false, Title = "Client Data folder" };
        dirDialog.DirSelected += d => { dataDir = d; Refresh(); };
        fileDialog = new FileDialog { FileMode = FileDialog.FileModeEnum.OpenFile, Access = FileDialog.AccessEnum.Filesystem, UseNativeDialog = false, Title = "nif.xml", Filters = new[] { "*.xml ; XML" } };
        fileDialog.FileSelected += f => { nifPath = f; Refresh(); };
        AddChild(dirDialog);
        AddChild(fileDialog);
        http = new HttpRequest();
        AddChild(http);
        http.RequestCompleted += OnHttp;
        Refresh();
    }

    void Refresh()
    {
        var dp = Settings.DataProblem(dataDir);
        var np = Settings.NifProblem(nifPath);
        dataLabel.Text = string.IsNullOrEmpty(dataDir) ? "(not set)" : dataDir;
        nifLabel.Text = string.IsNullOrEmpty(nifPath) ? "(not set)" : nifPath;
        if (pending == null) status.Text = dp != null ? "Data folder: " + dp : np != null ? "nif.xml: " + np : "Ready.";
        start.Disabled = dp != null || np != null;
        download.Disabled = pending != null;
    }

    void StartDownload()
    {
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("user://nifxml"));
        pending = "nif";
        status.Text = "Downloading nif.xml...";
        Refresh();
        var err = http.Request(NifXmlUrl);
        if (err != Error.Ok) { pending = null; status.Text = "Download failed to start: " + err; Refresh(); }
    }

    void OnHttp(long result, long code, string[] headers, byte[] body)
    {
        if (result != (long)HttpRequest.Result.Success || code != 200)
        {
            status.Text = $"Download failed (result {result}, HTTP {code}). Use \"Choose local file\" instead.";
            pending = null; Refresh(); return;
        }
        if (pending == "nif")
        {
            using (var f = Godot.FileAccess.Open(Settings.DownloadedNifXml, Godot.FileAccess.ModeFlags.Write)) f.StoreBuffer(body);
            pending = "license";
            if (http.Request(NifLicenseUrl) != Error.Ok) OnHttp((long)HttpRequest.Result.Success, 200, headers, System.Array.Empty<byte>());
            return;
        }
        if (body.Length > 0)
            using (var f = Godot.FileAccess.Open("user://nifxml/LICENSE", Godot.FileAccess.ModeFlags.Write)) f.StoreBuffer(body);
        pending = null;
        nifPath = ProjectSettings.GlobalizePath(Settings.DownloadedNifXml);
        Refresh();
    }

    void OnStart()
    {
        var err = Settings.Save(dataDir, nifPath);
        if (err != Error.Ok) { status.Text = "Could not save the settings: " + err; return; }
        // the environment overrides the file; nothing to do for it here
        foreach (var c in GetChildren()) c.QueueFree();
        PrepareUiCode();
    }

    /// <summary>
    /// The UI code of this client: loaded when built, else built now on a worker thread (translating every UI movie's
    /// ActionScript and compiling it takes about 20 seconds). A failed build is logged and the game goes on: the movies
    /// then run in the interpreter.
    /// </summary>
    void PrepareUiCode()
    {
        var data = Settings.DataDir;
        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(560, 0) };
        center.AddChild(box);
        uiLabel = new Label { Text = "Building the UI from your client's movies (first launch with this client)..." };
        uiBar = new ProgressBar { MinValue = 0, MaxValue = 1, Step = 0.001 };
        box.AddChild(uiLabel);
        box.AddChild(uiBar);
        Ms2.Formats.Pack.PackSet packs = null;
        DataCache.QuestConfig quests = null;
        // the archives' index files (2 MB) decide the entry lists a session reads: hashed again at every setup, so a changed
        // index never opens with an old list (the archives themselves are hashed again before a build reads them)
        try { foreach (var a in Ms2.Formats.Pack.PackIndex.Archives(data)) UiCode.Stamps.Hash(System.IO.Path.Combine(data, a + ".m2h"), reread: true); }
        catch (System.Exception e) when (e is System.IO.IOException or System.UnauthorizedAccessException) { GD.PushWarning("table cache: " + e.Message); }
        try { packs = Ms2.Formats.Pack.PackSet.Shared(data); quests = DataCache.QuestConfig.Current(packs); }
        catch (System.Exception e) { GD.PushWarning("table cache: " + e.Message); }
        uiBuild = System.Threading.Tasks.Task.Run(() =>
        {
            void progress(string phase, double f) { uiPhase = phase; uiFrac = f; }
            var r = UiCode.Built(data) ? null : UiCode.Build(data, progress);
            // its machine code, compiled ahead of time for this machine, so no dialog's first open waits for the JIT
            if (UiCode.Built(data))
            {
                uiPhase = "Compiling the UI code for this machine";
                double? s = Precompiled.Compile(UiCode.DllPath(data));
                if (s >= 0) GD.Print($"UI code precompiled in {s:F1} s");
            }
            // the client tables the UI reads, parsed once per install (after the code: a new code key starts a new folder)
            uiPhase = "Building the UI tables";
            try
            {
                double s = UiCode.BuildTables(data);
                if (s >= 0) GD.Print($"UI tables built: additional effects in {s:F1} s");
            }
            catch (System.Exception e) { GD.PushWarning("UI tables build failed: " + e.Message); }
            // the client's tables the field and HUD read, once per install (UiCode.Prune keeps them with their client)
            if (packs != null)
                try
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var built = DataCache.Build(packs, quests, progress);
                    if (built.Count > 0) { GD.Print($"table cache built: {string.Join(", ", built)} in {sw.Elapsed.TotalSeconds:F1} s"); if (r == null) UiCode.Prune(); }
                    else if (Ui.PerfLog.On) GD.Print($"table cache: all current, checked in {sw.Elapsed.TotalMilliseconds:F0} ms");
                }
                catch (System.Exception e) { GD.PushWarning("table cache build failed: " + e.GetBaseException().Message); }
            return r;
        });
        SetProcess(true);
    }

    public override void _Process(double delta)
    {
        if (uiBuild == null) return;
        if (!uiBuild.IsCompleted)
        {
            uiBar.Value = uiFrac;
            uiLabel.Text = uiPhase + "...";
            return;
        }
        var task = uiBuild;
        uiBuild = null;
        if (task.IsFaulted) GD.PushWarning("UI code build failed: " + task.Exception?.GetBaseException().Message);
        else if (task.Result is { } r)
        {
            GD.Print($"UI code built: {r.Bodies} bodies, {r.Distinct} distinct, {r.MoviesAllCompiled}/{r.MoviesWithCode} movies, " +
                     $"{r.DllBytes / 1048576.0:F1} MB, read {r.ReadSeconds:F1} s, translate {r.TranslateSeconds:F1} s, compile {r.CompileSeconds:F1} s");
            foreach (var e in r.Errors) GD.PushWarning("UI code: " + e);
        }
        try { UiCode.Load(Settings.DataDir); }
        catch (System.Exception e) { GD.PushWarning("UI code load failed: " + e.Message); }
        Continue();
    }

    void Continue() => GetTree().ChangeSceneToFile("res://flow/flow.tscn");
}
