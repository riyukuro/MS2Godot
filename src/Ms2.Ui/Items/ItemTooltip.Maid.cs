namespace Ms2.Ui.Items;

public sealed partial class ItemTooltip
{
    /// <summary>
    /// ASTooltip::SetMaidInfo (astooltip.cpp:822): an item with a maid id that has a description (CMaidDescTable) and a
    /// property row (CMaidInfoTable) replaces "statDescription" with a { type 0, text } row of common string 0x7a2 over the
    /// specialty, the salary with thousands separators (FUN_140763e10) and the salary icon (export_common_icon_meso for
    /// salary type 0, else export_common_icon_merat).
    /// STILL-OPEN: the owner's maid (CUserMaidManager::GetMaid by the item uid) gives the maid's name (FUN_1409dbbc0) and its
    /// running manufacture ("itemManufacture", 0x7a3 / 0x7a4); the port keeps no user maids.
    /// </summary>
    void SetMaidInfo(Item item)
    {
        int maidId = item.Data.MaidId;
        if (maidId <= 0) return;
        if (Tables.MaidSpecialty(maidId) is not { } specialty) return;
        if (Tables.MaidInfo(maidId) is not { } info) return;
        string icon = info.SalaryType == 0 ? "export_common_icon_meso" : "export_common_icon_merat";
        string text = Format(Tables.Common(0x7a2), specialty, NumberText.Group(info.Salary.ToString(System.Globalization.CultureInfo.InvariantCulture)), icon);
        Data["statDescription"] = new Dictionary<string, object> { ["type"] = 0, ["text"] = text };
    }
}
