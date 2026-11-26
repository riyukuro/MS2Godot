using Ms2.Quest;

namespace Ms2.Ui;

/// <summary>The UIRewardNotify (0x4e5d) toasts the trophy notices use; <see cref="Hud.RewardNotifyController"/> is one.</summary>
public interface IAchieveToasts
{
    /// <summary>FUN_141abfdc0: the trophy toast.</summary>
    void ShowAchieve(string name, string icon, int achieveType);
    /// <summary>FUN_141abf6e0: an item reward.</summary>
    void ShowItemReward(int itemId, int grade);
    /// <summary>FUN_141ac1b50: stat points.</summary>
    void ShowStatPoint(int points);
    /// <summary>FUN_141ac1950: skill points (kind 2 the sub-1 points).</summary>
    void ShowSkillPoint(int points, int kind);
    /// <summary>FUN_141ac1580: an icon notice with an effect id.</summary>
    void ShowIconNotice(string icon, string msg, int effect);
}

/// <summary>
/// The notices of CAchieveManager::OnPacket mode 2 (achievemanager.cpp:525-556): NotifyClear (@0x140936ec0) on a new grade,
/// NotifyReward (@0x1409372b0) when a reward was taken, and the main player info's progress line
/// (UIMainPlayerInfo::FUN_1419f0bb0, uimainplayerinfo.cpp:888). The HUD gives the toasts, the chat log, the guide and the
/// local player through the members.
/// </summary>
public sealed class AchieveNotices
{
    AchieveTexts texts;
    readonly Func<AchieveTexts> makeTexts;

    /// <summary>The trophy texts; made by the factory on the first notice that reads them.</summary>
    public AchieveTexts Texts => texts ??= makeTexts();

    public AchieveNotices(AchieveTexts texts) => this.texts = texts;

    /// <summary>Notices whose texts are made on first use (the first mode 2 update), not when the HUD loads.</summary>
    public AchieveNotices(Func<AchieveTexts> makeTexts) => this.makeTexts = makeTexts;

    /// <summary>FindRewardNotify(0x4e5d): the HUD's reward notify, null when there is none.</summary>
    public Func<IAchieveToasts> Toasts = () => null;
    /// <summary>FUN_1412751b0(text, kind): a system line of the chat log.</summary>
    public Action<string, int> Line = (_, _) => { };
    /// <summary>CGuideManager::AddTrigger(type, value, 0).</summary>
    public Action<int, int> Trigger = (_, _) => { };
    /// <summary>CGuideManager::IsRestricted(0x16): the tutorial holds the progress lines back.</summary>
    public Func<bool> ProgressRestricted = () => false;
    /// <summary>The local player's name (PCBase::vfunc_77), null when there is no local player.</summary>
    public Func<string> PlayerName = () => null;
    /// <summary>SkillDataTableCli::GetSkillData(code)'s icon, null without the skill's data. NotifyReward passes it through
    /// FUN_14016a730(m_strIcon, 0), the narrow to wide text conversion, so the path is the icon as it is.</summary>
    public Func<uint, string> SkillIcon = _ => null;
    /// <summary>UIMainPlayerInfo::AppendMessage (@0x1419f0ef0): FindMainPlayerInfo(0x4e5f) and its movie's
    /// appendMessage(message, type); nothing without the window.</summary>
    public Action<string, string> AppendMessage = (_, _) => { };
    /// <summary>FindMainPlayerInfo(0x4e5f) is not null: OnPacket calls FUN_1419f0bb0 only then, so without the window the
    /// trophy's last line percentage stays.</summary>
    public Func<bool> HasPlayerInfo = () => true;
    /// <summary>NotifySystem::Post(NotifyClearAchieve(id)): the clear's listeners (UIDynamicActionDialog::FUN_14192c660
    /// refreshes; CQuestManager::FUN_1416ca780 marks the available quests dirty, which the port computes on its own).</summary>
    public event Action<int> Cleared;

    /// <summary>
    /// NotifyClear (@0x140936ec0): the grade cleared (the current one when the last grade is complete, state 3, else the one
    /// before it, at least 1). Nothing more for a type 8 trophy; else the trophy toast (its name, its icon, the emblem for
    /// type 9), the guide's CanAchieveReward trigger (0x17) when the grade has a reward, the system line
    /// s_achieve_get_achieve_hero_complete (state 3) or _progress with the trophy's chat link, and the clear post.
    /// </summary>
    public void NotifyClear(Achieve a)
    {
        if (a == null) return;
        int grade = a.State != 3 ? a.CurGrade - 1 : a.CurGrade;
        if (grade < 1) grade = 1;
        if (a.Data.Type == 8) return;
        Toasts()?.ShowAchieve(Texts.Name(a.Id), a.Data.IconPath, a.Data.Type);
        // pData->GetGrade(nGrade)->m_reward: the grade is the trophy's own; a missing grade reads as no reward here
        if (a.Data.Grade(grade) is { Reward.Type: > 0 }) Trigger(0x17, 0);
        string link = AchieveLink(a.Id, grade);
        Line(Texts.CommonF(a.State == 3 ? 0x77d : 0x77c, link), Hud.TalkKind.SystemNotice);
        Cleared?.Invoke(a.Id);
    }

    /// <summary>
    /// FUN_140d2d440 (chatlinkhelpers.cpp): the trophy's chat link, "" without the player's trophy. With a local player the
    /// link carries s_achieve_tooltip_user_info of the player's name and s_achieve_state_complete (the last grade complete,
    /// or a grade below the current one) or s_achieve_state_progress, and the grade's recorded date. FUN_140d2d8a0 makes
    /// the href 'event:achieveTooltip,{id},4,{grade},{text},{date}' (CHATLINK_ACHIEVE) over the grade's title in white.
    /// </summary>
    public string AchieveLink(int id, int grade)
    {
        if (Texts.Find(id) is not { } a) return "";
        string text = "", date = "";
        if (PlayerName() is { } name)
        {
            int kind = a.State switch { 3 => 3, 2 => a.CurGrade > grade ? 3 : 2, _ => 2 };
            date = AchieveTexts.RecordedDate(a, grade);
            text = ClientFormat.Format(Texts.CommonByKey("s_achieve_tooltip_user_info"), name, Texts.Common(kind == 3 ? 0x780 : 0x77f));
        }
        string href = ClientFormat.Format("'event:achieveTooltip,{0},{1},{2},{3},{4}'", (ulong)(uint)id, 4, grade, text, date);
        return Hud.MessengerBrowser.LinkMarkup(href, "#ffffff", Texts.GradeTitle(id, grade));
    }

    const string EtcImage = "./Data/Resource/Image/ETC/";

    /// <summary>
    /// NotifyReward (@0x1409372b0), the reward of <paramref name="grade"/> taken: nothing without the trophy's data, the grade
    /// or the reward notify. A special reward row gives its own icon notice and the s_system_achieve_reward_auth_notify_manual
    /// line of its text; else by the reward type: an item's toast (1); stat points (3) and skill points (4) with their lines; a
    /// shop's buy authority (5 weapon, 6 build, 7 ride) with the item's name; with feature 0xbb a palette (8: one notice and line per colour of
    /// the palette tied to this trophy and grade, by the colour's name, string table 0x48), a beauty item by the local player's gender (10/11)
    /// and a dynamic action (12) with the skill's icon; with feature 0xbc a beauty item (9), where no local player ends the
    /// notice before the trigger. Last the guide's AchieveReward trigger (3) of the reward type.
    /// </summary>
    public void NotifyReward(int id, int grade)
    {
        if (Texts.Data(id) is not { } d || d.Grade(grade) is not { } g) return;
        if (Toasts() is not { } toasts) return;
        var r = g.Reward;
        if (Texts.SpecialNotify(id, grade, out var icon, out var msg))
        {
            toasts.ShowIconNotice(icon, msg, 0);
            Line(Texts.CommonKeyF("s_system_achieve_reward_auth_notify_manual", Texts.RewardText(id, grade)), Hud.TalkKind.Auth);
        }
        else
        {
            switch (r.Type)
            {
                case 1:
                    toasts.ShowItemReward((int)r.Code, r.Rank);
                    break;
                case 3:
                    toasts.ShowStatPoint((int)r.Value);
                    Line(Texts.CommonKeyF("s_reward_notify_stat_point", r.Value), Hud.TalkKind.SystemNotice);
                    break;
                case 4:
                    toasts.ShowSkillPoint((int)r.Value, r.SubJob);
                    Line(Texts.CommonKeyF(r.SubJob == 2 ? "s_reward_notify_skill_point_sub_1" : "s_reward_notify_skill_point", r.Value),
                         Hud.TalkKind.SystemNotice);
                    break;
                case 5: Shop(toasts, "weaponshop_unlock.png", r.Code); break;
                case 6: Shop(toasts, "buildshop_unlock.png", r.Code); break;
                case 7: Shop(toasts, "rideshop_unlock.png", r.Code); break;
            }
            if (Texts.FeatureOn(0xbb))
            {
                switch (r.Type)
                {
                    case 8:
                        foreach (var c in Texts.RewardColors(id, grade, r.Code))
                            Unlock(toasts, EtcImage + "colorpalette_unlock.png", "s_system_achieve_reward_coloring",
                                   "s_system_achieve_reward_coloring_auth_notify", Texts.TableString(0x48, c.StringKey));
                        break;
                    case 10:
                    case 11:
                        if (Texts.Gender() is { } gender10) Beauty(toasts, r, gender10);
                        break;
                    case 12:
                        if (SkillIcon(r.Code) is { } skillIcon)
                            Unlock(toasts, skillIcon, "s_system_achieve_reward_dynamicaction", "s_system_achieve_reward_dynamicaction_auth_notify",
                                   Texts.TableString(4, r.Code));
                        break;
                }
            }
            if (Texts.FeatureOn(0xbc) && r.Type == 9)
            {
                if (Texts.Gender() is not { } gender9) return;
                Beauty(toasts, r, gender9);
            }
        }
        Trigger(3, r.Type);
    }

    /// <summary>An unlock: the icon notice (effect 0) of the notice key and the line (kind 0x1c) of the line key, both of the name.</summary>
    void Unlock(IAchieveToasts toasts, string icon, string noticeKey, string lineKey, string name)
    {
        toasts.ShowIconNotice(icon, Texts.CommonKeyF(noticeKey, name), 0);
        Line(Texts.CommonKeyF(lineKey, name), Hud.TalkKind.Auth);
    }

    /// <summary>A shop's buy authority: the shop's image under Image/ETC and the item's name (string table 1).</summary>
    void Shop(IAchieveToasts toasts, string image, uint code) =>
        Unlock(toasts, EtcImage + image, "s_system_achieve_reward_shop_unlock", "s_system_achieve_reward_shop_auth_notify", Texts.TableString(1, code));

    /// <summary>The beauty reward: the reward's extra value of the gender ("male" for 0, else "female"), nothing without it.</summary>
    void Beauty(IAchieveToasts toasts, AchieveReward r, int gender)
    {
        if (!r.Extra.TryGetValue(gender == 0 ? "male" : "female", out var v)) return;
        Unlock(toasts, EtcImage + "customizeshop_unlock.png", "s_system_achieve_reward_beauty", "s_system_achieve_reward_beauty_auth_notify",
               Texts.TableString(1, AchieveTexts.ParseUInt(v)));
    }

    /// <summary>
    /// UIMainPlayerInfo::FUN_1419f0bb0 (uimainplayerinfo.cpp:888), with the main player info only: nothing while the guide
    /// holds ProgressAchieve (0x16) or unless the trophy is in progress (state 2). The percentage is the count over the
    /// current grade's goal; from 99 it is s_achieve_progress_notify_float of the name and the percentage each time, below it
    /// s_achieve_progress_notify_integer of the whole percentage when it rose by the trophy's noticePercent or more since the
    /// last line (and is at least 1), which then becomes the last line's percentage.
    /// </summary>
    public void Progress(Achieve a)
    {
        if (!HasPlayerInfo() || ProgressRestricted() || a.State != 2) return;
        float percent = (float)a.Count / (float)a.GradeGoal(a.CurGrade) * 100.0f;
        if (percent >= 99.0f)
        {
            AppendMessage(Texts.CommonKeyF("s_achieve_progress_notify_float", Texts.Name(a.Id), percent), "achieve");
            return;
        }
        int step = (int)((float)(int)percent - a.LastNotifiedPercent);
        if ((float)step >= (float)a.Data.NoticePercent && percent >= 1.0f)
        {
            a.LastNotifiedPercent = percent;
            AppendMessage(Texts.CommonKeyF("s_achieve_progress_notify_integer", Texts.Name(a.Id), (int)percent), "achieve");
        }
    }
}
