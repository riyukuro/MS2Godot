using System;
using Ms2.Ui.Hud;

namespace Ms2Godot.Ui;

/// <summary>The house visit question's outside (<see cref="IHouseVisitHost"/>) for the live session: the home state of
/// <see cref="Net.LiveSession.Home"/> and <see cref="Net.LiveSession.Housing"/>, the options host's agreement and feature table, the game menu's red circle.</summary>
sealed class HouseVisitHost(Net.LiveSession live, GameOptionHost options, Func<string, string> common, Func<bool> hasTutorial, Action<bool> redCircle) : IHouseVisitHost
{
    public bool HasTutorial => hasTutorial();
    public bool PersonalInfoFeature => options.IsFeatureEnabled(0x117);
    public bool HasLocalPlayer => live.SelfObjectId >= 0;
    public bool HasNoHome => live.Home.HasNoHome;
    /// <summary>FUN_1410d2f10 and FUN_1410c3180 == DAT_143ab2820: in a house whose owner is this account.</summary>
    public bool InMyHouse => live.Housing.InOwnersHouse && live.Housing.OwnerId == live.OwnAccountId;
    public bool HouseGroupAgreed => options.Agreement.HasGroupAgreement(1);
    public void SetHouseMenuRedCircle(bool show) => redCircle(show);
    public void SendHouseSetting(int kind, string value, bool flag) => options.SendHouseSetting(kind, value, flag);
    public string Text(string key) => common(key) ?? "";
    public string CommonText(string key) => common(key) ?? "";
    public string InputText(MessageBoxData box) => MessageBoxQueue.Layer?.InputText(box) ?? "";
}
