namespace Ms2.Ui.Items;

public sealed partial class ItemTooltip
{
    /// <summary>
    /// FUN_1409d7900 (astooltip.cpp:3200): for a local player, an item function's package as SetItemPackageList's rows.
    /// ItemChangeBeauty: the beauty shop's (first word) coupon items with data (FUN_1406e5af0), only those of the player's
    /// gender when the item itself allows that gender, each at its own rank (GetItemRank). OpenCoupleEffectBox (two words
    /// or more): the first word's item at the second word's rank, neither 0. ItemRemakeScroll: the scroll's (first word)
    /// item ids (CItemRemakeScrollTable) the player's job group may wear and is recommended, at the scroll's first rank (1
    /// for none).
    /// </summary>
    void SetItemFunctionPackage(int itemId)
    {
        var d = Tables.Items.Get(itemId);
        if (d == null || !model.HasLocalPlayer) return;
        var drops = new List<ItemDropEntry>();
        switch (d.FunctionName)
        {
            case "ItemChangeBeauty":
            {
                if (d.FunctionArgs.Count == 0) return;
                int shopId = d.FunctionArgInt(0, 0);
                if (shopId == 0) return;
                var coupons = Tables.BeautyCoupons.CouponsOf((uint)shopId);
                if (coupons.Length == 0) return;
                int gender = model.Player.Gender;
                bool genderOnly = d.IsGenderAllowed(gender);
                foreach (uint id in coupons)
                {
                    var c = Tables.Items.Get((int)id);
                    if (c == null) continue;
                    if (genderOnly && !c.IsGenderAllowed(gender)) continue;
                    drops.Add(new ItemDropEntry(c.Id, 0, c.Rank, 0, 0));
                }
                if (drops.Count == 0) return;
                break;
            }
            case "OpenCoupleEffectBox":
            {
                if (d.FunctionArgs.Count < 2) return;
                int id = d.FunctionArgInt(0, 0);
                if (id == 0 || Tables.Items.Get(id) == null) return;
                int rank = d.FunctionArgInt(1, 0);
                if (rank == 0) return;
                drops.Add(new ItemDropEntry(id, 0, rank, 0, 0));
                break;
            }
            case "ItemRemakeScroll":
            {
                if (d.FunctionArgs.Count == 0) return;
                if (Tables.RemakeScroll(d.FunctionArgInt(0, 0)) is not { Items.Length: > 0 } scroll) return;
                int rank = scroll.Ranks.Length == 0 ? 1 : (int)scroll.Ranks[0];
                int job = model.Player.JobGroup;
                foreach (uint id in scroll.Items)
                {
                    var c = Tables.Items.Get((int)id);
                    if (c == null || !c.IsJobAllowed(job) || !c.IsJobRecommended(job)) continue;
                    drops.Add(new ItemDropEntry((int)id, 0, rank, 0, 0));
                }
                break;
            }
            default: return;
        }
        SetItemPackageList(drops);
    }
}
