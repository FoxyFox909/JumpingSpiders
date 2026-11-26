using System.Collections.Generic;
using Xunit;

namespace JumpingSpiders.Tests;

public class ElementScalesTests
{
    [Fact]
    public void StoresAScaleByExactName()
    {
        ElementScales scales = new ElementScales();
        Assert.True(scales.Set("PedipalpL", 0.6));

        double scale;
        Assert.True(scales.TryGetScale("PedipalpL", out scale));
        Assert.Equal(0.6, scale);
        // The game matches element names exactly, so this table does too.
        Assert.False(scales.TryGetScale("pedipalpl", out scale));
        Assert.False(scales.TryGetScale("PedipalpLB", out scale));
        Assert.False(scales.TryGetScale(null, out scale));
    }

    [Fact]
    public void ScaleOfOneChangesNothingAndIsNotStored()
    {
        ElementScales scales = new ElementScales();
        Assert.True(scales.Set("PedipalpL", 1.0));
        Assert.True(scales.IsEmpty);
        Assert.Equal(0, scales.Count);
    }

    [Fact]
    public void LaterEntryReplacesEarlierOne()
    {
        ElementScales scales = new ElementScales();
        scales.Set("PedipalpL", 0.6);
        scales.Set("PedipalpL", 0.8);
        double scale;
        Assert.True(scales.TryGetScale("PedipalpL", out scale));
        Assert.Equal(0.8, scale);

        // Setting it back to 1 removes it.
        scales.Set("PedipalpL", 1.0);
        Assert.False(scales.TryGetScale("PedipalpL", out scale));
    }

    [Fact]
    public void UnusableEntriesAreRefused()
    {
        ElementScales scales = new ElementScales();
        Assert.False(scales.Set("PedipalpL", 0));
        Assert.False(scales.Set("PedipalpL", -0.6));
        Assert.False(scales.Set("PedipalpL", double.NaN));
        Assert.False(scales.Set("PedipalpL", double.PositiveInfinity));
        Assert.False(scales.Set("PedipalpL", ElementScales.MaxScale * 2));
        Assert.False(scales.Set("", 0.6));
        Assert.False(scales.Set(null, 0.6));
        Assert.True(scales.IsEmpty);

        Assert.True(scales.Set("PedipalpL", ElementScales.MinScale));
        Assert.True(scales.Set("PedipalpR", ElementScales.MaxScale));
        Assert.Equal(2, scales.Count);
    }

    [Fact]
    public void ReportsNamesTheModelDoesNotHave()
    {
        ElementScales scales = new ElementScales();
        scales.Set("PedipalpL", 0.6);
        scales.Set("PedipalpR", 0.6);

        HashSet<string> found = new HashSet<string>();
        found.Add("PedipalpL");
        found.Add("Cephalothorax");

        List<string> missing = scales.MissingFrom(found);
        Assert.Single(missing);
        Assert.Equal("PedipalpR", missing[0]);

        Assert.Equal(2, scales.MissingFrom(null).Count);

        found.Add("PedipalpR");
        Assert.Empty(scales.MissingFrom(found));
    }

    [Fact]
    public void OneNumberIsAUniformScaleThreeAreBoxFactors()
    {
        ElementScales scales = new ElementScales();
        Assert.True(scales.SetFromNumbers("PedipalpL", new double[] { 0.6 }));
        Assert.True(scales.SetFromNumbers("PedipalpLB", new double[] { 1, 0.6, 0.5 }));

        double scale;
        double[] factors;
        Assert.True(scales.TryGetScale("PedipalpL", out scale));
        Assert.Equal(0.6, scale);
        Assert.False(scales.TryGetBoxFactors("PedipalpL", out factors));
        Assert.Null(factors);

        Assert.False(scales.TryGetScale("PedipalpLB", out scale));
        Assert.True(scales.TryGetBoxFactors("PedipalpLB", out factors));
        Assert.Equal(new double[] { 1, 0.6, 0.5 }, factors);
        Assert.Equal(2, scales.Count);
    }

    [Fact]
    public void OtherCountsOfNumbersAreRefused()
    {
        ElementScales scales = new ElementScales();
        Assert.False(scales.SetFromNumbers("PedipalpL", null));
        Assert.False(scales.SetFromNumbers("PedipalpL", new double[0]));
        Assert.False(scales.SetFromNumbers("PedipalpL", new double[] { 1, 0.6 }));
        Assert.False(scales.SetFromNumbers("PedipalpL", new double[] { 1, 0.6, 0.6, 0.6 }));
        Assert.True(scales.IsEmpty);
    }

    [Fact]
    public void BoxFactorsAreCheckedLikeScales()
    {
        ElementScales scales = new ElementScales();
        Assert.False(scales.SetBoxFactors("PedipalpL", 1, 0, 0.6));
        Assert.False(scales.SetBoxFactors("PedipalpL", 1, 0.6, -0.6));
        Assert.False(scales.SetBoxFactors("PedipalpL", double.NaN, 0.6, 0.6));
        Assert.False(scales.SetBoxFactors("PedipalpL", 1, ElementScales.MaxScale * 2, 0.6));
        Assert.False(scales.SetBoxFactors("", 1, 0.6, 0.6));
        Assert.False(scales.SetBoxFactors(null, 1, 0.6, 0.6));
        Assert.True(scales.IsEmpty);

        // All three 1: accepted, changes nothing, not stored.
        Assert.True(scales.SetBoxFactors("PedipalpL", 1, 1, 1));
        Assert.True(scales.IsEmpty);
    }

    [Fact]
    public void LaterEntryReplacesEarlierOneOfTheOtherKind()
    {
        ElementScales scales = new ElementScales();
        double scale;
        double[] factors;

        scales.Set("PedipalpL", 0.6);
        scales.SetBoxFactors("PedipalpL", 1, 0.6, 0.6);
        Assert.False(scales.TryGetScale("PedipalpL", out scale));
        Assert.True(scales.TryGetBoxFactors("PedipalpL", out factors));
        Assert.Equal(1, scales.Count);

        scales.Set("PedipalpL", 0.8);
        Assert.True(scales.TryGetScale("PedipalpL", out scale));
        Assert.Equal(0.8, scale);
        Assert.False(scales.TryGetBoxFactors("PedipalpL", out factors));
        Assert.Equal(1, scales.Count);

        // A neutral entry of either kind clears the name.
        scales.SetBoxFactors("PedipalpL", 1, 0.6, 0.6);
        scales.Set("PedipalpL", 1.0);
        Assert.True(scales.IsEmpty);
    }

    [Fact]
    public void BoxFactorsComeBackAsACopy()
    {
        ElementScales scales = new ElementScales();
        scales.SetBoxFactors("PedipalpL", 1, 0.6, 0.6);
        double[] factors;
        scales.TryGetBoxFactors("PedipalpL", out factors);
        factors[1] = 5;
        scales.TryGetBoxFactors("PedipalpL", out factors);
        Assert.Equal(0.6, factors[1]);
    }

    [Fact]
    public void ReportsMissingNamesOfBothKinds()
    {
        ElementScales scales = new ElementScales();
        scales.Set("PedipalpL", 0.6);
        scales.SetBoxFactors("PedipalpLB", 1, 0.6, 0.6);
        HashSet<string> found = new HashSet<string>();
        found.Add("PedipalpL");
        List<string> missing = scales.MissingFrom(found);
        Assert.Single(missing);
        Assert.Equal("PedipalpLB", missing[0]);
        Assert.Equal(2, scales.MissingFrom(null).Count);
    }

    [Fact]
    public void FactorMultipliesTheModelsOwnScale()
    {
        Assert.Equal(0.6, ElementScales.Combine(1.0, 0.6), 10);
        Assert.Equal(1.2, ElementScales.Combine(2.0, 0.6), 10);
    }
}
