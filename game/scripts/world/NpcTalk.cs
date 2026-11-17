using Godot;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2Godot.Ui;
using Ms2Godot.Ui.Contracts;

namespace Ms2Godot.World;

/// <summary>
/// Talking to an npc in the offline sandbox (the client's TalkAction + UITalkManager, with the sandbox answering the server's part).
/// - Who: MyPC::FindTalkableNpc -> WorldService::FindNearestNpc, friendly 2 first, then friendly 1: within TalkableDistance plus
///   the npc's capsule radius, in front of the player (TalkableFrontOnly), nearest first; the npc must have something to say (a
///   function kind or an npc script).
/// - What: the client sends NpcTalk (0x22, mode 1); the server answers with talk flags and the script, and for a plain talk it is the
///   npc's &lt;select&gt; (flag 8, GetNpcPickTalk). The sandbox shows the select's pages (or the first &lt;script&gt;, or a neutral line),
///   with the client's buttons: s_quest_talk_progress (Next | Close) while pages remain, s_quest_talk_end (Close) on the last.
/// - Sounds: npcSoundStart when the talk opens (CNpc::PlayStartSound), each page's voiceID (ShowPage), npcSoundEnd when it ends
///   (UITalkManager::ResetTalk -> CNpc::PlayEndSound), all at the npc.
/// The player stands still while the window is open. Keys: the action key (Space) is Next / Close, Esc ends the talk.
/// </summary>
public sealed partial class NpcTalk : Node
{
    readonly PackSet packs;
    readonly List<FieldNpcs.Placed> npcs;
    readonly Player player;
    readonly ClientConstants constants;
    static NpcScriptTable scripts;
    static Func<string, string> common;
    UiAssets ui;
    CanvasLayer layer;
    TalkCinematicLogic window;
    UiWidgets.MouseRouter mouse;
    FieldNpcs.Placed talking;
    List<TalkCinematicLogic.Page> pages;
    int page;
    bool wasScripted, closing;
    public string PlayerName = "Player";
    public bool Open => window != null;
    public int Talks;

    public NpcTalk(PackSet packs, List<FieldNpcs.Placed> npcs, Player player, ClientConstants constants)
    {
        this.packs = packs; this.npcs = npcs; this.player = player; this.constants = constants;
        Name = "NpcTalk";
    }
    public NpcTalk() { }

    static NpcScriptTable Scripts(PackSet packs)
    {
        if (scripts != null) return scripts;
        var text = new GameStrings(packs, "en");
        common = text.Common();
        return scripts = new NpcScriptTable(packs, text);
    }

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

    /// <summary>The talk the server would start for a plain talk: the select pages.</summary>
    List<TalkCinematicLogic.Page> PagesFor(NpcRow row, out List<string> voices)
    {
        voices = new List<string>();
        var st = Scripts(packs);
        var n = st.Find(row.Id);
        var s = n?.Select ?? n?.Scripts.FirstOrDefault(x => x.Kind == "script");
        var contents = s?.Contents ?? new List<NpcScriptTable.Content>();
        if (s != null && s.RandomPick && contents.Count > 1) contents = new List<NpcScriptTable.Content> { contents[Random.Shared.Next(contents.Count)] };
        var list = new List<TalkCinematicLogic.Page>();
        string illust = IllustPath(row.Illust);
        // CCinematicComponent::vfunc_5: the speaker is FUN_1401aa6a0 of the talk npc ("title name"); no npc script sets
        // speakerNpc, myTalk or otherNpcTalk, and an npc spawned here has no owner (CNpc::vfunc_128, m_nE48), whose own name
        // would be used instead
        string speaker = st.NpcDisplayName(row.Id);
        foreach (var c in contents)
        {
            list.Add(new TalkCinematicLogic.Page { Npc = speaker, Script = st.Resolve(c.Text, PlayerName), LeftIllust = c.Illust != null ? IllustPath(c.Illust) : illust });
            voices.Add(c.VoiceId);
        }
        if (list.Count == 0) { list.Add(new TalkCinematicLogic.Page { Npc = speaker, Script = "...", LeftIllust = illust }); voices.Add(null); }
        for (int i = 0; i < list.Count; i++)
            list[i].Button = common(i + 1 < list.Count ? "s_quest_talk_progress" : "s_quest_talk_end") ?? "";
        return list;
    }

    /// <summary>UITalkCinematic::GetIllustPath: quest/illust/&lt;name&gt;.dds when it exists, else "empty".</summary>
    string IllustPath(string name)
    {
        if (string.IsNullOrEmpty(name)) return "empty";
        var entry = $"quest/illust/{name.ToLowerInvariant()}.dds";
        return packs.Exists("Resource/Image", entry) ? entry : "empty";
    }

    Texture2D LoadImage(string entry)
    {
        try
        {
            var img = DdsLoader.Load(packs.Read("Resource/Image", entry));
            return img == null ? null : ImageTexture.CreateFromImage(img);
        }
        catch { return null; }
    }

    List<string> voices;

    public bool Start(FieldNpcs.Placed npc)
    {
        if (window != null || npc == null) return false;
        talking = npc;
        pages = PagesFor(npc.Row, out voices);
        page = 0; closing = false;
        ui ??= new UiAssets(packs);
        layer = new CanvasLayer { Layer = 20, Name = "TalkCinematic" };
        AddChild(layer);
        window = new TalkCinematicLogic(ui, 1, LoadImage);
        layer.AddChild(window.Root);
        var size = GetViewport().GetVisibleRect().Size;
        window.SetStageSize(size.X, size.Y);
        mouse = new UiWidgets.MouseRouter(window.Root);
        UiBridge.ToHost += OnHost;
        wasScripted = player.ScriptDriven;
        player.ScriptDriven = true;
        player.Input2 = Vector2.Zero;
        Sound(npc.Row.SoundStart, npc.Position);
        window.Init(pages[0]);
        Talks++;
        GD.Print($"talk npc {npc.Row.Id} {npc.Row.Name}: {pages.Count} page(s), illust {npc.Row.Illust} -> {pages[0].LeftIllust}");
        return true;
    }

    void OnHost(string movie, string name, object[] args)
    {
        if (movie != UITalkCinematicContract.Movie || window == null) return;
        switch (name)
        {
            case UITalkCinematicContract.ToHost.OnLoadComplete: ShowPage(0); break;
            case UITalkCinematicContract.ToHost.OnNext: if (page + 1 < pages.Count) ShowPage(page + 1); else End(); break;
            case UITalkCinematicContract.ToHost.OnEnd: End(); break;
            case UITalkCinematicContract.ToHost.OnCloseComplete: CallDeferred(nameof(Teardown)); break;
        }
    }

    void ShowPage(int i)
    {
        page = i;
        window.SetScript(pages[i]);
        Sound(voices[i], talking.Position);
    }

    /// <summary>The player ends the talk (NpcTalk mode 0): the window hides; ResetTalk plays the npc's end sound.</summary>
    public void End()
    {
        if (window == null || closing) return;
        closing = true;
        window.Hide();
        Sound(talking.Row.SoundEnd, talking.Position);
    }

    static void Sound(string path, Vector3 at)
    {
        if (!string.IsNullOrEmpty(path)) FieldAudio.Instance?.PlayEventAt(path, at);
    }

    void Teardown()
    {
        UiBridge.ToHost -= OnHost;
        layer?.QueueFree();
        layer = null; window = null; mouse = null; talking = null;
        player.ScriptDriven = wasScripted;
        player.Input2 = Vector2.Zero;
    }

    /// <summary>The action key: Next on the open window, or start a talk with the npc in front.</summary>
    public bool ActionKey()
    {
        if (window != null)
        {
            if (!window.Loaded || closing) return true;
            if (window.NextVisible) UiBridge.Raise(UITalkCinematicContract.Movie, UITalkCinematicContract.ToHost.OnNext);
            else UiBridge.Raise(UITalkCinematicContract.Movie, UITalkCinematicContract.ToHost.OnEnd);
            return true;
        }
        return Start(FindTalkable());
    }

    public override void _Input(InputEvent e)
    {
        if (window == null) return;
        mouse?.Handle(e);
        if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }) { End(); GetViewport().SetInputAsHandled(); }
    }

    public override void _Process(double delta)
    {
        window?.Process(delta);
        if (window != null && !closing) player.Input2 = Vector2.Zero;
    }
}
