using System.Collections.Generic;
using Vintagestory.API.Common.Entities;

namespace JumpingSpiders;

/// <summary>What one wild spawn attempt did (WildSpiderSpawner.TrySpawn). Plain data.</summary>
public class SpawnResult
{
    public SpawnOutcome Outcome;

    /// <summary>One or more sentences for people: why it failed, or what was spawned where.</summary>
    public string Message = "";

    /// <summary>The new spider, or null.</summary>
    public Entity Spider;

    /// <summary>For a newly placed hammock: blocks above the trunk base. -1 otherwise.</summary>
    public int HammockHeight = -1;

    /// <summary>True when the spider spawned asleep inside its hammock.</summary>
    public bool InHammock;

    /// <summary>True when this attempt searched the chunk's trees for a new hammock spot (the expensive part).</summary>
    public bool SearchedTrees;

    /// <summary>Grown tree trunks found in the chunk, and how many of them were tested for reachable spots.</summary>
    public int TrunksFound;
    public int TrunksTested;

    /// <summary>Hammock spots that were tested.</summary>
    public int SpotsTested;

    /// <summary>
    /// For a forced spawn (SpawnRequest.Forced): one sentence for each natural rule that would have refused this
    /// spawn and was skipped. Empty when a natural spawn could have happened here, and always for natural spawns.
    /// </summary>
    public List<string> BypassedRules = new List<string>();

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
