using System;
using Ms2.Quest;

namespace Ms2.Ui.Hud;

/// <summary>
/// The chat lines of an exp gain (uiworldshareinfoeventlistener.cpp): FUN_1413606c0 for OnExpUp mode 0
/// (scenestategame.cpp:4180) and FUN_1413610f0 for OnPrestige kind 1 (:8825).
/// </summary>
public static class ExpChatLines
{
    /// <summary>The chat kind of the exp line (FUN_1412751b0(strA, 8)).</summary>
    public const int ExpKind = 8;

    /// <summary>The lines of one gain: <see cref="Line"/> goes out with kind 8, <see cref="Second"/> (only the assist bonus
    /// type has one) with kind 0x14; <see cref="Notify"/> asks for the reward notice FUN_141abf230(amount, 0).</summary>
    public readonly record struct Lines(string Line, string Second, bool Notify);

    /// <summary>
    /// FUN_1413606c0 (@0x1413606c0, uiworldshareinfoeventlistener.cpp:133). The amount is grouped (FUN_1409404f0). By type:
    /// 0xea62 s_msg_take_assist_bonus_exp (0x2be) and the second line s_msg_take_assist_bonus_exp_system (0x2bf); 3001 and
    /// 3002 s_msg_take_map_exp (0x2c0) and 3000 s_msg_take_taxi_exp (0x2c1) with the field's name (string table 3 of
    /// GetFieldId), both with the notice; 3003 s_msg_take_telescope_exp (0x2c2) with the field's name; 0x427, 0x423, 0x42f,
    /// 0x443, 0x444 and 0x445 their s_msg_take_*_exp keys; 3004 and 3007 s_msg_take_exp (0x2bd) with the notice; any other
    /// s_msg_take_exp. Then s_msg_take_event_additional when the packet's flag is set, and s_msg_take_rest_exp when the
    /// rest exp before the packet was above 0.
    /// STILL-OPEN: the PC cafe suffix s_msg_take_pcbang (0x2c5) of types 0x15 and 0xea61 (FUN_140475e80 of the type, the
    /// PC's FUN_141658fa0 flags against the client constant at +0x5910) is not ported, as for the meso line.
    /// </summary>
    /// <param name="common">GetStringCommon(id).</param>
    /// <param name="commonByKey">GetStringByKey(key).</param>
    /// <param name="fieldName">CStringTable::FUN_141811980(3, GetFieldId()).</param>
    public static Lines Compose(long amount, int type, bool rested, bool additional, Func<int, string> common,
        Func<string, string> commonByKey, Func<string> fieldName)
    {
        string num = AchieveTexts.Group(amount);
        string a, b = "";
        bool notify = false;
        switch (type)
        {
            case 0xea62:
                a = ClientFormat.Format(common(0x2be), num);
                b = ClientFormat.Format(common(0x2bf), num);
                break;
            case 3001:
            case 3002:
                a = ClientFormat.Format(common(0x2c0), fieldName(), num);
                notify = true;
                break;
            case 3000:
                a = ClientFormat.Format(common(0x2c1), fieldName(), num);
                notify = true;
                break;
            case 3003:
                a = ClientFormat.Format(common(0x2c2), fieldName(), num);
                break;
            case 0x427: a = ClientFormat.Format(commonByKey("s_msg_take_play_instrument_exp"), num); break;
            case 0x423: a = ClientFormat.Format(commonByKey("s_msg_take_fishing_exp"), num); break;
            case 0x42f: a = ClientFormat.Format(commonByKey("s_msg_take_arcade_exp"), num); break;
            case 0x443: a = ClientFormat.Format(commonByKey("s_msg_take_normal_chest_exp"), num); break;
            case 0x444: a = ClientFormat.Format(commonByKey("s_msg_take_normal_rare_exp"), num); break;
            case 0x445: a = ClientFormat.Format(commonByKey("s_msg_take_normal_rare_first_exp"), num); break;
            case 3004:
            case 3007:
                a = ClientFormat.Format(common(0x2bd), num);
                notify = true;
                break;
            default:
                a = ClientFormat.Format(common(0x2bd), num);
                break;
        }
        if (additional) a += commonByKey("s_msg_take_event_additional");
        if (rested) a += commonByKey("s_msg_take_rest_exp");
        return new Lines(a, b, notify);
    }

    /// <summary>
    /// FUN_1413610f0 (@0x1413610f0, :206): s_msg_take_adventure_level_exp of the grouped amount, kind 8.
    /// STILL-OPEN: the s_msg_take_event_additional suffix while the game rule's GetAdventureLevelBonusExpRate is not 0; the
    /// AdventureLevelBonusExpRate game event is read past (GameEvent.cs) and not kept, so the suffix never shows.
    /// </summary>
    public static string Adventure(long amount, Func<string, string> commonByKey) =>
        ClientFormat.Format(commonByKey("s_msg_take_adventure_level_exp"), AchieveTexts.Group(amount));
}
