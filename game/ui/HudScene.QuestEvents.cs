using Ms2.Ui.Hud;

namespace Ms2Godot.Ui;

/// <summary>The quest log's and the minimap's quest event slots (<see cref="QuestEventListeners"/>) on the live quest
/// book.</summary>
public partial class HudScene
{
    Action unwatchQuestEvents;

    readonly QuestEventListeners questListeners = new();

    void WatchQuestEvents()
    {
        if (unwatchQuestEvents != null || Ms2Godot.Net.LiveSession.Instance is not { } live) return;
        var l = questListeners;
        l.Data = id =>
        {
            try { return World.NpcTalk.ClientQuests(assets.Packs).Quest(id); }
            catch (Exception) { return null; }
        };
        l.QuestLog = () => questLogSide;
        l.Minimap = () => minimapSide;
        Action<int> started = l.Started, progress = l.Progress, rewarded = l.Rewarded, removed = l.Removed, tracking = l.Tracking;
        Action<int, int> result = l.Result;
        live.Quests.Started += started;
        live.Quests.ProgressUpdated += progress;
        live.Quests.Rewarded += rewarded;
        live.Quests.Removed += removed;
        live.Quests.TrackingChanged += tracking;
        live.Quests.ResultReceived += result;
        unwatchQuestEvents = () =>
        {
            live.Quests.Started -= started;
            live.Quests.ProgressUpdated -= progress;
            live.Quests.Rewarded -= rewarded;
            live.Quests.Removed -= removed;
            live.Quests.TrackingChanged -= tracking;
            live.Quests.ResultReceived -= result;
        };
    }

    void UnwatchQuestEvents()
    {
        unwatchQuestEvents?.Invoke();
        unwatchQuestEvents = null;
    }
}
