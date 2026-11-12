using Xunit;

namespace JumpingSpiders.Tests;

public class SaturationGuardTests
{
    [Fact]
    public void FoodFromTroughOrBaitIsTakenAway()
    {
        // Credited 1 portion for a real meal, then the vanilla eat task added 1 from a trap or trough.
        Assert.True(SaturationGuard.NeedsCorrection(2, 1));
        Assert.Equal(1f, SaturationGuard.AllowedSaturation(2, 1));
    }

    [Fact]
    public void NoCreditMeansNoSaturation()
    {
        Assert.Equal(0f, SaturationGuard.AllowedSaturation(1, 0));
    }

    [Fact]
    public void BreedingUsesUpSaturationAndCreditFollows()
    {
        // Vanilla breeding subtracted 2 portions; the new credited amount is what is left.
        Assert.False(SaturationGuard.NeedsCorrection(0, 2));
        Assert.Equal(0f, SaturationGuard.AllowedSaturation(0, 2));
    }

    [Fact]
    public void UsedUpCreditCannotBeRefilledByBait()
    {
        // Credit 2, breeding uses 2 (credit follows to 0), then bait adds 1: taken away again.
        float credited = SaturationGuard.AllowedSaturation(0, 2);
        Assert.Equal(0f, credited);
        Assert.Equal(0f, SaturationGuard.AllowedSaturation(1, credited));
    }

    [Fact]
    public void MatchingValuesAreLeftAlone()
    {
        Assert.False(SaturationGuard.NeedsCorrection(3, 3));
        Assert.False(SaturationGuard.NeedsCorrection(3.0005f, 3));
        Assert.Equal(3f, SaturationGuard.AllowedSaturation(3, 3));
    }
}
