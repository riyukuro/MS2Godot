using Ms2.Formats.Pack;

namespace Ms2.Formats.Model;

/// <summary>
/// A pc body's sequence by name, as the port finds it: Resource/Model/Character/&lt;body&gt;/&lt;name&gt;.kf in lower case
/// (body "male" or "female"). The body's kfm keeps the kf paths with empty names for many of them (male.kfm: 123 of its
/// sequences), so the name is the kf file's. STILL-OPEN: PCBase::PlayAnimation (@0x14166dd20, pcbase.cpp:1521-1527)
/// hands the name to the actor's animation manager, whose lookup is not read.
/// </summary>
public static class BodySequences
{
    public const string Archive = "Resource/Model/Character";

    /// <summary>The entry of <paramref name="name"/> in <see cref="Archive"/> for a body folder.</summary>
    public static string Entry(string body, string name) => $"{body}/{name.ToLowerInvariant()}.kf";

    /// <summary>Whether the body has a sequence of that name.</summary>
    public static bool Exists(PackSet packs, string body, string name) =>
        !string.IsNullOrEmpty(name) && packs.Exists(Archive, Entry(body, name));
}
