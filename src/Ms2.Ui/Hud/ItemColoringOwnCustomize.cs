using Ms2.Net.Packets;
using Ms2.Ui.Items;

namespace Ms2.Ui.Hud;

/// <summary>
/// The item colour dialog's Customize over the own avatar (<see cref="BeautyCustomize"/>): the worn item being dyed and the
/// colour edit of Customize::ApplyPalette (@0x140cd3220), Customize::SetColors (@0x140cd32f0) and Customize::GetModifiedAndSelect
/// (@0x140cd2360), which go to EditItems::ApplyPalette (customize.cpp:441) and EditItems::SetColors (customize.cpp:460, @0x140ccbc00).
/// <para>STILL-OPEN: the avatar colour item (skin and hair colour, FUN_141423760 / FUN_141423650) does nothing, so does the
/// multi-slot colour of an item with m_bMulti (only slot 0 is kept), the owner palette override of Customize::GetPalette, the colour's
/// delay (m_n2c) and the hair-apply effect, and the redraw of the avatar copy beyond the worn lists changing.</para>
/// </summary>
public sealed class ItemColoringOwnCustomize : IItemColoringCustomize
{
    readonly BeautyCustomize cust;
    /// <summary>The palette of the hair colours: FUN_140f02bc0 (uibeautyshopstyledialog.cpp:768).</summary>
    public const int HairPalette = 100000000;

    readonly ColorPaletteTable palettes;
    readonly Func<int, bool> isEyeItem;

    /// <param name="isEyeItem">CItemData::FUN_14053d020 of an item id: the model's first group type is 2.</param>
    public ItemColoringOwnCustomize(BeautyCustomize cust, ColorPaletteTable palettes, Func<int, bool> isEyeItem)
    {
        this.cust = cust; this.palettes = palettes; this.isEyeItem = isEyeItem;
    }

    public bool HasEditItem => cust.EditItemUid != 0;
    public long EditItemUid => cust.EditItemUid;
    public int EditItemId => cust.EditItemId;
    public bool HasChanges => cust.HasChanges;
    public bool IsEditItemColorChanged => cust.IsEditItemColorChanged;
    public void SelectUid(long uid) => cust.SelectUid(uid);
    public void RestoreUid(long uid) => cust.RestoreUid(uid);
    public void ResetEdit() => cust.ResetEdit();
    public void RestoreAll() => cust.RestoreAll();

    static Rgba Rgb(int v) => new((byte)v, (byte)(v >> 8), (byte)(v >> 16), (byte)(v >> 24));

    // The edit item as worn: EditItems::m_spData is set and it is not an avatar colour item (FUN_141423760 is STILL-OPEN).
    bool CanEdit => cust.EditItemUid != 0 && !cust.IsAvatarColorId(cust.EditItemId);

    /// <summary>
    /// EditItems::ApplyPalette (customize.cpp:441): needs the edit item and a palette; the row of the palette and colour serial
    /// number (GetPaletteColorBySN) is put on the worn item as FUN_141423cf0 does: its three values, the serial number and the
    /// palette id, with the eye values for slots 0 and 2 when the palette is the hair palette and the item is an eye item.
    /// </summary>
    public void ApplyPalette(uint palette, uint color)
    {
        if (!CanEdit || palette == 0) return;
        var row = palettes.BySN((int)palette, (int)color);
        if (row == null) return;
        bool eye = row.PaletteId == HairPalette && isEyeItem(cust.EditItemId);
        cust.DyeWorn(cust.EditItemUid, _ => new EquipColor(Rgb((int)(eye ? row.Eye0 : row.Ch0)), Rgb((int)row.Ch1),
            Rgb((int)(eye ? row.Eye2 : row.Ch2)), row.ColorSN, row.PaletteId));
    }

    /// <summary>
    /// EditItems::SetColors (customize.cpp:460) as Customize::SetColors(color, slot): CItemExtraData::SetSlotRgbComponent sets one
    /// of the three values (slot 0 to 2; another does nothing) and clears the serial number (-1) and the palette id (0).
    /// </summary>
    public void SetColors(int palette, int color)
    {
        if (!CanEdit || color is < 0 or > 2) return;
        int value = palette;
        cust.DyeWorn(cust.EditItemUid, c =>
        {
            var v = Rgb(value);
            return new EquipColor(color == 0 ? v : c.Primary, color == 1 ? v : c.Secondary, color == 2 ? v : c.Tertiary, -1, 0);
        });
    }

    /// <summary>
    /// Customize::GetModifiedAndSelect (@0x140cd2360): Equipment::GetModified (customize.cpp:1253) resets the edit and gives the
    /// worn item when it differs from its copy, else null; the hidden slots go back and the uid is selected. The item carries the
    /// slot 0 colour as the extra data.
    /// </summary>
    public BeautyColorItem GetModifiedAndSelect(long uid)
    {
        var modified = cust.GetModifiedByUid(uid);
        cust.ResetEdit();
        cust.ReapplyHidden();
        cust.SelectUid(uid);
        if (modified == null) return null;
        var color = modified.Color;
        return new BeautyColorItem(false, false, modified.Uid, modified.Id, w => w.WriteEquipColor(color));
    }

    public bool TryGetEditSlotColor(out uint palette, out uint color)
    {
        palette = color = 0;
        var worn = cust.WornEditItem();
        if (worn == null) return false;
        palette = (uint)worn.Color.PaletteId; color = (uint)worn.Color.Index;
        return true;
    }
}
