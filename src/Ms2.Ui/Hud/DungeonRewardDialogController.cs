using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Quest;
using Ms2.Ui.Items;

namespace Ms2.Ui.Hud;

/// <summary>
/// The C++ side of the dungeon result window, UIDungeonRewardDialog (client/ms2/uidungeonrewarddialog.cpp, 0x140e20ae0-0x140e26800; UI id
/// 0x4ef9, type name "DungeonRewardDialog") for its movie uidungeonrewarddialog. The inbound DungeonReward packet (0x1B, <see cref="DungeonRewardPacket"/>)
/// reaches OnPacket (@0x140e22130, :386): an open window is closed and its state reset, then mode 0 (the result, OnRewardPacket @0x140e22210),
/// 1 (a round, OnRoundPacket @0x140e225a0) and 3 (the minigame end, FUN_140e226b0) open the window, mode 2 (FUN_140e22650) only stores the rewards.
/// OnLoadComplete (@0x140e21090, :104) binds the close and the party leave buttons, fills the avatar's name and job, the meso and exp texts,
/// the reward items bag and, by the packet's kind, the clear time, the guild reward and the title (FUN_140e22d70); after 500 ms the bonus
/// effects start and 1000 ms later the totals include the bonus reward (vfunc_72 @0x140e21bd0, <see cref="Tick"/>).
/// STILL-OPEN: the character's render target (CreateAvatar @0x140e25420, loadRenderTarget @0x140e25540, the win pose at vfunc_25 @0x140e21dc0);
/// the rank gauge and the mission labels (FUN_140e24060 @0x140e24060, CreateMissionLabels @0x140e25dc0) which need RoomStageDungeonCli;
/// the massive event panel 20044 (FindMassiveEvent) and its restore at close (vfunc_67 @0x140e21b30); the win sound (PlaySystemSound with the
/// string at DAT_143ac5988); CQuestManager::MarkField180 at close; the minigame frames beyond their label and setRound call.
/// </summary>
public sealed class DungeonRewardDialogController : UiController
{
    public const int UiId = 0x4ef9;
    /// <summary>The reward items bag's id (m_268, the constructor @0x140e20ae0) and its slots' icon type and flags (CreateItemSlotBag 0x415, 0x800).</summary>
    public const string BagId = "DungeonReward_gainItems";
    public const int IconType = 0x415, SlotFlags = 0x800;
    /// <summary>vfunc_72: the first timer's 500 ms (FUN_140e22d70) and the second's 1000 ms (the delay constant).</summary>
    public const int BonusDelayMs = 500, TotalsDelayMs = 1000;
    /// <summary>The reward keys of the guild reward (FUN_140e22d70): the guild fund and the guild exp.</summary>
    public const byte GuildFundKey = 4, GuildExpKey = 5;
    /// <summary>The group type of a guild raid (GroupType 6): its room shows the guild reward.</summary>
    public const int GuildRaidGroup = 6;

    public override string Movie => "uidungeonrewarddialog";
    protected override string EntityMethod => "alignment";
    /// <summary>GetTypeName (@0x140e21000, :303).</summary>
    public override string TypeName => "DungeonRewardDialog";

    /// <summary>The tables before the movie loads (OnPacket runs before the window opens and needs the room table); after it the model's.</summary>
    public GameTables PreloadTables { get; set; }
    GameTables Tables => Host != null ? Model.Tables : PreloadTables ?? GameTables.Empty;

    /// <summary>The client's sends: DungeonReward 6 (SendDungeonReward) and Party mode 3 (SendPartyMode3).</summary>
    public Action<IOutPacket> Send = _ => { };
    /// <summary>GetTickCountMs2, for the two timers.</summary>
    public Func<long> NowMs = () => Environment.TickCount64;
    /// <summary>The weekly reward counts (GetWeeklyRewardCountText @0x140e14d70).</summary>
    public DungeonRecordBook Records = new();
    /// <summary>The local player exists (WorldService::GetLocalPlayer): CreateAvatar and SetCharacterInfo do nothing without it.</summary>
    public Func<bool> HasLocalPlayer = () => true;

    // ---- the state ResetState (@0x140e25610) clears ----

    /// <summary>m_type: 0 nothing, 1 the result, 2 a round, 3 the minigame start, 4 the minigame end.</summary>
    public int Type { get; private set; }
    /// <summary>m_win (true after a reset), m_party, m_room (null after a reset), m_278 the clear seconds, m_294 the best and m_298 the run score.</summary>
    public bool Win { get; private set; } = true;
    public bool Party { get; private set; }
    public DungeonRoomData Room { get; private set; }
    public int Seconds { get; private set; }
    public int BestScore { get; private set; }
    public int RunScore { get; private set; }
    public int Round { get; private set; }
    public int Stage { get; private set; }
    /// <summary>m_1e8 and m_228: the main and the bonus rewards.</summary>
    public DungeonRewardInfo Main { get; private set; } = new();
    public DungeonRewardInfo Bonus { get; private set; } = new();
    /// <summary>m_tip: the lines of the bonus detail's tooltip.</summary>
    public string Tip { get; private set; } = "";

    DungeonRewardPacket last;
    object rewardInfo, titleClip, closeButton, leaveButton;
    long timer1 = -1, timer2 = -1;       // m_timer1 and m_timer2 (a stop time, -1 = not running)
    readonly List<string> bagSlots = new();

    /// <summary>ResetState (@0x140e25610): the packet's state back to its start values.</summary>
    public void ResetState()
    {
        Type = 0; Win = true; Party = false; Room = null; Seconds = 0; BestScore = 0; RunScore = 0; Round = 0; Stage = 0;
        Main = new(); Bonus = new(); Tip = ""; last = null;
        rewardInfo = titleClip = closeButton = leaveButton = null;
        timer1 = timer2 = -1;
    }

    /// <summary>
    /// OnPacket's switch (@0x140e22130, :388) after the open window was closed and the state reset (the caller does both): the stored
    /// state of the mode. Returns whether the client opens the window (FUN_141924680) after it: modes 0, 1 and 3; mode 2 only stores;
    /// a mode the switch does not know does nothing.
    /// </summary>
    public bool OnPacket(DungeonRewardPacket p)
    {
        last = p;
        switch (p.Mode)
        {
            case 0:         // OnRewardPacket (:557)
                Type = 1; Tip = "";
                Win = p.Win;
                Room = Tables.DungeonRooms.GetRoom((uint)p.RoomId);
                Party = p.Party;
                Seconds = p.Seconds; BestScore = p.BestScore; RunScore = p.RunScore;
                Main = p.Main; Bonus = p.Bonus;
                BuildTip(p);
                return true;
            case 1:         // OnRoundPacket (:393)
                Type = 2; Round = p.Round; Stage = p.Stage; Main = p.Main;
                return true;
            case 2:         // FUN_140e22650 (:401)
                Type = 3; Main = p.Main;
                return false;
            case 3:         // FUN_140e226b0 (:405)
                Type = 4; Win = p.Win; Main = p.Main;
                return true;
            default:
                return false;
        }
    }

    /// <summary>OnRewardPacket's loop (:568): the line of each of the ten flags that is set, joined with a newline.</summary>
    void BuildTip(DungeonRewardPacket p)
    {
        for (int i = 0; i < 10; i++)
        {
            if (!p.IsBitSet(i)) continue;
            string text = TipLine(i);
            if (text.Length == 0) continue;
            if (Tip.Length > 0) Tip += "\n";
            Tip += text;
        }
    }

    /// <summary>FUN_140e25840 (@0x140e25840, :704): the tip line of a flag. 0 and a flag of 1 without the room give "".</summary>
    public string TipLine(int kind)
    {
        string C(string key) => Tables.Common(key) ?? "";
        switch (kind)
        {
            case 0: return "";
            case 1:
                if (Room == null) return C("s_dungeon_reward_bonus_event");   // the client falls through into case 2
                return ClientFormat.Format(C("s_dungeon_reward_dungeon_reward_count"),
                    Records.WeeklyRewardCountText(Room.RewardRoomId, C("s_dungeonRoom_event_reward_count")));
            case 2: return C("s_dungeon_reward_bonus_event");
            case 3: return C("s_dungeon_reward_mission_rank");
            case 4: return C("s_dungeon_reward_dungeon_helper");
            case 5: return C("s_dungeon_reward_dungeon_helper_event");
            case 6: return C("s_dungeon_reward_mentor");
            case 7: return C("s_dungeon_reward_mentee");
            case 8: return C("s_dungeon_reward_mentee_party_gift");
            case 9: return C("s_dungeon_reward_united_weekly");
            default: return "";          // the client asserts and gives ""
        }
    }

    /// <summary>
    /// OnLoadComplete (@0x140e21090, :104): UIDialog's; the reward clip m_rewardAni's m_rewardInfo and the rank clip's m_rankInfo; the close
    /// and party leave buttons of m_buttonArea (the client throws a SwfLoad error for a missing one), the close label "leave" for a room that
    /// leaves after the reward, the party leave button hidden; the title; the timers reset; the character's name and job
    /// (SetCharacterInfo), the texts and the bag (FUN_140e22820) and the kind's own layout. The mission rank part is STILL-OPEN, so the three
    /// clips sit at the positions FUN_140e24c50(false) gives.
    /// </summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        var reward = Host.Child("m_rewardAni");
        rewardInfo = reward == null ? null : Host.Child("m_rewardInfo", reward);
        var buttons = Host.Child("m_buttonArea");
        if (buttons != null)
        {
            closeButton = Host.Child("m_closeButton", buttons);
            leaveButton = Host.Child("m_partyLeaveButton", buttons);
            if (closeButton == null || leaveButton == null) { Host.RT.NoteMissing("dungeon reward: m_closeButton or m_partyLeaveButton"); return; }
            Bind(closeButton, OnCloseButton);
            if (Room != null && Room.IsLeaveAfterCloseReward) Host.Set(closeButton, "label", "$s_dungeonReward_leave");   // FUN_1422cdfb0
            Bind(leaveButton, OnPartyLeaveButton);
            Host.SetVisible(leaveButton, false);
        }
        titleClip = Host.Child("m_title");
        timer1 = timer2 = -1;
        SetCharacterInfo();
        ShowRewards();
        switch (Type)
        {
            case 1: ShowResult(); break;
            case 2: UpdateMinigameStage(); break;
            case 3: case 4: SetFrame(rewardInfo, "_minigame"); break;          // ShowMinigame @0x140e25050, FUN_140e250e0 @0x140e250e0
        }
        PlaceClips(false);                                                        // FUN_140e24c50(false): no mission rank (STILL-OPEN)
    }

    void Bind(object button, Action onClick)
    {
        Func<object[], object> f = _ => { onClick(); return null; };
        Host.Invoke(button, "addEventListener", "buttonClick", f, false, 0, false);
    }

    /// <summary>FUN_140e24c50 (@0x140e24c50, :492) for the case of no rank part: the avatar, the reward and the hidden rank clip at their places; with the rank part shown, the three.</summary>
    void PlaceClips(bool show)
    {
        var avatar = Host.Child("m_rewardAvatar");
        var reward = Host.Child("m_rewardAni");
        var rank = Host.Child("m_rankAni");
        if (avatar == null || reward == null || rank == null) return;
        if (show)
        {
            Host.SetVisible(rank, true);
            SetPos(avatar, 484, 242); SetPos(reward, 896, 242); SetPos(rank, 1516, 242);
        }
        else
        {
            Host.SetVisible(rank, false);
            SetPos(avatar, 714, 242); SetPos(reward, 1126, 242);
        }
    }

    void SetPos(object clip, int x, int y) { Host.Set(clip, "x", x); Host.Set(clip, "y", y); }

    /// <summary>SetCharacterInfo (@0x140e25170, :687): m_characterInfo.m_charInfo's m_charName and the job movie's frame "_{sub job}".</summary>
    void SetCharacterInfo()
    {
        if (!HasLocalPlayer()) return;
        var avatar = Host.Child("m_rewardAvatar");
        if (avatar == null) return;
        var info = Host.Child("m_characterInfo", avatar);
        if (info == null) return;
        var charInfo = Host.Child("m_charInfo", info);
        if (charInfo == null) return;
        SetText(Host.Child("m_charName", charInfo), Pc.Name);
        SetFrame(Host.Child("m_jobMovie", charInfo), "_" + Pc.SubJob.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// FUN_140e22820 (@0x140e22820, :583): without the reward clip nothing. The meso and exp texts (grouped numbers; the exp in s_format_exp),
    /// the bonus text and detail shown for the result (kind 1), the two effects hidden, and the reward items bag with the main reward's items,
    /// started in the movie by initRewardItemData.
    /// </summary>
    void ShowRewards()
    {
        if (rewardInfo == null) return;
        SetTotals(Main.GetMeso(), Main.GetExp());
        bool normal = Type == 1;
        if (Host.FindNamed("m_bonusText", Node(rewardInfo)) is { } bonus) Host.SetVisible(bonus, normal);
        if (Host.FindNamed("m_bonusDetail", Node(rewardInfo)) is { } detail) Host.SetVisible(detail, normal);
        ShowEffects(false);
        FillBag(Main.Items);
    }

    /// <summary>The meso text FUN_1409404f0 (the grouped number) and the exp text in s_format_exp.</summary>
    void SetTotals(long meso, long exp)
    {
        if (Host.FindNamed("m_mesoText", Node(rewardInfo)) is { } m) SetText(m, NumberText.Group(meso.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        if (Host.FindNamed("m_expText", Node(rewardInfo)) is { } e)
            SetText(e, ClientFormat.Format(Tables.Common("s_format_exp") ?? "", NumberText.Group(exp.ToString(System.Globalization.CultureInfo.InvariantCulture))));
    }

    /// <summary>FUN_140e23e10 (@0x140e23e10): the meso and exp effects play (frame 1) when asked for both and the bonus has the value, else hidden.</summary>
    void ShowEffects(bool both)
    {
        ShowEffect("m_mesoEffect", both && Bonus.GetMeso() > 0);
        ShowEffect("m_expEffect", both && Bonus.GetExp() > 0);
    }

    void ShowEffect(string name, bool show)
    {
        if (Host.FindNamed(name, Node(rewardInfo)) is not { } eff) return;
        Host.SetVisible(eff, show);
        if (show) Host.Invoke(eff, "gotoAndPlay", 1);                           // vfunc_12(1)
    }

    /// <summary>The reward bag of CreateItemSlotBag (the bag's slots), built from the items that exist in the item table (BuildRewardItems @0x140e25c30, :723).</summary>
    void FillBag(IReadOnlyList<DungeonRewardItem> items, IReadOnlyList<DungeonRewardItem> more = null)
    {
        ReleaseBag();
        int i = 0;
        int total = items.Count + (more?.Count ?? 0);
        for (int k = 0; k < total; k++)
        {
            var d = k < items.Count ? items[k] : more[k - items.Count];
            if (Item.Create(Tables.Items, d.ItemId, d.Rank) is not { } item) continue;
            item.SetCountClamped(d.Count);
            string name = BagId + "." + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Model.ItemSlots[name] = new ItemInfoSlot(name, item, i, IconType, SlotFlags);
            bagSlots.Add(name);
            i++;
        }
        Host.Invoke(Host.Entity, "initRewardItemData", Host.Obj(("bagId", BagId)));
    }

    void ReleaseBag()
    {
        foreach (var n in bagSlots) Model?.ItemSlots.Remove(n);
        bagSlots.Clear();
    }

    /// <summary>The names of the bag's slots (the movie asks for them by the bag id).</summary>
    public override IEnumerable<string> GetBagSlotBindings(string bag) => bag == BagId ? bagSlots : Array.Empty<string>();

    /// <summary>
    /// FUN_140e22d70 (@0x140e22d70, :610) for the result: the reward clip at "_dungeon", the time movie's text from string 0x97e (minutes,
    /// seconds), the guild reward block shown with the fund and exp of a guild raid's room and hidden for another, the first timer started
    /// when the bonus has rewards, the title and the clear time's name by win or loss, the party leave button shown for a party.
    /// </summary>
    void ShowResult()
    {
        if (rewardInfo == null) return;
        SetFrame(rewardInfo, "_dungeon");
        var time = Host.Child("m_timeMovie", rewardInfo);
        if (time != null)
            SetText(Host.Child("textField", time), ClientFormat.Format(Tables.Common(0x97e) ?? "", Seconds / 60, Seconds % 60));
        UpdateGuild(false);
        if (Bonus.HasRewards) timer1 = NowMs() + BonusDelayMs;
        string C(string key) => Tables.Common(key) ?? "";
        if (titleClip != null && Host.Child("m_name", titleClip) is { } name)
            Host.Set(name, "htmlText", C(Win ? "s_dungeon_reward_title_success" : "s_dungeon_reward_title_fail"));
        if (time != null)
            SetText(Host.Child("m_clearTimeName", time), C(Win ? "s_dungeon_reward_clear_time" : "s_dungeon_reward_fail_time"));
        if (Party && leaveButton != null) Host.SetVisible(leaveButton, true);
    }

    /// <summary>The guild reward block of FUN_140e22d70 and FUN_140e23690: shown with the fund and exp for a guild raid's room (the totals add the bonus), hidden for another.</summary>
    void UpdateGuild(bool withBonus)
    {
        var guild = Host.Child("m_rewardGuild", rewardInfo);
        if (guild == null) return;
        if (Room != null && Room.GroupType == GuildRaidGroup)
        {
            Host.SetVisible(guild, true);
            long fund = Main.GetReward(GuildFundKey) + (withBonus ? Bonus.GetReward(GuildFundKey) : 0);
            long exp = Main.GetReward(GuildExpKey) + (withBonus ? Bonus.GetReward(GuildExpKey) : 0);
            SetText(Host.Child("m_guildFund", guild), ((int)fund).ToString(System.Globalization.CultureInfo.InvariantCulture));
            SetText(Host.Child("m_guildExp", guild), ((int)exp).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        else Host.SetVisible(guild, false);
    }

    /// <summary>UpdateMinigameStage (@0x140e24e80): a round packet's reward clip: stage 1 the "_minigame" frame (FUN_140e24fc0), another stage "_minigame_stage" with setRound(round, stage) (SetMinigameRound @0x140e24ed0).</summary>
    void UpdateMinigameStage()
    {
        if (Type != 2) return;
        if (Stage == 1) { SetFrame(rewardInfo, "_minigame"); return; }
        if (rewardInfo == null) return;
        SetFrame(rewardInfo, "_minigame_stage");
        Host.Invoke(Host.Entity, "setRound", Round, Stage);
    }

    /// <summary>
    /// vfunc_72 (@0x140e21bd0, :347): once per frame. The first timer (500 ms after the result) starts the second (1000 ms; the massive event
    /// panel's notice in between is STILL-OPEN); the second timer shows the totals with the bonus (FUN_140e23690).
    /// </summary>
    public void Tick()
    {
        if (IsClosed || Host?.Entity == null) return;
        long now = NowMs();
        if (timer1 >= 0 && now >= timer1)
        {
            timer1 = -1;
            timer2 = now + TotalsDelayMs;
        }
        else if (timer2 >= 0 && now >= timer2)
        {
            timer2 = -1;
            ShowTotals();
        }
    }

    /// <summary>FUN_140e23690 (@0x140e23690, :643): the meso and exp with the bonus added, the guild block, both effects, and the bag with the main and the bonus items.</summary>
    void ShowTotals()
    {
        if (rewardInfo == null) return;
        SetTotals(Main.GetMeso() + Bonus.GetMeso(), Main.GetExp() + Bonus.GetExp());
        UpdateGuild(true);
        ShowEffects(true);
        FillBag(Main.Items, Bonus.Items);
    }

    /// <summary>OnCloseButton (@0x140e22740): with the movie loaded, RequestClose.</summary>
    public void OnCloseButton()
    {
        if (Host?.Entity == null) return;
        RequestClose();
    }

    /// <summary>RequestClose (@0x140e25790): a room that leaves after the reward sends DungeonReward 6, another plays the movie's close.</summary>
    public void RequestClose()
    {
        if (Room != null && Room.IsLeaveAfterCloseReward) Send(new DungeonRewardCloseRequest());
        else Host.Invoke(Host.Entity, "gotoAndPlay", "close");
    }

    /// <summary>OnPartyLeaveButton (@0x140e22780): with the movie loaded and a party result, Party mode 3 then DungeonReward 6.</summary>
    public void OnPartyLeaveButton()
    {
        if (Host?.Entity == null || !Party) return;
        Send(new PartyLeaveRequest());
        Send(new DungeonRewardCloseRequest());
    }

    /// <summary>vfunc_45 (@0x140e21cd0): "getTooltip" for m_bonusDetail is a text tooltip with the lines of the flags.</summary>
    public override IDictionary<string, object> GetTooltip(object[] args)
    {
        if (args == null || args.Length == 0 || Rt.ToString(args[0]) != "m_bonusDetail") return null;
        var t = new AsTooltip();
        return t.ApplyTextTooltip(Tip) ? t : null;
    }

    public override void Close()
    {
        base.Close();
        ReleaseBag();
    }

    // ---- helpers ----

    void SetText(object clip, string text)
    {
        if (clip == null) return;
        var field = Host.Has(clip, "htmlText") ? clip : Host.Child("textField", clip);
        if (field != null) Host.Set(field, "text", text);
    }

    void SetFrame(object clip, string label)
    {
        if (clip != null) Host.Invoke(clip, "gotoAndStop", label);              // vfunc_15
    }

    static AsDisplayObject Node(object o) => o as AsDisplayObject;
}
