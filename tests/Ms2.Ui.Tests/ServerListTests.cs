using Ms2.Ui.Launch;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// The launcher's saved server list and address rules, with no Godot: the store the launcher screen edits
/// (user://servers.json in the game).
/// The addresses in these tests are from the reserved documentation ranges, so nothing here can reach a real server.
/// </summary>
public class ServerListTests
{
    static readonly ServerEntry Reserved = new("Reserved", "192.0.2.1", 6553);

    string dir;

    [SetUp]
    public void SetUp() => dir = Path.Combine(TestContext.CurrentContext.WorkDirectory, "launcher-" + Guid.NewGuid().ToString("N"));

    [TearDown]
    public void TearDown()
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch (IOException) { }
    }

    string StorePath => Path.Combine(dir, ServerList.DefaultFileName);

    [Test]
    public void SavedListComesBackInOrder()
    {
        var list = new ServerList();
        Assert.That(list.Add("Reserved A", "192.0.2.1:6553", out string problem), Is.True, problem);
        Assert.That(list.Add("Reserved B", "198.51.100.7:1", out problem), Is.True, problem);
        Assert.That(list.Save(StorePath, out problem), Is.True, problem);

        var loaded = ServerList.Load(StorePath);
        Assert.That(loaded.Count, Is.EqualTo(2));
        Assert.That(loaded.Entries[0].Name, Is.EqualTo("Reserved A"));
        Assert.That(loaded.Entries[0].Address, Is.EqualTo("192.0.2.1:6553"));
        Assert.That(loaded.Entries[1].Host, Is.EqualTo("198.51.100.7"));
        Assert.That(loaded.Entries[1].Port, Is.EqualTo(1));
    }

    [Test]
    public void EditAndRemoveChangeTheSavedFile()
    {
        var list = new ServerList();
        Assert.That(list.Add("Old", "192.0.2.1:1", out _), Is.True);
        Assert.That(list.Edit(0, "New", "192.0.2.9:65535", out string problem), Is.True, problem);
        Assert.That(list.Save(StorePath, out problem), Is.True, problem);
        var loaded = ServerList.Load(StorePath);
        Assert.That(loaded.Count, Is.EqualTo(1));
        Assert.That(loaded.Entries[0].Name, Is.EqualTo("New"));
        Assert.That(loaded.Entries[0].Port, Is.EqualTo(65535));

        Assert.That(loaded.RemoveAt(0), Is.True);
        Assert.That(loaded.Save(StorePath, out problem), Is.True, problem);
        Assert.That(ServerList.Load(StorePath).Count, Is.EqualTo(0));
    }

    [Test]
    public void MissingFileIsAnEmptyList()
    {
        var list = ServerList.Load(StorePath);
        Assert.That(list.Count, Is.EqualTo(0));
        Assert.That(list.Save(StorePath, out string problem), Is.True, problem);
        Assert.That(ServerList.Load(StorePath).Count, Is.EqualTo(0));
    }

    [Test]
    public void BrokenFileIsAnEmptyListAndNeverThrows()
    {
        Directory.CreateDirectory(dir);
        foreach (string text in new[] { "", "{", "{ \"servers\": 4 }", "[1,2,3]", "{\"servers\":[{\"name\":\"x\"}]}" })
        {
            File.WriteAllText(StorePath, text);
            var list = ServerList.Load(StorePath);
            Assert.That(list.Count, Is.EqualTo(0), text);
        }
        // a list with one good row and one whose address no longer parses keeps only the good row
        File.WriteAllText(StorePath, "{\"servers\":[{\"name\":\"Good\",\"address\":\"192.0.2.1:2\"},{\"name\":\"Bad\",\"address\":\"nope\"}]}");
        var mixed = ServerList.Load(StorePath);
        Assert.That(mixed.Count, Is.EqualTo(1));
        Assert.That(mixed.Entries[0].Name, Is.EqualTo("Good"));
    }

    [Test]
    public void AddRejectsAnUnusableEntryAndKeepsTheList()
    {
        var list = new ServerList();
        Assert.That(list.Add("", "192.0.2.1:1", out string problem), Is.False);
        Assert.That(problem, Is.Not.Empty);
        Assert.That(list.Add("Name", "nope", out problem), Is.False);
        Assert.That(problem, Is.Not.Empty);
        Assert.That(list.Count, Is.EqualTo(0));

        Assert.That(list.Add("Name", "192.0.2.1:1", out _), Is.True);
        Assert.That(list.Edit(0, "Name", "192.0.2.1:0", out problem), Is.False);
        Assert.That(problem, Is.Not.Empty);
        Assert.That(list.Entries[0].Address, Is.EqualTo("192.0.2.1:1"), "the rejected edit changes nothing");
    }

    [TestCase("", false)]
    [TestCase("   ", false)]
    [TestCase("hostonly", false)]
    [TestCase("hostonly:", false)]
    [TestCase(":80", false)]
    [TestCase("192.0.2.1:0", false)]
    [TestCase("192.0.2.1:65536", false)]
    [TestCase("192.0.2.1:-1", false)]
    [TestCase("192.0.2.1:8x", false)]
    [TestCase("192.0.2.1:1:2", false)]
    [TestCase("ho st:80", false)]
    [TestCase("192.0.2.1:1", true)]
    [TestCase(" 192.0.2.1:65535 ", true)]
    [TestCase("name.example:80", true)]
    public void AddressValidation(string text, bool ok)
    {
        bool parsed = ServerAddress.TryParse(text, out string host, out int port, out string problem);
        Assert.That(parsed, Is.EqualTo(ok), text);
        if (ok)
        {
            Assert.That(problem, Is.Null);
            Assert.That(host, Is.Not.Empty);
            Assert.That(port, Is.InRange(1, 65535));
        }
        else
        {
            Assert.That(problem, Is.Not.Empty, "a rejected address says what is wrong");
            Assert.That(host, Is.Empty);
            Assert.That(port, Is.Zero);
        }
    }
}
