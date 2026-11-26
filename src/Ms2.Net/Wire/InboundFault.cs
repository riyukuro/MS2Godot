using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace Ms2.Net.Wire;

/// <summary>
/// The warning of an inbound packet whose handling threw: its opcode (and name in the connection's profile), its size, the
/// packet reader that threw (the innermost Ms2.Net.Packets frame, and the outermost when the packet's reader called it) or,
/// when no reader is on the stack, the method that threw, and the exception. Built only when a packet threw.
/// </summary>
public static class InboundFault
{
    public static string Describe(RawPacket p, ProtocolProfile profile, Exception ex)
    {
        var sb = new StringBuilder("packet 0x").Append(p.Opcode.ToString("X4"));
        if (profile != null && profile.TryInbound(p.Opcode, out var op)) sb.Append(' ').Append(op);
        sb.Append(" (").Append(p.Body?.Length ?? 0).Append(" bytes)");
        var (inner, outer, top) = Frames(ex);
        if (inner != null)
        {
            sb.Append(" read by ").Append(inner);
            if (outer != null && outer != inner) sb.Append(" in ").Append(outer);
        }
        else if (top != null) sb.Append(" not in a reader, at ").Append(top);
        return sb.Append(": ").Append(ex.GetType().Name).Append(": ").Append(ex.Message).ToString();
    }

    /// <summary>The innermost and outermost packet reader frames of the exception's stack, and its first frame.</summary>
    static (string Inner, string Outer, string Top) Frames(Exception ex)
    {
        string inner = null, outer = null, top = null;
        foreach (var f in new StackTrace(ex, false).GetFrames())
        {
            if (f.GetMethod() is not MethodBase m || m.DeclaringType is not { } t) continue;
            string name = Name(t) + "." + m.Name;
            top ??= name;
            if (!IsReader(t)) continue;
            inner ??= name;
            outer = name;
        }
        return (inner, outer, top);
    }

    /// <summary>A packet type of Ms2.Net.Packets (not a static helper class such as the reader extensions).</summary>
    static bool IsReader(Type t)
    {
        for (var o = t; o != null; o = o.DeclaringType) t = o;   // a lambda's or nested type's outer type
        return t.Namespace == "Ms2.Net.Packets" && !(t.IsAbstract && t.IsSealed);
    }

    static string Name(Type t) => t.DeclaringType is { } outer && t.Name.StartsWith('<') ? Name(outer) : t.Name;
}
