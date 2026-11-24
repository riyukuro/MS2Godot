using Godot;

namespace Ms2Godot.Ui;

/// <summary>The scripted live sound run's hooks (MS2_LIVE_SOUND, <see cref="Ms2Godot.Net.LiveSession.SoundRun"/>): the options
/// dialog's sound tab opened, its controls set and saved as a player would, through the dialog's own handlers.</summary>
public partial class HudScene
{
    /// <summary>Opens the options dialog on its sound page (SoundOptionPage, the page key 7). The tab index; 0 when there is none.</summary>
    public int OpenSoundOptionsScripted()
    {
        EnsureOptionHost();
        int index = 0, i = 0;
        foreach (var p in optionDialog.Pages)
        {
            i++;
            if (p is Ms2.Formats.Options.SoundOptionPage) { index = i; break; }
        }
        if (index == 0) return 0;
        OpenGameOption(index);
        return index;
    }

    /// <summary>The sound page is open and filled (its movie tab called OnTabInit).</summary>
    public bool SoundOptionsReadyScripted() => optionHost is { IsClosed: false } && optionDialog?.Pages is { } pages
        && System.Linq.Enumerable.OfType<Ms2.Formats.Options.SoundOptionPage>(pages) is var s && System.Linq.Enumerable.Any(s, p => p.Active);

    /// <summary>Moves a slider of the open tab (the movie's component, which ReadControls reads at the save).</summary>
    public void SetOptionValueScripted(string control, float value) => optionHost?.Tab.SetValue(control, value);

    /// <summary>Ticks or clears a check box of the open tab.</summary>
    public void SetOptionSelectedScripted(string control, bool on) => optionHost?.Tab.SetSelected(control, on);

    /// <summary>The dialog's save button (OnSave), then the dialog closed.</summary>
    public void SaveOptionsScripted()
    {
        optionDialog?.OnSave();
        optionDialog?.OnCloseDialog();
    }
}
