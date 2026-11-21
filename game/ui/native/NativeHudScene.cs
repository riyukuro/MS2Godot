using Godot;

namespace Ms2Godot.Ui.Native;

/// <summary>
/// The field with a native HUD (no Flash at run time): the skill bar's quick slots and the HP/SP orb (uimainplayerinfo),
/// the EXP bar (uiexpbar) and the game menu (uigamemenudialog). native_hud.tscn lays them out in stage units, anchored as
/// the movies' alignments place them; the art is baked from the player's Gfx at start-up. Data comes from
/// <see cref="StubHudData"/> as in the Flash HUD.
/// Args after `--`: --native-hudshot DIR saves hud.png (and hud_menu.png with a group list open) and quits.
/// </summary>
public partial class NativeHudScene : Node
{
    readonly StubHudData data = StubHudData.Instance;
    string shotDir; int frames;
    NGameMenu menu;

    public override void _EnterTree()
    {
        if (!NativeUi.Init()) { GetTree().Quit(2); return; }
        NativeUi.UseStageScale(GetWindow());
    }

    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--native-hudshot") shotDir = args[i + 1];
        var root = GetNode<Control>("Ui/Root");
        root.GetNode<NExpBar>("ExpBar").SetExp(data.ExpPercent, $"{data.ExpPercent:0.00}%");
        menu = root.GetNode<NGameMenu>("GameMenu");
        menu.Locale = data.Locale;
        menu.FeatureOn = f => !data.FeaturesOff.Contains(f);
        menu.Build();
        var orb = root.GetNode<NSkin>("PlayerInfo/m_playerInfo");
        foreach (var (name, label) in orb.Labels)
        {
            if (name.EndsWith("m_curHp")) label.Text = data.Player.HpCurrent.ToString();
            else if (name.EndsWith("m_maxHp")) label.Text = data.Player.HpMax.ToString();
            else if (name.EndsWith("m_curSp")) label.Text = data.SpCurrent.ToString();
            else if (name.EndsWith("m_maxSp")) label.Text = data.SpMax.ToString();
            else if (name.EndsWith("hpTxt")) label.Text = "Health";
            else if (name.EndsWith("spTxt")) label.Text = "Spirit";
            else if (name == "m_tag") label.Text = $"LV {data.Player.Level} {StubCharacterList.JobNames.GetValueOrDefault(data.Player.Job, "Beginner")} {data.Player.Name}";
        }
        ShowSkillList(root.GetNode<Control>("PlayerInfo/m_skillCommon"));
    }

    /// <summary>
    /// playerinfo_list_attack's "show" label (frames 1-15): each slot slides up and fades in. The slots are the list's
    /// __id4_ .. __id19_ in depth order (Q W E R A S D F 1 2 3 4 5 6 7 8); the tracks are replayed at the movie's fps.
    /// </summary>
    void ShowSkillList(Control list)
    {
        const string movie = "uimainplayerinfo", sym = "playerinfo_list_attack";
        var slots = list.GetChildren().OfType<NSkillSlot>().ToList();
        double fps = NativeUi.Art.Fps(movie, sym);
        for (int i = 0; i < slots.Count; i++)
        {
            var track = NativeUi.Art.Track(movie, sym, $"__id{4 + i}_", 1, 15);
            if (track.Count == 0) continue;
            var slot = slots[i];
            var tw = slot.CreateTween().SetIgnoreTimeScale();
            foreach (var (xf, a) in track)
            {
                var k = (xf.Origin, a);
                tw.TweenCallback(Callable.From(() => { slot.Position = k.Origin; slot.Modulate = new Color(1, 1, 1, k.a); }));
                tw.TweenInterval(1 / fps);
            }
        }
    }

    public override void _Process(double delta)
    {
        if (shotDir == null) return;
        frames++;
        if (frames == 300) Save("hud.png");
        if (frames == 301)
            foreach (var b in menu.FindChildren("*", "", true, false).OfType<NMenuButton>())
                if (b.LayerName == "charactergroup") { b.SetState("over"); b.ShowList(); }
        if (frames == 330) { Save("hud_menu.png"); GD.Print("menu entries: " + string.Join(", ", menu.GetNode("m_menus").GetChildren().Select(c => c.Name.ToString()))); GetTree().Quit(); }
    }

    void Save(string name)
    {
        DirAccess.MakeDirRecursiveAbsolute(shotDir);
        GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(shotDir, name));
        GD.Print("saved " + name);
    }
}
