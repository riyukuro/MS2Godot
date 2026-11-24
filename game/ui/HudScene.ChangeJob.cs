using Godot;
using Ms2.Ui;
using Ms2.Ui.Hud;

namespace Ms2Godot.Ui;

/// <summary>
/// The job change window (UIChangeJobDialog 0x4f6b, <see cref="ChangeJobDialogController"/>) and the game menu's "jobchange"
/// entry, which the menu lists only while UIChangeJobDialog::CanChangeJob passes (gameeventmanagercli.cpp:964).
/// </summary>
public partial class HudScene
{
    ChangeJobDialogController changeJob;
    MovieDialog changeJobMovie;
    Ms2.Ui.Skills.ChangeJobTable changeJobs;

    /// <summary>CChangeJobTable, loaded once (feature 0xca and the rows' features).</summary>
    Ms2.Ui.Skills.ChangeJobTable ChangeJobs() => changeJobs ??= Ms2.Ui.Skills.ChangeJobTable.Load(assets.Packs, hudModel.IsFeatureEnabled, hudModel.Tables);

    /// <summary>The local player's job system (PC::GetJobSystem): the live book, else the fixture's.</summary>
    Ms2.Ui.Skills.JobSystem LocalJobs()
    {
        var live = Ms2Godot.Net.LiveSession.Instance;
        var tables = hudModel.Tables.Skills;
        var book = live != null ? live.SkillBook : UiFixture.NewCharacterSkillBook(tables.Jobs, hudModel.Player.SubJob / 10);
        var jobs = new Ms2.Ui.Skills.JobSystem(tables) { IsFeatureEnabled = hudModel.IsFeatureEnabled };
        if (book != null) jobs.Read(book);
        return jobs;
    }

    /// <summary>UIChangeJobDialog::CanChangeJob @0x140bd88e0 for the local player.</summary>
    bool CanChangeJob()
    {
        var live = Ms2Godot.Net.LiveSession.Instance;
        if (live != null && live.SkillBook == null) return false;
        var jobs = LocalJobs();
        return ChangeJobDialogController.CanChangeJob(ChangeJobs(), hudModel.Tables.Skills.Jobs, hudModel.Tables.ChangeJobLevel,
            hudModel.HasLocalPlayer, jobs.JobGroupId, jobs.SubJob, hudModel.Player.Level, id => live?.Quests.Get(id)?.State ?? 0);
    }

    /// <summary>The menu's m_isEnabled functions this HUD answers (the rest answer no, see <see cref="GameMenuController.EnabledBy"/>).</summary>
    bool GameMenuEnabledBy(Ms2.Ui.Hud.GameMenuDef def) => def.EnabledBy == "UIChangeJobDialog::CanChangeJob" && CanChangeJob();

    /// <summary>The sub job's start quest, for the menu's FUN_14193b4a0.</summary>
    int ChangeJobStartQuest() => hudModel.HasLocalPlayer ? ChangeJobs().GetStartQuestId(LocalJobs().SubJob) : 0;

    Action unwatchChangeJob;

    /// <summary>The game menu's UIQuestEventListener slot 1 (vfunc_o178_1): UIQuestEventDispatcher::vfunc_1 (@0x141aa88e0)
    /// sends a started quest of type 3, 4, 5, 7 or 10 to slots 8 to 12 instead.</summary>
    void WatchChangeJob()
    {
        if (unwatchChangeJob != null || Ms2Godot.Net.LiveSession.Instance is not { } live) return;
        Action<int> onStarted = id =>
        {
            if (AbilityQuestTable().Quest(id)?.Type is 3 or 4 or 5 or 7 or 10) return;
            gameMenu?.OnQuestStarted(id);
        };
        live.Quests.Started += onStarted;
        // OnJob mode 2 for the local player (scenestategame.cpp:7489-7497): with feature 0xca, UIChangeJobEffect when the new
        // sub job is a job change's target
        Action<int> onJob = mode =>
        {
            if (mode != 2 || !hudModel.IsFeatureEnabled(0xca)) return;
            int sub = LocalJobs().SubJob;
            if (ChangeJobs().IsChangeTarget(sub)) ShowChangeJobEffect(sub);
        };
        live.SkillBookChanged += onJob;
        unwatchChangeJob = () => { live.Quests.Started -= onStarted; live.SkillBookChanged -= onJob; };
    }

    MovieDialog changeJobEffectMovie;

    /// <summary>UIChangeJobEffect::vfunc_2: the movie loads (again) and plays; its onHide at the last frame hides it.</summary>
    void ShowChangeJobEffect(int subJob)
    {
        if (changeJobEffectMovie != null) { changeJobEffectMovie.Host.Unload(); changeJobEffectMovie.QueueFree(); changeJobEffectMovie = null; }
        var c = new ChangeJobEffectController { SubJob = () => subJob };
        MovieDialog dlg = null;
        // the movie's own onHide call is still running: the unload waits for the frame's end
        c.Hidden += () => Callable.From(() =>
        {
            if (dlg == null || !GodotObject.IsInstanceValid(dlg)) return;
            dlg.Host.Unload(); dlg.QueueFree();
            if (changeJobEffectMovie == dlg) changeJobEffectMovie = null;
            GD.Print("change job: effect hidden");
        }).CallDeferred();
        changeJobEffectMovie = dlg = new MovieDialog(assets, c, hudModel, ScreenMode.PixelSize(GetWindow()));
        uiRoot.AddChild(dlg);
        GD.Print($"change job: effect for sub job {subJob}");
    }

    bool ChangeJobOpen => changeJobMovie != null && changeJob?.Host?.Entity != null;

    /// <summary>CUIService::ToggleDialog(0x4f6b).</summary>
    void ToggleChangeJob()
    {
        if (ChangeJobOpen) { changeJob.Close(); return; }
        if (GuideRefusesOpen("ChangeJobDialog")) return;
        if (changeJobMovie != null) { changeJobMovie.Host.Unload(); changeJobMovie.QueueFree(); changeJobMovie = null; }
        var jobs = LocalJobs();
        var tipPlayer = SkillTooltipPlayer(jobs);
        var c = changeJob = new ChangeJobDialogController
        {
            Table = ChangeJobs(),
            SubJob = () => jobs.SubJob,
            SkillData = id => hudModel.Tables.Skills.GetSkillData(id),
            // ASTooltip(local player, 0x10, 2).FUN_1409e1c50(id, 1, 1) (unit_140bd7e10_uichangejobdialog.cpp FUN_140bd8040)
            SkillTooltip = id => { tipPlayer.Level = hudModel.Player.Level; return Ms2.Ui.Skills.SkillTooltip.Build(hudModel.Tables, tipPlayer, id, 1, 0x10, true); },
        };
        c.JobChangeNotice += quest => GD.Print($"change job: the start quest {quest}'s talk (BeginTalkWithoutNpc) is not ported");
        c.Unloaded += () => { changeJobMovie?.Host.Unload(); changeJobMovie?.QueueFree(); changeJobMovie = null; };
        changeJobMovie = new MovieDialog(assets, c, hudModel, ScreenMode.PixelSize(GetWindow()));
        uiRoot.AddChild(changeJobMovie);
        GD.Print($"change job: open for sub job {jobs.SubJob} ({(c.Table.Get(jobs.SubJob) is { } d ? $"to {d.ChangeSubJobCode}, {d.Skills1.Count}+{d.Skills2.Count} skills" : "no row")})");
    }

    // ---- the scripted live run's hooks (MS2_LIVE_COMBAT_STEP jobchange): the menu's click and the window's button go the
    // movie's way, through OnCallback

    /// <summary>Whether the game menu's last build listed the jobchange entry (54), and CanChangeJob now.</summary>
    public string ChangeJobMenuState() => gameMenu == null ? "no menu"
        : $"listed {gameMenu.Listed.Contains(54)}, CanChangeJob {CanChangeJob()}, start quest {ChangeJobStartQuest()}";

    /// <summary>The menu's jobchange click (the movie's callback with the entry's name).</summary>
    public bool ChangeJobMenuClickScripted() => gameMenu != null && gameMenu.OnCallback("jobchange", System.Array.Empty<object>());

    /// <summary>The window: open, its sub job and the movie's job name text; null when closed.</summary>
    public string ChangeJobWindowState()
    {
        if (!ChangeJobOpen || changeJob.IsClosed) return null;
        var h = changeJob.Host;
        string name = h.Child("m_content") is { } content && h.Get(h.Get(content, "m_jobName"), "textField") is { } tf
            ? Ms2.Avm2.Rt.ToString(h.Get(tf, "text")) : "?";
        return $"sub job {changeJob.JobCode}, name \"{name}\"";
    }

    /// <summary>The window's change button (onChangeJob).</summary>
    public bool ChangeJobPressScripted() => ChangeJobOpen && changeJob.OnCallback("onChangeJob", System.Array.Empty<object>());

    /// <summary>Whether the job change effect's movie is up.</summary>
    public bool ChangeJobEffectUp => changeJobEffectMovie != null && GodotObject.IsInstanceValid(changeJobEffectMovie);
}
