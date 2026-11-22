namespace Ms2.Ui.Items;

public sealed partial class ItemTooltip
{
    /// <summary>
    /// FUN_1409d46a0 -> FUN_1409d77b0 (astooltip.cpp:382, :3179): "itemSeason", the season number of an OpenGachaBox
    /// item's box (GetOpenGachaBoxParam0) or of the box a coin item buys (GetRandomBoxIdByCoinItem), when that box shows
    /// its season on the icon (IsSeasonNumberOnIcon); FUN_1409e5320 makes the text.
    /// </summary>
    void SetItemSeason(int itemId)
    {
        var d = Tables.Items.Get(itemId);
        if (d == null) return;
        var boxes = Tables.GachaBoxes;
        int season = d.OpenGachaBoxParam0;
        if (boxes.IsCoinItem(itemId)) season = boxes.RandomBoxIdByCoinItem(itemId);
        if (season == 0 || !boxes.IsSeasonNumberOnIcon(season)) return;
        Data["itemSeason"] = boxes.SeasonText(season);
    }
}
