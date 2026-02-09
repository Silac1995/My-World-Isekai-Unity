using UnityEngine;
using System.Linq;

public class NeedSocial : CharacterNeed
{
    private float _currentValue;
    private float _maxValue = 100f;
    private float _lowThreshold = 30f;

    private float _socialTimer = 0f;
    private const float _tickInterval = 1f;
    private float _socialLossPerTick = 30f;

    public NeedSocial(Character character, float startValue = 80f) : base(character)
    {
        _currentValue = startValue;
    }

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

    public override void Resolve(NPCController npc)
    {
        if (npc.HasBehaviour<FollowTargetBehaviour>() || npc.HasBehaviour<MoveToTargetBehaviour>()) return;

        // On cherche un partenaire uniquement via l'Awareness
        Character target = FindClosestSocialPartner(npc.transform.position);

        if (target != null)
        {
            Debug.Log($"<color=cyan>[Need Social]</color> {npc.name} voit {target.CharacterName} et va lui parler.");
            npc.PushBehaviour(new FollowTargetBehaviour(target, 2.0f));
        }
    }

    private Character FindClosestSocialPartner(Vector3 currentPosition)
    {
        // 1. Récupérer le composant Awareness sur le personnage
        var awareness = _character.GetComponentInChildren<CharacterAwareness>();
        if (awareness == null) return null;

        // 2. Utiliser ta méthode générique pour trouver les CharacterInteractable dans le collider
        var nearbyPartners = awareness.GetVisibleInteractables<CharacterInteractable>();

        // 3. Filtrer et trier par distance
        return nearbyPartners
            .Select(interactable => interactable.Character) // On récupère le Character lié
            .Where(c => c != null && c.IsAlive())           // On vérifie qu'il est vivant
            .OrderBy(c => Vector3.Distance(currentPosition, c.transform.position))
            .FirstOrDefault();
    }
}