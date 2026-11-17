using Godot;
using Ms2.Formats.Pack;
using Ms2Godot.Ui.Contracts;
using C = Ms2Godot.Ui.Contracts.UICharacterCreateSelectDialogContract;

namespace Ms2Godot.Ui.Create;

/// <summary>
/// The job pick page (uicharactercreateselectdialog), ported by hand from the movie's AS3 (CharacterCreateSelectContent,
/// CharacterCreateJobButton) and the C++ dialog (UICharacterCreateSelectDialog: OnLoadComplete builds initData, onChangeTab
/// answers changeTab, onClickCreate pops the page down and the scene enters the create map with that job).
/// setMovieAndPlay plays the job's preview movie (characterCreateSelect.xml movie, e.g. jobPreview_80.usm) looped in
/// m_jobInfo.m_movie, sized to m_jobInfo.m_movieBg less 2 units.
/// </summary>
public sealed class JobSelectLogic
{
    const string Movie = C.Movie;
    const int ButtonWidth = 174, Spacing = 3;          // initData: m_tab / m_tab2 buttonWidth and spacing

    readonly UiMovie movie;
    readonly MovieClipNode root, content, tab, tab2;
    readonly CreateTables tables;
    readonly PackSet packs;
    VideoPlayerNode player;
    readonly List<(MovieClipNode Clip, ButtonWidget Button, CreateTables.Job Job, int Index)> buttons = new();
    public int SelectedJob { get; private set; }
    public ButtonWidget CreateButton, PrevButton;
    /// <summary>onClickCreate(jobCode): the page pops down and the create page opens with the job.</summary>
    public event Action<int> Create;
    public event Action Prev;

    /// <summary>The preview movie player (null until a job with a movie is shown).</summary>
    public VideoPlayerNode Player => player;

    public JobSelectLogic(UiMovie movie, MovieClipNode root, CreateTables tables, PackSet packs = null)
    {
        this.movie = movie; this.root = root; this.tables = tables; this.packs = packs;
        content = root.FindClip(C.Instances.M_content) ?? throw new InvalidOperationException("job select: missing m_content");
        tab = root.FindClip(C.Instances.M_content_m_tab);
        tab2 = root.FindClip(C.Instances.M_content_m_tab2);
        CreateButton = content.FindClip("m_create")?.Widget as ButtonWidget;
        PrevButton = root.FindClip("m_prev")?.Widget as ButtonWidget;
        IconButtons.Setup(movie, root.FindClip("m_prev"), "UICharacterCreateSelectDialog_btn_icon_prev", "$s_charcreateselect_btn_prev");
        if (CreateButton != null)
        {
            CreateButton.Set("label", "$s_charcreateselect_btn_create_char");
            CreateButton.Click += _ => ClickCreate();
        }
        if (PrevButton != null) PrevButton.Click += _ => { UiBridge.Raise(Movie, C.ToHost.OnClickPrev); Prev?.Invoke(); };
    }

    /// <summary>
    /// UICharacterCreateSelectDialog.alignment for a screen of <paramref name="screen"/> pixels with the dialog drawn at
    /// <paramref name="scale"/> pixels per stage unit (root at the screen origin): m_content (its m_size box, 2400 x 1200)
    /// is centred, m_fullBg covers the whole screen and m_prev sits at the bottom left corner.
    /// </summary>
    /// <summary>
    /// CharacterCreateSelect.alignment on a stage of <paramref name="stage"/> units, the dialog scaled by the lobby ratio
    /// <paramref name="r"/> (applyScale): m_content centred on the stage, m_fullBg from the stage origin over the stage plus 2,
    /// and btnsAlignment(m_prev, getUIScale, m_prev.height, globalToLocal(0, stageHeight)) at the bottom left. All in the
    /// dialog's units (stage / r).
    /// </summary>
    public void Align(Vector2 stage, float r)
    {
        var inDialog = stage / r;
        const float SizeW = 2400, SizeH = 1200;     // m_content.getSize(): m_size, 240 x 120 at scale 10
        if (root.ChildNamed(C.Instances.M_content) is { } c)
            c.Override = new Transform2D(0, new Vector2((inDialog.X - SizeW) / 2, (inDialog.Y - SizeH) / 2));
        if (root.ChildNamed(C.Instances.M_fullBg) is { } bg && bg.Node is MovieClipNode bgClip)
        {
            var b = bgClip.Bounds;
            float w = b.Size.X > 0 ? b.Size.X : 1922.073f, h = b.Size.Y > 0 ? b.Size.Y : 1082.034f;
            // globalToLocal(0, 0) is the dialog's origin; width (stageWidth + 2) / actualScaleX, height likewise
            bg.Override = new Transform2D(new Vector2((stage.X + 2) / r / w, 0), new Vector2(0, (stage.Y + 2) / r / h), Vector2.Zero);
        }
        if (root.ChildNamed("m_prev") is { } prev && prev.Node is MovieClipNode prevClip)
            prev.Override = new Transform2D(0, LobbyLayout.BtnsAlignment(new Vector2(0, inDialog.Y), prevClip.Bounds.Size.Y, stage));
        foreach (var clip in root.AllClips()) clip.GotoFrame(clip.Frame);
        SizePlayer();
    }

    /// <summary>OnLoadComplete -> initData(arrTab, selectedTabIndex, selectedJobData).</summary>
    public void InitData(int startJob)
    {
        foreach (var b in buttons) { b.Clip.GetParent()?.RemoveChild(b.Clip); b.Clip.QueueFree(); }
        buttons.Clear();
        var jobs = tables.Jobs;
        bool twoRows = jobs.Count > 6;
        int selectedTab = Math.Max(0, jobs.FindIndex(j => j.JobCode == startJob));
        int n1 = 0, n2 = 0;
        for (int i = 0; i < jobs.Count; i++)
        {
            // more than six tabs: even indices go to the top bar, odd ones to the second
            var bar = twoRows && i % 2 == 0 ? tab : tab2;
            int slot = twoRows && i % 2 == 0 ? n1++ : n2++;
            var clip = movie.BuildClass("UICharacterCreateSelectDialog_tab_job", bar.sx, bar.sy);
            if (clip == null) break;
            clip.Position = new Vector2(slot * (ButtonWidth + Spacing), 0);
            bar.AddChild(clip);
            UiWidgets.Attach(clip);
            var w = clip.Widget as ButtonWidget;
            if (w != null) w.Toggle = true;          // CLIK ButtonBar renderers toggle
            SetButtonData(clip, w, jobs[i]);
            int index = i;
            if (w != null) w.Click += _ => ChangeTab(index, true);
            buttons.Add((clip, w, jobs[i], i));
        }
        ChangeTab(selectedTab, false);
        content.GotoLabel("show", true);
        root.FindClip(C.Instances.M_fullBg)?.GotoLabel("show", true);
    }

    /// <summary>CharacterCreateJobButton.data: name, enabled (m_bg "_job*10" or "_dis"), event mark.</summary>
    static void SetButtonData(MovieClipNode clip, ButtonWidget w, CreateTables.Job job)
    {
        clip.SetNamedText("m_name", job.Name);
        foreach (var c in clip.Children)
            if (c.Name == "m_name" && c.Node is TextNode t) t.Text = job.Name;
        if (w != null) { w.Enabled = job.Enabled; w.Refresh(); }
        var bg = clip.FindClip("m_bg");
        string label = "_" + job.JobCode * 10;
        bg?.GotoLabel(job.Enabled || !(bg?.HasLabel(label + "_dis") ?? false) ? label : label + "_dis");
        clip.SetNamedVisible("m_event", job.Event);
    }

    /// <summary>onChangeTab / onChangeTab2: one selected renderer over both bars; the host answers with changeTab(jobData).</summary>
    public void ChangeTab(int index, bool raise)
    {
        if (index < 0 || index >= buttons.Count) return;
        foreach (var b in buttons) b.Button?.SetSelected(b.Index == index, false);
        var job = buttons[index].Job;
        SelectedJob = job.JobCode;
        if (raise) UiBridge.Raise(Movie, C.ToHost.OnChangeTab, (uint)index, job.JobCode);
        SetJobData(job);
        if (raise) content.GotoLabel("changeTab", true);
    }

    /// <summary>setJobData: name, description (html), job icon and the large job image at the "_job*10" frame.</summary>
    void SetJobData(CreateTables.Job job)
    {
        var info = root.FindClip(C.Instances.M_content_m_jobInfo);
        string label = "_" + job.JobCode * 10;
        info?.SetNamedText("m_name", job.Name);
        if (info?.Children.FirstOrDefault(c => c.Name == "m_desc")?.Node is TextNode desc) FlashHtml.Set(desc, job.Desc);
        info?.FindClip("m_icon")?.GotoLabel(label);
        root.FindClip(C.Instances.M_content_m_jobImage)?.GotoLabel(label);
        SetMovieAndPlay(job.Movie);
    }

    /// <summary>
    /// setMovieAndPlay: one VideoPlayer, made on first use at (0, 0) in m_jobInfo.m_movie, ceil(m_movieBg size - 2)
    /// (m_jobInfo units), looped; each job change plays that job's movie.
    /// </summary>
    void SetMovieAndPlay(string name)
    {
        if (packs == null || string.IsNullOrEmpty(name)) return;
        var holder = root.FindClip(C.Instances.M_content_m_jobInfo)?.FindClip("m_movie");
        if (holder == null) return;
        if (player == null)
        {
            player = new VideoPlayerNode();
            holder.AddChild(player);
        }
        SizePlayer();
        player.Play(packs, name, loop: true);
    }

    /// <summary>The player's size: m_movieBg's (in m_jobInfo units) less 2, rounded up, expressed in m_movie units.</summary>
    void SizePlayer()
    {
        var info = root.FindClip(C.Instances.M_content_m_jobInfo);
        if (player == null || info?.FindClip("m_movieBg") is not { } bg || player.GetParent() is not Node2D holder) return;
        var infoScale = info.GetGlobalTransform().Scale;
        var holderScale = holder.GetGlobalTransform().Scale;
        var r = bg.GlobalBounds();
        if (infoScale.X == 0 || infoScale.Y == 0 || holderScale.X == 0 || holderScale.Y == 0 || r.Size.X <= 0) return;
        var w = Math.Ceiling(r.Size.X / infoScale.X - 2);
        var h = Math.Ceiling(r.Size.Y / infoScale.Y - 2);
        player.Size = new Vector2((float)(w * infoScale.X / holderScale.X), (float)(h * infoScale.Y / holderScale.Y));
    }

    public void ClickCreate()
    {
        if (SelectedJob == 0) return;
        UiBridge.Raise(Movie, C.ToHost.OnClickCreate, SelectedJob);
        // UICharacterCreateSelectDialog::FUN_140f2f9c0: popdown, then OnEndPopdown -> CSceneStateMulti::SelectCreateJob
        content.GotoLabel("hide", true);
        root.FindClip(C.Instances.M_fullBg)?.GotoLabel("hide", true);
        player?.Stop();
        Create?.Invoke(SelectedJob);
    }
}

/// <summary>The few html tags the game's strings use (FONT color, BR, B) shown with a RichTextLabel over a movie text field.</summary>
public static class FlashHtml
{
    public static void Set(TextNode t, string html)
    {
        var rich = t.GetNodeOrNull<RichTextLabel>("Html");
        if (rich == null)
        {
            var l = t.Label;
            rich = new RichTextLabel
            {
                Name = "Html", BbcodeEnabled = true, FitContent = false, ScrollActive = false, MouseFilter = Control.MouseFilterEnum.Ignore,
                Position = l.Position, Size = l.Size, Scale = l.Scale, AutowrapMode = TextServer.AutowrapMode.WordSmart,
            };
            rich.AddThemeFontOverride("normal_font", l.LabelSettings.Font);
            rich.AddThemeFontSizeOverride("normal_font_size", l.LabelSettings.FontSize);
            rich.AddThemeColorOverride("default_color", l.LabelSettings.FontColor);
            rich.AddThemeConstantOverride("line_separation", 0);
            t.AddChild(rich);
            l.Visible = false;
        }
        rich.Text = ToBbcode(html ?? "");
    }

    public static string ToBbcode(string html)
    {
        var s = html.Replace("\\n", "\n").Replace("[", "[lb]");
        s = System.Text.RegularExpressions.Regex.Replace(s, "<FONT[^>]*color=['\"]?(#[0-9A-Fa-f]{6})['\"]?[^>]*>", "[color=$1]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        s = System.Text.RegularExpressions.Regex.Replace(s, "</FONT>", "[/color]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        s = System.Text.RegularExpressions.Regex.Replace(s, "<BR\\s*/?>", "\n", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        s = System.Text.RegularExpressions.Regex.Replace(s, "</?B>", m => m.Value.StartsWith("</") ? "[/b]" : "[b]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        s = System.Text.RegularExpressions.Regex.Replace(s, "<[^>]+>", "");
        return s.Replace("&lt;", "<").Replace("&gt;", ">").Replace("&amp;", "&");
    }
}

/// <summary>
/// The component values of class-placed IconButtons (the lobby back and guide buttons): their label and the icon clip
/// IconButton.iconClassName loads, centred over the label.
/// </summary>
public static class IconButtons
{
    public static void Setup(UiMovie movie, MovieClipNode button, string iconClass, string label)
    {
        if (button == null) return;
        (button.Widget as ButtonWidget)?.Set("label", label);
        var icon = movie.BuildClass(iconClass, button.sx, button.sy);
        if (icon == null) return;
        icon.Name = "m_icon";
        var b = button.Bounds;
        float iw = icon.Bounds.Size.X;
        icon.Position = new Vector2(b.Position.X + (b.Size.X - iw) / 2 - icon.Bounds.Position.X, b.Position.Y + b.Size.Y * 0.18f - icon.Bounds.Position.Y);
        button.AddChild(icon);
    }
}
