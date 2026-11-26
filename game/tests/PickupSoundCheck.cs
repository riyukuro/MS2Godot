using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Ms2.Formats.Pack;
using Ms2.Formats.World;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/pickup_sound.tscn) of the meso pickup sound (CDropItem::OnPickedUp, dropitem.cpp:990-1006:
/// the style's getSoundID from "ms2sound/sound/item" at the picker) against the client's FMOD event limits
/// (<see cref="Ms2.Audio.EffectVoiceSlots.Start"/>):
/// - five pickups in one frame: the event's max playbacks (2 for 09200002, steal oldest) sound, the newest ones, not five
///   copies on top of each other;
/// - a pickup and then 16 other item events in the same frame: the pickup still plays (100 channels, a free one first),
///   not cut off by the 16th.
/// Prints a play log (time, sound, voice, playing). The meso's get sound is its drop style's (by amount); the GMS2
/// data's drop styles have no sound ids, so there the KMS meso event is played by name. Needs MS2_DATA. Quits with 0 when it holds, else 1.
/// </summary>
public partial class PickupSoundCheck : Node
{
    const string GmsFallback = "09200002";
    static readonly int[] Amounts = { 1, 10, 100, 1000, 10000, 100000, 1000000, 10000000 };
    FieldAudio audio;
    readonly List<string> fails = new();
    int frames;

    void Log(string what, int voice) =>
        GD.Print($"pickup sound: {Time.GetTicksMsec()} ms {what} voice {voice} playing {audio.VoicePlaying(voice)}");

    public override void _Ready()
    {
        try { Run(); }
        catch (Exception e) { fails.Add(e.ToString()); }
    }

    void Run()
    {
        var packs = PackSet.Shared(Settings.DataDir);
        audio = new FieldAudio();
        AddChild(audio);
        if (!audio.Open(Settings.DataDir, packs)) { fails.Add("no sound bank"); return; }
        var table = new ItemDropTable(packs);
        // the meso's drop styles by amount (dropItemAsset rows of 90000001) and their get sounds
        var mesoIds = Amounts.Select(a => table.Look(ItemDropTable.AssetItemIds[0], a, 1, false)?.Style?.GetSoundId)
            .Where(s => !string.IsNullOrEmpty(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        GD.Print($"pickup sound: meso get sounds over {Amounts.Length} amounts: {(mesoIds.Count > 0 ? string.Join(" ", mesoIds) : "none")}");
        if (mesoIds.Count > 1) fails.Add($"the meso has {mesoIds.Count} get sounds; this check plays one");
        string get = mesoIds.FirstOrDefault() ?? GmsFallback;
        string path = DropEffects.SoundGroup + "/" + get;
        var clip = audio.Library.ResolveEvent(path);
        if (clip?.Event == null) { fails.Add($"{path} has no event"); return; }
        int max = clip.Event.MaxPlaybacks;
        GD.Print($"pickup sound: {clip.Event.FullPath} max playbacks {max} behavior {clip.Event.MaxPlaybacksBehavior} priority {clip.Event.Priority} wave {clip.Name} {clip.Seconds:F2} s");
        if (max != 2 || clip.Event.MaxPlaybacksBehavior != 1) fails.Add($"{get}: max playbacks {max} behavior {clip.Event.MaxPlaybacksBehavior}, the client's are 2 and 1 (steal oldest)");

        // five pickups in one frame
        var at = Vector3.Zero;
        var five = new int[5];
        for (int i = 0; i < five.Length; i++) five[i] = audio.PlayEventVoice(path, at, 0, 1, null);
        for (int i = 0; i < five.Length; i++) Log($"pickup {i + 1} of 5", five[i]);
        int playing = five.Count(audio.VoicePlaying);
        GD.Print($"pickup sound: five in one frame: {playing} playing");
        if (playing != Math.Min(5, max)) fails.Add($"five pickups in one frame: {playing} playing, the event allows {max}");
        else if (!audio.VoicePlaying(five[^1]) || !audio.VoicePlaying(five[^2])) fails.Add("five pickups: the newest two do not play (steal oldest)");

        // a pickup, then 16 other item sounds
        var others = audio.Library.Fev.AllEvents
            .Where(e => string.Equals(e.Group, "MS2Sound/Sound/Item", StringComparison.OrdinalIgnoreCase) && !e.Name.Equals(get, StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
        int pickup = audio.PlayEventVoice(path, at, 0, 1, null);
        Log("pickup", pickup);
        int made = 0;
        for (int i = 0; made < 16 && i < others.Count; i++)
        {
            int v = audio.PlayEventVoice(DropEffects.SoundGroup + "/" + others[i], at + new Vector3(100, 0, 0), 0, 1, null);
            if (v != 0) { made++; Log($"other {made} {others[i]}", v); }
        }
        if (made < 16) fails.Add($"only {made} other item sounds played");
        Log("pickup after 16 others", pickup);
        if (!audio.VoicePlaying(pickup)) fails.Add("the pickup was cut off by 16 other sounds");
    }

    public override void _Process(double delta)
    {
        if (++frames < 3) return;
        foreach (var f in fails) GD.PrintErr("FAIL " + f);
        GD.Print(fails.Count == 0 ? "pickup sound: PASS" : $"pickup sound: FAIL ({fails.Count})");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }
}
