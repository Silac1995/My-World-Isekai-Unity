using UnityEngine;

/// <summary>
/// CharacterAction pour récolter un Harvestable.
/// Le personnage joue une animation de récolte, attend la durée,
/// puis récolte l'item du Harvestable.
/// </summary>
public class CharacterHarvestAction : CharacterAction
{
    private Harvestable _target;
    private ItemSO _harvestedItem;

    /// <summary>L'item récolté après l'action (null si pas encore fini)</summary>
    public ItemSO HarvestedItem => _harvestedItem;

    public CharacterHarvestAction(Character character, Harvestable target)
        : base(character, target != null ? target.HarvestDuration : 1f)
    {
        _target = target;
    }

    public override bool CanExecute()
    {
        if (_target == null || !_target.CanHarvest())
        {
            Debug.LogWarning($"<color=orange>[Harvest Action]</color> {character.CharacterName} ne peut pas récolter : cible invalide ou épuisée.");
            return false;
        }

        // Vérification de la portée via l'InteractionZone (Collider) de l'objet
        if (_target.InteractionZone != null)
        {
            if (!_target.InteractionZone.bounds.Contains(character.transform.position))
            {
                // Fallback : On vérifie si on est au moins très proche du collider (pour les petits colliders ou offsets)
                float dist = Vector3.Distance(character.transform.position, _target.InteractionZone.bounds.ClosestPoint(character.transform.position));
                if (dist > 2.5f)
                {
                    Debug.LogWarning($"<color=orange>[Harvest Action]</color> {character.CharacterName} est trop loin de la zone d'interaction de {_target.gameObject.name} (Dist: {dist}).");
                    return false;
                }
            }
        }
        else
        {
            // Fallback si pas de zone : distance fixe
            float dist = Vector3.Distance(character.transform.position, _target.transform.position);
            if (dist > 3f)
            {
                Debug.LogWarning($"<color=orange>[Harvest Action]</color> {character.CharacterName} est trop loin pour récolter {_target.gameObject.name} (pas de zone d'interaction).");
                return false;
            }
        }

        return true;
    }

    public override void OnStart()
    {
        Debug.Log($"<color=cyan>[Harvest Action]</color> {character.CharacterName} commence à récolter {_target.gameObject.name}...");
    }

    public override void OnApplyEffect()
    {
        if (_target == null || !_target.CanHarvest())
        {
            Debug.LogWarning($"<color=orange>[Harvest Action]</color> {character.CharacterName} : la cible a disparu ou est épuisée.");
            return;
        }

        var actions = character.CharacterActions;
        if (actions == null) return;

        // When a networked client runs this action, the server is the only peer allowed
        // to spawn WorldItems and mutate the (scene-shared) Harvestable state — delegate
        // via RPC. Offline mode (IsSpawned == false) falls through to the local path.
        bool isNetworkedClient = actions.IsSpawned && !actions.IsServer;
        if (isNetworkedClient)
        {
            actions.RequestHarvestServerRpc(_target.transform.position);
        }
        else
        {
            // Server (host/NPC) or offline: run directly so local callers that inspect
            // HarvestedItem after OnApplyEffect keep working.
            _harvestedItem = actions.ApplyHarvestOnServer(_target);
        }
    }

    public override void OnCancel()
    {
        base.OnCancel();
        Debug.Log($"<color=orange>[Harvest Action]</color> {character.CharacterName} a annulé sa récolte.");
    }
}
