using Ms2.Net.Packets;

namespace Ms2Godot.Ui;

/// <summary>HUD numbers until the field packets feed them: the player is a Ms2.Net <see cref="UserBlock"/> (the same block FieldAddUser carries).</summary>
public sealed class StubHudData
{
    public UserBlock Player = new() { CharacterId = 1001, Name = "Player", Level = 21, Job = 1, JobCode = 1, HpMax = 981, HpCurrent = 981, Channel = 1, GearScore = 0 };
    public int SpMax = 100, SpCurrent = 100;
    public int EpMax = 120, EpCurrent = 120;
    public double ExpPercent = 3.87, AdventureExpPercent = 0;
    public long Mesos = 0, Merets = 0;
    public string FieldName = "DP-kr Tria";
    public string Locale = "KR";
    /// <summary>The fields whose taxi station the player has found (the login packet's list, OnTaxi adds to it). The
    /// reference run had found Tria's: its minimap shows the station enabled.</summary>
    public HashSet<int> FoundTaxis = new() { 2000001 };
    /// <summary>The field's interact objects' state from the server (1 = reactable). The reference run's extract station
    /// shows on its minimap, which only state 1 allows.</summary>
    public int InteractState = 1;
    /// <summary>Features the server has switched off (the reference run had no cash shop, Meret Market or job change quest).</summary>
    /// <summary>The level-up package quest's reward in the "Navigator" widget (item id, count) and its progress: the gauge
    /// shows while current &lt; max, the reward button once it is reached.</summary>
    public int NavigatorRewardItem = 20300327, NavigatorRewardCount = 1;
    public int NavigatorCurrent = 1, NavigatorMax = 5;
    public HashSet<string> FeaturesOff = new() { "LapentaFrontierQuest" };

    public double HpRatio => Player.HpMax == 0 ? 0 : (double)Player.HpCurrent / Player.HpMax;
    public double SpRatio => SpMax == 0 ? 0 : (double)SpCurrent / SpMax;
}
