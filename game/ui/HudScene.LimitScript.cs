namespace Ms2Godot.Ui;

/// <summary>The scripted live run's hooks for the field limit checks and the status options (MS2_LIVE_CHAT lines "@skillapply",
/// "@lapenunmount:N", "@statusoptions", "@boxclose"): the dialogs opened and their buttons pressed through the dialogs' own
/// callbacks, as the movie calls them on a click.</summary>
public partial class HudScene
{
    /// <summary>Opens the skill book when it is closed (ToggleDialog(0x4f2d)'s open). True when it is open.</summary>
    public bool OpenSkillBookScripted()
    {
        if (skillBook == null || skillBook.IsClosed) OpenSkillBook();
        return skillBook is { IsClosed: false };
    }

    /// <summary>The skill book's apply button: the movie's onApplySkill callback (vfunc_28 :2118). False when it is not open or
    /// the callback is not taken (waiting for the server).</summary>
    public bool PressSkillApplyScripted() => skillBook is { IsClosed: false } && skillBook.OnCallback("onApplySkill", System.Array.Empty<object>());

    /// <summary>Opens the lapenshard window when it is closed (ToggleDialog(0x4f6d)'s open; nothing without feature 0xd3). True
    /// when it is open.</summary>
    public bool OpenLapenshardScripted()
    {
        if (lapenshardMovie == null || lapenshard == null || lapenshard.IsClosed) OpenLapenshard();
        return lapenshard is { IsClosed: false };
    }

    /// <summary>The equip page's unmount of a slot: the movie's OnUnmountEquipSlot callback with the slot number.</summary>
    public bool UnmountLapenshardScripted(int slot) =>
        lapenshard is { IsClosed: false } && lapenshard.OnCallback("OnUnmountEquipSlot", new object[] { (double)slot });

    /// <summary>Opens the options dialog on its status page (StatusOptionPage). The tab index; 0 when there is none.</summary>
    public int OpenStatusOptionsScripted()
    {
        EnsureOptionHost();
        int index = 0, i = 0;
        foreach (var p in optionDialog.Pages)
        {
            i++;
            if (p is Ms2.Formats.Options.StatusOptionPage) { index = i; break; }
        }
        if (index == 0) return 0;
        OpenGameOption(index);
        return index;
    }

    /// <summary>The status page is open and filled (its movie tab called OnTabInit).</summary>
    public bool StatusOptionsReadyScripted() => optionHost is { IsClosed: false } && optionDialog?.Pages is { } pages
        && System.Linq.Enumerable.Any(System.Linq.Enumerable.OfType<Ms2.Formats.Options.StatusOptionPage>(pages), p => p.Active);

    /// <summary>A check box of the open tab: its state, or null when the movie has no such box.</summary>
    public bool? OptionSelectedScripted(string control) => optionHost?.Tab.IsSelected(control);

    /// <summary>The options dialog closed without a save.</summary>
    public void CloseOptionsScripted() => optionDialog?.OnCloseDialog();
}
