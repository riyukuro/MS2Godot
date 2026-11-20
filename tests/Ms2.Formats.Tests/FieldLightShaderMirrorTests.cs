using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>
/// A C# mirror of the field light terms in game/shaders/ms2_lights.gdshaderinc (ms2_light_atten, ms2_point_lights), pinned against
/// values worked out from the client's formulas (ms2materialnodelibrary.cpp MS2Light), and a text check that the shader still
/// holds the lines the mirror copies: change one without the other and a test fails.
/// </summary>
public class FieldLightShaderMirrorTests
{
    /// <summary>Attenuate = 1 / max(1, C + L d + Q d^2); a spot multiplies saturate((cos a - cos outer) / (cos inner - cos outer))
    /// raised to the exponent, 0 where that is 0. HLSL's saturate of x / 0 is 1 for x &gt; 0, else 0.</summary>
    static float Atten(float c, float l, float q, float d, bool spot, float ca, float cosOuter, float cosInner, float exponent)
    {
        float att = 1f / MathF.Max(1f, c + l * d + q * d * d);
        if (spot)
        {
            float diff = cosInner - cosOuter;
            float t = diff != 0f ? Math.Clamp((ca - cosOuter) / diff, 0f, 1f) : (ca > cosOuter ? 1f : 0f);
            att *= t <= 0f ? 0f : MathF.Pow(t, exponent);
        }
        return att;
    }

    /// <summary>One light's share of the diffuse accumulator: diffuse * max(0, N.L) * Attenuate + ambient * (1 - max(0, N.L)).</summary>
    static float Share(float diffuse, float ambient, float ndl, float att)
    {
        ndl = MathF.Max(ndl, 0f);
        return diffuse * (ndl * att) + ambient * (1f - ndl);
    }

    [Test]
    public void AttenuateIsOneOverTheClampedPolynomial()
    {
        Assert.That(Atten(1, 0, 0, 100, false, 0, 0, 0, 1), Is.EqualTo(1f));
        Assert.That(Atten(0.5f, 0.01f, 0, 100, false, 0, 0, 0, 1), Is.EqualTo(1f / 1.5f).Within(1e-6), "0.5 + 1 = 1.5");
        Assert.That(Atten(0, 0, 0.0001f, 100, false, 0, 0, 0, 1), Is.EqualTo(1f).Within(1e-6), "0.0001 * 1e4 = 1: 1/1");
        Assert.That(Atten(0, 0, 0.0002f, 100, false, 0, 0, 0, 1), Is.EqualTo(0.5f).Within(1e-6), "2 -> 1/2");
        Assert.That(Atten(0.05f, 0, 0, 10, false, 0, 0, 0, 1), Is.EqualTo(1f), "a sum under 1 never boosts");
    }

    [Test]
    public void SpotTermUsesTheAngleBandAndExponent()
    {
        float outer = MathF.Cos(30 * MathF.PI / 180), inner = MathF.Cos(20 * MathF.PI / 180);
        Assert.That(Atten(1, 0, 0, 0, true, 1f, outer, inner, 2), Is.EqualTo(1f).Within(1e-6), "inside the inner cone");
        Assert.That(Atten(1, 0, 0, 0, true, outer - 0.01f, outer, inner, 2), Is.EqualTo(0f), "outside the outer cone");
        float mid = (outer + inner) / 2;
        Assert.That(Atten(1, 0, 0, 0, true, mid, outer, inner, 2), Is.EqualTo(0.25f).Within(1e-6), "t = 0.5, exponent 2");
        // diff = 0 guard: a zero-width band is a step at the outer cosine
        Assert.That(Atten(1, 0, 0, 0, true, 0.9f, 0.8f, 0.8f, 3), Is.EqualTo(1f));
        Assert.That(Atten(1, 0, 0, 0, true, 0.7f, 0.8f, 0.8f, 3), Is.EqualTo(0f));
        Assert.That(Atten(1, 0, 0, 0, true, 0.8f, 0.8f, 0.8f, 3), Is.EqualTo(0f), "ca == outer is not above it");
    }

    [Test]
    public void AmbientTakesTheShareNdlLeavesAndIsNotAttenuated()
    {
        Assert.That(Share(2f, 0.5f, 1f, 0.5f), Is.EqualTo(1f), "facing: diffuse * att");
        Assert.That(Share(2f, 0.5f, 0f, 0.5f), Is.EqualTo(0.5f), "edge-on: all ambient");
        Assert.That(Share(2f, 0.5f, -1f, 0.1f), Is.EqualTo(0.5f), "facing away clamps N.L to 0");
        Assert.That(Share(2f, 0.5f, 0.5f, 0.5f), Is.EqualTo(0.5f + 0.25f), "2 * 0.25 + 0.5 * 0.5");
    }

    static string Include()
    {
        for (var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); dir != null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, "game", "shaders", "ms2_lights.gdshaderinc");
            if (File.Exists(path)) return File.ReadAllText(path);
        }
        Assert.Fail("game/shaders/ms2_lights.gdshaderinc not found above the test directory");
        return null!;
    }

    static string Flat(string s) => string.Join(" ", s.Split((char[])null!, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>The shader lines each mirror above copies, whitespace-flattened. Edit the shader and these, and the mirror.</summary>
    [TestCase("float att = 1.0 / max(1.0, texelFetch(ms2_lights, ivec2(i, 1), 0).w + texelFetch(ms2_lights, ivec2(i, 2), 0).w * d + texelFetch(ms2_lights, ivec2(i, 3), 0).w * d * d);")]
    [TestCase("float diff = r5.x - r4.w;")]
    [TestCase("float t = diff != 0.0 ? clamp((ca - r4.w) / diff, 0.0, 1.0) : (ca > r4.w ? 1.0 : 0.0);")]
    [TestCase("att *= t <= 0.0 ? 0.0 : pow(t, r5.y);")]
    [TestCase("float ndl = max(dot(l, n), 0.0);")]
    [TestCase("sum += ms2_light_diffuse(i) * (ndl * ms2_light_atten(i, r0, l, d));")]
    [TestCase("sum += ms2_light_ambient(i) * (1.0 - ndl);")]
    public void ShaderStillHoldsTheMirroredLine(string line)
    {
        Assert.That(Flat(Include()), Does.Contain(Flat(line)), "the shader formula changed: update the C# mirror in this file with it");
    }
}
