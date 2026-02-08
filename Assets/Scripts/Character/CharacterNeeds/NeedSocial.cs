using UnityEngine;
using System.Linq;

public class NeedSocial : CharacterNeed
{
    private float _currentValue;
    private float _maxValue = 100f;
    private float _decreaseRate = 0.5f; // Perte par seconde
    private float _lowThreshold = 30f;  // Seuil critique

    private float _socialTimer = 0f;
    private const float _tickInterval = 1f; // Toutes les 5 secondes
    private float _socialLossPerTick = 30f; // Valeur à perdre toutes les 5s


    public NeedSocial(Character character, float startValue = 80f) : base(character)
    {
        _currentValue = startValue;
    }

    // --- Logique de la barre ---

    public void UpdateValue()
    {
        _socialTimer += Time.deltaTime;

        if (_socialTimer >= _tickInterval)
        {
            DecreaseValue(_socialLossPerTick);
            _socialTimer = 0f; // Reset le timer
                               // Debug.Log($"Social décrémenté. Valeur actuelle : {_currentValue}");
        }
    }

    public void IncreaseValue(float amount) => _currentValue = Mathf.Clamp(_currentValue + amount, 0, _maxValue);
    public void DecreaseValue(float amount) => _currentValue = Mathf.Clamp(_currentValue - amount, 0, _maxValue);

    public bool IsLow() => _currentValue <= _lowThreshold;
    public bool NeedsSocialInteraction() => IsLow();

    // --- Implémentation de CharacterNeed ---

    public override bool IsActive()
    {
        // L'IA ne doit essayer de résoudre le besoin que s'il est bas
        return NeedsSocialInteraction() && !_character.CharacterInteraction.IsInteracting;
    }

    public override float GetUrgency()
    {
        // Pour le DEBUG : On affiche la "faim sociale" même si le besoin n'est pas encore critique
        // (100 - currentValue) donne le pourcentage de manque
        return 100f - _currentValue;
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