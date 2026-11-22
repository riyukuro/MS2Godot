using Ms2.Avm2;
using Ms2.Net.Packets;
using Ms2.Ui.Items;

namespace Ms2.Ui.Hud;

/// <summary>
/// UILapenshardUpgradeTab's state machine (client/ms2/guess/uilapenshardsystemdialog.cpp; tab id "upgrade", added by
/// UILapenshardSystemDialog::vfunc_65 @0x140c612f0). m_nState: 0 start, 1 a source is selected, 2 the upgrade is requested,
/// 3 the result is in, 4 the processing animation is complete. The page keeps the source (m_spSource), the chosen ingredients
/// (m_ingredients, uid to count, and their total m_nTotal), the success rate of the last preview (m_n_bc) and the sale rate
/// (m_n_c0). The three panels (source bag, upgrade slot, ingredient bag) are fed by <see cref="PanelEvent"/>, which is the
/// tab's vfunc_2 forwarded to every panel.
/// <para>Sends (outbound 0xAD): <see cref="SendMode3"/> @0x140c68bd0 (preview request), <see cref="SendMode4"/> @0x140c68aa0
/// (the ingredient total), <see cref="SendMode5"/> @0x140c69030 (the upgrade, behind <see cref="CanRequestUpgrade"/> @0x140c691f0).
/// Inbound 0x11B kinds 4, 5, 6 (vfunc_9 @0x140c68870): 5 falls through into 6, as the client's switch has no break.</para>
/// <para>STILL-OPEN: OnFillIngredientDebug (debug key mode only: mode 6 with the item id and the sale rate); the sale rate
/// feed (CGameRule::GetLapenshardUpgradeSaleRate, set by event sale packets) is the host's <see cref="SaleRate"/>; the panels and
/// their movie bindings (the next slice); the inventory counts the ingredient check reads come from the host.</para>
/// </summary>
public sealed partial class LapenshardUpgradeTabPage : LapenshardTabPage
{
    public override string Name => "upgrade";
    public override string LabelKey => "s_lapenshard_tabname_upgrade";

    public const int StateStart = 0, StateSelect = 1, StateRequested = 2, StateResult = 3, StateDone = 4;

    UiHost host;
    object root;
    readonly SortedDictionary<long, int> ingredients = new();

    /// <summary>m_nState (GetState @0x140c68e20).</summary>
    public int State { get; private set; }
    /// <summary>m_spSource (GetSource @0x140c68da0).</summary>
    public Item Source { get; private set; }
    /// <summary>m_nTotal (GetIngredientTotal @0x140c68fd0).</summary>
    public int IngredientTotal { get; private set; }
    /// <summary>m_n_bc: the success rate of the last preview, in 1/100 percent (GetN_bc @0x140c68e40).</summary>
    public int SuccessRate { get; private set; }
    /// <summary>m_n_c0: the sale rate the last UpdateSaleRate read (GetSaleRate @0x140c69650).</summary>
    public int SaleRateRead { get; private set; }

    /// <summary>CGameRule::GetLapenshardUpgradeSaleRate (gamerule.cpp:2873): the host's current rate, 0 for none.</summary>
    public Func<int> SaleRate = () => 0;
    /// <summary>The inventory count of an item id at a grade (Unk_Inventory::FUN_140a5f180).</summary>
    public Func<int, int, int> CountItems = (_, _) => 0;
    /// <summary>The inventory count of the items with a tag (Unk_Inventory::GetItemCountByTag).</summary>
    public Func<int, int> CountByTag = _ => 0;
    /// <summary>Unk_Inventory::GetItemCountByTag (inventory.cpp:835) over the inventory's records: items with the tag that the player
    /// can use (<paramref name="itemUseError"/> 0, :845). STILL-OPEN: the untagged count (FUN_140a5f180, inventory.cpp:782) takes
    /// rarity 0 as any rarity and skips unusable items, with GetItemRank for the rank; ours matches the rarity exactly (the tab passes the
    /// data rank, the panels the tooltip rank, 1 for rank 0). Latent: every source and ingredient row in KMS2_DEV is rank 3 or tagged.</summary>
    public static Func<int, int> TagCounter(Func<IEnumerable<Ms2.Net.Packets.EquipItem>> items, ItemDataTable table, Func<Item, int> itemUseError) =>
        tag => (int)Ms2.Ui.Shop.ShopController.CountItemsByTag(items(), table, tag, itemUseError);

    /// <summary>DAT_143ab2898: the meso the player holds.</summary>
    public Func<long> Mesos = () => 0;
    /// <summary>FUN_141275260(id, 1, 1): a system notice by string id (0x34 is the lack of meso).</summary>
    public event Action<int> SystemNoticeId;
    /// <summary>The tab's vfunc_2(event) as every panel gets it.</summary>
    public event Action<int> PanelEvent;

    /// <summary>vfunc_3: the page's movie root exists (m_ui). The panels build over it.</summary>
    public override void Show(UiHost h, object r) { host = h; root = r; BuildSourceBag(); BuildIngredientBag(); BuildSlotPanel(); }
    public override void Leave() { }
    public override void Destroy()
    {
        root = null; host = null; Source = null; ingredients.Clear();
        sourceItems.Clear(); ingredientItems.Clear(); slotItems.Clear(); slotKinds.Clear(); infoSlots.Clear(); pushed.Clear();
        panelSource = null; body = null; ingredientBagName = "";
    }

    /// <summary>The chosen ingredients, uid to count (the client's map).</summary>
    public IReadOnlyDictionary<long, int> Ingredients => ingredients;

    /// <summary>GetIngredientCount (@0x140c68ff0).</summary>
    public int GetIngredientCount(long uid) => ingredients.TryGetValue(uid, out var n) ? n : 0;

    /// <summary>vfunc_2 (@0x140c68260): ignored without the movie root; event 2 in state 0 or 1 resets the state and clears the
    /// ingredients; then every panel gets the event.</summary>
    public override void Notify(int ev)
    {
        if (root == null) return;
        if (ev == 2 && State is 0 or 1)
        {
            ResetState();
            ClearIngredients();
        }
        PanelEvent?.Invoke(ev);
    }

    /// <summary>ResetState (@0x140c68a50).</summary>
    public void ResetState() { State = 0; Source = null; SuccessRate = 0; }

    /// <summary>ClearSource (@0x140c68df0).</summary>
    public void ClearSource() => Source = null;

    /// <summary>ClearIngredients (@0x140c68f90).</summary>
    public void ClearIngredients() { ingredients.Clear(); IngredientTotal = 0; }

    /// <summary>UpdateSaleRate (@0x140c69620): m_n_c0 from the game rule.</summary>
    public void UpdateSaleRate() => SaleRateRead = SaleRate();

    /// <summary>The slot MyPC::FindLapenshardSlot (@0x14152f7c0) gives an item id: a worn slot holding it, 0 for none. The client
    /// walks its slot map in hash order; the lowest slot is taken here.</summary>
    int FindLapenshardSlot(int itemId)
    {
        for (int s = 1; s <= LapenshardEquipTabPage.Slots; s++) if (Owner.WornItemId(s) == itemId) return s;
        return 0;
    }

    bool HasPlayer => Owner?.Model is { HasLocalPlayer: true };

    /// <summary>SendLapenshardMode3 (@0x140c68bd0): the source is taken and the ingredients cleared before the slot is looked up
    /// (an item held by no slot and without a uid still leaves state 1, and sends nothing).</summary>
    public bool SendMode3(Item item)
    {
        if (root == null || item == null || !Owner.CanUse() || !HasPlayer) return false;
        Source = item;
        ClearIngredients();
        State = StateSelect;
        int slot = 0;
        if (item.Uid == 0)
        {
            slot = FindLapenshardSlot(item.ItemId);
            if (slot == 0) return false;
        }
        bool sent = Owner.Send?.Invoke(new LapenshardPreviewRequest(item.Uid, item.ItemId, slot)) == true;
        Notify(3);
        return sent;
    }

    /// <summary>SendLapenshardMode4 (@0x140c68aa0): the ingredient total of the source.</summary>
    public bool SendMode4()
    {
        if (!HasPlayer || Source == null) return false;
        int slot = 0;
        if (Source.Uid == 0)
        {
            slot = FindLapenshardSlot(Source.ItemId);
            if (slot == 0) return false;
        }
        return Owner.Send?.Invoke(new LapenshardAddFodder(Source.Uid, Source.ItemId, slot, IngredientTotal)) == true;
    }

    /// <summary>ChangeIngredientCount (@0x140c68e60): the entry and the total move by the delta (the client's clamp at zero is an
    /// unsigned compare and never fires), the entry goes at 0, event 4, then mode 4.</summary>
    public void ChangeIngredientCount(long uid, int delta)
    {
        if (!HasPlayer) return;
        int n = GetIngredientCount(uid) + delta;
        if (n == 0) ingredients.Remove(uid); else ingredients[uid] = n;
        IngredientTotal += delta;
        Notify(4);
        SendMode4();
    }

    /// <summary>CanRequestUpgrade (@0x140c691f0).</summary>
    public bool CanRequestUpgrade()
    {
        if (State != StateSelect || Source == null) return false;
        var table = Owner.Model.Tables.Lapenshards;
        var row = table.Get(Source.ItemId);
        if (row == null) return false;
        if (row.Meso > 0 && Mesos() < row.Meso)
        {
            SystemNoticeId?.Invoke(0x34);
            return false;
        }
        if ((uint)row.MinCount > (uint)IngredientTotal)
        {
            Owner.ShowSystemNotice("s_lapenshard_tabname_upgrade_lack_ingredient");
            return false;
        }
        foreach (var ing in LapenshardTable.Scaled(row, SaleRateRead))
        {
            if (ing == null || ing.ItemId == 0) continue;                  // IsEmpty
            int have = ing.Tag != 0 ? CountByTag(ing.Tag) : CountItems(ing.ItemId, Owner.Model.Tables.Items.Get(ing.ItemId)?.Rank ?? 0);
            if (ing.Count > have)
            {
                Owner.ShowSystemNotice("s_lapenshard_tabname_upgrade_lack_ingredient");
                return false;
            }
        }
        return true;
    }

    /// <summary>SendLapenshardMode5 (@0x140c69030): the upgrade request, then state 2 and the movie's RequestUpgrade.</summary>
    public bool SendMode5()
    {
        if (!CanRequestUpgrade() || !HasPlayer) return false;
        int slot = 0;
        if (Source.Uid == 0)
        {
            slot = FindLapenshardSlot(Source.ItemId);
            if (slot == 0) return false;
        }
        var fodder = ingredients.Select(p => (p.Key, p.Value)).ToArray();
        // Unk_LapenUpgrade::Write: the head (uid, item, slot), the fodder, then the sale rate m_n_c0
        if (Owner.Send?.Invoke(new LapenshardUpgrade(Source.Uid, Source.ItemId, slot, fodder, SaleRateRead)) != true) return false;
        State = StateRequested;
        if (host != null && root != null) host.Invoke(root, "RequestUpgrade");
        return true;
    }

    /// <summary>vfunc_6 (@0x140c68500), the page's own callbacks: OnAction (1 the upgrade, 3 the reset) and OnProcessingComplete.</summary>
    public override bool OnCallback(string name, object[] args)
    {
        if (root != null) PanelCallback(name, args);                 // the panels first, their results ignored
        if (root == null || !HasPlayer) return false;
        if (name.Equals("OnAction", StringComparison.OrdinalIgnoreCase))
        {
            if (!Owner.CanUse()) return true;
            if (args is { Length: > 0 })
            {
                int action = (int)Rt.ToUint32(args[0]);
                if (action == 1) SendMode5();
                else if (action == 3)
                {
                    ResetState();
                    Notify(8);
                }
            }
            return true;
        }
        if (name.Equals("OnProcessingComplete", StringComparison.OrdinalIgnoreCase))
        {
            State = StateDone;
            Notify(0xc);
        }
        return false;
    }

    /// <summary>vfunc_9 (@0x140c68870): kind 4 the preview rate; kind 5 the result (only while requested, and for the source's
    /// id), which falls through into kind 6; kind 6 event 9.</summary>
    public override void OnPacket(LapenshardMessage m)
    {
        switch (m)
        {
            case LapenshardMessage.Preview p:
                SuccessRate = p.Rate;
                Notify(5);
                break;
            case LapenshardMessage.UpgradeResult r:
                if (State != StateRequested) return;
                if (Source == null || Source.ItemId != r.ItemId) return;
                Notify(r.Success ? 6 : 7);
                State = StateResult;
                Notify(9);                                                   // the switch has no break after kind 5
                break;
            case LapenshardMessage.UpgradeDone:
                Notify(9);
                break;
        }
    }
}
