namespace Ms2.Ui.Items;

/// <summary>
/// Unk_Inventory::GetItemUseError / GetItemRequirementError (inventory.cpp:1578, :1590) and GetItemUsableState
/// (unit_141274d10_uihelpers.cpp:480): whether the local player (WorldService::GetLocalPlayer) can use an item, the code
/// the shop's "usable items only" filter and the item slots' lock / disable marks read.
/// STILL-OPEN: nothing feeds <see cref="InPcBang"/> (PC FUN_141658f30), <see cref="IsVip"/> (PC::IsVip) or
/// <see cref="WeddingInProgress"/> (WeddingManager::IsInProgress) yet, so they stay false; <see cref="EmotionLearned"/>
/// (SkillSystem::IsEmotionLearned) is null until a host has the learned emotions.
/// </summary>
public sealed class ItemUse
{
    /// <summary>The local player's gender, job group and level (PC::GetGender, GetJobGroupId, vfunc_78).</summary>
    public PlayerState Player;
    /// <summary>The player's server time (GetServerTime), which CItem::IsExpired compares with.</summary>
    public Func<long> ServerTime = () => 0;
    public bool InPcBang, IsVip, WeddingInProgress;
    /// <summary>SkillSystem::IsEmotionLearned(skill id, skill level); null counts none learned.</summary>
    public Func<int, int, bool> EmotionLearned;

    /// <summary>CItem::IsExpired (item.cpp:1303): an expiry time that has come.</summary>
    public bool IsExpired(Item item) { long now = ServerTime(); return item.ExpiryTime > 0 && item.ExpiryTime <= now; }

    /// <summary>
    /// GetItemUseError (:1578): 11 without the item, 5 an expired item without a maid id, then the requirement error,
    /// 10 when CItem::FUN_140528980 fails, else 0.
    /// </summary>
    public int Error(Item item)
    {
        if (item?.Data == null) return 11;
        if (IsExpired(item) && !item.Data.HasMaidId) return 5;
        int result = RequirementError(item.Data, item.Rarity);
        if (result != 0) return result;
        if (!Usable(item)) return 10;
        return 0;
    }

    /// <summary>
    /// GetItemRequirementError (:1590): 11 without a player, 6 a PC bang item outside one, 2 the gender, 3 the job, 1 the
    /// level, 7 a VIP item without VIP or a wedding item outside a wedding, 9 an emotion item already learned, else 0.
    /// </summary>
    public int RequirementError(ItemData data, int rarity)
    {
        if (Player == null || data == null) return 11;
        if (data.PCBang && !InPcBang) return 6;
        if (!data.IsGenderAllowed(Player.Gender)) return 2;
        if (!data.IsJobAllowed(Player.JobGroup)) return 3;
        if (!data.IsLevelAllowed(Player.Level)) return 1;
        if (data.Vip && !IsVip) return 7;
        if (data.Wedding && !WeddingInProgress) return 7;
        // FUN_14053ce40 (itemdata.cpp:725): type 0x18, an emotion item
        if (data.Type == 0x18 && EmotionLearned?.Invoke(data.SkillId, data.SkillLevel) == true) return 9;
        return 0;
    }

    /// <summary>
    /// CItem::FUN_140528980 (item.cpp:1717): an expired item that is not a pet needs a maid id.
    /// STILL-OPEN: the music score checks (a custom-note score item, FUN_140525680 / FUN_140525730, with no uses left
    /// (m_a0) or a score whose state m_n38 is not 0 or 1) need the item's music score, which is not ported; such an
    /// item counts as usable.
    /// </summary>
    public bool Usable(Item item) => !(IsExpired(item) && !item.Data.IsPetItem && !item.Data.HasMaidId);

    /// <summary>GetItemUsableState (uihelpers.cpp:480): 2 disable for codes 2, 3, 5, 6 and 10; 1 lock for 1 and 9; 3
    /// condition lock for 7; else 0 (0 without an item).</summary>
    public int UsableState(Item item)
    {
        if (item == null) return 0;
        return Error(item) switch { 2 or 3 or 5 or 6 or 10 => 2, 1 or 9 => 1, 7 => 3, _ => 0 };
    }
}
