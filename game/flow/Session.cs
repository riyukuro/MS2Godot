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

    /// <summary>
    /// The server picked in the server list: UIServerListDialog m_nServerId, set by OnSelectServer to the ServerList packet
    /// row's id (at least 1, uiserverlistdialog.cpp:529-538). The flow has no server list page yet, so it is 1, the first row
    /// of table/serverList.xml.
    /// </summary>
    public static int ServerId = 1;

    /// <summary>
    /// The channel the login asked for: UIServerListDialog m_nChannel, the login type of ResponseLogin, which OnLoginResult
    /// stores as the current channel (DAT_1439f4a7c, uiserverlistdialog.cpp:618). The flow logs in with channel 0.
    /// </summary>
    public static int Channel;

    /// <summary>The server line's initData (FUN_141900750) for the session's server and channel.</summary>
    public static LobbyServerInfo ServerInfo(UiAssets assets) => LobbyServerInfo.Build(assets, ServerId, Channel);

    /// <summary>The scene that runs the field in the flow (the HUD over the field); null when a scene runs alone.</summary>
    public static string GameScene;

    public static void Reset() { AccountId = null; Characters = null; Chosen = null; SelectedIndex = 0; }

    /// <summary>The look inputs of the chosen character; PlayerLook uses them over its command line defaults.</summary>
    public static void ApplyLook(ref int gender, ref int job)
    {
        if (Chosen?.User == null) return;
        gender = Chosen.User.Gender;
        job = Chosen.User.JobCode;
    }

    static Rgba Color(int argb) => new((byte)argb, (byte)(argb >> 8), (byte)(argb >> 16), (byte)(argb >> 24));

    /// <summary>The list the character select shows.</summary>
    public static List<CharacterEntry> List() => Characters ??= StubCharacterList.Build();

    /// <summary>
    /// A new character from a create request, in the first free slot: the job group it asked for, and as its job the group's
    /// first job (table/job.xml subJob1, as the create preview uses JobTable::GetSubJob1; <paramref name="subJob1"/>).
    /// </summary>
    public static CharacterEntry Add(CreateCharacter r, int subJob1 = 0)
    {
        var e = new CharacterEntry
        {
            User = new UserBlock
            {
                CharacterId = 2000 + List().Count, Name = r.Name, Level = 1, Job = subJob1, JobCode = r.JobGroup,
                MapA = 02000001, Gender = r.Gender, CreationTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Skin = new SkinColor(Color(r.Color0), Color(r.Color1)),
            },
        };
        // the look the player made: the request's items, as the list entry's equips
        if (r.Items is { Length: > 0 })
            foreach (var it in CreateCharacter.DecodeItems(r.Items))
                e.Equips.Add(new EquipItem { Id = it.Id, Slot = it.Slot, Color = it.Color, Appearance = it.Appearance ?? Array.Empty<float>() });
        List().Add(e);
        return e;
    }
}
