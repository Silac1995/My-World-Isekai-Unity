using System.Collections;
using UnityEngine;

/// <summary>
/// MonoBehaviour attached to characters to handle invitation responses.
/// Receives invitations and responds after a configurable delay,
/// based on relationship quality and personality traits.
/// If no response is given before the timeout, the invitation is refused.
/// Also manages following behavior for the invitation source.
/// </summary>
public class CharacterInvitation : MonoBehaviour
{
    [SerializeField] private Character _character;

    [Header("Settings")]
    [Tooltip("Time in seconds the character takes to 'think' before responding.")]
    [SerializeField] private float _responseDelay = 1f;

    [Tooltip("Maximum time to wait for a response. After this, invitation is auto-refused.")]
    [SerializeField] private float _responseTimeout = 10f;

    public Character Character => _character;

    /// <summary>
    /// Is this character currently considering an invitation?
    /// </summary>
    public bool HasPendingInvitation { get; private set; }

    private Coroutine _pendingCoroutine;
    private Coroutine _followCoroutine;

    private void Awake()
    {
        if (_character == null) _character = GetComponent<Character>();
    }

    // ──────────────────────────────────────────────
    //  RECEIVING (Target side)
    // ──────────────────────────────────────────────

    /// <summary>
    /// Called by InteractionInvitation.Execute() to send an invitation to this character.
    /// Starts a delayed evaluation coroutine.
    /// </summary>
    public void ReceiveInvitation(InteractionInvitation invitation, Character source)
    {
        if (invitation == null || source == null) return;

        // If already considering an invitation, refuse this new one
        if (HasPendingInvitation)
        {
            Debug.Log($"<color=orange>[Invitation]</color> {_character.CharacterName} is already considering another invitation, auto-refusing.");
            invitation.OnRefused(source, _character);
            return;
        }

        // Start the delayed response coroutine
        _pendingCoroutine = StartCoroutine(ProcessInvitation(invitation, source));
    }

    private IEnumerator ProcessInvitation(InteractionInvitation invitation, Character source)
    {
        HasPendingInvitation = true;

        // Wait for the "thinking" delay
        yield return new WaitForSeconds(_responseDelay);

        // Safety checks after delay
        if (source == null || !source.IsAlive() || _character == null || !_character.IsAlive())
        {
            HasPendingInvitation = false;
            // Stop the source from following since the invitation is over
            if (source != null && source.CharacterInvitation != null)
                source.CharacterInvitation.StopFollowingTarget();
            yield break;
        }

        // Evaluate the response
        bool accepted = EvaluateInvitation(invitation, source);

        // Stop the source from following — invitation is resolved
        if (source.CharacterInvitation != null)
            source.CharacterInvitation.StopFollowingTarget();

        // React
        if (accepted)
        {
            if (_character.CharacterSpeech != null)
                _character.CharacterSpeech.Say(invitation.GetAcceptMessage());

            invitation.OnAccepted(source, _character);
            Debug.Log($"<color=green>[Invitation]</color> {_character.CharacterName} accepted {source.CharacterName}'s invitation!");
        }
        else
        {
            if (_character.CharacterSpeech != null)
                _character.CharacterSpeech.Say(invitation.GetRefuseMessage());

            invitation.OnRefused(source, _character);
            Debug.Log($"<color=orange>[Invitation]</color> {_character.CharacterName} refused {source.CharacterName}'s invitation.");
        }

        HasPendingInvitation = false;
        _pendingCoroutine = null;
    }

    // ──────────────────────────────────────────────
    //  FOLLOWING (Source/Sender side)
    // ──────────────────────────────────────────────

    /// <summary>
    /// Called on the SOURCE's CharacterInvitation to follow the target
    /// while the invitation is pending. Stops automatically when the
    /// target's HasPendingInvitation becomes false.
    /// </summary>
    public void StartFollowingTarget(Character target)
    {
        StopFollowingTarget();
        
        // Players retain manual control instead of auto-following
        if (!_character.IsPlayer())
        {
            _followCoroutine = StartCoroutine(FollowTargetRoutine(target));
        }
    }

    /// <summary>
    /// Explicitly stops the follow coroutine. Called by ProcessInvitation
    /// on the target's side when the invitation resolves.
    /// </summary>
    public void StopFollowingTarget()
    {
        if (_followCoroutine != null)
        {
            StopCoroutine(_followCoroutine);
            _followCoroutine = null;
        }
    }

    private IEnumerator FollowTargetRoutine(Character target)
    {
        while (target != null &&
               target.CharacterInvitation != null &&
               target.CharacterInvitation.HasPendingInvitation &&
               _character.CharacterMovement != null)
        {
            bool inRange = false;

            if (target.CharacterInteractable != null && target.CharacterInteractable.InteractionZone != null)
            {
                Collider zone = target.CharacterInteractable.InteractionZone;
                Vector3 closestPoint = zone.ClosestPoint(_character.transform.position);
                float distanceToZone = Vector3.Distance(_character.transform.position, closestPoint);

                // Margin to account for the character's own radius
                if (distanceToZone <= 0.5f)
                {
                    inRange = true;
                }
            }
            else
            {
                // Fallback to a hardcoded distance if there is no interaction zone
                if (Vector3.Distance(_character.transform.position, target.transform.position) <= 2.0f)
                {
                    inRange = true;
                }
            }

            if (inRange)
            {
                _character.CharacterMovement.Stop();
            }
            else
            {
                _character.CharacterMovement.SetDestination(target.transform.position);
            }

            yield return new WaitForSeconds(0.5f);
        }
        _followCoroutine = null;
    }

    // ──────────────────────────────────────────────
    //  EVALUATION
    // ──────────────────────────────────────────────

    /// <summary>
    /// Evaluates whether this character accepts an invitation from the source.
    /// Uses relationship quality and sociability to determine acceptance.
    /// </summary>
    private bool EvaluateInvitation(InteractionInvitation invitation, Character source)
    {
        // --- CUSTOM INTERACTION EVALUATION ---
        bool? customEval = invitation.EvaluateCustomInvitation(source, _character);
        if (customEval.HasValue)
        {
            Debug.Log($"<color=cyan>[Invitation]</color> {_character.CharacterName} evaluates custom invitation from {source.CharacterName} → {(customEval.Value ? "ACCEPTED" : "REFUSED")}");
            return customEval.Value;
        }

        float acceptChance = 0.8f; // Base 80% chance for strangers

        // --- RELATION-BASED ---
        if (_character.CharacterRelation != null)
        {
            var rel = _character.CharacterRelation.GetRelationshipWith(source);
            if (rel != null)
            {
                if (_character.CharacterRelation.IsFriend(source))
                {
                    acceptChance = 1.0f; // Friends: 100% base
                }
                else if (_character.CharacterRelation.IsEnemy(source))
                {
                    acceptChance = 0.05f; // Enemies: 5% base
                }
                else
                {
                    // Acquaintance: scale based on relation value (0-20 → 80-95%)
                    acceptChance = Mathf.Lerp(0.8f, 0.95f, Mathf.Clamp01(rel.RelationValue / 20f));
                }
            }
        }

        // --- SOCIABILITY MODIFIER ---
        // High sociability (+15%) makes characters more open to invitations
        // Low sociability (-15%) makes them more reluctant
        if (_character.CharacterTraits != null)
        {
            float sociabilityBonus = (_character.CharacterTraits.GetSociability() - 0.5f) * 0.3f;
            acceptChance += sociabilityBonus;
        }

        acceptChance = Mathf.Clamp01(acceptChance);

        bool accepted = Random.value < acceptChance;
        Debug.Log($"<color=cyan>[Invitation]</color> {_character.CharacterName} evaluates invitation from {source.CharacterName}: {acceptChance:P0} chance → {(accepted ? "ACCEPTED" : "REFUSED")}");

        return accepted;
    }

    /// <summary>
    /// Force-cancel any pending invitation (e.g. if character dies or leaves).
    /// </summary>
    public void CancelPendingInvitation()
    {
        if (_pendingCoroutine != null)
        {
            StopCoroutine(_pendingCoroutine);
            _pendingCoroutine = null;
        }
        HasPendingInvitation = false;
    }

    private void OnDestroy()
    {
        StopFollowingTarget();
        CancelPendingInvitation();
    }
}
