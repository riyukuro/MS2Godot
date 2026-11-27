using Ms2.Avm2;
using Ms2.Avm2.Flash;

namespace Ms2.Ui;

/// <summary>
/// The text focus of the movies that are not on a <see cref="UiStage"/>. In the client every UI movie sits in main.gfx's stage,
/// so one question covers them all: CUIService::IsTextFieldFocused (@0x141adbb00, uiservice.cpp:1364) asks
/// GFxManager::IsTextFieldFocused(2) (@0x1422af2d0, gfxmanager.cpp:531), the IME manager of that movie
/// (IMEManager::IsTextFieldFocused @0x142544ef0, sfr_as3_20.cpp:347). The UIMessageBox layer (a UIHeadUpDisplay,
/// uimessagebox.cpp:371, its name prompts and number inputs) is one of those movies. Here a layer such as the message box
/// has its own Player outside the scene's stage, so it registers its movie here and <see cref="Focused"/> is the other half of
/// <see cref="InputBlock.TextFieldFocused"/>.
/// </summary>
public static class OffStageTextFocus
{
    sealed record Entry(Player Player, Func<bool> Shown);

    static readonly List<Entry> entries = new();

    /// <summary>Adds a movie; <paramref name="shown"/> says it is drawn (a hidden movie's focus does not count).</summary>
    public static void Add(Player player, Func<bool> shown)
    {
        Remove(player);
        entries.Add(new Entry(player, shown));
    }

    public static void Remove(Player player) => entries.RemoveAll(e => e.Player == player);

    /// <summary>A registered, shown movie's focus is a text field that takes the IME (<see cref="UiStage.TakesIme"/>).</summary>
    public static bool Focused
    {
        get
        {
            foreach (var e in entries)
                if (UiStage.TakesIme(e.Player.Focus) && e.Shown?.Invoke() != false) return true;
            return false;
        }
    }
}
