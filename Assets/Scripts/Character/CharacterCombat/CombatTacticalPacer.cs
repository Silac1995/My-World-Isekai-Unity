using UnityEngine;

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
    private const float IDLE_SWAY_RADIUS = 0.7f;
    private const float IDLE_SWAY_SPEED = 0.5f;
    private const float CIRCLE_SPEED = 1.5f;
    private const float CIRCLE_RADIUS_OFFSET = 2.0f;
    private const float MELEE_STEPBACK_DISTANCE = 2.0f;
    private const float MELEE_STANDOFF_BUFFER = 3.0f; // Melee idles at meleeRange + this buffer
    private const float UNENGAGED_FOLLOW_MELEE_DISTANCE = 5.0f;
    private const float LEASH_PULL_STRENGTH = 0.3f;
    private const float PATH_UPDATE_INTERVAL = 0.8f;

    /// <summary>
    /// Mirrors CombatFormation.MELEE_PREFERRED_DISTANCE (private in CombatFormation).
    /// Keep in sync if that value ever changes.
    /// </summary>
    private const float MELEE_PREFERRED_DISTANCE = 4.0f;

    private Character _self;
    private Vector3 _swayCenter;
    private float _perlinSeedX;
    private float _perlinSeedZ;
    private float _circleAngle;
    private float _lastPathUpdateTime;
    private bool _needsStepBack;

    public CombatTacticalPacer(Character self)
    {
        _self = self;
        _perlinSeedX = Random.Range(0f, 100f);
        _perlinSeedZ = Random.Range(0f, 100f);
        _circleAngle = Random.Range(0f, Mathf.PI * 2f);
        _swayCenter = self.transform.position;
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

        float now = Time.time;
        if (now - _lastPathUpdateTime < PATH_UPDATE_INTERVAL)
            return _self.transform.position;
        _lastPathUpdateTime = now;

        Vector3 destination;
        bool isRanged = IsRangedCharacter(_self);

        // Priority 1: Post-attack step-back (melee only)
        if (_needsStepBack && !isRanged)
        {
            _needsStepBack = false;
            destination = CalculateStepBack(target);
            _swayCenter = destination;
            return ApplyLeash(destination, engagement);
        }

        // Priority 2: Unengaged — follow target at distance
        if (engagement == null)
        {
            return CalculateUnengagedFollow(target, attackRange, isRanged);
        }

        // Priority 3: Tactical circling (outnumbering 2:1+, melee only)
        float outnumberRatio = engagement.GetOutnumberRatio(_self);
        if (!isRanged && outnumberRatio >= 2.0f)
        {
            destination = CalculateCirclingPosition(engagement, target);
            return ApplyLeash(destination, engagement);
        }

        // Priority 4: Idle standoff — maintain proper distance from target + sway
        destination = CalculateIdleStandoff(target, attackRange, isRanged);
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

        return CalculateIdleSway();
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
    /// Maintains standoff distance from target while adding subtle sway.
    /// Melee: stays at meleeRange + MELEE_STANDOFF_BUFFER from target.
    /// Ranged: stays at weapon range from target.
    /// </summary>
    private Vector3 CalculateIdleStandoff(Character target, float attackRange, bool isRanged)
    {
        Vector3 selfPos = _self.transform.position;
        Vector3 targetPos = target.transform.position;
        float standoffDist = isRanged ? attackRange : (attackRange + MELEE_STANDOFF_BUFFER);
        float currentDist = Vector3.Distance(selfPos, targetPos);

        // Calculate the ideal standoff point: stay at standoff distance in current direction from target
        Vector3 dirFromTarget = (selfPos - targetPos).normalized;
        if (dirFromTarget.sqrMagnitude < 0.01f)
            dirFromTarget = new Vector3((_self.GetInstanceID() % 2 == 0) ? 1f : -1f, 0f, 0f);

        Vector3 standoffPoint = targetPos + dirFromTarget * standoffDist;

        // If significantly out of position, move toward standoff point
        float drift = Mathf.Abs(currentDist - standoffDist);
        if (drift > 1.5f)
        {
            _swayCenter = standoffPoint;
            return standoffPoint;
        }

        // At correct distance — apply subtle Perlin sway around standoff point
        _swayCenter = standoffPoint;
        float time = Time.time;
        float noiseX = Mathf.PerlinNoise(_perlinSeedX + time * IDLE_SWAY_SPEED, 0) * 2f - 1f;
        float noiseZ = Mathf.PerlinNoise(0, _perlinSeedZ + time * IDLE_SWAY_SPEED) * 2f - 1f;

        return standoffPoint + new Vector3(
            noiseX * IDLE_SWAY_RADIUS,
            0,
            noiseZ * IDLE_SWAY_RADIUS
        );
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
