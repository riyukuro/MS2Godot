using System.Xml.Linq;
using Ms2.Formats.Model;
using Ms2.Formats.Pack;
using Ms2.Formats.Text;

namespace Ms2.Formats.World;

/// <summary>The effect service calls a region skill object makes (CEffectService, effectservice.cpp).</summary>
public interface IRegionSkillEffects
{
    /// <summary>EffectDataManager::GetEffectData: the effect xml exists.</summary>
    bool Has(string effect);
    /// <summary>FUN_1411a49c0: spawns a kept effect at a Gamebryo world transform; its handle, or 0 when it did not spawn.</summary>
    int Spawn(string effect, Xform at, bool always, int randomIndex);
    /// <summary>InsertEffect: a one-shot effect, not kept.</summary>
    void Insert(string effect, Xform at, bool always, int randomIndex);
    /// <summary>FUN_1411a67f0 (@0x1411a67f0, effectservice.cpp:1480-1492): fades a kept effect out over the seconds.</summary>
    void FadeOut(int handle, float seconds);
}

/// <summary>
/// CRegionSkillObject (src/ms2/guess/regionskillobject.cpp): the effects of a region skill (OnRegionSkill 0x4C mode 0), each
/// effect at every point with one rotation.
/// <para>Ported: <see cref="Init"/> (@0x1416f5a20, :83-207) for the motion effect (+0x28), <see cref="PlayLateEffect"/>
/// (@0x1416f7790, :405-433), <see cref="Reset"/> (@0x1416f6b90, :255-285), <see cref="FadeOutEffects"/> (@0x1416f6860,
/// :217-221) and <see cref="UpdateEffects"/>'s late call (@0x1416f6f50, :309-401).</para>
/// STILL-OPEN: the second loop effect (loopEffect, +0x38: Init spawns it for a local owner, then FUN_1411a68f0(e, false) and
/// StopLoopEffects' FUN_1411a68f0(e, true), CEffect::vfunc_10, whose meaning has no port), so it is not spawned, and mode 2
/// (StopLoopEffects) has nothing to stop.
/// STILL-OPEN: the pending points (spawn failed, or SplashCastEffectStartDelay) spawn in UpdateEffects only when
/// EffectSkipMgr::ShouldSkipByView passes; that test has no port, so pending points stay unspawned.
/// STILL-OPEN: PlayHitEffect (@0x1416f6900, OnRegionSkillDamage) and the collision shapes (DAT_143ae95e1/e0) are not ported.
/// STILL-OPEN: m_bLocal is COptimizer::FUN_14161ce60(owner) == 0 (optimizer.cpp:559-573), nonzero only in the optimizer's
/// preload mode for a PC; that mode has no port, so the caller passes true.
/// </summary>
public sealed class RegionSkillObject
{
    public int Id, OwnerId;
    public uint StartTick;
    public float RotX, RotY;
    public Vec3[] Points;
    public SkillMotion Attr;
    public bool Local = true;
    public readonly List<int> Effects = new();                       // m_effects
    public readonly List<(Vec3 Point, uint Time)> Pending = new();    // m_pending
    bool lateDone;                                                    // m_bLateDone

    /// <summary>The transform of a point (every spawn of Init, UpdateEffects, Reset, PlayLateEffect, PlayHitEffect):
    /// translate = the point, rotate = FromEulerAnglesXYZ(fRotX, 0, -fRotY * 3.1415927 / 180) (radians).</summary>
    public static Xform PointTransform(Vec3 point, float rotX, float rotY)
    {
        double z = -rotY * 3.1415927f / 180.0f;
        return new Xform(point, EulerXyzRadians(rotX, 0, z), 1);
    }

    /// <summary>NiMatrix3::FromEulerAnglesXYZ of radians: X times Y times Z, each Gamebryo's (transposed) rotation (as
    /// <see cref="DummyWorld.EulerXyz"/>, which takes degrees).</summary>
    public static double[] EulerXyzRadians(double x, double y, double z)
    {
        const double toDeg = 180.0 / (3.1415927f);
        return DummyWorld.EulerXyz(x * toDeg, y * toDeg, z * toDeg);
    }

    /// <summary>
    /// Init (@0x1416f5a20, regionskillobject.cpp:83-207): false when the skill, the level or motion 0 is missing
    /// (:91-99). The motion effect (+0x28): for each point, spawned when (local or IgnoreOptimalHideCastEffect) and not
    /// SplashCastEffectStartDelay, kept in m_effects; otherwise listed as pending with the server tick, or with the start tick
    /// when SplashCastEffectStartDelay (:105-135). Then StopLoopEffects and PlayLateEffect (:171-172).
    /// </summary>
    public static RegionSkillObject Init(int id, int ownerId, Vec3[] points, SkillMotion attr, float rotX, float rotY, uint startTick,
        bool local, uint serverTick, IRegionSkillEffects fx)
    {
        if (attr == null) return null;
        var o = new RegionSkillObject { Id = id, OwnerId = ownerId, Points = points, Attr = attr, RotX = rotX, RotY = rotY, StartTick = startTick, Local = local };
        if (!string.IsNullOrEmpty(attr.MotionEffect) && fx.Has(attr.MotionEffect))
        {
            foreach (var p in points)
            {
                int e = 0;
                if ((local || attr.IgnoreOptimalHideCastEffect) && !attr.SplashCastEffectStartDelay)
                    e = fx.Spawn(attr.MotionEffect, PointTransform(p, rotX, rotY), attr.IgnoreOptimalHideCastEffect, id);
                if (e != 0) o.Effects.Add(e);
                else o.Pending.Add((p, attr.SplashCastEffectStartDelay ? startTick : serverTick));
            }
        }
        o.PlayLateEffect(serverTick, fx);
        return o;
    }

    /// <summary>PlayLateEffect (@0x1416f7790, :405-433): once, when the start tick has come, SplashDurationCastEffect at every
    /// point (local or IgnoreOptimalHideCastEffect); done is set even when it did not play for a non-local owner.</summary>
    public void PlayLateEffect(uint serverTick, IRegionSkillEffects fx)
    {
        if (lateDone || string.IsNullOrEmpty(Attr.SplashDurationCastEffect) || StartTick > serverTick) return;
        if (Local || Attr.IgnoreOptimalHideCastEffect)
            foreach (var p in Points) fx.Insert(Attr.SplashDurationCastEffect, PointTransform(p, RotX, RotY), Attr.IgnoreOptimalHideCastEffect, Id);
        lateDone = true;
    }

    /// <summary>UpdateEffects (@0x1416f6f50): the pending spawns need ShouldSkipByView (STILL-OPEN, see the class), then
    /// PlayLateEffect (:400).</summary>
    public void UpdateEffects(uint serverTick, IRegionSkillEffects fx) => PlayLateEffect(serverTick, fx);

    /// <summary>Reset (@0x1416f6b90, :255-285): SplashEndEffect once at every point (local or IgnoreOptimalHideCastEffect).
    /// The collision touches before it are not ported.</summary>
    public void Reset(IRegionSkillEffects fx)
    {
        if (string.IsNullOrEmpty(Attr.SplashEndEffect)) return;
        if (Local || Attr.IgnoreOptimalHideCastEffect)
            foreach (var p in Points) fx.Insert(Attr.SplashEndEffect, PointTransform(p, RotX, RotY), Attr.IgnoreOptimalHideCastEffect, Id);
    }

    /// <summary>FadeOutEffects (@0x1416f6860, :217-221): every kept effect fades over RegionSkillFadeOutDuration.</summary>
    public void FadeOutEffects(float fadeSeconds, IRegionSkillEffects fx)
    {
        foreach (int e in Effects) fx.FadeOut(e, fadeSeconds);
    }
}

/// <summary>
/// WorldService's region skill objects (m_3a0): AddRegionSkillObject (@0x141bc32d0, worldservice_1.cpp:4159-4168) keeps a
/// new id only (a known id returns 0, and the object, whose Init already spawned its effects, is dropped with them still
/// playing); RemoveRegionSkillObject (@0x141bc33d0, :4180-4189) resets the object, fades its effects out and drops the id;
/// the world tick (@0x141bcb830, :9661-9665) runs every object's UpdateEffects.
/// </summary>
public sealed class RegionSkillObjects
{
    readonly Dictionary<int, RegionSkillObject> objects = new();
    public IReadOnlyDictionary<int, RegionSkillObject> Objects => objects;
    public int DroppedDuplicates;

    public bool Add(RegionSkillObject o)
    {
        if (objects.ContainsKey(o.Id)) { DroppedDuplicates++; return false; }
        objects[o.Id] = o;
        return true;
    }

    public void Remove(int id, float fadeSeconds, IRegionSkillEffects fx)
    {
        if (!objects.TryGetValue(id, out var o)) return;
        o.Reset(fx);
        o.FadeOutEffects(fadeSeconds, fx);
        objects.Remove(id);
    }

    public void Update(uint serverTick, IRegionSkillEffects fx)
    {
        foreach (var o in objects.Values) o.UpdateEffects(serverTick, fx);
    }

    public void Clear() => objects.Clear();

    /// <summary>The client's default of RegionSkillFadeOutDuration (RegionSkillFadeOutDuration.Define(.., 0.5f),
    /// unit_1402ab440_constantsblocks.cpp:814).</summary>
    public const float DefaultFadeOutDuration = 0.5f;

    /// <summary>RegionSkillFadeOutDuration from table/constants.xml (key / value rows), else the default.</summary>
    public static float FadeOutDuration(PackSet packs)
    {
        if (!packs.Exists("Xml", "table/constants.xml")) return DefaultFadeOutDuration;
        var doc = PackXml.Parse(packs.Read("Xml", "table/constants.xml"));
        foreach (var v in doc.Descendants("v"))
            if ((string)v.Attribute("key") == "RegionSkillFadeOutDuration"
                && float.TryParse((string)v.Attribute("value"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f))
                return f;
        return DefaultFadeOutDuration;
    }
}
