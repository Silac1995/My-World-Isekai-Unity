using UnityEngine;
using System.Linq;

public class NeedSocial : CharacterNeed
{
    private float _currentValue;
    private float _maxValue = 100f;
    private float _lowThreshold = 30f;

    private float _socialTimer = 0f;
    private const float _tickInterval = 1f;
    private float _socialLossPerTick = 20f; // Valeur de test élevée demandée par l'user

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
            // --- DÉCISION DE L'ACTION ---
            int relationScore = 0;
            bool hasPositiveRelation = false;

            if (_character.CharacterRelation != null)
            {
                var rel = _character.CharacterRelation.GetRelationshipWith(target);
                if (rel != null)
                {
                    relationScore = rel.RelationValue;
                    hasPositiveRelation = relationScore > 0;
                }
            }

            ICharacterInteractionAction actionToPerform;
            
            if (hasPositiveRelation)
            {
                // Priorité aux gens qu'on aime : logique standard
                actionToPerform = npc.DetermineSocialAction(relationScore);
            }
            else
            {
                // Désespoir social : on va voir n'importe qui et on a 60% de chance d'être gentil
                // pour satisfaire notre besoin coûte que coûte.
                actionToPerform = (Random.value < 0.6f) ? new InteractionTalk() : (ICharacterInteractionAction)new InteractionInsult();
            }

            Debug.Log($"<color=cyan>[Need Social]</color> {npc.name} engage {target.CharacterName} par besoin (Positif: {hasPositiveRelation}, Score: {relationScore}, Action: {actionToPerform.GetType().Name}).");

            npc.PushBehaviour(new MoveToTargetBehaviour(npc, target.gameObject, 7f, () =>
            {
                if (target == null) return;

                npc.Character.CharacterInteraction.StartInteractionWith(target, () => 
                {
                    npc.Character.CharacterInteraction.PerformInteraction(actionToPerform);
                    npc.Character.CharacterInteraction.EndInteraction();
                    IncreaseValue(50f); 
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
            .Where(c => c != null && c.IsAlive())
            .ToList();

        if (nearbyPartners.Count == 0) return null;

        // --- PRIORITÉ AUX RELATIONS POSITIVES ---
        var positivePartners = nearbyPartners
            .Where(c => _character.CharacterRelation != null && _character.CharacterRelation.GetRelationshipWith(c)?.RelationValue > 0)
            .OrderBy(c => Vector3.Distance(currentPosition, c.transform.position))
            .FirstOrDefault();

        if (positivePartners != null) return positivePartners;

        // --- SINON DÉSESPOIR : PRENDRE LE PLUS PROCHE (Neutre ou Négatif) ---
        return nearbyPartners
            .OrderBy(c => Vector3.Distance(currentPosition, c.transform.position))
            .FirstOrDefault();
    }
}
