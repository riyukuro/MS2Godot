namespace Ms2.Ui.Items;

public sealed partial class ItemTooltip
{
    /// <summary>
    /// FUN_1409d8570 (astooltip.cpp:3315): an item the break manager can break (CanBreakItem, <see cref="UiModel.CanBreakItem"/>)
    /// lists what breaking it gives (FUN_1409d8650 with the item's gacha id when set, else its id, its grade and its open
    /// socket count): a gacha box's coin (ItemGachaBoxTable::GetBox, CItemBreakIngredientTable::GetGachaBox), else the break
    /// ingredients (GetIngredients).
    /// </summary>
    void SetItemBreakIngredients(Item item)
    {
        if (model.CanBreakItem?.Invoke(item) != true) return;
        int itemId = item.ItemId;
        if (item.Record is { GachaDismantleId: not 0 } r) itemId = r.GachaDismantleId;      // CItem::FUN_140529640, m_dc
        int sockets = item.Record?.Socket?.Gems.Count ?? 0;                                    // GetOpenSlotCount
        var ingredients = Tables.GachaBoxes.HasBox(itemId)
            ? Tables.BreakIngredients.GachaBox(itemId)
            : Tables.BreakIngredients.Ingredients(itemId, item.Rarity, sockets);
        if (ingredients != null) SetItemBreakIngredientList(ingredients);
    }

    /// <summary>FUN_1409e7460 (astooltip.cpp:5647): with any ingredient, "itemBreakIngredientList" is a package row
    /// (ASObj::FUN_1409c5bd0 of an ItemDropResult with the count and the item's rank) per ingredient with a count whose item
    /// has data and is a piece (type 0x1b), a coin (IsCoinItem, type 0x14) or of type 0xf.</summary>
    void SetItemBreakIngredientList(IReadOnlyList<(int ItemId, int Count)> ingredients)
    {
        if (ingredients.Count == 0) return;
        var rows = new List<object>();
        foreach (var (id, count) in ingredients)
        {
            if (count == 0) continue;
            var d = Tables.Items.Get(id);
            if (d == null) continue;
            if (!d.IsPieceItem && d.Type != 0x14 && d.Type != 0xf) continue;
            if (PackageRow(new ItemDropEntry(id, count, d.Rank, 0, 0)) is { } row) rows.Add(row);
        }
        Data["itemBreakIngredientList"] = rows;
    }
}
