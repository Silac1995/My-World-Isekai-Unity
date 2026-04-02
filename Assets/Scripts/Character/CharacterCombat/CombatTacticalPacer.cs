using UnityEngine;
using System.Linq;

/// <summary>
/// Determines combat movement based on character state:
/// - Idle sway (waiting for initiative)
/// - Tactical circling (outnumbering 2:1+)
/// - Dynamic spacing (post-attack step-back for melee)
/// - Unengaged follow (trailing target without engagement)
/// - Ranged hold (no reactive kiting)
/// </summary>
public class CombatTacticalPacer
{
    // Scale: 11 units = 1.67m (human height). 1 unit ≈ 15cm.
    private const float IDLE_DRIFT_RADIUS = 30f;         // ~4.5m drift range
    private const float IDLE_DRIFT_MIN_INTERVAL = 5.0f;
    private const float IDLE_DRIFT_MAX_INTERVAL = 7.0f;
    private const float CIRCLE_SPEED = 1.5f;              // Angular speed (radians/s, scale-independent)
    private const float CIRCLE_RADIUS_OFFSET = 12f;       // ~1.8m outside preferred distance
    private const float MELEE_STEPBACK_DISTANCE = 8f;     // ~1.2m step back after attack
    private const float STANDOFF_MIN_DISTANCE = 5f;       // ~0.75m minimum from opponent center
    private const float UNENGAGED_FOLLOW_MELEE_DISTANCE = 25f; // ~3.8m follow distance when unengaged
    private const float LEASH_PULL_STRENGTH = 0.3f;
    private const float PATH_UPDATE_INTERVAL = 5.0f;

    /// <summary>
    /// Mirrors CombatFormation.MELEE_PREFERRED_DISTANCE (private in CombatFormation).
    /// Keep in sync if that value ever changes.
    /// </summary>
    private const float MELEE_PREFERRED_DISTANCE = 20f;   // ~3m melee engagement distance

    private Character _self;
    private Vector3 _swayCenter;
    private Vector3 _currentDriftTarget;
    private float _nextDriftTime;
    private float _circleAngle;
    private float _lastPathUpdateTime;
    private bool _needsStepBack;

    public CombatTacticalPacer(Character self)
    {
        _self = self;
        _circleAngle = Random.Range(0f, Mathf.PI * 2f);
        _swayCenter = self.transform.position;
        _currentDriftTarget = self.transform.position;
        _nextDriftTime = Time.time + Random.Range(0f, IDLE_DRIFT_MIN_INTERVAL);
    }

    /// <summary>
    /// Forces an immediate position evaluation on the next Tick.
    /// Call when entering battle or joining an engagement so characters move right away.
    /// </summary>
    public void ForceImmediateReposition()
    {
        _lastPathUpdateTime = 0f;
        _nextDriftTime = 0f;
    }

    /// <summary>
    /// Called after a melee attack executes to trigger a post-attack step-back.
    /// </summary>
    public void NotifyAttackCompleted()
    {
        _needsStepBack = true;
    }

    /// <summary>
    /// Resets the idle sway center to the character's current position.
    /// Call after repositioning events (attack execution, engagement change).
    /// </summary>
    public void ResetSwayCenter()
    {
        _swayCenter = _self.transform.position;
    }

    /// <summary>
    /// Main entry point: returns the desired movement destination.
    /// Called by CombatAILogic Phase 3 when no action intent is queued.
    /// </summary>
    /// <param name="target">Current combat target.</param>
    /// <param name="attackRange">Effective attack range of current weapon style.</param>
    /// <param name="engagement">The character's CombatEngagement, or null if unengaged.</param>
    /// <param name="isCharging">True if the character is actively charging toward the target to execute an action.</param>
    public Vector3 GetTacticalDestination(Character target, float attackRange,
        CombatEngagement engagement, bool isCharging)
    {
        if (isCharging || target == null)
            return _self.transform.position;

        Vector3 destination;
        bool isRanged = IsRangedCharacter(_self);
        float now = Time.time;

        // Priority 1: Post-attack step-back (melee only) — fires immediately, no throttle
        if (_needsStepBack && !isRanged)
        {
            _needsStepBack = false;
            destination = CalculateStepBack(target);
            _swayCenter = destination;
            _lastPathUpdateTime = now;
            return ApplyLeash(destination, engagement);
        }

        // Priority 2: Unengaged — follow target at distance (1s throttle, not 5s)
        if (engagement == null)
        {
            if (now - _lastPathUpdateTime < 1.0f)
                return _self.transform.position;
            _lastPathUpdateTime = now;
            return CalculateUnengagedFollow(target, attackRange, isRanged);
        }

        // Engaged idle movement uses the longer PATH_UPDATE_INTERVAL throttle
        if (now - _lastPathUpdateTime < PATH_UPDATE_INTERVAL)
            return _self.transform.position;
        _lastPathUpdateTime = now;

        // Priority 3: Tactical circling (outnumbering 2:1+, melee only)
        float outnumberRatio = engagement.GetOutnumberRatio(_self);
        if (!isRanged && outnumberRatio >= 2.0f)
        {
            destination = CalculateCirclingPosition(engagement, target);
            return ApplyLeash(destination, engagement);
        }

        // Priority 4: Idle standoff — maintain proper distance from target + lively movement
        destination = CalculateIdleStandoff(target, attackRange, isRanged, engagement);
        return ApplyLeash(destination, engagement);
    }

    /// <summary>
    /// After attacking, melee fighters step back away from the target
    /// to create visual breathing room and prevent model clipping.
    /// </summary>
    private Vector3 CalculateStepBack(Character target)
    {
        Vector3 selfPos = _self.transform.position;
        Vector3 awayFromTarget = (selfPos - target.transform.position).normalized;

        // Fallback direction if perfectly overlapping
        if (awayFromTarget.sqrMagnitude < 0.01f)
            awayFromTarget = new Vector3((_self.GetInstanceID() % 2 == 0) ? 1f : -1f, 0f, 0f);

        return selfPos + awayFromTarget * MELEE_STEPBACK_DISTANCE;
    }

    /// <summary>
    /// Characters not yet in an engagement trail their target at a safe follow distance.
    /// Ranged characters maintain their attack range; melee characters stay at a moderate gap.
    /// </summary>
    private Vector3 CalculateUnengagedFollow(Character target, float attackRange, bool isRanged)
    {
        Vector3 selfPos = _self.transform.position;
        Vector3 targetPos = target.transform.position;
        float followDistance = isRanged ? attackRange : UNENGAGED_FOLLOW_MELEE_DISTANCE;
        float currentDist = Vector3.Distance(selfPos, targetPos);

        if (currentDist > followDistance * 1.2f)
        {
            Vector3 dirToTarget = (targetPos - selfPos).normalized;
            return targetPos - dirToTarget * followDistance;
        }

        // Within follow distance — occasional drift
        if (Time.time >= _nextDriftTime)
        {
            Vector2 randomCircle = Random.insideUnitCircle * IDLE_DRIFT_RADIUS;
            _currentDriftTarget = selfPos + new Vector3(randomCircle.x, 0, randomCircle.y);
            _nextDriftTime = Time.time + Random.Range(IDLE_DRIFT_MIN_INTERVAL, IDLE_DRIFT_MAX_INTERVAL);
        }
        return _currentDriftTarget;
    }

    /// <summary>
    /// When outnumbering opponents 2:1 or more, melee fighters orbit the opponent center
    /// to create flanking pressure and avoid stacking on the same position.
    /// Each character starts at a random angle and advances at a fixed angular speed.
    /// </summary>
    private Vector3 CalculateCirclingPosition(CombatEngagement engagement, Character target)
    {
        Vector3 opponentCenter = engagement.GetOpponentCenter(_self);
        float circleRadius = MELEE_PREFERRED_DISTANCE + CIRCLE_RADIUS_OFFSET;

        _circleAngle += CIRCLE_SPEED * PATH_UPDATE_INTERVAL;
        if (_circleAngle > Mathf.PI * 2f) _circleAngle -= Mathf.PI * 2f;

        Vector3 circlePos = opponentCenter + new Vector3(
            Mathf.Cos(_circleAngle) * circleRadius,
            0,
            Mathf.Sin(_circleAngle) * circleRadius
        );

        _swayCenter = circlePos;
        return circlePos;
    }

    /// <summary>
    /// Lively idle movement within a distance band from the opponent center.
    /// Characters drift freely between min and max distance using Perlin noise.
    /// Min: STANDOFF_MIN_DISTANCE (close enough to feel engaged).
    /// Max: attackRange * 1.5 + 6 (far enough for breathing room).
    /// Only corrects position when outside the band — no oscillation.
    /// </summary>
    private Vector3 CalculateIdleStandoff(Character target, float attackRange, bool isRanged, CombatEngagement engagement)
    {
        Vector3 selfPos = _self.transform.position;
        float maxDist = isRanged ? attackRange : (attackRange * 3f + 35f); // ~6-7m from opponent center
        float minDist = STANDOFF_MIN_DISTANCE;

        // Use the opponent group center as reference (stable)
        Vector3 focalPoint = engagement != null
            ? engagement.GetOpponentCenter(_self)
            : target.transform.position;

        float currentDist = Vector3.Distance(selfPos, focalPoint);

        // Direction from focal point to self (our "side" of the fight)
        Vector3 dirFromFocal = (selfPos - focalPoint).normalized;
        if (dirFromFocal.sqrMagnitude < 0.01f)
            dirFromFocal = new Vector3((_self.GetInstanceID() % 2 == 0) ? 1f : -1f, 0f, 0f);

        // If outside the band, correct once then wait for next drift interval
        if (currentDist > maxDist)
        {
            _currentDriftTarget = focalPoint + dirFromFocal * maxDist;
            _swayCenter = _currentDriftTarget;
            _nextDriftTime = Time.time + Random.Range(IDLE_DRIFT_MIN_INTERVAL, IDLE_DRIFT_MAX_INTERVAL);
            return _currentDriftTarget;
        }
        if (currentDist < minDist)
        {
            _currentDriftTarget = focalPoint + dirFromFocal * minDist;
            _swayCenter = _currentDriftTarget;
            _nextDriftTime = Time.time + Random.Range(IDLE_DRIFT_MIN_INTERVAL, IDLE_DRIFT_MAX_INTERVAL);
            return _currentDriftTarget;
        }

        // Within band — pick a new drift destination every 5-7 seconds.
        // Each character gets an angular slot based on their index in the group
        // so allies spread out instead of stacking on top of each other.
        if (Time.time >= _nextDriftTime)
        {
            // Determine this character's slot angle within their side
            float baseAngle = GetSlotAngle(engagement, focalPoint);
            // Add random variation within the slot (±30°)
            float angleVariation = Random.Range(-0.52f, 0.52f);
            float finalAngle = baseAngle + angleVariation;
            float randomDist = Random.Range(minDist, maxDist);

            _currentDriftTarget = focalPoint + new Vector3(
                Mathf.Cos(finalAngle) * randomDist,
                0,
                Mathf.Sin(finalAngle) * randomDist
            );

            _swayCenter = _currentDriftTarget;
            _nextDriftTime = Time.time + Random.Range(IDLE_DRIFT_MIN_INTERVAL, IDLE_DRIFT_MAX_INTERVAL);
            return _currentDriftTarget;
        }

        // Timer not expired — hold position (don't re-send movement commands)
        return _self.transform.position;
    }

    /// <summary>
    /// Returns the base angle for this character's position around the focal point.
    /// Characters on the same side are evenly spread across a 180° arc on their side.
    /// </summary>
    private float GetSlotAngle(CombatEngagement engagement, Vector3 focalPoint)
    {
        if (engagement == null)
        {
            // No engagement — use instance ID for a deterministic angle
            return (Mathf.Abs(_self.GetInstanceID()) % 12) * (Mathf.PI * 2f / 12f);
        }

        // Find which group this character is in and their index
        bool inGroupA = engagement.GroupA.Members.Contains(_self);
        var myGroup = inGroupA ? engagement.GroupA : engagement.GroupB;

        int myIndex = 0;
        int aliveCount = 0;
        for (int i = 0; i < myGroup.Members.Count; i++)
        {
            var m = myGroup.Members[i];
            if (m == null || !m.IsAlive()) continue;
            if (m == _self) myIndex = aliveCount;
            aliveCount++;
        }

        if (aliveCount <= 1) aliveCount = 1;

        // GroupA occupies the left semicircle (PI/2 to 3PI/2), GroupB occupies the right (-PI/2 to PI/2)
        float sideCenter = inGroupA ? Mathf.PI : 0f;
        float arcSpread = Mathf.PI; // 180° arc per side
        float slotStep = arcSpread / Mathf.Max(1, aliveCount);
        float startAngle = sideCenter - arcSpread / 2f + slotStep / 2f;

        return startAngle + myIndex * slotStep;
    }

    /// <summary>
    /// Soft-constrains the destination within the engagement's leash radius.
    /// Characters that drift beyond the leash are gently pulled back toward the anchor,
    /// rather than hard-clamped, for smoother movement.
    /// </summary>
    private Vector3 ApplyLeash(Vector3 destination, CombatEngagement engagement)
    {
        if (engagement == null) return destination;

        Vector3 anchor = engagement.AnchorPoint;
        float leashRadius = engagement.LeashRadius;
        float distFromAnchor = Vector3.Distance(destination, anchor);

        if (distFromAnchor > leashRadius)
        {
            Vector3 toAnchor = (anchor - destination).normalized;
            float overshoot = distFromAnchor - leashRadius;
            destination += toAnchor * (overshoot * LEASH_PULL_STRENGTH);
        }

        return destination;
    }

    private bool IsRangedCharacter(Character character)
    {
        if (character?.CharacterCombat?.CurrentCombatStyleExpertise?.Style == null)
            return false;
        return character.CharacterCombat.CurrentCombatStyleExpertise.Style is RangedCombatStyleSO;
    }
}
