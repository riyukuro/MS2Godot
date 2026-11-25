using Ms2.Net.Packets;

namespace Ms2.Ui.Hud;

/// <summary>
/// The movie's calls the cut scene cinematic pane makes (uicutscenecinematic.cpp: InvokeSetCinematic, InvokeSetOpening,
/// InvokeSetIntro, the Invoke("setScript"/"hideScript"/"setSkip"/"setCaption") calls and Close's "hideScript", "hideDirect" and
/// gotoAndPlay("_out")). <see cref="CutSceneCinematicController"/> is the one that runs them on the movie.
/// </summary>
public interface ICinematicPane
{
    /// <summary>The movie's entity is there (m_30).</summary>
    bool Loaded { get; }
    /// <summary>InvokeSetCinematic (:143): setCinematic(id, text).</summary>
    void SetCinematic(int id, string text);
    /// <summary>InvokeSetOpening: setOpening(text, bool).</summary>
    void SetOpening(string text, bool isOpening);
    /// <summary>InvokeSetIntro: setIntro(text).</summary>
    void SetIntro(string text);
    /// <summary>SetScript (FUN_141908750's call): setScript(name, text, illust, align).</summary>
    void SetScript(string name, string text, string illust, int align);
    void HideScript();
    /// <summary>FUN_1419080b0: hideScript, hideDirect, then "_out" on the background and the document.</summary>
    void Close();
    /// <summary>FUN_141907d60's invoke: setSkip(state != 0, key, label) with m_skip visible when the state is not 0.</summary>
    void SetSkip(bool on, string key, string label, bool skipVisible);
    /// <summary>m_spSkip->SetVisible (SkipCinematic hides it once skipped).</summary>
    void SetSkipVisible(bool visible);
    /// <summary>OnPacketSetCaption (:727): setCaption(obj) with the translated texts.</summary>
    void SetCaption(CinematicPacket.Caption caption, string text1, string text2);
}

/// <summary>
/// The cut scene cinematic pane's state (UICutSceneCinematic, ui id 0x4e52, uicutscenecinematic.cpp) as the Cinematic
/// packet drives it. KeyMode (OnPacketKeyMode @0x141909700) sets the cinematic key group: on is sub scene 0x6200041 (and ends an NPC
/// talk first, SendNpcTalkMode0), off is the field's 0x620001d (SetCinematicKeyMode @0x141908040). Start (StartCinematic
/// @0x141907f80) does nothing for id 0, else sets mode 1, the id and the translated text. Close (FUN_1419080b0) hides the script and
/// the direct pane.
/// Types 4 to 12 keep the pane's own lists as the client does: SetSkip (FUN_14190a4c0) sets the skip state to its kind and the
/// message, ResetSkip (@0x14190afb0) empties the talks and the message and sets the state 0, Talk (FUN_141909880) queues a line
/// unless its translated text is empty, ClearTalks empties the queue, BalloonTalk (FUN_141909eb0) queues a line in its object's
/// group (made on first use) unless its text is empty, ClearBalloonGroup (@0x14190a400) empties a group's lines, Opening and
/// Intro (@0x14190ac70, @0x14190aee0) set mode 2 or 3 and the text and turn the key group on when it is off.
/// The pane runs by <see cref="Update"/> (UICutSceneCinematic::vfunc_15): <c>FUN_141908750</c> plays the talk queue (a line stays
/// up for its duration, the longer of the packet's and the voice's), <c>UpdateBalloons</c> starts each balloon line when its delay
/// has passed and hides it at its end. The skip prompt (RequestSkip :450, SkipCinematic) sends the Trigger of
/// <see cref="CinematicSkipTrigger"/> when the player confirms.
/// STILL-OPEN: what the key group does to the player's input (the sub scene's own key table), the NPC talk end that a key mode
/// on sends first, the trigger voice lookup (FindTriggerVoice / GetSoundLength: <see cref="SoundLength"/> returns 0 until it is
/// ported, so a talk is as long as its packet says and keeps no sound), the balloon text-key lookup of a raw BalloonTalk
/// (GetTextFilter), the player action state reset of SkipCinematic (FUN_1409536d0) and the NotifyCutSceneCinematicSkip notice.
/// </summary>
public sealed class CutSceneCinematic
{
    public bool KeyMode { get; private set; }
    public int Id { get; private set; }
    public string Text { get; private set; }
    /// <summary>Between a start and a close: the script and direct panes show (the client keeps the id and text after the close).</summary>
    public bool IsShowing { get; private set; }
    /// <summary>The client's m_nMode: 0 none, 1 cinematic, 2 opening, 3 intro.</summary>
    public int Mode { get; private set; }
    public bool IsOpening { get; private set; }
    /// <summary>The client's m_nSkipState: 0 no skip prompt, else the kind of the last SetSkip.</summary>
    public int SkipState { get; private set; }
    public string SkipMessage { get; private set; } = "";
    /// <summary>The talk lines still to come (the client's m_talks); the line on show is <see cref="CurrentTalk"/>.</summary>
    public List<CinematicPacket.Talk> Talks { get; } = new();
    /// <summary>The balloon lines still to come of each object (the client's m_groups).</summary>
    public Dictionary<int, List<CinematicPacket.BalloonTalk>> BalloonGroups { get; } = new();

    // ---- the pane and what it reads from the game -----------------------------------------------------------------------

    /// <summary>The movie; null before it loads (the state still follows the packets, the invokes are skipped).</summary>
    public ICinematicPane Pane;
    /// <summary>TranslateScriptText (uicutscenecinematic.cpp): "$key$" words of the trigger strings and the reserved words.</summary>
    public Func<string, string> Translate = s => s;
    /// <summary>The pane's clock in ms (GetTickCount64 of its Update).</summary>
    public Func<long> Clock = () => Environment.TickCount64;
    /// <summary>FindTriggerVoice + GetSoundLength (FUN_141909880): the length in ms of the voice named by the sound key for
    /// the local player's gender and job group, 0 when there is none.</summary>
    public Func<string, int> SoundLength = _ => 0;
    /// <summary>The trigger voice of a talk or balloon line.</summary>
    public Action<string> PlayVoice = _ => { };
    public Action StopVoice = () => { };
    /// <summary>The npc's name (FUN_141908750: npc id to its name).</summary>
    public Func<int, string> NpcName = _ => "";
    /// <summary>The talk's illustration path: the packet's name as a path (the client's wide form of it).</summary>
    public Func<string, string> IllustPath = s => s;
    /// <summary>The local player's name, or null when there is no local player (a talk of npc 0 is then not shown).</summary>
    public Func<string> PlayerName = () => null;
    /// <summary>The trigger the skip sends (UICutSceneCinematic::SendTrigger @0x141909230); called only for skip state 1 or 2.</summary>
    public Action SendTrigger = () => { };
    /// <summary>The skip confirm box (RequestSkip :476, text key s_cutscene_skip_msg_{message}); the callback gets the button, 0 for yes.</summary>
    public Action<string, Action<int>> ShowSkipBox = (_, _) => { };
    /// <summary>The balloon of an object (FUN_141bd1200, the PC or NPC with that object id): true when the object is there and its
    /// scene root is not culled.</summary>
    public Func<int, bool> BalloonActorReady = _ => false;
    /// <summary>The actor's balloon shows the text (SetText, Keep, the HUD entity, vfunc_2).</summary>
    public Action<int, string> ShowBalloon = (_, _) => { };
    /// <summary>The actor's balloon stops (vfunc_3).</summary>
    public Action<int> HideBalloon = _ => { };
    /// <summary>SetCinematicKeyMode's scene change (0x6200041 on, 0x620001d off).</summary>
    public event Action<bool> KeyModeChanged;
    /// <summary>NotifyCutSceneCinematicSkip, posted by SkipCinematic.</summary>
    public event Action Skipped;

    // ---- the talk queue and the balloons' groups --------------------------------------------------------------------------

    /// <summary>The line on show (FUN_141908750's current talk), or null.</summary>
    public CinematicPacket.Talk CurrentTalk { get; private set; }
    long talkEnd;
    string currentSound = "";
    bool skipped;
    string skipKey = "", skipLabel = "";

    sealed class Line
    {
        public CinematicPacket.BalloonTalk Packet;
        public long Start, End;
        public string Sound = "";
    }

    sealed class Group
    {
        public readonly List<Line> Lines = new();
        public bool Shown;
        public long Hide;
    }

    readonly Dictionary<int, Group> groups = new();

    /// <summary>The sound key of a text: the "$...$" word without its marks (FUN_14016a600, StripDollar).</summary>
    static string Stripped(string text) => text.Length >= 2 && text[0] == '$' && text[^1] == '$' ? text.Replace("$", "") : text;

    /// <summary>How many packets were applied: the HUD makes the pane (the client's entity) on the first one.</summary>
    public int Packets { get; private set; }

    public void Apply(CinematicPacket p)
    {
        Packets++;
        switch (p)
        {
            case CinematicPacket.KeyMode k: SetCinematicKeyMode(k.On); break;
            case CinematicPacket.Start s:
                StartCinematic(s.Id, s.Text);
                break;
            case CinematicPacket.Close:
                IsShowing = false;
                Pane?.Close();
                break;
            case CinematicPacket.SetSkip k:
                SkipMessage = k.Message;
                SetSkipState(k.Kind, "$s_cutscene_skip_key", k.Kind switch { 2 => "$s_cutscene_skip_state", 1 => "$s_cutscene_skip_scene", _ => "" });
                break;
            case CinematicPacket.ResetSkip:
                Talks.Clear(); SkipMessage = "";
                SetSkipState(0, "", "");
                break;
            case CinematicPacket.Talk t:
                if (Translate(t.Script).Length != 0) Talks.Add(t);
                break;
            case CinematicPacket.ClearTalks: ClearTalks(); break;
            case CinematicPacket.BalloonTalk b: AddBalloon(b); break;
            case CinematicPacket.ClearBalloonGroup g: ClearBalloonGroup(g.ObjectId); break;
            case CinematicPacket.Caption c:
                if (Pane is not { Loaded: true }) break;        // OnPacketSetCaption returns without the movie's entity (:~735)
                var caption = Stripped(c.Text2);
                Pane.SetCaption(c, Translate(c.Text1), Translate(c.Text2));
                if (caption.Length != 0) PlayVoice(caption);
                break;
            case CinematicPacket.Opening o:
                Mode = 2; Text = Translate(o.Text); IsOpening = o.IsOpening;
                Pane?.SetOpening(Text, o.IsOpening);
                if (!KeyMode) SetCinematicKeyMode(true);
                if (Stripped(o.Text) is { Length: > 0 } sound) PlayVoice(sound);
                break;
            case CinematicPacket.Intro i:
                Mode = 3; Text = Translate(i.Text);
                Pane?.SetIntro(Text);
                if (!KeyMode) SetCinematicKeyMode(true);
                break;
        }
    }

    /// <summary>StartCinematic (@0x141907f80, uicutscenecinematic.cpp:259-269): nothing for id 0, else mode 1, the id and the
    /// translated text, and setCinematic on the movie. OnPacketStartCinematic and the telescope (interactobject.cpp:983) call it.</summary>
    public void StartCinematic(int id, string text)
    {
        if (id == 0) return;
        Mode = 1; Id = id; Text = Translate(text ?? ""); IsShowing = true;
        Pane?.SetCinematic(Id, Text);
    }

    /// <summary>FUN_141907d60 (@0x141907d60, uicutscenecinematic.cpp:241-255), the skip prompt's state, key and label, called
    /// from outside the packets: the telescope's state 3 with its key cap and description (interactobject.cpp:984). A state
    /// other than 1 or 2 sends no trigger when skipped (<see cref="SkipCinematic"/>).</summary>
    public void SetSkip(int state, string key, string label) => SetSkipState(state, key ?? "", label ?? "");

    /// <summary>GFxSceneService::SetSubScene called outside the pane (the telescope, interactobject.cpp:975 and :1015): the
    /// cinematic sub scene 0x6200041 is the one the pane shows in and takes the keys in (the key group on); any other id
    /// leaves it. The pane's own SetCinematicKeyMode also ends an NPC talk first; this call does not.</summary>
    public void SetSubScene(int id)
    {
        bool on = id == CinematicSubScene;
        if (on == KeyMode) return;
        SetCinematicKeyMode(on);
    }

    /// <summary>The cinematic sub scene (UISceneGameCinematicCutScene).</summary>
    public const int CinematicSubScene = 0x6200041;

    /// <summary>The pane is wanted: a Cinematic packet came, or the cinematic sub scene was entered without one.</summary>
    public bool Wanted => Packets > 0 || KeyMode;

    /// <summary>SetCinematicKeyMode (@0x141908040): the key group on or off.</summary>
    void SetCinematicKeyMode(bool on)
    {
        KeyMode = on;
        KeyModeChanged?.Invoke(on);
    }

    /// <summary>The field's unload in the cinematic sub scene: WorldService::FUN_141bbc580 calls the scene's vfunc_54
    /// (worldservice_1.cpp:10119), which UISceneGameCinematicCutScene answers with SetSubScene(0x620001d) (uiscenegame.cpp:804):
    /// the key group goes off, and the pane with it. The mode, id and text stay, as the client keeps them.</summary>
    public void FieldUnloaded()
    {
        if (KeyMode) SetCinematicKeyMode(false);
    }

    // ---- skip ---------------------------------------------------------------------------------------------------------

    /// <summary>FUN_141907d60 (:~160): the state, key and label; not skipped yet; the prompt shows for a state other than 0.</summary>
    void SetSkipState(int state, string key, string label)
    {
        SkipState = state; skipKey = key; skipLabel = label; skipped = false;
        Pane?.SetSkip(state != 0, key, label, state != 0);
    }

    /// <summary>NotifyFadeInComplete: ResetSkipState, the state, key, label and message all zero.</summary>
    public void ResetSkipState()
    {
        SkipState = 0; skipKey = ""; skipLabel = ""; SkipMessage = "";
    }

    /// <summary>OnLoadComplete's replay (@0x141907720, :175-197): the pane's invokes for the mode in force, then the skip prompt
    /// again with the key "$s_cutscene_skip_key". The client's pane loads as the cinematic sub scene first shows it
    /// (CUIService FUN_141add040(0x4e52), uiservice.cpp:846), before any caller's FUN_141907d60; here the movie loads later,
    /// so a key set from outside the packets before the load (the telescope's key cap) is the one shown, as in the client.</summary>
    public void Replay()
    {
        if (Pane == null) return;
        switch (Mode)
        {
            case 1: if (Text != null) Pane.SetCinematic(Id, Text); break;
            case 2: Pane.SetOpening(Text ?? "", IsOpening); break;
            case 3: Pane.SetIntro(Text ?? ""); break;
        }
        Pane.SetSkip(SkipState != 0, skipKey.Length != 0 ? skipKey : "$s_cutscene_skip_key", skipLabel, SkipState != 0);
    }

    /// <summary>RequestSkip (:450): nothing without a prompt or once skipped; a message asks first (OnSkipMsgBox skips on yes).</summary>
    public void RequestSkip()
    {
        if (SkipState == 0 || skipped) return;
        if (SkipMessage.Length != 0)
        {
            ShowSkipBox($"s_cutscene_skip_msg_{SkipMessage}", result => { if (result == 0) SkipCinematic(); });
            return;
        }
        SkipCinematic();
    }

    /// <summary>SkipCinematic: the Trigger packet (only for skip state 1 or 2), the notice, and the prompt goes.</summary>
    public void SkipCinematic()
    {
        if (SkipState is 1 or 2) SendTrigger();
        Skipped?.Invoke();
        skipped = true;
        Pane?.SetSkipVisible(false);
    }

    /// <summary>Esc at the pane (UICutSceneCinematic vfunc_13, @0x141907c80, uicutscenecinematic.cpp:221-237): with the
    /// Develop feature (CFeatureManager 2) on, Shift+Esc ends the cinematic; otherwise Esc, Shift or not, asks to skip
    /// (RequestSkip), so the skip notify goes out. The dialogs' key pass calls a dialog's vfunc_13 only while its root clip is
    /// shown (vfunc_9, uiswfentitymanager.cpp:319; uiswfentity.h:46), and the pane is shown only in the cinematic sub scene,
    /// that is with the key group on. True when the pane took the key.</summary>
    public bool KeyEscape(bool shift, bool develop)
    {
        if (!KeyMode) return false;
        if (shift && develop) ForceEnd(); else RequestSkip();
        return true;
    }

    /// <summary>The key handler's Shift+Esc with the Develop feature (vfunc_13): the trigger, the key group off, the pane
    /// closed (FUN_1419080b0). It posts no skip notify (only SkipCinematic does, :1109-1120), so a telescope's view stays.</summary>
    public void ForceEnd()
    {
        SendTrigger();
        SetCinematicKeyMode(false);
        IsShowing = false;
        Pane?.Close();
    }

    // ---- talks ------------------------------------------------------------------------------------------------------

    void ClearTalks()
    {
        Pane?.HideScript();
        Talks.Clear();
        CurrentTalk = null;
        talkEnd = 0;
        currentSound = "";
        StopVoice();
    }

    /// <summary>FUN_141908750 (the talk half of Update): the current line stays until its end, then the next one comes.</summary>
    void UpdateTalks(long now)
    {
        if (CurrentTalk != null)
        {
            if (now < talkEnd) return;
            Pane?.HideScript();
            CurrentTalk = null;
            currentSound = "";
            StopVoice();
        }
        if (Talks.Count == 0) return;
        var t = Talks[0];
        Talks.RemoveAt(0);
        var text = Translate(t.Script);
        var sound = Stripped(t.Script);
        var length = sound.Length == 0 ? 0 : SoundLength(sound);
        // FUN_141909880: the line lasts the longer of the packet's duration and the voice's; the voice plays only if it has a length
        currentSound = length != 0 ? sound : "";
        CurrentTalk = t;
        talkEnd = now + Math.Max(length, t.Duration);
        if (t.NpcId == 0)
        {
            if (PlayerName() is { } me) Pane?.SetScript(me, text, "", 0);
        }
        else Pane?.SetScript(NpcName(t.NpcId), text, IllustPath(t.Illustration), t.Flag);
        if (currentSound.Length != 0) PlayVoice(currentSound);
    }

    // ---- balloons ---------------------------------------------------------------------------------------------------

    /// <summary>FUN_141909eb0: the line of an object's group, timed from now by its delay and duration.</summary>
    void AddBalloon(CinematicPacket.BalloonTalk b)
    {
        string text, sound = "";
        if (!b.Raw) { sound = Stripped(b.Text); text = Translate(b.Text); }
        else text = b.Text;      // STILL-OPEN: GetTextFilter()->GetString(text), the text filter's key lookup
        if (text.Length == 0) return;
        if (!BalloonGroups.TryGetValue(b.ObjectId, out var list)) BalloonGroups[b.ObjectId] = list = new();
        if (!groups.TryGetValue(b.ObjectId, out var g)) groups[b.ObjectId] = g = new();
        var start = Clock() + b.Delay;
        list.Add(b with { Text = text });
        g.Lines.Add(new Line { Packet = b with { Text = text }, Start = start, End = start + b.Duration, Sound = sound });
    }

    void ClearBalloonGroup(int objectId)
    {
        if (groups.TryGetValue(objectId, out var g))
        {
            if (g.Shown) { HideBalloon(objectId); g.Shown = false; }
            g.Lines.Clear();
        }
        if (BalloonGroups.TryGetValue(objectId, out var lines)) lines.Clear();
    }

    /// <summary>NotifyRemoveGameObject: the object's group goes.</summary>
    public void RemoveGameObject(int objectId)
    {
        groups.Remove(objectId);
        BalloonGroups.Remove(objectId);
    }

    /// <summary>UpdateBalloons: per group, an actor that is there and not culled hides its shown line at its end, else starts the
    /// first line whose start has passed.</summary>
    void UpdateBalloons(long now)
    {
        foreach (var (id, g) in groups)
        {
            if (!BalloonActorReady(id)) continue;
            if (g.Shown)
            {
                if (now < g.Hide) continue;
                HideBalloon(id);
                g.Shown = false;
            }
            var i = g.Lines.FindIndex(l => l.Start < now);
            if (i < 0) continue;
            var line = g.Lines[i];
            ShowBalloon(id, line.Packet.Text);
            g.Shown = true;
            g.Hide = line.End;
            g.Lines.RemoveAt(i);
            if (BalloonGroups.TryGetValue(id, out var queued)) queued.Remove(line.Packet);     // the record compares by value
            if (line.Sound.Length != 0) PlayVoice(line.Sound);
        }
    }

    /// <summary>UICutSceneCinematic::vfunc_15: the talks, then the balloons, at <paramref name="now"/> ms.</summary>
    public void Update(long now)
    {
        UpdateTalks(now);
        UpdateBalloons(now);
    }

    public void Update() => Update(Clock());
}
