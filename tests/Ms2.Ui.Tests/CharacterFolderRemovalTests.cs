using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>CharacterRemoved's folder removal (DeleteDirectoryTree on the account folder's entries starting with the id).</summary>
public class CharacterFolderRemovalTests
{
    string root;

    [SetUp] public void Make() => root = Directory.CreateTempSubdirectory("ms2-charfolder-").FullName;
    [TearDown] public void Drop() { try { Directory.Delete(root, true); } catch (IOException) { } }

    string Put(string rel)
    {
        var p = Path.Combine(root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, "x");
        return p;
    }

    [Test]
    public void TheCharactersFolderGoesWithEveryEntryStartingWithItsId()
    {
        Put("123/UIQuickSlotDialog.xml");
        Put("123/CharacterOption.xml");
        Put("123/sub/learningQuestCharacter.xml");
        Put("1234/UIChatDialog.xml");        // the client's FindFirstFileA("…/123*.*") lists it too
        Put("123.bak");
        Put("124/UIChatDialog.xml");
        Put("AccountOption.xml");
        int n = CharacterFolderRemoval.Remove(root, 123);
        var left = Directory.GetFileSystemEntries(root, "*", SearchOption.AllDirectories).Select(e => Path.GetRelativePath(root, e)).OrderBy(e => e).ToArray();
        TestContext.WriteLine($"deleted {n} files, left: {string.Join(", ", left)}");
        Assert.That(n, Is.EqualTo(5));
        Assert.That(left, Is.EqualTo(new[] { "124", Path.Combine("124", "UIChatDialog.xml"), "AccountOption.xml" }));
    }

    [Test]
    public void AFolderStartingWithADotIsKeptAndSoIsItsParent()
    {
        Put("123/a.xml");
        Put("123/.keep/b.xml");
        Put("123/.c");                       // a dot file is still a file: deleted
        Assert.That(CharacterFolderRemoval.Remove(root, 123), Is.EqualTo(2));
        Assert.That(File.Exists(Path.Combine(root, "123", ".keep", "b.xml")), "the dot folder is not entered");
        Assert.That(Directory.Exists(Path.Combine(root, "123")), "RemoveDirectoryA fails on a folder that is not empty");
    }

    [Test]
    public void NothingHappensWithoutTheCharactersFolder()
    {
        Put("1234/a.xml");
        Put("123.bak");
        Assert.That(CharacterFolderRemoval.Remove(root, 123), Is.EqualTo(0), "DirectoryExists fails: no DeleteDirectoryTree");
        Assert.That(File.Exists(Path.Combine(root, "1234", "a.xml")));
        Assert.That(File.Exists(Path.Combine(root, "123.bak")));
        Assert.That(CharacterFolderRemoval.Remove(Path.Combine(root, "none"), 123), Is.EqualTo(0));
        Assert.That(CharacterFolderRemoval.Remove(null, 123), Is.EqualTo(0));
    }

    [Test]
    public void ALinkIsRemovedNotFollowed()
    {
        var outside = Directory.CreateTempSubdirectory("ms2-charfolder-out-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(outside, "keep.xml"), "x");
            Put("123/a.xml");
            Directory.CreateSymbolicLink(Path.Combine(root, "123", "linked"), outside);
            CharacterFolderRemoval.Remove(root, 123);
            Assert.That(File.Exists(Path.Combine(outside, "keep.xml")), "the link's target is untouched");
            Assert.That(Directory.Exists(Path.Combine(root, "123")), Is.False);
        }
        finally { Directory.Delete(outside, true); }
    }
}
