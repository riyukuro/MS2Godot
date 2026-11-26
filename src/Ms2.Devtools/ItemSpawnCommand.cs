#if MS2_DEVTOOLS
using System.Globalization;
using System.Text;

namespace Ms2.Devtools;

/// <summary>
/// The item browser's spawn line: the Maple2 server's own "item" GM command (Maple2.Server.Game/Commands/ItemCommand.cs:
/// the id argument, "--amount"/"-a" default 1 (:26), "--rarity"/"-r" default 0, the item's own (:27), a rarity above 0
/// clamped to 1..6 (:15, :45)), sent as a chat line because the server runs a "/"-prefixed chat message as a command
/// (UserChatHandler: message.StartsWith('/') invokes the command router). It adds no packet and changes no server code.
/// The amount the server makes of it depends on the item: a currency (the id range 90000000..99999999, IsCurrency at
/// ModelExtensions.cs:94) keeps the amount as given (:53-57); a slot max of 1 (a non-stackable) makes that many copies,
/// capped by the bag's free slots (:59, :84-100); any other item is one stack whose amount the server sets (:102-105).
/// </summary>
public static class ItemSpawnCommand
{
    /// <summary>The server's highest rarity (ItemCommand MAX_RARITY, :15). A combat item's rank in the data is 0-6.</summary>
    public const int MaxRarity = 6;

    /// <summary>The server's currency ids (Item.IsCurrency, ModelExtensions.cs:94): 90000000 and up, below 100000000.</summary>
    public static bool IsCurrency(int itemId) => itemId is >= 90000000 and < 100000000;

    /// <summary>
    /// The command line for one spawn. The amount is at least 1 and has no upper bound for a currency or a slot max of 1
    /// (that many copies, the server caps them by free slots); a stackable item is capped at its slot max (the bag holds
    /// one stack of it; a slot max below 1 counts as 1 for that cap). Rarity 0 leaves --rarity off, for the item's own rank
    /// as every other source makes it (:45 reads it as -1); 1..6 is sent, and a larger one clamps to 6 like the server
    /// does. A full stack ("--amount slot max") is the shift-click path.
    /// </summary>
    public static string Line(int itemId, int amount, int rarity, uint slotMax)
    {
        int n = Math.Max(amount, 1);
        bool currency = IsCurrency(itemId);
        if (!currency && slotMax > 1) n = (int)Math.Min((uint)n, Math.Min(slotMax, 99999u));
        var sb = new StringBuilder(24);
        sb.Append("/item ").Append(itemId.ToString(CultureInfo.InvariantCulture));
        sb.Append(" --amount ").Append(n.ToString(CultureInfo.InvariantCulture));
        if (rarity > 0) sb.Append(" --rarity ").Append(Math.Clamp(rarity, 1, MaxRarity).ToString(CultureInfo.InvariantCulture));
        return sb.ToString();
    }
}
#endif
