using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Ms2.Formats.Options;
using Ms2.Formats.Pack;
using Ms2.Formats.World;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/effect_mute.tscn) of the "mute all" sound option's gate on an effect's sound
/// (CSoundEventEffect::vfunc_3 @0x141776760, soundeventmgr.cpp:460, before the cooltime at :463):
/// - muted, a soundEvent part with a cooltimeID makes no sound: no voice, and the cooltime is not used;
/// - unmuted, the same part plays at once (the muted play did not start its cooltime);
/// - played again at once it is cooling, so the cooltime is real.
/// The option is set in memory only and put back. Needs MS2_DATA. Quits with 0 when it holds, else 1.
/// </summary>
public partial class EffectMuteCheck : Node
{
    readonly List<string> fails = new();
    int frames;

    public override void _Ready()
    {
        var sound = GameOptions.Current.SoundOption;
        bool was = sound.MuteAll;
        try { Run(sound); }
        catch (Exception e) { fails.Add(e.ToString()); }
        finally { sound.MuteAll = was; }
    }

    void Run(GameOption.Sound sound)
    {
        var packs = PackSet.Shared(Settings.DataDir);
        var audio = new FieldAudio();
        AddChild(audio);
        if (!audio.Open(Settings.DataDir, packs)) { fails.Add("no sound bank"); return; }
        // a cooltime long enough not to run out within the check
        var root = Ms2.Formats.Text.PackXml.Parse(packs.Read("Xml", "table/cooltimetable.xml")).Root;
        var rows = root == null ? [] : Ms2.Ui.FeatureElements.Collect(packs, root, "v", "cooltimeID").ToList();
        var table = CooltimeTable.From(rows);
        int id = rows.Select(e => int.TryParse((string)e.Attribute("cooltimeID"), out int v) ? v : 0)
            .FirstOrDefault(v => v != 0 && table.TickOf(v) >= 2000);
        if (id == 0) { fails.Add("no cooltime of 2 s or more in table/cooltimetable.xml"); return; }
        var part = new EffectPart { Type = "soundEvent", SoundGroup = DropEffects.SoundGroup, SoundEvent = "09200002", CooltimeId = id };
        var owner = new EffectOwner(0, 0, 0, true);
        GD.Print($"effect mute: cooltime {id} of {table.TickOf(id)} ms, event {part.SoundGroup}/{part.SoundEvent}");

        sound.MuteAll = true;
        int muted = audio.PlayEffectSound(part, owner, Vector3.Zero);
        GD.Print($"effect mute: muted voice {muted}, muted {audio.EffectSoundsMuted}, cooling {audio.EffectSoundsCooling}");
        if (muted != 0) fails.Add($"muted: the part played on voice {muted}");
        if (audio.EffectSoundsCooling != 0) fails.Add("muted: the part was counted as cooling");

        sound.MuteAll = false;
        int played = audio.PlayEffectSound(part, owner, Vector3.Zero);
        GD.Print($"effect mute: unmuted voice {played}, cooling {audio.EffectSoundsCooling}");
        if (played == 0) fails.Add("unmuted: the part did not play (the muted play used its cooltime)");
        int again = audio.PlayEffectSound(part, owner, Vector3.Zero);
        GD.Print($"effect mute: again voice {again}, cooling {audio.EffectSoundsCooling}");
        if (again != 0 || audio.EffectSoundsCooling != 1) fails.Add($"again at once: voice {again}, cooling {audio.EffectSoundsCooling}; the cooltime did not hold");
    }

    public override void _Process(double delta)
    {
        if (++frames < 3) return;
        foreach (var f in fails) GD.PrintErr("FAIL " + f);
        GD.Print(fails.Count == 0 ? "effect mute: PASS" : $"effect mute: FAIL ({fails.Count})");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }
}
