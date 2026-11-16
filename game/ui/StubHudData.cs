using Ms2.Net.Packets;

namespace Ms2Godot.Ui;

/// <summary>HUD numbers until the field packets feed them: the player is a Ms2.Net <see cref="UserBlock"/> (the same block FieldAddUser carries).</summary>
public sealed class StubHudData
{
    public UserBlock Player = new() { CharacterId = 1001, Name = "Player", Level = 21, Job = 1, JobCode = 1, HpMax = 981, HpCurrent = 981, Channel = 1, GearScore = 0 };
    public int SpMax = 100, SpCurrent = 100;
    public double ExpPercent = 3.87;
    public long Mesos = 0, Merets = 0;
    public string FieldName = "DP-kr Tria";
    public string Locale = "KR";
    /// <summary>Features the server has switched off (the reference run had no cash shop, Meret Market or job change quest).</summary>
    /// <summary>The level-up package quest's reward in the "Navigator" widget (item id, count) and whether it can be claimed.</summary>
    public int NavigatorRewardItem = 20300327, NavigatorRewardCount = 1;
    public bool NavigatorRewardReady = true;
    public HashSet<string> FeaturesOff = new() { "CashShop", "MeratMarket", "LapentaFrontierQuest" };

    public double HpRatio => Player.HpMax == 0 ? 0 : (double)Player.HpCurrent / Player.HpMax;
    public double SpRatio => SpMax == 0 ? 0 : (double)SpCurrent / SpMax;
}
