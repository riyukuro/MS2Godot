using System.Numerics;

namespace Ms2.Formats.Model;

/// <summary>The quads of NiPSAlignedQuadGenerator. The system's Normal Method and Up Method (NiPSParticleSystem::AlignMethod,
/// NiPSParticleSystem.h:69-90) choose the kernel: a method with ALIGN_PER_PARTICLE (bit 0) takes each particle's position
/// (ALIGN_LOCAL_POSITION) or velocity (ALIGN_LOCAL_VELOCITY), the others one vector for the whole system, giving the FF, FV,
/// VF and VV kernels (NiPSAlignedQuadGenerator::AttachAlignMethods, NiPSAlignedQuadGenerator.cpp:357-405). The fixed
/// vectors are set per frame (SubmitAlignMethods, :555-735): ALIGN_LOCAL_FIXED takes the system's original world rotation
/// times the direction, unitized with UNIT_X for the normal and UNIT_Z for the up when it is zero; ALIGN_CAMERA takes
/// camera right x camera up for the normal and camera up for the up. In every kernel right = up.UnitCross(normal) and
/// up' = normal x right, and a quad's corners are its position +-(X +- Y) less the pivot offset
/// (NiPSAlignedQuadGeneratorKernelFF.cpp:42-169): X = size (cos sU right - sin sV up), Y = size (sin sU right + cos sV up),
/// the offset ((cV sin + cU cos) right + (cV cos - cU sin) up) with cU = size sU Center U and cV = size sV Center V. Vertex 0
/// (-X + Y) takes the texture's top left (NiPSAlignedQuadTextureKernel.cpp:136-160). Floats, like the kernels.</summary>
public static class AlignedQuad
{
    public const int PerParticle = 0x01, LocalFixed = 0x02, LocalPosition = 0x05, LocalVelocity = 0x09, Camera = 0x10;

    /// <summary>The kernel's scale amount: the amount over (limit - rest), 0 when the two are within NIPSKERNEL_EPSILON
    /// (NiPSAlignedQuadGenerator::NormalizeScaleAmountU/V, NiPSAlignedQuadGenerator.inl:15-40; NiPSKernelDefinitions.h:78).</summary>
    public static float NormalizedAmount(double amount, double limit, double rest)
    {
        float a = (float)amount, l = (float)limit, r = (float)rest;
        return MathF.Abs(l - r) < 0.0001f ? 0f : a / (l - r);
    }

    /// <summary>The generator refuses a positive amount with limit &lt; rest or a negative one with limit &gt;= rest, and the
    /// system then gets no quads (NiPSAlignedQuadGenerator.cpp:281-303).</summary>
    public static bool ScaleValid(ParticleSystemDef d) =>
        !(d.ScaleAmountU < 0 && d.ScaleLimitU >= d.ScaleRestU || d.ScaleAmountU > 0 && d.ScaleLimitU < d.ScaleRestU
          || d.ScaleAmountV < 0 && d.ScaleLimitV >= d.ScaleRestV || d.ScaleAmountV > 0 && d.ScaleLimitV < d.ScaleRestV);

    /// <summary>A quad side's scale at a speed: (amount v limit + rest) / (amount v + 1), amount normalized
    /// (NiPSAlignedQuadGeneratorKernelFF.cpp:65-79).</summary>
    public static float SpeedScale(float amount, float limit, float rest, float speed) =>
        (amount * speed * limit + rest) / (amount * speed + 1f);

    /// <summary>Point3::Unitize(zeroLengthDefault) (efd/Win32/Point3_Win32.inl:180-221): the default when the length is
    /// 1e-6 or less; Vector3.Zero as the default is plain Unitize.</summary>
    public static Vector3 Unitize(Vector3 v, Vector3 zeroDefault)
    {
        float l = v.Length();
        return l > 1e-6f ? v * (1f / l) : zeroDefault;
    }

    /// <summary>Point3::UnitCross (efd/Win32/Point3_Win32.inl:235-247): zero when the cross is 1e-6 or shorter.</summary>
    public static Vector3 UnitCross(Vector3 a, Vector3 b)
    {
        var c = Vector3.Cross(a, b);
        float l = c.Length();
        return l > 1e-6f ? c / l : Vector3.Zero;
    }

    /// <summary>One quad: its X and Y axes (top right = position + X + Y) and the offset its centre moves by, from the
    /// normal and up the kernel uses for it.</summary>
    public static void Quad(Vector3 normal, Vector3 up, float size, float scaleU, float scaleV, float centerU, float centerV,
                            float angle, out Vector3 x, out Vector3 y, out Vector3 offset)
    {
        var right = UnitCross(up, normal);
        var upq = Vector3.Cross(normal, right);
        float s = MathF.Sin(angle), c = MathF.Cos(angle);
        x = size * (c * scaleU * right - s * scaleV * upq);
        y = size * (s * scaleU * right + c * scaleV * upq);
        float cu = size * scaleU * centerU, cv = size * scaleV * centerV;
        offset = (cv * s + cu * c) * right + (cv * c - cu * s) * upq;
    }

    /// <summary>The texture animation of a system with "Has Animated Textures" (NiPSAlignedQuadTextureKernel.cpp:40-160, the
    /// kernel the generator adds only for such a system, NiPSAlignedQuadGenerator.cpp:325-337; the others keep the whole
    /// texture, :208-218). The generator's times are fractions of the particle's life; the frame runs linearly from the
    /// initial frame to initial + Num Frames over that span (age clamped to it, or folded back and forth with Ping Pong),
    /// each bound moved by floor(variance x its variation), variance the particle's NiSymmetricRandom from its spawn
    /// (NiPSParticleSystem.cpp:262-265). The frame is laid out across then down from the texture's top left; with UV
    /// Scrolling the floors are left out, so the cell slides. Floats, like the kernel.</summary>
    public readonly struct TexAnim
    {
        public readonly bool On, PingPong, UVScrolling;
        public readonly float Across, DeltaU, DeltaV, NormInitial, NormFinal, InitialFrame, InitialFrameVar, NumFrames, NumFramesVar;

        public TexAnim(ParticleSystemDef d)
        {
            On = d.Aligned && d.AnimatedTextures; PingPong = d.PingPong; UVScrolling = d.UVScrolling;
            Across = d.FramesAcross; DeltaU = 1f / d.FramesAcross; DeltaV = 1f / d.FramesDown;
            NormInitial = (float)d.InitialTime; NormFinal = (float)d.FinalTime;
            InitialFrame = d.InitialFrame; InitialFrameVar = (float)d.InitialFrameVar;
            NumFrames = d.NumFrames; NumFramesVar = (float)d.NumFramesVar;
        }

        /// <summary>The quad's texture cell: its top left (left, top) in texture coordinates; it spans DeltaU x DeltaV.
        /// Vertex 0 (the quad's top left) takes (left, top), vertex 2 (bottom right) (left + DeltaU, top + DeltaV).</summary>
        public void Cell(float age, float life, float variance, out float left, out float top)
        {
            float initialTime = NormInitial * life, finalTime = NormFinal * life, scaledAge;
            if (!PingPong) scaledAge = age < initialTime ? initialTime : age > finalTime ? finalTime : age;
            else
            {
                float keyDiff = finalTime - initialTime;
                if (keyDiff > 0.0001f)
                {
                    float whole = MathF.Floor(age / keyDiff);
                    scaledAge = (uint)whole % 2 > 0 ? finalTime - (age - finalTime * whole) : age - finalTime * whole;
                }
                else scaledAge = age;
            }
            float normTime = (scaledAge - initialTime) / (finalTime - initialTime);
            float init, fin;
            if (!UVScrolling) { init = InitialFrame + MathF.Floor(variance * InitialFrameVar); fin = init + (NumFrames + MathF.Floor(variance * NumFramesVar)); }
            else { init = InitialFrame + variance * InitialFrameVar; fin = init + (NumFrames + variance * NumFramesVar); }
            float linear = (1f - normTime) * init + normTime * fin;
            float row = MathF.Floor(linear * DeltaU);
            float col = (linear * DeltaU - row) * Across;
            if (!UVScrolling) col = MathF.Floor(col);
            left = col * DeltaU; top = row * DeltaV;
        }
    }

    /// <summary>The normal and up a particle's quad is built from. fixedNormal and fixedUp are the system's vectors for the
    /// methods without ALIGN_PER_PARTICLE (already unitized as SubmitAlignMethods leaves them); rel is the particle's
    /// position from the system's origin and vel its velocity; unitX and unitZ the kernels' defaults in the same space.
    /// The VF kernel unitizes the per-particle normal (NiPSAlignedQuadGeneratorKernelVF.cpp:78-82); the FV kernel uses
    /// the per-particle up as is (NiPSAlignedQuadGeneratorKernelFV.cpp:76-79); the VV kernel unitizes the normal with UNIT_X
    /// and the up with UNIT_Z when the system has rotations, else the normal plainly and the up not at all
    /// (NiPSAlignedQuadGeneratorKernelVV.cpp:93-102, 173-179). Only the direction of the transformed input counts, so the
    /// 1/scale of SubmitAlignMethods is left out.</summary>
    public static void Inputs(int normalMethod, int upMethod, bool rotations, Vector3 fixedNormal, Vector3 fixedUp,
                              Vector3 rel, Vector3 vel, Vector3 unitX, Vector3 unitZ, out Vector3 normal, out Vector3 up)
    {
        bool nPer = (normalMethod & PerParticle) != 0, uPer = (upMethod & PerParticle) != 0;
        normal = fixedNormal; up = fixedUp;
        if (nPer)
        {
            var n = normalMethod == LocalPosition ? rel : vel;
            normal = Unitize(n, uPer && rotations ? unitX : Vector3.Zero);
        }
        if (uPer)
        {
            var u = upMethod == LocalPosition ? rel : vel;
            up = nPer && rotations ? Unitize(u, unitZ) : u;
        }
    }
}
