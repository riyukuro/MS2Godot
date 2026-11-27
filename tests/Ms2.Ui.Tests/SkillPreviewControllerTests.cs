using Ms2.Ui.Hud;
using Ms2.Ui.Skills;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>UISkillPreviewDialog (uiskillpreviewdialog.cpp): vfunc_65's reset and CreateAvatar, FUN_140e93e70's kept action and
/// its emotion / skill pick, and the replay at the first draw (vfunc_25). No movie: these run before and without one.</summary>
public class SkillPreviewControllerTests
{
    sealed class Rig
    {
        public readonly Dictionary<uint, SkillData> Skills = new();
        public readonly List<string> Played = new();
        public bool Player = true;
        public SkillPreviewController C;

        public Rig()
        {
            Skills[90200001] = new SkillData { Id = 90200001, MainType = 0xe, ParamStr = "Emotion_Hello_A" };
            Skills[10200001] = new SkillData { Id = 10200001, MainType = 1 };
            C = new SkillPreviewController
            {
                Skill = id => Skills.TryGetValue(id, out var s) ? s : null,
                HasLocalPlayer = () => Player,
                PlayEmotion = s => Played.Add($"emotion {s.Id}"),
                PlaySkill = (s, l) => Played.Add($"skill {s.Id} {l}"),
            };
        }
    }

    [Test]
    public void KeptUntilTheAvatarIsUpThenPlayedOnce()
    {
        var r = new Rig();
        r.C.Open();
        Assert.That(r.C.Avatar, Is.Not.Null, "CreateAvatar with a local player");
        r.C.Preview(90200001, 0);          // PreviewAction right after OpenDialog: the avatar is not loaded yet
        Assert.That(r.Played, Is.Empty);
        Assert.That(r.C.PendingSkill, Is.EqualTo(90200001u));
        r.C.AvatarReady();                 // vfunc_25's first draw replays it
        Assert.That(r.Played, Is.EqualTo(new[] { "emotion 90200001" }));
        Assert.That(r.C.PendingSkill, Is.EqualTo(0u));
        r.C.AvatarReady();                 // a later draw: nothing kept, nothing played
        Assert.That(r.Played, Has.Count.EqualTo(1));
    }

    [Test]
    public void LoadedAvatarPlaysAtOnceAndPicksEmotionOrSkill()
    {
        var r = new Rig();
        r.C.Open();
        r.C.AvatarReady();
        r.C.Preview(90200001, 0);
        r.C.Preview(10200001, 3);
        Assert.That(r.Played, Is.EqualTo(new[] { "emotion 90200001", "skill 10200001 3" }));
    }

    [Test]
    public void NothingForSkillZeroNoAvatarOrUnknownRow()
    {
        var r = new Rig { Player = false };
        r.C.Open();
        Assert.That(r.C.Avatar, Is.Null, "CreateAvatar asserts without a local player");
        r.C.Preview(90200001, 0);
        Assert.That(r.C.PendingSkill, Is.EqualTo(0u), "no avatar: FUN_140e93e70 returns before keeping it");

        r = new Rig();
        r.C.Open();
        r.C.AvatarReady();
        r.C.Preview(0, 0);
        r.C.Preview(12345678, 0);          // no row: asserts, the action stays kept
        Assert.That(r.Played, Is.Empty);
        Assert.That(r.C.PendingSkill, Is.EqualTo(12345678u));
    }

    [Test]
    public void OpenClearsTheKeptAction()
    {
        var r = new Rig();
        r.C.Open();
        r.C.Preview(90200001, 0);
        r.C.Open();                        // vfunc_65 again: m_skill = m_level = 0, the avatar kept
        Assert.That(r.C.PendingSkill, Is.EqualTo(0u));
        r.C.AvatarReady();
        Assert.That(r.Played, Is.Empty);
    }
}
