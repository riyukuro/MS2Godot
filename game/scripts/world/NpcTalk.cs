using Godot;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2Godot.Ui;
using Ms2.Ui;
using Ms2.Ui.Hud;

namespace Ms2Godot.World;

/// <summary>
/// Talking to an npc in the offline sandbox (the client's TalkAction + UITalkManager, with the sandbox answering the server's part).
/// - Who: MyPC::FindTalkableNpc -> WorldService::FindNearestNpc, friendly 2 first, then friendly 1: within TalkableDistance plus
///   the npc's capsule radius, in front of the player (TalkableFrontOnly), nearest first; the npc must have something to say (a
///   function kind or an npc script).
/// - The window: uitalkcinematic run by its own ActionScript (<see cref="MovieDialog"/>) with UITalkCinematic's C++ side
///   (<see cref="TalkCinematicController"/>), the movie a plain talk opens (UITalkManager::OnTalkPacket mode 1 -> FUN_141b1ff80,
///   uitalkcinematic.cpp:1456: sub scene 0x6200031).
/// - What (the server's part): the client sends NpcTalk (mode 1); the server answers with the script and page and the button
///   set. The sandbox opens one of the npc's randomPick scripts (picked at random, as the server picks among them), else its
///   &lt;select&gt;, else its first &lt;script&gt;; a page with choices gets button set 9 (SelectableDistractor), a page with more after
///   it 4 (Next), the last 3 (Close). Mode 2 goes to the next page, or for a choice to one of the scripts its goto lists; past
///   the last page (or a choice without a script) the talk ends (mode 0). The server's script conditions are not here.
/// - Sounds: npcSoundStart when the talk opens (CNpc::PlayStartSound), each page's voiceID (ShowPage), npcSoundEnd when it ends
///   (UITalkManager::ResetTalk -> CNpc::PlayEndSound), all at the npc.
/// The player stands still while the window is open. Keys (UITalkCinematic::vfunc_13): the action key (Space) is Next, Esc ends
/// the talk, Up / Down move the choice focus.
/// </summary>
public sealed partial class NpcTalk : Node
{
    readonly PackSet packs;
    readonly List<FieldNpcs.Placed> npcs;
    readonly Player player;
    readonly ClientConstants constants;
    static NpcScriptTable scripts;
    static GameTables tables;
    UiAssets ui;
    CanvasLayer layer;
    MovieDialog window;
    TalkCinematicController side;
    FieldNpcs.Placed talking;
    NpcScriptTable.Script script;      // the server's script and page (m_nParam, m_nPage)
    int page;
    bool wasScripted;
    public string PlayerName = "Player";
    public bool Open => window != null;
    public int Talks;
    /// <summary>The page shown (0 based) and its script id, for the shots.</summary>
    public int Page => page;
    public int ScriptId => script?.Id ?? 0;

    public NpcTalk(PackSet packs, List<FieldNpcs.Placed> npcs, Player player, ClientConstants constants)
    {
        this.packs = packs; this.npcs = npcs; this.player = player; this.constants = constants;
        Name = "NpcTalk";
    }
    public NpcTalk() { }

    static NpcScriptTable Scripts(PackSet packs) => scripts ??= new NpcScriptTable(packs, new GameStrings(packs, "en"));

    bool HasTalk(NpcRow r) => r.Kind != 0 || Scripts(packs).Find(r.Id) != null;

    /// <summary>WorldService::FindNearestNpc for the talk action: friendly 2 first, then 1.</summary>
    public FieldNpcs.Placed FindTalkable()
    {
        if (player == null) return null;
        float range = constants.Float("TalkableDistance", 300);
        bool frontOnly = constants.Float("TalkableFrontOnly", 1) != 0;
        var me = player.GlobalPosition;
        var face = new Vector3(Mathf.Sin(player.Visual.Rotation.Y), 0, Mathf.Cos(player.Visual.Rotation.Y));
        foreach (int friendly in new[] { 2, 1 })
        {
            FieldNpcs.Placed best = null; float bestD = float.MaxValue;
            foreach (var n in npcs)
            {
                if (n.Row.Friendly != friendly || !HasTalk(n.Row) || !n.Root.Visible) continue;
                var to = n.Position - me;
                float d = to.Length();
                if (d > range + n.Row.CapsuleRadius) continue;
                if (frontOnly && new Vector3(to.X, 0, to.Z).Dot(face) < 0) continue;
                if (d < bestD) { bestD = d; best = n; }
            }
            if (best != null) return best;
        }
        return null;
    }

    /// <summary>The script the server opens a plain talk with (see the class summary).</summary>
    static NpcScriptTable.Script FirstScript(NpcScriptTable.Npc n)
    {
        if (n == null) return null;
        var picks = n.Scripts.Where(x => x.Kind == "script" && x.RandomPick).ToList();
        if (picks.Count > 0) return picks[Random.Shared.Next(picks.Count)];
        return n.Select ?? n.Scripts.FirstOrDefault(x => x.Kind == "script");
    }

    /// <summary>The page as the client builds it (CCinematicContent): the text with its words, the illust, the voice, the choices.</summary>
    TalkCinematicController.TalkContent ContentOf(NpcScriptTable.Content c)
    {
        var st = Scripts(packs);
        var t = new TalkCinematicController.TalkContent
        {
            Script = st.Resolve(c.Text, PlayerName, player?.Gender),
            Illust = c.Illust ?? "",
            Voice = c.VoiceId ?? "",
        };
        foreach (var (text, go) in c.Distractors) t.Distractors.Add((st.Resolve(text, PlayerName, player?.Gender), go.Length > 0));
        return t;
    }

    /// <summary>The server's button set for a page: 9 with choices, 4 with pages after it, 3 on the last.</summary>
    int ButtonsOf(int i)
    {
        var c = script.Contents[i];
        if (c.Distractors.Count > 0) return 9;
        return i + 1 < script.Contents.Count ? 4 : 3;
    }

    /// <summary>A neutral page for an npc whose only talk is its function (a shop, a storage ...), which the sandbox does not open.</summary>
    static NpcScriptTable.Script Neutral() => new() { Kind = "script", Contents = { new NpcScriptTable.Content { Text = "..." } } };

    public bool Start(FieldNpcs.Placed npc)
    {
        if (window != null || npc == null) return false;
        var n = Scripts(packs).Find(npc.Row.Id);
        script = FirstScript(n);
        if (script == null || script.Contents.Count == 0) script = Neutral();
        page = 0;
        talking = npc;
        ui ??= new UiAssets(packs);
        tables ??= new GameTables(packs, "en");
        if (!UiCode.Load(Settings.DataDir)) GD.PushWarning("UI code is not built for this data: the talk window runs interpreted");
        var size = (Vector2)GetWindow().Size;
        side = new TalkCinematicController
        {
            Talking = true,
            TalkNpcId = npc.Row.Id,
            Content = ContentOf(script.Contents[0]),
            ButtonSet = ButtonsOf(0),
            NpcName = id => Scripts(packs).NpcDisplayName(id),
            NpcIllust = id => id == npc.Row.Id ? npc.Row.Illust ?? "" : "",
            FileExists = ImageExists,
            ClientWidth = (int)size.X, ClientHeight = (int)size.Y,
            MovieScale = UiScale.ViewportFactor(size.X),
        };
        side.SendNext += OnNext;
        side.SendEnd += () => { };                      // NpcTalk mode 0: nothing to tell offline
        side.TalkReset += () => Sound(talking?.Row.SoundEnd, talking?.Position ?? Vector3.Zero);
        side.PlayVoice += v => Sound(v, talking?.Position ?? Vector3.Zero);
        side.CloseComplete += () => CallDeferred(nameof(Teardown));
        layer = new CanvasLayer { Layer = 20, Name = "TalkCinematic" };
        AddChild(layer);
        wasScripted = player.ScriptDriven;
        player.ScriptDriven = true;
        player.Input2 = Vector2.Zero;
        Sound(npc.Row.SoundStart, npc.Position);          // CNpc::PlayStartSound (FUN_141b1ff80)
        window = new MovieDialog(ui, side, new UiModel(tables), size);
        layer.AddChild(window);
        Talks++;
        GD.Print($"talk npc {npc.Row.Id} {npc.Row.Name}: script {script.Id} ({script.Contents.Count} page(s)), illust {npc.Row.Illust}");
        return true;
    }

    /// <summary>FUN_141b70ea0 for an illust path (./Data/Resource/Image/...): the image is in the data.</summary>
    bool ImageExists(string path)
    {
        int at = path.IndexOf("resource/image/", StringComparison.OrdinalIgnoreCase);
        return at >= 0 && packs.Exists("Resource/Image", path[(at + "resource/image/".Length)..].ToLowerInvariant());
    }

    /// <summary>The server's answer to NpcTalk mode 2: the next page, a choice's script, or the end (mode 0).</summary>
    void OnNext(int pick)
    {
        if (side == null || script == null) return;
        var c = script.Contents[page];
        if (c.Distractors.Count > 0)
        {
            var go = pick >= 0 && pick < c.Distractors.Count ? c.Distractors[pick].Goto : Array.Empty<int>();
            var n = Scripts(packs).Find(talking.Row.Id);
            var targets = go.Select(id => n?.ById(id)).Where(x => x != null && x.Contents.Count > 0).ToList();
            if (targets.Count == 0) { side.OnServerEnd(); return; }
            script = targets[Random.Shared.Next(targets.Count)];
            page = 0;
        }
        else if (page + 1 < script.Contents.Count) page++;
        else { side.OnServerEnd(); return; }
        side.OnServerPage(ContentOf(script.Contents[page]), ButtonsOf(page));
    }

    /// <summary>The player ends the talk (Esc: NpcTalk mode 0 and ResetTalk).</summary>
    public void End()
    {
        if (side == null) return;
        if (side.Talking) side.Key(TalkCinematicController.TalkKey.End);
        if (side.Talking) side.EndTalk();       // before the movie is loaded the key does nothing
    }

    static void Sound(string path, Vector3 at)
    {
        if (!string.IsNullOrEmpty(path)) FieldAudio.Instance?.PlayEventAt(path, at);
    }

    void Teardown()
    {
        if (window == null) return;
        window.Host.Unload();
        layer?.QueueFree();
        layer = null; window = null; side = null; talking = null; script = null;
        player.ScriptDriven = wasScripted;
        player.Input2 = Vector2.Zero;
    }

    /// <summary>The action key: Next on the open window, or start a talk with the npc in front.</summary>
    public bool ActionKey()
    {
        if (window != null)
        {
            side.Key(TalkCinematicController.TalkKey.Next);
            return true;
        }
        return Start(FindTalkable());
    }

    public override void _Input(InputEvent e)
    {
        if (window == null || side == null) return;
        if (e is not InputEventKey { Pressed: true } k) return;
        TalkCinematicController.TalkKey? key = k.Keycode switch
        {
            Key.Escape when !k.Echo => TalkCinematicController.TalkKey.End,
            Key.Up => TalkCinematicController.TalkKey.Up,
            Key.Down => TalkCinematicController.TalkKey.Down,
            _ => null,
        };
        if (key == null) return;
        side.Key(key.Value);
        GetViewport().SetInputAsHandled();
    }

    public override void _Process(double delta)
    {
        if (window != null) player.Input2 = Vector2.Zero;
    }
}
