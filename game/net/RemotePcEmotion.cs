using System;
using Godot;
using Ms2.Formats.World;
using Ms2.Net.Replay;

namespace Ms2Godot.Net;

/// <summary>
/// Another PC's body side of StateEmotion / StateEmotionIdle (<see cref="RemotePcStates"/>, stateemotionidle.cpp:196-870):
/// the skill row, the face item's emotion group (m_spD10, PC::vfunc_69's face item), the body's emotion sequences and the
/// name tag offset. STILL-OPEN: another PC's face emotion (FUN_14166ef10 / FUN_14166f8f0): the port has no face emotion for
/// other PCs (<see cref="Ms2.Ui.FaceEmotion"/>), so <see cref="Face"/> and <see cref="FaceRest"/> do nothing; the HP bar
/// offset (m_f174), as another PC's HP bar is not drawn here.
/// </summary>
public sealed partial class RemotePc : IEmotionBody
{
    /// <summary>SkillDataTableCli::GetSkillData of an emotion skill id, null without a row.</summary>
    public Func<int, EmotionRow> EmotionRowOf;
    /// <summary>The PC's face item's emotion group (m_spD10) now, null without one.</summary>
    public Func<EmotionGroup> EmotionGroupOf;
    /// <summary>m_f170 (FUN_141670330): added to OffsetPcNametag for the PC's name tag.</summary>
    public Action<float> NameTagOffset;

    public EmotionRow Row(int id) => EmotionRowOf?.Invoke(id);

    public string Animation(string name, int kind) => Ms2.Ui.Skills.EmotionCast.Animation(EmotionGroupOf?.Invoke(), name, kind);

    /// <summary>PCBase::PlayAnimation(name, 1, bForce, false) outside the action table: the kf's own cycle (0 loop, 1 back
    /// and forth, 2 clamp); with <paramref name="force"/> the sequence starts over even when it is the one playing.</summary>
    public EmotionPlay Play(string sequence, bool force)
    {
        if (Anims == null || Anim == null) return default;
        string key = Anims.Sequence(sequence, false);
        if (key == null || !Anim.HasAnimation(key)) return default;
        if (!force && key == currentName && Anim.IsPlaying()) return default;
        var a = Anim.GetAnimation(key);
        Anims.Kf.TryGetValue(key, out var kf);
        bool loop = kf == null || kf.Cycle == 0;
        a.LoopMode = loop ? Godot.Animation.LoopModeEnum.Linear
            : kf.Cycle == 1 ? Godot.Animation.LoopModeEnum.Pingpong : Godot.Animation.LoopModeEnum.None;
        bool same = key == currentName;
        Anim.Play(key, Blend(key));
        Anim.SpeedScale = 1f;
        if (same) Anim.Seek(0, true);
        currentAct = -1; currentName = key; lastPos = -1;
        return new EmotionPlay(true, a.Length, loop);
    }

    void IEmotionBody.Offsets(float nameTag, float hpBar) => NameTagOffset?.Invoke(nameTag);

    public void Face(string anim) { }

    public void FaceRest() { }
}
