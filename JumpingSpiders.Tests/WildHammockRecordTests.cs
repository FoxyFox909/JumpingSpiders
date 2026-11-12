using Xunit;

namespace JumpingSpiders.Tests;

public class WildHammockRecordTests
{
    [Fact]
    public void RoundTripKeepsEveryField()
    {
        WildHammockRecord record = new WildHammockRecord();
        record.State = WildHammockState.HasHammock;
        record.X = 512001;
        record.Y = 118;
        record.Z = -3;
        record.SearchedTotalHours = 12.5;
        record.LastSpawnTotalHours = 4000.25;

        WildHammockRecord read = WildHammockRecord.FromBytes(record.ToBytes());
        Assert.Equal(WildHammockState.HasHammock, read.State);
        Assert.Equal(512001, read.X);
        Assert.Equal(118, read.Y);
        Assert.Equal(-3, read.Z);
        Assert.Equal(12.5, read.SearchedTotalHours);
        Assert.Equal(4000.25, read.LastSpawnTotalHours);
    }

    [Fact]
    public void NewRecordIsUnknownAndNeverSpawned()
    {
        WildHammockRecord read = WildHammockRecord.FromBytes(new WildHammockRecord().ToBytes());
        Assert.Equal(WildHammockState.Unknown, read.State);
        Assert.Equal(WildHammockRecord.Never, read.LastSpawnTotalHours);
        Assert.Equal(WildHammockRecord.Never, read.SearchedTotalHours);
    }

    [Fact]
    public void MissingOrDamagedDataReadsAsEmpty()
    {
        Assert.Equal(WildHammockState.Unknown, WildHammockRecord.FromBytes(null).State);
        Assert.Equal(WildHammockState.Unknown, WildHammockRecord.FromBytes(new byte[0]).State);

        byte[] good = MakeHammockRecord().ToBytes();
        byte[] truncated = new byte[good.Length - 3];
        System.Array.Copy(good, truncated, truncated.Length);
        Assert.Equal(WildHammockState.Unknown, WildHammockRecord.FromBytes(truncated).State);

        byte[] otherVersion = MakeHammockRecord().ToBytes();
        otherVersion[0] = 99;
        Assert.Equal(WildHammockState.Unknown, WildHammockRecord.FromBytes(otherVersion).State);

        byte[] badState = MakeHammockRecord().ToBytes();
        badState[1] = 200;
        Assert.Equal(WildHammockState.Unknown, WildHammockRecord.FromBytes(badState).State);
    }

    [Fact]
    public void ClearHammockKeepsTheSpawnTime()
    {
        WildHammockRecord record = MakeHammockRecord();
        record.LastSpawnTotalHours = 77;
        record.ClearHammock();
        Assert.Equal(WildHammockState.Unknown, record.State);
        Assert.Equal(77, record.LastSpawnTotalHours);
    }

    private static WildHammockRecord MakeHammockRecord()
    {
        WildHammockRecord record = new WildHammockRecord();
        record.State = WildHammockState.HasHammock;
        record.X = 10;
        record.Y = 20;
        record.Z = 30;
        return record;
    }
}
