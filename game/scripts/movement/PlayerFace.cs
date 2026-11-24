using Ms2.Formats.World;
using Ms2.Ui;

namespace Ms2Godot;

/// <summary>
/// The own pc's face emotion (<see cref="FaceEmotion"/>): the face item's emotion group, ticked by the player's step, its
/// rest face following the battle stance, and the trigger's Ui sub 9 on it.
/// </summary>
public partial class Player
{
    /// <summary>The face emotion, null before the field sets it.</summary>
    public FaceEmotion FaceEmotion { get; private set; }

    long FaceMs => (long)(gameTime * 1000);

    /// <summary>The face model on (PCBase::FUN_14166f380): the group set, the rest face of the current stance.</summary>
    public void SetFace((FaceEmotion face, EmotionGroup group) f)
    {
        FaceEmotion = f.face;
        if (FaceEmotion == null) return;
        FaceEmotion.SetBattle(InBattle, FaceMs);
        FaceEmotion.SetGroup(f.group, FaceMs);
    }

    /// <summary>The trigger's Ui sub 9 on the own pc: the named face, or the rest face for an empty name.</summary>
    public void TriggerFace(string name) => FaceEmotion?.Trigger(name, FaceMs);

    /// <summary>PCBase::FUN_14166ef10(name, 1, 0, force) on the own pc (StateStun's "Stun", statestun.cpp:112): whether it
    /// played.</summary>
    public bool PlayFace(string name, bool force) => FaceEmotion?.Play(name, force, FaceMs) == true;

    // the body's key pass: the sequence it last read and the position it read to; FaceKeysFromStart makes the next pass
    // throw the keys in [0, position], as an activated sequence throws its keys from 0 (m_bNeedFirstTextKey)
    const double FaceKeysFromStart = -2;
    string faceKeyAnim;
    double faceKeyPos = FaceKeysFromStart;

    /// <summary>
    /// The keys the body's target sequence passed since the last step, handed to PCBase::TextKeyEvent's emo_ branch
    /// (pcbase.cpp:1032-1039, <see cref="FaceEmotion.TextKey"/>): a sequence such as Emotion_Angry_A or Bore_A changes the
    /// face at its key. The keys are the kf's and the body's companion xml's (<see cref="ActionAnims.Kf"/>).
    /// </summary>
    void FaceTextKeys()
    {
        if (FaceEmotion == null || Anims == null || Anim == null || string.IsNullOrEmpty(CurrentAnim) || !Anim.IsPlaying()) return;
        if (faceKeyAnim != CurrentAnim) { faceKeyAnim = CurrentAnim; faceKeyPos = FaceKeysFromStart; }
        double pos = Anim.CurrentAnimationPosition;
        double from = faceKeyPos;
        faceKeyPos = pos;
        if (!Anims.Kf.TryGetValue(CurrentAnim, out var kf) || kf.TextKeys.Count == 0) return;
        foreach (var (t, key) in kf.TextKeys)
        {
            bool crossed = from == FaceKeysFromStart ? t <= pos : pos >= from ? from < t && t <= pos : t > from || t <= pos;   // a loop's wrap
            if (crossed) FaceEmotion.TextKey(key, FaceMs);
        }
    }

    /// <summary>A sequence started again from its start throws its keys again.</summary>
    void FaceKeysRestart() => faceKeyPos = FaceKeysFromStart;
}
