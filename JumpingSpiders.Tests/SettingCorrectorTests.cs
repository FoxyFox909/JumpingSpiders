using Xunit;

namespace JumpingSpiders.Tests;

public class SettingCorrectorTests
{
    [Fact]
    public void GoodValuesPassWithoutWarnings()
    {
        SettingCorrector fix = new SettingCorrector();
        Assert.Equal(6, fix.AtLeast("Radius", 6, 0));
        Assert.Equal(0.0, fix.AtLeast("Hours", 0.0, 0));
        Assert.Equal(0.5, fix.Between("Share", 0.5, 0, 1));
        Assert.Equal(1.0, fix.Between("Share", 1.0, 0, 1));
        Assert.Equal(2.0, fix.AboveZero("Interval", 2.0, 1));
        Assert.Equal(60.0, fix.NotBelow("Max", 60.0, "Min", 30));
        Assert.Equal(5, fix.NotBelow("Up", 5, "minus Down", -2));
        Assert.Empty(fix.Warnings);
    }

    [Fact]
    public void BadValuesAreCorrectedWithOneWarningEach()
    {
        SettingCorrector fix = new SettingCorrector();
        Assert.Equal(0, fix.AtLeast("HammockBuildSearchRadius", -3, 0));
        Assert.Equal(1.0, fix.Between("SpiderChunkFraction", 1.5, 0, 1));
        Assert.Equal(0.0, fix.Between("WildHammockChance", -0.2, 0, 1));
        Assert.Equal(2.0, fix.AboveZero("NapSlotHours", 0, 2));
        Assert.Equal(1.0, fix.AboveZero("HammockBuildIntervalHours", -1, 1));
        Assert.Equal(30.0, fix.NotBelow("NapMaxMinutes", 10, "NapMinMinutes", 30));
        Assert.Equal(-1, fix.NotBelow("HammockBuildSearchUp", -4, "minus HammockBuildSearchDown", -1));
        Assert.Equal(7, fix.Warnings.Count);
        Assert.Equal("HammockBuildSearchRadius is -3 but must be at least 0; using 0.", fix.Warnings[0]);
        Assert.Equal("NapMaxMinutes is 10 but must not be below NapMinMinutes (30); using 30.", fix.Warnings[5]);
    }

    [Fact]
    public void NotANumberIsNeverFine()
    {
        SettingCorrector fix = new SettingCorrector();
        Assert.Equal(0.0, fix.AtLeast("A", double.NaN, 0));
        Assert.Equal(0.0, fix.Between("B", double.NaN, 0, 1));
        Assert.Equal(3.0, fix.AboveZero("C", double.NaN, 3));
        Assert.Equal(4.0, fix.NotBelow("D", double.NaN, "E", 4));
        Assert.Equal(4, fix.Warnings.Count);
    }
}
