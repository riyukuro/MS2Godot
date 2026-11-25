namespace Ms2.Net.Replay;

/// <summary>The skill row StateEmotion / StateEmotionIdle read (SkillDataTableCli::GetSkillData): its main type, basic
/// paramStr, kinds offsetNameTag and the HP bar's offset (the KMS row's offsetNameTag; the GMS2 row's offsetHPBar).</summary>
public sealed record EmotionRow(int MainType, string ParamStr, int OffsetNameTag, int OffsetHpBar);

/// <summary>A body sequence PCBase::PlayAnimation started: its length in seconds and whether it loops (the kf's cycle 0).</summary>
public readonly record struct EmotionPlay(bool Played, double Length, bool Loops);

/// <summary>
/// The PCBase side of a remote PC that StateEmotion (0xb) and StateEmotionIdle (0xf) drive (stateemotionidle.cpp:196-352,
/// :646-870): the skill row, the emotion group's animations (GetEmotionAnimation), the body's sequences, the face and the
/// name tag offsets.
/// </summary>
public interface IEmotionBody
{
    /// <summary>SkillDataTableCli::GetSkillData(id): null without a row.</summary>
    EmotionRow Row(int id);
    /// <summary>PCBase::GetEmotionAnimation @0x14166f790 (pcbase.cpp:1559-1570): the animation of kind
    /// <paramref name="kind"/> (0 anim, 1 idleAnim, 2-4 the bore animations) of the emotion of that name in the PC's face
    /// item's emotion group (m_spD10); null without one.</summary>
    string Animation(string name, int kind);
    /// <summary>PCBase::PlayAnimation(name, 1, bForce, false): without <paramref name="force"/> a sequence already playing
    /// is left as it is (not played); a name the body lacks plays nothing.</summary>
    EmotionPlay Play(string sequence, bool force);
    /// <summary>FUN_141670330 / FUN_141670360: m_f170 / m_f174, added to OffsetPcNametag and OffsetPcHpbar.</summary>
    void Offsets(float nameTag, float hpBar);
    /// <summary>FUN_14166ef10(anim, 1, 0, false): the face plays the emotion.</summary>
    void Face(string anim);
    /// <summary>FUN_14166f8f0(0, 1): the face rests.</summary>
    void FaceRest();
}
