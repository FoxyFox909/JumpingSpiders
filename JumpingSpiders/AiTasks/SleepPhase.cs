namespace JumpingSpiders;

/// <summary>The steps of one night for AiTaskSpiderSleep.</summary>
public enum SleepPhase
{
    /// <summary>Not sleeping.</summary>
    None,

    /// <summary>Jumping up to the hammock along a planned chain of jumps.</summary>
    Travelling,

    /// <summary>Walking to a spot from which the hammock can be reached by jumping.</summary>
    Walking,

    /// <summary>Asleep, in the hammock or on the spot.</summary>
    Asleep,

    /// <summary>Morning: hopping out of the hammock.</summary>
    Leaving
}
