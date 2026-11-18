using System.Xml.Linq;
using System.Globalization;
using Ms2.Formats.Pack;
using Ms2.Formats.World;

namespace Ms2.Ui;

/// <summary>
/// CKeyTable's keyboard keys and their bound actions, as far as the HUD's key captions need them (ms2/client/ms2/keytable.cpp).
/// InitKeys (:622) makes a key for every id 1..0xcf that has a name in string table 5 (string/&lt;lang&gt;/keyname.xml;
/// CStringTable::FUN_1418125b0 sends a plain key id to FUN_141811980(5, id), stringtable.cpp:799, and table/string.xml row 5 is
/// keyName), plus the game pad combine keys. FUN_1414297e0 (:429) then reads table/default/DefaultKey.xml: each
/// &lt;key keyCode code type priority&gt; row fills the key's action code, type and priority, game pad keys skipped; and
/// RebuildActionKeyMaps (:2654) maps each action code to its keys. The player's saved key config is not modelled.
/// </summary>
public sealed class KeyTable
{
    sealed class Key { public string Name; public long Code; public int Type, Priority; }

    readonly SortedDictionary<int, Key> keys = new();

    /// <summary>InitKeys (:653): the quick slot actions 500001..500025, slot i = action 0x7a121 + i.</summary>
    public const long FirstQuickSlotAction = 0x7a121;
    public const int QuickSlotActionCount = 25;

    public static readonly KeyTable Empty = new();

    KeyTable() { }

    public KeyTable(PackSet packs, params string[] languages)
    {
        var names = new GameStrings(packs, languages).Table("keyname", "name");
        for (int k = 1; k < 0xd0; k++)
            if (names.TryGetValue(k.ToString(CultureInfo.InvariantCulture), out var n) && !string.IsNullOrEmpty(n))
                keys[k] = new Key { Name = n };
        const string path = "table/default/defaultkey.xml";
        if (!packs.Exists("Xml", path)) return;
        var rows = new Dictionary<int, XElement>();
        foreach (var e in GameStrings.Parse(packs.Read("Xml", path)).Descendants("KeyConfig").Descendants("key"))
            if (int.TryParse((string)e.Attribute("keyCode"), out var kc)) rows[kc] = e;     // byKey[nKeyCode] = pNode: the last row wins
        foreach (var (k, key) in keys)
        {
            if (IsGamepadKey(k) || !rows.TryGetValue(k, out var e)) continue;
            key.Type = Atoi((string)e.Attribute("type"));
            key.Code = long.TryParse((string)e.Attribute("code"), out var c) ? c : 0;
            key.Priority = Atoi((string)e.Attribute("priority"));
        }
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
