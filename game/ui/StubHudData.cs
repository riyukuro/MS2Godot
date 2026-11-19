using Ms2.Net.Packets;

namespace Ms2Godot.Ui;

/// <summary>HUD numbers until the field packets feed them: the player is a Ms2.Net <see cref="UserBlock"/> (the same block FieldAddUser carries).</summary>
public sealed class StubHudData
{
    /// <summary>The one HUD data set: the HUD scenes read it and live mode writes the server's numbers into it.</summary>
    public static StubHudData Instance { get; } = new();
    public UserBlock Player = new() { CharacterId = 1001, Name = "Player", Level = 21, Job = 1, JobCode = 1, HpMax = 981, HpCurrent = 981, Channel = 1, GearScore = 0 };
    public int SpMax = 100, SpCurrent = 100;
    public int EpMax = 120, EpCurrent = 120;
    public double ExpPercent = 3.87, AdventureExpPercent = 0;
    public long Mesos = 0, Merets = 0;
    /// <summary>The ServerEnter wallet longs (<see cref="ServerEnter.Wallet"/>), null before a live entry.</summary>
    public long[] Wallet;
    public string Locale = "KR";
    /// <summary>The fields whose taxi station the player has found (the login packet's list, OnTaxi adds to it). The
    /// reference run had found Tria's: its minimap shows the station enabled. Offline only: a live entry replaces the list
    /// with the ServerEnter one, or an empty one when the packet is missing (LiveSession).</summary>
    public HashSet<int> FoundTaxis = new() { 2000001 };
    /// <summary>The field's interact objects' state from the server (1 = reactable). The reference run's extract station
    /// shows on its minimap, which only state 1 allows.</summary>
    public int InteractState = 1;
    /// <summary>
    /// The features the run's client has off: every name in table/feature.xml whose level is above the Live server type's (or
    /// left at zero), the set CFeatureManager::IsFeatureEnabledByName answers no for (featuremanager.cpp:832; there is no
    /// server list). Read once from the data, empty without one.
    /// </summary>
    static HashSet<string> featuresOff;
    public HashSet<string> FeaturesOff
    {
        get
        {
            if (featuresOff != null) return featuresOff;
            try
            {
                string dir = Settings.DataDir;
                featuresOff = string.IsNullOrEmpty(dir)
                    ? new HashSet<string>()
                    : Ms2.Ui.LobbyFeatures.Disabled(Ms2.Formats.Pack.PackSet.Shared(dir));
            }
            catch (Exception e) { Godot.GD.PushWarning("features: " + e.Message); featuresOff = new HashSet<string>(); }
            return featuresOff;
        }
    }
    /// <summary>The offline player's object id (its buffs' owner and caster).</summary>
    public int ObjectId = 1;
    /// <summary>The own buffs, as the own FieldAddUser's list would add them. The reference run shows Snipe (10500111 level 1,
    /// keep condition 99, enabled, no span) above Q.</summary>
    public List<BuffPacket> Buffs = new() { new(BuffPacket.Add, 1, 1, 1, 0, new BuffState(0, 0, 10500111, 1, 1, 1), 0) };

    public double HpRatio => Player.HpMax == 0 ? 0 : (double)Player.HpCurrent / Player.HpMax;
    public double SpRatio => SpMax == 0 ? 0 : (double)SpCurrent / SpMax;
}
