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
}
