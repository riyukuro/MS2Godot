namespace Ms2.Formats.World;

/// <summary>
/// Which placed entities receive the sun's shadow at a shadow level.
/// <para>ecr LightService attaches an entity whose IsReceivingShadow (0x90d6d437) is false to every shadow generator as an
/// unaffected receiver: when its scene graph is added (OnRenderedEntityAdded, LightService.cpp:1951-1957), when a generator
/// is made (AddUnaffectedCasterReceiverFunctor, LightService.cpp:350-356) and when the property changes
/// (LightService.cpp:630-635, detached again when it turns true). MS2ShadowVisitor::PopulateReceiverListAtl
/// (ms2shadowclickgenerator.cpp:899-912) takes those nodes out of the receiver list. Unset, the entity receives.</para>
/// <para>A hardware-instanced batch draws through its source entity. MeshInstancing::Init (meshinstancing.cpp:72-74) gives the
/// source the members' IsReceivingShadow (Unk_MeshInstancingConfig::flags31; members of one batch share it, Equals), so a
/// plain batch receives as its members do. For a batch whose members have UseShellRender (0x861681cb, Init's flag28),
/// HardwareInstancingService::SetInstancingsVisible (hardwareinstancingservice.cpp:1154-1168, run at the end of
/// BuildInstancings with the last SetShadowEnabled value and again from SetShadowEnabled) sets the source's
/// IsReceivingShadow to level == 4 &amp;&amp; shadows on, through FUN_14147d180 (meshinstancing.cpp:238-244), which keeps it off
/// when the members' flag is off. The level is FUN_140ae1c10 (the larger of the option's and the field's), so such a batch
/// receives only at level 4: the option dialog sets 0, 2 or 3 and fielddata's optionHighLevel is at most 3.</para>
/// <para>BuildInstancings (hardwareinstancingservice.cpp:830-904) batches an entity with UseInstancing, IsVisible and a model
/// asset; it joins the first batch of its asset whose config matches (Equals, hardwareinstancingservice.cpp:394-411:
/// IsCastingShadow, IsReceivingShadow, the nearby-light list, MaterialColor; a UseShellRender batch skips the instancing
/// cell). A batch left with one member is deleted, and that entity draws as itself with its own flag.</para>
/// <para>STILL-OPEN: the nearby-light list (FUN_1413d7160) also splits batches and is not modelled, so a shell batch
/// that the client splits into single members is taken as batched here. FUN_1413d6890 and HasHardwareSkinnedMesh also keep
/// an entity out of instancing and are not modelled.</para>
/// </summary>
public static class ShadowReceivers
{
    /// <summary>The entity's own IsReceivingShadow (true when unset).</summary>
    public static bool Own(FieldEntity e) => e.Bool("IsReceivingShadow", true);

    /// <summary>The entities among <paramref name="entities"/> that receive no sun shadow at <paramref name="level"/>.</summary>
    public static HashSet<FieldEntity> NotReceiving(IEnumerable<FieldEntity> entities, int level)
    {
        var none = new HashSet<FieldEntity>();
        var shellBatches = new Dictionary<(string, string, bool, bool), List<FieldEntity>>();
        foreach (var e in entities)
        {
            if (!Own(e)) { none.Add(e); continue; }
            if (level == 4 || !e.Bool("UseInstancing") || !e.Bool("UseShellRender") || !e.Bool("IsVisible", true)) continue;
            var asset = e.Get("NifAsset");
            if (asset == null || !asset.StartsWith("urn:llid:")) continue;
            var key = (asset.ToLowerInvariant(), e.Get("MaterialColor"), e.Bool("IsCastingShadow", true), true);
            if (!shellBatches.TryGetValue(key, out var list)) shellBatches[key] = list = new List<FieldEntity>();
            list.Add(e);
        }
        foreach (var list in shellBatches.Values)
            if (list.Count > 1) none.UnionWith(list);
        return none;
    }
}
