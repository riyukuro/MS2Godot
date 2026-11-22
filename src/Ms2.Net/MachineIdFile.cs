using System.Security.Cryptography;

namespace Ms2.Net;

/// <summary>
/// The 16 byte machine id of a GMS2 ticket login: random bytes made once and kept in a file, so one install sends the same id on every login
/// (the recordings show one id per session). Not derived from the machine, the user or any account.
/// </summary>
public static class MachineIdFile {
    /// <summary>Reads the id from <paramref name="path"/>, or makes and writes it when the file is missing or not 16 bytes (never all zeros). Fills <paramref name="id"/> and returns it.</summary>
    public static byte[] LoadOrCreate(string path, byte[] id) {
        if (id.Length != 16) throw new ArgumentException("machine id must be 16 bytes");
        try {
            if (File.Exists(path)) {
                var b = File.ReadAllBytes(path);
                if (b.Length == 16 && b.Any(x => x != 0)) { b.CopyTo(id, 0); return id; }
            }
        } catch (IOException) { } catch (UnauthorizedAccessException) { }
        do RandomNumberGenerator.Fill(id); while (id.All(x => x == 0));
        try {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllBytes(path, id);
        } catch (IOException) { } catch (UnauthorizedAccessException) { }   // unwritable: the id lasts for this run only
        return id;
    }
}
