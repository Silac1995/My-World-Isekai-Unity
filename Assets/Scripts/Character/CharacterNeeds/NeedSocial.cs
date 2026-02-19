using UnityEngine;
using System.Linq;

public class NeedSocial : CharacterNeed
{
    private float _currentValue;
    private float _maxValue = 100f;
    private float _lowThreshold = 30f;

    private float _socialTimer = 0f;
    private const float _tickInterval = 1f;
    private float _socialLossPerTick = 300f;

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

    public override bool Resolve(NPCController npc)
    {
        if (npc.HasBehaviour<FollowTargetBehaviour>() || npc.HasBehaviour<MoveToTargetBehaviour>()) return false;

        Character target = FindClosestSocialPartner(npc.transform.position);

        if (target != null)
        {
            Debug.Log($"<color=cyan>[Need Social]</color> {npc.name} voit {target.CharacterName} et s'approche.");

            npc.PushBehaviour(new MoveToTargetBehaviour(npc, target.gameObject, 7f, () =>
            {
                if (target == null) return;

                // On lance l'interaction avec un callback qui s'exécutera UNE FOIS POSITIONNÉ (10f X, Align Z)
                npc.Character.CharacterInteraction.StartInteractionWith(target, () => 
                {
                    npc.Character.CharacterInteraction.PerformInteraction(new InteractionTalk());
                    npc.Character.CharacterInteraction.EndInteraction();
                    IncreaseValue(50f); 
                });
            }));
            return true;
        }
        return false;
    }

    private Character FindClosestSocialPartner(Vector3 currentPosition)
    {
        var awareness = _character.GetComponentInChildren<CharacterAwareness>();
        if (awareness == null) return null;

        var nearbyPartners = awareness.GetVisibleInteractables<CharacterInteractable>();

        return nearbyPartners
            .Select(interactable => interactable.Character)
            .Where(c => c != null && c.IsAlive())
            .OrderBy(c => Vector3.Distance(currentPosition, c.transform.position))
            .FirstOrDefault();
    }
}

