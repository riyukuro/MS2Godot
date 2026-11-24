using Godot;
using Ms2.Ui.Hud;

namespace Ms2Godot.Ui;

/// <summary>
/// UIItemGetOptionDialog (0x4e6c), the auto-loot options, with its C++ side as <see cref="ItemGetOptionDialogController"/>:
/// the inventory's onClickItemOption opens it in mode 1 on the character options' autoLooting block
/// (<see cref="Ms2Godot.GameOptions.AutoLoot"/>). STILL-OPEN: mode 0, the pet's (RequestPet 6; the port has no pet info).
/// </summary>
public partial class HudScene
{
    ItemGetOptionDialogController itemGetOption;     // from open until the close
    MovieDialog itemGetOptionMovie;                  // the movie, from open until the unload

    bool ItemGetOptionOpen => itemGetOption != null && !itemGetOption.IsClosed;

    /// <summary>SetMode(mode), then CUIService::OpenDialog(0x4e6c): opens only a closed dialog.</summary>
    void OpenItemGetOption(int mode)
    {
        if (ItemGetOptionOpen) return;
        if (itemGetOptionMovie != null) { itemGetOptionMovie.Host.Unload(); itemGetOptionMovie.QueueFree(); itemGetOptionMovie = null; }
        var c = new ItemGetOptionDialogController
        {
            Mode = mode,
            Saved = () => Ms2Godot.GameOptions.AutoLoot,
            SaveCharacterSettings = Ms2Godot.GameOptions.SaveCharacterSettings,
            Feature = hudModel.IsFeatureEnabled,
            RegionCn = Ms2Godot.GameOptions.RegionCn,
        };
        c.Button += name => GD.Print($"item get option: {name} is not ported");
        c.Unloaded += () =>
        {
            itemGetOptionMovie?.Host.Unload(); itemGetOptionMovie?.QueueFree(); itemGetOptionMovie = null;
        };
        itemGetOption = c;
        itemGetOptionMovie = new MovieDialog(assets, c, hudModel, ScreenMode.PixelSize(GetWindow()));
        GD.Print($"item get option: mode {mode} load={itemGetOptionMovie.Loaded}");
        uiRoot.AddChild(itemGetOptionMovie);
    }

    /// <summary>UIDialog's key handling: Escape closes it.</summary>
    bool ItemGetOptionKeys(InputEventKey k)
    {
        if (!ItemGetOptionOpen || k.Keycode != Key.Escape) return false;
        itemGetOption.Close();
        return true;
    }
}
