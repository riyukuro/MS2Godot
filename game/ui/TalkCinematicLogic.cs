using Godot;
using Ms2Godot.Ui.Contracts;

namespace Ms2Godot.Ui;

/// <summary>
/// The npc talk window (uitalkcinematic, UITalkCinematic in the client), ported by hand from the movie's AS3
/// (talk_entity_body : TalkCinematic, talk_entity_script : TalkScript): the letterbox cuts, the npc illustration and the script
/// panel with the npc name, the text and the end / next buttons. The host calls <see cref="Init"/> (the "show" timeline: the
/// illustration comes in at frame 22, OnLoadComplete is raised at frame 30), then <see cref="SetScript"/> per page and
/// <see cref="Hide"/> (the "hide" timeline, onCloseComplete at its end). Callbacks go out through <see cref="UiBridge"/>.
/// </summary>
public sealed class TalkCinematicLogic
{
    /// <summary>One page (the object UITalkCinematic::ShowPage passes to setScript).</summary>
    public sealed class Page
    {
        public string Npc, Script, Button;
        public string LeftIllust;           // image path in Resource/Image, or "" / "empty" for none
        public bool LeftSpeak = true;
    }

    public readonly MovieClipNode Root;
    readonly MovieClipNode body, container, script, buttons, lNpc, rNpc;
    readonly ButtonWidget endBtn, nextBtn;
    readonly UiAssets assets;
    readonly Func<string, Texture2D> loadImage;
    Page initData;
    int frame = -1, stopAt;
    double t;
    public bool Loaded { get; private set; }
    public bool Closed { get; private set; }

    public TalkCinematicLogic(UiAssets assets, double scale, Func<string, Texture2D> loadImage)
    {
        this.assets = assets; this.loadImage = loadImage;
        var movie = new UiMovie(assets, UITalkCinematicContract.Movie);
        Root = movie.BuildRoot(scale);
        body = Root.Children.Select(c => c.Node).OfType<MovieClipNode>().First();
        container = body.FindClip(UITalkCinematicContract.Instances.M_container);
        script = body.FindClip(UITalkCinematicContract.Instances.M_container_m_script);
        buttons = body.FindClip(UITalkCinematicContract.Instances.M_container_m_script_m_buttons);
        lNpc = body.FindClip(UITalkCinematicContract.Instances.M_container_m_lNpc);
        rNpc = body.FindClip(UITalkCinematicContract.Instances.M_container_m_rNpc);
        endBtn = (ButtonWidget)body.FindClip(UITalkCinematicContract.Instances.M_container_m_script_m_buttons_m_end).Widget;
        nextBtn = (ButtonWidget)body.FindClip(UITalkCinematicContract.Instances.M_container_m_script_m_buttons_m_next).Widget;
        UiWidgets.ApplyInspector(Root, UITalkCinematicContract.Inspector);
        // TalkScript.configUI: the buttons raise OnEnd / OnNext
        endBtn.Click += _ => UiBridge.Raise(UITalkCinematicContract.Movie, UITalkCinematicContract.ToHost.OnEnd);
        nextBtn.Click += _ => UiBridge.Raise(UITalkCinematicContract.Movie, UITalkCinematicContract.ToHost.OnNext);
        // TalkCinematic.configUI: both illustrations hidden, the image plate off
        Show(container, "m_lNpc", false);
        Show(container, "m_rNpc", false);
        Show(container, "m_imagePlate", false);
        Clear();
        body.GotoFrame(0);
        container.GotoFrame(0);
    }

    static void Show(MovieClipNode parent, string name, bool on) => parent.SetNamedVisible(name, on);

    /// <summary>TalkCinematic.setStageSize: fit the 1920x1200 stage to the window (by width when the window is wider than 16:10).</summary>
    public void SetStageSize(float w, float h)
    {
        float l4 = h * 1.6f, l5 = w * 0.625f;
        float s;
        if (l4 < w && h / w > 0.5f) { s = (w + 1) / 1920f; Root.Position = new Vector2(0, h - l5 + 1); }
        else { s = (h + 1) / 1200f; Root.Position = new Vector2((w - l4) * 0.5f, 0); }
        Root.Scale = new Vector2(s, s);
    }

    /// <summary>init(page): play "show"; the illustration of this first page comes in at frame 22.</summary>
    public void Init(Page first)
    {
        initData = first;
        Loaded = false; Closed = false;
        Play(0, 39);
    }

    /// <summary>setScript(page) (no movie, no image plate): illustration and TalkScript.setData.</summary>
    public void SetScript(Page p)
    {
        SetIllust(p);
        Clear();
        SetTyping(p.Script ?? "");
        SetButton(p.Button);
        if (!string.IsNullOrEmpty(p.Npc)) SetNpc(p.Npc);
        // TypingText.start() completes at once, and with no reward TalkScript.onScriptComplete -> onButtonPlay
        UiBridge.Raise(UITalkCinematicContract.Movie, UITalkCinematicContract.ToHost.OnTalkComplete);
    }

    /// <summary>The "hide" timeline (body frames 41-56): the panel and the illustrations go out, then onCloseComplete.</summary>
    public void Hide()
    {
        Play(40, 55);
    }

    void Play(int from, int to) { frame = from; stopAt = to; t = 0; Step(from); }

    public void Process(double delta)
    {
        if (frame < 0) return;
        t += delta;
        int f = Math.Min(stopAt, (frame < 40 ? 0 : 40) + (int)(t * Root.Movie.Fps));
        while (frame < f) Step(++frame);
        if (frame >= stopAt) frame = -1;
    }

    void Step(int f)
    {
        body.GotoFrame(f);
        container.GotoFrame(Math.Min(f, f >= 40 ? 49 : 39));
        if (f == 21) SetIllust(initData);                                   // onShowNpc
        if (f == 29)                                                        // onShowScript
        {
            Loaded = true;
            UiBridge.Raise(UITalkCinematicContract.Movie, UITalkCinematicContract.ToHost.OnLoadComplete);
        }
        if (f == 40)                                                        // onHide
        {
            IllustHide(lNpc);
            IllustHide(rNpc);
        }
        if (f == 55)                                                        // onHideScript
        {
            Closed = true;
            UiBridge.Raise(UITalkCinematicContract.Movie, UITalkCinematicContract.ToHost.OnCloseComplete);
        }
    }

    // ---- TalkCinematic.setIllust / setNpcMovie ----

    string lIllust = "";

    void SetIllust(Page p)
    {
        if (p == null) return;
        SetNpcMovie(lNpc, "m_lNpc", p.LeftIllust ?? "", p.LeftSpeak, ref lIllust);
    }

    void SetNpcMovie(MovieClipNode clip, string name, string illust, bool speak, ref string current)
    {
        Show(container, name, true);
        if (illust == "" || illust == "empty") { IllustHide(clip); current = illust; return; }
        string label = speak
            ? clip.Labels.FirstOrDefault(l => l.Value == clip.Frame).Key switch { "listen" => "speak", null or "hide_from_speak" or "hide_from_listen" => "show", var x => x }
            : "listen";
        if (current != illust)
        {
            var inst = clip.FindClip("instance");
            foreach (var c in inst.GetChildren()) if (c is Sprite2D) c.QueueFree();
            var tex = loadImage(illust);
            if (tex != null)
            {
                // handleIllustLoadComplete: at most 700 wide, and a taller picture is lifted so its bottom 700 show
                var sz = tex.GetSize();
                float sx = sz.X > 700 ? 700 / sz.X : 1;
                var sp = new Sprite2D { Texture = tex, Centered = false, Scale = new Vector2(sx, 1), Position = new Vector2(0, sz.Y > 700 ? -(sz.Y - 700) : 0) };
                inst.AddChild(sp);
            }
        }
        if (FrameLabel(clip) != label || current != illust) clip.GotoLabel(label, true);
        current = illust;
    }

    static string FrameLabel(MovieClipNode clip)
    {
        string best = null; int bf = -1;
        foreach (var (k, v) in clip.Labels) if (v <= clip.Frame && v > bf) { best = k; bf = v; }
        return best;
    }

    void IllustHide(MovieClipNode clip)
    {
        var l = FrameLabel(clip);
        if (l == "show" || l == "speak") clip.GotoLabel("hide_from_speak", true);
        else if (l == "listen") clip.GotoLabel("hide_from_listen", true);
    }

    // ---- TalkScript ----

    void Clear()
    {
        SetTyping("");
        Show(script, "m_buttons", false);
    }

    void SetTyping(string text)
    {
        var typing = script.FindClip("m_container.typingText");
        typing?.SetText(text);
    }

    void SetNpc(string name)
    {
        var npc = script.FindClip("m_npc");
        npc?.SetText(name);
    }

    /// <summary>TalkScript.setButton: "label|label"; the one with "esc" in it is the end button; one end button sits further right.</summary>
    void SetButton(string spec)
    {
        Show(buttons, "m_end", false);
        Show(buttons, "m_next", false);
        if (string.IsNullOrEmpty(spec)) return;
        Show(script, "m_buttons", true);
        var parts = spec.Split('|');
        bool endShown = false;
        if (parts[0].ToLowerInvariant().Contains("esc")) { Show(buttons, "m_end", true); endShown = true; Label(endBtn, parts[0]); }
        else { Show(buttons, "m_next", true); Label(nextBtn, parts[0]); }
        if (parts.Length > 1)
        {
            if (endShown) { Show(buttons, "m_next", true); Label(nextBtn, parts[1]); }
            else { Show(buttons, "m_end", true); endShown = true; Label(endBtn, parts[1]); }
        }
        var ch = script.Children.FirstOrDefault(c => c.Name == "m_buttons");
        if (ch != null)
        {
            var m = ch.Base;
            m.Origin = new Vector2(parts.Length == 1 && endShown ? 1124 + 170 : 1124, m.Origin.Y);
            ch.Override = m;
            script.GotoFrame(script.Frame);
        }
    }

    static void Label(ButtonWidget b, string text)
    {
        b.Label = text;
        b.Node.SetText(text);
        foreach (var c in b.Node.AllClips()) c.SetText(text);
    }

    public ButtonWidget EndButton => endBtn;
    public ButtonWidget NextButton => nextBtn;
    public bool EndVisible => buttons.Children.Any(c => c.Name == "m_end" && c.Last >= 0);
    public bool NextVisible => buttons.Children.Any(c => c.Name == "m_next" && c.Last >= 0);
}
