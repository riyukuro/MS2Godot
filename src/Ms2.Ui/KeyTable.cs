using System.Xml.Linq;
using System.Globalization;
using Ms2.Formats.Options;
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
public sealed class KeyTable : IKeyTable
{
    sealed class Key { public string Name; public long Code; public int Type, Priority, Slot; }

    // keys in key order for the walks; byId for the lookups: on .NET 8, SortedDictionary<int, T>.TryGetValue allocates 24 B a
    // call where Dictionary allocates none, and the HUD looks keys up per frame (IsKeyOfSkill, GetKeySkillId).
    readonly SortedDictionary<int, Key> keys = new();
    readonly Dictionary<int, Key> byId = new();
    readonly Dictionary<int, (int Type, long Code, int Slot, int Priority)> backup = new();
    readonly Dictionary<string, string> names = new();
    byte[] defaultKeys;
    byte[] defaultMouseKeys;
    byte[] defaultPadKeysPs, defaultPadKeysXbox;
    readonly Dictionary<int, List<KeySetting>> settings = new();
    readonly GameStrings strings;
    Dictionary<string, string> skillNames;

    public const string DefaultKeyPath = "table/default/defaultkey.xml";
    public const string DefaultMouseControlKeyPath = "table/default/defaultmousecontrolkey.xml";
    public const string DefaultPsKeyPath = "table/default/defaultpskey.xml";
    public const string DefaultXboxKeyPath = "table/default/defaultxboxkey.xml";

    /// <summary>CKeyTable::GetLocked (m_nLocked): when true, key sends and UIKeySettingSelectDialog buttons are disabled (keytable.cpp:380,405, uikeysettingselectdialog.cpp:87).</summary>
    public bool Locked { get; set; }
    /// <summary>m_bDirty: set when key bindings change; cleared by <see cref="SendKeysToServer"/> (keytable.cpp:403,424,492).</summary>
    public bool IsDirty { get; private set; }
    /// <summary>CKeyTable::SendKeyTableMode1 (@0x1414294e0, keytable.cpp:378): raised per changed key (and its pair key) in <see cref="SetKeyBindingAndSync"/>.</summary>
    public event Action<KeyBinding> KeyBindingChanged;
    /// <summary>CKeyTable::SendKeysToServer (@0x141429600, keytable.cpp:399): raised with the full m_keys table on mode 2 save.</summary>
    public event Action<IReadOnlyList<KeyBinding>> KeysSentToServer;

    /// <summary>InitKeys (:679): keys that take each other's binding (Alt, Shift, Ctrl and 0x82/0x83 left and right).</summary>
    static readonly Dictionary<int, int> PairKeys = new()
    {
        [0x82] = 0x83, [0x83] = 0x82, [0x38] = 0x76, [0x76] = 0x38, [0x2a] = 0x36, [0x36] = 0x2a, [0x1d] = 0x6b, [0x6b] = 0x1d,
    };

    /// <summary>CKeyTable::GetPairKey @0x14142c830 (keytable.cpp:1143-1146): the other key of a pair (<see cref="PairKeys"/>).</summary>
    public static bool GetPairKey(int key, out int pair) => PairKeys.TryGetValue(key, out pair);

    /// <summary>InitKeys (:653): the quick slot actions 500001..500025, slot i = action 0x7a121 + i.</summary>
    public const long FirstQuickSlotAction = 0x7a121;
    public const int QuickSlotActionCount = 25;

    public static readonly KeyTable Empty = new();

    KeyTable() { }

    public KeyTable(PackSet packs, params string[] languages)
    {
        strings = new GameStrings(packs, languages);
        names = strings.Table("keyname", "name");
        LoadKeySettingTable(packs);
        for (int k = 1; k < 0xd0; k++)
            if (names.TryGetValue(k.ToString(CultureInfo.InvariantCulture), out var n) && !string.IsNullOrEmpty(n))
                AddKey(k, new Key { Name = n });
        // InitKeys (keytable.cpp:632-652): 112 gamepad combine keys (4 modifiers 0x96..0x99 x 28 targets)
        for (int first = 0x96; first <= 0x99; first++)
        {
            for (int second = 0x92; second < 0xa2; second++)
            {
                if (second >= 0x96 && second <= 0x99) continue;
                AddCombineKey(first, second, 0xf0);
            }
            for (int second = 0xa3; second < 0xb3; second++)
                AddCombineKey(first, second, 0xf1);
        }
        if (packs.Exists("Xml", DefaultKeyPath)) defaultKeys = packs.Read("Xml", DefaultKeyPath);
        if (packs.Exists("Xml", DefaultMouseControlKeyPath)) defaultMouseKeys = packs.Read("Xml", DefaultMouseControlKeyPath);
        if (packs.Exists("Xml", DefaultPsKeyPath)) defaultPadKeysPs = packs.Read("Xml", DefaultPsKeyPath);
        if (packs.Exists("Xml", DefaultXboxKeyPath)) defaultPadKeysXbox = packs.Read("Xml", DefaultXboxKeyPath);
        LoadDefaultKeys();
    }

    /// <summary>CKeyTable::MakeCombineKey (@0x141430e70, keytable.cpp:2339-2348).</summary>
    public static int MakeCombineKey(int first, int second, int mode) =>
        ((mode & 0xff) << 16) | (((first - 0x91) & 0xff) << 8) | ((second - 0x91) & 0xff);

    void AddKey(int id, Key key) { keys[id] = key; byId[id] = key; }

    /// <summary>CKeyTable::CreateCombineKeyString (@0x141430ee0, keytable.cpp:2352-2367) + InitKeys (:639-650).</summary>
    void AddCombineKey(int first, int second, int mode)
    {
        if (!byId.TryGetValue(first, out var f) || !byId.TryGetValue(second, out var s)) return;
        AddKey(MakeCombineKey(first, second, mode), new Key { Name = $"{f.Name},{s.Name}" });
    }

    /// <summary>FUN_1414297e0 (:429) on DefaultKey.xml: each row fills its key's type, code and priority (the last row of a key
    /// code wins; game pad keys skipped; the slot is left as it is).</summary>
    public void LoadDefaultKeys() => LoadDefaultXml(defaultKeys, sendToServer: false);

    /// <summary>
    /// CKeyTable::FUN_1414297e0 (@0x1414297e0, keytable.cpp:429-496): loads DefaultKey.xml or DefaultMouseControlKey.xml
    /// (called by UIKeySettingSelectDialog @0x140c58690, uikeysettingselectdialog.cpp:106,111), marks dirty and sends
    /// SendOp::KeyTable mode 2 when <paramref name="sendToServer"/> is true.
    /// </summary>
    public bool LoadDefaultKeys(string xmlPath, bool sendToServer = false)
    {
        byte[] bytes = string.Equals(xmlPath, DefaultMouseControlKeyPath, StringComparison.OrdinalIgnoreCase)
            ? defaultMouseKeys
            : defaultKeys;
        return LoadDefaultXml(bytes, sendToServer);
    }

    /// <summary>ResetKeysToDefault's FUN_1414297e0(DefaultKey.xml, 0) (gameoptioninputdefault.cpp:473): nothing sent.</summary>
    public void LoadDefaultKeyboardKeys() => LoadDefaultKeys(DefaultKeyPath, sendToServer: false);

    /// <summary>
    /// CKeyTable::FUN_141429e60 (@0x141429e60, keytable.cpp:500-565) on DefaultPSKey.xml (pad kind 2) or DefaultXboxKey.xml (kinds 1
    /// and 3), as ResetKeysToDefault picks them (gameoptioninputdefault.cpp:451-461): only the game pad keys take the file's type, code
    /// and priority (a key the file lacks gets 0), then the table is dirty and sent to the server (unlike the keyboard reset).
    /// STILL-OPEN: FUN_141432360(code), the emotion skill bookkeeping per key (keytable.cpp:2674).
    /// </summary>
    public bool LoadDefaultPadKeys(int padKind)
    {
        byte[] bytes = padKind == 2 ? defaultPadKeysPs : defaultPadKeysXbox;
        if (bytes == null) return false;
        var rows = new Dictionary<int, XElement>();
        foreach (var e in GameStrings.Parse(bytes).Descendants("KeyConfig").Descendants("key"))
            if (int.TryParse((string)e.Attribute("keyCode"), out var kc)) rows[kc] = e;
        foreach (var (k, key) in keys)
        {
            if (!IsGamepadKey(k)) continue;
            if (rows.TryGetValue(k, out var e))
            {
                key.Type = Atoi((string)e.Attribute("type"));
                key.Code = long.TryParse((string)e.Attribute("code"), out var c) ? c : 0;
                key.Priority = Atoi((string)e.Attribute("priority"));
            }
            else { key.Type = 0; key.Code = 0; key.Priority = 0; }
        }
        IsDirty = true;
        SendKeysToServer();
        return true;
    }

    /// <summary>
    /// CKeyTable::FUN_141431820 (@0x141431820, keytable.cpp:2478): the text of a game pad key on the HUD. Empty while the game pad
    /// option's Use byte is off; for pad kind 2 the four shoulder keys 0x96..0x99 read L1, L2, R1, R2 (any other key: empty);
    /// otherwise the key table's name for the key.
    /// </summary>
    public string GamepadKeyText(int key, bool gamepadUse, int padKind)
    {
        if (!gamepadUse) return "";
        if (padKind == 2)
            return key switch { 0x96 => "L1", 0x97 => "L2", 0x98 => "R1", 0x99 => "R2", _ => "" };
        return byId.TryGetValue(key, out var k) ? k.Name : "";
    }

    bool LoadDefaultXml(byte[] xmlBytes, bool sendToServer)
    {
        if (xmlBytes == null) return false;
        var rows = new Dictionary<int, XElement>();
        foreach (var e in GameStrings.Parse(xmlBytes).Descendants("KeyConfig").Descendants("key"))
            if (int.TryParse((string)e.Attribute("keyCode"), out var kc)) rows[kc] = e;     // byKey[nKeyCode] = pNode
        foreach (var (k, key) in keys)
        {
            if (IsGamepadKey(k)) continue;
            if (rows.TryGetValue(k, out var e))
            {
                key.Type = Atoi((string)e.Attribute("type"));
                key.Code = long.TryParse((string)e.Attribute("code"), out var c) ? c : 0;
                key.Priority = Atoi((string)e.Attribute("priority"));
            }
            else
            {
                // keytable.cpp:471-487: keys missing from the XML get empty attributes -> 0
                key.Type = 0; key.Code = 0; key.Priority = 0;
            }
        }
        IsDirty = true;
        if (sendToServer) SendKeysToServer();
        return true;
    }

    /// <summary>CKeyTable::SendKeysToServer (@0x141429600, keytable.cpp:399-425): emits mode 2 with all m_keys entries when
    /// dirty and unlocked. Note: the client also guards on GetCurrentSceneId() == 0x620000c (game scene, :401); in this port
    /// the only live callers run inside the field HUD.</summary>
    public bool SendKeysToServer()
    {
        if (!IsDirty || Locked) return false;
        var snapshot = SnapshotBindings();
        KeysSentToServer?.Invoke(snapshot);
        IsDirty = false;
        return true;
    }

    /// <summary>All key bindings in m_keys key-code order (the payload of SendKeysToServer mode 2, keytable.cpp:410-422).</summary>
    public List<KeyBinding> SnapshotBindings()
    {
        var list = new List<KeyBinding>(keys.Count);
        foreach (var (k, v) in keys)
            list.Add(new KeyBinding(k, v.Type, v.Code, v.Slot, v.Priority));
        return list;
    }

    public bool TryGetBinding(int key, out KeyBinding binding)
    {
        if (byId.TryGetValue(key, out var k))
        {
            binding = new KeyBinding(key, k.Type, k.Code, k.Slot, k.Priority);
            return true;
        }
        binding = default;
        return false;
    }

    /// <summary>CKeyTable::BackupKeys (@0x14142a510, keytable.cpp:568).</summary>
    public void BackupKeys()
    {
        backup.Clear();
        foreach (var (k, v) in keys)
            backup[k] = (v.Type, v.Code, v.Slot, v.Priority);
    }

    /// <summary>CKeyTable::RestoreKeysFromBackup (keytable.cpp; gameoptioninputdefault.cpp:595).</summary>
    public void RestoreKeysFromBackup()
    {
        if (backup.Count == 0) return;
        foreach (var (k, b) in backup)
            if (byId.TryGetValue(k, out var v)) { v.Type = b.Type; v.Code = b.Code; v.Slot = b.Slot; v.Priority = b.Priority; }
    }

    /// <summary>CKeyTable::SetKeyBindingAndSync (@0x14142b250, keytable.cpp:703-714): updates key (and its pair key) and sends mode 1 for each.</summary>
    public bool SetKeyBindingAndSync(int key, int type, long code, int slot, int priority)
    {
        if (!SetBinding(key, type, code, slot, priority)) return false;
        IsDirty = true;
        if (!Locked && TryGetBinding(key, out var primary))
        {
            KeyBindingChanged?.Invoke(primary);
            if (PairKeys.TryGetValue(key, out var pair) && TryGetBinding(pair, out var paired))
                KeyBindingChanged?.Invoke(paired);
        }
        return true;
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
        if (!byId.TryGetValue(key, out var k)) return false;
        if (code >= FirstQuickSlotAction && code < FirstQuickSlotAction + QuickSlotActionCount) slot = 0;     // IsQuickSlotAction
        if (k.Type == type && k.Code == code && k.Slot == slot && k.Priority == priority) return false;    // stKeyData::FUN_1414287c0
        if (code != 0 && string.IsNullOrEmpty(names.GetValueOrDefault(code.ToString(CultureInfo.InvariantCulture)))) return false;
        foreach (var t in PairKeys.TryGetValue(key, out var pair) && byId.TryGetValue(pair, out var pk) ? new[] { k, pk } : new[] { k })
        {
            t.Type = type; t.Code = code; t.Priority = priority; t.Slot = slot;
        }
        return true;
    }

    /// <summary>OnKeyTable mode 0 (keytable.cpp:285-350): ClearAllBindings, then DefaultKey.xml (use-default flag or no
    /// bindings) and, with feature 0xb5 (<paramref name="padKind"/> not null), the pad file of GetPadKind (kind 2 the PS file, else
    /// the Xbox one; keytable.cpp:296-303, 312-319), or each binding of the packet, followed by BackupKeys (:349).
    /// STILL-OPEN: FUN_141432360(code), the emotion bookkeeping per bound code (:331).</summary>
    public void Apply(ServerKeyTable t, Func<int> padKind = null)
    {
        if (t == null || !t.Loaded) return;
        ClearAllBindings();
        if (t.UsesDefaultKeys)
        {
            LoadDefaultKeys();
            if (padKind != null) LoadDefaultPadKeys(padKind() == 2 ? 2 : 1);
        }
        else foreach (var b in t.Bindings) SetBinding(b.Key, b.Type, b.Code, b.Slot, b.Priority);
        BackupKeys();
    }

    /// <summary>The action the game pad's own keys carry (FindKeyByActionAndPriority(0x30d41, 2) == 0 means no pad key is set,
    /// SetGamePadEnabled @0x140d8b900, ms2inputservice.cpp:615).</summary>
    public const long PadMarkerAction = 0x30d41;

    /// <summary>The pad file load of SetGamePadEnabled's enable path (ms2inputservice.cpp:615-621): when no key carries
    /// <see cref="PadMarkerAction"/> at priority 2, kind 2 loads the PS file and every other kind the Xbox one.</summary>
    public bool LoadDefaultPadKeysIfUnset(int padKind) =>
        FindKeyByActionAndPriority(PadMarkerAction, 2) == 0 && LoadDefaultPadKeys(padKind == 2 ? 2 : 1);

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

    /// <summary>The keys m_actionKeys holds for an action, any priority (CKeyTable::IsActionKeyDown walks them, keytable.cpp:1037).</summary>
    public IEnumerable<int> KeysOfAction(long action) =>
        action == 0 ? Enumerable.Empty<int>() : keys.Where(p => p.Value.Code == action).Select(p => p.Key);

    /// <summary>FindKeyByQuickSlot (:1202).</summary>
    public int FindKeyByQuickSlot(int index, int priority)
        => index < 0 || index >= QuickSlotActionCount ? 0 : FindKeyByActionAndPriority(FirstQuickSlotAction + index, priority);

    /// <summary>GetQuickSlotActionCount (@0x14142cbb0).</summary>
    public int GetQuickSlotActionCount() => QuickSlotActionCount;

    /// <summary>GetQuickSlotAction (@0x14142cbe0): 0 past the end.</summary>
    public long GetQuickSlotAction(int index) => index < 0 || index >= QuickSlotActionCount ? 0 : FirstQuickSlotAction + index;

    /// <summary>A table of only the given keys and names (string table 5: key ids and action codes), no DefaultKey.xml.</summary>
    internal KeyTable(IReadOnlyDictionary<string, string> keyNames)
    {
        names = new Dictionary<string, string>(keyNames);
        for (int k = 1; k < 0xd0; k++)
            if (names.TryGetValue(k.ToString(CultureInfo.InvariantCulture), out var n) && !string.IsNullOrEmpty(n))
                AddKey(k, new Key { Name = n });
    }

    /// <summary>IsKeyBoundToAction @0x14142cd50 (:1254): the key's binding is <paramref name="action"/> and its type is not 0.</summary>
    public bool IsKeyBoundToAction(int key, long action) => byId.TryGetValue(key, out var k) && k.Type != 0 && k.Code == action;

    /// <summary>IsQuickSlotAction @0x14142cf10 (:1285): one of m_quickSlotActions (500001..500025, InitKeys :653).</summary>
    public static bool IsQuickSlotAction(long code) => code >= FirstQuickSlotAction && code < FirstQuickSlotAction + QuickSlotActionCount;

    /// <summary>FindQuickSlotActionIndex @0x14142e220 (:1616): the action's index in m_quickSlotActions, -1 when none.</summary>
    public static int FindQuickSlotActionIndex(long code) => IsQuickSlotAction(code) ? (int)(code - FirstQuickSlotAction) : -1;

    /// <summary>
    /// FindQuickSlotIndexByKey @0x14142dcb0 (:1488): the first quick slot whose priority 0, 1 or 2 key is <paramref name="key"/>
    /// or that key's pair key (GetPairKey); -1 when none.
    /// </summary>
    public int FindQuickSlotIndexByKey(int key)
    {
        for (int i = 0; i < QuickSlotActionCount; i++)
            for (int priority = 0; priority < 3; priority++)
            {
                int bound = FindKeyByActionAndPriority(FirstQuickSlotAction + i, priority);
                if (bound == key) return i;
                if (bound != 0 && PairKeys.TryGetValue(bound, out var pair) && pair == key) return i;
            }
        return -1;
    }

    /// <summary>
    /// GetKeySkillId @0x14142d9b0 (:1418) on FindKeyData(key) (:1150): a quick slot action gives the skill of its slot on the
    /// current page (GetQuickSlotEntry, :1574; 0 for an empty slot); type 1 the key's slot; type 2 the action code when
    /// CDynamicActionData lists it (<paramref name="dynamicActionListed"/>, null = listed for none); else the key's slot.
    /// STILL-OPEN: type 1's CGuideManager IsRestricted(8) refusal and its FUN_141275260(0x888, 1, 1) notice are not ported,
    /// and CDynamicActionData is not loaded (the HUD passes null).
    /// </summary>
    public int GetKeySkillId(int key, ServerKeyTable slots, Func<long, bool> dynamicActionListed = null)
    {
        if (!byId.TryGetValue(key, out var k)) return 0;
        if (IsQuickSlotAction(k.Code))
        {
            int index = FindQuickSlotActionIndex(k.Code);
            if (index < 0 || slots == null) return 0;
            return slots.Entry(slots.CurrentPage, index).SkillId;
        }
        if (k.Type == 1) return k.Slot;
        if (k.Type == 2) return dynamicActionListed != null && dynamicActionListed(k.Code) ? (int)k.Code : 0;
        return k.Slot;
    }

    /// <summary>
    /// CKeyTable::FUN_14142c6c0 @0x14142c6c0 (keytable.cpp:1125) for one key: the key is listed for the skill when its entry is
    /// valid (IsKeyEntryValid @0x14142cf60, :1292: a type and a code, a quick slot action's slot not empty, a type 1 key's slot
    /// not 0) and GetKeySkillId gives the skill (no dynamic action list, the one-argument form). Lookups only, no allocation.
    /// </summary>
    public bool IsKeyOfSkill(int key, int skillId, ServerKeyTable slots)
    {
        if (!byId.TryGetValue(key, out var k) || !IsKeyEntryValid(k, slots)) return false;
        return GetKeySkillId(key, slots) == skillId;
    }

    /// <summary>IsKeyEntryValid @0x14142cf60 (keytable.cpp:1292-1314): a type and a code, a quick slot action's slot not empty
    /// on the current page, a type 1 key's slot not 0.</summary>
    bool IsKeyEntryValid(Key k, ServerKeyTable slots)
    {
        if (k.Type == 0 || k.Code == 0) return false;
        if (IsQuickSlotAction(k.Code))
        {
            int index = FindQuickSlotActionIndex(k.Code);
            return index >= 0 && slots != null && !slots.Entry(slots.CurrentPage, index).IsEmpty;
        }
        return k.Type != 1 || k.Slot != 0;
    }

    int[] boundIds = Array.Empty<int>();   // m_keys' keys and entries in order, copied once (the keys are made at load only)
    Key[] boundKeys = Array.Empty<Key>();

    /// <summary>
    /// CKeyTable::GetBoundKeys @0x14142c5d0 (keytable.cpp:1109-1121): the keys whose entry is valid and has a type, in the
    /// table's order, into <paramref name="into"/> (cleared first). 0 B once <paramref name="into"/> has grown.
    /// </summary>
    public void GetBoundKeys(List<int> into, ServerKeyTable slots)
    {
        into.Clear();
        EnsureBound();
        for (int i = 0; i < boundIds.Length; i++)
            if (IsKeyEntryValid(boundKeys[i], slots)) into.Add(boundIds[i]);
    }

    void EnsureBound()
    {
        if (boundIds.Length == keys.Count) return;
        boundIds = new int[keys.Count];
        boundKeys = new Key[keys.Count];
        keys.Keys.CopyTo(boundIds, 0);
        keys.Values.CopyTo(boundKeys, 0);
    }

    /// <summary>
    /// CKeyTable::IsActionKeyDown @0x14142c270 (keytable.cpp:1037-1048): a key m_actionKeys holds for <paramref name="action"/>
    /// (a key bound to it, any priority) is down (<paramref name="isDown"/>, IsKeyDownGated). 0 B (the keys' entries are read
    /// in place, so a rebind is seen).
    /// </summary>
    public bool IsActionKeyDown(long action, Func<int, bool> isDown)
    {
        if (action == 0) return false;   // RebuildActionKeyMaps (:2663) leaves code 0 out
        EnsureBound();
        for (int i = 0; i < boundKeys.Length; i++)
            if (boundKeys[i].Code == action && isDown(boundIds[i])) return true;
        return false;
    }

    /// <summary>The held item action (CKeyTable::GetSkillIdByKey, keytable.cpp:1466).</summary>
    public const long HeldItemAction = 0x493e2;

    /// <summary>
    /// GetSkillIdByKey @0x14142dad0 (:1452): none without a local player; a key bound to the attack (0x49401) gives
    /// <paramref name="attackSkill"/> (SkillCast.AttackKeySkill: riding, states 0x1b / 0x1c, GetNormalAttackSkillId); a key bound
    /// to 0x493e2 gives the held item's skill when <paramref name="heldItemSkill"/> has one (FUN_14165f150 and GetHeldItem;
    /// null = no held item, which falls through); else <see cref="GetKeySkillId"/>.
    /// </summary>
    public int GetSkillIdByKey(int key, bool hasPlayer, Func<int> attackSkill, Func<int?> heldItemSkill, ServerKeyTable slots,
                               Func<long, bool> dynamicActionListed = null)
    {
        if (!hasPlayer) return 0;
        if (IsKeyBoundToAction(key, 0x49401)) return attackSkill();
        if (IsKeyBoundToAction(key, HeldItemAction) && heldItemSkill?.Invoke() is { } held) return held;
        return GetKeySkillId(key, slots, dynamicActionListed);
    }

    /// <summary>
    /// GetQuickSlotItemUidByKey @0x14142ded0 (:1546): the item uid of the quick slot the key is bound to (FindQuickSlotIndexByKey,
    /// then the main player info's slot's item, SkillSlot::vfunc_25); 0 when the key is no quick slot key, there is no main player
    /// info (<paramref name="slotItemUid"/> null) or the slot finds no item. <paramref name="slotItemUid"/> gives the uid of the
    /// item the shown slot of an index finds (vfunc_25 by its uid, item id or skill), not the page entry's uid.
    /// </summary>
    public long GetQuickSlotItemUidByKey(int key, Func<int, long> slotItemUid)
    {
        int index = FindQuickSlotIndexByKey(key);
        if (index < 0 || slotItemUid == null) return 0;
        return slotItemUid(index);
    }

    /// <summary>GetKeyName (:1339): the key's name; "" for no key. (Game pad keys build an icon string with feature 0xb5;
    /// they never carry a DefaultKey binding here.)</summary>
    public string GetKeyName(int key) => byId.TryGetValue(key, out var k) ? k.Name : "";

    /// <summary>LoadKeySettingTable (:2148): table/keySetting.xml's &lt;key&gt; rows that pass IsElementAvailable and whose
    /// locale is in the list, appended to their category's list.</summary>
    void LoadKeySettingTable(PackSet packs)
    {
        const string path = "table/keysetting.xml";
        if (!packs.Exists("Xml", path)) return;
        var root = GameStrings.Parse(packs.Read("Xml", path)).Root;
        if (root?.Name != "ms2") return;
        foreach (var e in root.Elements("key"))
        {
            if (!LobbyFeatures.IsLocaleInList(packs, (string)e.Attribute("locale") ?? "") || !LobbyFeatures.IsEnabledByName(packs, (string)e.Attribute("feature")))
                continue;   // IsElementAvailable, then IsLocaleInList on the same attribute
            int category = Atoi((string)e.Attribute("category"));
            if (!settings.TryGetValue(category, out var list)) settings[category] = list = new();
            list.Add(new KeySetting(Atoi((string)e.Attribute("keyGuid")), Atoi((string)e.Attribute("type")),
                Atoi((string)e.Attribute("disableprimary")), Atoi((string)e.Attribute("disablesecondary")),
                Atoi((string)e.Attribute("disablethirdly"))));
        }
    }

    /// <summary>GetKeyAction: the code bound to <paramref name="key"/> (0 for none or an unknown key).</summary>
    public long GetKeyAction(int key) => keys.TryGetValue(key, out var k) ? k.Code : 0;

    /// <summary>IsKeyRebindable @0x14142cc40 (keytable.cpp:1227): every key but 1, 0x1c, 0x82, 0x83 and 0xa3..0xa6.</summary>
    public static bool IsKeyRebindable(int key) => key != 1 && key != 0x83 && key != 0x82 && key != 0x1c && !(key >= 0xa3 && key <= 0xa6);

    /// <summary>@0x141430470 (keytable.cpp:2191): <paramref name="action"/> has a row in key setting category 12.</summary>
    public bool IsListedInCategory12(long action) =>
        settings.TryGetValue(12, out var l) && l.Any(s => s.Action == action);

    /// <summary>GetSettingList (:2226): the category's rows; null for none.</summary>
    public IReadOnlyList<KeySetting> GetSettingList(int category) => settings.TryGetValue(category, out var l) ? l : null;

    /// <summary>CStringTable::FUN_1418125b0 (stringtable.cpp:799) for an action id: ids 0x56057c1..90299999 from table 4
    /// (skillName), the rest from table 5 (keyName). (The compact-config and closet ids 0xaae61.., 0xdbba1.. add player text;
    /// no key setting row uses them.)</summary>
    public string ActionName(long id)
    {
        var key = id.ToString(CultureInfo.InvariantCulture);
        if (id >= 0x56057c1 && id < 90300000)
        {
            skillNames ??= strings?.Table("skillname", "name") ?? new();
            return skillNames.TryGetValue(key, out var s) ? s : "";
        }
        return names.TryGetValue(key, out var n) ? n : "";
    }
}
