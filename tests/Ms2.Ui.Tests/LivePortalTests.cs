using System.Numerics;
using Ms2.Formats.Model;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Maple2.PacketLib.Tools;
using Ms2.Ui;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

public class LivePortalTests
{
    sealed class Host : IPortalHost
    {
        public bool HasPlayer { get; set; } = true;
        public bool InCubeMode { get; set; }
        public bool Restricted;
        /// <summary>Answer as the live host does: the session guide's limit.</summary>
        public bool UseGuide;
        public bool IsRestricted(int kind) => UseGuide ? Ms2.Ui.Guide.GuideManager.IsLimitActive(kind) : Restricted && kind == 9;
        public int FieldId { get; set; } = 1000;
        public long ServerTime { get; set; } = 5000;
        public readonly Dictionary<int, FieldData> Fields = new();
        public FieldData Field(int id) => Fields.GetValueOrDefault(id);
        public readonly HashSet<int> Shadow = new();
        public bool IsShadowContinent(int code) => Shadow.Contains(code);
        public readonly HashSet<int> Rewarded = new();
        public bool IsQuestRewarded(int q) => Rewarded.Contains(q);
        public string QuestName(int q) => "q" + q;
        public readonly List<IOutPacket> Sent = new();
        public void Send(IOutPacket p) => Sent.Add(p);
        public readonly List<(int id, int type, string tag)> Boxes = new();
        public void ShowCommonBox(int id, int type, string tag) => Boxes.Add((id, type, tag));
        public readonly List<(int id, Action<int, string> answer)> Inputs = new();
        public void ShowInputBox(int id, Action<int, string> answer) => Inputs.Add((id, answer));
        public readonly List<string> Notices = new();
        public void SystemNotice(string text) => Notices.Add(text);
        public readonly List<int> BoxErrors = new();
        public void UpdateBoxError(int id) => BoxErrors.Add(id);
        public readonly List<string> TextBoxes = new();
        public void ShowTextBox(string text) => TextBoxes.Add(text);
        public readonly List<string> Open = new();
        public void StillOpen(string what) => Open.Add(what);
        public readonly List<string> Dungeon = new();
        public void OpenDungeonRoomDialog(int portalId) => Dungeon.Add($"open {portalId}");
        public void OpenDungeonRoomDialogWithMode(int portalId, int mode) => Dungeon.Add($"mode {mode} portal {portalId}");
        public void OpenDungeonRoomDialogRoom(int portalId) => Dungeon.Add($"room {portalId}");
        public void CloseDungeonRoomDialog() => Dungeon.Add("close");
    }

    static FieldEntity Xblock(int id, string maskIds = null, string maskStates = null, bool enabled = true)
    {
        var raw = new XEntity { Id = "e" + id, ModelName = "PortalModel", Name = "portal" + id };
        raw.Props["PortalID"] = id.ToString();
        raw.Props["Position"] = "0, 0, 0";
        raw.Props["PortalDimension"] = "200, 200, 250";
        raw.Props["PortalEnable"] = enabled ? "True" : "False";
        if (maskIds != null) { raw.Props["MaskQuestID"] = maskIds; raw.Props["MaskQuestState"] = maskStates; }
        var flat = new FlatModel { Name = "PortalModel" };
        flat.Mixins.Add("Portal");
        return new FieldEntity { Raw = raw, Flat = flat };
    }

    static FieldPortalMessage Add(int id, byte type = 0, int target = 2000, bool visible = true, bool enabled = true,
        bool password = false, string feature = "", int startTick = 0, long owner = 0, int kind = 0) => new()
    {
        Mode = 0, Id = id, Type = type, TargetMap = target, Visible = visible, Enabled = enabled, Password = password,
        Feature = feature, StartTick = startTick, Owner = owner, ActionKind = kind, Dimension = new Vector3(200, 200, 250),
    };

    static (Host host, PortalAction action, LivePortals portals) Field(params FieldEntity[] entities) =>
        Field(null, null, entities);

    static (Host host, PortalAction action, LivePortals portals) Field(QuestMaskTable masks, Func<string, string, bool> available,
        params FieldEntity[] entities)
    {
        var host = new Host();
        host.Fields[1000] = new FieldData { Id = 1000 };
        host.Fields[2000] = new FieldData { Id = 2000 };
        var portals = new LivePortals(1000, masks, available, null);
        portals.Attach(entities);
        return (host, new PortalAction(host) { Portals = portals }, portals);
    }

    static readonly Vec3 Origin = new(0, 0, 0);

    /// <summary>Stand in the portal and press the key.</summary>
    static void Enter(PortalAction a)
    {
        if (a.Poll(Origin) && a.Accept()) a.Use();
    }

    static QuestMaskTable NoTable() => new(new System.Xml.Linq.XDocument(new System.Xml.Linq.XElement("ms2")),
        new System.Xml.Linq.XDocument(new System.Xml.Linq.XElement("ms2")));

    [Test]
    public void OpenPortalSendsModeZeroOfTheCurrentMap()
    {
        var (host, action, portals) = Field(Xblock(7));
        portals.Apply(Add(7));
        Enter(action);
        Assert.That(host.Sent, Is.EqualTo(new IOutPacket[] { new RequestMoveFieldPortal(1000, 7) }));
    }

    [Test]
    public void PortalNotInTheServerListIsNotUsed()
    {
        // the xblock has the portal but the server did not register it (the old offline list let it through)
        var (host, action, _) = Field(Xblock(7));
        Enter(action);
        Assert.That(host.Sent, Is.Empty);
    }

    [Test]
    public void HiddenPortalSendsNothing()
    {
        var (host, action, portals) = Field(Xblock(7));
        portals.Apply(Add(7, visible: false, enabled: false));
        Enter(action);
        Assert.That(host.Sent, Is.Empty);
        // mode 2 opens it
        portals.Apply(new FieldPortalMessage { Mode = 2, Id = 7, Visible = true, Enabled = true });
        Enter(action);
        Assert.That(host.Sent, Has.Count.EqualTo(1));
    }

    [Test]
    public void MaskedPortalSendsNothingUntilItsQuestIsTaken()
    {
        // own mask: shown from quest 100 taken (mode 1) until quest 200 rewarded (mode 3)
        var quests = new Dictionary<int, QuestMaskTable.QuestStatus>();
        var host = new Host();
        host.Fields[1000] = new FieldData { Id = 1000 }; host.Fields[2000] = new FieldData { Id = 2000 };
        var portals = new LivePortals(1000, NoTable(), null, q => quests.GetValueOrDefault(q));
        portals.Attach(new[] { Xblock(7, "100,200", "1,3") });
        var action = new PortalAction(host) { Portals = portals };
        portals.Apply(Add(7));
        Enter(action);
        Assert.That(host.Sent, Is.Empty);
        quests[100] = QuestMaskTable.QuestStatus.Active;
        portals.RefreshMasks();
        Enter(action);
        Assert.That(host.Sent, Has.Count.EqualTo(1));
    }

    [Test]
    public void FeatureGatedPortalSendsNothing()
    {
        var (host, action, portals) = Field(null, (f, _) => string.IsNullOrEmpty(f), Xblock(7));
        portals.Apply(Add(7, feature: "SomeFeature"));
        Enter(action);
        Assert.That(host.Sent, Is.Empty);
    }

    [TestCase((byte)0x10)]
    public void UnportedTypeSendsNothing(byte type)
    {
        var (host, action, portals) = Field(Xblock(7));
        portals.Apply(Add(7, type: type));
        Enter(action);
        Assert.That(host.Sent, Is.Empty);
        Assert.That(host.Open, Has.Count.EqualTo(1));
    }

    // PortalAction::UsePortal (actionkeyobject.cpp:1040): types 8, 0xc, 0xe and 0x11 open the dungeon room dialog and send nothing.
    [TestCase((byte)8, "open 7")]
    [TestCase((byte)0xc, "mode 3 portal 7")]
    [TestCase((byte)0xe, "mode 4 portal 7")]
    [TestCase((byte)0x11, "room 7")]
    public void DungeonRoomTypesOpenTheDialog(byte type, string expected)
    {
        var (host, action, portals) = Field(Xblock(7));
        portals.Apply(Add(7, type: type));
        Enter(action);
        Assert.That(host.Dungeon, Is.EqualTo(new[] { expected }));
        Assert.That(host.Sent, Is.Empty);
        Assert.That(host.Open, Is.Empty);
    }

    // PortalAction::vfunc_3 (:853): leaving the portal closes the dungeon room dialog.
    [Test]
    public void LeavingThePortalClosesTheDungeonRoomDialog()
    {
        var (host, action, _) = Field(Xblock(7));
        action.Leave();
        Assert.That(host.Dungeon, Is.EqualTo(new[] { "close" }));
    }

    [Test]
    public void RoomDungeonTypeSendsRoomDungeonNotModeZero()
    {
        var (host, action, portals) = Field(Xblock(7));
        portals.Apply(Add(7, type: 9));
        Enter(action);
        Assert.That(host.Sent, Is.EqualTo(new IOutPacket[] { new RoomDungeonPortalRequest() }));
    }

    [Test]
    public void TypeDSendsPortalOne()
    {
        var (host, action, portals) = Field(Xblock(7));
        portals.Apply(Add(7, type: 0xd));
        Enter(action);
        Assert.That(host.Sent, Is.EqualTo(new IOutPacket[] { new RequestMoveFieldPortal(1000, 1) }));
    }

    [Test]
    public void LockedPortalSendsNothing()
    {
        var (host, action, portals) = Field(Xblock(7));
        portals.Apply(Add(7));
        for (int i = 0; i < 5; i++) action.OnEnterUgcMap(new EnterUgcMapMessage { Mode = 4 });
        Enter(action);
        Assert.That(host.Sent, Is.Empty);
        Assert.That(host.Boxes[^1].id, Is.EqualTo(0xb0c));
        // the lock ends 10 s later
        host.ServerTime += 11;
        action.LastUsed = 0;
        Enter(action);
        Assert.That(host.Sent, Has.Count.EqualTo(1));
    }

    [Test]
    public void QuestGatedFieldSendsNothing()
    {
        var (host, action, portals) = Field(Xblock(7));
        host.Fields[2000].RequireQuest = 300;
        portals.Apply(Add(7));
        Enter(action);
        Assert.That(host.Sent, Is.Empty);
        Assert.That(host.Notices, Has.Count.EqualTo(1));
        host.Rewarded.Add(300);
        action.LastUsed = 0;
        Enter(action);
        Assert.That(host.Sent, Has.Count.EqualTo(1));
    }

    [Test]
    public void UnknownTargetFieldShowsTheLimitBox()
    {
        var (host, action, portals) = Field(Xblock(7));
        portals.Apply(Add(7, target: 9999));
        Enter(action);
        Assert.That(host.Sent, Is.Empty);
        Assert.That(host.Boxes, Is.EqualTo(new[] { (0x99d, 0, "") }));
    }

    [Test]
    public void ShadowPkFieldAsksFirst()
    {
        var (host, action, portals) = Field(Xblock(7));
        host.Fields[2000].ContinentCode = 5; host.Fields[2000].PkMode = 1; host.Shadow.Add(5);
        portals.Apply(Add(7));
        Enter(action);
        Assert.That(host.Sent, Is.Empty);
        Assert.That(host.Boxes, Is.EqualTo(new[] { (0x971, 1, "moveportal,2000,7") }));
        Assert.That(action.OnMoveBox(0, host.Boxes[0].tag), Is.True);
        Assert.That(host.Sent, Is.EqualTo(new IOutPacket[] { new RequestMoveFieldPortal(1000, 7) }));
    }

    [Test]
    public void PasswordPortalSendsTheTypedPassword()
    {
        var (host, action, portals) = Field(Xblock(7));
        portals.Apply(Add(7, password: true));
        Enter(action);
        Assert.That(host.Sent, Is.Empty);
        Assert.That(host.Inputs.Single().id, Is.EqualTo(0xb07));
        host.Inputs[0].answer(0, "1234,");
        Assert.That(host.Sent, Is.EqualTo(new IOutPacket[] { new RequestMoveFieldPortal(1000, 7, 0, 0, "", "1234") }));
    }

    [Test]
    public void UsedPortalIsNotUsedAgainUntilLeft()
    {
        var (host, action, portals) = Field(Xblock(7));
        portals.Apply(Add(7));
        Enter(action);
        Enter(action);
        Assert.That(host.Sent, Has.Count.EqualTo(1));
        Assert.That(action.Poll(new Vec3(5000, 0, 0)), Is.False);
        Enter(action);
        Assert.That(host.Sent, Has.Count.EqualTo(2));
    }

    [Test]
    public void GuideLimitAndCubeModeBlockThePortal()
    {
        var (host, action, portals) = Field(Xblock(7));
        portals.Apply(Add(7));
        host.Restricted = true;
        Enter(action);
        host.Restricted = false; host.InCubeMode = true;
        Enter(action);
        Assert.That(host.Sent, Is.Empty);
    }

    /// <summary>The guide's PortalUse limit (kind 9) holds the portal action while an event holds it and frees it after
    /// (PortalAction::vfunc_1, actionkeyobject.cpp:867).</summary>
    [Test, NonParallelizable]
    public void TheGuidePortalLimitHoldsThePortalWhileHeld()
    {
        var (host, action, portals) = Field(Xblock(7));
        portals.Apply(Add(7));
        host.UseGuide = true;
        var g = new Ms2.Ui.Guide.GuideManager(new GuideTests.World());
        try
        {
            Ms2.Ui.Guide.GuideManager.Current = g;
            Assert.That(action.Poll(Origin), Is.True, "no limit held");
            g.AddLimit(Ms2.Ui.Guide.GuideLimit.PortalUse);
            Assert.That(action.Poll(Origin), Is.False, "the limit held");
            g.AddLimit(Ms2.Ui.Guide.GuideLimit.PortalUse);
            g.RemoveLimit(Ms2.Ui.Guide.GuideLimit.PortalUse);
            Assert.That(action.Poll(Origin), Is.False, "held twice, released once");
            g.RemoveLimit(Ms2.Ui.Guide.GuideLimit.PortalUse);
            Assert.That(action.Poll(Origin), Is.True, "released");
            Ms2.Ui.Guide.GuideManager.Current = null;
            g.AddLimit(Ms2.Ui.Guide.GuideLimit.PortalUse);
            Assert.That(action.Poll(Origin), Is.True, "no session guide");
        }
        finally { Ms2.Ui.Guide.GuideManager.Current = null; }
    }

    [Test]
    public void SpawnedPortalUsesThePacketPoseAndStartTick()
    {
        var (host, action, portals) = Field();
        portals.Apply(Add(9, type: 3, startTick: 100));
        portals.Tick(50);
        Assert.That(action.Poll(Origin), Is.False);
        portals.Tick(101);
        Enter(action);
        Assert.That(host.Sent, Is.EqualTo(new IOutPacket[] { new RequestMoveFieldPortal(1000, 9) }));
        // mode 3 moves it away
        portals.Apply(new FieldPortalMessage { Mode = 3, Id = 9, Position = new Vector3(5000, 0, 0) });
        Assert.That(portals.At(Origin), Is.Empty);
    }

    [Test]
    public void OwnedHomePortalSendsModeTwo()
    {
        var (host, action, portals) = Field();
        portals.Apply(Add(9, type: 0xb, owner: 77, kind: 0));
        Enter(action);
        Assert.That(host.Sent, Is.EqualTo(new IOutPacket[] { new RequestMoveFieldHome(0, 0, 0, 77) }));
    }

    [Test]
    public void HousePrivilegePortalSendsNothing()
    {
        var e = Xblock(7);
        e.Raw.Props["IsHomePrivilege"] = "True";
        var (host, action, portals) = Field(e, Xblock(8));
        portals.Apply(Add(7));
        Enter(action);
        Assert.That(host.Sent, Is.Empty);
        Assert.That(host.Open, Has.Count.EqualTo(1));
        Assert.That(action.LastUsed, Is.EqualTo(7));
        // without the prop the same portal moves
        var (host2, action2, portals2) = Field(Xblock(7));
        portals2.Apply(Add(7));
        Enter(action2);
        Assert.That(host2.Sent, Is.EqualTo(new IOutPacket[] { new RequestMoveFieldPortal(1000, 7) }));
    }

    [Test]
    public void OwnedPortalOfAnotherTypeSendsNothing()
    {
        var (host, action, portals) = Field(Xblock(7));
        portals.Apply(Add(7, owner: 77));
        Enter(action);
        Assert.That(host.Sent, Is.Empty);
        Assert.That(host.Open, Has.Count.EqualTo(1));
    }

    [Test]
    public void UnboundPortalIsNotRegistered()
    {
        var (host, action, portals) = Field(Xblock(8));
        portals.Apply(Add(7));
        Assert.That(portals.ById(7), Is.Null);
        Assert.That(portals.All, Has.Count.EqualTo(0));
        // the same id added again (here as a spawned type) is taken
        portals.Apply(Add(7, type: 3));
        Assert.That(portals.ById(7)?.Type, Is.EqualTo(3));
        Enter(action);
        Assert.That(host.Sent, Is.EqualTo(new IOutPacket[] { new RequestMoveFieldPortal(1000, 7) }));
    }

    [Test]
    public void MessagesBeforeTheFieldAreHeld()
    {
        var host = new Host();
        host.Fields[1000] = new FieldData { Id = 1000 }; host.Fields[2000] = new FieldData { Id = 2000 };
        var portals = new LivePortals(1000, null, null, null);
        portals.Apply(Add(7));
        Assert.That(portals.All, Is.Empty);
        portals.Attach(new[] { Xblock(7) });
        Assert.That(portals.ById(7)?.HasEntity, Is.True);
    }

    [Test]
    public void FieldPortalAddReads()
    {
        var w = new ByteWriter();
        w.WriteByte(0); w.WriteInt(7); w.WriteByte(1); w.WriteByte(0);
        for (int i = 0; i < 9; i++) w.WriteFloat(i);
        w.WriteUnicodeString("m"); w.WriteInt(2000); w.WriteInt(3); w.WriteInt(1); w.WriteByte(1); w.WriteLong(77); w.WriteByte(0xb);
        w.WriteInt(10); w.WriteShort(4); w.WriteInt(20); w.WriteByte(1);
        w.WriteUnicodeString("o"); w.WriteUnicodeString("f"); w.WriteUnicodeString("KR");
        var m = FieldPortalMessage.Read(new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray()));
        Assert.That((m.Id, m.Visible, m.Enabled, m.Dimension, m.TargetMap, m.ObjectId, m.ActionKind, m.Owner, m.Type, m.EndTick,
            m.Extra, m.StartTick, m.Password, m.Locale),
            Is.EqualTo((7, true, false, new Vector3(6, 7, 8), 2000, 3, 1, 77L, (byte)0xb, 10, (ushort)4, 20, true, "KR")));
    }

    [Test]
    public void WrongHomePasswordUpdatesTheOpenBox()
    {
        var (host, action, _) = Field();
        action.OnEnterUgcMap(new EnterUgcMapMessage { Mode = 4 });
        Assert.That(host.BoxErrors, Is.EqualTo(new[] { 0xb0b }));
        Assert.That(host.Boxes, Is.Empty);
    }

    [Test]
    public void PrivateHomeIsAPlainBox()
    {
        var (host, action, _) = Field();
        action.OnEnterUgcMap(new EnterUgcMapMessage { Mode = 11 });
        Assert.That(host.TextBoxes, Has.Count.EqualTo(1));
        Assert.That(host.Notices, Is.Empty);
    }

    [Test]
    public void HomePasswordBoxNeedsThePlayer()
    {
        var (host, action, _) = Field();
        host.HasPlayer = false;
        action.OnEnterUgcMap(new EnterUgcMapMessage { Mode = 3, PortalId = 5 });
        Assert.That(host.Inputs, Is.Empty);
        Assert.That(action.PendingMove, Is.Null);
    }

    [Test]
    public void EnterUgcMapPasswordBoxReadsAndAnswers()
    {
        var w = new ByteWriter();
        w.WriteByte(3); w.WriteInt(5); w.WriteInt(1); w.WriteInt(2); w.WriteLong(3); w.WriteInt(4); w.WriteByte(1);
        var m = EnterUgcMapMessage.Read(new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray()));
        var (host, action, _) = Field();
        action.OnEnterUgcMap(m);
        host.Inputs.Single().answer(0, "pw");
        Assert.That(host.Sent, Is.EqualTo(new IOutPacket[] { new RequestMoveFieldHome(5, 1, 2, 3, "pw") }));
    }
}
