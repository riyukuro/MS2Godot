namespace Ms2Godot.Ui;

/// <summary>
/// The guide's dialog reads over this HUD (<see cref="Ms2Godot.Net.IGuideHudState"/>): each condition's
/// CUIService::FindObject of the dialog's id, then the dialog's own getter. Set on the session with the guide's pane.
/// </summary>
public partial class HudScene : Ms2Godot.Net.IGuideHudState
{
    /// <summary>Unk_BreakDialog&lt;UIInventoryDialog&gt;(0x4e2d) and vfunc_55 == 0 (unit_1413a76f0.cpp:137-139): the open
    /// inventory's tab (m_194, the view's tab).</summary>
    public int? InventoryTab =>
        inventory != null && inventoryMovie != null && !inventory.IsClosed ? inventory.View.Tab : null;

    /// <summary>GetUIDialog&lt;UIMasteryDialog&gt;(0x4f1e): the controller, built with the HUD; its getter answers -1 without the movie.</summary>
    public int? MasteryMainTab => mastery?.MainTabIndex();
    public int? MasterySubTab => mastery?.SubTabIndex();

    /// <summary>FindGameMenuDialog(0x4e28) (unit_1413a9df0_2.cpp:521): the HUD's game menu, an always-on dialog.</summary>
    public bool? MenuExpanded(string name) => gameMenu?.IsMenuExpanded(name);

    /// <summary>CItemData's type and HasCubeTextureMesh, as the inventory's arrows read them (<see cref="GuideInventoryOf"/>).</summary>
    public int ItemType(int itemId) => hudModel?.Tables.Items.Get(itemId)?.Type ?? 0;
    public bool ItemHasCubeTextureMesh(int itemId) => hudModel?.Tables.Items.Get(itemId)?.HasCubeTextureMesh ?? false;
}
