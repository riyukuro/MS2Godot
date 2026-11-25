using Ms2.Quest;

namespace Ms2.Ui.Hud;

/// <summary>
/// UIRewardNotify (ms2/client/ms2/uirewardnotify.cpp, "RewardNotify", UI 0x4e5d, movie uirewardnotify): the toasts the
/// movie plays one after the other. Every toast is an object handed to the movie's appendReward (AppendReward
/// @0x141ac1fe0); it is kept back while the movie is not loaded, while the sub scene is 0x6200031 or while the toasts are
/// blocked (m_block), and handed over by vfunc_15's frame check once none of those holds. This port has the merat and red
/// merat notices (ShowMeratNotice @0x141ac0860, ShowMeratRedNotice @0x141ac0990) and the movie's two callbacks
/// (vfunc_28 @0x141abe8b0).
/// </summary>
public sealed class RewardNotifyController : UiController
{
    public const int UiId = 0x4e5d;
    /// <summary>The sub scene id AppendReward and vfunc_15 hold the toasts back in.</summary>
    public const int HeldSubScene = 0x6200031;

    public override string Movie => "uirewardnotify";
    protected override string EntityMethod => "appendReward";
    protected override bool IsDialog => false;          // a UISwfEntity (flag 0x1f in the ctor), not a UIDialog
    public override string TypeName => "RewardNotify";   // GetTypeName @0x141abe240

    readonly List<object> queue = new();                // m_queue

    /// <summary>m_block: BlockToasts (@0x141abf1f0) sets it, UnblockToasts (@0x141abf210) clears it, vfunc_2 clears it on load.</summary>
    public bool Block;

    /// <summary>GFxSceneService::GetSubSceneId.</summary>
    public Func<int> SubSceneId;

    /// <summary>PlaySystemSound(key, "").</summary>
    public Action<string> Sound;

    /// <summary>CUIService::OpenDialog(id): the "onSkillLevelUp" callback opens 0x4f2d.</summary>
    public event Action<int> OpenDialog;

    /// <summary>Toasts waiting in m_queue.</summary>
    public int Pending => queue.Count;

    bool Held => Host?.Entity == null || (SubSceneId?.Invoke() ?? 0) == HeldSubScene || Block;

    /// <summary>AppendReward (@0x141ac1fe0): queue the object while held, else appendReward on the entity.</summary>
    public void AppendReward(object reward)
    {
        if (Held) queue.Add(reward);
        else Host.Invoke("appendReward", reward);
    }

    /// <summary>vfunc_15 (@0x141abe740), each frame: hand over what was kept back once nothing holds it.</summary>
    public void Update()
    {
        if (queue.Count == 0 || Held) return;
        var q = queue.ToArray();
        queue.Clear();
        foreach (var r in q) AppendReward(r);
    }

    /// <summary>The bottom bar heights and margin FUN_1418dd0d0 sums (uibuiltindialog.cpp:53), as PopupNpcTalk keeps them.</summary>
    public int BottomBarHeight = 21, AdventureBarHeight = 21, ScreenMargin = 193;

    /// <summary>FUN_1418dd0d0: (DAT_1439f8ccc - 2) + the adventure bar's height - 2 with feature 0xbd (FUN_1418dd110) + DAT_1439f8cc8.</summary>
    public int BottomInset
        => BottomBarHeight - 2 + (Model.IsFeatureEnabled(MainPlayerInfoController.AdventureLevelFeature) ? AdventureBarHeight - 2 : 0) + ScreenMargin;

    /// <summary>vfunc_43 (@0x141abe680, uirewardnotify.cpp:198): the entity aligned bottom centre (mode 2) with dx 8 and dy
    /// -(FUN_1418dd0d0 + 0x2c), on the stage's width and height whatever the window. Not UIDialog's (this is a UISwfEntity): the
    /// movie's own "alignment" is not called.</summary>
    public override void Layout(double stageW, double stageH)
    {
        if (Host?.Entity == null) return;
        Host.Align(Host.Entity, 2, 8, -(BottomInset + 0x2c));
    }

    /// <summary>ShowMeratNotice (@0x141ac0860): msg = FormatStringId(0x685, value), type "merat".</summary>
    public void ShowMeratNotice(long value) => Show(value, "merat");

    /// <summary>ShowMeratRedNotice (@0x141ac0990): the same message, type "merat_red".</summary>
    public void ShowMeratRedNotice(long value) => Show(value, "merat_red");

    /// <summary>FormatStringId(0x685, nValue) with the raw __int64: fmt prints it as plain decimal digits, no separators.</summary>
    public string MeratMessage(long value) => ClientFormat.Format(Model.Tables.Common(0x685), value);

    void Show(long value, string type) =>
        AppendReward(Host.Obj(("msg", MeratMessage(value)), ("type", type)));

    string Fmt(int id, params object[] args) => ClientFormat.Format(Model.Tables.Common(id), args);
    string Res(string key) => Model.Tables.CommonResource(key);

    /// <summary>FUN_141abf230 (@0x141abf230): msg 0x681 (exp), or 0x682 with the bonus; type "exp".</summary>
    public void ShowExp(long exp, long bonus) =>
        AppendReward(Host.Obj(("msg", bonus > 0 ? Fmt(0x682, AchieveTexts.Group(exp), bonus) : Fmt(0x681, AchieveTexts.Group(exp))), ("type", "exp")));

    /// <summary>FUN_141abf450 (@0x141abf450): msg 0x683, or 0x684 with the bonus; type "icon", the meso icon.</summary>
    public void ShowMoney(long money, long bonus) =>
        AppendReward(Host.Obj(("msg", bonus > 0 ? Fmt(0x684, AchieveTexts.Group(money), bonus) : Fmt(0x683, AchieveTexts.Group(money))),
            ("type", "icon"), ("icon", Res("meso_icon_path"))));

    /// <summary>FUN_141abf6e0 / FUN_141abfa10: an item toast (msg 0x687 with the grade's text colour and the item name).</summary>
    public void ShowItem(int itemId, int grade, uint textColor, string name, string icon, bool piece) =>
        AppendReward(Host.Obj(("type", "icon"), ("msg", Fmt(0x687, textColor, name)), ("icon", icon), ("grade", grade),
            ("itemPiece", piece), ("key", (uint)itemId)));

    /// <summary>The item data of an id (CItemDataTable::GetItemData); null uses the model's table.</summary>
    public Func<int, Ms2.Ui.Items.ItemData> ItemOf;

    /// <summary>FUN_141abf6e0 (uirewardnotify.cpp), the reactor reward (OnReactor mode 4): nothing without the item's data;
    /// else msg 0x687 with GetTextColor(grade) and the item name, the item's GetIconPath, the grade, IsPieceItem and the id.</summary>
    public void ShowItemReward(int itemId, int grade)
    {
        if ((ItemOf ?? Model.Tables.Items.Get)(itemId) is not { } d) return;
        ShowItem(itemId, grade, Model.Tables.Items.TextColor(grade), Model.Tables.ItemName(itemId), d.IconPath, d.IsPieceItem);
    }

    /// <summary>FUN_141ac1d10 (uirewardnotify.cpp:612), OnItemScript mode 4: nothing without the item's data; else type "boxOpen",
    /// nextType "cashCoupon", the item's GetIconPath and its name (CStringTable::FUN_141811980(1, id)).</summary>
    public void ShowCashCoupon(int itemId)
    {
        if ((ItemOf ?? Model.Tables.Items.Get)(itemId) is not { } d) return;
        AppendReward(Host.Obj(("type", "boxOpen"), ("nextType", "cashCoupon"), ("icon", d.IconPath), ("msg", Model.Tables.ItemName(itemId))));
    }

    /// <summary>FUN_141abfa10 (uirewardnotify.cpp:358), a picked-up drop's toast: nothing for the special ids (FUN_14053f900, the EP
    /// and SP balls), else as <see cref="ShowItemReward"/>. STILL-OPEN: the name of a UGC, score or pet item (CItem::FUN_140527b10)
    /// is the table name here.</summary>
    public void ShowPickedItem(int itemId, int grade)
    {
        if (Ms2.Formats.World.DropPickup.IsSpecialId(itemId)) return;
        ShowItemReward(itemId, grade);
    }

    /// <summary>FUN_141abfdc0 (@0x141abfdc0): the achievement toast; emblem when the achievement's type is 9.</summary>
    public void ShowAchieve(string name, string icon, int achieveType) =>
        AppendReward(Host.Obj(("type", "achieve"), ("msg", name), ("icon", icon), ("emblem", achieveType == 9)));

    /// <summary>FUN_141abffa0 (@0x141abffa0): OnNotifyLearnEmotion; msg 0x85b, icon grade 1.</summary>
    public void ShowLearnEmotion(string emotionName, string icon) =>
        AppendReward(Host.Obj(("type", "icon"), ("msg", Fmt(0x85b, emotionName)), ("icon", icon), ("grade", 1)));

    /// <summary>The skill data of an id (SkillDataTableCli::GetSkillData); null uses the model's table.</summary>
    public Func<uint, Ms2.Ui.Skills.SkillData> Skill;

    /// <summary>OnNotifyLearnEmotion (@0x141abea10) to FUN_141abffa0: the skill data of the id; nothing unless it is an emotion;
    /// the toast carries the skill's name and icon.</summary>
    public void OnLearnEmotion(int emotionId)
    {
        if ((Skill ?? Model.Tables.Skills.GetSkillData)((uint)emotionId) is not { IsEmotion: true } d) return;
        ShowLearnEmotion(Model.Tables.SkillName((uint)emotionId), d.Icon);
    }

    /// <summary>
    /// FUN_141ac0210 (@0x141ac0210, uirewardnotify.cpp:417): the player's job system's learnt skills without the sub skills
    /// (GetSkills(list, 1, true)); a skill with no skill data is passed over; each changed one gets a learn_skill toast;
    /// PC_SkillGet plays once when any did. OnLevelUp (scenestategame.cpp:9551) and OnJob (:7466-7528) call it.
    /// </summary>
    /// <returns>The number of toasts.</returns>
    public int ShowLearnSkills(Ms2.Ui.Skills.JobSystem jobs)
    {
        if (jobs == null) return 0;
        int n = 0;
        foreach (var s in jobs.GetSkills(true, true))
        {
            if ((Skill ?? Model.Tables.Skills.GetSkillData)(s.Id) is not { } d) continue;
            if (!s.Changed) continue;
            n++;
            ShowLearnSkill(Model.Tables.SkillName(s.Id), d.IsPassive, d.Icon);
        }
        if (n > 0) Sound?.Invoke("PC_SkillGet");
        return n;
    }

    /// <summary>FUN_141ac0210 (@0x141ac0210), one learned skill: msg 0x804, skillType, skillIconPath; the caller plays PC_SkillGet once.</summary>
    public void ShowLearnSkill(string skillName, bool skillType, string icon) =>
        AppendReward(Host.Obj(("type", "learn_skill"), ("msg", Fmt(0x804, skillName)), ("skillType", skillType ? 1 : 0), ("skillIconPath", icon)));

    /// <summary>FUN_141ac0ac0 (@0x141ac0ac0): the title toast; plays PC_TitleGet, msg 0x689, the title icon.</summary>
    public void ShowTitle(string titleName)
    {
        Sound?.Invoke("PC_TitleGet");
        AppendReward(Host.Obj(("msg", Fmt(0x689, titleName)), ("type", "icon"), ("icon", Res("title_icon_path"))));
    }

    /// <summary>
    /// CChatEmoticonCli::FUN_140a39360's tail (chatemoticoncli.cpp:221-239), ChatStamp mode 2 for this window
    /// (FindRewardNotify(0x4e5d)): nothing without the item's data (the assert, :222-226); else the common string
    /// s_msg_chat_emoticon_add_notify_message formatted with the item's name (FUN_14028dfa0, CStringTable 1), its value
    /// words replaced (CReservedWordTable::ReplaceValueWords), shown as an icon notice with the item's icon and effect 1
    /// (<see cref="ShowIconNotice(string, string, int)"/>, FUN_141ac1580). Returns that text, which the caller passes to
    /// ShowSystemNotice(text, 0, 1); null when nothing was shown.
    /// </summary>
    public string OnChatEmoticonAdded(int itemId)
    {
        if ((ItemOf ?? Model.Tables.Items.Get)(itemId) is not { } d) return null;
        string msg = ClientFormat.Format(Model.Tables.Common("s_msg_chat_emoticon_add_notify_message"), Model.Tables.ItemName(itemId));
        msg = Model.Tables.ReservedWords.ReplaceValueWords(msg, Model.HasLocalPlayer ? Model.Player.Name : null);
        ShowIconNotice(d.IconPath, msg, 1);
        return msg;
    }

    /// <summary>ShowIconNotice (@0x141ac0cd0).</summary>
    public void ShowIconNotice(string msg, string icon) =>
        AppendReward(Host.Obj(("msg", msg), ("type", "icon"), ("icon", icon)));

    /// <summary>FUN_141ac1580 (@0x141ac1580): an icon notice with an effect id.</summary>
    public void ShowIconNotice(string icon, string msg, int effect) =>
        AppendReward(Host.Obj(("msg", msg), ("type", "icon"), ("icon", icon), ("effect", effect)));

    /// <summary>FUN_141ac0e50 (@0x141ac0e50): a caught fish, msg 0xbd8; bigFish when the size class is 3.</summary>
    public void ShowFishingSuccess(int fishId, string name, string icon, uint length, int sizeClass, int medal) =>
        AppendReward(Host.Obj(("msg", Fmt(0xbd8, name)), ("type", "fishing_success"), ("icon", icon), ("size", length + "cm"),
            ("success", 1), ("bigFish", sizeClass == 3), ("medal", medal), ("key", (uint)fishId)));

    /// <summary>FUN_141ac11b0 (@0x141ac11b0): a fish that got away, msg 0xbd9.</summary>
    public void ShowFishingFail(int fishId, string name, string icon, uint length, int medal) =>
        AppendReward(Host.Obj(("msg", Fmt(0xbd9, name)), ("type", "fishing_fail"), ("icon", icon), ("size", length + "cm"),
            ("success", 0), ("medal", medal), ("key", (uint)fishId)));

    /// <summary>ShowSkillLevelUpNotice (@0x141ac14b0).</summary>
    public void ShowSkillLevelUp() => AppendReward(Host.Obj(("type", "skillLevelUp")));

    /// <summary>ShowStampNotice (@0x141ac1730).</summary>
    public void ShowStamp(string msg, int special, string tag) =>
        AppendReward(Host.Obj(("msg", msg), ("type", "stamp"), ("isSpecial", special), ("eventTag", tag)));

    /// <summary>NotifySkillBookDialog (@0x141ac18e0): OnSkillBookDialog(open) on the entity when it is loaded.</summary>
    /// <remarks>The client's notify exists from the start, so a book opened before this movie's load still reaches it;
    /// here the movie loads on its first use, so the last state is kept and handed over by <see cref="OnLoadComplete"/>.</remarks>
    public void NotifySkillBookDialog(bool open)
    {
        skillBookOpen = open;
        if (Host?.Entity != null) Host.Invoke("OnSkillBookDialog", open);
    }

    bool skillBookOpen;

    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        if (skillBookOpen && Host?.Entity != null) Host.Invoke("OnSkillBookDialog", true);
    }

    /// <summary>FUN_141ac1950 (@0x141ac1950): skill points; kind 2 uses the sub-1 text and the second icon.</summary>
    public void ShowSkillPoint(int points, int kind) =>
        AppendReward(Host.Obj(("type", "icon"),
            ("msg", ClientFormat.Format(Model.Tables.Common(kind == 2 ? "s_reward_notify_skill_point_sub_1" : "s_reward_notify_skill_point"), points)),
            ("icon", Res(kind == 2 ? "skill_point2_icon_path" : "skill_point_icon_path"))));

    /// <summary>FUN_141ac1b50 (@0x141ac1b50): stat points.</summary>
    public void ShowStatPoint(int points) =>
        AppendReward(Host.Obj(("type", "icon"), ("msg", ClientFormat.Format(Model.Tables.Common("s_reward_notify_stat_point"), points)),
            ("icon", Res("stat_point_icon_path"))));

    /// <summary>FUN_141ac1d10 (@0x141ac1d10): a box opening that continues as a cash coupon toast.</summary>
    public void ShowBoxOpen(string itemName, string icon) =>
        AppendReward(Host.Obj(("type", "boxOpen"), ("nextType", "cashCoupon"), ("icon", icon), ("msg", itemName)));

    /// <summary>OnNotifyChangedTokenMoney (@uirewardnotify.cpp:253): nothing unless count &gt; 0. Kinds 3-6 use strings 0x68c-0x68f
    /// with their token icons, kinds 9-13 the s_system_achieve_reward_* keys.</summary>
    public void NotifyChangedTokenMoney(int kind, long count)
    {
        if (count <= 0) return;
        string msg, icon;
        switch (kind)
        {
            case 3: msg = Fmt(0x68c, count); icon = Res("honor_token_icon_path"); break;
            case 4: msg = Fmt(0x68d, count); icon = Res("karma_token_icon_path"); break;
            case 5: msg = Fmt(0x68e, count); icon = Res("lu_token_icon_path"); break;
            case 6: msg = Fmt(0x68f, count); icon = Res("habi_token_icon_path"); break;
            case 9: msg = Key("s_system_achieve_reward_reverse_coin", count); icon = Res("reverse_coin_icon_path"); break;
            case 10: msg = Key("s_system_achieve_reward_mentor_token", count); icon = Res("mentor_token_icon_path"); break;
            case 11: msg = Key("s_system_achieve_reward_mentee_token", count); icon = Res("mentee_token_icon_path"); break;
            case 12: msg = Key("s_system_achieve_reward_star_point", count); icon = Res("star_point_icon_path"); break;
            case 13: msg = Key("s_system_achieve_reward_meso_market_token", count); icon = Res("meso_market_token_icon_path"); break;
            default: return;
        }
        ShowIconNotice(msg, icon);
    }

    string Key(string key, long count) => ClientFormat.Format(Model.Tables.Common(key), count);

    /// <summary>vfunc_28: "onReward" with type "achieve" plays system_Trophy_Hero; "onSkillLevelUp" opens 0x4f2d.</summary>
    public override bool OnCallback(string name, object[] args)
    {
        if (string.Equals(name, "onReward", StringComparison.OrdinalIgnoreCase))
        {
            if (args is { Length: > 0 } && args[0] as string == "achieve") Sound?.Invoke("system_Trophy_Hero");
            return true;
        }
        if (string.Equals(name, "onSkillLevelUp", StringComparison.OrdinalIgnoreCase)) OpenDialog?.Invoke(0x4f2d);
        return false;
    }

    /// <summary>The movie's rewardComplete (RewardComplete @0x141ac1f40).</summary>
    public void RewardComplete() { if (Host?.Entity != null) Host.Invoke("rewardComplete"); }
}
