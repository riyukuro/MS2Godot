using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The profile picture paths, merged downloads and the upload body/answer (ProfileImages), and the Ugc packet reads.</summary>
public class ProfileImagesTests
{
    const string Key = "data/profiles/avatar/12345/abc.png";

    static ProfileImages Images(params string[] files) => new()
    {
        NoProfile = "./data/resource/image/noprofile.png",
        Host = "http://web.example",
        Exists = f => files.Contains(f),
    };

    [Test]
    public void FileNameJoinsAFivePartKey()
    {
        Assert.That(ProfileImages.FileName(Key), Is.EqualTo("profilesavatar_12345_abc.png"));
        Assert.That(ProfileImages.FileName("a/b/c"), Is.EqualTo(""));
        Assert.That(ProfileImages.FileName(""), Is.EqualTo(""));
        Assert.That(ProfileImages.FileOfPath("./Custom/Download/Profile/profilesavatar_12345_abc.png"), Is.EqualTo("profilesavatar_12345_abc.png"));
        Assert.That(ProfileImages.FileOfPath("./data/resource/image/noprofile.png"), Is.Null);
    }

    [Test]
    public void PathGivesTheDefaultUntilTheFileIsThere()
    {
        var im = Images();
        Assert.That(im.ProfileImagePath("", true), Is.EqualTo(im.NoProfile));
        Assert.That(im.ProfileImagePath(Key, true), Is.EqualTo("./Custom/Download/Profile/profilesavatar_12345_abc.png"));
        Assert.That(im.Path(1, "", true), Is.EqualTo(im.NoProfile));

        var asked = new List<(string Key, string Url, string File)>();
        im.DownloadRequested += (k, u, f) => asked.Add((k, u, f));
        Assert.That(im.Path(12345, Key, true), Is.EqualTo(im.NoProfile));
        Assert.That(im.Path(7, Key, false), Is.EqualTo("./Custom/Download/Profile/profilesavatar_12345_abc.png"));
        Assert.That(asked, Is.EqualTo(new[] { (Key, "http://web.example/" + Key, "profilesavatar_12345_abc.png") }));

        var ready = new List<(long, string)>();
        im.Ready += (id, k) => ready.Add((id, k));
        im.Downloaded(Key, true);
        Assert.That(ready, Is.EqualTo(new[] { (12345L, Key), (7L, Key) }));
        im.Downloaded(Key, true);
        Assert.That(ready, Has.Count.EqualTo(2));

        var have = Images("profilesavatar_12345_abc.png");
        have.DownloadRequested += (_, _, _) => Assert.Fail("no download for a file that is there");
        Assert.That(have.Path(12345, Key, true), Is.EqualTo("./Custom/Download/Profile/profilesavatar_12345_abc.png"));
    }

    [Test]
    public void AFailedDownloadCanBeAskedAgain()
    {
        var im = Images();
        int asked = 0, ready = 0;
        im.DownloadRequested += (_, _, _) => asked++;
        im.Ready += (_, _) => ready++;
        im.Download(1, Key);
        im.Downloaded(Key, false);
        im.Download(1, Key);
        Assert.That((asked, ready), Is.EqualTo((2, 0)));
    }

    [Test]
    public void UploadBodyAndAnswer()
    {
        var body = ProfileImages.UploadBody(1, ProfileImages.ProfileKind, 10, 20, 0, 20, new byte[] { 0xAA, 0xBB });
        Assert.That(body, Has.Length.EqualTo(4 + 4 + 8 * 5 + 2));
        Assert.That(BitConverter.ToInt32(body, 0), Is.EqualTo(1));
        Assert.That(BitConverter.ToInt32(body, 4), Is.EqualTo(5));
        Assert.That(BitConverter.ToInt64(body, 8), Is.EqualTo(10));
        Assert.That(BitConverter.ToInt64(body, 16), Is.EqualTo(20));
        Assert.That(BitConverter.ToInt64(body, 24), Is.EqualTo(0));
        Assert.That(BitConverter.ToInt64(body, 32), Is.EqualTo(20));
        Assert.That(BitConverter.ToInt64(body, 40), Is.EqualTo(2));
        Assert.That(body[48..], Is.EqualTo(new byte[] { 0xAA, 0xBB }));

        Assert.That(ProfileImages.ParseUploadReply("0," + Key), Is.EqualTo((0, Key)));
        Assert.That(ProfileImages.ParseUploadReply("Character not in game."), Is.EqualTo((6, "")));
        Assert.That(new ProfileImages { UploadBase = "http://h:1/" }.UploadUrl, Is.EqualTo("http://h:1/urq.aspx"));
        Assert.That(new ProfileImages().UploadUrl, Is.Null);
    }

    [Test]
    public void UgcPacketsReadAndWrite()
    {
        var w = new Maple2.PacketLib.Tools.ByteWriter();
        w.WriteByte(0x11); w.WriteUnicodeString("http://web.example/ws.asmx?wsdl"); w.WriteUnicodeString("http://web.example");
        w.WriteUnicodeString("kr"); w.WriteByte(2);
        var e = (UgcEndpoint)UgcMessage.Read(new Maple2.PacketLib.Tools.ByteReader(w.ToArray()));
        Assert.That((e.Host, e.UploadBase), Is.EqualTo(("http://web.example", "http://web.example/")));
        Assert.That((e with { SoapMode = 1 }).UploadBase, Is.EqualTo(""));

        w = new Maple2.PacketLib.Tools.ByteWriter();
        w.WriteByte(0x0b); w.WriteInt(9); w.WriteLong(12345); w.WriteUnicodeString(Key);
        Assert.That(UgcMessage.Read(new Maple2.PacketLib.Tools.ByteReader(w.ToArray())), Is.EqualTo(new UgcProfileImage(9, 12345, Key)));

        var o = new Maple2.PacketLib.Tools.ByteWriter();
        new UgcProfileImageRequest(Key).Write(o);
        var r = new Maple2.PacketLib.Tools.ByteReader(o.ToArray());
        Assert.That((r.ReadByte(), r.ReadUnicodeString()), Is.EqualTo(((byte)0x0b, Key)));
    }

    [Test]
    public void ShotRectCutsTheHeadBetweenEffUiAndEffBody()
    {
        // n 199, margin 39: top 200.7 + 39, bottom 400.2 - 19.5, half width (380 - 239) / 2 = 70 about x 640
        Assert.That(ProfileImages.ShotRect(640f, 200.7f, 400.2f), Is.EqualTo((570u, 239u, 710u, 380u)));
        Assert.That(ProfileImages.ShotRect(100f, 0f, 100f), Is.EqualTo((65u, 20u, 135u, 90u)));
    }
}
