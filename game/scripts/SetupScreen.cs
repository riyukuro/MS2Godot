using Godot;

namespace Ms2Godot;

/// <summary>
/// Main scene. When the data folder and nif.xml are configured it hands over to the field scene; otherwise it asks for the
/// client's Data folder and for nif.xml (download it from the niftools nifxml repository, or point to a local file) and
/// saves the choice to user://settings.cfg.
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

    public override void _Ready()
    {
        if (Settings.Ready) { CallDeferred(nameof(Continue)); return; }
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
        Continue();
    }

    void Continue() => GetTree().ChangeSceneToFile("res://field.tscn");
}
