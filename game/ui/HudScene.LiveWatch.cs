using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Ms2.Ui;

namespace Ms2Godot.Ui;

/// <summary>
/// The live player watch's subscriptions as a check counts them (res://tests/hud_once.tscn and the live step hudonce): each
/// live event <see cref="WatchLivePlayer"/> and <see cref="HudModel"/> subscribe to must reach this HUD once per model the
/// HUD keeps, and no handler may hold a model the HUD no longer uses (one BuildHud, one HUD model).
/// </summary>
public partial class HudScene
{
    const BindingFlags EventField = BindingFlags.Instance | BindingFlags.NonPublic;

    static Delegate[] Handlers(object owner, string evt) =>
        (owner?.GetType().GetField(evt, EventField)?.GetValue(owner) as Delegate)?.GetInvocationList() ?? Array.Empty<Delegate>();

    /// <summary>The model a <see cref="WatchLivePlayer"/> Sync handler of this HUD holds; null for any other handler.</summary>
    UiModel WatchedModel(Delegate d)
    {
        if (d.Target == null || !d.Method.Name.Contains(nameof(WatchLivePlayer))) return null;
        var fields = d.Target.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (!fields.Any(f => f.FieldType == typeof(HudScene) && ReferenceEquals(f.GetValue(d.Target), this))) return null;
        return fields.FirstOrDefault(f => f.FieldType == typeof(UiModel))?.GetValue(d.Target) as UiModel;
    }

    /// <summary>
    /// The counts per live event: Sync handlers on the HUD model, the upper HUD's own model and any other model (a stale one),
    /// the ride handlers, the key table send handlers of each watched model's key table, and the HUD dialogs not on the HUD
    /// model. <paramref name="once"/> is true when every count is one per model and nothing stale is left.
    /// </summary>
    public string LiveWatchScripted(out bool once)
    {
        once = false;
        if (Ms2Godot.Net.LiveSession.Instance is not { } live) return "no session";
        var lines = new List<string>();
        var stale = new HashSet<UiModel>(ReferenceEqualityComparer.Instance);
        bool ok = true;
        void Count(string evt, object owner, string field)
        {
            var models = Handlers(owner, field).Select(WatchedModel).Where(m => m != null).ToList();
            int hud = models.Count(m => ReferenceEquals(m, hudModel)), upper = models.Count(m => ReferenceEquals(m, upperHudModel));
            int other = models.Count - hud - upper;
            foreach (var m in models) if (!ReferenceEquals(m, hudModel) && !ReferenceEquals(m, upperHudModel)) stale.Add(m);
            ok &= hud == 1 && upper == (upperHudModel != null ? 1 : 0) && other == 0;
            lines.Add($"{evt} hud {hud} upper {upper} other {other}");
        }
        Count("Me.Changed", live.Me, nameof(live.Me.Changed));
        Count("UserTagSymbolApplied", live.FieldProperties, nameof(live.FieldProperties.UserTagSymbolApplied));
        int ride = Handlers(live, nameof(live.OwnRideChanged)).Count(d => ReferenceEquals(d.Target, this) && d.Method.Name == nameof(OnOwnRide));
        ok &= ride == 1;
        lines.Add($"OwnRideChanged {ride}");
        int keysHud = Handlers(hudModel?.Keys, nameof(KeyTable.KeysSentToServer)).Length;
        int keysStale = stale.Sum(m => Handlers(m.Keys, nameof(KeyTable.KeysSentToServer)).Length);
        ok &= keysHud == 1 && keysStale == 0;
        lines.Add($"KeysSentToServer hud {keysHud} other {keysStale}");
        var off = new List<string>();
        foreach (var (name, d) in new[] { ("screen mask", screenMask), ("notice", notice), ("smart push", smartPushMovie) })
            if (d?.Host != null && !ReferenceEquals(d.Host.Model, hudModel)) off.Add(name);
        ok &= off.Count == 0;
        lines.Add("dialogs off the HUD model [" + string.Join(", ", off) + "]");
        lines.Add($"stale models {stale.Count}");
        once = ok;
        return string.Join("; ", lines);
    }
}
