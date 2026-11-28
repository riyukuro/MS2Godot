using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>CPetManager::OnResponsePet's summon modes (petmanager.cpp:722-1055) and SendRequestPet's attack toggle (:1118).</summary>
public class PetManagerTests
{
    const int Me = 100, Other = 200, Absent = 300;
    const long OldUid = 11, NewUid = 22;

    sealed class Host : IPetResponseHost
    {
        public readonly List<string> Log = new();
        public readonly Dictionary<int, long> Uids = new() { [Me] = OldUid, [Other] = OldUid };
        public readonly Dictionary<long, int> Types = new();
        public bool Shown = true;
        public bool PcExists(int id) => id == Me || id == Other;
        public bool IsLocalPc(int id) => id == Me;
        public void SetPcPet(int id, bool replace, EquipItem item, int petObject, long uid)
        {
            Uids[id] = uid;
            Log.Add($"pet {id} {(replace ? item?.Id.ToString() ?? "clear" : "keep")} {petObject} {uid}");
        }
        public long PcSummonedUid(int id) => Uids.TryGetValue(id, out var u) ? u : 0;
        public void RefreshItemSlot(long uid) => Log.Add($"slot {uid}");
        public int? PetTypeOfItem(long uid) => Types.TryGetValue(uid, out var t) ? t : null;
        public void InitPetInfo(bool up, bool attack, int auto) => Log.Add($"menu {up} {attack} {auto}");
        public void Notice(int id) => Log.Add($"notice {id:x}");
        public void MessageBox(int id) => Log.Add($"box {id:x}");
        public void OpenCharge() => Log.Add("charge");
        public bool OwnPetShown(int owner) => owner == Me && Shown;
        public void PlaySummonSound(int owner) => Log.Add($"sound {owner}");
    }

    static PetResponse R(PetResponseKind kind, int owner = Me, int value = 0, long uid = 0, byte flag = 0, PetItemRef item = null) =>
        new(kind, 0, owner, value, uid, 0, flag, null, Array.Empty<PetOption>(), null, Array.Empty<KeyValuePair<int, ushort>>(), item);

    static PetItemRef Known(long uid, int id = 50300001) => new(id, uid, 1, new EquipItem { Id = id, Uid = uid });
    static PetItemRef Unknown(long uid) => new(99999999, uid, 1, null);

    [Test]
    public void TheOwnSummonSetsTheUidTheSlotAndTheMenu()
    {
        var h = new Host(); h.Types[NewUid] = 1;
        var m = new PetManager();
        m.OnResponsePet(R(PetResponseKind.Summoned, value: 7, item: Known(NewUid)), h);
        Assert.That(m.SummonUid, Is.EqualTo(NewUid));
        Assert.That(h.Log, Is.EqualTo(new[] { $"pet {Me} 50300001 7 {NewUid}", $"slot {NewUid}", "menu True True 1" }));
    }

    [Test]
    public void ASummonedRowOfAnotherTypeHidesTheAttackAndNoRowSkipsTheMenu()
    {
        var h = new Host(); h.Types[NewUid] = 2;
        var m = new PetManager();
        m.OnResponsePet(R(PetResponseKind.Summoned, value: 7, item: Known(NewUid)), h);
        Assert.That(h.Log[^1], Is.EqualTo("menu True False 1"));
        h.Types.Clear(); h.Log.Clear();
        m.OnResponsePet(R(PetResponseKind.Summoned, value: 7, item: Known(NewUid)), h);
        Assert.That(h.Log, Has.None.StartsWith("menu"), "GetSummonedPetRow null: no InitPetInfo");
    }

    [Test]
    public void ReadPetItemWithoutAnItemClearsItAndAnUnknownIdKeepsBoth()
    {
        var h = new Host();
        var m = new PetManager();
        m.OnResponsePet(R(PetResponseKind.Summoned, value: 7), h);
        Assert.That(h.Log[0], Is.EqualTo($"pet {Me} clear 7 {OldUid}"), "pc.cpp:3357: no item: m_spItem11b8 0, m_nUidB78 kept");
        Assert.That(m.SummonUid, Is.EqualTo(OldUid));
        h.Log.Clear();
        m.OnResponsePet(R(PetResponseKind.Summoned, value: 8, item: Unknown(NewUid)), h);
        Assert.That(h.Log[0], Is.EqualTo($"pet {Me} keep 8 {OldUid}"), "pc.cpp:3375: unknown id returns before SetPetItem");
        Assert.That(m.SummonUid, Is.EqualTo(OldUid));
    }

    [Test]
    public void AnotherPcsSummonChangesOnlyItsPet()
    {
        var h = new Host(); h.Types[NewUid] = 1;
        var m = new PetManager();
        m.OnResponsePet(R(PetResponseKind.Summoned, owner: Other, value: 7, item: Known(NewUid)), h);
        Assert.That(h.Log, Is.EqualTo(new[] { $"pet {Other} 50300001 7 {NewUid}" }));
        Assert.That(m.SummonUid, Is.EqualTo(0));
        h.Log.Clear();
        m.OnResponsePet(R(PetResponseKind.Summoned, owner: Absent, value: 7, item: Known(NewUid)), h);
        m.OnResponsePet(R(PetResponseKind.Unsummoned, owner: Absent, uid: NewUid), h);
        Assert.That(h.Log, Is.Empty, "FindPCById null: nothing");
    }

    [Test]
    public void TheOwnUnsummonClearsTheMenuAndNotices()
    {
        var h = new Host(); h.Types[NewUid] = 1;
        var m = new PetManager();
        m.OnResponsePet(R(PetResponseKind.Summoned, value: 7, item: Known(NewUid)), h);
        h.Log.Clear();
        m.OnResponsePet(R(PetResponseKind.Unsummoned, uid: NewUid), h);
        Assert.That(m.SummonUid, Is.EqualTo(0));
        Assert.That(h.Log, Is.EqualTo(new[] { $"pet {Me} clear 0 0", $"slot {NewUid}", "menu False False 0", "notice c0c" }));
        h.Log.Clear();
        m.OnResponsePet(R(PetResponseKind.Unsummoned, owner: Other, uid: NewUid), h);
        Assert.That(h.Log, Is.EqualTo(new[] { $"pet {Other} clear 0 0" }), "another pc: no slot, menu or notice");
    }

    [Test]
    public void TheSummonSoundPlaysOnlyForTheOwnShownPet()
    {
        var h = new Host();
        var m = new PetManager();
        m.OnResponsePet(R(PetResponseKind.SummonSound), h);
        m.OnResponsePet(R(PetResponseKind.SummonSound, owner: Other), h);
        h.Shown = false;
        m.OnResponsePet(R(PetResponseKind.SummonSound), h);
        Assert.That(h.Log, Is.EqualTo(new[] { $"sound {Me}" }));
    }

    [Test]
    public void AutoChangedSetsTheAttackModeAndTheToggleSendsIt()
    {
        var m = new PetManager();
        Assert.That(m.Auto, Is.EqualTo(1), "CPetManager::CPetManager: m_nAuto 1");
        m.OnResponsePet(R(PetResponseKind.AutoChanged, flag: 0), new Host());
        Assert.That(m.Auto, Is.EqualTo(0));
        var sent = new List<IOutPacket>();
        Assert.That(m.ToggleAuto(sent.Add), Is.EqualTo(1));
        Assert.That(sent, Is.EqualTo(new IOutPacket[] { new PetAttackRequest(true) }));
        m.OnResponsePet(R(PetResponseKind.AutoChanged, flag: 1), new Host());
        Assert.That(m.Auto, Is.EqualTo(1));
    }

    [TestCase(0x1b, "notice 34")]
    [TestCase(0x1c, "charge")]
    [TestCase(0x20, "box c24")]
    [TestCase(0x1e, "notice f2")]
    [TestCase(0x21, "notice c21")]
    [TestCase(0x05, "notice 979")]
    public void EachErrorShowsTheClientsText(int error, string shown)
    {
        var h = new Host();
        new PetManager().OnResponsePet(R(PetResponseKind.Error, value: error), h);
        Assert.That(h.Log, Is.EqualTo(new[] { shown }));
    }

    [Test]
    public void Error0x27ShowsNothing()
    {
        var h = new Host();
        new PetManager().OnResponsePet(R(PetResponseKind.Error, value: 0x27), h);
        Assert.That(h.Log, Is.Empty);
    }

    [TestCase(PetResponseKind.Renamed)] [TestCase(PetResponseKind.Info)] [TestCase(PetResponseKind.Experience)]
    [TestCase(PetResponseKind.OtherPetInfo)] [TestCase(PetResponseKind.PetItem)] [TestCase(PetResponseKind.Unknown)]
    public void ThePetWindowsModesChangeNothingHere(PetResponseKind kind)
    {
        var h = new Host();
        var m = new PetManager();
        m.OnResponsePet(R(kind, value: 7, uid: NewUid, item: Known(NewUid)), h);
        Assert.That(h.Log, Is.Empty);
        Assert.That(m.SummonUid, Is.EqualTo(0));
    }
}
