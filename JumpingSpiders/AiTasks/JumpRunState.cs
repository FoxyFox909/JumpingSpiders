namespace JumpingSpiders;

/// <summary>What a JumpRunner is doing (JumpRunner.State).</summary>
public enum JumpRunState
{
    /// <summary>Nothing started yet, or stopped.</summary>
    Idle,

    /// <summary>Crouching before the next jump.</summary>
    WindingUp,

    /// <summary>In the air.</summary>
    Flying,

    /// <summary>Landed on an intermediate perch, waiting a moment before the next jump.</summary>
    Pausing,

    /// <summary>Every jump of the plan landed where it should: the spider is at the goal.</summary>
    Reached,

    /// <summary>A jump ended badly (hit a wall, landed somewhere else, water, ...); see JumpRunner.LastEndReason.</summary>
    Failed
}
