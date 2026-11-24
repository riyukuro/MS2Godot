using System;

namespace Ms2.Ui.ChatEmoticons;

/// <summary>
/// Joins one HUD scene to the session's ChatEmoticonState: the state outlives the scene (one CChatEmoticonCli per client,
/// the scene reloads on every field change), so the scene's box and typing handlers are added on Hook and taken off again
/// on Unhook. Without it, each reload would add one more box per ChatStamp result.
/// </summary>
public sealed class ChatEmoticonLink
{
    readonly ChatEmoticonState state;
    readonly Action<string> messageBox;
    readonly Action<string, byte, uint> typeInWindow;
    readonly Action<int> added;

    public ChatEmoticonLink(ChatEmoticonState state, Action<string> messageBox, Action<string, byte, uint> typeInWindow,
        Action<int> added = null)
    {
        this.state = state; this.messageBox = messageBox; this.typeInWindow = typeInWindow; this.added = added;
    }

    public void Hook()
    {
        Unhook();
        state.MessageBox += messageBox;
        state.TypeInWindow += typeInWindow;
        if (added != null) state.Added += added;
    }

    public void Unhook()
    {
        state.MessageBox -= messageBox;
        state.TypeInWindow -= typeInWindow;
        if (added != null) state.Added -= added;
    }
}
