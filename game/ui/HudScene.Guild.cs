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

    static long GuildServerTime() => Ms2Godot.Net.LiveSession.Instance?.GameSession?.ServerTime ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();

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
        book.HasOwnEffect = (id, level) => ownBuffs?.HasAny(id, level) == true;
        book.Level = () => hudModel.HasLocalPlayer ? hudModel.Player.Level : null;
        book.PlayerName = () => hudModel.HasLocalPlayer ? hudModel.Player.Name : null;
        book.ServerTime = GuildServerTime;
        book.LevelByExp = t.GuildLevelByExp;
        book.Property = level => t.GuildProperties.TryGetValue(level, out var p) ? p : null;
        book.DonateMeso = t.GuildDonateMeso;
        book.SkillTable ??= new Ms2.Formats.World.GuildSkillTable(assets.Packs, e => Ms2.Ui.FeatureElements.IsAvailable(assets.Packs, e));   // CGuildXmlData::LoadGuildBuff
        book.Events ??= new Ms2.Formats.World.GuildEventTable(assets.Packs, e => Ms2.Ui.FeatureElements.IsAvailable(assets.Packs, e));   // CGuildXmlData::LoadGuildEvent
        book.Houses ??= new Ms2.Formats.World.GuildHouseTable(assets.Packs, e => Ms2.Ui.FeatureElements.IsAvailable(assets.Packs, e));   // CGuildXmlData::LoadGuildHouse
        book.Npcs ??= new Ms2.Formats.World.GuildNpcTable(assets.Packs, e => Ms2.Ui.FeatureElements.IsAvailable(assets.Packs, e));   // CGuildXmlData::LoadGuildNpc
        book.StringTableText = t.StringTableText;
        book.Mesos = () => hudModel.HasLocalPlayer ? hudModel.Player.Mesos : 0;
        book.RefuseInvite = () => GameOptions.AccountCommunity.DisableGuildRequest;    // m_refuseGuildInvite, flag 5
        var checker = CommunityChecker();
        book.CheckName = name => checker.CheckGuildName(name, t.GuildNameLengthMin, t.GuildNameLengthMax, out var msg) == 0 ? null : msg ?? "";
        book.CheckGradeName = name => checker.CheckGuildGradeName(name, regionNa: false, out var msg) == 0 ? null : msg ?? "";
        book.CheckContent = text => checker.CheckContentText(text, out var msg) == 0 ? null : msg ?? "";
        // CheckName (uihelpers.cpp:873): FUN_140225210(name, message, 0), the long form
        book.BannedName = name => checker.CheckCharacterName(name, out _, shortForm: false) != 0;
        book.BannedWord = text => checker.CheckGuildNoticeText(text, out _) != 0;
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
        void InviteBox(Ms2.Net.Packets.GuildInviteInfo invite) => GuildInviteBox.Show(invite);
        book.DialogClose += CloseDialog;
        book.InviteBox += InviteBox;
        unwatchGuild = () =>
        {
            book.Notice -= Notice; book.ChatLine -= Chat; book.Box -= Box; book.Sound -= Sound; book.DialogClose -= CloseDialog;
            book.InviteBox -= InviteBox;
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
                JobName = t.JobName,
                MapName = id => t.MapName(id) ?? id.ToString(),
                StringTableText = t.StringTableText,
                FieldCapacity = id => Ms2.Formats.World.FieldData.FindById(assets.Packs, id)?.Capacity,   // CFieldDataCli::Find(id)->vfunc_2
                Events = book.Events,
                ServerTime = GuildServerTime,
                DonateMeso = t.GuildDonateMeso,
                EffectText = t.AdditionalDescription,
                EffectIcon = (id, lv) => t.Additional.GetLevel(id, lv)?.Icon,   // SkillDataTableCli::FUN_140aafef0, GetIcon
                TimeSpanText = sec => Ms2.Ui.Hud.BuffTooltip.FormatTimeSpan(t, sec, true, true, true, false),
                ReplaceValueWords = s => t.ReservedWords.ReplaceValueWords(s, hudModel.HasLocalPlayer ? hudModel.Player.Name : null),
                PlayerCharId = () => hudModel.HasLocalPlayer ? Ms2Godot.Net.LiveSession.Instance?.CharacterId : null,
                ImagePath = (id, code) => Ms2Godot.Net.ProfileImageService.Images.Path(id, code, true),
                AccumExp = level => t.GuildAccumExp.TryGetValue(level, out var v) ? v : 0,
                MaxLevel = () => t.GuildAccumExp.Count == 0 ? 0 : t.GuildAccumExp.Keys.Last(),
            };
            c.MemberPopupRequested += (x, y, member) => EnsurePopupMenu().OpenForGuildMember(x, y, member);   // PopUpMenuFromGuildMember
            c.RankingRequested += category => GD.Print($"guild: the ranking dialog (0x4f24, category {category}) is not in this HUD");
            c.RankPageRequested += WebBrowser.Navigate;
            c.PopupRequested += (x, y, name) => GD.Print($"guild: master popup for {name} (0x4e39 is not in this HUD)");
            c.Unloaded += () => { guildMovie?.Host.Unload(); guildMovie?.QueueFree(); guildMovie = null; };
            guild = c;
        }
        guildMovie = new MovieDialog(assets, guild, hudModel, ScreenMode.PixelSize(GetWindow()));
        GD.Print($"guild: load={guildMovie.Loaded} (in guild {book.InGuild})");
        uiRoot.AddChild(guildMovie);
    }

    /// <summary>The guild dialog's vfunc_13: UIDialog's Escape closes it.</summary>
    bool GuildKeys(InputEventKey k)
    {
        if (GuildOpen && k.Keycode == Key.Escape) { guild.Close(); return true; }
        return false;
    }

    /// <summary>The scripted live run's guild dialog: opened as the menu's Guild entry does (CUIService::ToggleDialog
    /// (0x4e4f)); false when it was already up.</summary>
    public bool OpenGuildScripted()
    {
        if (GuildOpen) return false;
        OpenGuild();
        return true;
    }

    /// <summary>The scripted live run's guild dialog, while it is up.</summary>
    public GuildDialogController GuildDialog => GuildOpen ? guild : null;

    /// <summary>
    /// A scripted click in the guild dialog: <paramref name="at"/> is where the path starts (a page of
    /// <see cref="GuildDialogController"/> or null for the movie root); a name is a member, "#n" the child n
    /// (getChildAt), "button:n" the category bar's button n (getButtonAt), "row:n" a list's row n (getRendererAt). The
    /// click lands at the middle of the member's bounds; false when the dialog or a step is not there.
    /// </summary>
    public bool ClickGuild(object at, params string[] path)
    {
        if (!GuildOpen || guildMovie is not { } m) return false;
        if (at == null) at = m.Host.Entity;
        foreach (var step in path)
        {
            if (step.StartsWith('#') && int.TryParse(step.AsSpan(1), out int ci)) at = m.Host.Invoke(at, "getChildAt", ci);
            else if (step.StartsWith("button:") && int.TryParse(step.AsSpan(7), out int bi)) at = m.Host.Invoke(at, "getButtonAt", bi);
            else if (step.StartsWith("row:") && int.TryParse(step.AsSpan(4), out int ri)) at = m.Host.Invoke(at, "getRendererAt", ri);
            else at = m.Host.Child(step, at);
            if (at == null) return false;
        }
        if (at is not Ms2.Avm2.Flash.AsDisplayObject o) return false;
        var b = o.GetBounds(null);
        m.Player.Click(b.X + b.W * 0.5, b.Y + b.H * 0.5);
        return true;
    }
}
