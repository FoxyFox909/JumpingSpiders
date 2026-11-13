namespace JumpingSpiders;

/// <summary>The steps of one pounce by AiTaskSpiderJumpAttack, in order.</summary>
public enum AttackPhase
{
    /// <summary>Not attacking.</summary>
    None,

    /// <summary>Walking (stalking) towards the target until it is within one jump.</summary>
    Approach,

    /// <summary>Standing still, front legs raised, turning to keep the target in view.</summary>
    WindUp,

    /// <summary>In the air towards the target; biting it if the spider touches it.</summary>
    Leap
}
