using System;
using System.Collections.Generic;
using Godot;
using Ms2.Formats.Pack;

namespace Ms2Godot;

/// <summary>
/// The process's cache of loaded models and textures: the client's CNifLoadingService, one per process
/// (nifeffectparam.cpp:1614). It is not cleared at a field change, only at shutdown (OnShutdown ClearTextureCache /
/// ClearNifCache, @0x141555e90) and by the user's ClearCaches command. Every frame (OnTick @0x141555e60) runs Sweep
/// (@0x1415543e0, nifeffectparam.cpp:1869), which acts once a minute (m_kSweepTimer.Check(60000), :1874) and removes an
/// entry that only the cache holds (refcount 1) and that has expired by its class: class 0 (NotCache) always, classes 3,
/// 4 and 5 (OtherSkill, NPC, DependTime) once unused for the lifetime (m_dLastUse + m_dLifetime &lt; now, m_dLifetime
/// 120 s), classes 1 and 2 (ForceKeep, MySkill) never. A texture that something else holds counts as used at each
/// sweep (nRefs &gt; 1: m_dLastUse = now, :1964); a model does not. A load of a cached entry sets its last use to the
/// game time and lowers its class to the asked one when that is lower (:2308-2310). Main thread.
/// <para>
/// A texture is held while a material or another node refers to it (its Godot reference count past the cache's own).
/// A model's data is managed, so a field that uses one holds it with a <see cref="Lease"/> until the field is freed.
/// </para>
/// STILL-OPEN: the field entities' own NIFs go through Gamebryo's SceneGraphService asset cache in the client
/// (MS2SceneGraphService::RemoveUnusedCacheAssetData @0x1414f1760, ms2scenegraphservice.cpp:384; the entity's asset is
/// dropped once no entity uses it), whose timing is in the SDK, not in src. They are kept here by the
/// CNifLoadingService rule at class 5 (DependTime), the class of the field's palette textures (MS2DefaultTexturePalette,
/// ms2scenegraphservice.cpp:177/184 -> FindTexture @0x141553eb0 / AddTexture @0x1415540f0). The render-device-lost skip
/// (DAT_143ae1f20) has no counterpart here.
/// </summary>
public static class NifCache
{
    /// <summary>The client's cache classes (m_nClass).</summary>
    public enum CacheClass { NotCache = 0, ForceKeep = 1, MySkill = 2, OtherSkill = 3, Npc = 4, DependTime = 5 }

    /// <summary>m_dLifetime (nifeffectparam.cpp:1614).</summary>
    public const double LifetimeSec = 120;
    /// <summary>m_kSweepTimer.Check(60000) (nifeffectparam.cpp:1874).</summary>
    public const double SweepIntervalSec = 60;

    internal sealed class Entry
    {
        public object Data;
        public CacheClass Class;
        public double LastUse;
        public int Leases;          // models: the fields that use it
    }

    static readonly Dictionary<(PackSet, string), Entry> models = new();
    static readonly Dictionary<(PackSet, string), Entry> textures = new();
    static readonly List<(PackSet, string)> dropped = new();
    static double lastSweep = double.NaN;
    static bool ticking;

    /// <summary>The game time in seconds (CMainSystem::GetGameTime); a check may set its own clock.</summary>
    public static Func<double> Clock = () => Time.GetTicksMsec() / 1000.0;

    /// <summary>The fields' hold on the models they use; disposing it lets the sweep drop them.</summary>
    public sealed class Lease : IDisposable
    {
        readonly List<Entry> held = new();
        internal void Hold(Entry e) { e.Leases++; held.Add(e); }
        public void Dispose()
        {
            foreach (var e in held) e.Leases--;
            held.Clear();
        }
    }

    /// <summary>A texture by its resolved archive/entry (lower case), loaded on a miss. A failed load is not kept.</summary>
    public static Texture2D Texture(PackSet packs, string key, CacheClass cls, Func<Texture2D> load)
    {
        var e = Find(textures, packs, key, cls);
        if (e != null) return (Texture2D)e.Data;
        var t = load();
        if (t != null) textures[(packs, key)] = new Entry { Data = t, Class = cls, LastUse = Clock() };
        return t;
    }

    /// <summary>A model's data by its resolved archive/entry (lower case), loaded on a miss and held by the lease. A failed
    /// load (null) is not kept.</summary>
    public static T Model<T>(PackSet packs, string key, CacheClass cls, Lease lease, Func<T> load) where T : class
    {
        var e = Find(models, packs, key, cls);
        if (e == null)
        {
            var v = load();
            if (v == null) return null;
            models[(packs, key)] = e = new Entry { Data = v, Class = cls, LastUse = Clock() };
        }
        lease?.Hold(e);
        return (T)e.Data;
    }

    static Entry Find(Dictionary<(PackSet, string), Entry> d, PackSet packs, string key, CacheClass cls)
    {
        StartTicking();
        if (!d.TryGetValue((packs, key), out var e)) return null;
        e.LastUse = Clock();
        if (cls < e.Class) e.Class = cls;
        return e;
    }

    static void StartTicking()
    {
        if (ticking) return;
        ticking = true;
        if (double.IsNaN(lastSweep)) lastSweep = Clock();
        if (Engine.GetMainLoop() is SceneTree tree) tree.ProcessFrame += Tick;
    }

    /// <summary>OnTick: the sweep, once its minute has passed.</summary>
    public static void Tick()
    {
        double now = Clock();
        if (double.IsNaN(lastSweep)) { lastSweep = now; return; }
        if (now - lastSweep < SweepIntervalSec) return;
        lastSweep = now;
        Sweep(now);
    }

    /// <summary>One sweep pass at <paramref name="now"/> (Sweep's body, without its timer).</summary>
    public static void Sweep(double now)
    {
        foreach (var (k, e) in models)
        {
            bool expired = Expired(e, now);
            if (e.Data == null || (e.Leases <= 0 && expired)) dropped.Add(k);
        }
        foreach (var k in dropped) models.Remove(k);
        dropped.Clear();
        foreach (var (k, e) in textures)
        {
            if (e.Data is not Texture2D t || !GodotObject.IsInstanceValid(t)) { dropped.Add(k); continue; }
            bool held = t.GetReferenceCount() > 1;
            if (held) e.LastUse = now;
            if (!held && Expired(e, now)) dropped.Add(k);
        }
        foreach (var k in dropped) textures.Remove(k);
        dropped.Clear();
    }

    static bool Expired(Entry e, double now) => e.Class switch
    {
        CacheClass.NotCache => true,
        CacheClass.ForceKeep or CacheClass.MySkill => false,
        _ => e.LastUse + LifetimeSec < now,
    };

    /// <summary>The counts of kept models and textures, for checks.</summary>
    public static (int Models, int Textures) Count => (models.Count, textures.Count);

    /// <summary>ClearNifCache / ClearTextureCache: everything goes (the client: shutdown and the ClearCaches command; here checks).</summary>
    public static void Clear()
    {
        models.Clear();
        textures.Clear();
    }
}
