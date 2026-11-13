using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace JumpingSpiders;

/// <summary>
/// Sleeping at night, JSON code "jumpingspiders.sleep": when the sun is down (SpiderAttributes.IsNight) the spider
/// goes to a silk hammock, jumping up to it (one or two jumps), sleeps in it until morning and hops out. With no
/// usable hammock it sleeps where it stands.
///
/// (How the game drives a task: see AiTaskSpiderJumpAttack.)
///
/// Staying up: a domesticated spider does not go to bed right at dusk but stays up for its daily curiosity times
/// Config.CuriosityStayUpHoursMax hours (SpiderAttributes.IsStayingUpAfterDusk). Meanwhile this task simply does not
/// start, so the lower-priority tasks run: exploring (at the night curiosity) and hunting when hungry. This only
/// delays going to bed: a spider saved asleep goes straight back to sleep, and one that is asleep is never woken by
/// it. Wild spiders go to bed at dusk.
///
/// Which hammock (design decisions 9 and 10):
/// - Wild spiders have one home hammock (SpiderAttributes.GetHomeHammock). It is forgotten when it is gone or
///   farther than Config.HomeHammockForgetDistance; then the nearest free hammock within
///   Config.HammockSleepSearchRange becomes the new home. If the home is taken tonight, another free one is used
///   for the night without changing the home.
/// - Domesticated spiders use the nearest free hammock within Config.HammockSleepSearchRange.
/// - A spider already standing in a hammock sleeps in that one. This is also how a spider resumes after a server
///   restart: its "sleeping" flag is saved, so the task starts at once (skipping its cooldown) and claims the
///   hammock again (hammock occupancy is kept in memory only).
/// The hammock is claimed (TryOccupy) as soon as it is chosen, so two spiders never head for the same one.
///
/// Getting there (SleepPhase):
/// - Travelling: JumpPlanner plans up to Config.MaxPlannedJumps jumps into the hammock's cell; JumpRunner does them,
///   each with a short wind-up.
/// - Walking: when no plan exists from here, LaunchSpotFinder finds a spot from which one does, and the game's
///   pathfinder walks there; then it plans again.
/// - It gives up on the hammock (and sleeps on the spot) after maxTravelAttempts plans, after travelTimeoutMs, or
///   when there is no launch spot or no walking path.
///
/// Waking up: in the morning (it then hops out with one jump to a random spot within leaveJumpRadius), when the
/// hammock is removed (the hammock clears the sleeping flag), or when hurt. While asleep the task reports
/// asleepPriorityForCancel, so a player walking up does not scare a sleeping wild spider away (it can be caught,
/// and breaking its hammock angers it, design decision 11), but being attacked does wake it.
/// After the task ends, its cooldown (vanilla mincooldown/maxcooldown, ms) passes before it tries again tonight.
/// </summary>
public class AiTaskSpiderSleep : AiTaskBase
{
    /// <summary>Name used in the debug log.</summary>
    public const string LogName = "sleep";

    /// <summary>The pathfinder stops walking this close to the launch spot, in blocks.</summary>
    public const float WalkStopDistance = 0.3f;

    /// <summary>
    /// Launch spots must be at most this many blocks above the spider's feet: the game's pathfinder only plans steps
    /// of one block up.
    /// </summary>
    public const int WalkStepUpBlocks = 1;

    /// <summary>
    /// Launch spots may be at most this many blocks below the spider's feet. Spiders take no fall damage, and for such
    /// creatures the pathfinder plans drops of up to 12 blocks; 8 is two full jumps up again.
    /// </summary>
    public const int WalkStepDownBlocks = 8;

    /// <summary>A spider this close (blocks, horizontally) to the hammock's sleep position is moved onto it exactly.</summary>
    public const double SnapDistance = 0.75;

    // ---- Knobs from the task's JSON entry (see the constructor for units and defaults) ----
    private readonly int checkIntervalMs;
    private readonly float asleepPriorityForCancel;
    private readonly string sleepAnimation;
    private readonly string walkAnimation;
    private readonly float moveSpeed;
    private readonly int launchSpotSearchRadius;
    private readonly int maxLaunchSpotPlanChecks;
    private readonly int maxTravelAttempts;
    private readonly int travelTimeoutMs;
    private readonly float leaveJumpRadius;
    private readonly float leaveHeightBias;
    private readonly bool wakeOnHurt;

    // ---- State of the current night ----

    /// <summary>What the task is doing now (read by debug tools).</summary>
    public SleepPhase Phase = SleepPhase.None;

    /// <summary>The hammock this spider is going to or sleeping in, or null (sleeping on the spot).</summary>
    public BlockPos HammockPos;

    private readonly JumpRunner runner;
    private long nextStartCheckMs;
    private long nextWakeCheckMs;
    private long travelDeadlineMs;
    private int travelAttempts;
    private bool walking;
    private bool walkArrived;
    private bool walkFailed;
    private bool hurt;
    private bool stayingUp;    // only so the debug log says when staying up begins and ends

    public AiTaskSpiderSleep(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig)
        : base(entity, taskConfig, aiConfig)
    {
        // Also read by the base class from the same entry: priority, mincooldown/maxcooldown (ms before it tries
        // again after waking or giving up).

        // Milliseconds: how often it checks whether it is night (to start) or morning (to wake).
        checkIntervalMs = taskConfig["checkIntervalMs"].AsInt(1000);
        // While asleep, only tasks with a higher priority than this can wake it (2.6: fleeing from an attacker, 2.8,
        // and attacking, 3.0, can; fleeing from a player walking up, 2.5, cannot).
        asleepPriorityForCancel = taskConfig["asleepPriorityForCancel"].AsFloat(2.6f);
        // Animation codes from the entity JSON; "" = none.
        sleepAnimation = SpiderTaskUtil.ReadAnimation(taskConfig, "sleepAnimation", "sleep");
        walkAnimation = SpiderTaskUtil.ReadAnimation(taskConfig, "walkAnimation", "walk");
        // Walking speed to a launch spot (vanilla movespeed units).
        moveSpeed = taskConfig["movespeed"].AsFloat(0.02f);
        // Blocks: how far around the hammock (horizontally) it looks for a spot to jump up from.
        launchSpotSearchRadius = taskConfig["launchSpotSearchRadius"].AsInt(6);
        // How many candidate launch spots are checked with the planner per attempt (each check is one full plan, about 1 ms).
        maxLaunchSpotPlanChecks = taskConfig["maxLaunchSpotPlanChecks"].AsInt(8);
        // How many times per night it plans its way to the hammock before giving up and sleeping on the spot.
        maxTravelAttempts = taskConfig["maxTravelAttempts"].AsInt(3);
        // Milliseconds: longest time to get to the hammock before giving up and sleeping on the spot.
        travelTimeoutMs = taskConfig["travelTimeoutMs"].AsInt(30000);
        // Blocks: in the morning it hops out of the hammock to a random spot this close; 0 = it just walks off later.
        leaveJumpRadius = taskConfig["leaveJumpRadius"].AsFloat(4);
        // Preference for high spots when hopping out in the morning (0 = none, so usually down to the ground).
        leaveHeightBias = taskConfig["leaveHeightBias"].AsFloat(0);
        // true = getting hurt wakes it (it then flees or fights through the other tasks).
        wakeOnHurt = taskConfig["wakeOnHurt"].AsBool(true);

        runner = new JumpRunner(entity);
        // Milliseconds: the short crouch before each jump up to the hammock, a random time between these two.
        runner.WindUpMinSeconds = taskConfig["windUpMinMs"].AsInt(200) / 1000f;
        runner.WindUpMaxSeconds = taskConfig["windUpMaxMs"].AsInt(400) / 1000f;
        // Milliseconds: rest on the intermediate perch between the two jumps.
        runner.PauseSeconds = taskConfig["pauseBetweenJumpsMs"].AsInt(300) / 1000f;
        // Degrees per second: how fast it turns towards the jump direction during the crouch.
        runner.TurnSpeedRadPerSec = taskConfig["turnSpeedDegPerSec"].AsFloat(360) * (float)Math.PI / 180f;
        runner.WindUpAnimation = SpiderTaskUtil.ReadAnimation(taskConfig, "windUpAnimation", "jumpwindup");
        runner.JumpAnimation = SpiderTaskUtil.ReadAnimation(taskConfig, "jumpAnimation", "jump");
    }

    /// <summary>Uninterruptible in the air; hard to interrupt while asleep (see asleepPriorityForCancel).</summary>
    public override float PriorityForCancel
    {
        get
        {
            if (runner.IsFlying())
            {
                return SpiderTaskUtil.InFlightPriorityForCancel;
            }
            if (Phase == SleepPhase.Asleep)
            {
                return asleepPriorityForCancel;
            }
            return base.PriorityForCancel;
        }
    }

    public override bool ShouldExecute()
    {
        long now = entity.World.ElapsedMilliseconds;
        if (now < nextStartCheckMs)
        {
            return false;
        }
        nextStartCheckMs = now + checkIntervalMs;

        bool resuming = SpiderAttributes.IsSleeping(entity);
        if (!SpiderAttributes.IsNight(entity.World, entity.Pos.XYZ))
        {
            if (resuming)
            {
                // Saved as asleep, but it is day now (the server was off, or the spider was out of everyone's range
                // at dawn): just be awake.
                SpiderAttributes.SetSleeping(entity, false);
                SpiderTaskUtil.Log(entity, LogName, "was saved asleep, but it is day: awake");
            }
            return false;
        }
        // A spider that was asleep when it was saved goes straight back to sleep, without waiting for the cooldown.
        if (!resuming && IsOnCooldown())
        {
            return false;
        }
        if (!PreconditionsSatisfied())
        {
            return false;
        }
        // A spider saved asleep may not be standing yet: physics has not run since it was loaded.
        if (entity.Swimming || entity.FeetInLiquid || (!resuming && !entity.OnGround))
        {
            return false;
        }
        // Checked last because it looks back through the evening for the sunset (cheap, but not free).
        if (!resuming && SpiderAttributes.IsStayingUpAfterDusk(entity))
        {
            if (!stayingUp)
            {
                stayingUp = true;
                SpiderTaskUtil.Log(entity, LogName, "stays up after dusk, for up to "
                    + SpiderAttributes.GetStayUpHours(entity).ToString("0.0") + " h (curiosity "
                    + SpiderAttributes.GetDailyCuriosity(entity).ToString("0.00") + ")");
            }
            return false;
        }
        if (stayingUp)
        {
            stayingUp = false;
            SpiderTaskUtil.Log(entity, LogName, "has stayed up long enough: goes to bed");
        }
        return true;
    }

    public override void StartExecute()
    {
        base.StartExecute();
        hurt = false;
        travelAttempts = 0;
        walking = false;
        HammockPos = null;
        Phase = SleepPhase.None;
        nextWakeCheckMs = entity.World.ElapsedMilliseconds + checkIntervalMs;
        travelDeadlineMs = entity.World.ElapsedMilliseconds + travelTimeoutMs;

        // Already standing in a hammock (also the case after a restart): sleep right here.
        BlockPos here = SpiderTaskUtil.FeetCell(entity);
        BlockEntitySilkHammock hammockHere = HammockFinder.GetHammockAt(entity.World, here);
        if (hammockHere != null && hammockHere.TryOccupy(entity))
        {
            HammockPos = here;
            FallAsleep();
            return;
        }

        HammockPos = ChooseHammock();
        if (HammockPos == null)
        {
            FallAsleep();
            return;
        }
        StartTravel();
    }

    public override bool ContinueExecute(float dt)
    {
        long now = entity.World.ElapsedMilliseconds;

        if (hurt && wakeOnHurt)
        {
            SpiderTaskUtil.Log(entity, LogName, "woken by getting hurt");
            return false;
        }
        if (HammockPos != null && Phase != SleepPhase.Leaving && HammockFinder.GetHammockAt(entity.World, HammockPos) == null)
        {
            SpiderTaskUtil.Log(entity, LogName, "hammock at " + HammockPos + " is gone (" + Phase + ")");
            HammockPos = null;
            return false;
        }
        if (Phase == SleepPhase.Asleep && !SpiderAttributes.IsSleeping(entity))
        {
            SpiderTaskUtil.Log(entity, LogName, "woken up (sleeping flag cleared)");
            return false;
        }

        if (now >= nextWakeCheckMs)
        {
            nextWakeCheckMs = now + checkIntervalMs;
            if (Phase != SleepPhase.Leaving && !SpiderAttributes.IsNight(entity.World, entity.Pos.XYZ))
            {
                if (Phase != SleepPhase.Asleep)
                {
                    SpiderTaskUtil.Log(entity, LogName, "morning came before it got to sleep");
                    return false;
                }
                BeginLeaving();
            }
        }

        if (Phase == SleepPhase.Travelling)
        {
            return ContinueTravelling(dt, now);
        }
        if (Phase == SleepPhase.Walking)
        {
            return ContinueWalking(now);
        }
        if (Phase == SleepPhase.Asleep)
        {
            SpiderTaskUtil.StopWalking(entity);
            return true;
        }
        if (Phase == SleepPhase.Leaving)
        {
            if (runner.IsBusy())
            {
                runner.Tick(dt);
                return runner.IsBusy();
            }
            return false;
        }
        return false;
    }

    private bool ContinueTravelling(float dt, long now)
    {
        if (runner.IsBusy())
        {
            runner.Tick(dt);
            if (runner.IsBusy())
            {
                return true;
            }
            SpiderTaskUtil.Log(entity, LogName, "jumps " + runner.State + " (" + runner.StepsDone + " landed, last "
                + runner.LastEndReason + ") at " + SpiderTaskUtil.Format(entity.Pos.X, entity.Pos.Y, entity.Pos.Z));
        }
        if (runner.State == JumpRunState.Reached && IsInHammockCell())
        {
            FallAsleep();
            return true;
        }
        // A jump went wrong: wait until it is back on its feet, then plan again (bounded by maxTravelAttempts).
        if (!entity.OnGround)
        {
            return true;
        }
        if (now > travelDeadlineMs)
        {
            GiveUpHammock("took too long");
            return true;
        }
        StartTravel();
        return true;
    }

    private bool ContinueWalking(long now)
    {
        if (now > travelDeadlineMs)
        {
            GiveUpHammock("took too long");
            return true;
        }
        if (walkFailed)
        {
            GiveUpHammock("could not walk to the launch spot");
            return true;
        }
        if (walkArrived && entity.OnGround)
        {
            StopWalkingAnimation();
            StartTravel();
        }
        return true;
    }

    public override void OnEntityHurt(DamageSource source, float damage)
    {
        base.OnEntityHurt(source, damage);
        if (Phase != SleepPhase.None)
        {
            hurt = true;
        }
    }

    public override void OnEntityDespawn(EntityDespawnData reason)
    {
        // Unloaded or removed: free the hammock for others. The saved sleeping flag stays, so after a reload or a
        // restart the spider resumes sleeping.
        ReleaseHammock();
        base.OnEntityDespawn(reason);
    }

    public override void FinishExecute(bool cancelled)
    {
        runner.Stop();
        pathTraverser.Stop();
        StopWalkingAnimation();
        WakeUp();
        if (cancelled)
        {
            SpiderTaskUtil.Log(entity, LogName, "interrupted (" + Phase + ")");
        }
        HammockPos = null;
        Phase = SleepPhase.None;
        base.FinishExecute(cancelled);
    }

    // ---- Choosing and reaching the hammock ----

    // The hammock to sleep in tonight, already claimed for this spider, or null (sleep on the spot).
    private BlockPos ChooseHammock()
    {
        JumpingSpidersConfig config = JumpingSpidersModSystem.Config;
        IWorldAccessor world = entity.World;
        bool wild = !SpiderAttributes.IsDomesticated(entity);
        BlockPos home = null;

        if (wild)
        {
            home = SpiderAttributes.GetHomeHammock(entity);
            if (home != null)
            {
                double distance = entity.Pos.XYZ.DistanceTo(HammockFinder.CellCenter(home));
                bool loaded = world.BlockAccessor.GetChunkAtBlockPos(home) != null;
                BlockEntitySilkHammock homeHammock = HammockFinder.GetHammockAt(world, home);
                if (distance > config.HomeHammockForgetDistance || (loaded && homeHammock == null))
                {
                    SpiderTaskUtil.Log(entity, LogName, "forgets its home hammock at " + home + " (distance "
                        + distance.ToString("0.0") + ", still there: " + (homeHammock != null) + ")");
                    SpiderAttributes.ClearHomeHammock(entity);
                    home = null;
                }
                else if (homeHammock != null && homeHammock.TryOccupy(entity))
                {
                    SpiderTaskUtil.Log(entity, LogName, "goes to its home hammock at " + home);
                    return home;
                }
            }
        }

        BlockEntitySilkHammock free = HammockFinder.FindNearestFree(world, entity.Pos.XYZ, config.HammockSleepSearchRange, entity);
        if (free == null || !free.TryOccupy(entity))
        {
            SpiderTaskUtil.Log(entity, LogName, "no free hammock within " + config.HammockSleepSearchRange + " blocks");
            return null;
        }
        BlockPos chosen = free.Pos.Copy();
        if (wild && home == null)
        {
            SpiderAttributes.SetHomeHammock(entity, chosen);
            SpiderTaskUtil.Log(entity, LogName, "adopts the hammock at " + chosen + " as its home");
        }
        else
        {
            SpiderTaskUtil.Log(entity, LogName, "goes to the free hammock at " + chosen);
        }
        return chosen;
    }

    // Plans the jumps from here, or walks to a spot from which they exist. Every call counts as one attempt.
    private void StartTravel()
    {
        travelAttempts++;
        if (travelAttempts > maxTravelAttempts)
        {
            GiveUpHammock("gave up after " + maxTravelAttempts + " attempts");
            return;
        }

        SpiderJumpWorld world = new SpiderJumpWorld(entity);
        JumpLimits limits = SpiderTaskUtil.CreateLimits();
        List<JumpStep> plan = JumpPlanner.PlanJumps(world, entity.Pos.X, entity.Pos.Y, entity.Pos.Z,
            HammockPos.X, HammockPos.Y, HammockPos.Z, limits, entity.World.Rand);
        if (plan != null)
        {
            if (plan.Count == 0)
            {
                FallAsleep();
                return;
            }
            SpiderTaskUtil.Log(entity, LogName, "jumps to the hammock in " + plan.Count + " jump(s), attempt " + travelAttempts);
            runner.Start(plan);
            Phase = SleepPhase.Travelling;
            return;
        }

        JumpPlanNode spot = LaunchSpotFinder.Find(world, entity.Pos.X, entity.Pos.Y, entity.Pos.Z,
            HammockPos.X, HammockPos.Y, HammockPos.Z, limits, launchSpotSearchRadius, WalkStepUpBlocks, WalkStepDownBlocks,
            maxLaunchSpotPlanChecks, entity.World.Rand);
        if (spot == null)
        {
            GiveUpHammock("no spot to jump up from");
            return;
        }
        walkArrived = false;
        walkFailed = false;
        pathTraverser.Stop();
        bool started = pathTraverser.NavigateTo_Async(new Vec3d(spot.FeetX, spot.FeetY, spot.FeetZ), moveSpeed, WalkStopDistance,
            OnWalkArrived, OnWalkFailed, OnWalkFailed);
        if (!started)
        {
            GiveUpHammock("the pathfinder is busy");
            return;
        }
        walking = true;
        SpiderTaskUtil.StartAnimation(entity, walkAnimation);
        Phase = SleepPhase.Walking;
        SpiderTaskUtil.Log(entity, LogName, "walks to " + SpiderTaskUtil.Format(spot.FeetX, spot.FeetY, spot.FeetZ)
            + " to jump up from there, attempt " + travelAttempts);
    }

    private void GiveUpHammock(string reason)
    {
        SpiderTaskUtil.Log(entity, LogName, "gives up on the hammock at " + HammockPos + ": " + reason);
        ReleaseHammock();
        HammockPos = null;
        FallAsleep();
    }

    private bool IsInHammockCell()
    {
        BlockPos here = SpiderTaskUtil.FeetCell(entity);
        return HammockPos != null && here.Equals(HammockPos);
    }

    // ---- Sleeping and waking ----

    private void FallAsleep()
    {
        runner.Stop();
        pathTraverser.Stop();
        StopWalkingAnimation();
        SpiderTaskUtil.StopWalking(entity);

        if (HammockPos != null)
        {
            BlockEntitySilkHammock hammock = HammockFinder.GetHammockAt(entity.World, HammockPos);
            if (hammock == null || !hammock.TryOccupy(entity))
            {
                HammockPos = null;
            }
            else
            {
                SnapTo(hammock.GetSleepPosition());
            }
        }

        SpiderAttributes.SetSleeping(entity, true);
        SpiderTaskUtil.StartAnimation(entity, sleepAnimation);
        Phase = SleepPhase.Asleep;
        if (HammockPos != null)
        {
            SpiderTaskUtil.Log(entity, LogName, "falls asleep in the hammock at " + HammockPos);
        }
        else
        {
            SpiderTaskUtil.Log(entity, LogName, "falls asleep on the spot at " + SpiderTaskUtil.Format(entity.Pos.X, entity.Pos.Y, entity.Pos.Z));
        }
    }

    // Moves the spider exactly onto its resting spot if it is already almost there (it lands within a hair of it).
    private void SnapTo(Vec3d sleepPos)
    {
        double dx = sleepPos.X - entity.Pos.X;
        double dz = sleepPos.Z - entity.Pos.Z;
        if (dx * dx + dz * dz > SnapDistance * SnapDistance)
        {
            return;
        }
        entity.Pos.X = sleepPos.X;
        entity.Pos.Z = sleepPos.Z;
        entity.Pos.Motion.Set(0, 0, 0);
    }

    // Morning: wake up and hop out of the hammock.
    private void BeginLeaving()
    {
        WakeUp();
        Phase = SleepPhase.Leaving;
        SpiderTaskUtil.Log(entity, LogName, "wakes up: morning");
        if (leaveJumpRadius <= 0 || !entity.OnGround)
        {
            return;
        }
        List<JumpStep> plan = JumpPlanner.PlanJumpToRandomSpot(new SpiderJumpWorld(entity),
            entity.Pos.X, entity.Pos.Y, entity.Pos.Z, entity.Pos.X, entity.Pos.Y, entity.Pos.Z,
            leaveJumpRadius, leaveHeightBias, SpiderTaskUtil.CreateLimits(), entity.World.Rand);
        if (plan != null)
        {
            JumpStep step = plan[0];
            SpiderTaskUtil.Log(entity, LogName, "hops out to " + SpiderTaskUtil.Format(step.ToX, step.ToY, step.ToZ));
            runner.Start(plan);
        }
    }

    // Clears the sleeping flag, frees the hammock and stops the sleep animation. Safe to call more than once.
    private void WakeUp()
    {
        if (SpiderAttributes.IsSleeping(entity))
        {
            SpiderAttributes.SetSleeping(entity, false);
        }
        ReleaseHammock();
        SpiderTaskUtil.StopAnimation(entity, sleepAnimation);
    }

    private void ReleaseHammock()
    {
        if (HammockPos == null)
        {
            return;
        }
        BlockEntitySilkHammock hammock = HammockFinder.GetHammockAt(entity.World, HammockPos);
        if (hammock != null)
        {
            hammock.Release(entity);
        }
    }

    private void StopWalkingAnimation()
    {
        if (walking)
        {
            walking = false;
            SpiderTaskUtil.StopAnimation(entity, walkAnimation);
        }
    }

    // Pathfinder callbacks (the game calls these from the traverser's tick).
    private void OnWalkArrived()
    {
        walkArrived = true;
    }

    private void OnWalkFailed()
    {
        walkFailed = true;
    }
}
