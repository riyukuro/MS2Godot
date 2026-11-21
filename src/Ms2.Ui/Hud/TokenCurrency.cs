using System.Globalization;

namespace Ms2.Ui.Hud;

/// <summary>The token currencies' shared client helpers: the icon (FUN_1418fe870), the amount text (FUN_141900120) and the
/// currency's item id (GetCurrencyItemId, commonfiles/gameentitycommon.cpp:315, @0x1403bb010).</summary>
public static class TokenCurrency
{
    /// <summary>FUN_1418fe870 (uicommon.cpp): the export_common_icon_* name and its vspace (-4 unless the table says more).</summary>
    public static (string Icon, int VSpace) Icon(int currency) => currency switch
    {
        4 => ("export_common_icon_honor", -6), 5 => ("export_common_icon_treva", -6), 6 => ("export_common_icon_lu", -6),
        7 => ("export_common_icon_habi", -6), 0xc => ("export_common_icon_reverse_coin", -7),
        0xd => ("export_common_icon_mentor_token", -4), 0xe => ("export_common_icon_mentee_token", -4),
        0xf => ("export_common_icon_star_point", -6), _ => ("export_common_icon_meso_market_token", -4),
    };

    /// <summary>FUN_141900120(currency, icon false, newline false): the grouped amount, " / cap" after it when the cap is
    /// not 0; "" for a currency without a cap row.</summary>
    public static string AmountText(UiModel model, int currency)
    {
        if (!model.Tables.TokenCaps.TryGetValue(currency, out long cap)) return "";
        model.Player.Tokens.TryGetValue(currency, out long amount);
        var text = Group(amount);
        return cap == 0 ? text : text + " / " + Group(cap);
    }

    static string Group(long n) => n.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>GetCurrencyItemId(type, color): the item the currency stands for; 0 for a type without one.</summary>
    public static int ItemId(int type, int color) => type switch
    {
        0 => 90000001,
        2 => color switch { 0 => 90000011, 3 => 90000016, 2 => 90000015, _ => 90000004 },
        3 => 90000006, 4 => 90000017, 5 => 90000013, 6 => 90000014, 7 => 90000018, 8 => 90000020,
        9 => 90000022, 10 => 90000023, 11 => 90000024, 12 => 90000025, 13 => 90000027,
        _ => 0,
    };
}
