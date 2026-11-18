using System;
using System.Collections.Generic;

namespace JumpingSpiders;

/// <summary>
/// A table of model element names and how to resize each one, read from the entity JSON ("elementScalesByType" on
/// the client "jumpingspiders.spider" behavior). Pure logic, covered by unit tests.
///
/// Why: both adult sexes share one model file, and the female draws some parts differently (her pedipalps). Instead
/// of a second model, EntityBehaviorJumpingSpider resizes the listed elements of a copy of the shape just before the
/// game builds the entity's mesh (see its OnTesselation).
///
/// An entry is one of two kinds:
/// - One number: a uniform scale. The element grows or shrinks about its rotation origin (the joint it turns around)
///   through the element's ScaleX/Y/Z, so its child elements and its keyframe offsets are scaled with it. A uniform
///   scale looks the same whichever order the game combines rotation and scale in.
/// - Three numbers [x, y, z]: per-axis factors for the element's own box, in the element's own axes (the X, Y and Z
///   of its size in the model editor). The box itself is reshaped (see BoxReshape), not the element's scale: a
///   non-uniform scale would skew any child that an animation turns (the game applies a parent's scale after the
///   child's rotation). Children keep their own size and are moved so they stay attached; list a child too to
///   reshape it.
/// One name has one entry; a later entry for the same name replaces the earlier one, whatever its kind.
/// </summary>
public class ElementScales
{
    /// <summary>
    /// Smallest scale or factor accepted (a fraction of the modelled size). At 0 the element would collapse and the
    /// game could not invert its matrix; a negative one would turn it inside out. Not a design knob, only a sanity bound.
    /// </summary>
    public const double MinScale = 0.01;

    /// <summary>Largest scale or factor accepted (times the modelled size). Not a design knob, only a sanity bound.</summary>
    public const double MaxScale = 10.0;

    /// <summary>Number of values in a per-axis entry: X, Y and Z.</summary>
    public const int AxisCount = 3;

    // Element names are compared exactly, as the game does when it matches animation keyframes to elements.
    private readonly Dictionary<string, double> scaleByName = new Dictionary<string, double>(StringComparer.Ordinal);

    // Per-axis box factors, each an array of AxisCount values. A name is never in both dictionaries.
    private readonly Dictionary<string, double[]> boxFactorsByName = new Dictionary<string, double[]>(StringComparer.Ordinal);

    /// <summary>True when the scale is a finite number from MinScale to MaxScale.</summary>
    public static bool IsUsableScale(double scale)
    {
        if (double.IsNaN(scale) || double.IsInfinity(scale))
        {
            return false;
        }
        return scale >= MinScale && scale <= MaxScale;
    }

    /// <summary>
    /// Adds a uniform scale. Returns false (and stores nothing) for an empty name or an unusable scale, so the caller
    /// can warn about it. A scale of exactly 1 is accepted but not stored: it would change nothing.
    /// A second entry for the same name replaces the first.
    /// </summary>
    public bool Set(string elementName, double scale)
    {
        if (string.IsNullOrEmpty(elementName) || !IsUsableScale(scale))
        {
            return false;
        }
        boxFactorsByName.Remove(elementName);
        if (scale == 1.0)
        {
            scaleByName.Remove(elementName);
            return true;
        }
        scaleByName[elementName] = scale;
        return true;
    }

    /// <summary>
    /// Adds per-axis box factors, in the element's own axes. Same rules as Set: false for an empty name or any
    /// unusable factor; all three exactly 1 is accepted but not stored; a later entry replaces an earlier one.
    /// </summary>
    public bool SetBoxFactors(string elementName, double factorX, double factorY, double factorZ)
    {
        if (string.IsNullOrEmpty(elementName)
            || !IsUsableScale(factorX) || !IsUsableScale(factorY) || !IsUsableScale(factorZ))
        {
            return false;
        }
        scaleByName.Remove(elementName);
        if (factorX == 1.0 && factorY == 1.0 && factorZ == 1.0)
        {
            boxFactorsByName.Remove(elementName);
            return true;
        }
        boxFactorsByName[elementName] = new double[] { factorX, factorY, factorZ };
        return true;
    }

    /// <summary>
    /// Adds an entry from the numbers a JSON value held: one number is a uniform scale (Set), three are per-axis box
    /// factors (SetBoxFactors). Any other count, or a null array, is refused (returns false).
    /// </summary>
    public bool SetFromNumbers(string elementName, double[] numbers)
    {
        if (numbers == null)
        {
            return false;
        }
        if (numbers.Length == 1)
        {
            return Set(elementName, numbers[0]);
        }
        if (numbers.Length == AxisCount)
        {
            return SetBoxFactors(elementName, numbers[0], numbers[1], numbers[2]);
        }
        return false;
    }

    /// <summary>True when no element needs changing, so the shape can be used as it is (no copy needed).</summary>
    public bool IsEmpty
    {
        get { return scaleByName.Count == 0 && boxFactorsByName.Count == 0; }
    }

    /// <summary>Number of elements that get changed, of both kinds.</summary>
    public int Count
    {
        get { return scaleByName.Count + boxFactorsByName.Count; }
    }

    /// <summary>The uniform scale for this element name, or false when it has none (it may have box factors).</summary>
    public bool TryGetScale(string elementName, out double scale)
    {
        if (elementName == null)
        {
            scale = 1.0;
            return false;
        }
        return scaleByName.TryGetValue(elementName, out scale);
    }

    /// <summary>
    /// The per-axis box factors for this element name, as a new array of X, Y, Z (the caller may keep or change it),
    /// or false when it has none (it may have a uniform scale).
    /// </summary>
    public bool TryGetBoxFactors(string elementName, out double[] factors)
    {
        factors = null;
        if (elementName == null)
        {
            return false;
        }
        double[] stored;
        if (!boxFactorsByName.TryGetValue(elementName, out stored))
        {
            return false;
        }
        factors = new double[] { stored[0], stored[1], stored[2] };
        return true;
    }

    /// <summary>
    /// The names in this table that are not among foundNames, in no particular order. Used to warn when the model no
    /// longer has an element the JSON names (for example after renaming it in the model editor).
    /// </summary>
    public List<string> MissingFrom(ICollection<string> foundNames)
    {
        List<string> missing = new List<string>();
        AddMissing(scaleByName.Keys, foundNames, missing);
        AddMissing(boxFactorsByName.Keys, foundNames, missing);
        return missing;
    }

    private static void AddMissing(IEnumerable<string> names, ICollection<string> foundNames, List<string> missing)
    {
        foreach (string name in names)
        {
            if (foundNames == null || !foundNames.Contains(name))
            {
                missing.Add(name);
            }
        }
    }

    /// <summary>
    /// The element's new scale on one axis: the scale the model gives it (1 unless set in the model editor) times
    /// the factor from the JSON.
    /// </summary>
    public static double Combine(double modelScale, double factor)
    {
        return modelScale * factor;
    }
}
