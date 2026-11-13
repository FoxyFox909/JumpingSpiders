using Vintagestory.API.Common;

namespace JumpingSpiders;

/// <summary>
/// Reads and writes a spider chunk's WildHammockRecord in the game's "map chunk mod data".
///
/// A map chunk is the game's per-column data (32 x 32 blocks, the whole height): height maps, climate, and a small
/// dictionary of named byte arrays that mods may use. It is saved with the world and never sent to players. Server
/// only. The game only saves a map chunk that is marked as changed; SetModdata does that, and Save marks it again
/// so this does not depend on that detail.
/// </summary>
public static class WildHammockRecords
{
    /// <summary>Name of our entry in the map chunk mod data.</summary>
    public const string ModDataKey = "jumpingspiders:wildhammock";

    /// <summary>The record of this chunk column, or an empty one (State Unknown) if it has none.</summary>
    public static WildHammockRecord Load(IMapChunk mapChunk)
    {
        if (mapChunk == null)
        {
            return new WildHammockRecord();
        }
        return WildHammockRecord.FromBytes(mapChunk.GetModdata(ModDataKey));
    }

    public static void Save(IMapChunk mapChunk, WildHammockRecord record)
    {
        if (mapChunk == null || record == null)
        {
            return;
        }
        mapChunk.SetModdata(ModDataKey, record.ToBytes());
        mapChunk.MarkDirty();
    }
}
