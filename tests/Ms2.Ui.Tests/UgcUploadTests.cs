using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The profile picture's upload (CUgcManager, ugcmanager_4.cpp): the folder's scan, the rows, the upload and its
/// answer, the terms box's tag, and the image headers (UgcImageInfo).</summary>
public class UgcUploadTests
{
    sealed class FakeFiles : IUgcFiles
    {
        public readonly List<UgcFileEntry> Entries = new();
        public readonly Dictionary<string, (int, int)?> Sizes = new();
        public readonly HashSet<string> OnDisk = new();
        public readonly Dictionary<string, byte[]> Data = new();
        public readonly List<string> Thumbnails = new(), Deleted = new(), Cleared = new(), Edited = new();

        public IEnumerable<UgcFileEntry> List(string folder) => Entries;
        // ".\Custom\Thumbnail\a.png" and ".\Custom\Thumbnail/a.png" are one file
        static string N(string p) => p.Replace('/', '\\');
        public bool Exists(string path) => OnDisk.Contains(N(path));
        public long? WriteTime(string path) => null;
        public (int Width, int Height)? ImageSize(string path) => Sizes.TryGetValue(path, out var s) ? s : null;
        public int MakeThumbnail(string src, string dst, int size) { Thumbnails.Add($"{src}>{dst}@{size}"); OnDisk.Add(N(dst)); return 0; }
        public void DeleteFiles(string folder) => Cleared.Add(folder);
        public bool Copy(string src, string dst) => false;
        public byte[] Read(string path) => Data.TryGetValue(N(path), out var d) ? d : null;
        public void Delete(string path) => Deleted.Add(path);
        public void OpenFolder(string folder) { }
        public void OpenInEditor(string path) => Edited.Add(path);
        public void PickImage(string dir, string filter, Action<string> picked) => picked("");

        public void Add(string name, long created, (int, int)? size, bool dir = false)
        {
            Entries.Add(new UgcFileEntry(name, created, created, dir));
            Sizes[UgcManager.ImageFolder + "\\" + name] = size;
            OnDisk.Add(UgcManager.ImageFolder + "\\" + name);
        }
    }

    static GameTables Tables() => (GameTables)Activator.CreateInstance(typeof(GameTables), nonPublic: true)!;

    static (UgcManager, FakeFiles) Manager()
    {
        var files = new FakeFiles();
        var m = new UgcManager(files, Tables()) { HasLocalPlayer = () => true, InField = () => true, User = () => new UgcUser(1, 100, 200) };
        return (m, files);
    }

    [Test]
    public void ScanKeepsReadableImagesNewestFirst()
    {
        var (m, files) = Manager();
        files.Add("b.png", 10, (64, 64));
        files.Add("a.PNG", 10, (64, 64));        // the extension lowercased; equal times by name
        files.Add("new.jpg", 30, (64, 64));
        files.Add("c.dds", 20, (64, 64));
        files.Add("big.png", 40, (1025, 10));    // over UGCImgUploadSizeLimit
        files.Add("bad.png", 50, null);           // no image
        files.Add("d.bmp", 60, (64, 64));         // not .dds / .png / .jpg
        files.Add("sub.png", 70, (64, 64), dir: true);
        m.RefreshLocalFiles(UgcManager.ProfileKind);
        Assert.That(Enumerable.Range(0, m.FileCount).Select(i => m.GetFile(i).Name), Is.EqualTo(new[] { "new.jpg", "c.dds", "a.PNG", "b.png" }));
        Assert.That(files.Cleared, Is.EqualTo(new[] { UgcManager.ThumbnailFolder }));

        m.RefreshLocalFiles(UgcManager.ProfileKind);    // the same list: the thumbnails stay
        Assert.That(files.Cleared, Has.Count.EqualTo(1));
    }

    [Test]
    public void RowsMakeMissingThumbnails()
    {
        var (m, files) = Manager();
        files.Add("a.png", 2, (64, 64));
        files.Add("b.png", 1, (64, 64));
        m.RefreshLocalFiles(UgcManager.ProfileKind);
        var rows = m.Rows(16, 0);
        Assert.That(rows.Select(r => r["fileName"]), Is.EqualTo(new[] { "a.png", "b.png" }));
        Assert.That(rows[0]["imgPath"], Is.EqualTo("./Custom/Thumbnail/a.png"));
        Assert.That(files.Thumbnails, Is.EqualTo(new[]
        {
            UgcManager.ImageFolder + "\\a.png>" + UgcManager.ThumbnailFolder + "\\a.png@107",
            UgcManager.ImageFolder + "\\b.png>" + UgcManager.ThumbnailFolder + "\\b.png@107",
        }));
        m.Rows(16, 0);      // on disk now: not made again
        Assert.That(files.Thumbnails, Has.Count.EqualTo(2));
        Assert.That(m.Rows(16, 1).Select(r => r["fileName"]), Is.EqualTo(new[] { "b.png" }));
    }

    [Test]
    public void ModeOneRefusesTheUpload()
    {
        var (m, _) = Manager();
        var notices = new List<int>();
        m.Mode = () => 1;
        m.CommonNotice = notices.Add;
        m.OpenProfileUpload();
        Assert.That(notices, Is.EqualTo(new[] { 0x2e7 }));
        Assert.That(m.Current, Is.Null);
    }

    [Test]
    public void OpenProfileUploadTogglesTheDialog()
    {
        var (m, _) = Manager();
        var toggled = new List<int>();
        var closed = new List<int>();
        m.ToggleDialog = toggled.Add;
        m.CloseDialog = closed.Add;
        m.OpenProfileUpload();
        Assert.That(toggled, Is.EqualTo(new[] { 0x4e56 }));
        Assert.That(closed, Is.EqualTo(new[] { 0x4e69, 0x4e70 }));
        Assert.That(m.Current.Kind, Is.EqualTo(5));
        Assert.That(m.Current.Viewer, Is.InstanceOf<ProfileUploadViewer>());
        Assert.That(m.Current.Viewer.Value, Is.EqualTo(200));
    }

    [Test]
    public void UploadPostsTheThumbnailAndSendsTheAnswerText()
    {
        var (m, files) = Manager();
        files.Add("a.png", 1, (64, 64));
        m.OpenProfileUpload();
        m.RefreshLocalFiles(5);
        m.Current.File = m.GetFile(0);
        string thumb = m.GetFile(0).ThumbnailDir;
        files.Data[thumb.Replace('/', '\\')] = new byte[] { 1, 2, 3 };
        byte[] posted = null;
        Action<string> answer = null;
        m.Post = (body, done) => { posted = body; answer = done; return true; };
        var keys = new List<string>();
        int failed = 0;
        m.SendProfileKey = keys.Add;
        m.SendUploadFailed = () => failed++;

        Assert.That(m.StartUpload(), Is.EqualTo(0));
        Assert.That(m.Locked, Is.True);
        Assert.That(posted, Is.EqualTo(ProfileImages.UploadBody(1, 5, 100, 200, 0, 200, new byte[] { 1, 2, 3 })));
        Assert.That(m.StartUpload(), Is.EqualTo(4));        // locked while the post is out

        answer("data/profiles/avatar/200/x.png");           // the text is sent whatever the answer's code was
        Assert.That(keys, Is.EqualTo(new[] { "data/profiles/avatar/200/x.png" }));
        Assert.That(files.Deleted, Is.EqualTo(new[] { thumb }));
        Assert.That(failed, Is.EqualTo(0));
        Assert.That(m.Current, Is.Null);
        Assert.That(m.Locked, Is.False);
    }

    [Test]
    public void UploadErrorsUnlock()
    {
        var (m, files) = Manager();
        files.Add("a.png", 1, (64, 64));
        m.OpenProfileUpload();
        Assert.That(m.StartUpload(), Is.EqualTo(0xb));      // no file picked
        Assert.That(m.Current, Is.Null);

        int failed = 0;
        m.SendUploadFailed = () => failed++;
        m.OpenProfileUpload();
        m.RefreshLocalFiles(5);
        m.Current.File = m.GetFile(0);
        files.Data[m.GetFile(0).ThumbnailDir.Replace('/', '\\')] = new byte[0x100000];
        Assert.That(m.StartUpload(), Is.EqualTo(0));        // 0xd2 comes as the post's answer
        Assert.That(failed, Is.EqualTo(1));
        Assert.That(m.Current, Is.Null);

        m.OpenProfileUpload();
        m.RefreshLocalFiles(5);
        m.Current.File = m.GetFile(0);
        files.Data[m.GetFile(0).ThumbnailDir.Replace('/', '\\')] = new byte[] { 1 };
        m.Post = (_, _) => false;                           // not SOAP mode 2
        m.StartUpload();
        Assert.That(failed, Is.EqualTo(2));
        Assert.That(m.Locked, Is.False);
    }

    [Test]
    public void ResultMessagesSkipZeroAndBlock()
    {
        Assert.That(UgcManager.ResultMessage(0), Is.EqualTo(""));
        Assert.That(UgcManager.ResultMessage(0x7a), Is.EqualTo(""));
    }

    [Test]
    public void PaintOpensOnlyPngAndJpg()
    {
        var (m, files) = Manager();
        files.Add("a.dds", 2, (64, 64));
        files.Add("b.png", 1, (64, 64));
        var notices = new List<int>();
        m.CommonNotice = notices.Add;
        m.RefreshLocalFiles(5);
        m.OpenFileInPaint(0);
        m.OpenFileInPaint(1);
        Assert.That(notices, Is.EqualTo(new[] { 0x2de }));
        Assert.That(files.Edited, Is.EqualTo(new[] { UgcManager.ImageFolder + "\\b.png" }));
    }

    [Test]
    public void TermsTagGivesTheCheckBox()
    {
        Assert.That(MessageBoxQueue.TermsAgreed("terms,checkbox0:true"), Is.True);
        Assert.That(MessageBoxQueue.TermsAgreed("terms,checkbox0:false"), Is.False);
        Assert.That(MessageBoxQueue.TermsAgreed("terms"), Is.False);
        Assert.That(MessageBoxQueue.TermsAgreed("noexist"), Is.False);
    }

    // ---- UgcImageInfo -----------------------------------------------------------------------------------------------

    static (int, int)? Size(byte[] b) => UgcImageInfo.Size(new MemoryStream(b));

    [Test]
    public void PngSizeFromIhdr()
    {
        var b = new byte[33];
        new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 13, 10, 26, 10, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R',
            0, 0, 1, 0, 0, 0, 0, 0x6b }.CopyTo(b, 0);
        Assert.That(Size(b), Is.EqualTo((256, 107)));
    }

    [Test]
    public void DdsSizeFromHeader()
    {
        var b = new byte[128];
        "DDS "u8.ToArray().CopyTo(b, 0);
        BitConverter.GetBytes(124).CopyTo(b, 4);
        BitConverter.GetBytes(64).CopyTo(b, 12);      // height
        BitConverter.GetBytes(512).CopyTo(b, 16);     // width
        Assert.That(Size(b), Is.EqualTo((512, 64)));
    }

    [Test]
    public void JpegSizeFromTheFirstFrame()
    {
        var b = new List<byte> { 0xff, 0xd8 };
        b.AddRange(new byte[] { 0xff, 0xe0, 0, 4, 0, 0 });                 // APP0
        b.AddRange(new byte[] { 0xff, 0xc4, 0, 3, 0 });                    // DHT: not a frame
        b.AddRange(new byte[] { 0xff, 0xc2, 0, 11, 8, 0x01, 0x2c, 0x00, 0xc8, 1, 1, 0x11, 0 });     // SOF2: 300 high, 200 wide
        Assert.That(Size(b.ToArray()), Is.EqualTo((200, 300)));
    }

    [Test]
    public void OtherFilesHaveNoSize()
    {
        Assert.That(Size(new byte[] { 0x42, 0x4d, 0, 0, 0, 0, 0, 0 }), Is.Null);
        Assert.That(Size(new byte[] { 0x89, (byte)'P' }), Is.Null);
        Assert.That(Size(new byte[] { 0xff, 0xd8, 0xff, 0xda, 0, 2 }), Is.Null);
    }
}
