namespace Ms2Godot.Net;

/// <summary>Which item-model assets a character of a gender wears.</summary>
public static class AssetGender
{
    /// <summary>
    /// ModelPart gender: default 2 (itemdatastruct.cpp:281), read from the asset's "gender"; the part is used when it is 2 (both)
    /// or the character's own (pc.cpp:1226). GMS2 writes gender="2" on every unisex asset (all hair); KMS leaves it off.
    /// </summary>
    public static bool Matches(string attr, int gender) => attr == null || Atoi(attr) is var g && (g == 2 || g == gender);

    /// <summary>XmlGetAttr reads the number with atoi: leading blanks, a sign, the digits; no digits is 0.</summary>
    static int Atoi(string s)
    {
        int i = 0, n = 0; s = s.TrimStart(); bool neg = false;
        if (i < s.Length && (s[i] == '-' || s[i] == '+')) neg = s[i++] == '-';
        while (i < s.Length && char.IsAsciiDigit(s[i])) n = n * 10 + (s[i++] - '0');
        return neg ? -n : n;
    }
}
