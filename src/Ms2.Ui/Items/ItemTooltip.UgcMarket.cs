namespace Ms2.Ui.Items;

public sealed partial class ItemTooltip
{
    /// <summary>
    /// FUN_1409d88e0 (astooltip.cpp:3359): with a local player (FUN_1409c6540), an item that can go on the UGC market
    /// (FUN_1406a6030, meratmarketbasketinfo.cpp:2659) but cannot be registered shows "ugcItemLimitRegister", common string
    /// 0x9b9. A designed item (HasCubeTextureMesh, or type 0x16) or a custom-note score item (FUN_140525680) is registrable by
    /// its data's limit flag (IsMeratMarketRegisterable); any other is not.
    /// STILL-OPEN: a designed item with a UGC id and a written custom score (FUN_140525790) go through FUN_1406a5a50's owner,
    /// blueprint and play-count checks against the local account; those leave the line out.
    /// </summary>
    void SetUgcItemLimitRegister(Item item)
    {
        var d = item.Data;
        if (!model.HasLocalPlayer) return;
        if (!UgcMarketItem(d)) return;
        bool registrable = false;
        if (d.HasCubeTextureMesh || d.IsUgcType)                                  // CItem::FUN_140526010
        {
            if (item.Record is { HasUgc: true, Ugc.Uid: > 0 }) return;            // HasUgcId: FUN_1406a5a50, STILL-OPEN
            registrable = d.EnableRegisterMeratMarket;
        }
        else if (item.Record?.HasMusic == true)                                   // FUN_140525680: a custom note
        {
            if (!string.IsNullOrEmpty(item.Record.MusicWriter)) return;           // FUN_140525790: FUN_1406a5a50, STILL-OPEN
            registrable = d.EnableRegisterMeratMarket;
        }
        if (!registrable) Data["ugcItemLimitRegister"] = Tables.Common(0x9b9);
    }

    /// <summary>FUN_1406a6030: a designed item; a type 0x16 item with feature 0xec; a score item with feature 0xa9.</summary>
    bool UgcMarketItem(ItemData d)
    {
        if (d.HasCubeTextureMesh) return true;
        if (d.IsUgcType) return model.IsFeatureEnabled(0xec);
        if (d.IsScoreItem) return model.IsFeatureEnabled(0xa9);
        return false;
    }
}
