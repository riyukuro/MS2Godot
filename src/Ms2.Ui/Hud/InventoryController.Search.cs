using Ms2.Avm2;
using Ms2.Formats.Text;
using Ms2.Ui.Items;

namespace Ms2.Ui.Hud;

/// <summary>
/// The bag's item search (uiinventorydialog.cpp): the search input's candidates as it changes (OnSearchTextChanged), the
/// search by the button (OnItemSearch) or Enter (vfunc_13), which scrolls to the first slot whose item has the searched
/// name and marks every such slot (updateIconSearchOn), or empties the input and shows "no item" (box 0x937).
/// </summary>
public sealed partial class InventoryController
{
    /// <summary>m_1d8: the search's text form (<see cref="JamoSearchText"/>), "" when none; cleared by OnLoadComplete and
    /// every bag rebuild (FUN_14199ba10).</summary>
    string searchText = "";

    /// <summary>GetSearchText @0x14199abe0.</summary>
    public string SearchText => searchText;

    /// <summary>The rows the last candidate list had (m_rowCount: at most 5).</summary>
    public int CandidateRows { get; private set; }

    /// <summary>OnLoadComplete's m_inputSearch (FUN_140bbe940) and its change handler (ASTextInput::AddChangeHandler, the
    /// "change" event).</summary>
    void BindSearchInput()
    {
        searchText = "";
        if (Host.Child("m_inputSearch") is not { } input) return;
        Func<object[], object> onChange = _ => { Host.RT.Guard("inventory search", OnSearchTextChanged); return null; };
        Host.Invoke(input, "addEventListener", "change", onChange, false, 0, false);
    }

    bool SearchFocused(object input) => Rt.ToNumber(Host.Get(input, "focused")) != 0;

    /// <summary>ASUIComponent::SetFocused (asuicomponent.cpp:123): losing it clears the stage focus.</summary>
    void SetFocused(object c, int n)
    {
        Host.Set(c, "focused", n);
        if (n == 0) Host.Set(Host.Player.Stage, "focus", null);
    }

    /// <summary>
    /// OnSearchTextChanged @0x14199a620 (:1232): nothing unless the input has the focus and a text; the text with its
    /// trailing blanks trimmed (CStringW::TrimRight) in its text form; when that is not empty the shown tab's matching
    /// item names (FindMatchingItemNames) are the candidates ({label: name}); m_rowCount = min(5, their count), then
    /// setCandidates.
    /// </summary>
    public void OnSearchTextChanged()
    {
        if (Host?.Entity == null || Host.Child("m_inputSearch") is not { } input || !SearchFocused(input)) return;
        string text = Rt.ToString(Host.Get(input, "text")) ?? "";
        if (text.Length == 0) return;
        string form = JamoSearchText.Of(text.TrimEnd(' ', '\t', '\n', '\v', '\f', '\r'));
        var names = form.Length > 0 ? FindMatchingItemNames(form) : new List<string>();
        CandidateRows = Math.Min(5, names.Count);
        Host.Set(input, "m_rowCount", CandidateRows);
        Host.Invoke(input, "setCandidates", DataProvider(names.Select(n => Host.Obj(("label", n)))));
    }

    /// <summary>FindMatchingItemNames @0x14199c610: the shown tab's items in slot order (GetItemsInCategory), each name
    /// (CItem::FUN_140527b10) whose lower-cased text form holds the lower-cased search; a name once.</summary>
    public List<string> FindMatchingItemNames(string form)
    {
        var names = new List<string>();
        if (View == null || Model == null) return names;
        string key = JamoSearchText.Lower(form);
        var t = new ItemTooltip(Model, 0);
        foreach (var cell in View.Cells(View.Tab))
        {
            if (cell.Empty || FindItem(cell.Uid) is not { } item) continue;
            string name = t.Name(item) ?? "";
            if (JamoSearchText.Lower(JamoSearchText.Of(name)).Contains(key, StringComparison.Ordinal) && !names.Contains(name))
                names.Add(name);
        }
        return names;
    }

    /// <summary>
    /// FUN_14199b800 (:1439), the "OnItemSearch" callback's and Enter's: an empty text searches "" (FUN_14199c2b0); else
    /// the platform's check of m_inputSearch (vfunc_37 with Unk_BlockList 0x4e2d) answers 1 and the text form is searched.
    /// The port has no platform check: the answer is taken as 1, so its later reply (vfunc_80, a refusal: the input emptied
    /// and box 0x8b7) never comes.
    /// </summary>
    public void Search(string text)
    {
        text ??= "";
        ApplySearch(text.Length == 0 ? "" : JamoSearchText.Of(text));
    }

    /// <summary>
    /// FUN_14199c2b0 (:1609): m_1d8 = the text; the bag's FindSlotBySearch; a slot found scrolls m_itemBagEx to its row
    /// (SetScrollPosition(slot / 6)); none (an empty search too) takes the focus off the input, empties it (SetHtmlTextW,
    /// vfunc_24(1)) and shows box 0x937 whose OK (OnSearchNotFoundConfirm) makes this dialog the top one and gives the
    /// input the focus back.
    /// </summary>
    public void ApplySearch(string form)
    {
        searchText = form ?? "";
        if (Host?.Entity == null) return;
        int slot = FindSlotBySearch(searchText);
        if (slot > -1)
        {
            if (Host.Child("m_itemBagEx") is { } list) Host.Set(list, "scrollPosition", slot / 6);
            return;
        }
        if (Host.Child("m_inputSearch") is { } input)
        {
            SetFocused(input, 0);
            Host.Set(input, "htmlText", "");
            Host.Invoke(input, "invalidate");
            Host.Invoke(input, "validateNow");
        }
        var box = MessageBoxQueue.Create(0, Model?.Tables.Common(0x937) ?? "", null, "");
        box.Callback = (result, _) => { OnSearchNotFoundConfirm(result); return true; };
        ShowBox(box);
    }

    /// <summary>OnSearchNotFoundConfirm @0x14199c7b0: OK (0) makes this dialog the top one (FUN_1422d4dd0) and focuses the input.</summary>
    void OnSearchNotFoundConfirm(int result)
    {
        if (result != 0 || Host?.Entity == null || Host.Child("m_inputSearch") is not { } input) return;
        if (Model != null) Model.TopLayer = Host.Id;
        SetFocused(input, 1);
    }

    /// <summary>
    /// InventorySlotBag::FindSlotBySearch @0x14140f620: every slot with an item has MatchSearchName run, which calls the
    /// slot's updateIconSearchOn with "its name's text form equals the search" (false for an empty search); here the
    /// slot's binding is pushed again, whose updateIconSearchOn member the slot sets the same way. The lowest matching
    /// slot, -1 for none.
    /// </summary>
    public int FindSlotBySearch(string form)
    {
        int found = -1;
        if (View == null || Model == null) return found;
        var t = new ItemTooltip(Model, 0);
        var cells = View.Cells(View.Tab);
        for (int i = 0; i < cells.Count && i < slotCount; i++)
        {
            if (cells[i].Empty || FindItem(cells[i].Uid) is not { } item) continue;
            Host.Push(SlotName(i));
            if (found < 0 && form.Length > 0 && NameMatches(t.Name(item), form)) found = i;
        }
        return found;
    }

    /// <summary>MatchSearchName / FUN_14140c180: the lower-cased text forms are equal.</summary>
    static bool NameMatches(string name, string form) =>
        JamoSearchText.Lower(JamoSearchText.Of(name ?? "")) == JamoSearchText.Lower(form);

    /// <summary>InvenSlot::FUN_14140c180 @0x14140c180, the slot binding's updateIconSearchOn: the dialog shown, a search,
    /// and the item's name matching it.</summary>
    bool SearchOn(Item item) => !IsClosed && searchText.Length > 0 && NameMatches(new ItemTooltip(Model, 0).Name(item), searchText);

    /// <summary>
    /// vfunc_13 @0x141999a80, Enter (DIK 0x1c): only while this dialog is the top one (FUN_1422d4e20); with the input
    /// focused the search runs (FUN_14199b800 of its text), the dialog takes the focus and the input loses it: taken. With
    /// the input not focused the dialog loses the focus and the key is not taken.
    /// </summary>
    public bool EnterKey()
    {
        if (Host?.Entity == null || IsClosed) return false;
        if (Model != null && Model.TopLayer != null && Model.TopLayer != Host.Id) return false;
        if (Host.Child("m_inputSearch") is not { } input) return false;
        if (SearchFocused(input))
        {
            Search(Rt.ToString(Host.Get(input, "text")) ?? "");
            Host.Set(Host.Entity, "focused", 1);
            SetFocused(input, 0);
            return true;
        }
        SetFocused(Host.Entity, 0);
        return false;
    }
}
