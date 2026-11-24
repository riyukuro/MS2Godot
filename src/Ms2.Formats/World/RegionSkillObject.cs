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
    /// <summary>FUN_1411a68f0 (@0x1411a68f0, effectservice.cpp:1496-1506): CEffect::vfunc_10 on every part of a kept effect,
    /// show or hide (CNifEffect: SetAppCulled(!visible), nifeffectparam.cpp:976-979; CLightEffect: the dimmer or 0,
    /// lighteffectparam.cpp:451-456; CRandomEffect: its chosen effect's). The base CEffect::vfunc_10 does nothing
    /// (effectservice.cpp:422), so by default nothing changes.</summary>
    void SetVisible(int handle, bool visible) { }
    /// <summary>WorldService::FUN_141bd19d0 (@0x141bd19d0, worldservice_1.cpp:6296-6300): the object kind of an object id
    /// (GetObjectKind: MyPC 1, PCBase 3, an npc 5 or 6 for a boss), 0 for no such object.</summary>
    int ObjectKind(int objectId) => 0;
    /// <summary><see cref="Spawn(string, Xform, bool, int)"/> with the effect target's object kind (m_nKind).</summary>
    int Spawn(string effect, Xform at, bool always, int randomIndex, int casterKind) => Spawn(effect, at, always, randomIndex);
    /// <summary><see cref="Insert(string, Xform, bool, int)"/> with the effect target's object kind (m_nKind).</summary>
    void Insert(string effect, Xform at, bool always, int randomIndex, int casterKind) => Insert(effect, at, always, randomIndex);
}

/// <summary>
/// CRegionSkillObject (src/ms2/guess/regionskillobject.cpp): the effects of a region skill (OnRegionSkill 0x4C mode 0), each
/// effect at every point with one rotation.
/// <para>Ported: <see cref="Init"/> (@0x1416f5a20, :83-207) for the motion effect (+0x28), <see cref="PlayLateEffect"/>
/// (@0x1416f7790, :405-433), <see cref="Reset"/> (@0x1416f6b90, :255-285), <see cref="FadeOutEffects"/> (@0x1416f6860,
/// :217-221), <see cref="PlayHitEffect"/> (@0x1416f6900, :224-252), <see cref="StopLoopEffects"/> (@0x1416f6e40, :294-298)
/// and <see cref="UpdateEffects"/>'s late call (@0x1416f6f50, :309-401).</para>
/// The loop effect (loopEffect, +0x38) is the region's look while it waits: Init spawns it for a local owner, hidden
/// (FUN_1411a68f0(e, false)), then StopLoopEffects shows it; each hit (PlayHitEffect) hides it and OnRegionSkill mode 2
/// (StopLoopEffects) shows it again. It is kept with the motion effects, so the remove fades it too.
/// STILL-OPEN: the pending points (spawn failed, or SplashCastEffectStartDelay) and the waiting loop effect points (spawn
/// failed) spawn in UpdateEffects only when EffectSkipMgr::ShouldSkipByView passes; that test has no port, so they stay
/// unspawned.
/// STILL-OPEN: the collision shapes (DAT_143ae95e1/e0) are not ported.
/// STILL-OPEN: m_bLocal is COptimizer::FUN_14161ce60(owner) == 0 (optimizer.cpp:559-573), nonzero only in the optimizer's
/// preload mode for a PC; that mode has no port, so the caller passes true.
/// </summary>
public sealed class RegionSkillObject
{
    public int Id, OwnerId;
    /// <summary>m_nOwnerType: the owner's object kind taken at Init (regionskillobject.cpp:103), every effect's m_nKind
    /// (:118, :152, :238, :271).</summary>
    public int OwnerKind;
    public uint StartTick;
    public float RotX, RotY;
    public Vec3[] Points;
    public SkillMotion Attr;
    public bool Local = true;
    public readonly List<int> Effects = new();                       // m_effects
    public readonly List<int> LoopEffects = new();                   // m_loopEffects
    public readonly List<Vec3> Waiting = new();                      // m_waiting: loop effect points not spawned
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
        var o = new RegionSkillObject { Id = id, OwnerId = ownerId, Points = points, Attr = attr, RotX = rotX, RotY = rotY, StartTick = startTick, Local = local, OwnerKind = fx.ObjectKind(ownerId) };
        if (!string.IsNullOrEmpty(attr.MotionEffect) && fx.Has(attr.MotionEffect))
        {
            foreach (var p in points)
            {
                int e = 0;
                if ((local || attr.IgnoreOptimalHideCastEffect) && !attr.SplashCastEffectStartDelay)
                    e = fx.Spawn(attr.MotionEffect, PointTransform(p, rotX, rotY), attr.IgnoreOptimalHideCastEffect, id, o.OwnerKind);
                if (e != 0) o.Effects.Add(e);
                else o.Pending.Add((p, attr.SplashCastEffectStartDelay ? startTick : serverTick));
            }
        }
        // the loop effect (:139-168): a local owner only (no IgnoreOptimalHideCastEffect test, m_b31 not set), kept in both
        // lists and hidden; a point it did not spawn at waits
        if (!string.IsNullOrEmpty(attr.LoopEffect) && fx.Has(attr.LoopEffect))
        {
            foreach (var p in points)
            {
                int e = local ? fx.Spawn(attr.LoopEffect, PointTransform(p, rotX, rotY), false, id, o.OwnerKind) : 0;
                if (e != 0)
                {
                    o.Effects.Add(e);
                    o.LoopEffects.Add(e);
                    fx.SetVisible(e, false);
                }
                else o.Waiting.Add(p);
            }
        }
        o.StopLoopEffects(fx);   // :171
        o.PlayLateEffect(serverTick, fx);
        return o;
    }

    /// <summary>PlayLateEffect (@0x1416f7790, :405-433): once, when the start tick has come, SplashDurationCastEffect at every
    /// point (local or IgnoreOptimalHideCastEffect); done is set even when it did not play for a non-local owner.</summary>
    public void PlayLateEffect(uint serverTick, IRegionSkillEffects fx)
    {
        if (lateDone || string.IsNullOrEmpty(Attr.SplashDurationCastEffect) || StartTick > serverTick) return;
        if (Local || Attr.IgnoreOptimalHideCastEffect)
            foreach (var p in Points) fx.Insert(Attr.SplashDurationCastEffect, PointTransform(p, RotX, RotY), Attr.IgnoreOptimalHideCastEffect, Id, OwnerKind);
        lateDone = true;
    }

    /// <summary>UpdateEffects (@0x1416f6f50): the pending spawns need ShouldSkipByView (STILL-OPEN, see the class), then
    /// PlayLateEffect (:400).</summary>
    public void UpdateEffects(uint serverTick, IRegionSkillEffects fx) => PlayLateEffect(serverTick, fx);

    /// <summary>PlayHitEffect (@0x1416f6900, :224-252), run by OnRegionSkillDamage (0x3D mode 5, scenestategame.cpp:7773-7776) once
    /// the region is found and before its targets are read, so with no target too: the loop effects hidden (:229-230), then
    /// SplashInvokeEffect (+0x30) once at every point (local or IgnoreOptimalHideCastEffect), the random index the region's
    /// id.</summary>
    public void PlayHitEffect(IRegionSkillEffects fx)
    {
        foreach (int e in LoopEffects) fx.SetVisible(e, false);
        if (string.IsNullOrEmpty(Attr.SplashInvokeEffect)) return;
        if (Local || Attr.IgnoreOptimalHideCastEffect)
            foreach (var p in Points) fx.Insert(Attr.SplashInvokeEffect, PointTransform(p, RotX, RotY), Attr.IgnoreOptimalHideCastEffect, Id, OwnerKind);
    }

    /// <summary>StopLoopEffects (@0x1416f6e40, :294-298), Init's last spawn step and OnRegionSkill mode 2: every loop effect
    /// shown (FUN_1411a68f0(e, true)).</summary>
    public void StopLoopEffects(IRegionSkillEffects fx)
    {
        foreach (int e in LoopEffects) fx.SetVisible(e, true);
    }

    /// <summary>Reset (@0x1416f6b90, :255-285): SplashEndEffect once at every point (local or IgnoreOptimalHideCastEffect).
    /// The collision touches before it are not ported.</summary>
    public void Reset(IRegionSkillEffects fx)
    {
        if (string.IsNullOrEmpty(Attr.SplashEndEffect)) return;
        if (Local || Attr.IgnoreOptimalHideCastEffect)
            foreach (var p in Points) fx.Insert(Attr.SplashEndEffect, PointTransform(p, RotX, RotY), Attr.IgnoreOptimalHideCastEffect, Id, OwnerKind);
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
    /// <summary>The objects, enumerated without an allocation (the per-frame force zone search).</summary>
    public Dictionary<int, RegionSkillObject>.ValueCollection Values => objects.Values;
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
