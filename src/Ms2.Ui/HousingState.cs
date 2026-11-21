using System.Collections.Generic;
using Ms2.Net.Packets;

namespace Ms2.Ui;

/// <summary>
/// The housing globals the cube lock check reads (MyPC::FUN_1415302e0, mypc.cpp:4445-4497), as OnResponseCube fills them
/// (scenestategame.cpp:10560-10606, :10746-10759).
/// <para>The own part (0x27): the housing grade (DAT_143ab291c), its exp (DAT_143ab2930), the day-change time
/// (DAT_143ab2928) and the grades whose reward is taken (DAT_143ab32b8). The house owner part (0x35 / 0x37): whether the pc is
/// in a house (DAT_143ab2857), its owner (DAT_143ab2938), the owner's grade (DAT_143ab2940) and trophy set
/// (DAT_143ab32d8).</para>
/// <para>STILL-OPEN: the dialogs 0x27 refreshes (mastery, game menu, housing point), 0x35's two last values (cube map service
/// f420 / f428) and the home info dialog's UpdateBalance; none is ported.</para>
/// </summary>
public sealed class HousingState
{
    /// <summary>DAT_143ab291c: the own housing grade.</summary>
    public ushort Grade { get; private set; }
    /// <summary>DAT_143ab2930: the own housing exp.</summary>
    public long Exp { get; private set; }
    /// <summary>DAT_143ab2928: the time 0x27 sent, checked with IsNotRecentDayChange.</summary>
    public long DayChangeTime { get; private set; }
    /// <summary>DAT_143ab32b8 (a CAtlArray, so in packet order with repeats): the housing grades whose reward is taken.</summary>
    public readonly List<int> RewardedGrades = new();

    /// <summary>DAT_143ab2857: set by a 0x35 for the own account.</summary>
    public bool InHouse { get; private set; }
    /// <summary>DAT_143ab2938: the house owner's id.</summary>
    public long OwnerId { get; private set; }
    /// <summary>DAT_143ab2940: the house owner's housing grade.</summary>
    public ushort OwnerGrade { get; private set; }
    /// <summary>DAT_143ab32d8: the house owner's (trophy id, grade) pairs.</summary>
    public readonly HashSet<(int Id, int Level)> OwnerTrophies = new();

    /// <summary>FUN_140930c50: in a house with a known owner.</summary>
    public bool InOwnersHouse => InHouse && OwnerId != 0;

    /// <summary>0x27 (:10560-10589): another account's packet changes nothing.</summary>
    public void Apply(ResponseCubeHousingGrade m)
    {
        if (m == null || !m.Mine) return;
        DayChangeTime = m.DayChangeTime;
        Grade = m.Grade;
        Exp = m.Exp;
        RewardedGrades.Clear();
        RewardedGrades.AddRange(m.RewardedGrades);
    }

    /// <summary>0x35 (:10590-10606): for the own account the pc is in the owner's house.</summary>
    public void Apply(ResponseCubeHouseOwner m, long ownAccount)
    {
        if (m == null || m.AccountId != ownAccount) return;
        InHouse = true;
        OwnerId = m.OwnerId;
    }

    /// <summary>0x37 (:10746-10759): the owner's grade and trophy set, replacing the old set.</summary>
    public void Apply(ResponseCubeOwnerTrophies m)
    {
        if (m == null) return;
        OwnerGrade = m.OwnerGrade;
        OwnerTrophies.Clear();
        foreach (var t in m.Trophies) OwnerTrophies.Add(t);
    }

    /// <summary>FUN_14092f420 from OnServerEnter (scenestategame.cpp:2070): the house owner part is cleared; the own part
    /// stays.</summary>
    public void ServerEnter()
    {
        InHouse = false;
        OwnerId = 0;
        OwnerGrade = 0;
        OwnerTrophies.Clear();
    }
}
