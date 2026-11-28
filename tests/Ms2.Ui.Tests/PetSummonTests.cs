using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>MyPC::FUN_14152aa90 (mypc.cpp:3735-3796), FUN_14152b2d0 / FUN_14152b8d0 (mypc.cpp:3220-3282) and the pet summon
/// state table (unit_14072f7f0_petsummonstate.cpp).</summary>
public class PetSummonTests
{
    static PetItemFacts Use() => new() { PetRowKnown = true, NpcKnown = true, FieldUsesPet = true, ItemUid = 0x51 };

    [Test]
    public void NoPetOutSummonsAndRidingRidesOffFirst()
    {
        var f = Use();
        Assert.That(PetItemUse.Decide(f, out int id, out string key), Is.EqualTo(PetItemUse.Outcome.Summon));
        Assert.That((id, key), Is.EqualTo((0, (string)null)));
        f.Riding = true;
        Assert.That(PetItemUse.Decide(f, out _, out _), Is.EqualTo(PetItemUse.Outcome.RideOffSummon));
    }

    [Test]
    public void TheSamePetUnsummonsEvenRidingAndAnotherTransfers()
    {
        var f = Use();
        f.SummonedUid = 0x51;
        f.Riding = true;
        Assert.That(PetItemUse.Decide(f, out _, out _), Is.EqualTo(PetItemUse.Outcome.Unsummon));
        f.SummonedUid = 0x52;
        Assert.That(PetItemUse.Decide(f, out _, out _), Is.EqualTo(PetItemUse.Outcome.RideOffTransfer));
        f.Riding = false;
        Assert.That(PetItemUse.Decide(f, out _, out _), Is.EqualTo(PetItemUse.Outcome.Transfer));
    }

    [Test]
    public void TheUseChecksComeInTheClientsOrder()
    {
        var f = Use();
        f.PetRowKnown = false;
        f.FieldUsesPet = false;
        f.HouseForbidsPet = true;
        f.BindsOnUse = true;
        Assert.That(PetItemUse.Decide(f, out _, out _), Is.EqualTo(PetItemUse.Outcome.None));
        f.PetRowKnown = true;
        f.NpcKnown = false;
        Assert.That(PetItemUse.Decide(f, out _, out _), Is.EqualTo(PetItemUse.Outcome.None));
        f.NpcKnown = true;
        Assert.That(PetItemUse.Decide(f, out int id, out string key), Is.EqualTo(PetItemUse.Outcome.Notice));
        Assert.That((id, key), Is.EqualTo((0x654, (string)null)));
        f.FieldUsesPet = true;
        Assert.That(PetItemUse.Decide(f, out id, out key), Is.EqualTo(PetItemUse.Outcome.Notice));
        Assert.That((id, key), Is.EqualTo((0, PetItemUse.HouseForbidsKey)));
        f.HouseForbidsPet = false;
        f.SummonedUid = 0x51;                     // the bind box comes before the unsummon too
        Assert.That(PetItemUse.Decide(f, out _, out _), Is.EqualTo(PetItemUse.Outcome.BindConfirm));
        f.Confirmed = true;
        Assert.That(PetItemUse.Decide(f, out _, out _), Is.EqualTo(PetItemUse.Outcome.Unsummon));
    }

    static PetSummonFacts Summon() => new() { ItemFound = true, Level = 10, RequiredLevel = 10, FieldUsesPet = true, State = 1 };

    [Test]
    public void TheSummonChecksComeInTheClientsOrder()
    {
        var f = Summon();
        f.ItemFound = false;
        f.PcBangRefused = f.VipRefused = f.WeddingRefused = f.OnWater = true;
        f.RequiredLevel = 11;
        f.FieldUsesPet = false;
        f.State = 0x2d;
        Assert.That(PetSummonChecks.Summon(f, out _), Is.EqualTo(PetSummonChecks.Result.None));
        f.ItemFound = true;
        var expected = new[]
        {
            new PetNotice(0xe2, null, null), new PetNotice(0, "s_item_err_puton_vip", null),
            new PetNotice(0, "s_item_err_puton_wedding", null), new PetNotice(0x654, null, null),
            new PetNotice(0, "s_pet_error_summon_level_limit", 11u), new PetNotice(0x654, null, null),
            new PetNotice(0, "s_pet_cant_summon_state_fishing", null),
        };
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.That(PetSummonChecks.Summon(f, out var n), Is.EqualTo(PetSummonChecks.Result.Notice), $"check {i}");
            Assert.That(n, Is.EqualTo(expected[i]), $"check {i}");
            switch (i)
            {
                case 0: f.PcBangRefused = false; break;
                case 1: f.VipRefused = false; break;
                case 2: f.WeddingRefused = false; break;
                case 3: f.OnWater = false; break;
                case 4: f.RequiredLevel = 10; break;
                case 5: f.FieldUsesPet = true; break;
                case 6: f.State = 1; break;
            }
        }
        Assert.That(PetSummonChecks.Summon(f, out var none), Is.EqualTo(PetSummonChecks.Result.Start));
        Assert.That(none, Is.EqualTo(default(PetNotice)));
    }

    [Test]
    public void TheLevelCheckReadsTheOwnLevelAsAnUnsignedShort()
    {
        var f = Summon();
        f.Level = 0x10005;                        // (ushort) 5
        f.RequiredLevel = 6;
        Assert.That(PetSummonChecks.Summon(f, out var n), Is.EqualTo(PetSummonChecks.Result.Notice));
        Assert.That(n.Arg, Is.EqualTo(6u));
    }

    [Test]
    public void AStateWithoutAKeyRefusesWithTheFieldNotice()
    {
        var f = Summon();
        f.State = 0x26;                           // a running StateSummon is not refused by the table
        Assert.That(PetSummonChecks.Summon(f, out _), Is.EqualTo(PetSummonChecks.Result.Start));
        foreach (int s in new[] { 3, 5, 6, 7, 8, 9, 0x12, 0x1b, 0x1c, 0x1d, 0x1e, 0x1f, 0x20, 0x2b })
        {
            f.State = s;
            Assert.That(PetSummonChecks.Summon(f, out var n), Is.EqualTo(PetSummonChecks.Result.Notice), $"state {s:x}");
            Assert.That(n, Is.EqualTo(new PetNotice(0x654, null, null)), $"state {s:x}");
        }
        f.State = 0x2f;
        PetSummonChecks.Summon(f, out var a);
        f.State = 0x10;
        PetSummonChecks.Summon(f, out var b);
        Assert.That((a.Key, b.Key), Is.EqualTo(("s_pet_cant_summon_state_play_instrument", "s_pet_cant_summon_state_skill")));
    }

    [Test]
    public void TheStateTableHasItsSeventeenStates()
    {
        var refused = new System.Collections.Generic.List<int>();
        for (int s = 0; s < 0x100; s++) if (!PetSummonChecks.StateAllows(s)) refused.Add(s);
        Assert.That(refused, Is.EqualTo(new[] { 3, 5, 6, 7, 8, 9, 0x10, 0x12, 0x1b, 0x1c, 0x1d, 0x1e, 0x1f, 0x20, 0x2b, 0x2d, 0x2f }));
        for (int s = 0; s < 0x100; s++)
            if (s is not (0x2f or 0x2d or 0x10)) Assert.That(PetSummonChecks.StateRefusalKey(s), Is.Empty);
    }

    [Test]
    public void TheTransferAsksOnlyTheStateAndTheField()
    {
        var f = Summon();
        f.PcBangRefused = f.VipRefused = f.WeddingRefused = f.OnWater = true;
        f.RequiredLevel = 99;
        Assert.That(PetSummonChecks.Transfer(f, out _), Is.EqualTo(PetSummonChecks.Result.Start));
        f.State = 0x2d;                           // no key on the transfer side
        Assert.That(PetSummonChecks.Transfer(f, out var n), Is.EqualTo(PetSummonChecks.Result.Notice));
        Assert.That(n, Is.EqualTo(new PetNotice(0x654, null, null)));
        f.State = 1;
        f.FieldUsesPet = false;
        Assert.That(PetSummonChecks.Transfer(f, out n), Is.EqualTo(PetSummonChecks.Result.Notice));
        Assert.That(n.Id, Is.EqualTo(0x654));
        f.ItemFound = false;
        Assert.That(PetSummonChecks.Transfer(f, out _), Is.EqualTo(PetSummonChecks.Result.None));
    }
}
