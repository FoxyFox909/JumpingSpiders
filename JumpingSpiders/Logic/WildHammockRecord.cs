using System;
using System.IO;

namespace JumpingSpiders;

/// <summary>
/// The per spider chunk memory of the wild spawner: where the chunk's one wild hammock is (or that the chunk has no
/// usable tree), and when a spider last spawned there. The spawner saves it in the chunk column's "map chunk mod
/// data" (a small byte array the game stores with the chunk column), so it survives restarts.
///
/// ToBytes / FromBytes turn it into those bytes. The format starts with a version number so a later version of the
/// mod can still read old saves. Plain data plus the codec; covered by unit tests.
/// </summary>
public class WildHammockRecord
{
    /// <summary>Format version written first. Records with another version are read as empty.</summary>
    public const byte FormatVersion = 1;

    /// <summary>Value of the time fields when the event never happened.</summary>
    public const double Never = -1;

    public WildHammockState State = WildHammockState.Unknown;

    /// <summary>Block position of the hammock (only meaningful when State is HasHammock).</summary>
    public int X;
    public int Y;
    public int Z;

    /// <summary>In-game total hours when the last hammock spot search ended without a site (State NoSite), or Never.</summary>
    public double SearchedTotalHours = Never;

    /// <summary>In-game total hours of the last wild spawn in this spider chunk, or Never.</summary>
    public double LastSpawnTotalHours = Never;

    public byte[] ToBytes()
    {
        MemoryStream stream = new MemoryStream();
        BinaryWriter writer = new BinaryWriter(stream);
        writer.Write(FormatVersion);
        writer.Write((byte)State);
        writer.Write(X);
        writer.Write(Y);
        writer.Write(Z);
        writer.Write(SearchedTotalHours);
        writer.Write(LastSpawnTotalHours);
        writer.Flush();
        return stream.ToArray();
    }

    /// <summary>
    /// Reads bytes written by ToBytes. Returns an empty record (State Unknown) for null, for a different format
    /// version and for damaged data, so a bad record only costs one new search.
    /// </summary>
    public static WildHammockRecord FromBytes(byte[] data)
    {
        WildHammockRecord record = new WildHammockRecord();
        if (data == null || data.Length == 0)
        {
            return record;
        }
        try
        {
            BinaryReader reader = new BinaryReader(new MemoryStream(data));
            byte version = reader.ReadByte();
            if (version != FormatVersion)
            {
                return new WildHammockRecord();
            }
            byte state = reader.ReadByte();
            if (!Enum.IsDefined(typeof(WildHammockState), (int)state))
            {
                return new WildHammockRecord();
            }
            record.State = (WildHammockState)state;
            record.X = reader.ReadInt32();
            record.Y = reader.ReadInt32();
            record.Z = reader.ReadInt32();
            record.SearchedTotalHours = reader.ReadDouble();
            record.LastSpawnTotalHours = reader.ReadDouble();
        }
        catch (EndOfStreamException)
        {
            return new WildHammockRecord();
        }
        return record;
    }

    /// <summary>Forget the hammock (or the "no site" result), keeping the spawn time.</summary>
    public void ClearHammock()
    {
        State = WildHammockState.Unknown;
        X = 0;
        Y = 0;
        Z = 0;
        SearchedTotalHours = Never;
    }
}
