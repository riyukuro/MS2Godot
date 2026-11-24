using System.Numerics;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>CAutoFindTaxiStation (autofindtaxistation.cpp:203-414) over a recording host. No data needed.</summary>
public class AutoFindTaxiStationTests
{
    const int Field = 2000062;

    sealed class Host : IAutoFindTaxiHost
    {
        public bool FieldAutoFind { get; set; } = true;
        public List<Vector3> StationList = new() { new Vector3(1000, 2000, 300) };
        public IReadOnlyList<Vector3> Stations => StationList;
        public int FieldId { get; set; } = Field;
        public readonly HashSet<int> FoundList = new();
        public readonly List<int> RequestedList = new();
        public bool Home;
        public readonly List<IOutPacket> Sent = new();
        public readonly List<string> Log = new();
        public bool Flag;
        public bool HasMode = true;
        public Vector3 Mode = new(0, 0, 500), Target = new(10, 20, 30);

        public bool Found(int field) => FoundList.Contains(field);
        public bool Requested(int field) => RequestedList.Contains(field);
        public void AddRequested(int field) => RequestedList.Add(field);
        public bool HomeTaxiField(int field) => Home && field == Field;
        public void Send(IOutPacket packet) => Sent.Add(packet);
        public void SetPlayerStateFlag(bool on) { Flag = on; Log.Add(on ? "flag+" : "flag-"); }
        public bool ModePosition(out Vector3 position) { position = Mode; return HasMode; }
        public float ModeDistance { get; set; } = 800;
        public float ModeYaw { get; set; } = 0.5f;
        public float ModePitch { get; set; } = 1.0f;
        public Vector3 TargetModePosition => Target;
        public readonly List<(Vector3 To, float Seconds, int Mode, bool Keep)> Flights = new();
        public void FlyTo(Vector3 position, float seconds, int mode, bool keep)
        {
            Flights.Add((position, seconds, mode, keep));
            Log.Add($"fly {mode}");
            Mode = position;
        }
        public readonly List<int> Pending = new();
        public void SetPendingMode(int mode) { Pending.Add(mode); Log.Add($"pending {mode}"); }
    }

    static (AutoFindTaxiStation, Host) New(Action<Host> setup = null)
    {
        var h = new Host();
        setup?.Invoke(h);
        return (new AutoFindTaxiStation(h), h);
    }

    /// <summary>FUN_1409f1450's camera spot over the station (:255-264).</summary>
    static Vector3 Over(Host h, Vector3 station) => new(h.ModeDistance * MathF.Sin(h.ModeYaw) + station.X,
        h.ModeDistance * MathF.Cos(h.ModeYaw) + station.Y, h.ModeDistance * MathF.Cos(h.ModePitch) + station.Z);

    [Test]
    public void ANewFieldFliesOverTheStationThenAsksForTheTaxi()
    {
        var (a, h) = New();
        a.StartAutoFind();
        Assert.That(a.Active, Is.True);
        Assert.That(h.Flag, Is.True, "AddStateFlags(0x100) at the start (:225)");
        a.Advance(999);
        Assert.That(h.Flights, Is.Empty, "the fly waits 1000 ms (:223)");

        var from = h.Mode;
        a.Advance(1);
        var to = Over(h, h.StationList[0]);
        float ms = (from - to).Length() * 0.8f;
        Assert.That(h.Flights, Has.Count.EqualTo(1));
        Assert.That(h.Flights[0].To, Is.EqualTo(to));
        Assert.That(h.Flights[0].Seconds, Is.EqualTo(ms / 1000.0f));
        Assert.That(h.Flights[0].Mode, Is.EqualTo(AutoFindTaxiStation.LockMode));
        Assert.That(h.Flights[0].Keep, Is.False);
        Assert.That(h.Sent, Is.Empty, "the request waits for the camera");

        a.Advance((uint)ms - 1);
        Assert.That(h.Sent, Is.Empty);
        a.Advance(1);
        Assert.That(h.Sent, Has.Count.EqualTo(1));
        Assert.That(h.Sent[0], Is.TypeOf<RequestTaxiAutoFind>());
        Assert.That(h.RequestedList, Is.EqualTo(new[] { Field }), "SendRequestTaxiMode5 adds the field (:298)");
        Assert.That(a.Active, Is.True);
    }

    [Test]
    public void TheAnswerFliesTheCameraBackToTheFollowModeAndResets()
    {
        var (a, h) = New();
        a.StartAutoFind();
        a.Advance(1000);
        a.Advance(100000);                      // one frame: the request goes out, its 5 s wait counts from this frame
        Assert.That(h.Sent, Has.Count.EqualTo(1));
        Assert.That(a.Active, Is.True);
        a.Advance(AutoFindTaxiStation.AnswerWaitMs - 1);
        Assert.That(a.Active, Is.True);
        a.Advance(1);
        Assert.That(a.Active, Is.False, "no answer in 5 s: FUN_1409f1a00 resets");
        Assert.That(h.Pending, Is.EqualTo(new[] { AutoFindTaxiStation.TargetMode }));

        (a, h) = New();
        a.StartAutoFind();
        a.Advance(1000);
        a.Advance((uint)(h.Flights[0].Seconds * 1000.0f));
        Assert.That(h.Sent, Has.Count.EqualTo(1));
        h.FoundList.Add(Field);                 // OnTaxi adds the field, then calls FUN_1409f1970 (scenestategame.cpp:7923)
        a.OnTaxiFound();
        a.Advance(AutoFindTaxiStation.ReturnDelayMs - 1);
        Assert.That(h.Flights, Has.Count.EqualTo(1));
        var from = h.Mode;
        a.Advance(1);
        Assert.That(h.Flights, Has.Count.EqualTo(2));
        float ms = (from - h.Target).Length() * 0.5f;
        Assert.That(h.Flights[1], Is.EqualTo((h.Target, ms / 1000.0f, AutoFindTaxiStation.TargetMode, true)));
        Assert.That(a.Active, Is.True, "the reset waits for the camera");
        Assert.That(h.Flag, Is.True);
        a.Advance(5000);                        // the 5 s wait ends on a found field: nothing
        Assert.That(h.Pending, Is.Empty);
        Assert.That(a.Active, Is.False);
        Assert.That(h.Flag, Is.False);
    }

    [Test]
    public void WithoutACameraModeTheRequestGoesOutAtOnceAndTheFollowModeIsSetBack()
    {
        var (a, h) = New(x => x.HasMode = false);
        a.StartAutoFind();
        a.Advance(1000);
        Assert.That(h.Flights, Is.Empty);
        Assert.That(h.Sent, Has.Count.EqualTo(1));
        h.FoundList.Add(Field);
        a.OnTaxiFound();
        a.Advance(AutoFindTaxiStation.ReturnDelayMs);
        Assert.That(h.Pending, Is.EqualTo(new[] { AutoFindTaxiStation.TargetMode }));
        Assert.That(a.Active, Is.False);
    }

    [Test]
    public void EachStationGetsItsOwnFlightAndRequest()
    {
        var (a, h) = New(x => x.StationList.Add(new Vector3(-500, 0, 0)));
        a.StartAutoFind();
        a.Advance(1000);
        Assert.That(h.Flights, Has.Count.EqualTo(2), "the loop runs over every MS2TaxiStation entity (:247)");
        a.Advance(100);
        Assert.That(h.Sent, Has.Count.LessThanOrEqualTo(2));
        a.Advance(20000);
        Assert.That(h.Sent, Has.Count.EqualTo(2));
    }

    [Test]
    public void NothingStartsWhereTheFieldIsOffHasNoStationOrIsKnown()
    {
        foreach (var setup in new Action<Host>[]
                 {
                     x => x.FieldAutoFind = false,
                     x => x.StationList.Clear(),
                     x => x.FoundList.Add(Field),
                     x => x.RequestedList.Add(Field),
                 })
        {
            var (a, h) = New(setup);
            a.StartAutoFind();
            a.Advance(10000);
            Assert.That(a.Active, Is.False);
            Assert.That(a.TimerCount, Is.Zero);
            Assert.That(h.Log, Is.Empty);
            Assert.That(h.Sent, Is.Empty);
        }
    }

    [Test]
    public void AHouseTaxiFieldOnlyDropsTheStateFlag()
    {
        var (a, h) = New(x => x.Home = true);
        a.StartAutoFind();
        Assert.That(h.Flag, Is.True, "StartAutoFind does not test the house privilege (:219)");
        a.Advance(1000);
        Assert.That(h.Flag, Is.False);
        Assert.That(h.Flights, Is.Empty);
        Assert.That(h.Sent, Is.Empty);
        Assert.That(a.Active, Is.True, "FUN_1409f1450 leaves m_nActive set");
        Assert.That(a.EscapeTick(true), Is.True);
        Assert.That(h.Sent, Is.Empty, "Esc sends nothing on a house-taxi field (:376)");
        Assert.That(a.Active, Is.False);
    }

    [Test]
    public void EscapeAsksAtOnceAndEndsTheFind()
    {
        var (a, h) = New();
        Assert.That(a.EscapeTick(true), Is.False, "not active: Esc is not taken");
        a.StartAutoFind();
        Assert.That(a.EscapeTick(false), Is.False);
        Assert.That(a.EscapeTick(true), Is.True);
        Assert.That(h.Sent, Has.Count.EqualTo(1));
        Assert.That(h.Sent[0], Is.TypeOf<RequestTaxiAutoFind>());
        Assert.That(h.RequestedList, Is.EqualTo(new[] { Field }));
        Assert.That(h.Pending, Is.EqualTo(new[] { AutoFindTaxiStation.TargetMode }));
        Assert.That(a.Active, Is.False);
        Assert.That(h.Flag, Is.False);
        a.Advance(1000);                        // the fly still due runs on an inactive find: nothing
        Assert.That(h.Flights, Is.Empty);

        (a, h) = New();
        a.StartAutoFind();
        a.Advance(1000);
        a.Advance((uint)(h.Flights[0].Seconds * 1000.0f));
        Assert.That(h.Sent, Has.Count.EqualTo(1));
        Assert.That(a.EscapeTick(true), Is.True);
        Assert.That(h.Sent, Has.Count.EqualTo(1), "an asked field is not asked again");
    }

    [Test]
    public void AStartDuringACutsceneWaitsForItsEnd()
    {
        var (a, h) = New();
        a.Pause();
        a.StartAutoFind();
        Assert.That(a.Pending, Is.True);
        Assert.That(a.Active, Is.False);
        Assert.That(a.EscapeTick(false), Is.False);
        Assert.That(a.Active, Is.False, "still paused");
        a.Resume();
        a.EscapeTick(false);
        Assert.That(a.Active, Is.True);
        Assert.That(a.Pending, Is.False);
        Assert.That(h.Flag, Is.True);
    }

    [Test]
    public void AHitOnTheOwnPcEndsAFindOnAFieldNotFound()
    {
        var (a, h) = New();
        a.CancelUnlessFound();
        Assert.That(h.Log, Is.Empty, "not active: nothing (:316)");

        a.StartAutoFind();
        a.Advance(1000);
        Assert.That(h.Flights, Has.Count.EqualTo(1));
        a.CancelUnlessFound();                  // PC::vfunc_33 (pc.cpp:896) while the camera is away
        Assert.That(a.Active, Is.False, "m_nDone is set, then Reset clears the state (:321-323)");
        Assert.That(h.Pending, Is.EqualTo(new[] { AutoFindTaxiStation.TargetMode }));
        Assert.That(h.Flag, Is.False);
        a.Advance(100000);
        Assert.That(h.Sent, Is.Empty, "the request still due runs on a reset find: nothing");

        (a, h) = New();
        a.StartAutoFind();
        a.Advance(1000);
        h.FoundList.Add(Field);                 // the field was found meanwhile: the hit changes nothing (:320)
        a.CancelUnlessFound();
        Assert.That(a.Active, Is.True);
        Assert.That(h.Pending, Is.Empty);
    }

    [Test]
    public void AHouseTaxiFieldIsAFieldMetaStationWithTheHousePrivilege()
    {
        var meta = Ms2.Quest.FieldMeta.Parse(
            "<ms2><taxiStation field='2' IsHomePrivilege='1'/><taxiStation field='3' IsHomePrivilege='0'/><taxiStation field='4'/></ms2>");
        Assert.That(TaxiManager.IsHomeTaxiField(meta, 2), Is.True);
        Assert.That(TaxiManager.IsHomeTaxiField(meta, 3), Is.False);
        Assert.That(TaxiManager.IsHomeTaxiField(meta, 4), Is.False);
        Assert.That(TaxiManager.IsHomeTaxiField(meta, 5), Is.False, "no station row");
        Assert.That(TaxiManager.IsHomeTaxiField(null, 2), Is.False);
    }

    [Test]
    public void TheFieldLeaveDropsTheStateAndTheTimers()
    {
        var (a, h) = New();
        a.StartAutoFind();
        a.FieldLeave();
        Assert.That(a.Active, Is.False);
        Assert.That(a.TimerCount, Is.Zero);
        a.Advance(10000);
        Assert.That(h.Flights, Is.Empty);
        Assert.That(h.Sent, Is.Empty);
    }
}
