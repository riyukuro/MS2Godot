namespace Ms2.Ui.Items;

public sealed partial class ItemTooltip
{
    /// <summary>
    /// SetPetEvolution (astooltip.cpp:2721): with feature 0xeb, a pet item at the pet max level (Client::Constants PetMaxLevel)
    /// whose item level and grade need evolution points (CExpTable::GetPetEvolution) shows "petEvolution" (the
    /// s_pet_tooltip_evolution_point text and "gauge:\n\n"), "petEvolutionGauge" ("{0} / {1}" of the points and the need) and
    /// "petEvolutionPercent" (points * 100 / need, unsigned).
    /// </summary>
    void SetPetEvolution(Item item)
    {
        if (!model.IsFeatureEnabled(0xeb)) return;
        var d = item.Data;
        if (!d.IsPetItem) return;
        uint need = Tables.PetEvolution(d.LevelLimit, item.Rarity);
        if (need == 0) return;
        var r = item.Record;
        if ((uint)(ushort)(r?.PetLevel ?? 0) < Tables.PetMaxLevel) return;
        uint point = (uint)(r?.PetEvolutionPoint ?? 0);
        Data["petEvolution"] = CommonF("s_pet_tooltip_evolution_point") + "gauge:\n\n";
        Data["petEvolutionGauge"] = Ms2.Quest.ClientFormat.Format("{0} / {1}", point, need);
        Data["petEvolutionPercent"] = point * 100 / need;
    }
}
