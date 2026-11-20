using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>
/// The forms ModelBuilder compiles from game/shaders/field_lit.gdshader (its #define lines), resolved with the shader's
/// #ifdef/#ifndef/#else/#endif: which forms are lit, so take the sun's shadow in light(). The client draws a blended
/// receiver with the shadow: its receiver list (ms2shadowclickgenerator.cpp:914-975) and the material descriptor do not
/// filter on alpha, and a shadowed light turns per-vertex lighting off (NiStandardMaterial.cpp:652-666).
/// </summary>
public class FieldShaderFormsTests
{
    static readonly (string name, string[] defines)[] Forms =
    {
        ("opaque", new string[0]),
        ("blend", new[] { "BLEND" }),
        ("add", new[] { "BLEND", "ADD" }),
        ("fade", new[] { "BLEND", "FADE" }),
        ("fade add", new[] { "BLEND", "ADD", "FADE" }),
        ("shell", new[] { "BLEND", "SHELL" }),
    };

    static string ShaderSource(string file = "field_lit.gdshader")
    {
        for (var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); dir != null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, "game", "shaders", file);
            if (File.Exists(path)) return File.ReadAllText(path);
        }
        Assert.Fail($"game/shaders/{file} not found above the test directory");
        return null!;
    }

    /// <summary>The lines left after the conditionals (#ifdef, #ifndef, #if defined(X), #elif defined(X), #else, #endif), with
    /// the given names defined (#define inside a live block adds one).</summary>
    static List<string> Resolve(string src, IEnumerable<string> defines)
    {
        var defined = new HashSet<string>(defines);
        // per open block: whether its parent is live, and whether a branch of it has been taken
        var stack = new Stack<(bool parentLive, bool taken)>();
        bool live = true;
        var output = new List<string>();
        bool Test(string line)
        {
            if (line.StartsWith("#ifdef ")) return defined.Contains(line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1]);
            if (line.StartsWith("#ifndef ")) return !defined.Contains(line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1]);
            var m = System.Text.RegularExpressions.Regex.Match(line, @"^#(?:el)?if\s+defined\s*\(\s*(\w+)\s*\)\s*$");
            Assert.That(m.Success, $"unsupported conditional: {line}");
            return defined.Contains(m.Groups[1].Value);
        }
        foreach (var raw in src.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("#ifdef ") || line.StartsWith("#ifndef ") || line.StartsWith("#if "))
            {
                bool cond = Test(line);
                stack.Push((live, cond));
                live = live && cond;
            }
            else if (line.StartsWith("#elif "))
            {
                Assert.That(stack, Is.Not.Empty, $"#elif without #if: {line}");
                var (parentLive, taken) = stack.Pop();
                bool cond = !taken && Test(line);
                stack.Push((parentLive, taken || cond));
                live = parentLive && cond;
            }
            else if (line.StartsWith("#else"))
            {
                Assert.That(stack, Is.Not.Empty, "#else without #if");
                var (parentLive, taken) = stack.Peek();
                live = parentLive && !taken;
            }
            else if (line.StartsWith("#endif"))
            {
                Assert.That(stack, Is.Not.Empty, "#endif without #if");
                live = stack.Pop().parentLive;
            }
            else if (live)
            {
                if (line.StartsWith("#define ")) defined.Add(line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1]);
                else output.Add(line);
            }
        }
        Assert.That(stack, Is.Empty, "unbalanced conditionals");
        return output;
    }

    [Test]
    public void BlendedFormsTakeTheSunShadow()
    {
        var src = ShaderSource();
        foreach (var (name, defines) in Forms)
        {
            var lines = Resolve(src, defines);
            var modes = lines.Where(l => l.StartsWith("render_mode ")).ToList();
            Assert.That(modes, Has.Count.EqualTo(1), name);
            bool lit = name != "shell";
            Assert.That(modes[0].Contains("unshaded"), Is.EqualTo(!lit), $"{name}: {modes[0]}");
            Assert.That(lines.Contains("void light() {"), Is.EqualTo(lit), $"{name}: light()");
            Assert.That(lines.Any(l => l.Contains("ATTENUATION")), Is.EqualTo(lit), $"{name}: shadow attenuation");
            if (lit)
            {
                Assert.That(modes[0], Does.Contain("ambient_light_disabled").And.Contain("specular_disabled"), name);
                Assert.That(lines.Any(l => l.StartsWith("instance uniform float ms2_receive")), name);
            }
        }
    }

    /// <summary>Godot gives instance uniforms their index by declaration order per shader, and one name must have one index in
    /// every material of an instance (a fluid mesh draws the shell form with a blend colour pass as next_pass): the uniforms
    /// shared by several forms come first, in the same order.</summary>
    [Test]
    public void InstanceUniformsKeepTheirIndexInEveryForm()
    {
        var src = ShaderSource();
        var decls = Forms.Select(f => Resolve(src, f.defines)
            .Where(l => l.StartsWith("instance uniform ")).Select(l => l.Split(' ')[3].TrimEnd(';', '=')).ToList()).ToList();
        foreach (var d in decls) Assert.That(d[0], Is.EqualTo("ms2_light_off"));
        // ms2_receive exists only in the lit forms, after ms2_light_off
        foreach (var d in decls.Where(d => d.Count > 1)) Assert.That(d, Is.EqualTo(new[] { "ms2_light_off", "ms2_receive" }));
    }

    /// <summary>The cull mode each cull define (ModelBuilder.CullDefine) gives, in the colour shader and the glow shader alike:
    /// the glow click swaps the material only (postprocessingservice.cpp:2830-2836), so the draw mode culls the glow too.</summary>
    [TestCase("field_lit.gdshader")]
    [TestCase("field_glow.gdshader")]
    public void CullFollowsTheDefine(string file)
    {
        var src = ShaderSource(file);
        foreach (var (cull, mode) in new[] { ((string)null, "cull_back"), ("CULL_NONE", "cull_disabled"), ("CULL_FRONT", "cull_front") })
        {
            var defines = cull == null ? new string[0] : new[] { cull };
            var modes = Resolve(src, defines).Where(l => l.StartsWith("render_mode ")).ToList();
            Assert.That(modes, Has.Count.EqualTo(1), $"{file} {cull}");
            Assert.That(modes[0].TrimEnd(';').EndsWith(", MS2_CULL"), $"{file} {cull}: {modes[0]}");
            // the live #define of MS2_CULL, read by turning it into a plain line
            var cullLine = Resolve(src.Replace("#define MS2_CULL ", "MS2_CULL= "), defines).Single(l => l.StartsWith("MS2_CULL= "));
            Assert.That(cullLine.Substring("MS2_CULL= ".Length).Trim(), Is.EqualTo(mode), $"{file} {cull}");
        }
    }

    /// <summary>The glow pass runs the colour pass's alpha test, function and reference included (MS2GlowMaterial ends in
    /// HandleMS2FinalPixelOutputs -> HandleAlphaTest, ms2standardmaterial.cpp:786): field_glow clips with field_lit's test.</summary>
    [Test]
    public void GlowPassClipsAsTheColourPass()
    {
        static string Test(string src) => src.Split('\n').Select(l => l.Trim()).Single(l => l.StartsWith("if (alpha_test"));
        Assert.That(Test(ShaderSource("field_glow.gdshader")), Is.EqualTo(Test(ShaderSource())));
        Assert.That(ShaderSource("field_glow.gdshader"), Does.Contain("uniform vec3 alpha_clip"));
    }
    /// <summary>Character meshes with the NiAlphaProperty blend bit blend as the field's (FieldShader): the BLEND form mixes
    /// without the z write and writes the material alpha times the base alpha, BLEND + ADD (destination factor ONE) adds,
    /// and the opaque form stays opaque. Without these forms a blended NPC or item part drew as an opaque sheet.</summary>
    [Test]
    public void CharacterShaderHasTheBlendedForms()
    {
        var src = ShaderSource("character_lit.gdshader");
        string Mode(params string[] d) => Resolve(src, d).Single(l => l.StartsWith("render_mode "));
        Assert.That(Mode(), Does.Not.Contain("blend_"));
        Assert.That(Mode("BLEND"), Does.Contain("blend_mix").And.Contain("depth_draw_never"));
        Assert.That(Mode("BLEND", "ADD"), Does.Contain("blend_add").And.Contain("depth_draw_never"));
        Assert.That(Resolve(src, new[] { "BLEND" }).Any(l => l.StartsWith("ALPHA = ")), Is.True);
        Assert.That(Resolve(src, new string[0]).Any(l => l.StartsWith("ALPHA = ")), Is.False);
    }
}
