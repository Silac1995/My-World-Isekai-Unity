using UnityEngine;

/// <summary>
/// CharacterAction pour récolter un GatherableObject.
/// Le personnage joue une animation de récolte, attend la durée,
/// puis récolte l'item du GatherableObject.
/// </summary>
public class CharacterGatherAction : CharacterAction
{
    private GatherableObject _target;
    private ItemSO _harvestedItem;

    /// <summary>L'item récolté après l'action (null si pas encore fini)</summary>
    public ItemSO HarvestedItem => _harvestedItem;

    public CharacterGatherAction(Character character, GatherableObject target)
        : base(character, target != null ? target.GatherDuration : 1f)
    {
        _target = target;
    }

    public override bool CanExecute()
    {
        if (_target == null || !_target.CanGather())
        {
            Debug.LogWarning($"<color=orange>[Gather Action]</color> {character.CharacterName} ne peut pas récolter : cible invalide ou épuisée.");
            return false;
        }

        return true;
    }

    public override void OnStart()
    {
        // Jouer l'animation de récolte
        var animator = character.CharacterVisual?.CharacterAnimator?.Animator;
        if (animator != null)
        {
            animator.SetTrigger("IsDoingAction");
        }

        Debug.Log($"<color=cyan>[Gather Action]</color> {character.CharacterName} commence à récolter {_target.gameObject.name}...");
    }

    public override void OnApplyEffect()
    {
        if (_target == null || !_target.CanGather())
        {
            Debug.LogWarning($"<color=orange>[Gather Action]</color> {character.CharacterName} : la cible a disparu ou est épuisée.");
            return;
        }

        // Récolter l'item (retourne le ItemSO)
        _harvestedItem = _target.Gather(character);

        if (_harvestedItem != null)
        {
            // Spawn le WorldItem au sol devant le personnage
            Vector3 spawnPos = character.transform.position + character.transform.forward * 0.5f + Vector3.up * 0.3f;
            GatherableObject.SpawnWorldItem(_harvestedItem, spawnPos);
        }
    }

    public override void OnCancel()
    {
        base.OnCancel();
        Debug.Log($"<color=orange>[Gather Action]</color> {character.CharacterName} a annulé sa récolte.");
    }
}
