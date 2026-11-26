using System.Globalization;
using System.Text;

namespace Ms2.Ui.Items;

/// <summary>
/// The item browser's spawn line: the Maple2 server's own "item" GM command (Maple2.Server.Game/Commands/ItemCommand.cs:
/// the id argument, "--amount"/"-a" default 1 (:26), "--rarity"/"-r" default 0, the item's own (:27), a rarity above 0
/// clamped to 1..6 (:15, :45)), sent as a chat line because the server runs a "/"-prefixed chat message as a command
/// (UserChatHandler: message.StartsWith('/') invokes the command router). It adds no packet and changes no server code.
/// </summary>
public static class ItemSpawnCommand
{
    /// <summary>The server's highest rarity (ItemCommand MAX_RARITY, :15). A combat item's rank in the data is 0-6.</summary>
    public const int MaxRarity = 6;

    /// <summary>The command line for one spawn. The amount is clamped to 1..slot max (the bag takes one stack, so the
    /// item table's slotMax is the most; a slot max below 1 counts as 1). Rarity 0 leaves --rarity off, for the item's
    /// own rank as every other source makes it (:45 reads it as -1); 1..6 is sent, and a larger one clamps to 6 like the
    /// server does. A full stack ("--amount slot max") is the shift-click path.</summary>
    public static string Line(int itemId, int amount, int rarity, uint slotMax)
    {
        int max = (int)Math.Clamp(slotMax, 1u, 99999u);
        var sb = new StringBuilder(24);
        sb.Append("/item ").Append(itemId.ToString(CultureInfo.InvariantCulture));
        sb.Append(" --amount ").Append(Math.Clamp(amount, 1, max).ToString(CultureInfo.InvariantCulture));
        if (rarity > 0) sb.Append(" --rarity ").Append(Math.Clamp(rarity, 1, MaxRarity).ToString(CultureInfo.InvariantCulture));
        return sb.ToString();
    }
}
