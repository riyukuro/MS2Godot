using Ms2.Formats.World;
using Ms2.Quest;

namespace Ms2.Ui.Hud;

/// <summary>
/// The npcs' idle monologue balloons: CNpc::UpdateMonologue (@0x141099370, npcactorandvibrate.cpp:2049), run from each
/// npc's frame (CNpc::vfunc_53, :2374), CNpc::ResetMonologueTimer (@0x141099580) and UITalkManager::ShowNpcMonologue
/// (@0x141b20790, uitalkcinematic.cpp:1578), which shows the picked &lt;monologue&gt; in the npc's chat balloon through
/// PCBase::ShowChatBalloon (@0x14166fff0, pcbase.cpp:1664).
/// <para>
/// Per npc two counters: m_nDE0, the ms since the last show or stop, and m_nDE4, a random head start (both 0 from the CNpc
/// constructor, :497). An npc with no quest mark (quest state 4) and a balloon not kept for a chat: while the balloon is
/// active, MonologueShowTime ms stop it; else, once m_nDE0 + m_nDE4 reaches MonologueInterval + MonologueRandom, the talk
/// manager shows a monologue. Either way the timer resets. The three constants are MS2Ticks: seconds in constants.xml,
/// held in ms (ParseTick, stringconversionhelpers.cpp:63).
/// </para>
/// <para>
/// STILL-OPEN: the affinity npcs' kinds 7 (maid not expired) and 8 (expired) of ShowNpcMonologue (vfunc_128 and
/// GetMaid) are not ported, every npc asks for kind 9.
/// </para>
/// </summary>
public sealed class NpcMonologue
{
    /// <summary>One npc's monologue state (CNpc m_nDE0 / m_nDE4).</summary>
    public sealed class Npc
    {
        public int ObjectId;
        public int NpcId;
        /// <summary>The npc's chat balloon actor (its CNpc::vfunc_85 entity is asked for each frame).</summary>
        public ChatBalloonController.Actor Balloon;
        public int Elapsed;          // m_nDE0
        public int HeadStart;        // m_nDE4
    }

    readonly ChatBalloonController balloons;
    readonly NpcScriptTable scripts;

    /// <summary>MonologueInterval, MonologueRandom and MonologueShowTime, in ms.</summary>
    public uint IntervalMs = 15000, RandomMs = 10000, ShowTimeMs = 5000;
    /// <summary>GFxSceneService FUN_1422b9ea0: a sub scene (a talk or cut scene) is up, no monologue runs.</summary>
    public Func<bool> InSubScene = () => false;
    /// <summary>The npc's quest state (m_questState.m_state); 4 is no mark.</summary>
    public Func<Npc, int> QuestState = _ => 4;
    /// <summary>UITalkManager m_nNpcId: the object id of the npc in talk, 0 when none (GetTalking).</summary>
    public Func<int> TalkingObjectId = () => 0;
    /// <summary>The content text as shown: its object words (ReplaceObjectWords, run at load by CScriptContent::vfunc_2), then ReplaceValueWords.</summary>
    public Func<string, string> Resolve = t => t;
    /// <summary>CEffectService::PlayVoiceAt(voice, the npc's position).</summary>
    public Action<Npc, string> PlayVoice = (_, _) => { };
    /// <summary>
    /// FUN_140767ff0 and GetRandomRange: the client's s_random (an Unk_Random seeded by GetTickCountMs2() + rand(),
    /// questmasktable.cpp:381).
    /// </summary>
    public Func<uint> Rand;

    public NpcMonologue(ChatBalloonController balloons, NpcScriptTable scripts)
    {
        this.balloons = balloons; this.scripts = scripts;
        var shared = new QuestRandom(unchecked((int)Environment.TickCount64 + Random.Shared.Next(0x8000)));
        Rand = shared.Next;
    }

    /// <summary>CNpc::UpdateMonologue(fTime), <paramref name="seconds"/> the frame time.</summary>
    public void Update(Npc n, float seconds)
    {
        if (InSubScene()) return;
        if (QuestState(n) != 4) return;
        var balloon = balloons.NpcBalloon(n.Balloon);
        if (balloon == null || balloon.Kept) return;
        int ms = (int)(seconds * 1000.0f);         // FUN_14028d260
        if (balloon.Active)
        {
            n.Elapsed += ms;
            if ((uint)n.Elapsed >= ShowTimeMs)
            {
                balloons.Stop(balloon);              // vfunc_3
                ResetTimer(n);
            }
        }
        else
        {
            n.Elapsed += ms;
            if ((uint)(n.Elapsed + n.HeadStart) >= IntervalMs + RandomMs)
            {
                Show(n);
                ResetTimer(n);
            }
        }
    }

    /// <summary>
    /// CNpc::ResetMonologueTimer: m_nDE0 0; with a MonologueRandom, m_nDE4 = an Unk_Random seeded by the object id times
    /// GetRandomRange(MonologueRandom, 0), its first draw mod MonologueRandom.
    /// </summary>
    public void ResetTimer(Npc n)
    {
        n.Elapsed = 0;
        if (RandomMs == 0) return;
        int range = (int)(Rand() % RandomMs);       // GetRandomRange(n, 0): Range(n, 0)
        var r = new QuestRandom(unchecked(n.ObjectId * range));
        n.HeadStart = (int)(r.Next() % RandomMs);
    }

    /// <summary>
    /// UITalkManager::ShowNpcMonologue(npc, friendly): not for the npc in talk; the kind-9 pick (FUN_140856fa0) of the
    /// npc's monologues, its first content shown in the balloon (PCBase::ShowChatBalloon: ReplaceValueWords, SetText,
    /// vfunc_2), and its voice played at the npc when there is no talk.
    /// </summary>
    public void Show(Npc n)
    {
        int talking = TalkingObjectId();
        if (talking == n.ObjectId) return;
        const int kind = 9;
        var script = scripts?.PickMonologue(n.NpcId, kind, Rand);
        if (script == null || script.Contents.Count == 0) return;
        var content = script.Contents[0];
        ShowChatBalloon(n, content.Text);
        if (talking == 0 && !string.IsNullOrEmpty(content.VoiceId)) PlayVoice(n, content.VoiceId);
    }

    /// <summary>PCBase::ShowChatBalloon: vfunc_94(0) (empty for an npc), the value words, then the balloon's text and start.</summary>
    public void ShowChatBalloon(Npc n, string text)
    {
        text = Resolve(text ?? "");
        var balloon = balloons.NpcBalloon(n.Balloon);
        if (balloon == null) return;
        ChatBalloonController.SetText(balloon, text);
        balloons.Start(balloon);
    }
}
