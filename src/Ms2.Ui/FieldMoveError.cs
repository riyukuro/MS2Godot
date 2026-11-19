using Ms2.Formats.Text;
using Ms2.Quest;

namespace Ms2.Ui;

/// <summary>
/// GetFieldMoveErrorMessage @0x140cc20a0 (unnamed/unit_140cc20a0_fieldmoveerrormessage.cpp:151): the message box text of a
/// RequestFieldEnter error (CSceneStateGame::OnRequestFieldEnter, scenestategame.cpp:2361). Some errors are common string
/// ids (GetStringCommon), some common string keys (GetStringCommonByKey); any other error is s_move_err_default with the
/// error code as {0} (FUN_140cc25a0 -> FUN_14028dfa0).
/// </summary>
public static class FieldMoveError
{
    /// <summary>The default key, formatted with the error code.</summary>
    public const string DefaultKey = "s_move_err_default";

    /// <summary>The common string id of an error, or null when the error goes by key.</summary>
    public static int? StringId(int error) => error switch
    {
        8 => 0x268,
        9 or 0x15 => 0x269,
        0x11 => 0x26a,
        10 or 0x16 => 0x26c,
        0xc => 0x26d,
        _ => null,
    };

    /// <summary>The common string key of an error: the switch's own key, the key of its string id, or the default key.</summary>
    public static string Key(int error)
    {
        if (StringId(error) is int id) return CommonStringIds.Key(id);
        return error switch
        {
            0x1b => "s_party_err_not_chief",
            0x1c => "s_move_err_NotFoundParty",
            0x1d => "s_move_err_dungeon_not_exist",
            0x1e => "s_move_err_FailCreateDungeon",
            0x1f => "s_move_err_DungeonMatch",
            0x20 => "s_move_err_InsideDungeonUser",
            0x21 => "s_move_err_ExpireEnterTime",
            0x22 => "s_move_err_InsideSurvivalSquad",
            7 => "s_move_err_field_limit",
            0x23 => "s_move_err_wedding_complete",
            _ => DefaultKey,
        };
    }

    /// <summary>The message: the common string of <see cref="Key"/>; the default one takes the error code.</summary>
    public static string Text(int error, System.Func<string, string> commonByKey)
    {
        string key = Key(error);
        string text = key == null ? "" : commonByKey(key) ?? "";
        return key == DefaultKey ? ClientFormat.Format(text, error) : text;
    }
}
