using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>
/// Every place in game/ that creates an effect (an effect xml played, spawned or inserted, a soundMusic part, a hit sound,
/// an event voice)
/// is in the reviewed table below with the object kind it passes and the client's kind for the same owner. The client puts
/// the owner's WorldService::GetObjectKind (@0x141bd1a10, worldservice_1.cpp:6304-6309: MyPC::vfunc_58 1, mypc.cpp:1247;
/// PCBase::vfunc_58 3, pcbase.cpp:1092; Ridee::vfunc_58 the rider's, ridee.cpp:539-545; an npc 5, a boss 6) or a fixed
/// kind in the effect target's m_nKind (NoticeMessage's default is 9, unit_140a52ef0_noticemessageaccessors.cpp:22). The
/// kind decides the onlyCasterMyPC filter, the soundMusic gate (kind &lt;= 1), the view distance row of effectOption.xml
/// and forceHearDistance (effectservice.cpp:245-331). No entry point has a default kind, so a new site must name one;
/// a new site fails here until it is reviewed and added. The scene checks under game/tests/ are not scanned.
/// </summary>
public class EffectCasterKindSiteTests
{
    /// <summary>A reviewed site: the file under game/, a text its statement holds, the kind text the call passes, and the
    /// client's kind for that owner with its cite.</summary>
    sealed record Site(string File, string Key, string Kind, string Client);

    const string PassThrough = "passes its caller's kind (the callers are rows of this table)";

    static readonly Site[] Reviewed =
    {
        // the own PC: MyPC::vfunc_58 1
        new("ui/HudScene.Cast.cs", "if (!r.Level.HasSequenceName) live.ReleaseKeptEffect(live.SpawnKeptEffect(fx, me.Visual,", "CasterMyPc",
            "1: the own motion effect of a level without a sequence, inserted on the PC (PlayCastStartEffect, stateskill.cpp:1834)"),
        new("net/LiveSession.SwimDash.cs", "swimDashEffect = SpawnKeptEffect(name, player.Visual,", "CasterMyPc",
            "1: StateSwimDash's dash effect started at the own PC (SetActor) and kept as m_nEffect (FUN_1417fbec0, stateswimdash.cpp:221-233)"),
        new("ui/HudScene.Cast.cs", "ownTagIds.Motion = live.SpawnKeptEffect(fx, me.Visual,", "CasterMyPc",
            "1: the own motion effect kept on the PC (AnimActivated SetActor, stateskill.cpp:750-761)"),
        new("ui/HudScene.Cast.cs", "if (played) ownTagIds.Motion = live.SpawnKeptEffect(motion.MotionEffect, player.Visual,", "CasterMyPc",
            "1: the motion effect of a cast the server started, kept on the PC (stateskill.cpp:750-761)"),
        new("ui/HudScene.Cast.cs", "live.ReleaseKeptEffect(live.SpawnKeptEffect(motion.MotionEffect, player.Visual,", "CasterMyPc",
            "1: the same without a sequence, inserted on the PC (stateskill.cpp:1834)"),
        new("ui/HudScene.Cast.cs", "FieldAudio.Instance?.PlayEventVoice(Ms2.Audio.EventSoundTable.TextKeyType(key), player.Gender,", "CasterMyPc",
            "1: the own PC's EventSound key voice, PlayEventVoice's GetObjectKind (effectservice.cpp:1381)"),
        new("net/LiveSession.cs", "FieldAudio.Instance?.PlayEventVoice(Ms2.Audio.EventSoundTable.MyPcResurrection,", "CasterMyPc",
            "1: the own tomb's myPC_Resurrection voice (pc.cpp:3464, effectservice.cpp:1381)"),
        new("ui/HudScene.Cast.cs", "SpawnKeptEffect(fx, v,", "CasterMyPc",
            "1: the own skill state's loop, loop keep, loop end and end effects kept or inserted on the PC (stateskill.cpp:844-860, :2358, :2376, :515-567)"),
        new("ui/HudScene.Cast.cs", "SpawnKeptEffect(path, player.Visual,", "CasterMyPc", "1: the own tag effects (AnimActivated, stateskill.cpp:757)"),
        new("ui/HudScene.Cast.cs", "PlayHitSound(hs.Attack, hs.Defence,", "CasterMyPc", "1: the own hit sound (PlayHitSound, effectservice.cpp:1355)"),
        new("ui/HudScene.Cast.cs", "live.PlayFieldEffect(fx.File,", "CasterMyPc", "1: the own magic's end effect (CMagic, magicdata.cpp:2151)"),
        new("net/LiveSession.Skills.cs", "remote?.PlayEffect(path, player.GlobalPosition,", "CasterMyPc", "1: the own PC's effect"),
        new("net/LiveSession.cs", "ReleaseKeptEffect(SpawnKeptEffect(path, actor,", "CasterMyPc",
            "1: EFFECT_LEVEL_UP (OnLevelUp, scenestategame.cpp:9520-9540) and EFFECT_ADVENTURE_LEVEL_UP (OnPrestige kind 2, :8838-8849) inserted on the own PC or its ridee; the kind is GetObjectKind of the PC, not the ridee"),
        new("net/RemoteLayer.Magics.cs", "int h = fx.SpawnOn(e.File, node,", "casterKind",
            "the magic owner's (CMagic, magicdata.cpp:927-931): ownSceneEffects 1, remoteSceneEffects 3"),
        // other PCs: PCBase::vfunc_58 3
        new("net/RemoteLayer.Casts.cs", "OutOfSight(r) ? 0 : SpawnKeptEffect(fx, r.Visual,", "CasterOtherPc",
            "3: a user's skill state effects kept on it, tag, motion, loop and loop keep (stateskill.cpp:750-761, :844-860, :2358), past the sight test (:2790-2807)"),
        new("net/RemoteLayer.Casts.cs", "ReleaseKeptEffect(SpawnKeptEffect(fx, r.Visual,", "CasterOtherPc",
            "3: a user's skill state effects inserted on it, the motion effect of a level without a sequence (:1834), loop end (:2376) and end (:515-567)"),
        new("net/RemoteLayer.Magics.cs", "st.EventSound = key => FieldAudio.Instance?.PlayEventVoice(", "CasterOtherPc",
            "3: a user's EventSound key voice, PlayEventVoice's GetObjectKind (effectservice.cpp:1381)"),
        new("net/RemoteLayer.Magics.cs", "combat.PlayEffect(fx.File, new Vector3(fx.At.X, fx.At.Z, -fx.At.Y), MagicEffectBasis(fx),", "CasterOtherPc",
            "3: a user's magic end effect (CMagic, magicdata.cpp:2151)"),
        new("net/RemoteLayer.Ride.cs", "combat.PlayEffect(path, r.Pc.Body.GlobalPosition,", "CasterOtherPc", "3: a user's mount effect (Ridee::vfunc_58, the rider's)"),
        new("net/RemoteLayer.Ride.cs", "ReleaseKeptEffect(SpawnKeptEffect(path, actor,", "CasterOtherPc",
            "3: EFFECT_LEVEL_UP (scenestategame.cpp:9520-9540) and EFFECT_ADVENTURE_LEVEL_UP (:8838-8849) inserted on another PC or its ridee; the kind is GetObjectKind of the PC (PCBase::vfunc_58), not the ridee"),
        // npcs
        new("net/RemoteLayer.Casts.cs", "ReleaseKeptEffect(SpawnKeptEffect(motion.MotionEffect, n.root,", "kind",
            "the npc's, NpcLayer.CasterKind (CNpc::vfunc_58, 5 or 6): the motion effect of a level without a sequence, inserted on it (stateskill.cpp:1834)"),
        new("net/RemoteLayer.Casts.cs", "e.Ids.Motion = SpawnKeptEffect(fx.Motion, root,", "kind",
            "the npc's, NpcLayer.CasterKind: its motion effect kept on it (AnimActivated, stateskill.cpp:750-761)"),
        new("net/RemoteLayer.Casts.cs", "SpawnKeptEffect(fx, insertNpcRoot,", "insertNpcKind",
            "the npc's, NpcLayer.CasterKind: its end effect inserted at the state's exit (stateskill.cpp:515-567)"),
        new("net/NpcLayer.cs", "Combat?.PlayEffect(effect, i.Root.GlobalPosition,", "CasterBoss : Ms2.Audio.SoundMusicEffectState.CasterNpc",
            "6 for a boss, else 5 (CNpc::FUN_141098fb0)"),
        new("net/NpcLayer.cs", "FieldAudio.Instance?.PlayHitSound(material,", "relation", "the attacker's relation (NpcLayer.PlayHitSound)"),
        new("net/NpcLayer.cs", "Combat?.PlayEffect(regen, i.Root.GlobalPosition,", "CasterSystem",
            "0: the npc data's regenEffect, inserted at the npc's place with no actor (StateSpawn, statespawn.cpp:114-127)"),
        // tombs and field markers
        new("net/RemoteLayer.cs", "FieldAudio.Instance?.PlayHitSound(1, soundMaterial, 1, at,", "soundMaterial, 1, at",
            "1: FUN_140ade030(1, 1) for the local player's tag hit on a tomb or marker (breakableobject.cpp:233, worldservice_1.cpp:9367)"),
        new("scripts/movement/PlayerTombstone.cs", "FieldAudio.Instance?.PlayHitSound(hitter, State.HitSound, relation,", "relation",
            "Tombstone.PacketSounds' relation, FUN_140ade030's for the Tombstone packet (pc.cpp:3464, breakableobject.cpp:233)"),
        // system (0)
        new("scripts/world/RegionSkills.cs", "invincibleHandle = SpawnOn(fx,", "CasterSystem", "0: the invincible effect (pc.cpp:3419)"),
        new("net/RemoteInvincible.cs", "fx.SpawnOn(effect, actor,", "CasterSystem", "0: the invincible effect (pc.cpp:3419)"),
        new("scripts/world/TaxiStations.cs", "PlayEffectSoundMusic(part,", "CasterSystem", "0: the taxi's sound music"),
        new("scripts/world/FieldTriggers.cs", "effects.Insert(fx, e.Transform,", "RegionSkills.Insert: CasterSystem",
            "0: a trigger's effect (triggeractioncli.cpp:600, :698, :758)"),
        new("scripts/world/FieldTriggers.cs", "effects.Spawn(e.Get(\"XmlFilePath\")", "RegionSkills.Spawn: CasterSystem",
            "0: a trigger's shown effect (triggeractioncli.cpp:804)"),
        new("net/ReactorLayer.cs", "Fx?.Spawn(effect,", "RegionSkills.Spawn: CasterSystem", "0: a reactor's effect (reactor.cpp:287)"),
        new("scripts/world/LiftTargetMarkers.cs", "effects.Spawn(LiftableTargetBoxes.MarkerEffect,", "RegionSkills.Spawn: CasterSystem",
            "0: the lift target's marker (liftabletarget.cpp:147)"),
        // world objects (7)
        new("scripts/world/FieldVibrateDraws.cs", "effects.Insert(path, entry.Entity.Transform,", "CasterBackground",
            "7: a vibrate object's effect, the request's kind set to 7 (CEntityVibrator, entityvibrator.cpp:190, :213)"),
        // interact objects
        new("scripts/world/FieldInteractDraws.cs", "effects.Insert(effect, new Xform(o.Position, Xform.Identity.R, 1), false, 0)",
            "RegionSkills.Insert: CasterSystem", "0: an interact object's remove effect, a one-shot at its position (scenestategame.cpp:6415-6426)"),
        new("scripts/world/FieldInteractDraws.cs", "int h = effects.Spawn(effect, StateAt(o), false, 0, 1)", "false, 0, 1)",
            "1: CInteractObject::vfunc_12's success effect kept as m_nParticle, the local player its actor (interactobject.cpp:647-665)"),
        new("scripts/world/FieldInteractDraws.cs", "int handle = effects.Spawn(effect, StateAt(o), false, 0)", "RegionSkills.Spawn: CasterSystem",
            "0: CInteractObject::ShowStateEffect's state effect, request m_nObject 0 (interactobject.cpp:451-471)"),
        // pass-through
        new("net/LiveSession.Skills.cs", "=> remote?.PlayEffect(effect, at, yawRadians, casterKind)", "casterKind", PassThrough),
        new("net/LiveSession.Skills.cs", "=> remote?.PlayEffect(effect, at, rotation, casterKind)", "casterKind", PassThrough),
        new("net/LiveSession.Skills.cs", "=> remote?.SpawnKeptEffect(effect, actor, casterKind)", "casterKind", PassThrough),
        new("net/RemoteLayer.Magics.cs", "=> RegionFx?.SpawnOn(effect, actor, casterKind)", "casterKind", PassThrough),
        new("net/RemoteLayer.cs", "=> combat.PlayEffect(effect, at, yawRadians, casterKind)", "casterKind", PassThrough),
        new("net/RemoteLayer.cs", "=> combat.PlayEffect(effect, at, rotation, casterKind)", "casterKind", PassThrough),
        new("net/CombatLayer.cs", "=> PlayEffect(xmlPath, at, new Basis(", "casterKind", PassThrough),
        new("net/CombatLayer.cs", "FieldAudio.Instance?.PlayEffectSoundMusic(part, casterKind)", "casterKind", PassThrough),
        new("scripts/world/RegionSkills.cs", "FieldAudio.Instance?.PlayEffectSoundMusic(part, casterKind)", "casterKind",
            "the effect's kind: SpawnOn's, a region skill's owner (RegionSkillObject.OwnerKind, regionskillobject.cpp:103-118), else 0"),
        new("net/RemoteLayer.Magics.cs", "combat.PlayEffectOn(fx.File, NodeOf(fx.ActorId), fx.CasterKind)", "fx.CasterKind",
            "a hit effect's m_nObject (DamageHitEffects.KindOf)"),
        new("net/RemoteLayer.Magics.cs", "combat.PlayEffect(fx.File, new Vector3(fx.At.X, fx.At.Z, -fx.At.Y), ClientBasis(", "fx.CasterKind",
            "a hit effect's m_nObject (DamageHitEffects.KindOf)"),
        new("net/ReplayField.cs", "combat.PlayEffect(id, at, yaw, casterKind)", "casterKind", "ReplayField.CasterKindOf: the recorded PC 1, another 3"),
    };

    static readonly Regex Entry = new(
        @"(?<![\w.]\s*(void|int|bool)\s)\b(PlayEffect|PlayEffectOn|PlayFieldEffect|SpawnOn|SpawnKeptEffect|PlayEffectSoundMusic)\(|" +
        @"\b\w+\??\.(PlayHitSound|PlayEventVoice)\(|\b(effects|Fx\??|fx|RegionFx\??)\.(Spawn|Insert)\(");

    static readonly Regex Declaration = new(@"(public|private|internal|protected|static)[^;=\n]*\b\w+\($");

    [Test]
    public void EveryEffectSiteIsReviewedAndPassesTheClientsKind()
    {
        var game = GameDir();
        var found = new List<(string File, int Line, string Body)>();
        foreach (var path in Directory.EnumerateFiles(game, "*.cs", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(game, path).Replace(Path.DirectorySeparatorChar, '/');
            if (rel.StartsWith(".godot/", StringComparison.Ordinal) || rel.Contains("/obj/") || rel.StartsWith("tests/", StringComparison.Ordinal)) continue;
            var text = File.ReadAllText(path).Replace("\r\n", "\n");
            foreach (Match m in Entry.Matches(text))
            {
                int lineStart = text.LastIndexOf('\n', m.Index) + 1;
                string before = text[lineStart..(m.Index + m.Length)];
                if (before.TrimStart().StartsWith("//", StringComparison.Ordinal) || Declaration.IsMatch(before)) continue;
                found.Add((rel, text[..m.Index].Count(c => c == '\n') + 1, Statement(text, lineStart, m.Index)));
            }
        }

        var problems = new List<string>();
        var used = new HashSet<Site>();
        foreach (var (file, line, body) in found)
        {
            var sites = Reviewed.Where(s => s.File == file && body.Contains(s.Key, StringComparison.Ordinal)).ToList();
            if (sites.Count != 1)
            {
                problems.Add($"game/{file}:{line}: {(sites.Count == 0 ? "an effect site not in the reviewed table" : "matches several table rows")}: {body.Split('\n')[0].Trim()}");
                continue;
            }
            used.Add(sites[0]);
            string kind = sites[0].Kind.StartsWith("RegionSkills.", StringComparison.Ordinal) ? null : sites[0].Kind;
            if (kind != null && !body.Contains(kind, StringComparison.Ordinal))
                problems.Add($"game/{file}:{line}: passes no {kind}");
        }
        foreach (var s in Reviewed.Where(s => !used.Contains(s)))
            problems.Add($"game/{s.File}: the table row \"{s.Key}\" matches no site (removed or changed: update the table)");

        // RegionSkills.Spawn / Insert without a kind are the system's (0) effects: triggers, reactors, the lift markers
        var region = File.ReadAllText(Path.Combine(game, "scripts", "world", "RegionSkills.cs"));
        if (!region.Contains("int casterKind = Ms2.Audio.SoundMusicEffectState.CasterSystem)", StringComparison.Ordinal))
            problems.Add("game/scripts/world/RegionSkills.cs: Play's kind for Spawn / Insert is not CasterSystem (0)");

        TestContext.Out.WriteLine($"{found.Count} effect sites, {Reviewed.Length} reviewed");
        Assert.That(problems, Is.Empty, string.Join("\n", problems));
        Assert.That(found, Has.Count.EqualTo(Reviewed.Length));
    }

    /// <summary>
    /// Every site above reaches one of these entry points, and each runs CreateEffect's view test (effectservice.cpp:287-345)
    /// for the part kinds it plays: a nif part's CNifEffect::vfunc_3 test (nifeffectparam.cpp:757-766) in CombatLayer.Play
    /// (PlayEffect, PlayEffectOn; the hit effects, npc and boss skill effects, the npc regen, the replay) and RegionSkills.Play
    /// (SpawnOn, SpawnKeptEffect, Spawn, Insert; region skills, kept effects, triggers, reactors); a soundEvent part's
    /// (SoundEventOutOfView) in FieldAudio.PlayEffectSound, which both call; a hit sound's and an event voice's
    /// FUN_1411a5110 test (SoundEventVolume, effectservice.cpp:1321) in PlayHitSound, PlaySoundEvent and PlayEventVoice.
    /// soundMusic and light parts are not tested by the client (their effects have no vfunc_8, :337). RegionSkills.Play tests
    /// for every caller with a kind other than 0, SpawnOn's too, not only a region skill's.
    /// </summary>
    [TestCase("net/CombatLayer.cs", "void Play(string xmlPath, Godot.Vector3 at, Basis rotation, int casterKind, Node3D actor)", "Skip.PartOutOfView(part, casterKind,")]
    [TestCase("scripts/world/RegionSkills.cs", "List<Node3D> Play(string xml, Xform at, bool keep, int handle,", "skipTable.PartOutOfView(part, casterKind,")]
    [TestCase("scripts/world/RegionSkills.cs", "List<Node3D> Play(string xml, Xform at, bool keep, int handle,", "var skipTable = casterKind != 0 ? Skip : null;")]
    [TestCase("audio/FieldAudio.cs", "public int PlayEffectSound(EffectPart part, EffectOwner owner,", "EffectSkipTable.SoundEventOutOfView(effectSkip, part, owner.Kind,")]
    [TestCase("audio/FieldAudio.cs", "public bool PlayHitSound(", "EffectSkipTable.SoundEventVolume(effectSkip, kind,")]
    [TestCase("audio/FieldAudio.cs", "public bool PlaySoundEvent(", "EffectSkipTable.SoundEventVolume(effectSkip, kind,")]
    [TestCase("audio/FieldAudio.cs", "public string PlayEventVoice(", "EffectSkipTable.SoundEventVolume(effectSkip, objectKind,")]
    public void EveryEffectEntryPointRunsTheClientsViewTest(string file, string signature, string test)
    {
        var text = File.ReadAllText(Path.Combine(GameDir(), file)).Replace("\r\n", "\n");
        int at = text.IndexOf(signature, StringComparison.Ordinal);
        Assert.That(at, Is.GreaterThanOrEqualTo(0), $"game/{file}: no {signature}");
        int open = text.IndexOf('{', at), depth = 0, end = open;
        for (; end < text.Length; end++)
            if (text[end] == '{') depth++;
            else if (text[end] == '}' && --depth == 0) break;
        Assert.That(text[open..end], Does.Contain(test), $"game/{file}: {signature} runs no view test");
    }

    /// <summary>The statement from its line's start to the close of the call's argument list (comments and strings skipped).</summary>
    static string Statement(string text, int lineStart, int at)
    {
        int open = text.IndexOf('(', at);
        int depth = 0;
        for (int i = open; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '/') { i = text.IndexOf('\n', i); if (i < 0) break; continue; }
            if (c == '"' || c == '\'')
            {
                for (i++; i < text.Length && text[i] != c; i++) if (text[i] == '\\') i++;
                continue;
            }
            if (c == '(') depth++;
            else if (c == ')' && --depth == 0) return text[lineStart..(i + 1)];
        }
        return text[lineStart..];
    }

    static string GameDir()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, "src", "Ms2.Formats")) && File.Exists(Path.Combine(d.FullName, "game", "project.godot")))
                return Path.Combine(d.FullName, "game");
        Assert.Ignore("source tree not found");
        return null!;
    }
}
