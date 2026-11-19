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
    /// vfunc_83 with ProgressiveLoading: the spawn asks before the actor is added, so it gets none and the npc kind never
    /// shows; only a later quest mark change (RefreshQuestState) asks again and makes the entity.
    /// </summary>
    [Test]
    public void BeforeTheActorIsAddedOnlyALaterQuestMarkMakesAnEntity()
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
        // ProgressiveLoading off (Optimization.xml value 0): the spawn gets its entity at once
        var d = new EmoticonController { ProgressiveLoading = false };
        Load(d);
        Assert.That(d.SpawnNpc(new EmoticonController.Actor { ObjectId = 3, IsNpc = true, NpcKind = 0xb, Friendly = 1 }, () => NpcQuestMarks.Mark.None), Is.Not.Null);
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
}
