using UnityEngine;
using System.Linq;

public class NeedSocial : CharacterNeed
{
    private float _currentValue;
    private float _maxValue = 100f;
    private float _lowThreshold = 30f;

    private float _socialTimer = 0f;
    private const float _tickInterval = 1f;
    private float _socialLossPerTick = 3f; // Valeur de test élevée demandée par l'user

    public NeedSocial(Character character, float startValue = 80f) : base(character)
    {
        _currentValue = startValue;
    }

    /// <summary>
    /// Appelé par CharacterNeeds.Update() pour gérer la perte naturelle du besoin.
    /// </summary>
    public void UpdateValue()
    {
        _socialTimer += Time.deltaTime;
        if (_socialTimer >= _tickInterval)
        {
            DecreaseValue(_socialLossPerTick);
            _socialTimer = 0f;
        }
    }

    public void IncreaseValue(float amount) => _currentValue = Mathf.Clamp(_currentValue + amount, 0, _maxValue);
    public void DecreaseValue(float amount) => _currentValue = Mathf.Clamp(_currentValue - amount, 0, _maxValue);

    public bool IsLow() => _currentValue <= _lowThreshold;
    public bool NeedsSocialInteraction() => IsLow();

    public override bool IsActive()
    {
        return NeedsSocialInteraction() && !_character.CharacterInteraction.IsInteracting;
    }

    public override float GetUrgency()
    {
        return 100f - _currentValue;
    }

    public override bool Resolve(NPCController npc)
    {
        if (npc.HasBehaviour<FollowTargetBehaviour>() || npc.HasBehaviour<MoveToTargetBehaviour>()) return false;

        Character target = FindBestSocialPartner(npc.transform.position);

        if (target != null)
        {
            Debug.Log($"<color=cyan>[Need Social]</color> {npc.name} engage {target.CharacterName} par besoin social.");

            npc.PushBehaviour(new MoveToTargetBehaviour(npc, target.gameObject, 7f, () =>
            {
                if (target == null || !target.IsAlive()) return;

                // On lance l'interaction qui va maintenant gérer la séquence de dialogue automatiquement
                npc.Character.CharacterInteraction.StartInteractionWith(target, () => 
                {
                    IncreaseValue(50f); // Boost de satisfaction immédiat lors du début de l'échange
                });
            }));
            return true;
        }
        return false;
    }

    private Character FindBestSocialPartner(Vector3 currentPosition)
    {
        var awareness = _character.GetComponentInChildren<CharacterAwareness>();
        if (awareness == null) return null;

        var nearbyPartners = awareness.GetVisibleInteractables<CharacterInteractable>()
            .Select(interactable => interactable.Character)
            .Where(c => c != null && c.IsAlive() && c != _character)
            .ToList();

        if (nearbyPartners.Count == 0) return null;

        // --- SÉPARATION DES CATÉGORIES ---
        var knownPartners = nearbyPartners
            .Where(c => _character.CharacterRelation != null && _character.CharacterRelation.GetRelationshipWith(c)?.RelationValue > 0)
            .OrderBy(c => Vector3.Distance(currentPosition, c.transform.position))
            .ToList();

        var otherPartners = nearbyPartners
            .Except(knownPartners)
            .OrderBy(c => Vector3.Distance(currentPosition, c.transform.position))
            .ToList();

        // --- LOGIQUE 80% CONNAISSANCES / 20% AUTRES ---
        bool prioritizeKnown = Random.value < 0.8f;

        if (prioritizeKnown)
        {
            if (knownPartners.Count > 0) return knownPartners[0];
            if (otherPartners.Count > 0) return otherPartners[0];
        }
        else
        {
            if (otherPartners.Count > 0) return otherPartners[0];
            if (knownPartners.Count > 0) return knownPartners[0];
        }

        return null;
    }
}
