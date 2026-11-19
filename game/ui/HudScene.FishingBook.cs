using Godot;
using Ms2.Formats.World;
using Ms2.Quest;
using Ms2.Ui;
using Ms2.Ui.Hud;

namespace Ms2Godot.Ui;

/// <summary>
/// The fishing book: UIFishingBookDialog (0x4ee6, movie uifishingbookdialog) over the fish tables and the session's catch
/// records (the Fishing packets, 0x73). Opened by the upper HUD's fishing book button (OnFishingBookClick,
/// uiupperhuddialog.cpp:507). The book sends nothing. Offline, no fish is known.
/// </summary>
public partial class HudScene
{
    FishingBookController fishingBook;
    MovieDialog fishingBookMovie;
    static readonly FishingBookData offlineFishingBook = new();
    Action unwatchFishingBook;

    bool FishingBookOpen => fishingBookMovie != null && fishingBook?.IsClosed == false;

    /// <summary>The dialog's C++ side with its packet listener: a new list or a catch fills the open book's tab again
    /// (the client's handler calls RefreshCurrentTab when the dialog exists).</summary>
    void FishingBookSide()
    {
        unwatchFishingBook?.Invoke();
        var live = Ms2Godot.Net.LiveSession.Instance;
        var c = fishingBook = new FishingBookController { Data = live?.FishingBook ?? offlineFishingBook };
        c.HasTitleId = id => live != null && live.Me.Titles.Contains(id);
        c.AchieveCompleted = id => live?.Achieves.Find(id)?.IsCompleted == true;
        c.ReplaceBoldWords = s => (questTable ??= QuestTable.Load(assets.Packs, new GameStrings(assets.Packs, assets.Language).Languages,
            data.Locale, f => !data.FeaturesOff.Contains(f))).Words.ReplaceBoldWords(s);
        c.MapExists = id => hudModel.Tables.MapName((int)id) != null;
        c.PlaySound += key => FieldAudio.Instance?.PlayUi(key);
        c.Unloaded += () =>
        {
            fishingBookMovie?.Host.Unload();
            fishingBookMovie?.QueueFree();
            fishingBookMovie = null;
        };
        Action<Ms2.Net.Packets.FishingMessage> onPacket = _ => { if (FishingBookOpen) fishingBook.RefreshCurrentTab(); };
        if (live != null) live.FishingReceived += onPacket;
        unwatchFishingBook = () => { if (live != null) live.FishingReceived -= onPacket; };
    }

    /// <summary>CUIService::ToggleDialog(0x4ee6): open when closed, else vfunc_67.</summary>
    void ToggleFishingBook()
    {
        if (FishingBookOpen) fishingBook.Close();
        else OpenFishingBook();
    }

    void OpenFishingBook()
    {
        if (fishingBook == null) FishingBookSide();
        if (fishingBookMovie != null) { fishingBookMovie.Host.Unload(); fishingBookMovie.QueueFree(); fishingBookMovie = null; }
        fishingBookMovie = new MovieDialog(assets, fishingBook, hudModel, GetWindow().Size);
        uiRoot.AddChild(fishingBookMovie);
    }
}
