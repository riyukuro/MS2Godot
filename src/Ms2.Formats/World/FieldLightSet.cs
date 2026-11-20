using Ms2.Formats.Model;

namespace Ms2.Formats.World;

/// <summary>One point or spot light entity of a field, with the values the client's NiLight gets from it.</summary>
public sealed class FieldLight
{
    /// <summary>The light's bit in a receiver's mask (<see cref="FieldLightSet.Unlit"/>): its place in xblock order.</summary>
    public int Index;
    public FieldEntity Entity;
    /// <summary>NiSpotLight (the entity contains SpotLight), else NiPointLight (LightService::CreateLight, LightService.cpp:465-472).</summary>
    public bool Spot;
    /// <summary>Client space (Z up).</summary>
    public Vec3 Position;
    /// <summary>The world direction a spot shines along: column 0 of its rotation (NiSpotLight::UpdateWorldData,
    /// NiSpotLight.cpp:41), the rotation built from the entity's Rotation as <see cref="FieldEntity.Transform"/> builds it
    /// (LightService::UpdateLightProperties, LightService.cpp:779-787).</summary>
    public Vec3 Direction;
    /// <summary>LightData::m_radius: the Range property, 100 when the entity has none (MS2LightService::CreateQueuedLights,
    /// ms2lightservice.cpp:506-507). It only picks the receivers; the shader has no range cut-off.</summary>
    public float Range;
    public float Dimmer;
    /// <summary>Colours as the entity gives them (rgb), not times the dimmer.</summary>
    public float[] Diffuse, Ambient, Specular;
    public float Constant, Linear, Quadratic;
    /// <summary>cos of OuterSpotAngle and InnerSpotAngle (degrees, the half angle; NiSpotLight::SetSpotAngle,
    /// NiSpotLight.inl:23-48) and SpotExponent.</summary>
    public float OuterCos, InnerCos, Exponent;
    /// <summary>The LightPriority property (0 when the entity has none).</summary>
    public float Priority;
    /// <summary>AlwaysAffectedByLight / NeverAffectedByLight entity ids (the priority overrides, set to 100 / 0).</summary>
    public HashSet<string> Always = new(StringComparer.OrdinalIgnoreCase), Never = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// The point and spot lights of a field and which placed entity each one lights, as the client's MS2LightService (on ecr
/// LightService) attaches them.
/// <para>Loading: MS2LightService::vfunc_0 (ms2lightservice.cpp:235-239) turns automatic relighting off and queues every light
/// entity; when the world is loaded (WorldService @0x141bcda90, worldservice_1.cpp:6920) vfunc_1 runs CreateQueuedLights
/// (ms2lightservice.cpp:490-568), which makes each NiLight (UpdateLightProperties), attaches it to every entity scene graph
/// (AddAffectedObjectFunctor over ForEachEntitySceneGraph) and then turns automatic relighting on only when the field has more
/// than 3 point, 2 spot or 3 directional lights (UpdateRelightingMode, ms2lightservice.cpp:316-337; MS2SpotLight carries the
/// PointLight mixin, so a spot counts as a point light there). Then RequestSortedRelight: the next OnTick
/// (ms2lightservice.cpp:189-207) runs RelightScene(false) -> SetAffectedNodeLists over every receiver
/// (LightService.cpp:1476-1716).</para>
/// <para>Receivers: entities whose model contains Lightable and not ExceptLightService (LightService::OnRenderedEntityAdded,
/// LightService.cpp:1906-1912; the MS2 test 0x48235e30 is ExceptLightService, id 1210277424). For each one the relight queues every light whose range reaches its world bound
/// (LightAffectsReceiver: |light - centre|^2 &lt; (range + radius)^2, LightService.cpp:1424-1473; the sort-axis sweep
/// around it only skips lights that cannot pass that test) or whose AlwaysAffectedByLight names it, ranks each light type by
/// GetLightPriorityForReceiver (LightService.cpp:1125-1212), and attaches the first MaxPointLights point lights and
/// MaxSpotLights spot lights (the receiver's own properties; 0 when it has none) whose priority is above 0. An entity that is
/// not a receiver keeps every light CreateQueuedLights attached.</para>
/// <para>An entity whose scene graph is added after that relight (a character, an NPC) gets every light, no range test
/// (OnRenderedEntityAdded's loop, LightService.cpp:1926-1932), and with automatic relighting off nothing relights it.</para>
/// <para>The hardware-instanced batches (HardwareInstancingService::BuildInstancings, hardwareinstancingservice.cpp:913-943)
/// set NeverAffectedByLight on each light for the batches it does not reach member by member, which leaves each batch the
/// lights that reach its members: the per-entity test here.</para>
/// <para>Not modelled: automatic relighting (no field has more lights than the limits; STILL-OPEN if one did: characters
/// would then be relit by range and limit as they move); the receiver bound is the merged mesh bounds, which the caller
/// supplies.</para>
/// </summary>
public sealed class FieldLightSet
{
    /// <summary>MAX_LIGHT_PRIORITY (LightService.h:79).</summary>
    public const float MaxLightPriority = 100f;

    public readonly List<FieldLight> Lights = new();
    public int DirectionalLights;

    /// <summary>UpdateRelightingMode (ms2lightservice.cpp:316-337): more than 3 point (spots included), 2 spot or 3 directional lights.</summary>
    public bool AutomaticRelighting
    {
        get
        {
            int points = 0, spots = 0;
            foreach (var l in Lights)
                if (l.Entity.HasMixin("PointLight")) points++;
                else if (l.Spot) spots++;
            return !(points <= 3 && spots <= 2 && DirectionalLights <= 3);
        }
    }

    /// <summary>The point and spot light entities of <paramref name="entities"/>, in their order (the light list order).</summary>
    public static FieldLightSet Read(IEnumerable<FieldEntity> entities)
    {
        var set = new FieldLightSet();
        foreach (var e in entities)
        {
            if (!e.HasMixin("Light")) continue;
            bool spot = e.HasMixin("SpotLight");
            if (!spot && !e.HasMixin("PointLight"))
            {
                if (e.HasMixin("DirectionalLight")) set.DirectionalLights++;
                continue;
            }
            var t = e.Transform;
            var dir = new Vec3(t.R[0], t.R[3], t.R[6]);
            // UpdateLightProperties sets a value only when the entity has it; otherwise the NiLight keeps its constructor's
            // (NiLight.cpp:26-41: dimmer 1, colours white; NiPointLight.cpp:23-29: attenuation 0, 1, 0; NiSpotLight.cpp:23-33:
            // exponent 1, angles 0)
            float outer = (float)e.Number("OuterSpotAngle", 0), inner = (float)e.Number("InnerSpotAngle", 0);
            var l = new FieldLight
            {
                Index = set.Lights.Count,
                Entity = e,
                Spot = spot,
                Position = e.Position,
                Direction = dir,
                Range = (float)e.Number("Range", 100),
                Dimmer = (float)e.Number("Dimmer", 1),
                Diffuse = Rgb(e, "DiffuseColor"),
                Ambient = Rgb(e, "AmbientColor"),
                Specular = Rgb(e, "SpecularColor"),
                Constant = (float)e.Number("ConstantAttenuation", 0),
                Linear = (float)e.Number("LinearAttenuation", 1),
                Quadratic = (float)e.Number("QuadraticAttenuation", 0),
                OuterCos = MathF.Cos(outer * MathF.PI / 180f),
                InnerCos = MathF.Cos(inner * MathF.PI / 180f),
                Exponent = (float)e.Number("SpotExponent", 1),
                Priority = (float)e.Number("LightPriority", 0),
            };
            AddIds(l.Always, e, "AlwaysAffectedByLight");
            AddIds(l.Never, e, "NeverAffectedByLight");
            set.Lights.Add(l);
        }
        return set;
    }

    static float[] Rgb(FieldEntity e, string key)
    {
        var c = e.Numbers(key);
        return c != null && c.Length >= 3 ? new[] { (float)c[0], (float)c[1], (float)c[2] } : new[] { 1f, 1f, 1f };
    }

    static void AddIds(HashSet<string> ids, FieldEntity e, string key)
    {
        if (e.Raw.Sets.TryGetValue(key, out var sets)) foreach (var (_, v) in sets) ids.Add(Id(v));
        else if (e.Get(key) is { Length: > 0 } v) ids.Add(Id(v));
    }

    /// <summary>An entity id in one form: an xblock id is 32 hex digits, an entity ref may be written as urn:llid:xxxxxxxx-xxxx-...</summary>
    static string Id(string v)
    {
        if (v.StartsWith("urn:llid:", StringComparison.OrdinalIgnoreCase)) v = v.Substring(9);
        return v.Replace("-", "");
    }

    /// <summary>The entity is a receiver of the relight (OnRenderedEntityAdded's model tests, LightService.cpp:1906-1909).</summary>
    public static bool Receives(FieldEntity e) => e.HasMixin("Lightable") && !e.HasMixin("ExceptLightService");

    /// <summary>
    /// The lights that do not light <paramref name="receiver"/>, one bit per <see cref="FieldLight.Index"/>, for a receiver whose
    /// world bound is (<paramref name="center"/>, <paramref name="radius"/>) in client space. 0: every light.
    /// </summary>
    public uint Unlit(FieldEntity receiver, Vec3 center, float radius)
    {
        if (Lights.Count == 0 || !Receives(receiver)) return 0;
        uint attached = Pick(receiver, center, radius, spot: false, (uint)receiver.Number("MaxPointLights", 0))
                      | Pick(receiver, center, radius, spot: true, (uint)receiver.Number("MaxSpotLights", 0));
        uint all = Lights.Count >= 32 ? uint.MaxValue : (1u << Lights.Count) - 1;
        return all & ~attached;
    }

    uint Pick(FieldEntity receiver, Vec3 center, float radius, bool spot, uint max)
    {
        var id = receiver.Raw.Id == null ? "" : Id(receiver.Raw.Id);
        var queue = new List<(float priority, int index)>();
        foreach (var l in Lights)
        {
            if (l.Spot != spot) continue;
            bool always = l.Always.Contains(id) && !l.Never.Contains(id);
            if (always || AffectsReceiver(l, center, radius))
                queue.Add((PriorityFor(l, id, center, radius), l.Index));
        }
        // the client pops its std::priority_queue (largest priority first; its own heap order decides equal priorities)
        queue = PopOrder(queue);
        uint bits = 0;
        for (int i = 0; i < queue.Count && i < max; i++)
            if (queue[i].priority > 0f) bits |= 1u << queue[i].index;
        return bits;
    }

    /// <summary>
    /// The order a std::priority_queue&lt;LightPriority&gt; (operator&lt;, a max-heap over a vector) pops the entries pushed in
    /// <paramref name="pushed"/> order (LightService::AddReceiverToLightQueue pushes, SetAffectedNodeLists pops until empty,
    /// LightService.cpp:1215-1250, 1655-1675). Equal priorities come out in the heap's order, which is MSVC's push_heap /
    /// pop_heap (hole sifted down to a leaf along the larger child, the right one on a tie, then sifted up).
    /// STILL-OPEN: the push order is the light order here; the client pushes in the order its sort-axis sweep finds the lights.
    /// </summary>
    public static List<(float priority, int index)> PopOrder(List<(float priority, int index)> pushed)
    {
        var heap = new List<(float priority, int index)>();
        foreach (var v in pushed)
        {
            heap.Add(v);
            SiftUp(heap, heap.Count - 1, 0, v);
        }
        var popped = new List<(float priority, int index)>();
        while (heap.Count > 0)
        {
            popped.Add(heap[0]);
            var val = heap[^1];
            heap.RemoveAt(heap.Count - 1);
            if (heap.Count == 0) break;
            int bottom = heap.Count, hole = 0, idx = 0;
            int maxNonLeaf = (bottom - 1) / 2;
            while (idx < maxNonLeaf)
            {
                idx = 2 * idx + 2;
                if (heap[idx].priority < heap[idx - 1].priority) idx--;
                heap[hole] = heap[idx];
                hole = idx;
            }
            if (bottom % 2 == 0 && idx == (bottom - 2) / 2)
            {
                idx = bottom - 1;
                heap[hole] = heap[idx];
                hole = idx;
            }
            SiftUp(heap, hole, 0, val);
        }
        return popped;
    }

    static void SiftUp(List<(float priority, int index)> heap, int hole, int top, (float priority, int index) val)
    {
        while (hole > top)
        {
            int parent = (hole - 1) >> 1;
            if (!(heap[parent].priority < val.priority)) break;
            heap[hole] = heap[parent];
            hole = parent;
        }
        heap[hole] = val;
    }

    /// <summary>LightService::LightAffectsReceiver (LightService.cpp:1466-1473): squared distance against (range + radius)^2, in floats.</summary>
    public static bool AffectsReceiver(FieldLight l, Vec3 center, float radius)
    {
        float dx = (float)(l.Position.X - center.X), dy = (float)(l.Position.Y - center.Y), dz = (float)(l.Position.Z - center.Z);
        float r = l.Range + radius;
        return dx * dx + dy * dy + dz * dz < r * r;
    }

    /// <summary>
    /// LightService::GetLightPriorityForReceiver (LightService.cpp:1125-1212), with its quirk: the intensity's distance squared
    /// term uses the linear attenuation (LightService.cpp:1166-1169). An override (Always 100, Never 0) replaces the light's
    /// LightPriority. Priority = (p / 100 * 8 - 4) + intensity + share in range + share affected.
    /// </summary>
    public static float PriorityFor(FieldLight l, string receiverId, Vec3 center, float radius)
    {
        const float eps = 1.1920929e-07f;   // FLT_EPSILON
        float dx = (float)(l.Position.X - center.X), dy = (float)(l.Position.Y - center.Y), dz = (float)(l.Position.Z - center.Z);
        float distance = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
        float receiverRadius = radius + eps, lightRadius = l.Range + eps;

        float affectedDist = -(distance - lightRadius - receiverRadius);
        affectedDist = MathF.Min(affectedDist, receiverRadius * 2);
        float percentAffected = Math.Clamp(affectedDist / (receiverRadius * 2), 0f, 1f);

        float distMinRadius = MathF.Max(distance - receiverRadius, 0f);
        float percentInRange = MathF.Min(distMinRadius, lightRadius);
        percentInRange = Math.Clamp(1f - percentInRange / lightRadius, 0f, 1f);

        float intensity = MathF.Abs(l.Dimmer);
        float attenuation = l.Constant + l.Linear * distMinRadius + l.Linear * distMinRadius * distMinRadius;
        intensity *= 1f / attenuation;
        intensity = Math.Clamp(MathF.Min(intensity, 1f), 0f, 1f);

        float priority = l.Priority;
        if (receiverId != null)
        {
            // one map per light: Never written after Always (CreateQueuedLights, ms2lightservice.cpp:524-551), so Never wins
            if (l.Never.Contains(receiverId)) priority = 0;
            else if (l.Always.Contains(receiverId)) priority = MaxLightPriority;
        }
        priority /= MaxLightPriority;
        priority = priority * 8f - 4f;
        return priority + intensity + percentInRange + percentAffected;
    }
}
