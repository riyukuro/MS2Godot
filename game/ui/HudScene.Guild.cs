using Godot;
using Ms2.Ui;
using Ms2.Ui.Hud;

namespace Ms2Godot.Ui;

/// <summary>
/// The guild dialog: UIGuildDialog (0x4e4f, movie uiguilddialog) over CGuildManager's state. Live, the book is the session's
/// (the Guild packets, 0x8A) and the sends go out (Guild 0x4B). Offline, an empty book, and the sends are logged. The C++
/// side is kept between opens, as the client keeps its dialogs. STILL-OPEN: the master popup (0x4e39) is logged.
/// </summary>
public partial class HudScene
{
    GuildDialogController guild;            // UIGuildDialog's C++ side, kept between opens
    MovieDialog guildMovie;                 // its movie, from open until OnEndClose
    static GuildBook offlineGuild;
    bool guildWatched;
    Action unwatchGuild;

    bool GuildOpen => guild != null && !guild.IsClosed;

    /// <summary>CUIService::ToggleDialog(0x4e4f): open when closed, else vfunc_67.</summary>
    void ToggleGuild()
    {
        if (!GuildOpen) OpenGuild();
        else guild.Close();
    }

    GuildBook GuildState()
    {
        var book = Ms2Godot.Net.LiveSession.Instance is { } live ? live.Guilds : (offlineGuild ??= new GuildBook { Send = SendGuild });
        var t = hudModel.Tables;
        book.Common = t.Common;
        book.CommonByKey = key => t.Common(key) ?? "";
        book.NameLengthMin = t.GuildNameLengthMin;
        book.CreateMinLevel = t.GuildCreateMinLevel;
        book.Level = () => hudModel.HasLocalPlayer ? hudModel.Player.Level : null;
        var checker = CommunityChecker();
        book.CheckName = name => checker.CheckGuildName(name, t.GuildNameLengthMin, t.GuildNameLengthMax, out var msg) == 0 ? null : msg ?? "";
        return book;
    }

    /// <summary>The book's UI calls (CGuildManager): the notices (ShowSystemNotice with the chat log), the chat lines, the
    /// boxes, the sounds and CloseDialog(0x4e4f). Subscribed once per scene.</summary>
    void WatchGuild()
    {
        if (guildWatched || Ms2Godot.Net.LiveSession.Instance is not { } live) return;
        guildWatched = true;
        var book = GuildState();
        void Notice(string text, int type, bool log)
        {
            if (string.IsNullOrEmpty(text) || SystemText == null) return;
            SystemText.ShowMessage(text, type, 0);
            if (log) ChatLog.AddSystemLine(text, TalkKind.SystemNotice);
        }
        void Chat(string text, int kind) => ChatLog.AddSystemLine(text, kind);
        void Box(string text) => MessageBoxQueue.ShowMessageBox(text, 0, null, "");
        void Sound(string key) => FieldAudio.Instance?.PlayUi(key);
        void CloseDialog() { if (GuildOpen) guild.Close(); }
        book.Notice += Notice;
        book.ChatLine += Chat;
        book.Box += Box;
        book.Sound += Sound;
        book.DialogClose += CloseDialog;
        unwatchGuild = () =>
        {
            book.Notice -= Notice; book.ChatLine -= Chat; book.Box -= Box; book.Sound -= Sound; book.DialogClose -= CloseDialog;
            guildWatched = false;
        };
    }

    /// <summary>The guild dialog's sends: out live, logged offline.</summary>
    void SendGuild(Ms2.Net.Wire.IOutPacket packet)
    {
        if (Ms2Godot.Net.LiveSession.Instance is { } s) s.SendGuild(packet);
        else GD.Print($"guild: offline, not sent: {packet}");
    }

    /// <summary>Opens the guild dialog (FUN_141924680 -> vfunc_65): the movie runs its own ActionScript with UIGuildDialog's
    /// C++ side (GuildDialogController). The movie is dropped when its close tween ends.</summary>
    void OpenGuild()
    {
        if (GuildOpen) return;
        if (guildMovie != null) { guildMovie.Host.Unload(); guildMovie.QueueFree(); guildMovie = null; }
        var book = GuildState();
        if (guild == null || guild.Book != book)
        {
            var t = hudModel.Tables;
            var c = new GuildDialogController(book)
            {
                Common = t.Common,
                CommonByKey = key => t.Common(key) ?? "",
                LevelByExp = t.GuildLevelByExp,
                CreatePrice = t.GuildCreatePrice,
                NameLengthMin = t.GuildNameLengthMin,
                NameLengthMax = t.GuildNameLengthMax,
                PlayerName = () => hudModel.HasLocalPlayer ? hudModel.Player.Name : null,
                FeatureEnabled = hudModel.IsFeatureEnabled,
            };
            c.PopupRequested += (x, y, name) => GD.Print($"guild: master popup for {name} (0x4e39 is not in this HUD)");
            c.Unloaded += () => { guildMovie?.Host.Unload(); guildMovie?.QueueFree(); guildMovie = null; };
            guild = c;
        }
        guildMovie = new MovieDialog(assets, guild, hudModel, GetWindow().Size);
        GD.Print($"guild: load={guildMovie.Loaded} (in guild {book.InGuild})");
        uiRoot.AddChild(guildMovie);
    }

    /// <summary>The guild dialog's vfunc_13: UIDialog's Escape closes it.</summary>
    bool GuildKeys(InputEventKey k)
    {
        if (GuildOpen && k.Keycode == Key.Escape) { guild.Close(); return true; }
        return false;
    }
}
