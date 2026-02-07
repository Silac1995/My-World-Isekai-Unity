using UnityEngine;
using System.Linq;

public class NeedSocial : CharacterNeed
{
    private float _currentValue;
    private float _maxValue = 100f;
    private float _decreaseRate = 0.5f; // Perte par seconde
    private float _lowThreshold = 30f;  // Seuil critique

    public NeedSocial(Character character, float startValue = 80f) : base(character)
    {
        _currentValue = startValue;
    }

    // --- Logique de la barre ---

    public void UpdateValue()
    {
        // On diminue le besoin social au fil du temps
        DecreaseValue(_decreaseRate * Time.deltaTime);
    }

    public void IncreaseValue(float amount) => _currentValue = Mathf.Clamp(_currentValue + amount, 0, _maxValue);
    public void DecreaseValue(float amount) => _currentValue = Mathf.Clamp(_currentValue - amount, 0, _maxValue);

    public bool IsLow() => _currentValue <= _lowThreshold;
    public bool NeedsSocialInteraction() => IsLow();

    // --- Implémentation de CharacterNeed ---

    public override bool IsActive()
    {
        // Actif si la barre est basse ET qu'on n'est pas déjà en train d'interagir
        return NeedsSocialInteraction() && !_character.CharacterInteraction.IsInteracting;
    }

    public override float GetUrgency()
    {
        if (!IsActive()) return 0f;
        // Plus la barre est proche de 0, plus l'urgence est proche de 100
        return (1f - (_currentValue / _maxValue)) * 100f;
    }

    public override void Resolve(NPCController npc)
    {
        if (npc.HasBehaviour<FollowTargetBehaviour>() || npc.HasBehaviour<MoveToTargetBehaviour>()) return;

        // Trouver le personnage le plus proche (joueur ou autre NPC)
        Character target = FindClosestSocialPartner(npc.transform.position);

        if (target != null)
        {
            Debug.Log($"<color=cyan>[Need]</color> {npc.name} se sent seul et va voir {target.CharacterName}");

            // On utilise ton FollowBehaviour pour s'approcher
            // Une fois arrivé, le système d'interaction pourra se déclencher
            npc.PushBehaviour(new FollowTargetBehaviour(target, 2.0f));
        }
    }

    private Character FindClosestSocialPartner(Vector3 currentPosition)
    {
        return Object.FindObjectsByType<Character>(FindObjectsSortMode.None)
            .Where(c => c != _character && c.IsAlive())
            .OrderBy(c => Vector3.Distance(currentPosition, c.transform.position))
            .FirstOrDefault();
    }
}