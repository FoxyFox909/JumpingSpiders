using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace JumpingSpiders;

/// <summary>What one wild spawn attempt did (WildSpiderSpawner.TrySpawn). Plain data.</summary>
public class SpawnResult
{
    public SpawnOutcome Outcome;

    /// <summary>One or more sentences for people: why it failed, or what was spawned where.</summary>
    public string Message = "";

    /// <summary>The new spider, or null.</summary>
    public Entity Spider;

    /// <summary>The spider's home hammock, or null when it has none.</summary>
    public BlockPos Hammock;

    /// <summary>True when the home hammock already existed (the chunk's record, or one found nearby).</summary>
    public bool HammockReused;

    /// <summary>True when this attempt placed a new hammock on a tree.</summary>
    public bool HammockPlaced;

    /// <summary>For a newly placed hammock: blocks above the trunk base. -1 otherwise.</summary>
    public int HammockHeight = -1;

    /// <summary>True when the spider wanted a hammock but the chunk has no usable tree (searched now or recently).</summary>
    public bool NoUsableTree;

    /// <summary>True when the spider spawned asleep inside its hammock.</summary>
    public bool InHammock;

    /// <summary>True when this attempt searched the chunk's trees for a new hammock spot (the expensive part).</summary>
    public bool SearchedTrees;

    /// <summary>Grown tree trunks found in the chunk, and how many of them were tested for reachable spots.</summary>
    public int TrunksFound;
    public int TrunksTested;

    /// <summary>Hammock spots that were tested, and how many of them a spider could jump into.</summary>
    public int SpotsTested;
    public int SpotsReachable;

    /// <summary>Adds a sentence to Message.</summary>
    public void Note(string sentence)
    {
        if (Message.Length > 0)
        {
            Message = Message + " ";
        }
        Message = Message + sentence;
    }

    /// <summary>Sets the outcome and adds its explanation; returns this, so a failing check can "return result.Fail(...)".</summary>
    public SpawnResult Fail(SpawnOutcome outcome, string sentence)
    {
        Outcome = outcome;
        Note(sentence);
        return this;
    }
}
