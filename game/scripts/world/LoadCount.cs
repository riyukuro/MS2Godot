namespace Ms2Godot.World;

/// <summary>
/// A field load's entity count, as the client's loading screen reads it. WorldService::FUN_141bbd640 (the
/// OwnedEntityEnterWorld handler, message 8253) asks EntityWorldManager::FUN_1411cfb80 -> GetBlockInfo for the block's
/// counts while the world is loading (m_state 2): the entities that have entered the world (WorldLoadedInfo
/// m_enteredWorldCount) and the block's entities (m_newEntities.size()). It then hands count + 1 and the total to
/// CUIService::FUN_141adb9c0 -> UILoadingProgress::SetProgress. Here an entity counts when the field build has placed
/// it: its model's draws and colliders are in the world, or it has nothing left to place.
/// </summary>
public sealed class LoadCount
{
    public int Count { get; private set; }
    public int Total { get; private set; }
    /// <summary>Raised on every change, with the count and the total.</summary>
    public event System.Action<int, int> Changed;

    public void Begin(int total) { Total = total; Count = 0; Changed?.Invoke(Count, Total); }

    public void Add(int n = 1)
    {
        if (n <= 0) return;
        Count += n;
        Changed?.Invoke(Count, Total);
    }
}
