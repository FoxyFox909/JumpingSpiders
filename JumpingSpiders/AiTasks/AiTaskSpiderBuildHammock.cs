using System;
using System.Collections.Generic;
using System.Diagnostics;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace JumpingSpiders;

/// <summary>
/// Building a hammock, JSON code "jumpingspiders.buildhammock" (adult JSON only, minGeneration 1, so only
/// domesticated adults build; the code checks both again).
///
/// (How the game drives a task: see AiTaskSpiderJumpAttack.)
///
/// When: a kill owes the spider one hammock (SpiderAttributes.IsHammockBuildArmed). While it owes one and is
/// sated, it makes one attempt every Config.HammockBuildIntervalHours in-game hours (HammockBuildSchedule) until one
/// succeeds. The time of the next attempt is saved on the spider when an attempt starts, so an unloaded chunk or a
/// restart never gives a burst of attempts. A failed attempt just waits for the next hour.
///
/// One attempt, spread over many server ticks so that no tick does much (Phase, BuildPhase):
/// 1. Pick: random cells around the spider until one can hold a hammock (HammockBuilder.TryPickBuildSite,
///    pickTriesPerTick per tick, pickMaxTries in all). Every such cell is equally likely, high or low.
/// 2. Spacing: if another hammock (anyone's) is too close (Config.HammockSpacing, one per 3 x 3 x 3), the attempt fails.
/// 3. Get within reach (BuildReach: Config.HammockBuildRange from the body's middle to the cell), in rounds. Each
///    round, from where the spider stands: build at once if within reach; else list the spots within reach where it
///    could stand (BuildStanceFinder) and plan jumps to the nearest (JumpPlanner, at most maxPlanSearches searches per
///    attempt, never two in one tick); else test jumps at the walls and ceilings next to the site (ClingJump, a few per
///    tick); else walk (the game's pathfinder) to a spot to stand on, or to a spot from which such a jump works. Every
///    jump has a wind-up. A jump at a wall or ceiling ends with the spider holding on there as soon as it touches a
///    surface within reach: its physics is paused (as in a ceiling hammock, see AiTaskSpiderSleep) until it has built.
///    Every spot is right against the site (BuildStanceFinder), so the spider usually lands or arrives a little short of
///    it: within maxSidleDistance it sidles the rest of the way along the floor, and a spider that got hold of the wall
///    or ceiling it jumped at within maxCrawlDistance of its spot crawls there (sidleSpeed, physics still paused).
///    It gives up after maxApproachRounds rounds, maxWalks walks or approachTimeoutMs.
/// 4. Build: stand (or hang) still for buildTimeMs, check the spacing again, place the hammock
///    (HammockBuilder.PlaceHammock), let go. Spiders take no fall damage, so a clinging spider simply drops.
///
/// Priority 1.35 in the JSON: above exploring (1.3), below getting out of water, the bait eat task, hunting, sleeping
/// and fleeing, so it never pre-empts them, and any of them can interrupt it (except in mid-air).
///
/// /jspider build starts an attempt at once with StartForcedAttempt, ignoring the timer and the hunger.
/// Every attempt logs how it ended, with its most expensive tick (Logs/server-debug.log).
/// </summary>
public class AiTaskSpiderBuildHammock : AiTaskBase
{
    /// <summary>Name used in the debug log.</summary>
    public const string LogName = "build";

    /// <summary>The pathfinder stops walking this close to the goal, in blocks.</summary>
    public const float WalkStopDistance = 0.3f;

    /// <summary>A spider within reach builds only when its box stays at least this far (sideways) out of the site's cell.</summary>
    public const double StandingBuildSiteMargin = 0.02;

    /// <summary>Launch spots for a cling jump are looked for at most this far below the spider's feet, in blocks.</summary>
    public const int LaunchSpotStepDown = 2;

    // ---- Knobs from the task's JSON entry (see the constructor for units and defaults) ----
    private readonly int checkIntervalMs;
    private readonly int pickTriesPerTick;
    private readonly int pickMaxTries;
    private readonly int maxPlanSearches;
    private readonly int clingChecksPerTick;
    private readonly int maxClingChecks;
    private readonly int maxLaunchSpotChecks;
    private readonly int launchSpotSearchRadius;
    private readonly int maxWalks;
    private readonly int maxApproachRounds;
    private readonly int approachTimeoutMs;
    private readonly int buildTimeMs;
    private readonly float sidleSpeed;
    private readonly float maxSidleDistance;
    private readonly float maxCrawlDistance;
    private readonly float moveSpeed;
    private readonly float turnSpeedRadPerSec;
    private readonly string walkAnimation;
    private readonly string buildAnimation;

    // ---- State of the current attempt (in memory; an attempt lasts seconds) ----

    /// <summary>What the attempt is doing now (read by debug tools).</summary>
    public BuildPhase Phase = BuildPhase.None;

    /// <summary>The spot the current (or last) attempt picked, or null.</summary>
    public HammockSite Site;

    /// <summary>How the last attempt ended, for /jspider info and the log.</summary>
    public string LastOutcome = "";

    private readonly JumpRunner runner;
    private SpiderJumpWorld jumpWorld;
    private long nextCheckMs;
    private long deadlineMs;
    private long buildDoneMs;
    private int pickTriesDone;
    private int planSearches;
    private int clingChecks;
    private int launchSpotChecks;
    private int walks;
    private int rounds;
    private List<BuildStance> standing;
    private List<BuildStance> clinging;
    private List<BuildStance> launchSpots;
    private BuildStance launchSpotsFor;
    private int nextIndex;
    private BuildStance clingTarget;
    private BuildStance walkTarget;
    private BuildStance sidleTarget;
    private bool held;
    private string heldOn = "";
    private bool walking;
    private bool walkArrived;
    private bool walkFailed;
    private string outcome;
    private bool succeeded;
    private bool forced;
    private HammockSite presetSite;
    private IServerPlayer reportTo;

    // Cost bookkeeping (server main thread time spent by this task), for the debug log.
    private readonly Stopwatch watch = new Stopwatch();
    private double pendingTickMs;
    private double worstTickMs;
    private BuildPhase worstTickPhase;
    private double totalMs;
    private int ticks;

    public AiTaskSpiderBuildHammock(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig)
        : base(entity, taskConfig, aiConfig)
    {
        // Also read by the base class from the same entry: priority, minGeneration.

        // Milliseconds: how often it checks whether an attempt is due.
        checkIntervalMs = taskConfig["checkIntervalMs"].AsInt(2000);
        // Random cells tried per tick while picking the spot, and in all (each try is a few block lookups).
        pickTriesPerTick = taskConfig["pickTriesPerTick"].AsInt(40);
        pickMaxTries = taskConfig["pickMaxTries"].AsInt(400);
        // Most jump-planner searches per attempt (each is about 1 ms; never more than one per tick).
        maxPlanSearches = taskConfig["maxPlanSearches"].AsInt(2);
        // Jumps at a wall or ceiling tested per tick, and in all per attempt (each is one arc, far cheaper than a search).
        clingChecksPerTick = taskConfig["clingChecksPerTick"].AsInt(4);
        maxClingChecks = taskConfig["maxClingChecks"].AsInt(24);
        // Spots to walk to and jump at a wall from, tested per attempt (one arc each, clingChecksPerTick per tick).
        maxLaunchSpotChecks = taskConfig["maxLaunchSpotChecks"].AsInt(12);
        // Blocks: how far (horizontally) from a clinging spot it looks for a spot to jump from.
        launchSpotSearchRadius = taskConfig["launchSpotSearchRadius"].AsInt(3);
        // Most walks per attempt.
        maxWalks = taskConfig["maxWalks"].AsInt(2);
        // Most rounds of "where am I, how do I get within reach" per attempt (one after every jump or walk).
        maxApproachRounds = taskConfig["maxApproachRounds"].AsInt(5);
        // Milliseconds: an attempt that has not got within reach by then fails.
        approachTimeoutMs = taskConfig["approachTimeoutMs"].AsInt(20000);
        // Milliseconds: how long building takes, standing or holding on to a wall or ceiling.
        buildTimeMs = taskConfig["buildTimeMs"].AsInt(1500);
        // Blocks per second: how fast it sidles along the floor, or crawls along a wall or ceiling, onto its exact spot.
        sidleSpeed = taskConfig["sidleSpeed"].AsFloat(1.5f);
        // Blocks: it sidles onto a standing spot this close (on the same floor), and crawls onto a clinging spot this
        // close to where it got hold of the wall or ceiling; farther off it jumps or walks.
        maxSidleDistance = taskConfig["maxSidleDistance"].AsFloat(0.8f);
        maxCrawlDistance = taskConfig["maxCrawlDistance"].AsFloat(1.0f);
        // Walking speed (vanilla movespeed units).
        moveSpeed = taskConfig["movespeed"].AsFloat(0.02f);
        // Degrees per second: how fast it turns towards the site while building, and towards a jump while winding up.
        turnSpeedRadPerSec = taskConfig["turnSpeedDegPerSec"].AsFloat(360) * (float)Math.PI / 180f;
        // Animation codes from the entity JSON; "" = none.
        walkAnimation = SpiderTaskUtil.ReadAnimation(taskConfig, "walkAnimation", "walk");
        buildAnimation = SpiderTaskUtil.ReadAnimation(taskConfig, "buildAnimation", "");

        runner = new JumpRunner(entity);
        // Milliseconds: the crouch before each jump, a random time between these two.
        runner.WindUpMinSeconds = taskConfig["windUpMinMs"].AsInt(350) / 1000f;
        runner.WindUpMaxSeconds = taskConfig["windUpMaxMs"].AsInt(900) / 1000f;
        // Milliseconds: rest on the intermediate perch between two jumps.
        runner.PauseSeconds = taskConfig["pauseBetweenJumpsMs"].AsInt(300) / 1000f;
        runner.TurnSpeedRadPerSec = turnSpeedRadPerSec;
        runner.WindUpAnimation = SpiderTaskUtil.ReadAnimation(taskConfig, "windUpAnimation", "jumpwindup");
        runner.JumpAnimation = SpiderTaskUtil.ReadAnimation(taskConfig, "jumpAnimation", "jump");
    }

    /// <summary>Nothing may interrupt the spider while it is in the air.</summary>
    public override float PriorityForCancel
    {
        get
        {
            if (runner.IsFlying())
            {
                return SpiderTaskUtil.InFlightPriorityForCancel;
            }
            return base.PriorityForCancel;
        }
    }

    public override bool ShouldExecute()
    {
        long now = entity.World.ElapsedMilliseconds;
        if (now < nextCheckMs)
        {
            return false;
        }
        nextCheckMs = now + checkIntervalMs;

        if (!SpiderAttributes.IsAdult(entity) || !SpiderAttributes.IsDomesticated(entity))
        {
            return false;
        }
        if (!PreconditionsSatisfied())
        {
            return false;
        }
        if (!entity.OnGround || entity.Swimming || entity.FeetInLiquid)
        {
            return false;
        }
        JumpingSpidersConfig config = JumpingSpidersModSystem.Config;
        return HammockBuildSchedule.IsAttemptDue(SpiderAttributes.IsHammockBuildArmed(entity), !SpiderAttributes.IsHungry(entity),
            entity.World.Calendar.TotalHours, SpiderAttributes.GetNextBuildAttemptHours(entity), config.HammockBuildIntervalHours);
    }

    public override void StartExecute()
    {
        watch.Restart();
        base.StartExecute();
        JumpingSpidersConfig config = JumpingSpidersModSystem.Config;
        jumpWorld = new SpiderJumpWorld(entity);
        deadlineMs = entity.World.ElapsedMilliseconds + approachTimeoutMs;
        pickTriesDone = 0;
        planSearches = 0;
        clingChecks = 0;
        launchSpotChecks = 0;
        walks = 0;
        rounds = 0;
        standing = null;
        clinging = null;
        launchSpots = null;
        launchSpotsFor = null;
        clingTarget = null;
        walkTarget = null;
        sidleTarget = null;
        held = false;
        walking = false;
        outcome = null;
        succeeded = false;
        worstTickMs = 0;
        totalMs = 0;
        ticks = 0;
        Site = null;

        if (!forced)
        {
            // Saved at once: whatever happens to this attempt, the next one is an interval away.
            SpiderAttributes.SetNextBuildAttemptHours(entity,
                HammockBuildSchedule.NextAttemptHours(entity.World.Calendar.TotalHours, config.HammockBuildIntervalHours));
            SpiderTaskUtil.Log(entity, LogName, "starts its hourly build attempt");
        }
        if (presetSite != null)
        {
            // /jspider build has picked and checked the spot already.
            Site = presetSite;
            presetSite = null;
            Phase = BuildPhase.FindingStances;
        }
        else
        {
            Phase = BuildPhase.Picking;
        }
        watch.Stop();
        pendingTickMs = watch.Elapsed.TotalMilliseconds;
    }

    public override bool ContinueExecute(float dt)
    {
        watch.Restart();
        BuildPhase phaseBefore = Phase;
        bool goOn = Step(dt);
        watch.Stop();
        double tickMs = pendingTickMs + watch.Elapsed.TotalMilliseconds;
        pendingTickMs = 0;
        ticks++;
        totalMs += tickMs;
        if (tickMs > worstTickMs)
        {
            worstTickMs = tickMs;
            worstTickPhase = phaseBefore;
        }
        return goOn;
    }

    // One tick of the attempt. False when it is over.
    private bool Step(float dt)
    {
        long now = entity.World.ElapsedMilliseconds;
        if (Phase == BuildPhase.Done)
        {
            return false;
        }
        if (now > deadlineMs && CanGiveUpNow())
        {
            Fail("could not get within reach in " + (approachTimeoutMs / 1000) + " s");
            return false;
        }

        if (Phase == BuildPhase.Picking)
        {
            StepPicking();
        }
        else if (Phase == BuildPhase.FindingStances)
        {
            StepFindingStances();
        }
        else if (Phase == BuildPhase.Planning)
        {
            StepPlanning();
        }
        else if (Phase == BuildPhase.FindingClings)
        {
            StepFindingClings();
        }
        else if (Phase == BuildPhase.CheckingClingJumps)
        {
            StepCheckingClingJumps();
        }
        else if (Phase == BuildPhase.ChoosingWalk)
        {
            StepChoosingWalk();
        }
        else if (Phase == BuildPhase.CheckingLaunchSpots)
        {
            StepCheckingLaunchSpots();
        }
        else if (Phase == BuildPhase.Jumping)
        {
            StepJumping(dt);
        }
        else if (Phase == BuildPhase.ClingJumping)
        {
            StepClingJumping(dt);
        }
        else if (Phase == BuildPhase.Walking)
        {
            StepWalking();
        }
        else if (Phase == BuildPhase.Sidling)
        {
            StepSidling(dt);
        }
        else if (Phase == BuildPhase.Building || Phase == BuildPhase.Clinging)
        {
            StepBuilding(dt, now);
        }
        return Phase != BuildPhase.Done;
    }

    // ---- 1 and 2: pick a spot, check the spacing ----

    private void StepPicking()
    {
        int tries = Math.Min(pickTriesPerTick, pickMaxTries - pickTriesDone);
        int used;
        HammockSite site = PickSite(tries, out used);
        pickTriesDone += used;
        if (site == null)
        {
            if (pickTriesDone >= pickMaxTries)
            {
                Fail("found no spot that can hold a hammock in " + pickMaxTries + " tries");
            }
            return;
        }
        Site = site;
        SpiderTaskUtil.Log(entity, LogName, "picked " + DescribeSite(site) + " (try " + pickTriesDone + ")");
        BlockEntitySilkHammock tooClose = FindHammockTooClose(site.Pos);
        if (tooClose != null)
        {
            Fail("a hammock at " + tooClose.Pos + " is too close to " + site.Pos + " (one per "
                + JumpingSpidersModSystem.Config.HammockSpacing + " x " + JumpingSpidersModSystem.Config.HammockSpacing + " x "
                + JumpingSpidersModSystem.Config.HammockSpacing + ")");
            return;
        }
        Phase = BuildPhase.FindingStances;
    }

    // ---- 3: get within reach, in rounds ----

    // Start of a round: build if within reach, else list the standing spots within reach.
    private void StepFindingStances()
    {
        if (!entity.OnGround || entity.Swimming)
        {
            return;
        }
        rounds++;
        if (rounds > maxApproachRounds)
        {
            Fail("could not get within reach in " + maxApproachRounds + " rounds");
            return;
        }
        if (IsInReachStanding())
        {
            StartBuilding();
            return;
        }
        standing = BuildStanceFinder.FindStanding(jumpWorld, jumpWorld, Site.Pos.X, Site.Pos.Y, Site.Pos.Z,
            JumpingSpidersModSystem.Config.HammockBuildRange, HalfWidth(), BodyHeight(), entity.Pos.X, entity.Pos.Y, entity.Pos.Z);
        clinging = null;
        nextIndex = 0;
        // Landed or arrived a little short of a spot on the same floor: sidle onto it.
        for (int i = 0; i < standing.Count; i++)
        {
            BuildStance stance = standing[i];
            if (Math.Abs(stance.FeetY - entity.Pos.Y) < 0.05 && HorizontalDistance(stance.FeetX, stance.FeetZ) <= maxSidleDistance)
            {
                StartSidling(stance, false);
                return;
            }
        }
        if (standing.Count > 0 && planSearches < maxPlanSearches)
        {
            Phase = BuildPhase.Planning;
        }
        else
        {
            Phase = BuildPhase.FindingClings;
        }
    }

    // One planner search, to the nearest standing spot.
    private void StepPlanning()
    {
        planSearches++;
        BuildStance target = standing[0];
        List<JumpStep> plan = JumpPlanner.PlanJumps(jumpWorld, entity.Pos.X, entity.Pos.Y, entity.Pos.Z,
            target.CellX, target.CellY, target.CellZ, SpiderTaskUtil.CreateLimits(), entity.World.Rand);
        if (plan != null && plan.Count > 0)
        {
            SpiderTaskUtil.Log(entity, LogName, "jumps to " + SpiderTaskUtil.Format(target.FeetX, target.FeetY, target.FeetZ)
                + " to build from there, " + plan.Count + " jump(s)");
            runner.Start(plan);
            Phase = BuildPhase.Jumping;
            return;
        }
        Phase = BuildPhase.FindingClings;
    }

    private void StepFindingClings()
    {
        clinging = BuildStanceFinder.FindClinging(jumpWorld, jumpWorld, Site.Pos.X, Site.Pos.Y, Site.Pos.Z,
            JumpingSpidersModSystem.Config.HammockBuildRange, HalfWidth(), BodyHeight(), entity.Pos.X, entity.Pos.Y, entity.Pos.Z);
        nextIndex = 0;
        Phase = BuildPhase.CheckingClingJumps;
    }

    // A few jumps from here at the walls and ceilings next to the site.
    private void StepCheckingClingJumps()
    {
        JumpLimits limits = SpiderTaskUtil.CreateLimits();
        int checkedThisTick = 0;
        while (nextIndex < clinging.Count && checkedThisTick < clingChecksPerTick && clingChecks < maxClingChecks)
        {
            BuildStance stance = clinging[nextIndex];
            nextIndex++;
            checkedThisTick++;
            clingChecks++;
            JumpStep step = ClingJump.TryJump(jumpWorld, entity.Pos.X, entity.Pos.Y, entity.Pos.Z, stance, limits);
            if (step != null)
            {
                StartClingJump(stance, step);
                return;
            }
        }
        if (nextIndex < clinging.Count && clingChecks < maxClingChecks)
        {
            // More to test next tick.
            return;
        }
        Phase = BuildPhase.ChoosingWalk;
    }

    // Where to walk: a standing spot within walking reach, else the launch spots of the nearest clinging spot.
    private void StepChoosingWalk()
    {
        if (walks >= maxWalks)
        {
            Fail("could not get within reach (walked " + walks + " times)");
            return;
        }
        int feetCellY = JumpPlanner.CellOfFeetY(entity.Pos.Y);
        for (int i = 0; i < standing.Count; i++)
        {
            BuildStance stance = standing[i];
            bool walkableHeight = stance.CellY <= feetCellY + AiTaskSpiderSleep.WalkStepUpBlocks
                && stance.CellY >= feetCellY - AiTaskSpiderSleep.WalkStepDownBlocks;
            if (walkableHeight && !IsSameCell(stance, walkTarget))
            {
                StartWalk(stance, "to build from there");
                return;
            }
        }
        if (clinging == null || clinging.Count == 0)
        {
            Fail("no spot within reach to stand on or hold on to");
            return;
        }
        // The nearest clinging spot that has not been walked for yet.
        launchSpotsFor = clinging[0];
        launchSpots = BuildStanceFinder.FindLaunchSpots(jumpWorld, launchSpotsFor, SpiderTaskUtil.CreateLimits(),
            launchSpotSearchRadius, AiTaskSpiderSleep.WalkStepUpBlocks, LaunchSpotStepDown, entity.Pos.X, entity.Pos.Y, entity.Pos.Z);
        nextIndex = 0;
        Phase = BuildPhase.CheckingLaunchSpots;
    }

    private void StepCheckingLaunchSpots()
    {
        JumpLimits limits = SpiderTaskUtil.CreateLimits();
        int checkedThisTick = 0;
        while (nextIndex < launchSpots.Count && checkedThisTick < clingChecksPerTick && launchSpotChecks < maxLaunchSpotChecks)
        {
            BuildStance spot = launchSpots[nextIndex];
            nextIndex++;
            checkedThisTick++;
            launchSpotChecks++;
            if (IsSameCell(spot, walkTarget))
            {
                continue;
            }
            if (ClingJump.TryJump(jumpWorld, spot.FeetX, spot.FeetY, spot.FeetZ, launchSpotsFor, limits) != null)
            {
                StartWalk(spot, "to jump at the wall or ceiling from there");
                return;
            }
        }
        if (nextIndex < launchSpots.Count && launchSpotChecks < maxLaunchSpotChecks)
        {
            return;
        }
        Fail("found no way to get within reach");
    }

    private void StepJumping(float dt)
    {
        if (runner.IsBusy())
        {
            runner.Tick(dt);
            if (runner.IsBusy())
            {
                return;
            }
            SpiderTaskUtil.Log(entity, LogName, "jump " + runner.State + " (" + runner.LastEndReason + ") at "
                + SpiderTaskUtil.Format(entity.Pos.X, entity.Pos.Y, entity.Pos.Z));
        }
        if (entity.OnGround)
        {
            Phase = BuildPhase.FindingStances;
        }
    }

    private void StepClingJumping(float dt)
    {
        if (runner.IsBusy())
        {
            runner.Tick(dt);
        }
        if (TryCatchHold())
        {
            return;
        }
        if (runner.IsBusy())
        {
            return;
        }
        // Missed (or bounced off something out of reach): back on its feet, try again from there.
        if (entity.OnGround)
        {
            SpiderTaskUtil.Log(entity, LogName, "jump at the wall or ceiling missed (" + runner.LastEndReason + "), now at "
                + SpiderTaskUtil.Format(entity.Pos.X, entity.Pos.Y, entity.Pos.Z));
            Phase = BuildPhase.FindingStances;
        }
    }

    private void StepWalking()
    {
        if (IsInReachStanding())
        {
            StopWalk();
            StartBuilding();
            return;
        }
        if (walkFailed)
        {
            StopWalk();
            SpiderTaskUtil.Log(entity, LogName, "could not walk there");
            Phase = BuildPhase.FindingStances;
            return;
        }
        if (walkArrived && entity.OnGround)
        {
            StopWalk();
            Phase = BuildPhase.FindingStances;
        }
    }

    // Moves the spider straight towards sidleTarget, sidleSpeed blocks per second, as long as its box fits on the way.
    private void StepSidling(float dt)
    {
        double dx = sidleTarget.FeetX - entity.Pos.X;
        double dy = sidleTarget.FeetY - entity.Pos.Y;
        double dz = sidleTarget.FeetZ - entity.Pos.Z;
        double distance = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        double step = sidleSpeed * dt;
        bool arrived = distance <= step;
        double moveX = sidleTarget.FeetX;
        double moveY = sidleTarget.FeetY;
        double moveZ = sidleTarget.FeetZ;
        if (!arrived)
        {
            moveX = entity.Pos.X + dx * step / distance;
            moveY = entity.Pos.Y + dy * step / distance;
            moveZ = entity.Pos.Z + dz * step / distance;
        }
        bool blocked = !jumpWorld.IsFree(moveX, moveY, moveZ);
        if (!blocked)
        {
            entity.Pos.X = moveX;
            entity.Pos.Y = moveY;
            entity.Pos.Z = moveZ;
            entity.Pos.Motion.Set(0, 0, 0);
        }
        if (!held)
        {
            SpiderTaskUtil.StopWalking(entity);
        }
        if (!arrived && !blocked)
        {
            return;
        }
        bool inReach = InReachAt(entity.Pos.X, entity.Pos.Y, entity.Pos.Z);
        if (held)
        {
            if (inReach)
            {
                StartClingBuilding();
            }
            else
            {
                Fail("could not crawl within reach along " + heldOn);
            }
            return;
        }
        if (inReach && entity.OnGround)
        {
            StartBuilding();
            return;
        }
        // Something in the way: decide again from here (this counts as a round).
        Phase = BuildPhase.FindingStances;
    }

    private void StartSidling(BuildStance target, bool holding)
    {
        sidleTarget = target;
        Phase = BuildPhase.Sidling;
        SpiderTaskUtil.Log(entity, LogName, (holding ? "crawls" : "sidles") + " " + HorizontalDistance(target.FeetX, target.FeetZ).ToString("0.00")
            + " blocks onto its spot at " + SpiderTaskUtil.Format(target.FeetX, target.FeetY, target.FeetZ));
    }

    // ---- 4: build ----

    private void StepBuilding(float dt, long now)
    {
        if (Phase == BuildPhase.Building)
        {
            SpiderTaskUtil.StopWalking(entity);
        }
        else
        {
            entity.Pos.Motion.Set(0, 0, 0);
        }
        SpiderTaskUtil.TurnTowards(entity, Site.Pos.X + 0.5, Site.Pos.Z + 0.5, turnSpeedRadPerSec, dt);
        if (now < buildDoneMs)
        {
            return;
        }
        PlaceNow();
    }

    private void StartBuilding()
    {
        runner.Stop();
        SpiderTaskUtil.StopWalking(entity);
        Phase = BuildPhase.Building;
        buildDoneMs = entity.World.ElapsedMilliseconds + buildTimeMs;
        SpiderTaskUtil.StartAnimation(entity, buildAnimation);
        SpiderTaskUtil.Log(entity, LogName, "builds standing at " + SpiderTaskUtil.Format(entity.Pos.X, entity.Pos.Y, entity.Pos.Z));
    }

    private void PlaceNow()
    {
        BlockEntitySilkHammock tooClose = FindHammockTooClose(Site.Pos);
        if (tooClose != null)
        {
            Fail("meanwhile a hammock appeared too close, at " + tooClose.Pos);
            return;
        }
        string how = "standing";
        if (held)
        {
            how = "holding on to " + heldOn;
        }
        if (!HammockBuilder.PlaceHammock(entity.World, Site.Pos, Site.SupportFacing))
        {
            Fail("the spot could not take the hammock after all (taken, no support, or something in the way)");
            return;
        }
        SpiderAttributes.SetHammockBuildArmed(entity, false);
        succeeded = true;
        Finish("built a hammock at " + DescribeSite(Site) + ", " + how);
        Release();
    }

    // ---- Moving ----

    private void StartClingJump(BuildStance stance, JumpStep step)
    {
        clingTarget = stance;
        string where = "the wall";
        if (stance.IsCeiling())
        {
            where = "the ceiling";
        }
        SpiderTaskUtil.Log(entity, LogName, "jumps at " + where + " to hold on at "
            + SpiderTaskUtil.Format(stance.FeetX, stance.FeetY, stance.FeetZ));
        List<JumpStep> plan = new List<JumpStep>();
        plan.Add(step);
        runner.Start(plan);
        Phase = BuildPhase.ClingJumping;
    }

    // While flying at a wall or ceiling: holds on as soon as it touches a surface within reach of the site.
    // The aimed-at surface is tried first, then the other four, so bouncing off something else within reach works too.
    private bool TryCatchHold()
    {
        if (entity.OnGround || entity.Swimming || clingTarget == null)
        {
            return false;
        }
        if (!runner.IsFlying() && runner.LastEndReason != JumpEndReason.HitWall && runner.LastEndReason != JumpEndReason.HitCeiling)
        {
            return false;
        }
        double dx = clingTarget.FeetX - entity.Pos.X;
        double dy = clingTarget.FeetY - entity.Pos.Y;
        double dz = clingTarget.FeetZ - entity.Pos.Z;
        bool nearAimedSpot = dx * dx + dy * dy + dz * dz <= (maxCrawlDistance + BuildStanceFinder.SurfaceProbe) * (maxCrawlDistance + BuildStanceFinder.SurfaceProbe);
        bool nearSite = BuildReach.IsWithinReach(entity.Pos.X, entity.Pos.Y, entity.Pos.Z, BodyHeight(), Site.Pos.X, Site.Pos.Y, Site.Pos.Z,
            JumpingSpidersModSystem.Config.HammockBuildRange + BuildStanceFinder.SurfaceProbe);
        if (!nearAimedSpot && !nearSite)
        {
            return false;
        }
        // The surface it aimed at, anywhere it can crawl onto its spot from.
        if (nearAimedSpot && TryHoldTowards(clingTarget.SurfaceX, clingTarget.SurfaceY, clingTarget.SurfaceZ, true))
        {
            return true;
        }
        // Or any surface right where it can build.
        if (TryHoldTowards(0, 1, 0, false) || TryHoldTowards(1, 0, 0, false) || TryHoldTowards(-1, 0, 0, false)
            || TryHoldTowards(0, 0, 1, false) || TryHoldTowards(0, 0, -1, false))
        {
            return true;
        }
        return false;
    }

    // aimed: the surface of the clinging spot it jumped at; it holds on within maxCrawlDistance of the spot and crawls
    // there. Otherwise it only holds on where it can build at once.
    private bool TryHoldTowards(int dirX, int dirY, int dirZ, bool aimed)
    {
        double holdX;
        double holdY;
        double holdZ;
        if (!ClingJump.FindHold(jumpWorld, entity.Pos.X, entity.Pos.Y, entity.Pos.Z, dirX, dirY, dirZ,
            BuildStanceFinder.SurfaceProbe, BuildStanceFinder.ClingGap, out holdX, out holdY, out holdZ))
        {
            return false;
        }
        bool inReach = InReachAt(holdX, holdY, holdZ);
        bool crawl = false;
        if (!inReach)
        {
            if (!aimed)
            {
                return false;
            }
            double cx = clingTarget.FeetX - holdX;
            double cy = clingTarget.FeetY - holdY;
            double cz = clingTarget.FeetZ - holdZ;
            if (cx * cx + cy * cy + cz * cz > maxCrawlDistance * maxCrawlDistance)
            {
                return false;
            }
            crawl = true;
        }
        runner.Stop();
        held = true;
        SpiderTaskUtil.SetPhysicsPaused(entity, true);
        entity.Pos.X = holdX;
        entity.Pos.Y = holdY;
        entity.Pos.Z = holdZ;
        heldOn = "a wall";
        if (dirY > 0)
        {
            heldOn = "the ceiling";
        }
        SpiderTaskUtil.Log(entity, LogName, "holds on to " + heldOn + " at " + SpiderTaskUtil.Format(holdX, holdY, holdZ));
        if (crawl)
        {
            StartSidling(clingTarget, true);
            return true;
        }
        StartClingBuilding();
        return true;
    }

    private void StartClingBuilding()
    {
        Phase = BuildPhase.Clinging;
        buildDoneMs = entity.World.ElapsedMilliseconds + buildTimeMs;
        SpiderTaskUtil.StartAnimation(entity, buildAnimation);
        SpiderTaskUtil.Log(entity, LogName, "builds holding on to " + heldOn + " at " + SpiderTaskUtil.Format(entity.Pos.X, entity.Pos.Y, entity.Pos.Z));
    }

    private void StartWalk(BuildStance target, string why)
    {
        walks++;
        walkTarget = target;
        walkArrived = false;
        walkFailed = false;
        pathTraverser.Stop();
        bool started = pathTraverser.NavigateTo_Async(new Vec3d(target.FeetX, target.FeetY, target.FeetZ), moveSpeed,
            WalkStopDistance, OnWalkArrived, OnWalkFailed, OnWalkFailed);
        if (!started)
        {
            Fail("the pathfinder is busy");
            return;
        }
        walking = true;
        SpiderTaskUtil.StartAnimation(entity, walkAnimation);
        Phase = BuildPhase.Walking;
        SpiderTaskUtil.Log(entity, LogName, "walks to " + SpiderTaskUtil.Format(target.FeetX, target.FeetY, target.FeetZ) + " " + why);
    }

    private void StopWalk()
    {
        pathTraverser.Stop();
        if (walking)
        {
            walking = false;
            SpiderTaskUtil.StopAnimation(entity, walkAnimation);
        }
        SpiderTaskUtil.StopWalking(entity);
    }

    // ---- Ending ----

    private void Fail(string reason)
    {
        succeeded = false;
        Finish(reason);
    }

    private void Finish(string text)
    {
        outcome = text;
        Phase = BuildPhase.Done;
    }

    /// <summary>Lets go of a wall or ceiling (resumes physics, so it drops). Safe to call more than once.</summary>
    private void Release()
    {
        if (held)
        {
            held = false;
            SpiderTaskUtil.SetPhysicsPaused(entity, false);
        }
    }

    public override void FinishExecute(bool cancelled)
    {
        runner.Stop();
        StopWalk();
        Release();
        SpiderTaskUtil.StopAnimation(entity, buildAnimation);
        if (outcome == null)
        {
            outcome = "interrupted (" + Phase + ")";
            if (!cancelled)
            {
                outcome = "ended (" + Phase + ")";
            }
        }
        LastOutcome = outcome;
        string result = "no hammock";
        if (succeeded)
        {
            result = "success";
        }
        string summary = "attempt over (" + result + "): " + outcome + ". Cost: " + ticks + " ticks, worst tick "
            + worstTickMs.ToString("0.00") + " ms (" + worstTickPhase + "), " + totalMs.ToString("0.0") + " ms in all, " + planSearches + " planner search(es), "
            + (clingChecks + launchSpotChecks) + " wall/ceiling jump test(s), " + pickTriesDone + " pick tries.";
        SpiderTaskUtil.Log(entity, LogName, summary);
        if (forced)
        {
            Report("Spider id " + entity.EntityId + ": " + summary);
        }
        forced = false;
        reportTo = null;
        Phase = BuildPhase.None;
        base.FinishExecute(cancelled);
    }

    public override void OnEntityHurt(DamageSource source, float damage)
    {
        base.OnEntityHurt(source, damage);
        // Let go at once rather than in the next AI tick: the AI stops for good if this hit kills it.
        Release();
    }

    public override void OnEntityDespawn(EntityDespawnData reason)
    {
        Release();
        base.OnEntityDespawn(reason);
    }

    // ---- /jspider build ----

    /// <summary>
    /// Starts an attempt now, ignoring the hourly timer, the hunger and whether a hammock is owed (for /jspider build).
    /// Picks the spot and checks the spacing right away (a bounded number of block lookups), then hands over to the
    /// task, which interrupts whatever the spider is doing unless it is in the air. Returns what happened so far, worded
    /// to follow "Spider id N " (it starts with a verb). When the result comes later, it is sent to reportTo (or the
    /// server log for the console).
    /// </summary>
    public string StartForcedAttempt(IServerPlayer caller)
    {
        EntityBehaviorTaskAI taskAi = entity.GetBehavior<EntityBehaviorTaskAI>();
        if (taskAi == null)
        {
            return "has no AI, so it cannot build.";
        }
        IAiTask active = taskAi.TaskManager.ActiveTasksBySlot[Slot];
        if (active == this)
        {
            return "is already in the middle of an attempt (" + Phase + ").";
        }
        if (active != null && active.PriorityForCancel >= SpiderTaskUtil.InFlightPriorityForCancel)
        {
            return "is in the air; try again in a moment.";
        }
        if (!entity.OnGround || entity.Swimming)
        {
            return "is not standing on the ground; try again in a moment.";
        }

        Stopwatch commandWatch = Stopwatch.StartNew();
        jumpWorld = new SpiderJumpWorld(entity);
        int used;
        HammockSite site = PickSite(pickMaxTries, out used);
        if (site == null)
        {
            return "found no spot that can hold a hammock within " + JumpingSpidersModSystem.Config.HammockBuildSearchRadius
                + " blocks (" + pickMaxTries + " tries). Nothing changed.";
        }
        string picked = "picked " + DescribeSite(site) + ". ";
        BlockEntitySilkHammock tooClose = FindHammockTooClose(site.Pos);
        if (tooClose != null)
        {
            commandWatch.Stop();
            return picked + "A hammock at " + tooClose.Pos + " is too close (one per " + JumpingSpidersModSystem.Config.HammockSpacing
                + " cubed), so the attempt fails here, as an hourly one would. (" + commandWatch.Elapsed.TotalMilliseconds.ToString("0.00") + " ms)";
        }
        forced = true;
        reportTo = caller;
        presetSite = site;
        taskAi.TaskManager.ExecuteTask(this, Slot);
        commandWatch.Stop();
        string next = "It is on its way; the result follows in chat (and in the debug log).";
        if (IsInReachStanding())
        {
            next = "It is within reach and builds there now; the result follows in chat.";
        }
        return picked + next + " (" + commandWatch.Elapsed.TotalMilliseconds.ToString("0.00") + " ms)";
    }

    // ---- Helpers ----

    // Up to tries random cells (HammockBuilder.TryPickBuildSite), seen from the spider's eyes.
    private HammockSite PickSite(int tries, out int used)
    {
        return HammockBuilder.TryPickBuildSite(entity.World, SpiderTaskUtil.FeetCell(entity),
            entity.Pos.X, entity.Pos.Y + entity.LocalEyePos.Y, entity.Pos.Z, jumpWorld, tries, out used);
    }

    // While walking or deciding, a deadline can end the attempt; in the air or while building it waits for that to end.
    private bool CanGiveUpNow()
    {
        if (runner.IsFlying())
        {
            return false;
        }
        return Phase != BuildPhase.Building && Phase != BuildPhase.Clinging && Phase != BuildPhase.Done;
    }

    private bool IsInReachStanding()
    {
        if (Site == null || !entity.OnGround)
        {
            return false;
        }
        return InReachAt(entity.Pos.X, entity.Pos.Y, entity.Pos.Z);
    }

    // Could a spider with its feet here build at the site: within reach, a clear line, and not in the hammock's way.
    private bool InReachAt(double feetX, double feetY, double feetZ)
    {
        if (BuildReach.BoxOverlapsCell(feetX, feetY, feetZ, HalfWidth(), BodyHeight(), StandingBuildSiteMargin,
            Site.Pos.X, Site.Pos.Y, Site.Pos.Z))
        {
            return false;
        }
        if (jumpWorld == null)
        {
            jumpWorld = new SpiderJumpWorld(entity);
        }
        return BuildReach.CanReach(jumpWorld, feetX, feetY, feetZ, BodyHeight(), Site.Pos.X, Site.Pos.Y, Site.Pos.Z,
            JumpingSpidersModSystem.Config.HammockBuildRange);
    }

    private double HorizontalDistance(double x, double z)
    {
        double dx = x - entity.Pos.X;
        double dz = z - entity.Pos.Z;
        return Math.Sqrt(dx * dx + dz * dz);
    }

    private BlockEntitySilkHammock FindHammockTooClose(BlockPos pos)
    {
        int spacing = JumpingSpidersModSystem.Config.HammockSpacing;
        List<BlockEntitySilkHammock> nearby = HammockFinder.FindAllWithin(entity.World, HammockFinder.CellCenter(pos),
            HammockFinder.SpacingSearchRange(spacing));
        for (int i = 0; i < nearby.Count; i++)
        {
            BlockPos other = nearby[i].Pos;
            if (other.dimension == pos.dimension && HammockSpacing.Conflicts(pos.X, pos.Y, pos.Z, other.X, other.Y, other.Z, spacing))
            {
                return nearby[i];
            }
        }
        return null;
    }

    private string DescribeSite(HammockSite site)
    {
        string mount = "on the " + site.SupportFacing.Code + " wall";
        if (site.SupportFacing == BlockFacing.UP)
        {
            mount = "under a ceiling";
        }
        else if (site.SupportFacing == BlockFacing.DOWN)
        {
            mount = "on the floor";
        }
        int above = site.Pos.Y - JumpPlanner.CellOfFeetY(entity.Pos.Y);
        return site.Pos + " " + mount + ", " + above + " block(s) above the spider's feet";
    }

    private void Report(string text)
    {
        if (reportTo != null)
        {
            reportTo.SendMessage(GlobalConstants.GeneralChatGroup, text, EnumChatType.Notification);
            return;
        }
        entity.World.Logger.Notification("[jumpingspiders] " + text);
    }

    private double HalfWidth()
    {
        return (entity.CollisionBox.X2 - entity.CollisionBox.X1) / 2;
    }

    private double BodyHeight()
    {
        return entity.CollisionBox.Y2 - entity.CollisionBox.Y1;
    }

    private static bool IsSameCell(BuildStance a, BuildStance b)
    {
        if (a == null || b == null)
        {
            return false;
        }
        return a.CellX == b.CellX && a.CellY == b.CellY && a.CellZ == b.CellZ;
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
