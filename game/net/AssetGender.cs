namespace Ms2Godot.Net;

/// <summary>Which item-model assets a character of a gender wears.</summary>
public static class AssetGender
{
    /// <summary>
    /// ModelPart gender: default 2 (itemdatastruct.cpp:280), read from the asset's "gender"; the part is used when it is 2 (both)
    /// or the character's own (pc.cpp:1226). GMS2 writes gender="2" on every unisex asset (all hair); KMS leaves it off.
    /// </summary>
    public static bool Matches(string attr, int gender) => attr == null || !int.TryParse(attr, out int g) || g == 2 || g == gender;
}
