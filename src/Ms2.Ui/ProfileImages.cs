using System.Text;

namespace Ms2.Ui;

/// <summary>
/// The client's profile picture paths and downloads (ugcimagepath.cpp, CUgcManager's profile downloads, ugcmanager_2.cpp /
/// ugcmanager_4.cpp), without the files: the host checks whether a file is in the download folder (<see cref="Exists"/>),
/// fetches a requested key (<see cref="DownloadRequested"/>) and reports it back (<see cref="Downloaded"/>).
/// <list type="bullet">
/// <item>A key is the web server's path of the picture, "data/profiles/avatar/&lt;character id&gt;/&lt;name&gt;.png".</item>
/// <item>The movies are given "./Custom/Download/Profile/&lt;file name&gt;" (<see cref="ProfileImagePath"/>), the default
/// image for none.</item>
/// <item>A finished download raises <see cref="Ready"/> for every character that asked for the key
/// (UIUGCEventDispatcher::OnProfileImage); the windows that show the picture refresh from it.</item>
/// </list>
/// STILL-OPEN: a blocked buddy's BlockedProfile (FUN_141028be0's CBuddyManager check) needs a buddy list, and the CN
/// download layout (FUN_141026d10, a region of "cn") is not ported: every key downloads from the host.
/// </summary>
public sealed class ProfileImages
{
    /// <summary>GetProfileImagePath's folder.</summary>
    public const string Folder = "./Custom/Download/Profile";

    /// <summary>CommonResource m_strNoProfile (table/commonresource.xml DEFAULT_PROFILE_IMAGE_PATH).</summary>
    public string NoProfile { get; set; } = "";

    /// <summary>DAT_143accfc0: the host the downloads come from (Ugc mode 0x11); "" before the login server sent it.</summary>
    public string Host { get; set; } = "";

    /// <summary>CSoapClient m_strPath: the base "urq.aspx" is posted to (Ugc mode 0x11 in SOAP mode 2), "" without one.</summary>
    public string UploadBase { get; set; } = "";

    /// <summary>CUgcManager::GetMode (Ugc mode 0x16): in mode 1 every key the packets carry is taken as "" and no upload opens.</summary>
    public int Mode { get; set; }

    /// <summary>_waccess on GetDownloadFilePath(5, key): whether the file of this name is in the download folder.</summary>
    public Func<string, bool> Exists = _ => false;

    /// <summary>A download to start: the key, its url and the file name to keep it under.</summary>
    public event Action<string, string, string> DownloadRequested;

    /// <summary>UIUGCEventDispatcher::OnProfileImage(character id, key): a picture is ready (downloaded, or the field's
    /// profile picture packet).</summary>
    public event Action<long, string> Ready;

    /// <summary>A download finished for the character, raised before <see cref="Ready"/>: ProcessResult sets the key as the
    /// picture of the character's PC in the field (the own PC or FUN_141bd1530's user) outside the lobby.</summary>
    public event Action<long, string> Arrived;

    readonly Dictionary<string, List<long>> pending = new(StringComparer.Ordinal);

    /// <summary>FUN_141027490 (@0x141027490): a key of 5 parts split on "/" gives "{1}{2}_{3}_{4}", any other "".</summary>
    public static string FileName(string key)
    {
        var parts = (key ?? "").Split('/');
        return parts.Length == 5 ? parts[1] + parts[2] + "_" + parts[3] + "_" + parts[4] : "";
    }

    /// <summary>GetProfileImagePath (@0x141028a90): an empty key with <paramref name="useDefault"/> gives the default image,
    /// any other the download folder's file.</summary>
    public string ProfileImagePath(string key, bool useDefault)
    {
        if (string.IsNullOrEmpty(key) && useDefault) return NoProfile;
        return Folder + "/" + FileName(key);
    }

    /// <summary>GetCacheKey (@0x141027200) outside CN: "{host}/{key}".</summary>
    public string DownloadUrl(string key) => Host + "/" + key;

    /// <summary>The key's file name if <paramref name="path"/> is one of <see cref="ProfileImagePath"/>'s, else null.</summary>
    public static string FileOfPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        var p = path.Replace('\\', '/');
        const string folder = "custom/download/profile/";
        int at = p.IndexOf(folder, StringComparison.OrdinalIgnoreCase);
        return at < 0 ? null : p[(at + folder.Length)..];
    }

    /// <summary>
    /// FUN_141028be0 (@0x141028be0): the picture to show for the character. No key: the default image. A file not in the
    /// download folder yet starts its download and gives the default image when <paramref name="noDownload"/> (the
    /// windows' case), else the path it will have.
    /// </summary>
    public string Path(long characterId, string key, bool noDownload)
    {
        if (string.IsNullOrEmpty(key)) return NoProfile;
        if (Exists(FileName(key))) return ProfileImagePath(key, false);
        Download(characterId, key);
        return noDownload ? NoProfile : ProfileImagePath(key, false);
    }

    /// <summary>
    /// FUN_141884e30 -> DownloadProfileImage (@0x141884d70): a type 5 download of the key for the character. A key already
    /// being fetched only gets the character added (CUgcDownloadProfile::AddProxy), so it is fetched once.
    /// </summary>
    public void Download(long characterId, string key)
    {
        if (string.IsNullOrEmpty(key) || FileName(key).Length == 0) return;
        if (pending.TryGetValue(key, out var ids)) { if (!ids.Contains(characterId)) ids.Add(characterId); return; }
        pending[key] = new List<long> { characterId };
        DownloadRequested?.Invoke(key, DownloadUrl(key), FileName(key));
    }

    /// <summary>CUgcDownloadProfile::ProcessResult (@0x1418714a0): on success every character that asked for the key gets
    /// <see cref="Ready"/>. A failed download raises nothing; the key can be asked for again.</summary>
    public void Downloaded(string key, bool ok)
    {
        if (key == null || !pending.Remove(key, out var ids) || !ok) return;
        foreach (var id in ids) { Arrived?.Invoke(id, key); Ready?.Invoke(id, key); }
    }

    /// <summary>The field's profile picture packet (OnCharacterProfileImage): the windows refresh at once.</summary>
    public void Changed(long characterId, string key) => Ready?.Invoke(characterId, key);

    // ---- upload: CSoapClient::UploadUgcFile (@0x141771040) -------------------------------------------------------------

    /// <summary>The profile picture's upload kind (CUgcViewerProfile, OpenProfileUpload @0x14187fb30).</summary>
    public const int ProfileKind = 5;

    /// <summary>The upload's size range: 1 byte to under 1 MiB.</summary>
    public const int MaxUploadSize = 1 << 20;

    /// <summary>Where the upload is posted: m_strPath + "urq.aspx"; null without a base (SOAP mode 1).</summary>
    public string UploadUrl => string.IsNullOrEmpty(UploadBase) ? null : UploadBase + "urq.aspx";

    /// <summary>
    /// The body UploadUgcFile posts, little-endian: i32 the user's type (DAT_143ab2830, the picked server's id), i32 kind,
    /// i64 account id, i64 character id, i64 the resource uid (0 for a profile picture), i64 the upload's value
    /// (FUN_1418a41a0; the character id for a profile picture), i64 the file size, then the file.
    /// </summary>
    public static byte[] UploadBody(int userType, int kind, long accountId, long characterId, long uid, long value, byte[] file)
    {
        file ??= Array.Empty<byte>();
        using var ms = new MemoryStream(44 + file.Length);
        using var w = new BinaryWriter(ms);
        w.Write(userType); w.Write(kind); w.Write(accountId); w.Write(characterId); w.Write(uid); w.Write(value);
        w.Write((long)file.Length); w.Write(file);
        w.Flush();
        return ms.ToArray();
    }

    /// <summary>
    /// The answer UploadUgcFile reads: "code,text" split on ","; a body of another number of fields is code 6 (and is tried
    /// again), code 0 is success with the stored file's key as the text.
    /// </summary>
    public static (int Code, string Text) ParseUploadReply(string body)
    {
        var fields = (body ?? "").Split(',');
        if (fields.Length != 2) return (6, "");
        return (int.TryParse(fields[0].Trim(), out int c) ? c : 0, fields[1]);
    }

    /// <summary>The answer as bytes (FUN_140188f50 reads it as a narrow string).</summary>
    public static (int Code, string Text) ParseUploadReply(byte[] body) => ParseUploadReply(body == null ? "" : Encoding.UTF8.GetString(body));

    // ---- the new character's picture: CUgcManager::FUN_141880600 (@0x141880600) -----------------------------------------

    /// <summary>The thumbnail's side (CUgcImageConverter::ConvertThumbnail: 0x6b).</summary>
    public const int ThumbnailSize = 107;

    /// <summary>
    /// The screen rect of the shot of a just-made character, from its Eff_UI point (<paramref name="topX"/>,
    /// <paramref name="topY"/>) and its Eff_Body point's height <paramref name="bottomY"/> in screen pixels (y down): a
    /// fifth of the gap off the top, a tenth off the bottom, and a square centred on Eff_UI's x as wide as the rest is tall.
    /// The fields are unsigned as in the client's NiRect&lt;unsigned int&gt;; right and bottom are exclusive (the D3DX source rect).
    /// </summary>
    public static (uint Left, uint Top, uint Right, uint Bottom) ShotRect(float topX, float topY, float bottomY)
    {
        int n = (int)(bottomY - topY);
        int margin = (int)(n * 0.2f);
        uint top = (uint)(topY + margin);
        uint bottom = (uint)(bottomY - margin * 0.5f);
        n = (int)(bottom - top);
        n = (int)(n * 0.5);
        return ((uint)(topX - n), top, (uint)(topX + n), bottom);
    }
}
