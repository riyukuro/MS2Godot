using System.Xml.Linq;
using System.Globalization;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Net.Packets;

namespace Ms2.Ui;

/// <summary>
/// CKeyTable's keyboard keys and their bound actions, as far as the HUD's key captions need them (ms2/client/ms2/keytable.cpp).
/// InitKeys (:622) makes a key for every id 1..0xcf that has a name in string table 5 (string/&lt;lang&gt;/keyname.xml;
/// CStringTable::FUN_1418125b0 sends a plain key id to FUN_141811980(5, id), stringtable.cpp:799, and table/string.xml row 5 is
/// keyName), plus the game pad combine keys. FUN_1414297e0 (:429) then reads table/default/DefaultKey.xml: each
/// &lt;key keyCode code type priority&gt; row fills the key's action code, type and priority, game pad keys skipped; and
/// RebuildActionKeyMaps (:2654) maps each action code to its keys. <see cref="Apply"/> takes the bindings the server keeps
/// for the player (the KeyTable packet).
/// </summary>
public sealed class KeyTable
{
    sealed class Key { public string Name; public long Code; public int Type, Priority, Slot; }

    readonly SortedDictionary<int, Key> keys = new();
    readonly Dictionary<string, string> names = new();
    byte[] defaultKeys;

    /// <summary>InitKeys (:679): keys that take each other's binding (Alt, Shift, Ctrl and 0x82/0x83 left and right).</summary>
    static readonly Dictionary<int, int> PairKeys = new()
    {
        [0x82] = 0x83, [0x83] = 0x82, [0x38] = 0x76, [0x76] = 0x38, [0x2a] = 0x36, [0x36] = 0x2a, [0x1d] = 0x6b, [0x6b] = 0x1d,
    };

    /// <summary>InitKeys (:653): the quick slot actions 500001..500025, slot i = action 0x7a121 + i.</summary>
    public const long FirstQuickSlotAction = 0x7a121;
    public const int QuickSlotActionCount = 25;

    public static readonly KeyTable Empty = new();

    KeyTable() { }

    public KeyTable(PackSet packs, params string[] languages)
    {
        names = new GameStrings(packs, languages).Table("keyname", "name");
        for (int k = 1; k < 0xd0; k++)
            if (names.TryGetValue(k.ToString(CultureInfo.InvariantCulture), out var n) && !string.IsNullOrEmpty(n))
                keys[k] = new Key { Name = n };
        const string path = "table/default/defaultkey.xml";
        if (packs.Exists("Xml", path)) defaultKeys = packs.Read("Xml", path);
        LoadDefaultKeys();
    }

    /// <summary>FUN_1414297e0 (:429) on DefaultKey.xml: each row fills its key's type, code and priority (the last row of a key
    /// code wins; game pad keys skipped; the slot is left as it is).</summary>
    public void LoadDefaultKeys()
    {
        if (defaultKeys == null) return;
        var rows = new Dictionary<int, XElement>();
        foreach (var e in GameStrings.Parse(defaultKeys).Descendants("KeyConfig").Descendants("key"))
            if (int.TryParse((string)e.Attribute("keyCode"), out var kc)) rows[kc] = e;     // byKey[nKeyCode] = pNode
        foreach (var (k, key) in keys)
        {
            if (IsGamepadKey(k) || !rows.TryGetValue(k, out var e)) continue;
            key.Type = Atoi((string)e.Attribute("type"));
            key.Code = long.TryParse((string)e.Attribute("code"), out var c) ? c : 0;
            key.Priority = Atoi((string)e.Attribute("priority"));
        }
    }

    /// <summary>ClearAllBindings (:599): code, slot and type to 0 (the priority stays).</summary>
    public void ClearAllBindings()
    {
        foreach (var key in keys.Values) { key.Code = 0; key.Slot = 0; key.Type = 0; }
    }

    /// <summary>FUN_141431c80 (bSave 0): binds <paramref name="key"/>; a quick slot action takes slot 0, an unchanged binding and a
    /// code with no name in string table 5 are refused, and the key's pair key (left/right) gets the same binding.</summary>
    public bool SetBinding(int key, int type, long code, int slot, int priority)
    {
        if (!keys.TryGetValue(key, out var k)) return false;
        if (code >= FirstQuickSlotAction && code < FirstQuickSlotAction + QuickSlotActionCount) slot = 0;     // IsQuickSlotAction
        if (k.Type == type && k.Code == code && k.Slot == slot && k.Priority == priority) return false;    // stKeyData::FUN_1414287c0
        if (code != 0 && string.IsNullOrEmpty(names.GetValueOrDefault(code.ToString(CultureInfo.InvariantCulture)))) return false;
        foreach (var t in PairKeys.TryGetValue(key, out var pair) && keys.TryGetValue(pair, out var pk) ? new[] { k, pk } : new[] { k })
        {
            t.Type = type; t.Code = code; t.Priority = priority; t.Slot = slot;
        }
        return true;
    }

    /// <summary>OnKeyTable mode 0 (keytable.cpp:285): ClearAllBindings, then DefaultKey.xml (use-default flag or no bindings) or
    /// each binding of the packet.</summary>
    public void Apply(ServerKeyTable t)
    {
        if (t == null || !t.Loaded) return;
        ClearAllBindings();
        if (t.UsesDefaultKeys) LoadDefaultKeys();
        else foreach (var b in t.Bindings) SetBinding(b.Key, b.Type, b.Code, b.Slot, b.Priority);
    }

    static int Atoi(string s) => int.TryParse(s, out var v) ? v : 0;

    /// <summary>IsGamepadKey (:2536).</summary>
    public static bool IsGamepadKey(long k) => (k > 0x91 && k < 0xb3) || (k > 0xf00000 && k < 0xf10811);

    /// <summary>FindKeyByActionAndPriority (:1160): a key bound to <paramref name="action"/> at that priority; 0 = none.</summary>
    public int FindKeyByActionAndPriority(long action, int priority)
    {
        if (action == 0) return 0;
        foreach (var (k, key) in keys)
            if (key.Code == action && key.Priority == priority) return k;
        return 0;
    }

    /// <summary>FindKeyByQuickSlot (:1202).</summary>
    public int FindKeyByQuickSlot(int index, int priority)
        => index < 0 || index >= QuickSlotActionCount ? 0 : FindKeyByActionAndPriority(FirstQuickSlotAction + index, priority);

    /// <summary>GetKeyName (:1339): the key's name; "" for no key. (Game pad keys build an icon string with feature 0xb5;
    /// they never carry a DefaultKey binding here.)</summary>
    public string GetKeyName(int key) => keys.TryGetValue(key, out var k) ? k.Name : "";
}
