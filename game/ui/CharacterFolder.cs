using Godot;
using Ms2.Ui;
using Ms2Godot.Flow;

namespace Ms2Godot.Ui;

/// <summary>
/// GetCharacterFolder (unit_141274d10_uihelpers.cpp:408): the client's "../../Config/&lt;account id&gt;/&lt;character id&gt;"
/// (FUN_1412756c0 formats both as %I64u, from the account and character the client entered with). Ours is
/// user://Config/&lt;account id&gt;/&lt;character id&gt;, beside user://Config/GameOption.xml.
/// </summary>
public sealed class CharacterFolder : ICharacterFolder
{
    readonly string dir;

    public CharacterFolder(long accountId, long characterId) =>
        dir = $"user://Config/{(ulong)accountId}/{(ulong)characterId}";

    /// <summary>The chosen character's folder; null when a scene runs without a chosen character.</summary>
    public static CharacterFolder ForChosen() =>
        Session.Chosen?.User is { } u ? new CharacterFolder(u.AccountId, u.CharacterId) : null;

    public string Read(string file)
    {
        string path = dir + "/" + file;
        if (!Godot.FileAccess.FileExists(path)) return null;
        using var f = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
        return f?.GetAsText();
    }

    public void Write(string file, string text)
    {
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(dir));
        using var f = Godot.FileAccess.Open(dir + "/" + file, Godot.FileAccess.ModeFlags.Write);
        f?.StoreString(text);
    }
}
