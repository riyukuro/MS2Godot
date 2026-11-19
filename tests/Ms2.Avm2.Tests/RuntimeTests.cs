using System.Text.Json.Nodes;
using Ms2.Avm2;
using Ms2.Avm2.Flash;
using NUnit.Framework;

namespace Ms2.Avm2.Tests;

/// <summary>The value rules and the flash.* runtime without game data (an empty movie layout).</summary>
public class RuntimeTests
{
    static (Runtime, Player) Empty()
    {
        var rt = new Runtime();
        var layout = new JsonObject { ["movie"] = "t", ["stage"] = new JsonObject { ["width"] = 800, ["height"] = 600 }, ["symbols"] = new JsonObject(), ["root"] = new JsonObject() };
        return (rt, new Player(rt, layout));
    }

    [Test]
    public void Conversions()
    {
        Assert.That(Rt.ToString(0.1 + 0.2), Is.EqualTo("0.30000000000000004"));
        Assert.That(Rt.ToString(1e21), Is.EqualTo("1e+21"));
        Assert.That(Rt.Add("a", 1), Is.EqualTo("a1"));
        Assert.That(Rt.Add(int.MaxValue, 1), Is.EqualTo(2147483648.0));
        Assert.That(Rt.ToInt32(4294967296.0 + 5), Is.EqualTo(5));
        Assert.That(Rt.LooseEquals("1", 1), Is.True);
        Assert.That(Rt.StrictEquals("1", 1), Is.False);
        Assert.That(Rt.LessThan(double.NaN, 1), Is.Null);
    }

    [Test]
    public void ClassObjectsInheritObjectMethods()
    {
        // A class object is an Object: `SomeClass.hasOwnProperty("x")` (fl.motion.AnimatorFactoryBase.addTargetInfo) used to throw
        var (rt, _) = Empty();
        Runtime.Current = rt;
        var cls = rt.GetClass("flash.geom.Point");
        Assert.That(rt.CallProperty(cls, Name.Public("hasOwnProperty"), new object[] { "nothing" }), Is.EqualTo(false));
        Assert.That(rt.CallProperty(cls, Name.Public("hasOwnProperty"), new object[] { "polar" }), Is.EqualTo(true));
    }

    [Test]
    public void Vector3DFollowsScaleform()
    {
        // flash.geom.Vector3D (sfr_as3_11.cpp): the class used to be undefined, so `new Vector3D(..)` in a movie failed
        var (rt, _) = Empty();
        Runtime.Current = rt;
        var cls = rt.GetClass("flash.geom.Vector3D");
        Assert.That(cls, Is.Not.Null);
        var a = (AsVector3D)cls.Construct(1.0, 2.0, 2.0);
        Assert.That(a.length, Is.EqualTo(3));
        Assert.That(a.w, Is.EqualTo(0));
        var b = AsVector3D.New(rt, 0, 0, 1);
        var c = a.crossProduct(b);
        Assert.That((c.x, c.y, c.z), Is.EqualTo((2.0, -1.0, 0.0)));
        Assert.That(a.dotProduct(b), Is.EqualTo(2));
        Assert.That(a.add(b).z, Is.EqualTo(3));
        Assert.That(a.subtract(b).z, Is.EqualTo(1));
        Assert.That(a.normalize(), Is.EqualTo(3));
        Assert.That(a.length, Is.EqualTo(1).Within(1e-12));
        Assert.That(a.nearEquals(a.clone(), 1e-9), Is.True);
        Assert.That(AsVector3D.distance(b, AsVector3D.New(rt, 0, 0, 4)), Is.EqualTo(3));
        Assert.That(b.ToAsString(), Is.EqualTo("(x=0, y=0, z=1)"));
        Assert.That(AsVector3D.X_AXIS.x, Is.EqualTo(1));
        Assert.That(AsVector3D.Z_AXIS, Is.SameAs(AsVector3D.Z_AXIS));
    }

    [Test]
    public void PerspectiveProjectionFollowsScaleform()
    {
        // flash.geom.PerspectiveProjection (sfr_as3_11.cpp): the defaults, the argument ctor, and Transform.perspectiveProjection
        var (rt, _) = Empty();
        Runtime.Current = rt;
        var cls = rt.GetClass("flash.geom.PerspectiveProjection");
        Assert.That(cls, Is.Not.Null);
        var pp = (AsPerspectiveProjection)cls.Construct();
        Assert.That(pp.fieldOfView, Is.EqualTo(55));
        Assert.That(pp.projectionCenter.x, Is.EqualTo(250));
        Assert.That(pp.focalLength, Is.EqualTo(250 / Math.Tan(55 * Math.PI / 360)).Within(1e-9));
        var q = (AsPerspectiveProjection)cls.Construct(90.0, 0.0);
        Assert.That(q.fieldOfView, Is.EqualTo(90));
        Assert.That(q.focalLength, Is.EqualTo(pp.focalLength));
        var m = q.toMatrix3D();
        Assert.That(m.rawData.Items.Count, Is.EqualTo(16));
        // Transform: the root starts with a 55 degree projection centred on the stage; matrix3D carries position and z rotation
        var root = rt.Player.Root;
        if (root != null)
        {
            var rp = root.transform.perspectiveProjection;
            Assert.That(rp.fieldOfView, Is.EqualTo(55));
            Assert.That((rp.projectionCenter.x, rp.projectionCenter.y), Is.EqualTo((400.0, 300.0)));
            var m3 = (AsMatrix3D)rt.GetClass("flash.geom.Matrix3D").Construct();
            m3.appendRotation(90, AsVector3D.Z_AXIS);
            root.transform.matrix3D = m3;
            Assert.That(root.rotation, Is.EqualTo(90).Within(1e-6));
        }
    }

    [Test]
    public void NamespaceConstantIsTheNamespaceItself()
    {
        // A hand-built constant pool (no movie data): kind 0x16 (package namespace) as a trait constant is the namespace, so a
        // later RTQName access qualifies by it; it used to read back as null (the public namespace).
        var abc = new Ms2.Swf.Abc.AbcFile
        {
            Ints = new int[1], Uints = new uint[1], Doubles = new double[1], Strings = new[] { "", "motion_internal" },
            Namespaces = new[] { new Ms2.Swf.Abc.Namespace { Name = "*" }, new Ms2.Swf.Abc.Namespace { Kind = 0x16, Name = "motion_internal" } },
            NsSets = new[] { System.Array.Empty<int>() }, Multinames = new[] { new Ms2.Swf.Abc.Multiname { Kind = 0, Name = "*" } },
            Methods = new Ms2.Swf.Abc.MethodInfo[0], Instances = new Ms2.Swf.Abc.InstanceInfo[0],
            Classes = new Ms2.Swf.Abc.ClassInfo[0], Scripts = new Ms2.Swf.Abc.ScriptInfo[0], Hash = "namespace-constant-test",
        };
        var rt = new Runtime();
        var ctx = new AbcContext(rt, abc, "test");
        var v = ctx.Constant(0x16, 1);
        Assert.That(v, Is.InstanceOf<AsNamespace>());
        Assert.That(((AsNamespace)v).Ns.Uri, Is.EqualTo("motion_internal"));
    }

    [Test]
    public void Matrix3DGetterIsLiveInTwips()
    {
        // Transform::matrix3DGet (sfr_as3_11.cpp:1927) builds the matrix from the object's current state on every read: a,b,c,d,
        // then tx, ty and z in twips; it is not a stored copy. rawData reads the translation back divided by 20, so it shows pixels.
        var (rt, _) = Empty();
        Runtime.Current = rt;
        var sp = (AsSprite)rt.GetClass("flash.display.Sprite").Construct();
        sp.x = 3; sp.y = 4; sp.z = 5;
        var m = sp.transform.matrix3D;
        Assert.That(m.rawData.Items[12], Is.EqualTo(3.0));
        Assert.That(m.rawData.Items[13], Is.EqualTo(4.0));
        Assert.That(m.rawData.Items[14], Is.EqualTo(5.0));
        sp.x = 10; sp.scaleX = 2;
        var m2 = sp.transform.matrix3D;
        Assert.That(m2.rawData.Items[12], Is.EqualTo(10.0));
        Assert.That(m2.rawData.Items[0], Is.EqualTo(2.0).Within(1e-9));
    }

    [Test]
    public void Matrix3DSetKeepsRotationsAndStopsAnimMoves()
    {
        // Transform::matrix3DSet (sfr_as3_11.cpp:2029): rotationX/Y/Z and the three scales come from the matrix, and the script
        // now owns the placement (also for a null set).
        var (rt, _) = Empty();
        Runtime.Current = rt;
        var sp = (AsSprite)rt.GetClass("flash.display.Sprite").Construct();
        var m3 = (AsMatrix3D)rt.GetClass("flash.geom.Matrix3D").Construct();
        m3.appendScale(1, 1, 3);
        m3.appendRotation(30, AsVector3D.X_AXIS);
        sp.transform.matrix3D = m3;
        Assert.That(sp.rotationX, Is.EqualTo(30).Within(1e-4));
        Assert.That(sp.scaleZ, Is.EqualTo(3).Within(1e-6));
        Assert.That(sp.MatrixLocked, Is.True);
        var other = (AsSprite)rt.GetClass("flash.display.Sprite").Construct();
        other.transform.matrix3D = null;
        Assert.That(other.MatrixLocked, Is.True);
    }

    [Test]
    public void PerspectiveProjectionWritesBackToItsObject()
    {
        // The projection a Transform hands out is bound to the object: its setters change the object's own FOV, focal length
        // and centre (PerspectiveProjection::fieldOfViewSet etc., sfr_as3_11.cpp:1519-1545).
        var (rt, _) = Empty();
        Runtime.Current = rt;
        var sp = (AsSprite)rt.GetClass("flash.display.Sprite").Construct();
        AsTransform.InitFov(sp);                       // a loaded movie starts at 55 (sfr_as3_18.cpp:8135)
        var pp = sp.transform.perspectiveProjection;
        Assert.That(pp.fieldOfView, Is.EqualTo(55));
        pp.fieldOfView = 90;
        pp.projectionCenter = AsPoint.New(rt, 10, 20);
        var again = sp.transform.perspectiveProjection;
        Assert.That(again.fieldOfView, Is.EqualTo(90));
        Assert.That((again.projectionCenter.x, again.projectionCenter.y), Is.EqualTo((10.0, 20.0)));
    }

    [Test]
    public void PerspectiveProjectionSetterPathWritesBack()
    {
        // assigning a projection binds it to the object, so the object's FOV follows later writes to the assigned one
        // (no InitFov: the object has no projection data until the assignment makes it)
        var (rt, _) = Empty();
        Runtime.Current = rt;
        var sp = (AsSprite)rt.GetClass("flash.display.Sprite").Construct();
        var src = (AsPerspectiveProjection)rt.GetClass("flash.geom.PerspectiveProjection").Construct();
        sp.transform.perspectiveProjection = src;
        var pp = sp.transform.perspectiveProjection;
        pp.fieldOfView = 90;
        Assert.That(sp.transform.perspectiveProjection.fieldOfView, Is.EqualTo(90));
    }

    [Test]
    public void Matrix3DGetterKeepsRotationsAndNullClears()
    {
        var (rt, _) = Empty();
        Runtime.Current = rt;
        var sp = (AsSprite)rt.GetClass("flash.display.Sprite").Construct();
        sp.rotationX = 30; sp.scaleZ = 2;
        var m = sp.transform.matrix3D;
        double c = Math.Cos(30 * Math.PI / 180), s = Math.Sin(30 * Math.PI / 180);
        Assert.That(Convert.ToDouble(m.rawData.Items[5]), Is.EqualTo(c).Within(1e-9));
        Assert.That(Convert.ToDouble(m.rawData.Items[6]), Is.EqualTo(s).Within(1e-9));
        Assert.That(Convert.ToDouble(m.rawData.Items[10]), Is.EqualTo(2 * c).Within(1e-9));
        sp.transform.matrix3D = null;                   // Clear3D: the 3D state and the 2D matrix reset
        Assert.That((sp.rotationX, sp.scaleZ, sp.z), Is.EqualTo((0.0, 1.0, 0.0)));
        Assert.That(sp.scaleX, Is.EqualTo(1));
    }

    [Test]
    public void Matrix3DFromPropertiesIsRzRyRxS()
    {
        // UpdateTransform3D (sfr_gfx_06.cpp:1903-1920): T * (Rz * Ry) * (Rx * S), Matrix3F(a, b) = a * b (sfr_render_03.cpp:80-95)
        var (rt, _) = Empty();
        Runtime.Current = rt;
        var sp = (AsSprite)rt.GetClass("flash.display.Sprite").Construct();
        sp.rotation = 20; sp.rotationY = 10;
        var m = sp.transform.matrix3D;
        double r20 = 20 * Math.PI / 180, r10 = 10 * Math.PI / 180;
        Assert.That(Convert.ToDouble(m.rawData.Items[1]), Is.EqualTo(Math.Sin(r20) * Math.Cos(r10)).Within(1e-9));   // [1][0] = sz*cy
        Assert.That(Convert.ToDouble(m.rawData.Items[2]), Is.EqualTo(-Math.Sin(r10)).Within(1e-9));                 // [2][0] = -sy
    }

    [Test]
    public void Matrix3DSetIsHeldUntilASetterRuns()
    {
        var (rt, _) = Empty();
        Runtime.Current = rt;
        var sp = (AsSprite)rt.GetClass("flash.display.Sprite").Construct();
        var m3 = (AsMatrix3D)rt.GetClass("flash.geom.Matrix3D").Construct();
        m3.appendRotation(30, AsVector3D.Y_AXIS);
        m3.appendRotation(20, AsVector3D.Z_AXIS);
        var want = Enumerable.Range(0, 16).Select(i => Convert.ToDouble(m3.rawData.Items[i])).ToArray();
        sp.transform.matrix3D = m3;
        var got = sp.transform.matrix3D;
        for (int i = 0; i < 12; i++) Assert.That(Convert.ToDouble(got.rawData.Items[i]), Is.EqualTo(want[i]).Within(1e-6), "element " + i);
        sp.x = 7;                                           // x is not a rotation/scale/z setter: the 3x3 stays
        Assert.That(Convert.ToDouble(sp.transform.matrix3D.rawData.Items[1]), Is.EqualTo(want[1]).Within(1e-6));
        sp.rotationX = 15;                                  // a setter rebuilds M34 from the properties
        Assert.That(sp.transform.matrix3D.rawData.Items[6], Is.Not.EqualTo(want[6]));
    }

    [Test]
    public void NullMatrix3DAfterAPure2DMatrixResetsTo2DIdentity()
    {
        // any SetMatrix3D sets the node's Is3D flag and Clear3D resets the geom, the 2D values too (sfr_gfx_06.cpp:191-201)
        var (rt, _) = Empty();
        Runtime.Current = rt;
        var sp = (AsSprite)rt.GetClass("flash.display.Sprite").Construct();
        var m3 = (AsMatrix3D)rt.GetClass("flash.geom.Matrix3D").Construct();
        m3.appendScale(2, 3, 1);
        sp.transform.matrix3D = m3;
        Assert.That(sp.scaleX, Is.EqualTo(2).Within(1e-9));
        sp.transform.matrix3D = null;
        Assert.That(sp.scaleX, Is.EqualTo(1).Within(1e-9));
        Assert.That(sp.scaleY, Is.EqualTo(1).Within(1e-9));
    }

    [Test]
    public void Matrix3DFollowsScaleform()
    {
        // flash.geom.Matrix3D (sfr_as3_11.cpp): undefined before, which stopped the Flash timeline helpers that build one
        var (rt, _) = Empty();
        Runtime.Current = rt;
        var cls = rt.GetClass("flash.geom.Matrix3D");
        Assert.That(cls, Is.Not.Null);
        var m = (AsMatrix3D)cls.Construct();
        Assert.That(m.determinant, Is.EqualTo(1));
        m.appendTranslation(10, 20, 30);
        m.appendScale(2, 2, 2);                    // append: scale after the move
        var p = m.transformVector(AsVector3D.New(rt, 1, 1, 1));
        Assert.That((p.x, p.y, p.z), Is.EqualTo((22.0, 42.0, 62.0)));
        var q = m.deltaTransformVector(AsVector3D.New(rt, 1, 1, 1));
        Assert.That((q.x, q.y, q.z), Is.EqualTo((2.0, 2.0, 2.0)));
        var inv = m.clone(); inv.invert();
        var back = inv.transformVector(p);
        Assert.That((back.x, back.y, back.z), Is.EqualTo((1.0, 1.0, 1.0)).Within(1e-9));
        m.prependTranslation(1, 0, 0);             // prepend: the move happens first
        Assert.That(m.transformVector(AsVector3D.New(rt, 0, 0, 0)).x, Is.EqualTo(22));
        // rawData is column by column with the translation in pixels (stored in twips)
        var raw = m.rawData;
        Assert.That(raw.Items.Count, Is.EqualTo(16));
        Assert.That(m.position.x, Is.EqualTo(22 / 20.0));
        var r = (AsMatrix3D)cls.Construct();
        r.appendRotation(90, AsVector3D.Z_AXIS);
        var v = r.transformVector(AsVector3D.New(rt, 1, 0, 0));
        Assert.That((v.x, v.y, v.z), Is.EqualTo((0.0, 1.0, 0.0)).Within(1e-6));
        Assert.Throws<AsThrow>(() => m.recompose(null));
    }

    [Test]
    public void DisplayListAndEvents()
    {
        var (rt, p) = Empty();
        var sprite = rt.GetClass("flash.display.Sprite");
        var a = (AsContainer)sprite.Construct();
        Assert.That(a, Is.InstanceOf<AsContainer>());
        var b = (AsDisplayObject)sprite.Construct();
        p.Stage.addChild(a);
        a.addChild(b);
        int added = 0;
        a.addEventListener("added", new AsPlainFunction(rt, new NativeMethod("f", (s, args) => { added++; return Rt.Undef; })));
        var c = (AsDisplayObject)sprite.Construct();
        a.addChild(c);
        Assert.That(added, Is.EqualTo(1));
        Assert.That(a.getChildIndex(c), Is.EqualTo(1));
        b.x = 10; a.x = 5;
        Assert.That(b.LocalToGlobal(0, 0).X, Is.EqualTo(15));
    }

    /// <summary>
    /// Loader.unloadAndStop (sfr_as3_07.cpp:5160 -> MovieRoot::UnloadMovie, sfr_as3_19.cpp:8453): the unloaded content does
    /// not run again — its enterFrame listeners are gone (and with them its tweens) and the movie's timers are stopped —
    /// though the player stays and keeps ticking.
    /// </summary>
    [Test]
    public void UnloadAndStopStopsTheContent()
    {
        var (rt, p) = Empty();
        Runtime.Current = rt;
        var loader = (AsLoader)rt.GetClass("flash.display.Loader").Construct();
        p.Stage.addChild(loader);
        var content = (AsContainer)rt.GetClass("flash.display.Sprite").Construct();
        loader.addChild(content);
        int frames = 0, ticks = 0;
        content.addEventListener("enterFrame", new AsPlainFunction(rt, new NativeMethod("f", (s, args) => { frames++; return Rt.Undef; })));
        var timer = (AsTimer)rt.GetClass("flash.utils.Timer").Construct(10.0, 0);
        timer.addEventListener("timer", new AsPlainFunction(rt, new NativeMethod("t", (s, args) => { ticks++; return Rt.Undef; })));
        timer.start();
        for (int i = 0; i < 4; i++) p.Tick();
        Assert.That(frames, Is.GreaterThan(0), "the content is on the stage and hears enterFrame");
        Assert.That(ticks, Is.GreaterThan(0), "its timer runs");
        loader.unloadAndStop();
        Assert.That(loader.numChildren, Is.Zero, "the content is gone");
        int f = frames, t = ticks;
        for (int i = 0; i < 10; i++) p.Tick();
        Assert.That(frames, Is.EqualTo(f), "no enterFrame of the unloaded content");
        Assert.That(ticks, Is.EqualTo(t), "no timer of the unloaded movie");
    }

    [Test]
    public void SetChildIndexIgnoresANonChildAndAnIndexPastTheEnd()
    {
        // GFx setChildIndex: a negative index is 0; a non-child or an index at or past the count is a no-op, no error
        var (rt, p) = Empty();
        var sprite = rt.GetClass("flash.display.Sprite");
        var a = (AsContainer)sprite.Construct();
        var b = (AsDisplayObject)sprite.Construct();
        var c = (AsDisplayObject)sprite.Construct();
        p.Stage.addChild(a);
        a.addChild(b); a.addChild(c);
        var s = (AsContainer)c;
        Assert.DoesNotThrow(() => s.setChildIndex(c, 0));
        Assert.DoesNotThrow(() => a.setChildIndex(b, 2));
        Assert.That((a.getChildIndex(b), a.getChildIndex(c)), Is.EqualTo((0, 1)));
        a.setChildIndex(c, -3);
        Assert.That((a.getChildIndex(c), a.getChildIndex(b)), Is.EqualTo((0, 1)));
    }

    [Test]
    public void RenderRunsOnlyAfterStageInvalidateAndAnInvalidateInsideItIsDropped()
    {
        // MovieRoot::AdvanceFrame (sfr_as3_16z.cpp:607-611): StageInvalidated queues the render chain, cleared after it.
        // The chain holds every display object with a render listener, on the stage or not (sfr_as3_09.cpp:1089).
        var (rt, p) = Empty();
        var sprite = rt.GetClass("flash.display.Sprite");
        var on = (AsDisplayObject)sprite.Construct();
        var off = (AsDisplayObject)sprite.Construct();
        p.Stage.addChild(on);
        int onRenders = 0, offRenders = 0;
        on.addEventListener("render", new AsPlainFunction(rt, new NativeMethod("r", (_, a) =>
        {
            onRenders++;
            p.Call(p.Stage, "invalidate", System.Array.Empty<object>());
            return Rt.Undef;
        })));
        off.addEventListener("render", new AsPlainFunction(rt, new NativeMethod("r", (_, a) => { offRenders++; return Rt.Undef; })));
        for (int i = 0; i < 5; i++) p.Tick();
        Assert.That((onRenders, offRenders), Is.EqualTo((0, 0)), "no stage.invalidate: no render");
        p.Call(p.Stage, "invalidate", System.Array.Empty<object>());
        p.Tick();
        Assert.That((onRenders, offRenders), Is.EqualTo((1, 1)));
        Assert.That(p.RenderInvalidated, Is.False, "the handler's invalidate is cleared with the flag");
        p.Tick();
        Assert.That((onRenders, offRenders), Is.EqualTo((1, 1)));
    }

    [Test]
    public void GotoRunsTheFrameActionsAtOnceEvenForTheCurrentFrame()
    {
        // MovieClip::gotoAndStop/gotoAndPlay (GotoFrameAndRun, sfr_as3_07.cpp): FrameCounter::QueueFrameActions and the
        // action levels run inside the call, so every frameConstructed and exitFrame listener hears it before it returns;
        // a missing label returns first, play state untouched.
        var (rt, p) = Empty();
        var mc = (AsMovieClip)rt.GetClass("flash.display.MovieClip").Construct();
        var other = (AsDisplayObject)rt.GetClass("flash.display.Sprite").Construct();
        p.Stage.addChild(mc);
        var seen = new List<string>();
        AsPlainFunction On(string tag) => new(rt, new NativeMethod(tag, (_, a) => { seen.Add(tag); return Rt.Undef; }));
        mc.addEventListener("frameConstructed", On("mc"));
        other.addEventListener("frameConstructed", On("other"));
        mc.addEventListener("exitFrame", On("exit"));
        mc.play();
        p.Call(mc, "gotoAndStop", new object[] { 1 });
        Assert.That(seen, Is.EqualTo(new[] { "mc", "other", "exit" }));
        Assert.That(mc.isPlaying, Is.False);
        seen.Clear();
        mc.play();
        p.Call(mc, "gotoAndStop", new object[] { "nolabel" });
        Assert.That(seen, Is.Empty);
        Assert.That(mc.isPlaying, Is.True);
    }

    static (Runtime, Player, AsMovieClip) FourFrames()
    {
        var layout = new JsonObject
        {
            ["movie"] = "t", ["stage"] = new JsonObject { ["width"] = 800, ["height"] = 600 },
            ["symbols"] = new JsonObject(), ["root"] = new JsonObject { ["frames"] = 4 },
        };
        var rt = new Runtime();
        var p = new Player(rt, layout);
        Assert.That(p.Start(), Is.True);
        return (rt, p, (AsMovieClip)p.Root);
    }

    [Test]
    public void FrameScriptsRunInPlayListOrder()
    {
        // FrameCounter::QueueFrameActions walks the play list (pPlayPrev); a child is spliced in next to its place in the
        // parent (FindInsertToPlayList, sfr_as3_17d.cpp:177), so the order is the display tree's pre-order, not the order
        // the clips were marked in.
        var (rt, p, root) = FourFrames();
        root.stop();
        var seen = new List<string>();
        var mcClass = rt.GetClass("flash.display.MovieClip");
        AsMovieClip Clip(string tag, AsContainer into)
        {
            var mc = (AsMovieClip)mcClass.Construct();
            mc.FrameScripts[0] = new AsPlainFunction(rt, new NativeMethod(tag, (_, a) => { seen.Add(tag); return Rt.Undef; }));
            into.Children.Add(mc); mc.Parent = into;
            return mc;
        }
        var a = Clip("a", root); var b = Clip("b", root);
        var bKid = Clip("b.kid", b); var aKid = Clip("a.kid", a);
        // marked: b.kid, a.kid (children of the later and the earlier sibling), then the siblings, then a clip off the stage.
        // The "off last" expectation pins a guess: the client keeps off-stage clips on the play list too (sfr_as3_16.cpp:2347,
        // sfr_as3_18.cpp:3375) and the exact order is STILL-OPEN (see SortByPlayList).
        var off = (AsMovieClip)mcClass.Construct();
        off.FrameScripts[0] = new AsPlainFunction(rt, new NativeMethod("off", (_, x) => { seen.Add("off"); return Rt.Undef; }));
        p.MarkScript(off);
        foreach (var mc in new[] { bKid, aKid, b, a }) p.MarkScript(mc);
        p.RunFrameActions();
        Assert.That(seen, Is.EqualTo(new[] { "a", "a.kid", "b", "b.kid", "off" }));
        Assert.That(rt.Errors, Is.Empty);
    }

    [Test]
    public void GotoRunsTheTargetFrameScriptBetweenFrameConstructedAndExitFrame()
    {
        // FrameCounter::QueueFrameActions queues frameConstructed, then the frame scripts (AL_Frame), then exitFrame, and
        // ExecuteActionQueue runs them in that order inside gotoAndStop
        var (rt, p, root) = FourFrames();
        root.stop();
        var seen = new List<string>();
        AsPlainFunction On(string tag) => new(rt, new NativeMethod(tag, (_, a) => { seen.Add(tag); return Rt.Undef; }));
        p.Call(root, "addFrameScript", new object[] { 2, On("script") });
        root.addEventListener("frameConstructed", On("constructed"));
        root.addEventListener("exitFrame", On("exit"));
        p.Call(root, "gotoAndStop", new object[] { 3 });
        Assert.That(seen, Is.EqualTo(new[] { "constructed", "script", "exit" }));
        Assert.That(root.Frame, Is.EqualTo(2));
        seen.Clear();
        p.Tick();
        Assert.That(seen, Is.EqualTo(new[] { "constructed", "exit" }), "the script ran once");
        Assert.That(rt.Errors, Is.Empty);
    }

    [Test]
    public void AGotoInsideTheFramePhaseDropsTheClipsPendingEntries()
    {
        // GotoFrameAndRun (sfr_as3_07.cpp:1808): RemoveActionQueueEntriesFor(AL_Frame, clip) before the frame changes, so
        // the clip's exitFrame the Tick queued is skipped; the other entries still pending run inside the call, ahead of
        // the new frameConstructed/script/exitFrame, and the Tick finds the queue empty after
        var (rt, p, root) = FourFrames();
        var other = (AsDisplayObject)rt.GetClass("flash.display.Sprite").Construct();
        var seen = new List<string>();
        AsPlainFunction On(string tag, Action then = null) =>
            new(rt, new NativeMethod(tag, (_, a) => { seen.Add(tag); then?.Invoke(); return Rt.Undef; }));
        p.Call(root, "addFrameScript", new object[] { 1, On("script2", () => p.Call(root, "gotoAndStop", new object[] { 4 })), 3, On("script4") });
        root.addEventListener("frameConstructed", On("constructed"));
        root.addEventListener("exitFrame", On("exit"));
        other.addEventListener("exitFrame", On("otherExit"));
        p.Tick();
        Assert.That(seen, Is.EqualTo(new[] { "constructed", "script2", "otherExit", "constructed", "script4", "exit", "otherExit" }));
        Assert.That((root.Frame, root.isPlaying), Is.EqualTo((3, false)));
        Assert.That(rt.Errors, Is.Empty);
    }

    [Test]
    public void NextFrameOnlyChangesTheFrameAndItsScriptRunsWithTheNextFrameActions()
    {
        // MovieClip::nextFrame (sfr_as3_07.cpp:3339): Sprite::GotoFrame and SetPlayState(stopped), no actions run
        var (rt, p, root) = FourFrames();
        var seen = new List<string>();
        AsPlainFunction On(string tag) => new(rt, new NativeMethod(tag, (_, a) => { seen.Add(tag); return Rt.Undef; }));
        p.Call(root, "addFrameScript", new object[] { 1, On("script2") });
        p.Call(root, "nextFrame", Array.Empty<object>());
        Assert.That((root.Frame, root.isPlaying, seen.Count), Is.EqualTo((1, false, 0)));
        p.Tick();
        Assert.That(seen, Is.EqualTo(new[] { "script2" }));
    }

    [Test]
    public void ADraggedSpriteFollowsTheMouseAndAReleaseOverNothingReachesTheStage()
    {
        var (rt, p) = Empty();
        var s = (AsSprite)rt.GetClass("flash.display.Sprite").Construct();
        s.graphics.beginFill(0xffffff); s.graphics.drawRect(0, 0, 40, 40);
        s.x = 100; s.y = 50;
        p.Stage.addChild(s);
        p.MouseMove(110, 60);
        s.startDrag();
        // InteractiveObject::DoMouseDrag on a mouse move (Stage::PropagateMouseEvent) and on a frame (AvmSprite::AdvanceFrame)
        p.MouseMove(310, 160);
        Assert.That((s.x, s.y), Is.EqualTo((300.0, 150.0)), "the grab point kept");
        p.MouseX = 210; p.MouseY = 60;
        p.Tick();
        Assert.That((s.x, s.y), Is.EqualTo((200.0, 50.0)));
        s.stopDrag();
        p.MouseMove(500, 500);
        Assert.That((s.x, s.y), Is.EqualTo((200.0, 50.0)), "not after stopDrag");

        // MovieRoot::GenerateMouseEvents: mouse up, wheel and move go to the stage when nothing is under the mouse
        var seen = new List<string>();
        foreach (var type in new[] { "mouseUp", "mouseMove", "mouseWheel" })
            p.Call(p.Stage, "addEventListener", new object[] { type, new AsPlainFunction(rt, new NativeMethod(type, (_, a) => { seen.Add(type); return Rt.Undef; })) });
        p.MouseMove(700, 500);
        p.MouseButton(true);
        p.MouseButton(false);
        p.Wheel(3);
        Assert.That(seen, Is.EqualTo(new[] { "mouseMove", "mouseUp", "mouseWheel" }));
    }

    [Test]
    public void ARightPressInAMovieWithoutMouseEventExStillBecomesARightMouseDown()
    {
        // the client's VM has scaleform.gfx.MouseEventEx built in (ASVM::MouseEventExClass, sfr_as3_15.cpp:3570), and
        // CreateMouseEventObject (sfr_as3_09.cpp:2325-2328) sets buttonIdx on every player-made event; a movie whose ABC does
        // not declare the class used to make Main's right-button listener throw a ReferenceError on every press
        var (rt, p) = Empty();
        p.Start();                      // Main's stage listeners go on here
        var s = (AsSprite)rt.GetClass("flash.display.Sprite").Construct();
        s.graphics.beginFill(0xffffff); s.graphics.drawRect(0, 0, 40, 40);
        p.Stage.addChild(s);
        var seen = new List<string>();
        foreach (var type in new[] { "mouseDown", "rightMouseDown" })
            p.Call(s, "addEventListener", new object[] { type, new AsPlainFunction(rt, new NativeMethod(type, (_, a) => { seen.Add(type); return Rt.Undef; })) });
        p.MouseMove(10, 10);
        p.MouseButton(true, Player.RightButton);
        p.MouseButton(false, Player.RightButton);
        p.MouseButton(true);
        p.MouseButton(false);
        Assert.That(rt.Errors, Is.Empty);
        Assert.That(seen, Is.EqualTo(new[] { "rightMouseDown", "mouseDown" }), "the right press only as rightMouseDown, the left as mouseDown");
    }

    [Test]
    public void ZSetterMakesTheNode3DEvenAtTheDefault()
    {
        // SetZ calls UpdateTransform3D unconditionally (sfr_gfx_06.cpp:1220-1233), so Clear3D has a 3D state to reset
        var (rt, _) = Empty();
        Runtime.Current = rt;
        var sp = (AsSprite)rt.GetClass("flash.display.Sprite").Construct();
        sp.z = 0; sp.x = 5;
        sp.transform.matrix3D = null;
        Assert.That(sp.x, Is.EqualTo(0));
    }

    [Test]
    public void Scale9GridIsCopiedOnSetAndOnGet()
    {
        // scale9GridSet copies (sfr_as3_07.cpp:1833-1848); scale9GridGet builds a new Rectangle, null when empty (:1617-1630)
        var (rt, _) = Empty();
        Runtime.Current = rt;
        var bg = (AsSprite)rt.GetClass("flash.display.Sprite").Construct();
        var r = AsRectangle.New(rt, 1, 2, 3, 4);
        bg.scale9Grid = r; r.x = 9;
        Assert.That(bg.scale9Grid.x, Is.EqualTo(1));
        Assert.That(bg.scale9Grid, Is.Not.SameAs(r));
        Assert.That(bg.scale9Grid, Is.Not.SameAs(bg.scale9Grid));
        bg.scale9Grid = AsRectangle.New(rt, 1, 2, 0, 4);
        Assert.That(bg.scale9Grid, Is.Null, "an empty grid reads as null");
    }

    [Test]
    public void Scale9GridIsTheSymbolsGridUntilASetterReplacesIt()
    {
        // DisplayObject::scale9GridGet (sfr_as3_07.cpp:1617) reads the node's grid, which a symbol's DefineScale9Grid sets; it is
        // null only without one (SliderEx.updateTooltip calls m_bg.scale9Grid.isEmpty() on a bg that has the grid)
        var (rt, _) = Empty();
        Runtime.Current = rt;
        var s = (AsSprite)rt.GetClass("flash.display.Sprite").Construct();
        Assert.That(s.scale9Grid, Is.Null);
        s.Symbol = new JsonObject { ["scale9"] = new JsonObject { ["x"] = 4, ["y"] = 5, ["w"] = 30, ["h"] = 20 } };
        Assert.That((s.scale9Grid.x, s.scale9Grid.y, s.scale9Grid.width, s.scale9Grid.height), Is.EqualTo((4.0, 5.0, 30.0, 20.0)));
        s.scale9Grid = null;
        Assert.That(s.scale9Grid, Is.Null, "a setter replaces the symbol's grid, null clears it");
    }

    [Test]
    public void DropTargetIsTheTopmostEntityUnderTheMouseOtherThanTheDraggedSprite()
    {
        // Sprite::dropTargetGet -> MovieImpl::GetTopMostEntity(mouse, 0, testAll true, ignore the dragged sprite)
        var (rt, p) = Empty();
        var sprite = rt.GetClass("flash.display.Sprite");
        AsSprite Box(double x)
        {
            var b = (AsSprite)sprite.Construct();
            b.graphics.beginFill(0xffffff); b.graphics.drawRect(0, 0, 40, 40);
            b.x = x;
            return b;
        }
        var slot = Box(100);
        var other = Box(200);
        var dragged = Box(100);
        p.Stage.addChild(slot); p.Stage.addChild(other); p.Stage.addChild(dragged);
        p.MouseX = 120; p.MouseY = 20;
        Assert.That(dragged.dropTarget, Is.SameAs(slot), "the slot under the dragged sprite, by its drawing");
        p.MouseX = 220;
        Assert.That(dragged.dropTarget, Is.SameAs(other));
        p.MouseX = 400;
        Assert.That(dragged.dropTarget, Is.Null, "nothing under the mouse");

        // a text field in the slot is the target itself (every object counts); with mouseChildren off, the slot
        var tf = (AsTextField)rt.GetClass("flash.text.TextField").Construct();
        tf.width = 40; tf.height = 20; tf.selectable = false;
        slot.addChild(tf);
        p.MouseX = 110; p.MouseY = 10;
        Assert.That(dragged.dropTarget, Is.SameAs(tf));
        slot.mouseChildren = false;
        Assert.That(dragged.dropTarget, Is.SameAs(slot));

        // a hidden sprite or a script mask takes nothing; the slot under it does
        slot.mouseChildren = true;
        var cover = Box(100);
        p.Stage.addChildAt(cover, p.Stage.getChildIndex(dragged));
        cover.visible = false;
        Assert.That(dragged.dropTarget, Is.SameAs(tf));
        cover.visible = true;
        other.mask = cover;
        Assert.That(dragged.dropTarget, Is.SameAs(tf));
    }

    [Test]
    public void VisibleSetterKeepsTimelineMoves()
    {
        // a clip whose alpha the timeline animates 0 -> 0.5 -> 1, with the placement's visible flag on
        static JsonObject Cx(double a) => new() { ["mul"] = new JsonArray(1, 1, 1, a), ["add"] = new JsonArray(0, 0, 0, 0) };
        var layout = new JsonObject
        {
            ["movie"] = "t", ["stage"] = new JsonObject { ["width"] = 800, ["height"] = 600 },
            ["symbols"] = new JsonObject { ["1"] = new JsonObject { ["kind"] = "sprite", ["timeline"] = new JsonObject { ["frames"] = 1 } } },
            ["root"] = new JsonObject
            {
                ["frames"] = 4,
                ["children"] = new JsonArray(new JsonObject
                {
                    ["id"] = 1, ["depth"] = 1, ["first"] = 0, ["name"] = "arrow", ["visible"] = true, ["color"] = Cx(0),
                    ["moves"] = new JsonArray(new JsonObject { ["frame"] = 2, ["color"] = Cx(0.5) }, new JsonObject { ["frame"] = 3, ["color"] = Cx(1) }),
                }),
            },
        };
        var rt = new Runtime();
        var p = new Player(rt, layout);
        Assert.That(p.Start(), Is.True);
        var arrow = ((AsContainer)p.Root).Children.Single(c => c.InstanceName == "arrow");
        arrow.visible = false;
        arrow.visible = true;
        Assert.That(arrow.ColorLocked || arrow.MatrixLocked, Is.False);
        for (int i = 0; i < 3 && ((AsMovieClip)p.Root).Frame < 3; i++) p.Tick();
        Assert.That(((AsMovieClip)p.Root).Frame, Is.EqualTo(3));
        Assert.That(arrow.Color.Am, Is.EqualTo(1));
        // the timeline does not set visible again on its frames
        arrow.visible = false;
        ((AsMovieClip)p.Root).gotoAndPlay(1);
        p.Tick(); p.Tick();
        Assert.That(arrow.IsVisible, Is.False);
        Assert.That(rt.Errors, Is.Empty);
    }

    AsTextField Field(Runtime rt, double w, bool wrap)
    {
        var tf = (AsTextField)rt.GetClass("flash.text.TextField").Construct();
        tf.FromSymbol(new JsonObject { ["bounds"] = new JsonObject { ["x"] = 0, ["y"] = 0, ["w"] = w, ["h"] = 40 }, ["text"] = new JsonObject { ["size"] = 12, ["wordWrap"] = wrap } });
        return tf;
    }

    [Test]
    public void HtmlTextWhosePlainTextIsAKeyIsTranslated()
    {
        // the translator takes plain text (caps 0): an html text is parsed and its text is the key; the result is html
        var (rt, p) = Empty();
        p.Translate = k => k == "$s_points" ? "Attribute <b>Points</b>" : null;
        var tf = (AsTextField)rt.GetClass("flash.text.TextField").Construct();
        tf.FromSymbol(new JsonObject { ["bounds"] = new JsonObject { ["x"] = 0, ["y"] = 0, ["w"] = 100, ["h"] = 20 },
            ["text"] = new JsonObject { ["size"] = 12, ["html"] = true, ["initial"] = "<p align=\"left\"><font size=\"18\" color=\"#ffffff\">$s_points</font></p>" } });
        Assert.That(tf.Text, Is.EqualTo("Attribute Points"));
        Assert.That(tf.Html, Is.EqualTo("Attribute <b>Points</b>"));
        tf.htmlText = "<font color=\"#ff0000\">$s_other</font>";
        Assert.That(tf.Text, Is.EqualTo("$s_other"), "a key with no value stays");
        tf.htmlText = "<b>$s_points</b> left";
        Assert.That(tf.Text, Is.EqualTo("$s_points left"), "only a whole text is a key");
    }

    [Test]
    public void TextAutoSizeScaleFollowsDocViewFormat()
    {
        // shrink: the smaller axis ratio where the text is larger; text that fits keeps its size
        Assert.That(AsTextField.TextAutoSizeScale("shrink", 100, 200, 690, 156, 24), Is.EqualTo(156.0 / 200).Within(1e-9));
        Assert.That(AsTextField.TextAutoSizeScale("shrink", 800, 200, 690, 156, 24), Is.EqualTo(156.0 / 200).Within(1e-9));
        Assert.That(AsTextField.TextAutoSizeScale("shrink", 100, 100, 690, 156, 24), Is.EqualTo(1));
        // never below a 6 px line
        Assert.That(AsTextField.TextAutoSizeScale("shrink", 100, 1000, 690, 50, 24), Is.EqualTo(6.0 / 24).Within(1e-9));
        // fit grows too, but not on an axis under 20
        Assert.That(AsTextField.TextAutoSizeScale("fit", 100, 10, 200, 156, 10), Is.EqualTo(2));
        Assert.That(AsTextField.TextAutoSizeScale("fit", 10, 10, 200, 156, 10), Is.EqualTo(1));
        Assert.That(AsTextField.TextAutoSizeScale("none", 800, 800, 10, 10, 24), Is.EqualTo(1));
    }

    [Test]
    public void ReadabilityTextFollowsTheSymbolAndScriptFieldsAreReadable()
    {
        var (rt, _) = Empty();
        Assert.That(((AsTextField)rt.GetClass("flash.text.TextField").Construct()).antiAliasType, Is.EqualTo("advanced"));
        Assert.That(Field(rt, 60, false).antiAliasType, Is.EqualTo("normal"));
        var tf = (AsTextField)rt.GetClass("flash.text.TextField").Construct();
        tf.FromSymbol(new JsonObject { ["bounds"] = new JsonObject { ["x"] = 0, ["y"] = 0, ["w"] = 60, ["h"] = 40 }, ["text"] = new JsonObject { ["size"] = 12, ["readable"] = true } });
        Assert.That(tf.antiAliasType, Is.EqualTo("advanced"));
    }

    [Test]
    public void EnglishWrapsAtSpacesAndHyphensAndBreaksAnOverlongWord()
    {
        var (rt, _) = Empty();
        var tf = Field(rt, 64, true);
        tf.text = "Character Adventure well-known";
        var lines = tf.Lines();
        foreach (var l in lines) TestContext.WriteLine("[" + l + "]");
        Assert.That(lines.Select(l => l.Trim()), Is.EqualTo(new[] { "Character", "Adventure", "well-", "known" }));
        // a word wider than the box breaks inside the word (ParagraphFormatter::CheckWordWrap, no break point on the line)
        tf.text = "Extraordinarily";                                    // wider than the box
        var parts = tf.Lines();
        Assert.That(parts.Count, Is.GreaterThan(1));
        Assert.That(string.Concat(parts), Is.EqualTo("Extraordinarily"));
        Assert.That(tf.textWidth, Is.LessThanOrEqualTo(64 - 4), "each piece fits the box less its gutters");
        // the words before it still wrap at the space
        tf.text = "go Extraordinarily";
        Assert.That(tf.Lines()[0], Is.EqualTo("go "));
    }

    [Test]
    public void KoreanKeepsPerCharacterBreaking()
    {
        var (rt, _) = Empty();
        var tf = Field(rt, 40, true);
        tf.text = "\uAC00\uB098\uB2E4\uB77C\uB9C8\uBC14";
        Assert.That(tf.Lines().Count, Is.GreaterThan(1));
        Assert.That(string.Concat(tf.Lines()), Is.EqualTo("\uAC00\uB098\uB2E4\uB77C\uB9C8\uBC14"));
    }

    [Test]
    public void SingleLineCaptionShrinksThenEllipsis()
    {
        var (rt, _) = Empty();
        var tf = Field(rt, 50, false);
        tf.text = "Settings";          // a little too wide: shrinks, no cut
        double w = TextMeasure.Width("", 12, "Settings");
        var (l1, s1) = tf.Fitted();
        Assert.That(w, Is.GreaterThan(46));
        Assert.That(s1, Is.LessThan(1).And.GreaterThanOrEqualTo(AsTextField.MinFitScale));
        Assert.That(l1[0], Is.EqualTo("Settings"));
        tf.text = "Character Information Window";    // far too wide: 80% then an ellipsis
        var (l2, s2) = tf.Fitted();
        Assert.That(s2, Is.EqualTo(AsTextField.MinFitScale));
        Assert.That(l2[0], Does.EndWith("…"));
        Assert.That(TextMeasure.Width("", 12 * s2, l2[0]), Is.LessThanOrEqualTo(46));
        // the AS-visible metrics are those of the full text
        Assert.That(tf.text, Is.EqualTo("Character Information Window"));
    }
    [Test]
    public void HtmlImageCharsSizeTheirLines()
    {
        // StyledText::ParseHtml: an IMG tag is a space; ProcessImageTags sizes it by the exported image and moves it up by its
        // height + VSPACE; FinalizeLine takes its ascent and descent, the layout its width + 2 px
        var (rt, player) = Empty();
        var (height, ascent) = (TextMeasure.HeightRatio, TextMeasure.AscentRatio);
        try
        {
            TextMeasure.HeightRatio = _ => 1.0;
            TextMeasure.AscentRatio = _ => 0.8;
            player.ExportedImageSize = (movie, name) => name == "icon" ? (22, 22) : null;
            var tf = Field(rt, 200, false);
            tf.SetHtml("Title\n<img src='icon' vspace='-6'/> 5");
            Assert.That(tf.text, Is.EqualTo("Title\n  5"));
            Assert.That(tf.textWidth, Is.EqualTo(Math.Max(TextMeasure.Width("", 12, "Title"), TextMeasure.Width("", 12, " 5") + 24)));
            // line 1: 9.6 + 2.4 px; line 2: the image's 16 above and 6 below the baseline
            Assert.That(tf.textHeight, Is.EqualTo((240 + 440) / 20.0));
            // a url that names no image: the tag's size, no move up
            tf.SetHtml("x<img src='none' width='10' height='4'/>");
            Assert.That(tf.textWidth, Is.EqualTo(TextMeasure.Width("", 12, "x") + 12));
            Assert.That(tf.textHeight, Is.EqualTo((192 + 80) / 20.0));
            // plain text has no images
            tf.text = "Title\n  5";
            Assert.That(tf.textHeight, Is.EqualTo(24));
        }
        finally { (TextMeasure.HeightRatio, TextMeasure.AscentRatio) = (height, ascent); }
    }

    [Test]
    public void FontColorTakesAnyHexLengthAndKeepsTheLow24Bits()
    {
        // SGMLParser::ParseHexInt then TextFormat::SetColor32: the item grade colours are written as #ffRRGGBB
        static uint? Color(string html) => HtmlText.Parse(html).Formats[0].Color;
        Assert.That(Color("<font color='#ff5ad35f'>a</font>"), Is.EqualTo(0x5ad35fu));
        Assert.That(Color("<font color='#5ad35f'>a</font>"), Is.EqualTo(0x5ad35fu));
        Assert.That(Color("<font color='#fff'>a</font>"), Is.EqualTo(0xfffu));
        Assert.That(Color("<font color='#12g'>a</font>"), Is.Null);
        Assert.That(Color("<font color='#'>a</font>"), Is.Null);
    }

    [Test]
    public void HtmlTextGetterWritesParagraphsAsScaleform()
    {
        // TextField::GetText(true) -> StyledText::GetHtml: one TEXTFORMAT + P block per paragraph, the last one left out when
        // empty, so html appended to the getter's value starts a new line in a multiline field
        var (rt, _) = Empty();
        var tf = (AsTextField)rt.GetClass("flash.text.TextField").Construct();
        tf.FromSymbol(new JsonObject
        {
            ["bounds"] = new JsonObject { ["x"] = 0, ["y"] = 0, ["w"] = 200, ["h"] = 40 },
            ["text"] = new JsonObject { ["font"] = "F", ["size"] = 12, ["color"] = "#EEEEEE", ["multiline"] = true, ["html"] = true, ["leading"] = 2, ["initial"] = "" },
        });
        Assert.That(tf.htmlText, Is.EqualTo(""));
        foreach (var row in new[] { "Active", "Long Range / Physical", "a<b" })
            tf.htmlText = tf.htmlText + "<font color='#AAAAAA'>" + System.Net.WebUtility.HtmlEncode(row) + "</font>";
        Assert.That(tf.text, Is.EqualTo("Active\rLong Range / Physical\ra<b"));
        const string P = "<TEXTFORMAT LEADING=\"2\"><P ALIGN=\"LEFT\"><FONT FACE=\"F\" SIZE=\"12\" COLOR=\"#AAAAAA\" KERNING=\"0\">";
        Assert.That(tf.htmlText, Is.EqualTo(P + "Active</FONT></P></TEXTFORMAT>" + P + "Long Range / Physical</FONT></P></TEXTFORMAT>" + P + "a&lt;b</FONT></P></TEXTFORMAT>"));
        // runs: a FONT per change of colour or size, B / I / U per run, the default format where the html sets none
        tf.htmlText = "x<b>y</b><font size='20'>z</font>";
        Assert.That(tf.htmlText, Is.EqualTo("<TEXTFORMAT LEADING=\"2\"><P ALIGN=\"LEFT\"><FONT FACE=\"F\" SIZE=\"12\" COLOR=\"#EEEEEE\" KERNING=\"0\">x<B>y</B></FONT>"
            + "<FONT FACE=\"F\" SIZE=\"20\" COLOR=\"#EEEEEE\" KERNING=\"0\">z</FONT></P></TEXTFORMAT>"));
        // an empty P makes an empty paragraph, which is written when it is not the last
        tf.htmlText = "<p></p>";
        Assert.That(tf.htmlText, Is.EqualTo("<TEXTFORMAT LEADING=\"2\"><P ALIGN=\"LEFT\"><FONT FACE=\"F\" SIZE=\"12\" COLOR=\"#EEEEEE\" KERNING=\"0\"></FONT></P></TEXTFORMAT>"));
        // text clears the html flag: the getter gives the text (textSet)
        tf.text = "plain";
        Assert.That(tf.htmlText, Is.EqualTo("plain"));
    }

    [Test]
    public void HtmlTextKeepsALinkThroughTheGetter()
    {
        // ParseHtml's HTML_A keeps HREF as the run's URL; GetHtml writes it inside the FONT, outside B, and the text after
        // the end A has none (a chat input with an item link appended to its html)
        var (rt, _) = Empty();
        var tf = (AsTextField)rt.GetClass("flash.text.TextField").Construct();
        tf.FromSymbol(new JsonObject
        {
            ["bounds"] = new JsonObject { ["x"] = 0, ["y"] = 0, ["w"] = 200, ["h"] = 20 },
            ["text"] = new JsonObject { ["font"] = "F", ["size"] = 12, ["color"] = "#EEEEEE", ["html"] = true, ["initial"] = "" },
        });
        tf.htmlText = "hi";
        tf.htmlText = tf.htmlText + "<a href='event:itemTooltip,7,3,7,15000001,3'><FONT color='#5ad35f'>[<b>Sword</b>]</FONT></a> ";
        Assert.That(tf.text, Is.EqualTo("hi[Sword] "));
        const string F = "<FONT FACE=\"F\" SIZE=\"12\" COLOR=\"#{0}\" KERNING=\"0\">";
        string Font(string c) => F.Replace("{0}", c);
        Assert.That(tf.htmlText, Is.EqualTo("<TEXTFORMAT><P ALIGN=\"LEFT\">" + Font("EEEEEE") + "hi</FONT>" + Font("5AD35F")
            + "<A HREF=\"event:itemTooltip,7,3,7,15000001,3\">[</A><A HREF=\"event:itemTooltip,7,3,7,15000001,3\"><B>Sword</B></A>"
            + "<A HREF=\"event:itemTooltip,7,3,7,15000001,3\">]</A></FONT>" + Font("EEEEEE") + " </FONT></P></TEXTFORMAT>"));
    }
}
