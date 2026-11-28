using System.Linq;
using Godot;

namespace Ms2Godot.Ui;

/// <summary>The scripted live combat run's hooks (MS2_LIVE_COMBAT_STEP, <see cref="Ms2Godot.Net.LiveSession.CombatRun"/>): each does what
/// a key press or a button click does, through the same handler.</summary>
public partial class HudScene
{
    /// <summary>The attack key pressed: the key bound to the attack action (0x49401) goes the way <see cref="CastKeys"/> sends it.
    /// True when the press gave a cast request.</summary>
    public bool AttackScripted()
    {
        if (levelUpGuide is { IsClosed: false }) levelUpGuide.Close();     // the level-up guide window opens over the field and hides the shots
        if (hudModel?.Keys is not { } keys) return false;
        for (int dik = 1; dik < 0xd4; dik++)
            if (keys.IsKeyBoundToAction(dik, 0x49401)) return CastKey(dik) == true;
        return false;
    }

    /// <summary>Whether the revival dialog or the auto-revive countdown is open (the own death's dialogs).</summary>
    public bool RevivalUiUp() => RevivalOpen || (autoRevive is { IsClosed: false });

    /// <summary>The open revival dialog's paid mode, its meso and merat costs and its two labels; null without the dialog.</summary>
    public string RevivalState() => RevivalOpen
        ? $"mode {revival.PaidMode}, meso cost {revival.MesoCost}, merat cost {revival.MeratCost}, labels \"{revival.Label1}\" / \"{revival.Label2}\""
        : null;

    /// <summary>The revival dialog's "onRevival(button)" callback, as its movie raises it. False without the dialog.</summary>
    public bool ReviveScripted(int button) => RevivalOpen && revival.OnCallback("onRevival", new object[] { button });

    /// <summary>The inventory's equip action on the bag item <paramref name="itemId"/> (the right click's RequestEquipItem(item, 0, 0)).
    /// False without the item or the inventory.</summary>
    public bool EquipScripted(int itemId, bool another = false)
    {
        var live = Ms2Godot.Net.LiveSession.Instance;
        var worn = another ? live?.Me.Equips?.Select(w => w.Uid).ToHashSet() : null;     // a second copy: the one not worn yet
        if (live?.Items.All.FirstOrDefault(e => e.Id == itemId && worn?.Contains(e.Uid) != true) is not { } e) return false;
        if (inventory == null || inventory.IsClosed) OpenInventory();
        if (inventory?.FindItem(e.Uid) is not { } item) return false;
        inventory.RequestEquipItem(item, 0, 0);
        if (inventory is { IsClosed: false }) inventory.Close();     // the window would cover the field in the shots
        return true;
    }

    /// <summary>The click on the bag cell of item <paramref name="itemId"/> as the movie raises it: notifySlotUse(slot, button), button 0 the
    /// double click, 1 the right click. Opens the inventory (and leaves it open). False without the item or the inventory.</summary>
    public bool SlotClickScripted(int itemId, int button)
    {
        if (inventory == null || inventory.IsClosed) OpenInventory();
        if (inventory?.SlotNameOfItem(itemId) is not { } name) return false;
        return inventory.OnCallback("notifySlotUse", new object[] { name, button });
    }

    /// <summary>The saved tutorial step (save id 110) set as the server's guide record would: the events up to it are cleared, the next start.</summary>
    public bool GuideStepScripted(int step)
    {
        if (Ms2.Ui.Guide.GuideManager.Current is not { } g) return false;
        g.ClearStates();     // the account's own record holds save id 1 (everything cleared)
        g.ReceiveStates(new Ms2.Net.Packets.GuideRecordIn(new[] { (110, step) }, 0));
        g.QueueOccurrence(Ms2.Ui.Guide.GuideOccurrence.GuideClear, 0);     // the open conditions are tried on an occurrence
        return true;
    }

    /// <summary>Opens the inventory (the guide's InventoryDialog occurrence) when it is shut; the character window through the same menu route.</summary>
    public bool OpenInventoryScripted() { if (inventory == null || inventory.IsClosed) OpenInventory(); return inventory is { IsClosed: false }; }
    public void CloseInventoryScripted() { if (inventory is { IsClosed: false }) inventory.Close(); }

    /// <summary>The quest log's give-up button for a quest: the log opened when it is shut, then its movie's onGiveUpQuest
    /// (GiveUpQuest, uiquestdialog.cpp:1792); whether the open log took it.</summary>
    public bool QuestLogGiveUpScripted(int questId)
    {
        if (questLog == null || questLogSide == null || questLogSide.IsClosed) OpenQuestLog();
        if (questLog?.Host == null || questLogSide is not { IsClosed: false }) return false;
        questLog.Player.External?.Invoke(questLog.Host.Id, new object[] { "onGiveUpQuest", questId });
        return true;
    }

    /// <summary>Whether the minimap's mark of a portal shows; null without the minimap or the mark.</summary>
    public bool? MinimapPortalMarkScripted(int portalId) => minimapSide?.PortalMarkShown(portalId);

    /// <summary>The minimap's portal marks (id and whether each shows), the xblock portals with no mark and the marked portals
    /// the xblock lacks; null without the minimap.</summary>
    public string MinimapPortalMarksScripted()
    {
        if (minimapSide == null) return null;
        var marked = minimapSide.Marks.Where(m => m.HasPortal).Select(m => m.PortalId).ToList();
        string shown = string.Join(" ", marked.Select(id => $"{id}:{(minimapSide.PortalMarkShown(id) is { } v ? (v ? "shown" : "hidden") : "no clip")}"));
        return $"marks [{shown}], xblock only [{string.Join(" ", minimapXblockPortals.Except(marked))}], " +
            $"not in the xblock [{string.Join(" ", marked.Except(minimapXblockPortals))}]";
    }

    /// <summary>The minimap's entity of an npc: its icon, frame and layer and whether it shows; null without the minimap or the entity.</summary>
    public string MinimapNpcMarkScripted(int objectId) =>
        minimapSide?.Npcs.FirstOrDefault(n => n.ObjectId == objectId) is { Clip: not null } n
            ? $"icon {n.Icon}, frame {n.State}, layer {n.Layer}, guide {n.ShowGuide}, quest target {n.QuestTarget}, shown {minimapSide.NpcMarkShown(objectId)}"
            : null;

    /// <summary>The minimap's entity of an interact object: its icon and layer and whether it shows; null without it.</summary>
    public string MinimapNamedMarkScripted(string name) =>
        minimapSide?.NamedMark(name) is { } m ? $"icon {m.Icon}, layer {m.Layer}, shown {minimapSide.NamedMarkShown(name)}" : null;

    /// <summary>The minimap's forced minimize: the controller's, the movie's m_forceMinimize and its min/max button; null
    /// without the minimap.</summary>
    public string MinimapForceScripted() => minimapSide is { } c
        ? $"forced {c.ForceMinimize}, movie {c.MovieForceState()?.ToString() ?? "none"}, image {c.ImageName ?? "none"}, minimapInvisible {c.MinimapInvisible}"
        : null;

    /// <summary>The character window's avatar: its angles (m_kAngleBase) and its drawn front's turn from the camera (+x)
    /// toward the screen's right (-z), in degrees; null before the view is made.</summary>
    public (float X, float Y, float Z, float Yaw)? MyInfoAvatarScripted()
    {
        if (myInfo?.Avatar is not { } a) return null;
        for (int i = 0; i < GetChildCount(); i++)
            if (GetChild(i) is AvatarView { CopyBasis: Basis b })
            {
                var f = b * Vector3.Back;
                var (x, y, z) = a.Angle;
                return (x, y, z, Mathf.RadToDeg(Mathf.Atan2(-f.Z, f.X)));
            }
        return null;
    }

    /// <summary>A window point on the character window's m_mouseArea its hit test reaches; null when shut.</summary>
    public Vector2? MyInfoAvatarPointScripted() =>
        myInfoMovie?.Host.Entity is Ms2.Avm2.Flash.AsDisplayObject root && Descendant(myInfoMovie.Host, root, "m_mouseArea") is { } area
            ? WindowPoint(myInfoMovie, area) : null;

    /// <summary><see cref="SendMouse"/> with the left or right button.</summary>
    public void SendMouseScripted(Vector2 at, bool? press, bool right) => SendMouse(at, press, right ? MouseButton.Right : MouseButton.Left);

    /// <summary>Shuts every dialog, then opens one of the everyday windows the way its menu button does (inventory: the guide's
    /// InventoryDialog occurrence, quest: the quest log toggle, sysmenu: the system menu); whether it is open. A live shot of the
    /// same window in two builds compares how the whole swf UI draws.</summary>
    public bool OpenWindowScripted(string name)
    {
        CloseAllDialogs();
        switch (name)
        {
            case "inventory": return OpenInventoryScripted();
            case "quest": ToggleQuestLog(); return questLogSide is { IsClosed: false };
            case "sysmenu": OpenSystemMenu(); return systemMenu is { IsClosed: false };
            case "achieve": OpenAchieve(); return AchieveOpen;
            case "fame": OpenFamePointBoard(); return FamePointBoardOpen;
            case "famemenu": FamePointBoardMenu(); return FamePointBoardOpen;    // the game menu entry: s_fame_point_board_warn2 without a tab
        }
        return false;
    }

    /// <summary>The open trophy window switched to the category row <paramref name="categoryId"/> as its tree's click does
    /// (onTabChange of the row's index and id); what the page then shows, for the log. Null when the window is shut or has no
    /// such row.</summary>
    public string AchieveTabScripted(int categoryId)
    {
        if (!AchieveOpen || achieve.Host?.Entity == null) return null;
        var rows = achieveCategories.GetRows();
        int index = rows.FindIndex(r => r.Id == categoryId);
        if (index < 0) return null;
        achieve.OnCallback("onTabChange", new object[] { (double)index, (double)categoryId });
        var h = achieve.Host;
        string name = achieveTexts.TableString(0x4a, categoryId);
        if (rows[index].Tab == 1)
            return $"{name}: the summary, score {achieve.Book.Sum} ({string.Join("/", achieve.Book.Tally)})";
        if (h.Child("m_heroTrophy") is not { } hero || h.Child("m_list", hero) is not { } list) return $"{name}: no list";
        var dp = h.Get(list, "dataProvider");
        int n = Ms2.Avm2.Rt.ToInt32(h.Get(dp, "length"));
        var opts = new Ms2.Ui.Hud.AchieveController.FilterOptions
        {
            Survival = rows[index].Tab == 2, CategoryTag = rows[index].CategoryTag, ContentsTag = rows[index].ContentsTag, Filter = 4,
        };
        var shown = achieve.Book.GetListFromTable(0, 0, false, false).Where(a => achieve.RowFilter(a, "", opts)).ToList();
        var started = shown.FirstOrDefault(a => a.State == 2 && a.IsStarted) ?? shown.FirstOrDefault();
        string first = started != null && achieve.Row(started) is { } row
            ? $", first {row["name"]} {(row.TryGetValue("cur", out var cur) ? cur : "-")}/{(row.TryGetValue("max", out var max) ? max : "-")}"
            : "";
        return $"{name}: {n} rows, {shown.Count} by the filter{first}";
    }

    /// <summary>The open fame point board switched to the tab of <paramref name="alliance"/> as the tab bar's click does
    /// (selectedIndex, whose INDEX_CHANGE calls onChangeTab); what the page then shows, for the log. Null when the board is
    /// shut or has no such tab.</summary>
    public string FameTabScripted(int alliance)
    {
        if (!FamePointBoardOpen || famePointBoard.Host?.Entity is null) return null;
        int index = famePointBoard.Tabs.ToList().IndexOf(alliance);
        var h = famePointBoard.Host;
        if (index < 0 || h.Child("m_tab") is not { } bar) return null;
        h.Set(bar, "selectedIndex", index);
        string Text(string name) => h.Child(name) is { } t ? Ms2.Avm2.Rt.ToString(h.Get(t, "text")) : "-";
        var contents = h.Child("m_fameLog") is { } log ? h.Get(log, "m_contents") : null;
        int lines = contents == null ? -1 : Ms2.Avm2.Rt.ToInt32(h.Get(contents, "numChildren"));
        var (cur, max, point) = famePointBoard.Fame.Progress(alliance);
        return $"shown {famePointBoard.Shown}: {Text("m_allianceName")} / {Text("m_allianceGrade")}, fame {cur}/{max} {point}, {lines} log lines";
    }

    /// <summary>The player's trophy <paramref name="id"/> as the book has it, for the log.</summary>
    public string AchieveStateScripted(int id) =>
        Ms2Godot.Net.LiveSession.Instance?.Achieves.Find(id) is { } a
            ? $"state {a.State} grade {a.CurGrade} start {a.StartGrade} count {a.Count} rewards waiting {a.RewardCount}"
            : "none";

    /// <summary>
    /// A left click on the chat link of trophy <paramref name="id"/> grade <paramref name="grade"/>, as the messenger movie's
    /// showTooltipTextLinkEvent runs it (the runtime sends no TextEvent.LINK for html event: links yet): the link the trophy
    /// notices make (AchieveLink), its text after "event:" split on ",", and getUserData "getAchieve" with fields 1, 3, 4
    /// and 5 on the first chat window. Then the trophy window's getTooltipAchieve(id, grade), the window opened when it is
    /// shut. Id 0 takes the book's first trophy with a recorded grade (complete) and <paramref name="grade"/> -1 its first
    /// grade without one (in progress). For the log.
    /// </summary>
    public string AchieveLinkScripted(int id, int grade)
    {
        if (Ms2Godot.Net.LiveSession.Instance?.Achieves is not { Notices: { } notices } book) return "no trophy notices";
        if (id == 0)
        {
            var pick = book.GetListFromTable(0, 0, false, false).Where(a => a.Data != null)
                .SelectMany(a => a.Data.GradeConditions.Keys.OrderBy(g => g).Select(g => (a, g)))
                .FirstOrDefault(x => grade < 0 ? x.a.State == 2 && x.a.GetValue(x.g) == 0 : x.a.GetValue(x.g) != 0);
            if (pick.a == null) return "no such trophy in the book";
            (id, grade) = (pick.a.Id, pick.g);
        }
        string link = notices.AchieveLink(id, grade);
        int at = link.IndexOf("event:", System.StringComparison.Ordinal), end = at < 0 ? -1 : link.IndexOf('\'', at);
        if (end < 0) return $"trophy {id} grade {grade}: no link ({link})";
        var f = link[(at + 6)..end].Split(',');
        if (messengers.Count == 0 || f.Length < 6) return $"trophy {id} grade {grade}: no chat window or fields ({link})";
        static string Show(System.Collections.Generic.IDictionary<string, object> t) => t == null ? "null" : string.Join(" | ",
            new[] { "title", "grade", "desc", "reward", "state", "closeButton", "comment", "imgPath" }.Where(t.ContainsKey).Select(k => $"{k}={t[k]}"));
        var chat = messengers[0].Side.GetUserData("getAchieve", new object[] { f[1], f[3], f[4], f[5] });
        if (!AchieveOpen) OpenAchieve();
        var window = AchieveOpen ? achieve.GetUserValue("getTooltipAchieve", new object[] { (double)id, (double)grade }) as System.Collections.Generic.IDictionary<string, object> : null;
        return $"trophy {id} grade {grade}: link {link} -> chat tooltip {Show(chat)}; window tooltip {Show(window)}";
    }

    /// <summary>The trophy window's reward button of trophy <paramref name="id"/> (onReward); the window opened when it is shut.
    /// Whether the request went out.</summary>
    public bool AchieveRewardScripted(int id)
    {
        if (!AchieveOpen) OpenAchieve();
        return AchieveOpen && achieve.OnCallback("onReward", new object[] { (double)id });
    }

    /// <summary>The open inventory switched to a tab (<see cref="Ms2.Ui.Hud.InventoryController.ShowTab"/>); whether it shows it.</summary>
    public bool InventoryTabScripted(int tab) => inventory is { IsClosed: false } inv && inv.ShowTab(tab);

    /// <summary>Opens the character window through the menu's route (OpenMyInfo); whether it is up afterwards.</summary>
    public bool OpenCharScripted() { OpenMyInfo(); return myInfoMovie != null; }

    /// <summary>The guide event's state for the run: 0 idle, 1 opening, 2 open (GuideEntity m_nState), 3 cleared; -1 without
    /// the guide or the event.</summary>
    public int GuideEventScripted(int id)
    {
        if (Ms2.Ui.Guide.GuideManager.Current is not { } g || g.FindEntity(id) is not { } e) return -1;
        return g.IsCleared(id) ? 3 : e.State;
    }

    /// <summary>The guide's running events and held limits, for the log.</summary>
    public string GuideStateText() => Ms2.Ui.Guide.GuideManager.Current?.DescribeState() ?? "no guide manager";

    /// <summary>The worn item <paramref name="itemId"/> taken off: the inventory view's unequip request, as the character window's
    /// take-off sends it. False when it is not worn.</summary>
    public bool UnequipScripted(int itemId)
    {
        var live = Ms2Godot.Net.LiveSession.Instance;
        if (live?.Me.Equips?.FirstOrDefault(e => e.Id == itemId) is not { } worn || live.Inventory?.RequestUnequip == null) return false;
        live.Inventory.RequestUnequip(worn.Uid);
        return true;
    }

    /// <summary>The player turned to face <paramref name="target"/> (a world position) on the flat, as a turn before a press.
    /// Returns the distance to it, or -1 without a player.</summary>
    public float FaceScripted(Vector3 target)
    {
        if (player == null) return -1;
        var d = target - player.GlobalPosition; d.Y = 0;
        player.Face(d);
        return d.Length();
    }

    /// <summary>The own cast of skill <paramref name="skillId"/> through the request and route a quick slot click takes
    /// (<see cref="CastOwn"/>), without the slot. True when the request was not refused.</summary>
    public bool CastSkillScripted(uint skillId)
    {
        var w = QuickSlotWorld();
        var held = w.Weapon is { } h ? weaponActions.FindAction(h.Right, h.Left) : null;
        if (w.Tables.GetSkillData(skillId) is { } sd)
            GD.Print($"live: combat skill {skillId} weapons {string.Join(' ', sd.Weapons.Select(x => $"R{x.RightHand}/L{x.LeftHand}"))}, held R{w.Weapon?.Right}/L{w.Weapon?.Left}");
        if (RequestOwn(w, skillId, held, 0) is not { } r) return false;
        RouteOwn(r);
        return true;
    }

    /// <summary>The quick slots' cooldown answer for skill <paramref name="skillId"/>: each slot holding it, whether the slot
    /// shows the skill's running cooldown (<see cref="Ms2.Ui.Skills.SkillSlotState.NotifyCooldownStart"/>, the check that runs
    /// the slot's onCooldownStart) with its time left and total; the same check without a slot when none holds it.</summary>
    public string CooldownScripted(uint skillId)
    {
        var w = QuickSlotWorld();
        if (w.Tables.GetSkillData(skillId) is not { } sd) return "no skill data";
        string One(Ms2.Ui.Skills.SkillSlotState s) =>
            s.NotifyCooldownStart(sd.CooldownKey, out float remain, out float total) ? $"shown {remain:F2}/{total:F2}" : "not shown";
        var parts = new System.Collections.Generic.List<string> { $"useCoolDownShowUI {sd.Condition.UseCoolDownShowUI}" };
        for (int i = 0; playerInfo != null && i < Ms2.Ui.Hud.MainPlayerInfoController.SlotCount; i++)
            if (playerInfo.ShownEntry(i) is var e && e.SkillId == (int)skillId)
                parts.Add($"slot {i}: {One(new Ms2.Ui.Skills.SkillSlotState(w, (uint)e.SkillId, e.ItemId, e.ItemUid))}");
        if (parts.Count == 1) parts.Add($"no slot holds it; without a slot: {One(new Ms2.Ui.Skills.SkillSlotState(w, skillId, 0, 0))}");
        return string.Join(", ", parts);
    }

    /// <summary>Harness: the learning quest dialog no longer opens by itself (as with its game option on), so a shot of the
    /// screen centre is not covered by the next new learning quest. True when the dialog object exists.</summary>
    public bool LearningAutoOpenOffScripted() { learningAutoOpenOff = true; return learningQuest != null; }

    /// <summary>The Escape key pressed, through the dialog key handler. True when a dialog took it.</summary>
    public bool EscScripted() =>
        DialogKeys(new InputEventKey { Keycode = Key.Escape, PhysicalKeycode = Key.Escape, Pressed = true, Echo = false });

    /// <summary>Harness: the player's move input driven by the script (x right, y forward, camera relative); false without a player.</summary>
    public bool DriveScripted(Vector2 input, bool walk)
    {
        if (player == null) return false;
        player.ScriptDriven = true; player.Input2 = input; player.WalkMode = walk;
        return true;
    }

    /// <summary>Harness: the camera-relative move input (the inverse of Player.WorldDir) that runs the player toward
    /// <paramref name="target"/> (a world position) on the flat. Returns the flat distance, or -1 without a player.</summary>
    public float DriveTowardScripted(Vector3 target, bool walk)
    {
        if (player == null) return -1;
        var d = target - player.GlobalPosition; d.Y = 0;
        float len = d.Length();
        if (len < 1f) { DriveScripted(Vector2.Zero, walk); return len; }
        d /= len;
        var fwd = new Vector3(-Mathf.Sin(player.CameraYaw), 0, -Mathf.Cos(player.CameraYaw));
        var right = new Vector3(-fwd.Z, 0, fwd.X);
        DriveScripted(new Vector2(d.Dot(right), d.Dot(fwd)), walk);
        return len;
    }

    /// <summary>Harness: gives the player back to the keyboard.</summary>
    public void DriveEnd() { if (player != null) { player.Input2 = Vector2.Zero; player.ScriptDriven = false; } }
}
