using Ms2.Avm2;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Ui.Skills;

namespace Ms2.Ui.Hud;

/// <summary>
/// UICoupleActionDialog (ms2/client/ms2/uicoupleactiondialog.cpp, UI 0x4f4e, "CoupleAction"), the C++ side of
/// uicoupleaction: the local player's learned couple emotions (main type 0xf) as a grid, with the sort, favorite-only and
/// search filters, the favorite toggle (Emote mode 4) and the use of a clicked emotion on the selected character
/// (onSelectEmotion, <see cref="UseEmotion"/>). It is the twin of <see cref="DynamicActionDialogController"/> without the
/// key and isLocked members, with a grid of at least 0x1e cells and no cube mode check.
/// STILL-OPEN: vfunc_13's Enter in the search box (SetSearchText from m_inputSearch, then the focus moves), the arrow keys
/// FUN_140bee150, the key bind FUN_140bee5e0, OnMessage's message box (FUN_14151f200), onFocusIn / onFocusOut and the
/// tooltip (vfunc_49 @0x140bec760) do nothing here.
/// </summary>
public sealed class CoupleActionDialogController : UiController
{
    public override string Movie => "uicoupleaction";
    protected override string EntityMethod => "buildActions";
    public override string TypeName => "CoupleAction";

    /// <summary>The skill main type the list keeps (FUN_140becef0: GetMainType() != 0xf is skipped).</summary>
    public const int CoupleActionMainType = 0xf;
    /// <summary>PadEntriesToGrid (@0x140bee040): rows of 6, at least 0x1e cells.</summary>
    public const int GridColumns = 6, GridMinimum = 0x1e;

    readonly EmotionList emotions;
    readonly List<Emotion> valid = new();
    readonly SortedDictionary<uint, Emotion> listed = new();      // m_180, std::map keyed by the unsigned id
    readonly List<Emotion> sorted = new();
    readonly List<object> actions = new();
    long selected;          // m_178: SetSelectedCharacter
    bool sortFlag;          // m_1a0: onSort
    bool favoriteOnly;      // m_1a1: onFavoriteSort
    string search = "";     // m_1a8

    /// <summary>SkillDataTableCli::GetSkillData; null when unknown.</summary>
    public Func<uint, SkillData> Skill = _ => null;
    /// <summary>CStringTable::FUN_141811980(4, id), the skill's name.</summary>
    public Func<uint, string> ActionName = _ => "";
    /// <summary>CDynamicActionData::Find(id)->m_reserved; null when no row (<see cref="GameTables.DynamicActionReserved"/>).</summary>
    public Func<uint, IReadOnlyList<string>> Reserved = _ => null;
    /// <summary>The local player exists (WorldService::GetLocalPlayer).</summary>
    public Func<bool> HasPlayer = () => true;
    /// <summary>WorldService::FUN_141bd1530(m_178): the selected character is in the field.</summary>
    public Func<long, bool> HasCharacter = _ => false;
    /// <summary>The server time the emotions' expiry is tested against (stEmotion::FUN_1417643d0).</summary>
    public Func<long> Now = () => 0;
    /// <summary>FUN_140710590, the send.</summary>
    public Action<IOutPacket> Send = _ => { };
    /// <summary>MyPC::FUN_141525480(id, 0) @0x141525480: the own cast of the skill at GetSkillLevel.</summary>
    public Action<uint> UseSkill = _ => { };

    public CoupleActionDialogController(EmotionList emotions) { this.emotions = emotions; }

    /// <summary>The last buildActions array: one member map per action, then nulls up to the grid.</summary>
    public IReadOnlyList<object> Actions => actions;
    /// <summary>The emotion the last build focused (setFocusByEmotion); 0 = none.</summary>
    public uint FocusId { get; private set; }
    /// <summary>m_178, the character the emotion is asked of (the popup's target).</summary>
    public long SelectedCharacter => selected;
    public bool SortFlag => sortFlag;
    public bool FavoriteOnly => favoriteOnly;
    public string SearchText => search;
    public int ListedCount => listed.Count;

    bool NoMovie => Host?.Entity == null;

    /// <summary>vfunc_65 (@0x140bec0c0), run when the dialog opens: the selected character (m_178), m_180 and the search
    /// text are cleared, both sort flags off.</summary>
    public void Opened()
    {
        selected = 0;
        listed.Clear();
        search = "";
        sortFlag = false;
        favoriteOnly = false;
    }

    /// <summary>SetSelectedCharacter (@0x140bed2a0): m_178.</summary>
    public void SetSelectedCharacter(long characterId) => selected = characterId;

    /// <summary>OnLoadInit (@0x140bec200): FUN_140becef0, then NotifyLearnEmotion (FUN_140becea0) refreshes. OnLoadComplete
    /// (@0x140bec2f0) adds m_inputSearch's change handler FUN_140beced0, which is empty.</summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        emotions.Learned -= OnLearned;
        emotions.Learned += OnLearned;
        Unloaded -= Unhook;
        Unloaded += Unhook;
        Refresh();
    }

    void OnLearned(int id) => Refresh();
    void Unhook() => emotions.Learned -= OnLearned;

    /// <summary>vfunc_67 (@0x140bec140): UIDialog's close, then the skill system's new marks cleared (FUN_141769280) and
    /// m_180 cleared.</summary>
    public override void Close()
    {
        base.Close();
        emotions.ClearNew();
        listed.Clear();
    }

    /// <summary>
    /// FUN_140becef0: m_180 cleared; without the movie or the player nothing more. The valid emotions (GetValidEmotions)
    /// whose skill exists with main type 0xf go into m_180 by id (std::map insert: the first one of an id stays), then the
    /// build in the order m_1a0 picks.
    /// </summary>
    public void Refresh()
    {
        listed.Clear();
        if (NoMovie || !HasPlayer()) return;
        valid.Clear();
        emotions.GetValid(Now(), valid);
        for (int i = 0; i < valid.Count; i++)
        {
            var e = valid[i];
            uint id = (uint)e.Id;
            var skill = Skill(id);
            if (skill == null || skill.MainType != CoupleActionMainType) continue;
            listed.TryAdd(id, e);
        }
        Build(sortFlag);
    }

    /// <summary>
    /// FUN_140bed2d0 (m_1a0 off: Unk_EmotionGreater, the 0x141764410 body, <see cref="EmotionList.CompareAscending"/>) and
    /// FUN_140bed8a0 (m_1a0 on: Unk_EmotionLess, the 0x141764570 body,
    /// <see cref="DynamicActionDialogController.CompareDescending"/>): each action whose skill exists, a favorite when
    /// m_1a1 is on, and with a search text (never "/") whose name (string table 4) or one of whose keywords
    /// (GetEmotionKeywords split at ", ", FUN_140988d60) holds it (CStringW::Find, case sensitive) is pushed as
    /// {id, isNew, iconPath, isFavorite}; a new one is focused (the last wins). PadEntriesToGrid, buildActions, then
    /// setFocusByEmotion (SetFocusByEmotion @0x140bed170) when one was focused.
    /// </summary>
    void Build(bool descending)
    {
        sorted.Clear();
        foreach (var e in listed.Values) sorted.Add(e);
        if (descending) sorted.Sort(DynamicActionDialogController.CompareDescending);
        else sorted.Sort(EmotionList.CompareAscending);
        actions.Clear();
        uint focus = 0;
        for (int i = 0; i < sorted.Count; i++)
        {
            var e = sorted[i];
            uint id = (uint)e.Id;
            var skill = Skill(id);
            if (skill == null) continue;
            if (favoriteOnly && !e.Entry.Favorite) continue;
            if (search.Length != 0)
            {
                if (search == "/") continue;
                if (!Matches(id)) continue;
            }
            if (e.IsNew) focus = id;
            actions.Add(new Dictionary<string, object>
            {
                ["id"] = id,
                ["isNew"] = e.IsNew ? 1 : 0,
                ["iconPath"] = skill.Icon ?? "",
                ["isFavorite"] = e.Entry.Favorite,
            });
        }
        PadEntriesToGrid(actions);
        FocusId = focus;
        Host.Invoke("buildActions", actions);
        if (focus != 0) Host.Invoke("setFocusByEmotion", focus);
    }

    bool Matches(uint id)
    {
        if ((ActionName(id) ?? "").Contains(search, StringComparison.Ordinal)) return true;
        foreach (var word in EmotionKeywords(id).Split(KeywordSeparators, StringSplitOptions.RemoveEmptyEntries))
            if (word.Contains(search, StringComparison.Ordinal)) return true;
        return false;
    }

    static readonly char[] KeywordSeparators = { ',', ' ' };

    /// <summary>GetEmotionKeywords (@0x140bedf00): "" without a CDynamicActionData row, else each reserved path followed by
    /// a space ("%s "), right-trimmed.</summary>
    public string EmotionKeywords(uint id)
    {
        var reserved = Reserved(id);
        if (reserved == null) return "";
        return string.Concat(reserved.Select(r => r + " ")).TrimEnd();
    }

    /// <summary>PadEntriesToGrid (@0x140bee040): nulls up to max(ceil(count / 6.0f) * 6, 0x1e).</summary>
    public static void PadEntriesToGrid(List<object> list)
    {
        int rounded = (int)(MathF.Ceiling(list.Count / 6.0f) * 6.0f);
        int missing = Math.Max(rounded, GridMinimum) - list.Count;
        for (int i = 0; i < missing; i++) list.Add(null);
    }

    /// <summary>vfunc_28 (@0x140bec930); the names are matched without case (_stricmp).</summary>
    public override bool OnCallback(string name, object[] args)
    {
        if (base.OnCallback(name, args)) return true;
        switch (name.ToLowerInvariant())
        {
            case "onselectemotion":
            {
                // :302-307: PopUInt, 0 ignored; the use, then vfunc_67 closes the dialog whether or not it was used
                if (args.Length == 0) return false;
                uint id = Rt.ToUint32(args[0]);
                if (id == 0) return false;
                UseEmotion(id);
                Close();
                return false;
            }
            case "onfavoritesort":
                if (args.Length == 0) return false;         // SetArgsIfAny fails: return 0
                favoriteOnly = Rt.ToBoolean(args[0]);
                Refresh();
                return false;
            case "onsort":
                if (args.Length == 0) return false;
                sortFlag = Rt.ToBoolean(args[0]);
                Refresh();
                return false;
            case "onfavorite":
            {
                if (args.Length == 0) return false;
                uint id = Rt.ToUint32(args[0]);
                if (id == 0) return false;
                if (!listed.TryGetValue(id, out var e)) return false;
                ushort level = e.Level;
                bool favorite = args.Length > 1 && Rt.ToBoolean(args[1]);
                e.Entry = e.Entry with { Favorite = favorite };
                Send(new EmoteFavoriteRequest((int)id, level, favorite));
                if (favoriteOnly) Refresh();
                return false;
            }
            case "onsearch":
                if (args.Length == 0) return false;
                SetSearchText(Rt.ToString(args[0]) ?? "");
                return false;
            // onFocusIn, onFocusOut: STILL-OPEN (see the class summary)
        }
        return false;
    }

    /// <summary>FUN_140bed1f0 (@0x140bed1f0, uicoupleactiondialog.cpp:405-420): nothing without the local player or when the
    /// selected character (m_178) is not in the field (FUN_141bd1530); else MyPC::FUN_141525480(id, 0)
    /// (<see cref="UseSkill"/>). No cube mode check here: StateSkill's couple branch makes its own.</summary>
    public void UseEmotion(uint id)
    {
        if (!HasPlayer()) return;
        if (!HasCharacter(selected)) return;
        UseSkill(id);
    }

    /// <summary>SetSearchText (@0x140bede70): an equal text (CompareNoCase) changes nothing; else it is kept and the list
    /// refreshed.</summary>
    public void SetSearchText(string text)
    {
        if (string.Equals(search, text, StringComparison.OrdinalIgnoreCase)) return;
        search = text;
        Refresh();
    }
}
