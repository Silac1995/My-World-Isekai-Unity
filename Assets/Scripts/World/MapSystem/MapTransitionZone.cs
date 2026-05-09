using Unity.Netcode;
using UnityEngine;
using MWI.WorldSystem;

/// <summary>
/// Trigger collider at the edge of a Region MapController (no door).
/// Handles party gathering for leaders and solo transitions for non-party characters.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class MapTransitionZone : MonoBehaviour
{
    [SerializeField] private string _targetMapId;
    [SerializeField] private Vector3 _targetPosition;
    [SerializeField] private Transform _targetSpawnPoint;

    public string TargetMapId => _targetMapId;
    public Vector3 TargetPosition => _targetSpawnPoint != null ? _targetSpawnPoint.position : _targetPosition;

    private void OnTriggerEnter(Collider other)
    {
        if (!other.TryGetComponent(out Character character)) return;
        if (!character.IsAlive()) return;

        // Only server processes logic
        if (NetworkManager.Singleton != null && !NetworkManager.Singleton.IsServer) return;

        CharacterParty party = character.CharacterParty;

        if (party != null && party.IsInParty)
        {
            // Skip all party logic when inside an Interior map
            var zoneMap = GetComponentInParent<MapController>();
            bool isInInterior = zoneMap != null && zoneMap.Type == MapType.Interior;

            if (!isInInterior)
            {
                if (party.IsPartyLeader)
                {
                    MapController targetMap = MapController.GetByMapId(_targetMapId);
                    if (targetMap != null && (targetMap.Type == MapType.Region || targetMap.Type == MapType.Dungeon))
                    {
                        party.StartGathering(_targetMapId, TargetPosition);
                        return;
                    }
                }
                else
                {
                    Debug.Log($"<color=yellow>[MapTransitionZone]</color> Party member {character.CharacterName} approaching border");
                    return;
                }
            }
        }

        // Solo character → normal transition
        var transitionAction = new CharacterMapTransitionAction(
            character, null, _targetMapId, TargetPosition, 0.5f);
        character.CharacterActions.ExecuteAction(transitionAction);
    }
}
