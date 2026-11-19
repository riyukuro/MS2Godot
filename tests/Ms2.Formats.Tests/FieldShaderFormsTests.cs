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

    static string ShaderSource()
    {
        for (var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); dir != null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, "game", "shaders", "field_lit.gdshader");
            if (File.Exists(path)) return File.ReadAllText(path);
        }
        Assert.Fail("game/shaders/field_lit.gdshader not found above the test directory");
        return null!;
    }

    /// <summary>The lines left after the conditionals, with the given names defined (#define inside a live block adds one).</summary>
    static List<string> Resolve(string src, IEnumerable<string> defines)
    {
        var defined = new HashSet<string>(defines);
        var stack = new Stack<(bool parentLive, bool cond)>();
        bool live = true;
        var output = new List<string>();
        foreach (var raw in src.Split('\n'))
        {
            var line = raw.Trim();
            string[] w = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (line.StartsWith("#ifdef ") || line.StartsWith("#ifndef "))
            {
                bool cond = defined.Contains(w[1]) == line.StartsWith("#ifdef ");
                stack.Push((live, cond));
                live = live && cond;
            }
            else if (line.StartsWith("#else"))
            {
                var (parentLive, cond) = stack.Peek();
                live = parentLive && !cond;
            }
            else if (line.StartsWith("#endif"))
            {
                live = stack.Pop().parentLive;
            }
            else if (live)
            {
                if (line.StartsWith("#define ")) defined.Add(w[1]);
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
}
