using Ms2.Avm2;
using Ms2.Quest;

namespace Ms2.Ui.Hud;

/// <summary>
/// UICharacterAbilityDialog (UI id 0x4f0c, ms2/client/ms2/uicharacterabilitydialog.cpp:330-675): the game menu's
/// character ability dialog. From level 50 with feature 0x2d and CharacterAbilityOpenQuestID rewarded, it lists the
/// abilities of each category (up to five) in required-level order with their learned / locked state, the points left,
/// and the reset button (a meso reset box, or the cooldown left).
/// </summary>
/// <remarks>
/// The learned abilities, the reset time and the extra points are CCharacterAbilityManager's (<see cref="CharacterAbilityData"/>,
/// filled by the CharacterAbility packet); the session applies it and calls <see cref="BuildAbilityTable"/> and
/// <see cref="OnAbilityLearned"/>.
/// STILL-OPEN: onAddButton dereferences GetAbility's result without a check; an id the table lacks is refused here.
/// STILL-OPEN: OnAbilityLearned passes LearnAbility the effect id, which the movie compares with the renderers' ability
/// ids, so the learn animation plays only where both ids are the same; it is ported as the client does it.
/// </remarks>
public sealed class CharacterAbilityController : UiController, IMessageBoxListener
{
    public override string TypeName => "CharacterAbilityDialog";     // GetTypeName (@0x140f2a330)
    public override string Movie => "uicharacterabilitydialog";
    protected override string EntityMethod => "buildAbility";

    /// <summary>The common string ids the dialog reads (commonstringtable.cpp:4757-4779).</summary>
    public const int NoPointId = 0xc57, NoMesoId = 0xc58, PointInfoId = 0xc5a, MesoResetDescId = 0xc5b, MesoResetButtonId = 0xc5c,
        CoolingId = 0xc5d, DisabledQuestId = 0xc60;
    /// <summary>The key of common string 0xc58 (FUN_141275260(0xc58, 1, 1), a system notice).</summary>
    public const string NoMesoKey = "s_character_ability_err_no_meso";

    public CharacterAbilityTable Table = new();
    public CharacterAbilityData Data = new();
    /// <summary>CQuestManagerCommon::IsQuestRewarded.</summary>
    public Func<int, bool> IsQuestRewarded = _ => false;
    /// <summary>GetServerTime, in seconds.</summary>
    public Func<long> ServerTime = () => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    /// <summary>GetQuestData, GetQuestName and GetChapterName for the disabled text.</summary>
    public Func<int, QuestData> Quest = _ => null;
    public Func<int, string> QuestName = _ => "";
    public Func<int, string> ChapterName = _ => "";

    /// <summary>CharacterAbility mode 2 with the ability id (<see cref="Ms2.Net.Packets.CharacterAbilityLearnRequest"/>).</summary>
    public event Action<int> SendLearn;
    /// <summary>CharacterAbility mode 1 (<see cref="Ms2.Net.Packets.CharacterAbilityResetRequest"/>).</summary>
    public event Action SendReset;
    /// <summary>FUN_141275260(id, 1, 1): a system notice of a common string, by its key.</summary>
    public event Action<string> SystemNotice;
    /// <summary>FindGameMenuDialog(0x4e28)->FUN_141938ae0(0x4f0c, n): the game menu's notify count of this dialog.</summary>
    public event Action<int> MenuNotifyCount;

    /// <summary>ShowMessageBox / FUN_140d73c60 (show a box) and FUN_14151f200 (the next box); hooks for tests.</summary>
    public Action<MessageBoxData> ShowBox = MessageBoxQueue.Show;
    public Action NextBox = MessageBoxQueue.Next;

    bool built;                 // m_bBuilt
    ulong? lastTick;            // m_timer (Init in the ctor, then Check(1000))

    uint DefaultPoint => Model.Tables.CharacterAbilityDefaultPoint;
    int OpenQuest => (int)Model.Tables.CharacterAbilityOpenQuestID;
    int? Level => Model.HasLocalPlayer ? Pc.Level : null;

    /// <summary>FUN_1401e0800(level): feature 0x2d and the level reached <see cref="CharacterAbilityData.StartLevel"/>.</summary>
    bool IsOpen(ushort level) => CharacterAbilityData.IsOpen(Model.IsFeatureEnabled(Feature.CharacterAbility), level);

    /// <summary>
    /// vfunc_72 (@0x140f2a4a0): nothing while closed or before the table is built; else once a second (m_timer.Check(1000),
    /// which re-arms) the reset button (<see cref="UpdateCoolButton"/>). The timer is armed on the first call.
    /// </summary>
    public void Process(ulong nowMs)
    {
        lastTick ??= nowMs;
        if (IsClosed || Host?.Entity == null || !built) return;
        if (nowMs - lastTick.Value < 1000) return;
        lastTick = nowMs;
        UpdateCoolButton();
    }

    /// <summary>
    /// OnLoadComplete (@0x140f2a510): UIDialog's, the close button (BindCloseButton("m_close")), not built, the disabled
    /// text; then with a local player, the abilities open (<see cref="IsOpen"/>) and the open quest rewarded: the game
    /// menu's count cleared and the table built.
    /// </summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        BindCloseButton("m_close");
        built = false;
        UpdateDisabledText();
        if (!Model.HasLocalPlayer) return;
        if (!IsOpen((ushort)Pc.Level)) return;
        if (!IsQuestRewarded(OpenQuest)) return;
        MenuNotifyCount?.Invoke(0);
        BuildAbilityTable();
    }

    /// <summary>BindCloseButton (uidialog.cpp:977): the child's click handler (ASButton::AddClickHandler) runs vfunc_70,
    /// the close.</summary>
    void BindCloseButton(string name)
    {
        if (Host.Child(name) is not { } b) { Host.RT.NoteMissing("character ability dialog: " + name); return; }
        Func<object[], object> f = _ => { Close(); return null; };
        Host.Invoke(b, "addEventListener", "buttonClick", f, false, 0, false);
    }

    /// <summary>
    /// vfunc_28 (@0x140f2a620), after UIDialog's, with a local player:
    /// <list type="bullet">
    /// <item>onAddButton: no point left, box 0xc57 (handled); else with an argument (PopUInt) nothing for a learned
    /// ability or one above the level (handled), else mode 2 with the id.</item>
    /// <item>onResetButton: while cooling, box 0xc5d; else the yes / cancel box 0xc5b with the cooldown's days and the
    /// meso, Enter off, buttons 0xc5c / cancel, tag "OnMesoReset" (handled).</item>
    /// </list>
    /// Common string 0xc5d is s_character_ability_merat_reset_desc, the client's id (the cooldown error would be 0xc5f).
    /// </summary>
    public override bool OnCallback(string name, object[] args)
    {
        if (!Model.HasLocalPlayer) return false;
        if (name.Equals("onAddButton", StringComparison.OrdinalIgnoreCase))
        {
            if (Data.Remain(Level, DefaultPoint) == 0)
            {
                ShowBox(MessageBoxQueue.Create(0, Model.Tables.Common(NoPointId), null, ""));
                return true;
            }
            if (args is { Length: > 0 })
            {
                int id = (int)Rt.ToUint32(args[0]);
                if (Data.IsLearned(id)) return true;
                var a = Table.GetAbility(id);
                if (a == null || a.RequireLevel > (ushort)Pc.Level) return true;
                SendLearn?.Invoke(id);
            }
        }
        else if (name.Equals("onResetButton", StringComparison.OrdinalIgnoreCase))
        {
            if (Data.CooldownLeft(ServerTime(), Model.Tables.CharacterAbilityResetCoolTimeMinute) > 0)
                ShowBox(MessageBoxQueue.Create(0, Model.Tables.Common(CoolingId), null, ""));
            else
            {
                long days = (uint)(Model.Tables.CharacterAbilityResetCoolTimeMinute * 60) / 86400;    // CTimeSpan::GetDays
                string text = ClientFormat.Format(Model.Tables.Common(MesoResetDescId), days, Model.Tables.CharacterAbilityResetMeso);
                var box = MessageBoxQueue.Create(1, text, this, "OnMesoReset");
                box.EnterAllowed = false;                                   // FUN_141a024a0(0)
                box.SetButtonLabelIds(MesoResetButtonId, 0x476, 0);
                ShowBox(box);
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// vfunc_49 (@0x140f2abc0), after UIObject's, with a local player: "ability" with the id: the ability's effect at its
    /// level as a buff tooltip (FUN_1409e2750(skill, 5, 0)), none for an unknown ability or effect; "CategoryIcon" with the
    /// id: the category's tooltip text.
    /// </summary>
    public override IDictionary<string, object> GetTooltip(object[] args)
    {
        if (!Model.HasLocalPlayer || args is not { Length: > 0 }) return null;
        string kind = Rt.ToString(args[0]);
        int id = args.Length > 1 ? (int)Rt.ToUint32(args[1]) : 0;
        if (kind == "ability")
        {
            var a = Table.GetAbility(id);
            if (a == null) return null;
            var skill = Model.Tables.Additional.GetLevel((uint)a.AdditionalEffectId, a.AdditionalEffectLevel);
            return skill == null ? null : BuffTooltip.Build(Model.Tables, skill, 5, 0);
        }
        if (kind == "CategoryIcon")
        {
            var c = Table.GetCategory(id);
            return c == null ? null : AsTooltip.Text(c.Tooltip);
        }
        return null;
    }

    /// <summary>
    /// OnMessage (@0x140f2ae70) from a message box: on yes (0) to "OnMesoReset", too few mesos (DAT_143ab2898 below
    /// CharacterAbilityResetMeso) shows the notice 0xc58 and stops there (the next box is not shown); else mode 1. Then the
    /// next box. Handled either way.
    /// </summary>
    public bool OnMessageBox(MessageBoxController sender, int button, string tag)
    {
        if ((tag ?? "").StartsWith("OnMesoReset", StringComparison.Ordinal) && button == 0)
        {
            if (Pc.Mesos < Model.Tables.CharacterAbilityResetMeso)
            {
                SystemNotice?.Invoke(NoMesoKey);
                return true;
            }
            SendReset?.Invoke();
        }
        NextBox();
        return true;
    }

    /// <summary>
    /// OnChangeMyLevel (@0x140f2b000, NotifyChangeMyLevel, registered by vfunc_65 while the dialog exists): with the
    /// abilities open at the new level and the open quest rewarded, the game menu's count (<see cref="NewAbilityAt"/>);
    /// then, loaded and not closed, the table rebuilt.
    /// </summary>
    public void OnChangeMyLevel(ushort level)
    {
        if (!IsOpen(level)) return;
        if (!IsQuestRewarded(OpenQuest)) return;
        MenuNotifyCount?.Invoke(NewAbilityAt(Table, Data, level) ? 1 : 0);
        if (Host?.Entity == null || IsClosed) return;
        BuildAbilityTable();
    }

    /// <summary>FUN_140f2bee0 (@0x140f2bee0): an ability of the table (GetAbilities' order) that opens at exactly
    /// <paramref name="level"/> and is not learned.</summary>
    public static bool NewAbilityAt(CharacterAbilityTable table, CharacterAbilityData data, ushort level)
    {
        foreach (var a in table.Abilities)
            if (a.RequireLevel == level && !data.IsLearned(a.Id)) return true;
        return false;
    }

    /// <summary>OnAbilityLearned (@0x140f2b110): the movie's LearnAbility(category, effect id), then the point text.</summary>
    public void OnAbilityLearned(CharacterAbilityTable.Ability a)
    {
        if (Host?.Entity != null) Host.Invoke("LearnAbility", a.CategoryId, a.AdditionalEffectId);
        UpdatePointText();
    }

    /// <summary>
    /// BuildAbilityTable (@0x140f2b1e0): nothing before the movie loads or without categories. Each category (GetCategories'
    /// order): buildCategory(id, name); one without abilities is skipped. Its abilities by required level (a CRBMap SetAt
    /// in the category map's order, so of two at one level the later stays), ascending; each with an effect at its level:
    /// id, iconPath, requireLevel, disableAbility (FUN_140f2a100), learnAbility, newAbility false; then buildAbility(id,
    /// the list). Then the point text, the reset button, built.
    /// </summary>
    public void BuildAbilityTable()
    {
        if (Host?.Entity == null) return;
        if (Table.Categories.Count == 0) return;
        foreach (var cat in Table.Categories)
        {
            Host.Invoke("buildCategory", cat.Id, cat.Name ?? "");
            if (cat.Abilities.Count == 0) continue;
            var byLevel = new SortedDictionary<ushort, CharacterAbilityTable.Ability>();
            foreach (var a in cat.Abilities) byLevel[a.RequireLevel] = a;
            var arr = new List<object>();
            foreach (var a in byLevel.Values)
            {
                var skill = Model.Tables.Additional.GetLevel((uint)a.AdditionalEffectId, a.AdditionalEffectLevel);
                if (skill == null) continue;
                arr.Add(Host.Obj(
                    ("id", (uint)a.Id), ("iconPath", skill.Icon ?? ""), ("requireLevel", a.RequireLevel),
                    ("disableAbility", IsDisabled(a.Id) ? 1 : 0), ("learnAbility", Data.IsLearned(a.Id) ? 1 : 0),
                    ("newAbility", false)));
            }
            Host.Invoke("buildAbility", cat.Id, arr);
        }
        UpdatePointText();
        UpdateCoolButton();
        built = true;
    }

    /// <summary>FUN_140f2a100: locked: no table row, no local player, or a required level above the player's.</summary>
    bool IsDisabled(int id)
    {
        var a = Table.GetAbility(id);
        if (a == null || !Model.HasLocalPlayer) return true;
        return a.RequireLevel > (ushort)Pc.Level;
    }

    /// <summary>FUN_140f2b760 (@0x140f2b760): SetPointInfoText of 0xc5a with the points left (as an unsigned) and the total.</summary>
    void UpdatePointText()
    {
        if (Host?.Entity == null) return;
        uint total = Data.Total(Level, DefaultPoint);
        uint remain = (uint)Data.Remain(Level, DefaultPoint);
        Host.Invoke("SetPointInfoText", (object)ClientFormat.Format(Model.Tables.Common(PointInfoId), remain, total));
    }

    /// <summary>
    /// FUN_140f2b850 (@0x140f2b850): SetRemainCoolButton(cooling, the time left, any learned): while cooling the time left
    /// as days and hours, the minutes only under a day and the seconds only under an hour.
    /// </summary>
    void UpdateCoolButton()
    {
        if (Host?.Entity == null) return;
        long remain = Data.CooldownLeft(ServerTime(), Model.Tables.CharacterAbilityResetCoolTimeMinute);
        bool cooling = false;
        string text = "";
        if (remain > 0)
        {
            cooling = true;
            long days = remain / 86400, hours = remain / 3600 % 24;
            text = BuffTooltip.FormatTimeSpan(Model.Tables, remain, true, true, days == 0, hours == 0);
        }
        Host.Invoke("SetRemainCoolButton", cooling, text, Data.Any);
    }

    /// <summary>
    /// UpdateDisabledText (@0x140f2ba20): the open quest's data, nothing without it; the unlock text from string table 0x50
    /// (specialQuestType), else by the quest type: 0 epic 0x5dd, 1 world 0x5de, 2 event 0x5df, 6 s_quest_guild_quest,
    /// 8 s_quest_field_quest, 9 s_quest_alliance_quest; then SetDisabledText of 0xc60 with the quest's name, its required
    /// level, the unlock text and the chapter's name.
    /// </summary>
    void UpdateDisabledText()
    {
        if (Host?.Entity == null) return;
        int id = OpenQuest;
        var quest = Quest(id);
        if (quest == null) return;
        string unlock = Model.Tables.StringTableText(0x50, id);
        if (unlock.Length == 0)
        {
            unlock = quest.Type switch
            {
                0 => Model.Tables.Common(0x5dd),
                1 => Model.Tables.Common(0x5de),
                2 => Model.Tables.Common(0x5df),
                6 => Model.Tables.Common("s_quest_guild_quest") ?? "",
                8 => Model.Tables.Common("s_quest_field_quest") ?? "",
                9 => Model.Tables.Common("s_quest_alliance_quest") ?? "",
                _ => unlock,
            };
        }
        string text = ClientFormat.Format(Model.Tables.Common(DisabledQuestId), QuestName(id) ?? "", (ushort)quest.Require.Level,
            unlock, ChapterName(quest.Chapter) ?? "");
        Host.Invoke("SetDisabledText", (object)text);
    }
}
