using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ms2.Ui.Launch;

/// <summary>
/// One server the launcher's list keeps: a name to show and the login server's address as host:port. The address is
/// validated when the entry is made, so every entry the store holds is connectable.
/// </summary>
public sealed record ServerEntry(string Name, string Host, int Port)
{
    /// <summary>The address as the list shows it and the store saves it.</summary>
    public string Address => Host + ":" + Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// A login server address, "host:port", for the launcher's text fields. The host is a host name or an IPv4 literal;
/// the port is a whole number from 1 to 65535. Kept here, free of Godot, so the tests can cover every rejected case.
/// </summary>
public static class ServerAddress
{
    public const int MinPort = 1, MaxPort = 65535;

    /// <summary>
    /// Parses <paramref name="text"/>. False with a message for the field on the caller's screen when the text is not a
    /// usable address; nothing is guessed.
    /// </summary>
    public static bool TryParse(string text, out string host, out int port, out string problem)
    {
        host = "";
        port = 0;
        problem = null;
        string t = (text ?? "").Trim();
        if (t.Length == 0)
        {
            problem = "Enter the address as host:port.";
            return false;
        }
        int colon = t.LastIndexOf(':');
        if (colon < 0 || t.IndexOf(':') != colon)
        {
            problem = "Use host:port, with one colon.";
            return false;
        }
        string h = t[..colon].Trim();
        string p = t[(colon + 1)..].Trim();
        if (h.Length == 0)
        {
            problem = "The host is empty.";
            return false;
        }
        if (h.Any(char.IsWhiteSpace))
        {
            problem = "The host cannot contain spaces.";
            return false;
        }
        if (!int.TryParse(p, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int value)
            || value < MinPort || value > MaxPort)
        {
            problem = "The port must be a whole number from 1 to 65535.";
            return false;
        }
        host = h;
        port = value;
        return true;
    }

    /// <summary>A name for the list: not empty, trimmed.</summary>
    public static bool TryName(string text, out string name, out string problem)
    {
        name = (text ?? "").Trim();
        problem = name.Length == 0 ? "Enter a name." : null;
        return problem == null;
    }
}

/// <summary>
/// The launcher's saved server list: user://servers.json (the caller gives the path, so tests use their own). A missing
/// file is an empty list; a file that does not parse is an empty list too, never an exception. Entries whose address no
/// longer parses are dropped on load. The file holds no account and no password.
/// </summary>
public sealed class ServerList
{
    public const string DefaultFileName = "servers.json";

    /// <summary>What the file holds, in the order the list shows.</summary>
    public sealed class FileFormat
    {
        [JsonPropertyName("servers")] public List<FileEntry> Servers { get; set; } = new();
    }

    public sealed class FileEntry
    {
        [JsonPropertyName("name")] public string Name { get; set; }
        [JsonPropertyName("address")] public string Address { get; set; }
    }

    static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    readonly List<ServerEntry> entries = new();
    public IReadOnlyList<ServerEntry> Entries => entries;
    public int Count => entries.Count;

    /// <summary>Reads the list at <paramref name="path"/>; every failure gives an empty list.</summary>
    public static ServerList Load(string path)
    {
        var list = new ServerList();
        try
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return list;
            var file = JsonSerializer.Deserialize<FileFormat>(File.ReadAllText(path));
            if (file?.Servers == null) return list;
            foreach (var row in file.Servers)
            {
                if (row == null) continue;
                if (!ServerAddress.TryParse(row.Address, out var host, out int port, out _)) continue;
                string name = (row.Name ?? "").Trim();
                if (name.Length == 0) continue;
                list.entries.Add(new ServerEntry(name, host, port));
            }
        }
        catch (Exception)
        {
            // a truncated or hand-edited file starts the list empty, as a missing one does
            list.entries.Clear();
        }
        return list;
    }

    /// <summary>Writes the list. False with a message when the file cannot be written (the caller shows it).</summary>
    public bool Save(string path, out string problem)
    {
        problem = null;
        try
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var file = new FileFormat
            {
                Servers = entries.Select(e => new FileEntry { Name = e.Name, Address = e.Address }).ToList(),
            };
            File.WriteAllText(path, JsonSerializer.Serialize(file, WriteOptions));
            return true;
        }
        catch (Exception e)
        {
            problem = "Could not save the server list: " + e.Message;
            return false;
        }
    }

    /// <summary>Appends a checked entry. False with a message when the name or the address is not usable.</summary>
    public bool Add(string name, string address, out string problem)
    {
        if (!TryEntry(name, address, out var entry, out problem)) return false;
        entries.Add(entry);
        return true;
    }

    /// <summary>Replaces the entry at <paramref name="index"/>, checked as <see cref="Add"/> checks.</summary>
    public bool Edit(int index, string name, string address, out string problem)
    {
        problem = null;
        if (index < 0 || index >= entries.Count) { problem = "That server is no longer in the list."; return false; }
        if (!TryEntry(name, address, out var entry, out problem)) return false;
        entries[index] = entry;
        return true;
    }

    /// <summary>Drops the entry at <paramref name="index"/>; false when it is not there any more.</summary>
    public bool RemoveAt(int index)
    {
        if (index < 0 || index >= entries.Count) return false;
        entries.RemoveAt(index);
        return true;
    }

    static bool TryEntry(string name, string address, out ServerEntry entry, out string problem)
    {
        entry = null;
        if (!ServerAddress.TryName(name, out string clean, out problem)) return false;
        if (!ServerAddress.TryParse(address, out string host, out int port, out problem)) return false;
        entry = new ServerEntry(clean, host, port);
        return true;
    }
}
