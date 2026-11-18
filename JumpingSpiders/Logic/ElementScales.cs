using System;
using System.Collections.Generic;

namespace JumpingSpiders;

/// <summary>
/// A table of model element names and how much to scale each one, read from the entity JSON
/// ("elementScalesByType" on the client "jumpingspiders.spider" behavior). Pure logic, covered by unit tests.
///
/// Why: both adult sexes share one model file, and the female draws some parts smaller (her pedipalps). Instead of
/// a second model, EntityBehaviorJumpingSpider scales the listed elements of a copy of the shape just before the game
/// builds the entity's mesh (see its OnTesselation). A scaled element shrinks or grows about its rotation origin
/// (the joint it turns around), and its child elements come along.
///
/// One number per element, applied to X, Y and Z alike. A uniform scale looks the same whichever order the game
/// combines rotation and scale in (the mesh builder and the two animation versions do not all use the same order).
/// </summary>
public class ElementScales
{
    /// <summary>
    /// Smallest scale accepted (a fraction of the modelled size). At 0 the element would collapse and the game
    /// could not invert its matrix; a negative scale would turn it inside out. Not a design knob, only a sanity bound.
    /// </summary>
    public const double MinScale = 0.01;

    /// <summary>Largest scale accepted (times the modelled size). Not a design knob, only a sanity bound.</summary>
    public const double MaxScale = 10.0;

    // Element names are compared exactly, as the game does when it matches animation keyframes to elements.
    private readonly Dictionary<string, double> scaleByName = new Dictionary<string, double>(StringComparer.Ordinal);

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
    /// Adds one entry. Returns false (and stores nothing) for an empty name or an unusable scale, so the caller can
    /// warn about it. A scale of exactly 1 is accepted but not stored: it would change nothing.
    /// A second entry for the same name replaces the first.
    /// </summary>
    public bool Set(string elementName, double scale)
    {
        if (string.IsNullOrEmpty(elementName) || !IsUsableScale(scale))
        {
            return false;
        }
        if (scale == 1.0)
        {
            scaleByName.Remove(elementName);
            return true;
        }
        scaleByName[elementName] = scale;
        return true;
    }

    /// <summary>True when no element needs scaling, so the shape can be used as it is (no copy needed).</summary>
    public bool IsEmpty
    {
        get { return scaleByName.Count == 0; }
    }

    /// <summary>Number of elements that get scaled.</summary>
    public int Count
    {
        get { return scaleByName.Count; }
    }

    /// <summary>The scale for this element name, or false when the element is drawn as modelled.</summary>
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
    /// The names in this table that are not among foundNames, in no particular order. Used to warn when the model no
    /// longer has an element the JSON names (for example after renaming it in the model editor).
    /// </summary>
    public List<string> MissingFrom(ICollection<string> foundNames)
    {
        List<string> missing = new List<string>();
        foreach (string name in scaleByName.Keys)
        {
            if (foundNames == null || !foundNames.Contains(name))
            {
                missing.Add(name);
            }
        }
        return missing;
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
