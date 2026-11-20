using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using Ms2.Ui.Items;

namespace Ms2.Ui.Skills;

/// <summary>A &lt;weapon rh lh normalAttack actionprefix&gt; row of Table/weaponAction.xml (CWeaponAction).</summary>
public sealed record WeaponAction(int RightHand, int LeftHand, int NormalAttack, string Prefix);

/// <summary>
/// CWeaponActionTable (weaponactiontable.cpp): the weapon actions by (right, left) hand kind and the left hands allowed with a
/// right hand.
/// </summary>
public sealed class WeaponActionTable
{
    readonly List<WeaponAction> actions = new();
    readonly Dictionary<int, List<int>> allowedLeftHands = new();

    public static readonly WeaponActionTable Empty = new();

    public static WeaponActionTable Load(PackSet packs)
    {
        if (packs == null) return Empty;
        string path = packs.Exists("Xml", "table/weaponaction.xml") ? "table/weaponaction.xml"
            : packs.Exists("Xml", "table/weaponAction.xml") ? "table/weaponAction.xml" : null;
        return path != null ? Load(GameStrings.Parse(packs.Read("Xml", path))) : Empty;
    }

    /// <summary>
    /// CWeaponActionTable::Load @0x141b74720 (:73): each &lt;weapon&gt; in file order, then each &lt;allowWeapon rh lh&gt; with its
    /// comma list of left hands (a later row for the same right hand replaces the earlier one, CAtlMap::SetAt).
    /// </summary>
    public static WeaponActionTable Load(XDocument doc)
    {
        var t = new WeaponActionTable();
        var root = doc?.Root?.Name == "ms2" ? doc.Root : null;
        if (root == null) return t;
        foreach (var w in root.Elements("weapon"))
            t.actions.Add(new WeaponAction(Xml.Int(w, "rh"), Xml.Int(w, "lh"), Xml.Int(w, "normalAttack"), (string)w.Attribute("actionprefix") ?? ""));
        foreach (var a in root.Elements("allowWeapon"))
            t.allowedLeftHands[Xml.Int(a, "rh")] = Xml.Tokens((string)a.Attribute("lh")).Select(Xml.Atoi).ToList();
        return t;
    }

    /// <summary>FindAction @0x141b74b50 (:108): the first row with both hand kinds, null without one.</summary>
    public WeaponAction FindAction(int rightHand, int leftHand) =>
        actions.FirstOrDefault(a => a.RightHand == rightHand && a.LeftHand == leftHand);

    /// <summary>IsAllowed @0x141b74c30 (:121): a right hand with an &lt;allowWeapon&gt; row takes only its listed left hands.</summary>
    public bool IsAllowed(int rightHand, int leftHand) =>
        !allowedLeftHands.TryGetValue(rightHand, out var left) || left.Contains(leftHand);

    /// <summary>
    /// PC::UpdateWeaponAction @0x141653f00 (pc.cpp:2838), the action PC::GetWeaponAction returns:
    /// - each hand's kind is its item's equip subtype, 0 when the hand is empty or PC::CanUseItem refuses the item;
    /// - the left hand counts 0 when it holds the right hand's item, or when IsAllowed rejects the pair;
    /// - then FindAction of the two kinds (null when no row has them).
    /// The hands are the caller's: the client takes the layer 5 item first, else FUN_141655ee0(slot, 1) (the layer 0 item for
    /// a PC with no clone).
    /// </summary>
    public WeaponAction HeldAction(EquipItem rightHand, EquipItem leftHand, Func<int, ItemData> itemData, Func<EquipItem, bool> canUseItem)
    {
        int Kind(EquipItem item)
        {
            if (item == null) return 0;
            int kind = itemData(item.Id)?.EquipSubtype ?? 0;
            return canUseItem(item) ? kind : 0;
        }
        int right = Kind(rightHand), left = Kind(leftHand);
        if (rightHand != null && ReferenceEquals(rightHand, leftHand)) left = 0;
        if (!IsAllowed(right, left)) left = 0;
        return FindAction(right, left);
    }
}
