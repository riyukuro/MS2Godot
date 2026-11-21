using Ms2.Avm2;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Ui.Skills;

namespace Ms2.Ui.Hud;

/// <summary>
/// UIDynamicActionDialog (ms2/client/ms2/uidynamicactiondialog.cpp, UI 0x4e7a, "DynamicAction"), the C++ side of
/// uidynamicaction: the local player's learned emotions (main type 0xe) as a grid, with the sort, favorite-only and search
/// filters and the favorite toggle (Emote mode 4).
/// STILL-OPEN: onSelectEmotion (UseAction @0x14192c9b0: MyPC::FUN_141525480, the emotion skill use), onFocusIn /
/// onFocusOut and vfunc_13's key bind flow (the 0x84f confirm box, the 0x84c notice, FUN_14192df00
/// SetKeyBindingAndSync), onShiftRClick (PreviewAction @0x14192ca10, the skill preview dialog 0x4ec3), the tooltip
/// (vfunc_49 @0x14192bb30) and NotifyClearAchieve (FUN_14192c660 refreshes; its sender is not ported) do nothing here.
/// </summary>
public sealed class DynamicActionDialogController : UiController
{
    public override string Movie => "uidynamicaction";
    protected override string EntityMethod => "buildActions";
    public override string TypeName => "DynamicAction";

    /// <summary>The skill main type the list keeps (FUN_14192c6b0: GetMainType() != 0xe is skipped).</summary>
    public const int DynamicActionMainType = 0xe;
    /// <summary>PadEntriesToGrid (@0x14192d960): rows of 6, at least 0x18 cells.</summary>
    public const int GridColumns = 6, GridMinimum = 0x18;

    readonly EmotionList emotions;
    readonly List<Emotion> valid = new();
    readonly SortedDictionary<uint, Emotion> listed = new();      // m_190, std::map keyed by the unsigned id
    readonly List<Emotion> sorted = new();
    readonly List<object> actions = new();
    bool sortFlag;          // m_1b0: onSort
    bool favoriteOnly;      // m_1b1: onFavoriteSort
    string search = "";     // m_1b8

    /// <summary>SkillDataTableCli::GetSkillData; null when unknown.</summary>
    public Func<uint, SkillData> Skill = _ => null;
    /// <summary>CStringTable::FUN_141811980(4, id), the skill's name.</summary>
    public Func<uint, string> ActionName = _ => "";
    /// <summary>CDynamicActionData::Find(id)->m_reserved; null when no row (<see cref="GameTables.DynamicActionReserved"/>).</summary>
    public Func<uint, IReadOnlyList<string>> Reserved = _ => null;
    /// <summary>GetKeyTable()->GetKeyName(FindKeyByActionAndPriority(id, 0)); null = the model's key table.</summary>
    public Func<uint, string> ActionKeyName;
    /// <summary>The local player exists (WorldService::GetLocalPlayer, FUN_14192c6b0 returns without one).</summary>
    public Func<bool> HasPlayer = () => true;
    /// <summary>The server time the emotions' expiry is tested against (stEmotion::FUN_1417643d0).</summary>
    public Func<long> Now = () => 0;
    /// <summary>FUN_140710590, the send.</summary>
    public Action<IOutPacket> Send = _ => { };

    public DynamicActionDialogController(EmotionList emotions) { this.emotions = emotions; }

    /// <summary>The last buildActions array: one member map per action, then nulls up to the grid.</summary>
    public IReadOnlyList<object> Actions => actions;
    /// <summary>The emotion the last build focused (setFocusByEmotion); 0 = none.</summary>
    public uint FocusId { get; private set; }
    public bool SortFlag => sortFlag;
    public bool FavoriteOnly => favoriteOnly;
    public string SearchText => search;
    public int ListedCount => listed.Count;

    bool NoMovie => Host?.Entity == null;

    /// <summary>vfunc_65 (@0x14192ad70), run when the dialog opens: the key bind state (m_178) and the search text are
    /// cleared, both sort flags off.</summary>
    public void Opened()
    {
        search = "";
        sortFlag = false;
        favoriteOnly = false;
    }

    /// <summary>OnLoadInit (@0x14192aff0): FUN_14192c6b0, then NotifyLearnEmotion (FUN_14192c630) refreshes. OnLoadComplete
    /// (@0x14192b1c0) adds m_inputSearch's change handler FUN_14192c690, which is empty.</summary>
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

    /// <summary>vfunc_67 (@0x14192ade0): the skill system's new marks cleared (FUN_141769280), m_190 cleared, then
    /// UIDialog's close.</summary>
    public override void Close()
    {
        emotions.ClearNew();
        listed.Clear();
        base.Close();
    }

    /// <summary>
    /// FUN_14192c6b0: m_190 cleared; without the movie or the player nothing more. The valid emotions
    /// (GetValidEmotions) whose skill exists with main type 0xe go into m_190 by id (std::map insert: the first one of an
    /// id stays), then the build in the sort order m_1b0 picks.
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
            if (skill == null || skill.MainType != DynamicActionMainType) continue;
            listed.TryAdd(id, e);
        }
        Build(sortFlag);
    }

    /// <summary>
    /// FUN_14192ca70 (m_1b0 off: std::set with Unk_EmotionGreater, the 0x141764410 body, <see cref="EmotionList.CompareAscending"/>)
    /// and FUN_14192d100 (m_1b0 on: Unk_EmotionLess, the 0x141764570 body, <see cref="CompareDescending"/>): each action
    /// whose skill exists, a favorite when m_1b1 is on, and with a search text (never "/") whose name (string table 4)
    /// or one of whose keywords (GetActionKeywords split at ", ", FUN_140988d60) holds it (CStringW::Find, case
    /// sensitive) is pushed as {id, key, isNew, isLocked 0, iconPath, isFavorite}; a new one is focused (the last wins).
    /// PadEntriesToGrid, buildActions, then setFocusByEmotion (SetFocusByEmotion @0x14192c930) when one was focused.
    /// </summary>
    void Build(bool descending)
    {
        sorted.Clear();
        foreach (var e in listed.Values) sorted.Add(e);
        if (descending) sorted.Sort(CompareDescending);
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
                ["key"] = KeyName(id),
                ["isNew"] = e.IsNew ? 1 : 0,
                ["isLocked"] = 0,
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
        string keywords = ActionKeywords(id);
        foreach (var word in keywords.Split(KeywordSeparators, StringSplitOptions.RemoveEmptyEntries))
            if (word.Contains(search, StringComparison.Ordinal)) return true;
        return false;
    }

    static readonly char[] KeywordSeparators = { ',', ' ' };

    /// <summary>GetActionKeywords (@0x14192d820): "" without a CDynamicActionData row, else each reserved path followed by
    /// a space ("%s "), right-trimmed.</summary>
    public string ActionKeywords(uint id)
    {
        var reserved = Reserved(id);
        if (reserved == null) return "";
        return string.Concat(reserved.Select(r => r + " ")).TrimEnd();
    }

    string KeyName(uint id)
    {
        if (ActionKeyName != null) return ActionKeyName(id) ?? "";
        var keys = Model.Keys;
        return keys.GetKeyName(keys.FindKeyByActionAndPriority(id, 0));
    }

    /// <summary>PadEntriesToGrid (@0x14192d960): nulls up to max(ceil(count / 6.0f) * 6, 0x18).</summary>
    public static void PadEntriesToGrid(List<object> list)
    {
        int rounded = (int)(MathF.Ceiling(list.Count / 6.0f) * 6.0f);
        int missing = Math.Max(rounded, GridMinimum) - list.Count;
        for (int i = 0; i < missing; i++) list.Add(null);
    }

    /// <summary>Unk_SsEmotionGreater (@0x141764570, skillslot.cpp:1804): the non-permanent first; permanent ones by id
    /// descending, the others by sort time descending, ties by id descending (ids unsigned).</summary>
    public static int CompareDescending(Emotion a, Emotion b)
    {
        if (a.Entry.Permanent != b.Entry.Permanent) return a.Entry.Permanent ? 1 : -1;
        if (!a.Entry.Permanent && a.Entry.SortTime != b.Entry.SortTime) return b.Entry.SortTime.CompareTo(a.Entry.SortTime);
        return ((uint)b.Id).CompareTo((uint)a.Id);
    }

    /// <summary>vfunc_28 (@0x14192bed0); the names are matched without case (_stricmp).</summary>
    public override bool OnCallback(string name, object[] args)
    {
        if (base.OnCallback(name, args)) return true;
        switch (name.ToLowerInvariant())
        {
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
            // onSelectEmotion, onFocusIn, onFocusOut, onShiftRClick: STILL-OPEN (see the class summary)
        }
        return false;
    }

    /// <summary>SetSearchText (@0x14192d790): an equal text (CompareNoCase) changes nothing; else it is kept and the list
    /// refreshed.</summary>
    public void SetSearchText(string text)
    {
        if (string.Equals(search, text, StringComparison.OrdinalIgnoreCase)) return;
        search = text;
        Refresh();
    }
}
