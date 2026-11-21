using Ms2.Net.Packets;

namespace Ms2.Ui.ChatEmoticons;

/// <summary>
/// CChatEmoticonCli (chatemoticoncli.cpp), the local player's chat emoticons: the favorite ids (m_ids) and the owned packs
/// with their expiry (m_groups). The ChatStamp packet's handlers (DispatchPacket @0x140a3a700) change it and tell the
/// listeners the client's calls reach: the dialog's refresh / clear / new-group, a message box by common key, the added
/// pack's notice, and the messenger windows that type or send an emoticon.
/// STILL-OPEN: type 2's reward notify (the 0x4e5d window's notice and the system notice with the item's name) has no
/// listener; the Added event carries the item id.
/// </summary>
public sealed class ChatEmoticonState
{
    readonly List<uint> ids = new();
    readonly List<ChatEmoticonGroup> groups = new();

    public ChatEmoticonState(ChatEmoticonTable table) { Table = table; }

    public ChatEmoticonTable Table { get; }
    /// <summary>The server time the expiry is tested against.</summary>
    public Func<long> Now = () => 0;

    public IReadOnlyList<uint> Favorites => ids;
    public IReadOnlyList<ChatEmoticonGroup> Groups => groups;

    /// <summary>The dialog's refresh (FUN_140bd2620: rebuild model, tabs, items).</summary>
    public event Action Refresh;
    /// <summary>The dialog's ClearEmoticonData.</summary>
    public event Action Clear;
    /// <summary>The dialog's AddNewGroup(group).</summary>
    public event Action<uint> NewGroup;
    /// <summary>A common string message box by key (FUN_140d73bc0 with the key's string).</summary>
    public event Action<string> MessageBox;
    /// <summary>Type 2's reward notify: the pack's item id.</summary>
    public event Action<int> Added;
    /// <summary>Type 3: UIMessenger::TypeEmoticon(flag, id) for the window of that name.</summary>
    public event Action<string, byte, uint> TypeInWindow;
    /// <summary>Type 4: UIGroupMessenger::SendEmoticonImage(id) for the window of that name.</summary>
    public event Action<string, uint> SendImageInWindow;

    readonly System.Collections.Generic.SortedSet<uint> added = new();   // FUN_140a39360's setAdded

    /// <summary>FindGroup (@0x140a3a640).</summary>
    public ChatEmoticonGroup? FindGroup(uint id)
    {
        foreach (var g in groups) if (g.Id == id) return g;
        return null;
    }

    /// <summary>IsGroupExpired (@0x140a3a140): an unowned group is expired; owned ones until the expiry has passed.</summary>
    public bool IsGroupExpired(uint id) => FindGroup(id) is not { } g || !(g.Expire >= Now());

    /// <summary>IsEmoticonUsable (@0x140a39dd0): the emoticon is known, its pack owned and the expiry not passed.</summary>
    public bool IsEmoticonUsable(uint id)
    {
        var e = Table.GetEmoticon(id);
        return e != null && FindGroup(e.GroupId) is { } g && !(Now() > g.Expire);
    }

    /// <summary>IsEmoticonFavorite (@0x140a39fa0): the emoticon is known and in the favorites.</summary>
    public bool IsEmoticonFavorite(uint id) => Table.GetEmoticon(id) != null && ids.Contains(id);

    /// <summary>FUN_140a3a0b0: the emoticon's pack is expired; an unknown emoticon is not.</summary>
    public bool IsEmoticonExpired(uint id) => Table.GetEmoticon(id) is { } e && IsGroupExpired(e.GroupId);

    /// <summary>AddGroup (@0x140a3a290): the group is removed first, then appended.</summary>
    public void AddGroup(uint id, long expire)
    {
        RemoveGroup(id);
        groups.Add(new ChatEmoticonGroup(id, expire));
    }

    /// <summary>RemoveGroup (@0x140a3a380).</summary>
    public void RemoveGroup(uint id) => groups.RemoveAll(g => g.Id == id);

    /// <summary>SendMessenger (@0x140a3a410): the list request with the groups whose expiry has passed (now &gt; expiry).</summary>
    public ChatStickerListRequest ListRequest()
    {
        long now = Now();
        return new ChatStickerListRequest(groups.Where(g => now > g.Expire).Select(g => g.Id).ToList());
    }

    /// <summary>
    /// DispatchPacket (@0x140a3a700): the handler of the message's type.
    /// 0 (@0x140a38fa0): m_ids and m_groups replaced, the dialog cleared and refreshed.
    /// 1 (@0x140a39170): the packs and favorites removed, the dialog refreshed, then the box s_msg_chat_emoticon_expired.
    /// 2 (@0x140a39360): the packs added, the dialog told of each new one and refreshed, then the item notice.
    /// 3 / 4 (@0x140a39830, @0x140a39940): the messenger window of the name types / sends the emoticon.
    /// 5 (@0x140a39720): the id moved to the end of the favorites, the dialog refreshed; 6 (@0x140a397b0): removed, refreshed.
    /// 7 (@0x140a39a20): the result's box: 0 print_failed; 1, 3, 4 add_failed; 2 add_failed_already_exist.
    /// </summary>
    public void Apply(ChatStampMessage m)
    {
        switch (m)
        {
            case ChatStampMessage.List l:
                ids.Clear(); groups.Clear();
                ids.AddRange(l.Favorites);
                foreach (var g in l.Groups) AddGroup(g.Id, g.Expire);
                Clear?.Invoke();
                Refresh?.Invoke();
                break;
            case ChatStampMessage.Expired x:
                foreach (uint g in x.Groups) RemoveGroup(g);
                foreach (uint id in x.Favorites) ids.Remove(id);
                Refresh?.Invoke();
                MessageBox?.Invoke("s_msg_chat_emoticon_expired");
                break;
            case ChatStampMessage.Added a:
                added.Clear();
                foreach (var g in a.Groups) { AddGroup(g.Id, g.Expire); added.Add(g.Id); }   // std::set setAdded
                foreach (uint id in added) NewGroup?.Invoke(id);                              // ascending, once each
                Refresh?.Invoke();
                Added?.Invoke(a.ItemId);
                break;
            case ChatStampMessage.Typed t: TypeInWindow?.Invoke(t.Window, t.Flag, t.Id); break;
            case ChatStampMessage.GroupSent s: SendImageInWindow?.Invoke(s.Window, s.Id); break;
            case ChatStampMessage.MovedToEnd e:
                ids.Remove(e.Id); ids.Add(e.Id);
                Refresh?.Invoke();
                break;
            case ChatStampMessage.Removed r:
                ids.Remove(r.Id);
                Refresh?.Invoke();
                break;
            case ChatStampMessage.Result res:
                string key = res.Code switch
                {
                    0 => "s_msg_chat_emoticon_print_failed",
                    1 or 3 or 4 => "s_msg_chat_emoticon_add_failed",
                    2 => "s_msg_chat_emoticon_add_failed_already_exist",
                    _ => null,
                };
                if (key != null) MessageBox?.Invoke(key);
                break;
        }
    }
}
