using System.Collections.Generic;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace JumpingSpiders;

/// <summary>
/// The test functions HammockFinder hands to the vanilla POI registry. The registry calls a "matcher" for each point
/// of interest near the search centre and wants true for the ones that count. A matcher is a delegate (a reference
/// to a method); this small class holds the extra data a matcher needs (which spider is asking, where found hammocks
/// go) so we can pass a named method instead of an inline lambda.
///
/// Only HammockFinder uses this class.
/// </summary>
public class HammockPoiSearch
{
    /// <summary>The spider looking for a hammock (for MatchFreeHammock).</summary>
    public Entity ForSpider;

    /// <summary>Every hammock CollectHammock saw (for CollectHammock).</summary>
    public List<BlockEntitySilkHammock> Found = new List<BlockEntitySilkHammock>();

    /// <summary>Matcher: a hammock that is free for ForSpider.</summary>
    public bool MatchFreeHammock(IPointOfInterest poi)
    {
        BlockEntitySilkHammock hammock = poi as BlockEntitySilkHammock;
        if (hammock == null)
        {
            return false;
        }
        return hammock.IsFreeFor(ForSpider);
    }

    /// <summary>
    /// Matcher that records every hammock and always says "no". GetNearestPoi only remembers matches, so answering
    /// no makes it visit every POI within the radius. (The registry's WalkPois would be the obvious method, but in
    /// 1.22.7 it skips the last chunk column of the area, so it misses hammocks.)
    /// </summary>
    public bool CollectHammock(IPointOfInterest poi)
    {
        BlockEntitySilkHammock hammock = poi as BlockEntitySilkHammock;
        if (hammock != null)
        {
            Found.Add(hammock);
        }
        return false;
    }
}
