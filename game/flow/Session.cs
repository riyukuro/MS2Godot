using Ms2.Net.Packets;
using Ms2Godot.Ui;

namespace Ms2Godot.Flow;

/// <summary>
/// What the offline game flow carries between scenes: the stub account, its character list and the chosen character.
/// The scenes read it through one-line hooks and fall back to their own defaults when it is empty (each scene still runs alone).
/// </summary>
public static class Session
{
    public static string AccountId;
    public static List<CharacterEntry> Characters;
    public static CharacterEntry Chosen;
    public static int SelectedIndex;

    /// <summary>The scene that runs the field in the flow (the HUD over the field); null when a scene runs alone.</summary>
    public static string GameScene;

    /// <summary>Raised by the create screen when it has built its create request (nothing is sent).</summary>
    public static event Action<CreateCharacter> CreateRequested;
    public static void RaiseCreateRequested(CreateCharacter request) => CreateRequested?.Invoke(request);

    public static void Reset() { AccountId = null; Characters = null; Chosen = null; SelectedIndex = 0; }

    /// <summary>The look inputs of the chosen character; PlayerLook uses them over its command line defaults.</summary>
    public static void ApplyLook(ref int gender, ref int job)
    {
        if (Chosen?.User == null) return;
        gender = Chosen.User.Gender;
        job = Chosen.User.JobCode;
    }

    /// <summary>The list the character select shows.</summary>
    public static List<CharacterEntry> List() => Characters ??= StubCharacterList.Build();

    /// <summary>A new character from a create request, in the first free slot.</summary>
    public static CharacterEntry Add(CreateCharacter r)
    {
        var e = new CharacterEntry
        {
            User = new UserBlock
            {
                CharacterId = 2000 + List().Count, Name = r.Name, Level = 1, Job = r.JobGroup, JobCode = r.JobGroup,
                MapA = 02000001, Gender = r.Gender, CreationTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            },
        };
        List().Add(e);
        return e;
    }
}
