using Ms2.Ui.Skills;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The skill preview avatar's StateEmotion -> StateEmotionIdle / StateIdle order (stateemotionidle.cpp), with a
/// recording host: what is played, with which bForce, and when the face plays and rests.</summary>
public class PreviewEmotionTests
{
    sealed class Rig
    {
        public readonly List<string> Log = new();
        public readonly Dictionary<(string, int), string> Anims = new();
        public readonly Dictionary<string, (double, int)> Seq = new();
        public string Current;
        public readonly PreviewEmotion E;

        public Rig()
        {
            E = new PreviewEmotion
            {
                Animation = (n, k) => Anims.TryGetValue((n, k), out var a) ? a : null,
                Play = (n, force) =>
                {
                    if (!Seq.TryGetValue(n, out var s)) return null;
                    if (!force && Current == n) return null;
                    Current = n;
                    Log.Add($"play {n} {force}");
                    return s;
                },
                Face = n => Log.Add($"face {n}"),
                FaceRest = () => Log.Add("rest"),
                Idle = () => { Current = "idle"; Log.Add("idle"); },
            };
        }

        public static SkillData Row(uint id, string param, int main = EmotionCast.MainTypeEmotion) =>
            new() { Id = id, MainType = main, ParamStr = param };
    }

    [Test]
    public void AnimOnceThenIdleAnimLooped()
    {
        var r = new Rig();
        r.Anims[("Hello", 0)] = "Emotion_Hello_A";
        r.Anims[("Hello", 1)] = "Emotion_Hello_Idle_A";
        r.Seq["Emotion_Hello_A"] = (2.0, 2);          // clamped: one EndOfSequence at its end
        r.Seq["Emotion_Hello_Idle_A"] = (1.0, 0);
        r.E.Enter(Rig.Row(90200001, "Hello"));
        Assert.That(r.E.State, Is.EqualTo(EmotionCast.StateEmotion));
        Assert.That(r.Log, Is.EqualTo(new[] { "face Hello", "play Emotion_Hello_A True" }));
        r.E.Tick(1.5);
        Assert.That(r.E.State, Is.EqualTo(EmotionCast.StateEmotion));
        r.E.Tick(0.6);                                  // the anim's end: on to StateEmotionIdle, the face not rested
        Assert.That(r.E.State, Is.EqualTo(EmotionCast.StateEmotionIdle));
        Assert.That(r.Log.Skip(2), Is.EqualTo(new[] { "face Hello", "play Emotion_Hello_Idle_A True" }));
        for (int i = 0; i < 10; i++) r.E.Tick(0.5);     // vfunc_9 without bForce: the loop plays on, nothing restarted
        Assert.That(r.Log, Has.Count.EqualTo(4));
        Assert.That(r.E.State, Is.EqualTo(EmotionCast.StateEmotionIdle));
    }

    [Test]
    public void NoIdleAnimGoesToIdleWithTheFaceRested()
    {
        var r = new Rig();
        r.Anims[("Angry", 0)] = "Emotion_Angry_A";
        r.Seq["Emotion_Angry_A"] = (1.0, 2);
        r.E.Enter(Rig.Row(90200002, "Angry"));
        r.E.Tick(1.01);
        Assert.That(r.E.State, Is.EqualTo(0));
        Assert.That(r.Log, Is.EqualTo(new[] { "face Angry", "play Emotion_Angry_A True", "rest", "idle" }));
    }

    [Test]
    public void LoopedAnimEndsEachPass()
    {
        var r = new Rig();
        r.Anims[("Cry", 0)] = "Emotion_Cry_A";
        r.Anims[("Cry", 1)] = "Emotion_Cry_Idle_A";
        r.Seq["Emotion_Cry_A"] = (1.0, 0);             // a looped anim: its first pass's end is the EndOfSequence
        r.Seq["Emotion_Cry_Idle_A"] = (1.0, 0);
        r.E.Enter(Rig.Row(90200003, "Cry"));
        r.E.Tick(0.9);
        Assert.That(r.E.State, Is.EqualTo(EmotionCast.StateEmotion));
        r.E.Tick(0.2);
        Assert.That(r.E.State, Is.EqualTo(EmotionCast.StateEmotionIdle));
    }

    [Test]
    public void SameParamInStateEmotionIsKeptOtherwiseEnteredAgain()
    {
        var r = new Rig();
        r.Anims[("Hello", 0)] = "Emotion_Hello_A";
        r.Anims[("Hello", 1)] = "Emotion_Hello_Idle_A";
        r.Seq["Emotion_Hello_A"] = (2.0, 2);
        r.Seq["Emotion_Hello_Idle_A"] = (1.0, 0);
        var hello = Rig.Row(90200001, "Hello");
        r.E.Enter(hello);
        r.E.Tick(1.0);
        r.E.Enter(hello);                               // FUN_14166ed70: 0xb plays "Hello" already, kept
        Assert.That(r.Log, Has.Count.EqualTo(2));
        r.E.Tick(1.1);                                  // the first play's end still counts
        Assert.That(r.E.State, Is.EqualTo(EmotionCast.StateEmotionIdle));
        r.E.Enter(hello);                               // from 0xf: entered again, the exit rests the face first
        Assert.That(r.E.State, Is.EqualTo(EmotionCast.StateEmotion));
        Assert.That(r.Log.Skip(4), Is.EqualTo(new[] { "rest", "face Hello", "play Emotion_Hello_A True" }));
    }

    [Test]
    public void RowWithoutEmotionTypeOrAnimLeavesAtTheNextFrame()
    {
        var r = new Rig();
        r.E.Enter(Rig.Row(10200001, "", 1));            // not main type 0xe: nothing played
        Assert.That(r.Log, Is.Empty);
        Assert.That(r.E.State, Is.EqualTo(EmotionCast.StateEmotion));
        r.E.Tick(0.016);
        Assert.That(r.E.State, Is.EqualTo(0));
        Assert.That(r.Log, Is.EqualTo(new[] { "rest", "idle" }));

        r = new Rig();
        r.E.Enter(Rig.Row(90200009, "NoSuch"));         // the group has no such emotion: the face is asked, no anim
        r.E.Tick(0.016);
        Assert.That(r.Log, Is.EqualTo(new[] { "face NoSuch", "rest", "idle" }));
    }
}
