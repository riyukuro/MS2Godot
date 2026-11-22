using Ms2.Net.Packets;

namespace Ms2.Net;

/// <summary>
/// The cube map values CubeMapInformationService keeps from OnLoadUgcMap (scenestategame.cpp:2243-2327). Both modes call
/// the same setters, with the reader's defaults when the packet has no info: no owner, empty name and message, area 10,
/// height 5, the house setting cleared, no permission and no layout. The field teardown (WorldService FUN_141bbc580,
/// worldservice_1.cpp:10015, through FUN_1410b76a0 to the reset FUN_1410d0310, cubemapinformationservice_3.cpp:5428-5480)
/// clears them on every new field load (<see cref="Reset"/>); LoadMap's same-map early return does not.
/// STILL-OPEN: the map id setter's pass over the cubes (FUN_1410c3020, :1398-1410) and the plot bounds FUN_1410c3670
/// (:4733, p300 / p30c from the area and height) are not ported, nor the setting's Apply on the read info
/// (scenestategame.cpp:2241).
/// </summary>
public sealed class CubeMapInfo
{
    /// <summary>The permission kinds the reset clears (0 to 8, :5469-5476).</summary>
    public const int PermissionKinds = 9;

    /// <summary>f1b0 (FUN_1410c3020 sets): the packet's map id.</summary>
    public int MapId { get; private set; }
    /// <summary>f1b4 (FUN_1410c3120): the plot's map id.</summary>
    public int PlotMapId { get; private set; }
    /// <summary>f1b8 (FUN_1410c3160): the plot number.</summary>
    public int PlotNumber { get; private set; }
    /// <summary>f1c0 (FUN_1410c31a0 sets, FUN_1410c3180 reads): the owner's account id; 0 for none. The house checks
    /// compare it with the own account (e.g. FUN_1410d5640, :1192).</summary>
    public long Owner { get; private set; }
    /// <summary>s1c8 (FUN_1410c3220).</summary>
    public string Name { get; private set; } = "";
    /// <summary>s1d0 (FUN_1410c32b0).</summary>
    public string Message { get; private set; } = "";
    /// <summary>f1d8 (FUN_1410c3310): the info's first byte.</summary>
    public int HeadByte { get; private set; }
    /// <summary>f2f0 (FUN_1410c3410).</summary>
    public int CurrentArchitectScore { get; private set; }
    /// <summary>f2f4 (FUN_1410c3450).</summary>
    public int ArchitectScore { get; private set; }
    /// <summary>f2f8 (FUN_1410c3490): 0 after the reset, 10 from a packet without info.</summary>
    public int Area { get; private set; }
    /// <summary>f2fc (FUN_1410c34d0): 0 after the reset, 5 from a packet without info.</summary>
    public int Height { get; private set; }
    /// <summary>f330 (FUN_1410c3630 sets, FUN_1410c3650 reads, FUN_1410c35c0 tests == 1): the byte after the plot number.</summary>
    public int PlotMode { get; private set; }
    /// <summary>f418 (FUN_1410c3530): the house setting (Unk_UhSetting: background, lighting, camera).</summary>
    public byte Background { get; private set; }
    public byte Lighting { get; private set; }
    public byte Camera { get; private set; }

    // f368 (FUN_1410c3e40) and f378 (FUN_1410c3f10) by kind: a packet sets the kinds it carries and leaves the others
    readonly Dictionary<int, bool> permissionSet = new();
    readonly Dictionary<int, int> permissionValue = new();
    readonly List<UgcMapLayout> designs = new();
    readonly List<UgcMapLayout> blueprints = new();

    public CubeMapInfo() => Reset();

    /// <summary>f3dc (FUN_1410d5530; the home menu's "the design has changes"): reset false, set by <see cref="SetDesignChanged"/>.
    /// STILL-OPEN: the callers that set it true (cubemapinformationservice_3.cpp:2722-2770) are not ported.</summary>
    public bool DesignChanged { get; private set; }

    /// <summary>FUN_1410d54f0 (@0x1410d54f0, cubemapinformationservice_3.cpp:1162): only while the service's owner is the own account.</summary>
    public void SetDesignChanged(bool changed, long ownAccount)
    {
        if (ownAccount != Owner) return;
        DesignChanged = changed;
    }

    /// <summary>FUN_1410d4450 (@0x1410d4450, :2025): the slot's name and time when the slot is in a388, else a new slot at the end;
    /// nothing unless <paramref name="characterId"/> is the service's owner (FUN_1410c3180).</summary>
    public void SetDesignSlot(long characterId, int slot, string name, long time) => SetSlot(designs, characterId, slot, name, time);

    /// <summary>FUN_1410d4710 (@0x1410d4710, :2044): the same for a3a8.</summary>
    public void SetBlueprintSlot(long characterId, int slot, string name, long time) => SetSlot(blueprints, characterId, slot, name, time);

    void SetSlot(List<UgcMapLayout> list, long characterId, int slot, string name, long time)
    {
        if (Owner != characterId) return;
        var info = new UgcMapLayout(slot, name, time);
        int i = list.FindIndex(l => l.Id == slot);
        if (i >= 0) list[i] = info; else list.Add(info);
    }

    /// <summary>f368's flag of the kind: false when no packet set it.</summary>
    public bool PermissionSet(int kind) => permissionSet.TryGetValue(kind, out bool v) && v;
    /// <summary>f378's value of the kind: 0 when no packet set it.</summary>
    public int PermissionValue(int kind) => permissionValue.TryGetValue(kind, out int v) ? v : 0;
    /// <summary>a388 (FUN_1410d43c0 appends): the own house's designs.</summary>
    public IReadOnlyList<UgcMapLayout> Designs => designs;
    /// <summary>a3a8 (FUN_1410d4680 appends): the own house's blueprints.</summary>
    public IReadOnlyList<UgcMapLayout> Blueprints => blueprints;

    /// <summary>FUN_1410d0310's part for these values (:5447-5478).</summary>
    public void Reset()
    {
        MapId = PlotMapId = PlotNumber = 0;
        Owner = 0;
        Name = Message = "";
        HeadByte = CurrentArchitectScore = ArchitectScore = Area = Height = PlotMode = 0;
        Background = Lighting = Camera = 0;
        permissionSet.Clear();
        permissionValue.Clear();
        for (int k = 0; k < PermissionKinds; k++) { permissionSet[k] = false; permissionValue[k] = 0; }
        designs.Clear();
        blueprints.Clear();
        DesignChanged = false;
    }

    /// <summary>OnLoadUgcMap's setters (:2248-2281 for mode 0, :2294-2327 otherwise; the same calls).</summary>
    public void Apply(LoadUgcMap m)
    {
        var h = m.Home;
        MapId = m.MapId;
        PlotNumber = h?.PlotNumber ?? 0;
        PlotMapId = h?.PlotMapId ?? 0;
        Owner = h?.Owner ?? 0;
        Name = h?.Name ?? "";
        Message = h?.Message ?? "";
        HeadByte = h?.HeadByte ?? 0;
        CurrentArchitectScore = h?.CurrentArchitectScore ?? 0;
        ArchitectScore = h?.ArchitectScore ?? 0;
        PlotMode = h?.PlotMode ?? 0;
        Area = h?.Area ?? 10;
        Height = h?.Height ?? 5;
        Background = h?.Background ?? 0;
        Lighting = h?.Lighting ?? 0;
        Camera = h?.Camera ?? 0;
        if (h == null) return;
        designs.AddRange(h.Designs);
        blueprints.AddRange(h.Blueprints);
        for (int k = 0; k < h.Permissions.Count; k++)
        {
            var (set, value) = h.Permissions[k];
            permissionSet[k] = set;
            if (set) permissionValue[k] = value;
        }
    }
}
