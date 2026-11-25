using Ms2.Swf.Abc;

namespace Ms2.Ui;

/// <summary>
/// The login background's title events. The background movie's title sprite (Background.getContent: the loaded movie's
/// first child, e.g. BG_Henesys_Login_1) has frame scripts that dispatch an MS2ObjectEvent with eventName "showTitle" and
/// "endShowTitle"; Background.onDispathEvent passes the name on with ExternalInterface.call("dispatchEvent", name), which
/// reaches UISceneLogin::vfunc_61 (uiscenelogin.cpp:243-255): ShowTitle, and bit 0x4000. The frames differ between the
/// movies (endShowTitle on frame 320 or 231), so they are read from the title class's own code: the constructor's
/// addFrameScript(index, method, ...) pairs and the event names those methods push.
/// </summary>
public static class LoginTitleEvents
{
    /// <summary>The two events vfunc_61 answers.</summary>
    public const string ShowTitle = "showTitle", EndShowTitle = "endShowTitle";

    /// <summary>
    /// The title events of class <paramref name="className"/> (a qualified "package.Name" or the bare name) in
    /// <paramref name="abcs"/>: the frame index (0-based, as addFrameScript counts) and the event, in frame order. Empty when
    /// the class or its frame scripts are not there.
    /// </summary>
    public static List<(int Frame, string Event)> Find(IEnumerable<AbcFile> abcs, string className)
    {
        var found = new List<(int Frame, string Event)>();
        if (abcs == null || string.IsNullOrEmpty(className)) return found;
        string local = className[(Math.Max(className.LastIndexOf('.'), className.LastIndexOf(':')) + 1)..];
        foreach (var abc in abcs)
        {
            var inst = abc.Instances.FirstOrDefault(i => NameOf(abc, i.Name) == local);
            if (inst == null) continue;
            var methods = new Dictionary<string, int>();
            foreach (var t in inst.Traits)
                if (t.Kind == TraitKind.Method && NameOf(abc, t.Name) is { } n) methods[n] = t.MethodIndex;
            foreach (var (frame, method) in FrameScripts(abc, abc.Methods[inst.Init].Body))
            {
                if (!methods.TryGetValue(method, out int mi) || abc.Methods[mi].Body is not { } body) continue;
                foreach (var insn in Disasm.Decode(body.Code))
                    if (insn.Op == 0x2C && abc.Strings[insn.A] is ShowTitle or EndShowTitle) found.Add((frame, abc.Strings[insn.A]));
            }
            break;
        }
        found.Sort((a, b) => a.Frame.CompareTo(b.Frame));
        return found;
    }

    /// <summary>
    /// The frame scripts the title's playhead has reached: every event from <paramref name="next"/> on whose frame is at or
    /// before <paramref name="frame"/> goes to <paramref name="send"/> once, in order (a jump past several frames runs each).
    /// </summary>
    public static void Due(IReadOnlyList<(int Frame, string Event)> events, ref int next, int frame, Action<string> send)
    {
        if (events == null) return;
        while (next < events.Count && events[next].Frame <= frame) send(events[next++].Event);
    }

    /// <summary>The constructor's addFrameScript arguments: an integer push, then the method's property read.</summary>
    static IEnumerable<(int Frame, string Method)> FrameScripts(AbcFile abc, MethodBody ctor)
    {
        if (ctor == null) yield break;
        int? frame = null;
        foreach (var insn in Disasm.Decode(ctor.Code))
        {
            switch (insn.Op)
            {
                case 0x24: case 0x25: frame = insn.A; break;                  // pushbyte, pushshort
                case 0x2D: frame = abc.Ints[insn.A]; break;                    // pushint
                case 0x2E: frame = (int)abc.Uints[insn.A]; break;              // pushuint
                case 0xD0: break;                                              // getlocal0 (this) between the two
                case 0x66: case 0x60:                                          // getproperty, getlex
                    if (frame is { } f && NameOf(abc, insn.A) is { } m) yield return (f, m);
                    frame = null;
                    break;
                default: frame = null; break;
            }
        }
    }

    static string NameOf(AbcFile abc, int multiname) =>
        multiname > 0 && multiname < abc.Multinames.Length ? abc.Multinames[multiname].Name : null;
}
