using System.Numerics;
using Ms2.Formats.Model;

namespace Ms2.Formats.World;

/// <summary>
/// The effects and sounds of one CDropItem (dropitem.cpp) through the field's effect service:
/// <list type="bullet">
/// <item>the drop sound when it is thrown (SetState(1), :646-656): the style row's dropSoundID from "ms2sound/sound/item" at
/// the drop, with the object kind of what it flew out of (WorldService::FUN_141bd19d0 of m_nOwnerObjId); only on the paths
/// that found the ground, the one that found none returns before it (:571-583);</item>
/// <item>the grade effect of the item's rarity (CItemDataTable::GetGradeEffect, the rarity glow) and the row's beEffect when
/// it has one, kept at the drop with no rotation while it rests (SetState(0), :706-726);</item>
/// <item>both stopped (CEffectService::FUN_1411a6060) when it flies to its picker (SetState(2), :658-663) or is freed (the
/// dtor, :226-230);</item>
/// <item>the get sound, the row's getSoundID at the picker with kind 1, when the local player picked it up (OnPickedUp,
/// :993-1006: the picker cast to MyPC).</item>
/// </list>
/// The sounds go to the sink as (group, event, object kind, client position).
/// </summary>
public sealed class DropEffects
{
    /// <summary>FUN_1411a5110's group of both sounds.</summary>
    public const string SoundGroup = "ms2sound/sound/item";
    /// <summary>The kind OnPickedUp plays the get sound with (the local player).</summary>
    public const int LocalPlayerKind = 1;
    /// <summary>DAT_143a09600: the identity rotation of the effects' transform.</summary>
    static readonly double[] NoRotation = { 1, 0, 0, 0, 1, 0, 0, 0, 1 };

    readonly IRegionSkillEffects fx;
    readonly Action<string, string, int, Vector3> sound;
    int effectA, effectB;

    /// <summary>m_nEffectIdA (the grade effect) and m_nEffectIdB (the beEffect): the kept handles, 0 when none runs.</summary>
    public int EffectA => effectA;
    public int EffectB => effectB;

    public DropEffects(IRegionSkillEffects fx, Action<string, string, int, Vector3> sound)
    {
        this.fx = fx;
        this.sound = sound;
    }

    /// <summary>SetState(1)'s end: the drop sound; false when the row has none.</summary>
    public bool Thrown(DropStyle row, int sourceKind, Vector3 at)
    {
        if (string.IsNullOrEmpty(row?.DropSoundId)) return false;
        sound?.Invoke(SoundGroup, row.DropSoundId, sourceKind, at);
        return true;
    }

    /// <summary>SetState(0): the grade effect, then the beEffect when the row names one.</summary>
    public void Rested(string gradeEffect, DropStyle row, Vector3 at)
    {
        if (fx == null) return;
        var x = new Xform(new Vec3(at.X, at.Y, at.Z), NoRotation, 1);
        if (!string.IsNullOrEmpty(gradeEffect)) effectA = fx.Spawn(gradeEffect, x, false, 0);
        if (!string.IsNullOrEmpty(row?.BeEffect)) effectB = fx.Spawn(row.BeEffect, x, false, 0);
    }

    /// <summary>SetState(2) and the dtor: both kept effects stopped at once.</summary>
    public void Stop()
    {
        if (effectA != 0) fx?.FadeOut(effectA, 0);
        if (effectB != 0) fx?.FadeOut(effectB, 0);
        effectA = effectB = 0;
    }

    /// <summary>OnPickedUp with the local player as picker: the get sound at the picker; false when the row has none.</summary>
    public bool PickedUp(DropStyle row, Vector3 picker)
    {
        if (string.IsNullOrEmpty(row?.GetSoundId)) return false;
        sound?.Invoke(SoundGroup, row.GetSoundId, LocalPlayerKind, picker);
        return true;
    }
}
