namespace JumpingSpiders;

/// <summary>What a spider chunk's saved record knows about its wild hammock (see WildHammockRecord).</summary>
public enum WildHammockState
{
    /// <summary>Nobody has looked for a hammock spot in this spider chunk yet (or the last hammock is gone).</summary>
    Unknown = 0,

    /// <summary>The chunk's hammock is at X, Y, Z. Checked against the world each time it is used.</summary>
    HasHammock = 1,

    /// <summary>A search found no usable tree; do not search again before the retry time.</summary>
    NoSite = 2
}
