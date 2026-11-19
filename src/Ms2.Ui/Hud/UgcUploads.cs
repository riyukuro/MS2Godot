namespace Ms2.Ui.Hud;

/// <summary>A directory entry as FindFirstFileW / FindNextFileW give it: the name, the write and create times (any
/// increasing tick unit), and whether it is a directory.</summary>
public readonly record struct UgcFileEntry(string Name, long WriteTime, long CreateTime, bool IsDirectory);

/// <summary>
/// The file system and shell calls of the UGC uploads, in the client's own path forms (".\Custom\Image\a.png",
/// ".\Custom\Thumbnail/a.png", "./Custom/Thumbnail/a.png"): the host maps the client's Custom folder to its own.
/// </summary>
public interface IUgcFiles
{
    /// <summary>The entries of a folder (FindFirstFileW on folder\*.*), "." and ".." left out; none for a missing folder.</summary>
    IEnumerable<UgcFileEntry> List(string folder);
    /// <summary>_waccess(path, 0).</summary>
    bool Exists(string path);
    /// <summary>FindFirstFileW's last write time; null for a missing file.</summary>
    long? WriteTime(string path);
    /// <summary>GetImageFileInfo (D3DXGetImageInfoFromFile): the image's size, null when it cannot be read.</summary>
    (int Width, int Height)? ImageSize(string path);
    /// <summary>CUgcImageConverter::ConvertThumbnail (ugcmanager_1.cpp:510): <paramref name="src"/> loaded, any old
    /// <paramref name="dst"/> deleted, scaled to size x size A8R8G8B8 and saved as a PNG; 0, or the converter's error.</summary>
    int MakeThumbnail(string src, string dst, int size);
    /// <summary>DeleteThumbnails: every file in the folder deleted.</summary>
    void DeleteFiles(string folder);
    /// <summary>CopyFileW(src, dst, bFailIfExists 1).</summary>
    bool Copy(string src, string dst);
    /// <summary>_wfopen(path, "rb") and the whole file; null when it cannot be opened.</summary>
    byte[] Read(string path);
    /// <summary>DeleteFileW.</summary>
    void Delete(string path);
    /// <summary>ShellExecute of explorer on the folder.</summary>
    void OpenFolder(string folder);
    /// <summary>ShellExecute of mspaint on the file.</summary>
    void OpenInEditor(string path);
    /// <summary>FUN_141b6e360, the Open File dialog in <paramref name="dir"/> with <paramref name="filter"/>: the picked path,
    /// or "" when cancelled. The client's dialog is modal; the host's answers later.</summary>
    void PickImage(string dir, string filter, Action<string> picked);
}

/// <summary>
/// CUploadFile (ugcmanager_*.cpp): one image file of the upload folder. <see cref="Set"/> splits the path into the title
/// (the name without its extension) and the extension.
/// </summary>
public sealed class UgcUploadFile
{
    public string Path { get; private set; } = "";
    public string Name { get; private set; } = "";
    public string Title { get; private set; } = "";
    public string Ext { get; private set; } = "";
    public long WriteTime { get; private set; }
    public long CreateTime { get; private set; }

    /// <summary>CUploadFile::Set(path, name, write, create, 1): _wsplitpath of the path gives the title and extension.</summary>
    public void Set(string path, string name, long write, long create)
    {
        Path = path; Name = name; WriteTime = write; CreateTime = create;
        string file = path[(Math.Max(path.LastIndexOf('\\'), path.LastIndexOf('/')) + 1)..];
        int dot = file.LastIndexOf('.');
        Title = dot >= 0 ? file[..dot] : file;
        Ext = dot >= 0 ? file[dot..] : "";
    }

    /// <summary>GetThumbnailDir: the thumbnail's file system path.</summary>
    public string ThumbnailDir => UgcManager.ThumbnailFolder + "/" + Title + ".png";

    /// <summary>GetThumbnailFilePath: the thumbnail's url for the movies.</summary>
    public string ThumbnailPath => "./Custom/Thumbnail/" + Title + ".png";

    /// <summary>Refresh: FindFirstFileW on the path; true (the new write time kept) when the file changed since.</summary>
    public bool Refresh(IUgcFiles files)
    {
        if (files.WriteTime(Path) is not long t || t == WriteTime) return false;
        WriteTime = t;
        return true;
    }
}

/// <summary>m_user of CUgcUploadCtrl: the uploading user (DAT_143ab2830 the picked server's id, the account and the
/// character).</summary>
public readonly record struct UgcUser(int Type, long AccountId, long CharacterId);

/// <summary>
/// CUgcViewer (ugcviewerequip.cpp): what an upload kind previews its picked file on and restores when the upload ends.
/// </summary>
public interface IUgcViewer
{
    /// <summary>vfunc_2: whether the viewer has content of its own (HasViewerContent); the base's 0.</summary>
    bool HasContent { get; }
    /// <summary>vfunc_3(file): show the file; false when it could not.</summary>
    bool Preview(UgcUploadFile file);
    /// <summary>vfunc_4: put back what was shown before the preview.</summary>
    void Restore();
    /// <summary>vfunc_5: the per-frame refresh mark.</summary>
    void MarkRefreshed();
    /// <summary>vfunc_10: the value UploadUgcFile posts after the uid (FUN_1418a41a0).</summary>
    long Value { get; }
}

/// <summary>
/// CUgcViewerProfile (ugcviewerequip.cpp:705-790): the profile picture shows on the character window.
/// <list type="bullet">
/// <item>vfunc_3 needs the current upload, a local player, the file on disk and a thumbnail path; the upload's file
/// becomes it, and with the character window (0x2711) up ShowProfileImage(thumbnail path).</item>
/// <item>vfunc_4: the window's RefreshProfileImage; vfunc_5: m_bRefreshed; vfunc_9: 0x6b; vfunc_10: the character id.</item>
/// </list>
/// </summary>
public sealed class ProfileUploadViewer : IUgcViewer
{
    readonly UgcManager manager;
    public ProfileUploadViewer(UgcManager manager, UgcUser user) { this.manager = manager; User = user; }

    public UgcUser User { get; }
    /// <summary>m_bRefreshed.</summary>
    public bool Refreshed { get; private set; }
    public bool HasContent => false;
    public long Value => User.CharacterId;
    /// <summary>vfunc_9: 0x6b.</summary>
    public int Id => 0x6b;

    public bool Preview(UgcUploadFile file)
    {
        var ctrl = manager.Current;
        if (ctrl == null || !manager.HasLocalPlayer() || file == null || !manager.Files.Exists(file.Path)) return false;
        if (file.ThumbnailPath.Length == 0) return false;
        ctrl.File = file;
        if (manager.MyInfo?.Invoke() is not { } info) return false;
        info.ShowProfileImage(file.ThumbnailPath);
        return true;
    }

    public void Restore() => manager.MyInfo?.Invoke()?.RefreshProfileImage();
    public void MarkRefreshed() => Refreshed = true;
}

/// <summary>
/// CUgcUploadCtrl: one upload, its kind, dialog, picked file and viewer, and m_upload (the type, uid, name, the
/// answer's path) with m_strResult, the file PrepareUpload chose to post.
/// </summary>
public sealed class UgcUploadCtrl
{
    public int Kind { get; set; }
    public int Dialog { get; set; }
    public UgcUploadFile File { get; set; }
    public IUgcViewer Viewer { get; set; }
    public UgcUser User { get; set; }
    /// <summary>m_upload.m_nType: 1 while a prepared upload is out.</summary>
    public int UploadType { get; set; }
    public long Uid { get; set; }
    public string Name { get; set; } = "";
    /// <summary>m_upload.m_strPath: the web host's answer text.</summary>
    public string Path { get; set; } = "";
    /// <summary>m_strResult: the file to post.</summary>
    public string Result { get; set; } = "";

    /// <summary>
    /// PrepareUpload(0). Kind 5 is FUN_1418a43d0 (ugcviewerequip.cpp:1579): no file gives 0xb, else the file's
    /// thumbnail is the one posted. Only the profile upload opens here (OpenProfileUpload), so no other kind's
    /// prepare runs; STILL-OPEN: theirs.
    /// </summary>
    public int PrepareUpload()
    {
        if (Kind != UgcManager.ProfileKind) return 0xd;
        if (File == null) return 0xb;
        Result = File.ThumbnailDir;
        return 0;
    }
}

/// <summary>
/// CUgcManager's uploads (ugcmanager_4.cpp): the current upload, its folder's image files (m_files), the preview, the
/// upload through CSoapClient::UploadPendingUgc (soapclient.cpp:99) and its answer (FUN_141882d80).
/// The client's upload is synchronous; here <see cref="Post"/> answers later and the rest runs then.
/// </summary>
public sealed class UgcManager
{
    public const int ProfileKind = 5;
    public const int UploadDialogId = 0x4e56;
    /// <summary>FUN_141026920(3 / 5): the image folder of the banner and profile kinds.</summary>
    public const string ImageFolder = @".\Custom\Image";
    public const string ThumbnailFolder = @".\Custom\Thumbnail";
    /// <summary>ConvertThumbnail's size.</summary>
    public const int ThumbnailSize = 107;
    /// <summary>The extensions the scan takes (m_extensions).</summary>
    static readonly string[] Extensions = { ".dds", ".png", ".jpg" };
    /// <summary>AddImage's open-file filter.</summary>
    public const string ImageFilter = "Image Files (*.dds, *.png, *.jpg)";

    public UgcManager(IUgcFiles files, GameTables tables) { Files = files; Tables = tables; }

    public IUgcFiles Files { get; }
    GameTables Tables { get; }

    /// <summary>GetMode (Ugc mode 0x16).</summary>
    public Func<int> Mode = () => 0;
    /// <summary>GetCurrentUpload.</summary>
    public UgcUploadCtrl Current { get; private set; }
    /// <summary>The upload lock (LockForUpload / UnlockUpload).</summary>
    public bool Locked { get; private set; }

    /// <summary>A post is out (<see cref="Post"/> has not answered yet).</summary>
    bool posting;
    /// <summary>m_bInit: set by SetInitialized (@0x14187f1f0) during a frame's preview refresh, closing the dialog.</summary>
    bool initialized;

    readonly List<UgcUploadFile> files = new();

    // ---- the host ---------------------------------------------------------------------------------------------------

    /// <summary>FUN_141275260(id, 1, 1): a system notice of common string id.</summary>
    public Action<int> CommonNotice;
    /// <summary>ShowSystemNotice(text).</summary>
    public Action<string> Notice;
    /// <summary>A box of common string id (FUN_140d73bc0), with the listener and tag; null listener = none.</summary>
    public Action<int, IMessageBoxListener, string> Box;
    /// <summary>A box of a text (CommonByKey / FormatStringId).</summary>
    public Action<string> TextBox;
    /// <summary>CBlockManager::FindBlockedAction(3) (FUN_140a30ad0 shows its notice). STILL-OPEN: no block list.</summary>
    public Func<bool> UploadBlocked = () => false;
    /// <summary>FindDialog(id): open and not closed.</summary>
    public Func<int, bool> DialogOpen = _ => false;
    public Action<int> CloseDialog;
    public Action<int> ToggleDialog;
    /// <summary>The open upload dialog (FindDialog(0x4e56) as UIUGCImageUploadDialog).</summary>
    public Func<UgcImageUploadController> UploadDialog = () => null;
    /// <summary>FindMyInfoDialog(0x2711), null when it is not up.</summary>
    public Func<MyInfoController> MyInfo;
    public Func<bool> HasLocalPlayer = () => false;
    /// <summary>The scene is the field (0x620000c).</summary>
    public Func<bool> InField = () => false;
    /// <summary>DAT_143ab2830 / DAT_143ab2820 / DAT_143ab2828.</summary>
    public Func<UgcUser> User = () => default;
    /// <summary>
    /// CSoapClient::UploadUgcFile in SOAP mode 2: the body posted to urq.aspx; the answer's text (the code is not read by
    /// UploadPendingUgc). False when the client is not in SOAP mode 2 (UploadPendingUgc's "NoUploadWithSoap", 1).
    /// </summary>
    public Func<byte[], Action<string>, bool> Post = (_, _) => false;
    /// <summary>SendUGCMode11(key): the profile picture's key to the game server.</summary>
    public Action<string> SendProfileKey;
    /// <summary>SendUGCMode0: the upload failed.</summary>
    public Action SendUploadFailed;
    /// <summary>UICharacterListDialog(0x4ef1).FUN_1418ed4e0(character id, key): the lobby's upload.</summary>
    public Action<long, string> LobbyProfile;

    // ---- opening ----------------------------------------------------------------------------------------------------

    /// <summary>
    /// OpenProfileUpload (@0x14187fb30, ugcmanager_4.cpp:734): mode 1 gives notice 0x2e7; a blocked upload stops;
    /// OpenExclusiveUploadDialog(0x4e56) (@0x14188fdb0) stops when the dialog is up, else closes 0x4e69 and 0x4e70.
    /// Then the upload: a profile viewer for the user, kind 5, the dialog 0x4e56, toggled open.
    /// </summary>
    public void OpenProfileUpload()
    {
        if (Mode() == 1) { CommonNotice?.Invoke(0x2e7); return; }
        if (UploadBlocked()) return;
        if (DialogOpen(UploadDialogId)) return;
        CloseDialog?.Invoke(0x4e69);
        CloseDialog?.Invoke(0x4e70);
        var user = User();
        var ctrl = new UgcUploadCtrl { Kind = ProfileKind, User = user, Dialog = UploadDialogId };
        ctrl.Viewer = new ProfileUploadViewer(this, user);
        Current = ctrl;
        ToggleDialog?.Invoke(UploadDialogId);
    }

    // ---- the folder -------------------------------------------------------------------------------------------------

    /// <summary>FUN_141026920(kind): the kind's image folder (3 and 5: Custom\Image).</summary>
    public static string FolderOf(int kind) => kind switch
    {
        1 => @".\Custom\Equip", 2 => @".\Custom\Cube", 6 => @".\Custom\Guild", 8 => @".\Custom\Guild\Poster",
        _ => ImageFolder,
    };

    public int FileCount => files.Count;

    /// <summary>GetUploadFile(i): null out of range.</summary>
    public UgcUploadFile GetFile(int i) => i >= 0 && i < files.Count ? files[i] : null;

    /// <summary>
    /// RefreshLocalFiles(kind) @0x141889ce0: the folder scanned; at UGCImgFileCountCheck files or more the box 0x2e3; a
    /// list that is not the cached one (FUN_14188c020: the count, and each index's name and write time) deletes every
    /// thumbnail and replaces it.
    /// </summary>
    public void RefreshLocalFiles(int kind)
    {
        var scan = Scan(FolderOf(kind));
        if (scan.Count >= Tables.UGCImgFileCountCheck) Box?.Invoke(0x2e3, null, "");
        if (SameFiles(scan)) return;
        Files.DeleteFiles(ThumbnailFolder);
        files.Clear();
        files.AddRange(scan);
    }

    bool SameFiles(List<UgcUploadFile> scan)
    {
        if (scan.Count != files.Count) return false;
        for (int i = 0; i < scan.Count; i++)
            if (scan[i].Name != files[i].Name || scan[i].WriteTime != files[i].WriteTime) return false;
        return true;
    }

    /// <summary>
    /// FUN_14188d380: the folder's files (no directories) whose lowercased extension is .dds, .png or .jpg and whose image
    /// reads at no more than UGCImgUploadSizeLimit each way, sorted by FUN_141890240: the newest created first, equal
    /// times by name (ordinal).
    /// </summary>
    List<UgcUploadFile> Scan(string folder)
    {
        int limit = Tables.UGCImgUploadSizeLimit;
        var list = new List<UgcUploadFile>();
        foreach (var e in Files.List(folder))
        {
            if (e.IsDirectory || e.Name == "." || e.Name == "..") continue;
            int dot = e.Name.LastIndexOf('.');
            string ext = dot >= 0 ? e.Name[dot..].ToLowerInvariant() : "";
            if (Array.IndexOf(Extensions, ext) < 0) continue;
            string path = folder + "\\" + e.Name;
            if (Files.ImageSize(path) is not { } size || size.Width > limit || size.Height > limit) continue;
            var f = new UgcUploadFile();
            f.Set(path, e.Name, e.WriteTime, e.CreateTime);
            list.Add(f);
        }
        list.Sort((a, b) => a.CreateTime != b.CreateTime ? b.CreateTime.CompareTo(a.CreateTime) : string.CompareOrdinal(a.Name, b.Name));
        return list;
    }

    /// <summary>
    /// FUN_141889e20(count, start): the rows of the files from <paramref name="start"/>, each thumbnail made when it is not
    /// on disk: fileName, tooltip (FormatStringId(0x2ec, name)) and imgPath (the thumbnail's url).
    /// </summary>
    public List<Dictionary<string, object>> Rows(int count, int start)
    {
        var rows = new List<Dictionary<string, object>>();
        for (int i = start; i < start + count && i < files.Count; i++)
        {
            if (i < 0) continue;
            var f = files[i];
            if (!Files.Exists(f.ThumbnailDir)) ConvertIconFile(f.Path);
            rows.Add(new Dictionary<string, object>
            {
                ["fileName"] = f.Name,
                ["tooltip"] = Ms2.Quest.ClientFormat.Format(MessageBoxData.Common(0x2ec), f.Name),
                ["imgPath"] = f.ThumbnailPath,
            });
        }
        return rows;
    }

    /// <summary>ConvertIconFile (ugcmanager_1.cpp:888): 0x17 for an empty path, 0x18 for a missing file, else
    /// ConvertThumbnail's result, into FUN_14102a5d0's Custom\Thumbnail\&lt;title&gt;.png (107 x 107).</summary>
    public int ConvertIconFile(string path)
    {
        if (string.IsNullOrEmpty(path)) return 0x17;
        if (!Files.Exists(path)) return 0x18;
        var f = new UgcUploadFile();
        f.Set(path, "", 0, 0);
        return Files.MakeThumbnail(path, ThumbnailFolder + "\\" + f.Title + ".png", ThumbnailSize);
    }

    // ---- the dialog's calls -----------------------------------------------------------------------------------------

    /// <summary>DoesFileExistByIndex: _waccess on the file's path.</summary>
    public bool FileExists(int i) => GetFile(i) is { } f && Files.Exists(f.Path);

    /// <summary>IsCurrentUploadFile(i): the file is on disk and is the upload's file.</summary>
    public bool IsCurrentUploadFile(int i) => GetFile(i) is { } f && Files.Exists(f.Path) && Current?.File == f;

    /// <summary>HasViewerContent (:985): the viewer's vfunc_2.</summary>
    public bool HasViewerContent() => Current?.Viewer?.HasContent ?? false;

    /// <summary>FUN_141881800(kind, i) -> FUN_141881650: the file, the upload and its kind must match; the viewer's
    /// preview of the file.</summary>
    public bool Preview(int kind, int i)
    {
        var f = GetFile(i);
        var ctrl = Current;
        if (f == null || ctrl == null || ctrl.Kind != kind || ctrl.Viewer == null) return false;
        return ctrl.Viewer.Preview(f);
    }

    /// <summary>OpenUploadFolder: explorer on the kind's folder.</summary>
    public void OpenUploadFolder(int kind) => Files.OpenFolder(FolderOf(kind));

    /// <summary>OpenFileInPaint(i, 1): not .png or .jpg gives notice 0x2de, else mspaint on the file.</summary>
    public void OpenFileInPaint(int i)
    {
        if (GetFile(i) is not { } f) return;
        string ext = f.Ext.ToLowerInvariant();
        if (ext != ".png" && ext != ".jpg") { CommonNotice?.Invoke(0x2de); return; }
        Files.OpenInEditor(f.Path);
    }

    /// <summary>
    /// AddImage (FUN_1418810c0, :1002): kind 0 gives the "select a design" box; the open-file dialog's pick, read as an
    /// image, at no more than UGCImgUploadSizeLimit each way (else box 0x2ea with the limit), copied into the kind's
    /// folder unless a file of the name is there (box 0x2e9). <paramref name="done"/> gets true when it was added.
    /// </summary>
    public void AddImage(string dir, string filter, Action<bool> done)
    {
        var ctrl = Current;
        if (ctrl == null) { done(false); return; }
        if (ctrl.Kind == 0) { TextBox?.Invoke(MessageBoxData.CommonByKey?.Invoke("s_msg_ugc_fail_select_design") ?? ""); done(false); return; }
        int kind = ctrl.Kind;
        Files.PickImage(dir, filter, src =>
        {
            if (string.IsNullOrEmpty(src) || !Files.Exists(src)) { done(false); return; }
            if (Files.ImageSize(src) is not { } size) { done(false); return; }
            int limit = Tables.UGCImgUploadSizeLimit;
            if (size.Width > limit || size.Height > limit)
            {
                TextBox?.Invoke(Ms2.Quest.ClientFormat.Format(MessageBoxData.Common(0x2ea), limit, limit));
                done(false); return;
            }
            var f = new UgcUploadFile();
            f.Set(src, "", 0, 0);
            if (!Files.Copy(src, FolderOf(kind) + "\\" + f.Title + f.Ext)) { Box?.Invoke(0x2e9, null, ""); done(false); return; }
            done(true);
        });
    }

    // ---- the upload -------------------------------------------------------------------------------------------------

    /// <summary>
    /// StartUpload (FUN_141882020, :1162): no upload 0xd, no file 0xb, no local player 0xc, locked already 4. The profile
    /// kind is not one FUN_14088db30 sends at once (1, 2, 3, 6, 7, 8, 9), so: uid 0, name "", type 1; a blocked upload
    /// 0x7a; PrepareUpload's error; then UploadPendingUgc. A nonzero result goes to ShowUploadResult now; the post's own
    /// answers go to <see cref="OnUploaded"/>. (The client's ShowUploadResult(0) after its synchronous upload finds the
    /// upload already cleared.)
    /// </summary>
    public int StartUpload()
    {
        // the client's post is synchronous, so nothing can start while it is out; here an upload agreed again before the
        // answer would take ShowUploadResult(4)'s unlock and drop the answer: it is refused with no result shown
        if (posting) return 4;
        var ctrl = Current;
        int r;
        if (ctrl == null) r = 0xd;
        else if (ctrl.File == null) r = 0xb;
        else if (!HasLocalPlayer()) r = 0xc;
        else if (Locked) r = 4;
        else
        {
            Locked = true;
            ctrl.Uid = 0; ctrl.Name = ""; ctrl.UploadType = 1;
            r = UploadBlocked() ? 0x7a : ctrl.PrepareUpload();
            if (r == 0) UploadPending();
        }
        if (r != 0) ShowUploadResult(r);
        return r;
    }

    /// <summary>
    /// UploadPendingUgc (soapclient.cpp:99): no upload 0xd; no file to post 0xf; a file that does not open is
    /// Exception("file.Open"), whose code is 0; empty or 1 MB and more 0xd2; outside SOAP mode 2 1. The post's answer text
    /// is the result and the posted file is deleted; the error is 0 whatever code the answer had. Then FUN_141882d80.
    /// </summary>
    void UploadPending()
    {
        var ctrl = Current;
        if (ctrl == null) { OnUploaded(null, 0xd, ""); return; }
        string path = ctrl.Result;
        if (path.Length == 0) { OnUploaded(ctrl, 0xf, ""); return; }
        var data = Files.Read(path);
        if (data == null) { OnUploaded(ctrl, 0, ""); return; }
        if (data.Length == 0 || data.Length >= 0x100000) { OnUploaded(ctrl, 0xd2, ""); return; }
        var u = ctrl.User;
        var body = ProfileImages.UploadBody(u.Type, ctrl.Kind, u.AccountId, u.CharacterId, ctrl.Uid, ctrl.Viewer?.Value ?? 0, data);
        posting = true;
        bool sent = Post(body, text =>
        {
            posting = false;
            Files.Delete(path);
            OnUploaded(ctrl, 0, text ?? "");
        });
        if (!sent) { posting = false; OnUploaded(ctrl, 1, ""); }
    }

    /// <summary>
    /// FUN_141882d80 (:1387): only for the current upload with a viewer. The answer text is m_upload's path;
    /// ShowUploadResult. A finished profile upload sends the key in the field (SendUGCMode11) or hands it to the lobby's
    /// character list, then unlocks and clears; a failed one sends SendUGCMode0.
    /// </summary>
    public void OnUploaded(UgcUploadCtrl ctrl, int n, string text)
    {
        if (ctrl == null || ctrl != Current || ctrl.Viewer == null) return;
        ctrl.Path = text;
        ShowUploadResult(n);
        if (n != 0) { SendUploadFailed?.Invoke(); return; }
        if (ctrl.Kind != ProfileKind) return;   // the other kinds' answers (SendUGCMode3 / 12, the downloads): not opened here
        if (InField() && HasLocalPlayer()) SendProfileKey?.Invoke(text);
        else LobbyProfile?.Invoke(ctrl.User.CharacterId, text);
        Unlock();
    }

    /// <summary>
    /// ShowUploadResult @0x1418826a0: the open upload dialog's OnUploadResult(n); a nonzero result restores the viewer,
    /// unlocks and clears. STILL-OPEN: 0x7a's block notice.
    /// </summary>
    public void ShowUploadResult(int n)
    {
        if (Current == null) return;
        if (DialogOpen(UploadDialogId)) UploadDialog()?.OnUploadResult(n);
        if (n != 0 && Current != null)
        {
            Current.Viewer?.Restore();
            Unlock();
        }
    }

    /// <summary>UnlockUpload and ClearCurrentUpload.</summary>
    void Unlock() { Locked = false; Current = null; }

    /// <summary>
    /// OnUploadResult(dialog) @0x141880ec0 (:960), as the upload dialog closes: FUN_1418a3af0 restores the viewer when the
    /// upload is of that dialog and not of type 3; with no prepared upload out (type 0) it unlocks and clears.
    /// </summary>
    public void OnUploadResult(int dialog)
    {
        var ctrl = Current;
        if (ctrl == null) return;
        if (ctrl.Dialog == dialog && ctrl.Viewer != null && ctrl.UploadType != 3) ctrl.Viewer.Restore();
        if (ctrl.UploadType == 0) Unlock();
    }

    /// <summary>GetUploadResultMessage (:594): "" for 0 and 0x7a, else BlockFormat(0x461, n).</summary>
    public static string ResultMessage(int n) =>
        n == 0 || n == 0x7a ? "" : Ms2.Quest.ClientFormat.Format(MessageBoxData.Common(0x461), n);

    /// <summary>SetInitialized (@0x14187f1f0).</summary>
    public void SetInitialized() => initialized = true;

    /// <summary>
    /// Update (@0x14187f1c0), each frame: FUN_14188bbb0. m_bInit cleared, the upload's RefreshPreview (@0x1418a38a0): with
    /// its dialog up, the viewer's refresh mark, and a picked file whose write time moved gets a new thumbnail, a new
    /// preview and the dialog's thumbnail update (UIUGCEventDispatcher vfunc_4 -> m_18c). Set m_bInit closes the dialog.
    /// The downloads (FUN_14188bce0) run in <see cref="ProfileImages"/>.
    /// </summary>
    public void Update()
    {
        initialized = false;
        var ctrl = Current;
        if (ctrl == null || !DialogOpen(ctrl.Dialog)) return;
        if (ctrl.Viewer != null)
        {
            ctrl.Viewer.MarkRefreshed();
            if (ctrl.File != null && ctrl.File.Refresh(Files))
            {
                ConvertIconFile(ctrl.File.Path);
                ctrl.Viewer.Preview(ctrl.File);
                UploadDialog()?.ThumbnailChanged(ctrl.Dialog);
            }
        }
        if (initialized && DialogOpen(ctrl.Dialog)) CloseDialog?.Invoke(ctrl.Dialog);
    }
}
