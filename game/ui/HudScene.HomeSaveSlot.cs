using Godot;
using Ms2.Ui.Hud;

namespace Ms2Godot.Ui;

/// <summary>
/// UIHomeSaveSlotDialog (0x4f27, movie uihomesaveslotdialog, uihomesaveslotdialog.cpp) over <see cref="HomeSaveSlotHost"/>. The home menu's
/// design load and blueprint import entries set its mode (SetSaveMode @0x140faa6d0 / SetLoadMode @0x140faa750); the dialog opens when its movie is
/// not loaded yet. STILL-OPEN: the save entries of the menu (they need the cube count) do not reach it.
/// </summary>
public partial class HudScene
{
    HomeSaveSlotDialogController homeSaveSlot;
    MovieDialog homeSaveSlotMovie;

    bool HomeSaveSlotOpen => homeSaveSlotMovie != null && homeSaveSlot?.IsClosed == false;

    void SetHomeSaveSlotMode(bool save)
    {
        if (!HomeSaveSlotOpen)
        {
            if (liveHome == null) return;
            if (commonStrings == null) SystemMenuFacts();
            var host = new HomeSaveSlotHost(liveHome, id => HomeOptionHost.FieldIsHome(assets.Packs, id), GameOptions.Language);
            var c = homeSaveSlot = new HomeSaveSlotDialogController(host);
            c.MovieLoaded = () => homeSaveSlotMovie != null;
            c.Open = () => ShowHomeSaveSlots(c);
            if (save) c.SetSaveMode(); else c.SetLoadMode();
            return;
        }
        if (save) homeSaveSlot.SetSaveMode(); else homeSaveSlot.SetLoadMode();
    }

    void ShowHomeSaveSlots(HomeSaveSlotDialogController c)
    {
        if (homeSaveSlotMovie != null)
        {
            homeSaveSlotMovie.Host.Unload();
            homeSaveSlotMovie.QueueFree();
            homeSaveSlotMovie = null;
        }
        var m = homeSaveSlotMovie = new MovieDialog(assets, c, hudModel, ScreenMode.PixelSize(GetWindow()));
        c.Unloaded += () =>
        {
            m.Host.Unload();
            m.QueueFree();
            if (homeSaveSlotMovie == m) homeSaveSlotMovie = null;
        };
        uiRoot.AddChild(m);
    }
}
