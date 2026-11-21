using System.Numerics;
using Ms2.Formats.Model;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Ui;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

public class LiveInteractObjectsTests
{
    sealed class Host : IInteractHost
    {
        public bool HasPlayer { get; set; } = true;
        public bool Restricted;
        public bool IsRestricted(int kind) => Restricted && kind == 3;
        public int FieldId { get; set; } = 20000062;
        public ushort Channel { get; set; } = 2;
        public long NowMs { get; set; } = 1000;
        public int PlayerStateId { get; set; } = 1;
        public readonly HashSet<(int, int)> Shutdown = new();
        public bool IsInteractObjectShutdown(int fieldId, int rowId) => Shutdown.Contains((fieldId, rowId));
        public int FlushCalls;
        public void FlushPlayerState() => FlushCalls++;
        public bool PlayerRiding { get; set; }
        public float MasteryInteractionDistance { get; set; } = 300f;

        public readonly Dictionary<int, QuestMaskTable.QuestStatus> Quests = new();
        public QuestMaskTable.QuestStatus QuestStatusOf(int questId) => Quests.GetValueOrDefault(questId);

        public readonly Dictionary<uint, uint> Items = new();
        public bool HasRequiredItem(uint code, int rank, uint consume) => Items.GetValueOrDefault(code) >= consume;

        public readonly HashSet<(uint, ushort)> Effects = new();
        public bool HasAdditionalEffect(uint effectId, ushort level) => Effects.Contains((effectId, level));
        public string AdditionalEffectName(uint effectId, ushort level) => $"effect:{effectId}:{level}";

        public bool IsPartyLeader { get; set; } = true;
        public readonly Dictionary<int, int> Mastery = new();
        public int MasteryGrade(int masteryType) => Mastery.GetValueOrDefault(masteryType);
        public bool GatherAllowed { get; set; } = true;
        public bool CanGatherRecipe(int recipeId) => GatherAllowed;

        public readonly List<IOutPacket> Sent = new();
        public void Send(IOutPacket packet) => Sent.Add(packet);

        public string Common(int id) => $"common:{id:x}";
        // the additional effect error takes the effect's name as {0}
        public string CommonByKey(string key) => key == "s_interact_err_additional_effect" ? $"key:{key}:{{0}}" : $"key:{key}";
        public string ItemName(uint itemCode) => $"item:{itemCode}";
        public string InteractActionName(int rowId) => $"action:{rowId}";
        public string InteractNoticeText(int nameId) => $"notice:{nameId}:{{0}}";
        public string MasteryTypeName(int type) => $"mtype:{type}";
        public string MasteryGradeName(int type, int value) => $"mgrade:{type}:{value}";

        public readonly List<(string text, int style)> Notices = new();
        public void SystemNotice(string text, int style = 1) => Notices.Add((text, style));
        public readonly List<string> ChatNotices = new();
        public void ChatNotice(string text) => ChatNotices.Add(text);

        public ICastingEventListener ActiveListener;
        public string CastCaption;
        public int CastDurationMs;
        public int CastCancels;
        public bool HasBar = true;
        public bool StartCasting(ICastingEventListener listener, string text, int durationMs)
        {
            if (!HasBar) return false;
            ActiveListener = listener;
            CastCaption = text;
            CastDurationMs = durationMs;
            return true;
        }
        public void CancelCasting()
        {
            CastCancels++;
            ActiveListener = null;
        }

        public readonly List<(string anim, bool loop)> Anims = new();
        public int StopAnimCalls;
        public void PlayPlayerAnim(string animName, bool loop) => Anims.Add((animName, loop));
        public void StopPlayerInteractAnim() => StopAnimCalls++;

        public readonly List<(string name, byte state)> StateChanges = new();
        public void OnObjectStateChanged(LiveInteractObject obj) => StateChanges.Add((obj.Name, obj.State));
        public readonly List<(string name, string fx)> SuccessEffects = new();
        public void OnObjectSuccessEffect(LiveInteractObject obj, string effectAsset) => SuccessEffects.Add((obj.Name, effectAsset));
        public readonly List<(string swf, string value)> OpenedUis = new();
        public void OnOpenUiDialog(string swf, string value) => OpenedUis.Add((swf, value));
        public readonly List<(int type, int rowId)> GuideTriggers = new();
        public void OnGuideTrigger(int triggerType, int rowId) => GuideTriggers.Add((triggerType, rowId));
        public readonly List<string> Open = new();
        public void StillOpen(string what) => Open.Add(what);
    }

    static FieldEntity XblockEntity(string id, int rowId, Vec3 pos)
    {
        var raw = new XEntity { Id = id, ModelName = "MS2InteractObject", Name = "obj_" + id };
        raw.Props["interactID"] = rowId.ToString();
        raw.Props["Position"] = $"{pos.X}, {pos.Y}, {pos.Z}";
        raw.Props["Rotation"] = "0, 0, 0";
        var flat = new FlatModel { Name = "MS2InteractObject" };
        flat.Mixins.Add("MS2InteractObject");
        return new FieldEntity { Raw = raw, Flat = flat };
    }

    static InteractObjectTable MakeTable(params InteractObjectRow[] rows) =>
        new(rows.ToDictionary(r => r.Id));

    [Test]
    public void TableLoadsBothInteractXmlFilesFromRealData()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        var packs = PackSet.Shared(d);
        var tbl = InteractObjectTable.Shared(packs);
        Assert.That(tbl.Count, Is.GreaterThan(4000));
        // interactobject.xml (mesh type 1) and interactobject_mastery.xml (gathering type 6)
        var mesh = tbl.Get(10000001);
        Assert.That(mesh, Is.Not.Null);
        Assert.That(mesh.Type, Is.EqualTo(InteractObjectMessage.KindMesh));
        Assert.That(mesh.InteractionDistance, Is.GreaterThan(0));
        var gather = tbl.Get(60001001);
        Assert.That(gather, Is.Not.Null);
        Assert.That(gather.Type, Is.EqualTo(InteractObjectMessage.KindGathering));
        Assert.That(gather.GatheringRecipeId, Is.GreaterThan(0));
        Assert.That(gather.ReactTimeMs, Is.GreaterThan(0));
    }

    [Test]
    public void MessagesBeforeAttachAreHeldAndAppliedToLowercaseUuids()
    {
        var host = new Host();
        var tbl = MakeTable(new InteractObjectRow { Id = 100, Type = InteractObjectMessage.KindMesh, InteractionDistance = 200 });
        var live = new LiveInteractObjects(host, tbl);

        live.Apply(new InteractObjectMessage
        {
            Mode = InteractObjectMessage.ModeState,
            Name = "AA-BB-CC",
            State = 1,
            Kind = InteractObjectMessage.KindMesh,
        });
        Assert.That(live.All, Is.Empty);

        live.Attach(new[] { XblockEntity("AA-BB-CC", 100, new Vec3(0, 0, 0)) });
        var obj = live.ByName("aa-bb-cc");
        Assert.That(obj, Is.Not.Null);
        Assert.That(obj.Name, Is.EqualTo("aa-bb-cc"));
        Assert.That(obj.State, Is.EqualTo(1));
        Assert.That(live.Poll(new Vec3(50, 0, 0)), Is.True);
        Assert.That(live.Current, Is.SameAs(obj));
    }

    [Test]
    public void InstantInteractSendsMode12AndRespectsClientGates()
    {
        var host = new Host();
        var tbl = MakeTable(new InteractObjectRow
        {
            Id = 200,
            Type = InteractObjectMessage.KindMesh,
            InteractionDistance = 250,
            ReactTimeMs = 0,
            ItemCode = 30000001,
            ItemConsume = 2,
        });
        var live = new LiveInteractObjects(host, tbl);
        live.Attach(new[] { XblockEntity("chest-1", 200, new Vec3(0, 0, 0)) });
        live.Apply(new InteractObjectMessage { Mode = InteractObjectMessage.ModeState, Name = "chest-1", State = 1, Kind = 1 });

        Assert.That(live.Poll(new Vec3(100, 0, 0)), Is.True);
        Assert.That(live.NearestUsable(new Vec3(100, 0, 0)), Is.False);
        Assert.That(live.Use(new Vec3(100, 0, 0)), Is.False);
        Assert.That(host.Sent, Is.Empty);
        Assert.That(host.Notices, Has.Count.EqualTo(1));

        host.Items[30000001] = 2;
        host.Restricted = true;
        Assert.That(live.Poll(new Vec3(100, 0, 0)), Is.False);
        host.Restricted = false;
        Assert.That(live.Poll(new Vec3(100, 0, 0)), Is.True);
        Assert.That(live.NearestUsable(new Vec3(100, 0, 0)), Is.True);
        Assert.That(live.Use(new Vec3(100, 0, 0)), Is.True);
        Assert.That(host.Sent, Has.Count.EqualTo(1));
        var req = host.Sent[0] as InteractObjectCompleteRequest;
        Assert.That(req, Is.Not.Null);
        Assert.That(req.Names, Is.EqualTo(new[] { "chest-1" }));
    }

    [Test]
    public void TimedInteractSendsMode11PlaysAnimCastsAndCompletesWithMode12()
    {
        var host = new Host();
        var tbl = MakeTable(new InteractObjectRow
        {
            Id = 300,
            Type = InteractObjectMessage.KindMesh,
            InteractionDistance = 200,
            ReactTimeMs = 1500,
            ReactAnis = new[] { "Object_React_B" },
        });
        var live = new LiveInteractObjects(host, tbl);
        live.Attach(new[] { XblockEntity("lever-1", 300, new Vec3(0, 0, 0)) });
        live.Apply(new InteractObjectMessage { Mode = InteractObjectMessage.ModeState, Name = "lever-1", State = 1, Kind = 1 });

        Assert.That(live.Poll(new Vec3(10, 0, 0)), Is.True);
        Assert.That(live.Use(new Vec3(10, 0, 0)), Is.True);
        Assert.That(live.IsCasting, Is.True);
        Assert.That(host.CastCaption, Is.EqualTo("action:300"));
        Assert.That(host.CastDurationMs, Is.EqualTo(1500));
        // StateInteraction plays the react animation once (loop false)
        Assert.That(host.Anims, Is.EqualTo(new[] { ("Object_React_B", false) }));
        Assert.That(host.Sent.Single(), Is.InstanceOf<InteractObjectStartRequest>());
        Assert.That(((InteractObjectStartRequest)host.Sent[0]).Names, Is.EqualTo(new[] { "lever-1" }));

        // casting finishes -> Tick sends mode 0x0c and adds guide trigger 9
        live.OnCastingFinished();
        live.Tick(new Vec3(10, 0, 0), playerMoved: false);
        Assert.That(live.IsCasting, Is.False);
        Assert.That(host.StopAnimCalls, Is.EqualTo(1));
        Assert.That(host.Sent, Has.Count.EqualTo(2));
        Assert.That(((InteractObjectCompleteRequest)host.Sent[1]).Names, Is.EqualTo(new[] { "lever-1" }));
        Assert.That(host.GuideTriggers, Is.EqualTo(new[] { (9, 300) }));
    }

    [Test]
    public void TimedInteractCancelsWhenPlayerMovesOrLeavesRange()
    {
        var host = new Host();
        var tbl = MakeTable(new InteractObjectRow { Id = 301, Type = 1, InteractionDistance = 150, ReactTimeMs = 2000 });
        var live = new LiveInteractObjects(host, tbl);
        live.Attach(new[] { XblockEntity("box-1", 301, new Vec3(0, 0, 0)) });
        live.Apply(new InteractObjectMessage { Mode = InteractObjectMessage.ModeState, Name = "box-1", State = 1, Kind = 1 });

        live.Poll(new Vec3(0, 0, 0));
        Assert.That(live.Use(new Vec3(0, 0, 0)), Is.True);
        live.Tick(new Vec3(0, 0, 0), playerMoved: true);
        Assert.That(live.IsCasting, Is.False);
        Assert.That(host.CastCancels, Is.EqualTo(1));
        Assert.That(host.StopAnimCalls, Is.EqualTo(1));
        Assert.That(host.Sent, Has.Count.EqualTo(1)); // only mode 0x0b, never mode 0x0c
    }

    [Test]
    public void GatheringMultiTargetSendRespawnAndSuccessFailAnims()
    {
        var host = new Host { NowMs = 10_000, MasteryInteractionDistance = 300 };
        var tbl = MakeTable(new InteractObjectRow
        {
            Id = 600,
            Type = InteractObjectMessage.KindGathering,
            InteractionDistance = 300,
            ReactTimeMs = 1000,
            ResetTimeMs = 5000,
            GatheringRecipeId = 777,
            ReactAnis = new[] { "Mining_A" },
            SuccessAni = "Mining_Success",
            FailAni = "Mining_Fail",
        });
        var live = new LiveInteractObjects(host, tbl);
        live.Attach(new[]
        {
            XblockEntity("ore-1", 600, new Vec3(0, 0, 0)),
            XblockEntity("ore-2", 600, new Vec3(100, 0, 0)),
            XblockEntity("ore-far", 600, new Vec3(900, 0, 0)),
        });
        live.Apply(new InteractObjectMessage
        {
            Mode = InteractObjectMessage.ModeStates,
            Entries = new List<InteractObjectMessage.StateEntry>
            {
                new("ore-1", 1, 6, 0),
                new("ore-2", 1, 6, 0),
                new("ore-far", 1, 6, 0),
            }
        });

        Assert.That(live.Poll(new Vec3(20, 0, 0)), Is.True);
        Assert.That(live.IsGatheringCurrent, Is.True);
        Assert.That(live.Use(new Vec3(20, 0, 0)), Is.True);
        Assert.That(host.CastCaption, Is.EqualTo("key:s_action_gathering"));
        Assert.That(((InteractObjectStartRequest)host.Sent[0]).Names, Is.EqualTo(new[] { "ore-1", "ore-2" }));

        live.OnCastingFinished();
        live.Tick(new Vec3(20, 0, 0), playerMoved: false);
        Assert.That(((InteractObjectCompleteRequest)host.Sent[1]).Names, Is.EqualTo(new[] { "ore-1", "ore-2" }));

        // mode 5 success increments recipe count, plays successAni, enters respawn cooldown
        live.Apply(new InteractObjectMessage
        {
            Mode = InteractObjectMessage.ModeMessage,
            Name = "ore-1",
            Kind = InteractObjectMessage.KindGathering,
            GatherType = 0,
            GatherCount = 2,
        });
        Assert.That(live.ByName("ore-1").State, Is.EqualTo(0));
        Assert.That(live.ByName("ore-1").RespawnTickMs, Is.EqualTo(15_000));
        Assert.That(live.GatherCounts[777], Is.EqualTo(2));
        Assert.That(host.Anims[^1], Is.EqualTo(("Mining_Success", false)));

        // mode 5 failure plays failAni without incrementing count
        live.Apply(new InteractObjectMessage
        {
            Mode = InteractObjectMessage.ModeMessage,
            Name = "ore-2",
            Kind = InteractObjectMessage.KindGathering,
            GatherType = 1,
            GatherCount = 0,
        });
        Assert.That(live.GatherCounts[777], Is.EqualTo(2));
        Assert.That(host.Anims[^1], Is.EqualTo(("Mining_Fail", false)));

        // Tick restores state 1 once NowMs >= RespawnTickMs
        host.NowMs = 15_000;
        live.Tick(new Vec3(20, 0, 0), playerMoved: false);
        Assert.That(live.ByName("ore-1").State, Is.EqualTo(1));
        Assert.That(live.ByName("ore-1").RespawnTickMs, Is.EqualTo(0));
    }

    [Test]
    public void ModesAddRemoveResultAndNoticeMatchClient()
    {
        var host = new Host { Channel = 2, FieldId = 20000062 };
        var tbl = MakeTable(
            new InteractObjectRow { Id = 10, Type = InteractObjectMessage.KindMesh, InteractionDistance = 200, EffectSuccess = "fx_ok.nif", ItemCode = 999 },
            new InteractObjectRow { Id = 20, Type = InteractObjectMessage.KindTelescope, InteractionDistance = 200, Collection = 1 });
        var live = new LiveInteractObjects(host, tbl);
        live.Attach(new[] { XblockEntity("tel-1", 20, new Vec3(0, 0, 0)) });

        // mode 9 adds dynamic mesh
        live.Apply(new InteractObjectMessage
        {
            Mode = InteractObjectMessage.ModeAdd,
            Name = "dyn-1",
            State = 1,
            Kind = InteractObjectMessage.KindMesh,
            RowId = 10,
            Position = new Vector3(10, 20, 30),
            Model = "MS2InteractMesh",
        });
        Assert.That(live.ByName("dyn-1"), Is.Not.Null);
        // an entity model other than MS2InteractMesh / MS2InteractActor makes no object (scenestategame.cpp:6332-6368)
        live.Apply(new InteractObjectMessage
        {
            Mode = InteractObjectMessage.ModeAdd,
            Name = "dyn-2",
            State = 1,
            Kind = InteractObjectMessage.KindMesh,
            RowId = 10,
            Model = "MS2InteractObject",
        });
        Assert.That(live.ByName("dyn-2"), Is.Null);

        // mode 13 result 0 -> success effect; telescope collection notice fires once
        live.Apply(new InteractObjectMessage { Mode = InteractObjectMessage.ModeResult, Result = 0, Name = "dyn-1", Kind = 1 });
        Assert.That(host.SuccessEffects, Is.EqualTo(new[] { ("dyn-1", "fx_ok.nif") }));
        live.Apply(new InteractObjectMessage { Mode = InteractObjectMessage.ModeResult, Result = 0, Name = "tel-1", Kind = 2 });
        live.Apply(new InteractObjectMessage { Mode = InteractObjectMessage.ModeResult, Result = 0, Name = "tel-1", Kind = 2 });
        Assert.That(host.Notices, Is.EqualTo(new[] { ("common:897", 3) }));

        // mode 13 result 4 -> missing item notice; 6 & 12 silent
        live.Apply(new InteractObjectMessage { Mode = InteractObjectMessage.ModeResult, Result = 4, Name = "dyn-1", Kind = 1 });
        live.Apply(new InteractObjectMessage { Mode = InteractObjectMessage.ModeResult, Result = 6, Name = "dyn-1", Kind = 1 });
        live.Apply(new InteractObjectMessage { Mode = InteractObjectMessage.ModeResult, Result = 12, Name = "dyn-1", Kind = 1 });
        Assert.That(host.Notices, Has.Count.EqualTo(2));

        // mode 14 notice: same channel & map -> system notice style 10; other -> chat notice
        live.Apply(new InteractObjectMessage { Mode = InteractObjectMessage.ModeNotice, Channel = 2, MapId = 20000062, NameId = 5, NoticeArg = "Alice" });
        Assert.That(host.Notices[^1], Is.EqualTo(("notice:5:Alice", 10)));
        live.Apply(new InteractObjectMessage { Mode = InteractObjectMessage.ModeNotice, Channel = 9, MapId = 20000062, NameId = 5, NoticeArg = "Bob" });
        Assert.That(host.ChatNotices, Is.EqualTo(new[] { "notice:5:Bob" }));

        // mode 10 remove
        live.Apply(new InteractObjectMessage { Mode = InteractObjectMessage.ModeRemove, Name = "dyn-1", Effect = "fx_gone.nif" });
        Assert.That(live.ByName("dyn-1"), Is.Null);
        Assert.That(host.SuccessEffects[^1], Is.EqualTo(("dyn-1", "fx_gone.nif")));
    }

    // Unk_ActionKeyManager::Update polls its CRBMap by ascending key: GatheringAction (4) before InteractAction (0x12);
    // the billboard action (0x18) takes a kind 8 pick that InteractAction's vfunc_13 refuses (actionkeyobject.cpp:549, 1523, 2591)
    [Test]
    public void GatheringPollsBeforeInteractAndBillBoardOnlyThroughItsAction()
    {
        var host = new Host();
        var tbl = MakeTable(
            new InteractObjectRow { Id = 1, Type = 1, InteractionDistance = 200 },
            new InteractObjectRow { Id = 2, Type = 6, InteractionDistance = 300 },
            new InteractObjectRow { Id = 8, Type = 8, InteractionDistance = 200 });
        var live = new LiveInteractObjects(host, tbl);
        live.Attach(new[]
        {
            XblockEntity("mesh", 1, new Vec3(10, 0, 0)),
            XblockEntity("ore", 2, new Vec3(150, 0, 0)),
            XblockEntity("board", 8, new Vec3(1000, 0, 0)),
        });
        live.Apply(new InteractObjectMessage { Mode = InteractObjectMessage.ModeAllState, State = 1 });

        // the mesh is nearer, but the gathering action comes first
        Assert.That(live.Poll(new Vec3(0, 0, 0)), Is.True);
        Assert.That(live.Current, Is.SameAs(live.ByName("ore")));
        Assert.That(live.IsGatheringCurrent, Is.True);

        // at the billboard: the interact poll refuses kind 8, the billboard poll takes it
        Assert.That(live.Poll(new Vec3(990, 0, 0)), Is.False);
        Assert.That(live.PollBillBoard(new Vec3(990, 0, 0)), Is.True);
        Assert.That(live.Current, Is.SameAs(live.ByName("board")));
    }

    // FUN_1417d25e0 (stateinteractdata.cpp:518-541): a row without reactAni plays nothing; "Object_React_A" is for no row
    [Test]
    public void TimedInteractWithoutReactAniPlaysNoMotion()
    {
        var host = new Host();
        var tbl = MakeTable(new InteractObjectRow { Id = 302, Type = 1, InteractionDistance = 150, ReactTimeMs = 1000 });
        var live = new LiveInteractObjects(host, tbl);
        live.Attach(new[] { XblockEntity("door-1", 302, new Vec3(0, 0, 0)) });
        live.Apply(new InteractObjectMessage { Mode = InteractObjectMessage.ModeState, Name = "door-1", State = 1, Kind = 1 });

        live.Poll(new Vec3(0, 0, 0));
        Assert.That(live.Use(new Vec3(0, 0, 0)), Is.True);
        Assert.That(live.IsCasting, Is.True);
        Assert.That(host.Anims, Is.Empty);
    }

    // InteractAction::vfunc_2 (actionkeyobject.cpp:1549-1556): one missing additional effect is named by
    // FUN_141812290(id, level, "name"), not by its id
    [Test]
    public void MissingAdditionalEffectNoticeNamesTheEffect()
    {
        var host = new Host();
        var tbl = MakeTable(new InteractObjectRow
        {
            Id = 303, Type = 1, InteractionDistance = 150, ReactTimeMs = 0,
            ConditionEffects = new (uint, ushort)[] { (90000123, 2) },
        });
        var live = new LiveInteractObjects(host, tbl);
        live.Attach(new[] { XblockEntity("altar-1", 303, new Vec3(0, 0, 0)) });
        live.Apply(new InteractObjectMessage { Mode = InteractObjectMessage.ModeState, Name = "altar-1", State = 1, Kind = 1 });

        live.Poll(new Vec3(0, 0, 0));
        Assert.That(live.Use(new Vec3(0, 0, 0)), Is.False);
        Assert.That(host.Notices, Is.EqualTo(new[] { ("key:s_interact_err_additional_effect:effect:90000123:2", 1) }));
        Assert.That(host.Sent, Is.Empty);
    }

    [Test]
    public void PollAndTickAreZeroAllocationPerFrame()
    {
        var host = new Host();
        var tbl = MakeTable(
            new InteractObjectRow { Id = 1, Type = 1, InteractionDistance = 200 },
            new InteractObjectRow { Id = 2, Type = 6, InteractionDistance = 300 });
        var live = new LiveInteractObjects(host, tbl);
        live.Attach(new[]
        {
            XblockEntity("m1", 1, new Vec3(50, 0, 0)),
            XblockEntity("g1", 2, new Vec3(80, 0, 0)),
        });
        live.Apply(new InteractObjectMessage { Mode = InteractObjectMessage.ModeAllState, State = 1 });

        var pos = new Vec3(0, 0, 0);
        var far = new Vec3(5000, 0, 0);
        AssertNoAlloc(live, pos, "both in reach");
        AssertNoAlloc(live, far, "nothing in reach");

        // a cast running: a timed object in reach, used, then ticked without finishing
        var host2 = new Host();
        var live2 = new LiveInteractObjects(host2, MakeTable(new InteractObjectRow { Id = 3, Type = 1, InteractionDistance = 200, ReactTimeMs = 100000 }));
        live2.Attach(new[] { XblockEntity("t1", 3, new Vec3(20, 0, 0)) });
        live2.Apply(new InteractObjectMessage { Mode = InteractObjectMessage.ModeAllState, State = 1 });
        live2.Poll(pos);
        Assert.That(live2.Use(pos), Is.True);
        AssertNoAlloc(live2, pos, "cast running");
        Assert.That(live2.IsCasting, Is.True);
    }

    static void AssertNoAlloc(LiveInteractObjects live, Vec3 pos, string what)
    {
        for (int i = 0; i < 20; i++) { live.Poll(pos); live.Tick(pos, false); }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 600; i++)
        {
            live.Poll(pos);
            live.Tick(pos, false);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocated, Is.EqualTo(0), $"{what}: Poll+Tick allocated {allocated} B over 600 frames");
    }

    static (Host host, LiveInteractObjects live) Lever(int reactMs)
    {
        var host = new Host();
        var live = new LiveInteractObjects(host, MakeTable(new InteractObjectRow { Id = 700, Type = 1, InteractionDistance = 200, ReactTimeMs = (uint)reactMs }));
        live.Attach(new[] { XblockEntity("lv-1", 700, new Vec3(0, 0, 0)) });
        live.Apply(new InteractObjectMessage { Mode = InteractObjectMessage.ModeState, Name = "lv-1", State = 1, Kind = 1 });
        live.Poll(new Vec3(10, 0, 0));
        return (host, live);
    }

    [Test]
    public void TimedInteractRunsOnlyFromTheStatesTheClientAllows()
    {
        // InteractAction::vfunc_2 (actionkeyobject.cpp:1585): FUN_141527160(0x19) == 0 returns; the state change to 0x19
        // is allowed only from states 1, 0xb, 0xf, 0x21 and 0x3a
        foreach (int st in new[] { 1, 0xb, 0xf, 0x21, 0x3a })
            Assert.That(LiveInteractObjects.StateAllowsInteraction(st), Is.True, $"state {st:x}");
        foreach (int st in new[] { -1, 0, 2, 0x10, 0x19 })
            Assert.That(LiveInteractObjects.StateAllowsInteraction(st), Is.False, $"state {st:x}");

        foreach (int st in new[] { 2, -1 })
        {
            var (host, live) = Lever(1500);
            host.PlayerStateId = st;
            Assert.That(live.Use(new Vec3(10, 0, 0)), Is.False, $"state {st}");
            Assert.That(host.Sent, Is.Empty);
            Assert.That(live.IsCasting, Is.False);
        }
        var (h1, l1) = Lever(1500);
        Assert.That(l1.Use(new Vec3(10, 0, 0)), Is.True);
        Assert.That(h1.Sent.Single(), Is.InstanceOf<InteractObjectStartRequest>());
    }

    [Test]
    public void InstantInteractStopsInState10AndFlushesTheStateFirst()
    {
        // reactTime 0 (:1594-1600): state 0x10 returns; else MyPC::FUN_14152e0c0 runs before the object's use
        var (host, live) = Lever(0);
        host.PlayerStateId = 0x10;
        Assert.That(live.Use(new Vec3(10, 0, 0)), Is.False);
        Assert.That(host.Sent, Is.Empty);
        Assert.That(host.FlushCalls, Is.EqualTo(0));
        host.PlayerStateId = 2;
        Assert.That(live.Use(new Vec3(10, 0, 0)), Is.True);
        Assert.That(host.FlushCalls, Is.EqualTo(1));
        Assert.That(host.Sent.Single(), Is.InstanceOf<InteractObjectCompleteRequest>());
    }

    [Test]
    public void ShutDownObjectShowsNoticeAndSendsNothing()
    {
        // CInteractObject::vfunc_4 (interactobject.cpp:399-403): a shut-down (field, row) shows common 0x2f7 and refuses
        var (host, live) = Lever(0);
        host.Shutdown.Add((host.FieldId, 700));
        Assert.That(live.Use(new Vec3(10, 0, 0)), Is.False);
        Assert.That(host.Sent, Is.Empty);
        Assert.That(host.Notices, Is.EqualTo(new[] { ("common:2f7", 1) }));
        host.Shutdown.Clear();
        Assert.That(live.Use(new Vec3(10, 0, 0)), Is.True);
    }

    [Test]
    public void WithoutCastingBarTheCastStillRunsItsTime()
    {
        var (host, live) = Lever(1500);
        host.HasBar = false;
        Assert.That(live.Use(new Vec3(10, 0, 0)), Is.True);
        live.Tick(new Vec3(10, 0, 0), false);
        Assert.That(live.IsCasting, Is.True);
        Assert.That(host.Sent, Has.Count.EqualTo(1));
        host.NowMs += 1499;
        live.Tick(new Vec3(10, 0, 0), false);
        Assert.That(host.Sent, Has.Count.EqualTo(1));
        host.NowMs += 1;
        live.Tick(new Vec3(10, 0, 0), false);
        Assert.That(live.IsCasting, Is.False);
        Assert.That(host.Sent[1], Is.InstanceOf<InteractObjectCompleteRequest>());
    }
}

public class ActionKeyPollTests
{
    [Test]
    public void FirstMatchingKeyWinsInClientKeyOrder()
    {
        // Unk_ActionKeyManager::Update (actionkeyobject.cpp:549-603): ascending key, interact 0x12 < portal 0x13 <
        // talk 0x14 < billboard 0x18
        Assert.That(ActionKeyPoll.First(true, true, true, true), Is.EqualTo(ActionKeyTarget.Interact));
        Assert.That(ActionKeyPoll.First(false, true, true, true), Is.EqualTo(ActionKeyTarget.Portal));
        Assert.That(ActionKeyPoll.First(false, false, true, true), Is.EqualTo(ActionKeyTarget.Talk));
        Assert.That(ActionKeyPoll.First(false, false, false, true), Is.EqualTo(ActionKeyTarget.BillBoard));
        Assert.That(ActionKeyPoll.First(false, false, false, false), Is.EqualTo(ActionKeyTarget.None));
    }

    [Test]
    public void PollRunsAtMostEvery100MsAcrossTheTickWrap()
    {
        var p = new ActionKeyPoll();
        Assert.That(p.Due(5), Is.True);
        Assert.That(p.Due(104), Is.False);
        Assert.That(p.Due(105), Is.True);
        var w = new ActionKeyPoll();
        Assert.That(w.Due(uint.MaxValue - 10), Is.True);
        Assert.That(w.Due(50), Is.False);
        Assert.That(w.Due(89), Is.True);
    }

    [Test]
    public void PressRunsOnlyTheCurrentActionAndNothingWithAHeldItem()
    {
        // ActivateCurrentAction (:608-624): a held item returns; else only m_nCur runs, a refused one does not fall through
        var p = new ActionKeyPoll { Current = ActionKeyTarget.Interact };
        Assert.That(p.OnPress(false), Is.EqualTo(ActionKeyTarget.Interact));
        Assert.That(p.OnPress(true), Is.EqualTo(ActionKeyTarget.None));
    }
}
