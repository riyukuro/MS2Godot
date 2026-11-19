using System.Xml.Linq;
using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Quest;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The overhead emoticon (uiemoticon) run by its own ActionScript and fed by EmoticonController (needs MS2_DATA).</summary>
public class EmoticonTests
{
    static PackSet packs;
    static MovieSource src;
    static GameTables tables;

    static UiModel Model()
    {
        if (packs == null)
        {
            var d = Environment.GetEnvironmentVariable("MS2_DATA");
            if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
            packs = new PackSet(d);
            src = new MovieSource(k => packs.FindByFileName("Resource/Gfx", k + ".gfx") is { } e ? packs.Read("Resource/Gfx", e) : null);
            tables = new GameTables(packs, "en");
        }
        return new UiModel(tables);
    }

    static UiHost Load(EmoticonController c)
    {
        var m = Model();
        var (rt, p) = src.Open(c.Movie, r => r.Budget = 20_000_000, c.Libraries);
        p.StageW = 1920; p.StageH = 1080;
        var h = new UiHost(p, c, m);
        Assert.That(h.Load(), Is.True, c.Movie + " load: " + string.Join("; ", rt.Errors));
        for (int i = 0; i < 5; i++) p.Tick();
        return h;
    }

    /// <summary>The movie instance open placed in the clip's dynamic container (Emoticon.loadMovieClip), null for none.</summary>
    static object Shown(UiHost h, EmoticonController.Entity e)
    {
        var icon = h.Get(e.Clip, "m_icon");
        var dyn = h.Get(icon, "dynamicContainer");
        return Rt.ToNumber(h.Get(dyn, "numChildren")) > 0 ? h.Invoke(dyn, "getChildAt", 0) : null;
    }

    static (float, float, float)? At(EmoticonController.Actor _) => (500f, 400f, 0f);

    [Test]
    public void AnEquipNpcShowsItsKindsMovieOverItsAnchor()
    {
        long now = 10_000;
        var c = new EmoticonController { TickMs = () => now };
        var h = Load(c);
        // iconName empty, kind 0xb: held, kind 0, the kind's movie (GetNpcMovieName)
        var npc = new EmoticonController.Actor { ObjectId = 1, IsNpc = true, ActorAdded = true, NpcKind = 0xb, NpcId = 11000004, Friendly = 2, Distance = 100 };
        var e = c.SpawnNpc(npc, () => NpcQuestMarks.Mark.None);
        Assert.That(e, Is.Not.Null);
        Assert.That(e.Kind, Is.EqualTo(0));
        Assert.That(e.Held, Is.True);
        c.Process(At);
        Assert.That(h.RT.Errors, Is.Empty);
        Assert.That(e.Opened, Is.EqualTo("emoticon_movie_npc_equip"));
        Assert.That(Shown(h, e), Is.Not.Null);
        Assert.That(Rt.ToBoolean(h.Get(e.Clip, "visible")), Is.True);
        // vfunc_6: the npc offset on the stage point
        Assert.That(Rt.ToNumber(h.Get(e.Clip, "x")), Is.EqualTo(500));
        Assert.That(Rt.ToNumber(h.Get(e.Clip, "y")), Is.EqualTo(400 + c.NpcOffset));
        // beyond EmoticonVisibleDistance, or not projected: hidden; back in range: opened again
        npc.Distance = c.VisibleDistance + 1;
        c.Process(At);
        Assert.That(Rt.ToBoolean(h.Get(e.Clip, "visible")), Is.False);
        npc.Distance = 100;
        c.Process(At);
        Assert.That(Rt.ToBoolean(h.Get(e.Clip, "visible")), Is.True);
        c.Process(_ => null);
        Assert.That(Rt.ToBoolean(h.Get(e.Clip, "visible")), Is.False);
    }

    [Test]
    public void TheIconNameWinsOverTheKindAndEmptyHidesNothingItSet()
    {
        var c = new EmoticonController();
        var h = Load(c);
        var a = new EmoticonController.Actor { ObjectId = 1, IsNpc = true, ActorAdded = true, NpcKind = 0xb, Friendly = 1, Icon = "emoticon_movie_npc_mail" };
        var e = c.SpawnNpc(a, () => NpcQuestMarks.Mark.None);
        c.Process(At);
        Assert.That(e.Opened, Is.EqualTo("emoticon_movie_npc_mail"));
        // no icon and kind 0: nothing to show, stopped
        var b = new EmoticonController.Actor { ObjectId = 2, IsNpc = true, ActorAdded = true, NpcKind = 0, Friendly = 1 };
        var f = c.SpawnNpc(b, () => NpcQuestMarks.Mark.None);
        Assert.That(f.Kind, Is.EqualTo(4));
        Assert.That(f.Active, Is.False);
        // not friendly: no emoticon (vfunc_83)
        Assert.That(c.SpawnNpc(new EmoticonController.Actor { ObjectId = 3, IsNpc = true, ActorAdded = true, NpcKind = 0xb }, () => NpcQuestMarks.Mark.None), Is.Null);
        Assert.That(h.RT.Errors, Is.Empty);
    }

    [Test]
    public void TheQuestMarkReplacesTheNpcMovieAndEachStateResolves()
    {
        long now = 10_000;
        var quest = new QuestData { Id = 100, Type = 0 };
        var c = new EmoticonController { TickMs = () => now, QuestOf = id => id == 100 ? quest : null };
        var h = Load(c);
        var mark = new NpcQuestMarks.Mark(0, 100, 0);
        var npc = new EmoticonController.Actor { ObjectId = 1, IsNpc = true, ActorAdded = true, NpcKind = 0xb, Friendly = 1, HasQuests = true, Distance = 10 };
        var e = c.SpawnNpc(npc, () => mark);
        Assert.That(e.Kind, Is.EqualTo(1));
        c.Process(At);
        Assert.That(e.Opened, Is.EqualTo("emoticon_movie_quest_start_epic"));
        Assert.That(Shown(h, e), Is.Not.Null, "the quest start class resolves");
        // m_nE14 starts at 0, so the first tick refreshes and stamps the time; a change is then picked up after 1000 ms
        c.NpcTick(npc, () => mark);
        mark = new NpcQuestMarks.Mark(1, 100, 0);
        now += 500;
        c.NpcTick(npc, () => mark);
        Assert.That(npc.Quest.State, Is.EqualTo(0));
        now += 600;
        c.NpcTick(npc, () => mark);
        Assert.That(npc.Quest.State, Is.EqualTo(1));
        c.Process(At);
        Assert.That(e.Opened, Is.EqualTo("emoticon_movie_quest_progress"));
        Assert.That(Shown(h, e), Is.Not.Null);
        mark = new NpcQuestMarks.Mark(2, 100, 0);
        npc.QuestDirty = true;
        c.NpcTick(npc, () => mark);
        c.Process(At);
        Assert.That(e.Opened, Is.EqualTo("emoticon_movie_quest_complete_epic"));
        Assert.That(Shown(h, e), Is.Not.Null);
        // no mark left: back to the kind's movie
        mark = NpcQuestMarks.Mark.None;
        npc.QuestDirty = true;
        c.NpcTick(npc, () => mark);
        c.Process(At);
        Assert.That(e.Kind, Is.EqualTo(0));
        Assert.That(e.Opened, Is.EqualTo("emoticon_movie_npc_equip"));
        foreach (var kind in new[] { "", "_repeat", "_event" })
            foreach (var s in new[] { "start", "complete" })
            {
                var name = "emoticon_movie_quest_" + s + kind;
                h.Invoke(e.Clip, "open", name);
                Assert.That(Shown(h, e), Is.Not.Null, name);
            }
        Assert.That(h.RT.Errors, Is.Empty);
    }

    /// <summary>
    /// vfunc_83 with ProgressiveLoading: an ask before the actor is added gets none (the client's spawn asks after the add;
    /// before it only when the model never loaded). Nothing of the npc's asks again but a quest mark change; the spawn after
    /// the add holds the kind at once.
    /// </summary>
    [Test]
    public void BeforeTheActorIsAddedNoAskMakesAnEntity()
    {
        long now = 10_000;
        var quest = new QuestData { Id = 100, Type = 0 };
        var c = new EmoticonController { TickMs = () => now, QuestOf = id => id == 100 ? quest : null };
        var h = Load(c);
        Assert.That(c.ProgressiveLoading, Is.True, "on without Optimization.xml");
        var equip = new EmoticonController.Actor { ObjectId = 1, IsNpc = true, NpcKind = 0xb, Friendly = 1, Distance = 10 };
        Assert.That(c.SpawnNpc(equip, () => NpcQuestMarks.Mark.None), Is.Null);
        var mark = NpcQuestMarks.Mark.None;
        var giver = new EmoticonController.Actor { ObjectId = 2, IsNpc = true, NpcKind = 0xb, Friendly = 1, HasQuests = true, Distance = 10 };
        Assert.That(c.SpawnNpc(giver, () => mark), Is.Null);
        // the actor is added later; nothing asks for the equip npc's emoticon again
        EmoticonController.OnActorAdded(equip);
        EmoticonController.OnActorAdded(giver);
        for (int i = 0; i < 3; i++)
        {
            now += 1100;
            c.NpcTick(equip, () => NpcQuestMarks.Mark.None);
            c.NpcTick(giver, () => mark);
        }
        c.Process(At);
        Assert.That(c.Entities, Is.Empty, "the reset quest state is no change");
        // the quest state moves off the reset value: the next 1000 ms tick makes the entity, kind 1
        mark = new NpcQuestMarks.Mark(0, 100, 0);
        now += 1100;
        c.NpcTick(giver, () => mark);
        Assert.That(c.Entities.ContainsKey(1), Is.False);
        Assert.That(c.Entities.TryGetValue(2, out var e), Is.True);
        Assert.That(e.Kind, Is.EqualTo(1));
        c.Process(At);
        Assert.That(e.Opened, Is.EqualTo("emoticon_movie_quest_start_epic"));
        Assert.That(Shown(h, e), Is.Not.Null);
        // the spawn after the actor's add: the kind is held and started
        var added = new EmoticonController.Actor { ObjectId = 4, IsNpc = true, NpcKind = 0xd, Friendly = 1, Distance = 10 };
        EmoticonController.OnActorAdded(added);
        var g = c.SpawnNpc(added, () => NpcQuestMarks.Mark.None);
        Assert.That(g, Is.Not.Null);
        Assert.That((g.Held, g.Kind), Is.EqualTo((true, 0)));
        // ProgressiveLoading off (Optimization.xml value 0): the spawn gets its entity at once
        var d = new EmoticonController { ProgressiveLoading = false };
        Load(d);
        Assert.That(d.SpawnNpc(new EmoticonController.Actor { ObjectId = 3, IsNpc = true, NpcKind = 0xb, Friendly = 1 }, () => NpcQuestMarks.Mark.None), Is.Not.Null);
        Assert.That(h.RT.Errors, Is.Empty);
    }

    /// <summary>
    /// The talk's name tag (SetNpcNameTag): the status kind with the packet's movie name, the only kind the talk scene shows;
    /// a page shown keeps it after that name tag (m_nSubMode 1) and drops it otherwise (FUN_14192f9f0), and leaving the
    /// scene drops the kind too but keeps a name not yet opened (Entity::vfunc_8).
    /// </summary>
    [Test]
    public void TheTalksNameTagIsTheStatusKindUntilAnotherPageIsShown()
    {
        bool inTalk = true;
        var c = new EmoticonController { SceneHidesEmoticons = () => inTalk };
        var h = Load(c);
        var npc = new EmoticonController.Actor { ObjectId = 1, IsNpc = true, ActorAdded = true, NpcKind = 0xb, Friendly = 1, Distance = 10 };
        var e = c.SpawnNpc(npc, () => NpcQuestMarks.Mark.None);
        c.Process(At);
        Assert.That(Rt.ToBoolean(h.Get(e.Clip, "visible")), Is.False, "the talk scene hides the npc kind");
        Assert.That(c.SetNpcNameTag(npc, "emoticon_movie_quest_progress"), Is.SameAs(e));
        Assert.That(e.Kind, Is.EqualTo(2));
        c.Process(At);
        Assert.That(e.Opened, Is.EqualTo("emoticon_movie_quest_progress"));
        Assert.That(Shown(h, e), Is.Not.Null);
        Assert.That(Rt.ToNumber(h.Get(e.Clip, "y")), Is.EqualTo(400 + c.NpcOffset + 30), "vfunc_6: the status kind is 30 lower");
        Assert.That(e.Icon, Is.Empty, "the status kind's name is used once");
        c.TalkPageShown(npc, 1);
        Assert.That(e.Kind, Is.EqualTo(2));
        c.TalkPageShown(npc, 0);
        Assert.That(e.Kind, Is.EqualTo(0), "dropped, then the npc's refresh");
        Assert.That(e.Active, Is.True);
        // set again, then the scene is left before it opens: the frame drops the kind (vfunc_8: FUN_14192f8b0(4) and the
        // refresh) but not the name, so the npc kind opens with the name tag's movie name
        c.SetNpcNameTag(npc, "emoticon_movie_npc_mail");
        inTalk = false;
        c.Process(At);
        Assert.That(e.Kind, Is.EqualTo(0));
        c.Process(At);
        Assert.That(e.Opened, Is.EqualTo("emoticon_movie_npc_mail"));
        Assert.That(e.Icon, Is.EqualTo("emoticon_movie_npc_mail"), "only the status kind's open uses the name once");
        // before the actor is added (ProgressiveLoading) vfunc_83 gives none: nothing is set
        var late = new EmoticonController.Actor { ObjectId = 2, IsNpc = true, NpcKind = 0xb, Friendly = 1 };
        Assert.That(c.SetNpcNameTag(late, "emoticon_movie_npc_mail"), Is.Null);
        c.TalkPageShown(late, 0);
        Assert.That(c.Entities.ContainsKey(2), Is.False);
        Assert.That(h.RT.Errors, Is.Empty);
    }

    [Test]
    public void AReleasedEntityIsPooledForTheNextActor()
    {
        var c = new EmoticonController();
        var h = Load(c);
        var a = new EmoticonController.Actor { ObjectId = 1, IsNpc = true, ActorAdded = true, NpcKind = 2, Friendly = 1 };
        var e = c.SpawnNpc(a, () => NpcQuestMarks.Mark.None);
        c.Process(At);
        c.Release(1);
        Assert.That(Rt.ToBoolean(h.Get(e.Clip, "visible")), Is.False);
        var b = new EmoticonController.Actor { ObjectId = 2, IsNpc = true, ActorAdded = true, NpcKind = 3, Friendly = 1 };
        var f = c.SpawnNpc(b, () => NpcQuestMarks.Mark.None);
        Assert.That(f, Is.SameAs(e));
        Assert.That(c.ClipsMade, Is.EqualTo(1));
        c.Process(At);
        Assert.That(f.Opened, Is.EqualTo("emoticon_movie_npc_mail"));
        Assert.That(h.RT.Errors, Is.Empty);
    }

    [Test]
    public void TheMovieNamesFollowTheClientsTables()
    {
        var c = new EmoticonController { OfficerSymbol = k => k == 0x60 ? "green" : null };
        Assert.That(c.NpcMovieName(0xc), Is.EqualTo("emoticon_movie_npc_equip"));
        Assert.That(c.NpcMovieName(0x50), Is.EqualTo("emoticon_movie_npc_headhunter"));
        Assert.That(c.NpcMovieName(500), Is.EqualTo(""));
        Assert.That(c.NpcMovieName(0x60), Is.EqualTo("emoticon_movie_npc_green"));
        Assert.That(c.NpcMovieName(1), Is.EqualTo(""));
        Assert.That(c.QuestMovieName(1, 1), Is.EqualTo("emoticon_movie_quest_progress"));
        Assert.That(c.QuestMovieName(1, 4), Is.EqualTo(""));
        Assert.That(c.QuestMovieName(1, 0), Is.EqualTo("emoticon_movie_quest_start"));
    }

    /// <summary>
    /// The officer npcs of fameDef.xml (CFameTable): each def's officer kind is an officer npc and shows the first def's
    /// symbol (by alliance) of that kind, lower case, as "emoticon_movie_npc_{symbol}" over the npc.
    /// </summary>
    [Test]
    public void AnOfficerNpcShowsItsAlliancesSymbol()
    {
        Model();
        var fame = tables.Fame;
        Assert.That(fame.Defs, Is.Not.Empty);
        foreach (var d in fame.Defs)
        {
            Assert.That(fame.IsOfficerNpc(d.OfficerNpcKind), Is.True, $"alliance {d.Alliance}");
            var first = fame.Defs.First(x => x.OfficerNpcKind == d.OfficerNpcKind);
            Assert.That(fame.GetSymbolByOfficerNpc(d.OfficerNpcKind), Is.EqualTo(first.Symbol));
            Assert.That(first.Symbol, Is.EqualTo(first.Symbol.ToLowerInvariant()).And.Not.Empty);
        }
        Assert.That(fame.IsOfficerNpc(0xb), Is.False);
        Assert.That(fame.GetSymbolByOfficerNpc(0xb), Is.EqualTo(""));

        var c = new EmoticonController { OfficerSymbol = k => fame.IsOfficerNpc(k) ? fame.GetSymbolByOfficerNpc(k) : null };
        var h = Load(c);
        var def = fame.Defs.First();
        var npc = new EmoticonController.Actor { ObjectId = 1, IsNpc = true, ActorAdded = true, NpcKind = def.OfficerNpcKind, Friendly = 2, Distance = 100 };
        var e = c.SpawnNpc(npc, () => NpcQuestMarks.Mark.None);
        Assert.That(e, Is.Not.Null);
        c.Process(At);
        Assert.That(h.RT.Errors, Is.Empty);
        Assert.That(e.Opened, Is.EqualTo("emoticon_movie_npc_" + def.Symbol));
        Assert.That(Shown(h, e), Is.Not.Null);
    }

    /// <summary>LoadFameDef's rules on a made table: an unavailable season is skipped, a later def of an alliance replaces
    /// it (its old kind stays an officer npc with no symbol), the symbol is lowered and a missing attribute is 0 / "".</summary>
    [Test]
    public void TheFameDefsFollowLoadFameDef()
    {
        var doc = XDocument.Parse("""
            <ms2>
              <season name="SkyFotress"><fame alliance="DarkWind" officerNpcKind="100" symbol="DarkWind" /></season>
              <season name="Georg" skip="1"><fame alliance="Georg" officerNpcKind="108" symbol="Georg" /></season>
              <season name="KritiasExped">
                <fame alliance="darkwind" officerNpcKind="105" symbol="MapleUnion" isExped="1" />
                <fame alliance="GreenHood" />
              </season>
            </ms2>
            """);
        var fame = new FameTable(doc, s => s.Attribute("skip") == null);
        Assert.That(fame.Defs.Select(d => d.Alliance), Is.EqualTo(new[] { 1, 5 }));
        var dark = fame.Defs.Single(d => d.Alliance == FameTable.Alliance("DarkWind"));
        Assert.That(dark, Is.EqualTo(new FameTable.Def(1, 105, "mapleunion", 1, 2)));
        Assert.That(fame.IsOfficerNpc(100), Is.True);
        Assert.That(fame.GetSymbolByOfficerNpc(100), Is.EqualTo(""));
        Assert.That(fame.GetSymbolByOfficerNpc(105), Is.EqualTo("mapleunion"));
        Assert.That(fame.IsOfficerNpc(108), Is.False);
        Assert.That(fame.Defs.Single(d => d.Alliance == 5), Is.EqualTo(new FameTable.Def(5, 0, "", 0, 2)));
        Assert.That(FameTable.Empty.Defs, Is.Empty);
    }

    /// <summary>vfunc_6: a PC's emoticon moves by its FUN_141670500 (OffsetPcChatballoon), another actor's by nothing.</summary>
    [Test]
    public void APcsEmoticonMovesByItsChatBalloonOffset()
    {
        var c = new EmoticonController { NpcOffset = -30, PcOffset = -45 };
        var h = Load(c);
        EmoticonController.Entity Shows(EmoticonController.Actor a)
        {
            // what the PC's on-demand emoticon does (FUN_14192f8b0, FUN_14192f8d0, vfunc_2), here by hand
            var e = c.GetOrCreate(a);
            e.Kind = 0; e.Icon = "emoticon_movie_npc_mail"; e.Held = true; e.Active = e.Reopen = true;
            return e;
        }
        var pc = Shows(new EmoticonController.Actor { ObjectId = 1, IsPc = true, ActorAdded = true, Distance = 100 });
        var other = Shows(new EmoticonController.Actor { ObjectId = 2, ActorAdded = true, Distance = 100 });
        c.Process(At);
        Assert.That(h.RT.Errors, Is.Empty);
        Assert.That(pc.Opened, Is.EqualTo("emoticon_movie_npc_mail"));
        Assert.That(Rt.ToNumber(h.Get(pc.Clip, "y")), Is.EqualTo(400 + c.PcOffset));
        Assert.That(Rt.ToNumber(h.Get(other.Clip, "y")), Is.EqualTo(400));
    }
}
