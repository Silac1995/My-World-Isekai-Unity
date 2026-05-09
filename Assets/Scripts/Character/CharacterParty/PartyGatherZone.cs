using UnityEngine;

/// <summary>
/// Placed on a child GameObject with a BoxCollider (isTrigger) on the "PartyGather" physics layer.
/// Forwards trigger events to the parent CharacterParty component.
/// </summary>
public class PartyGatherZone : MonoBehaviour
{
    private CharacterParty _owner;

    public void Initialize(CharacterParty owner)
    {
        _owner = owner;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_owner == null) return;
        if (other.TryGetComponent(out Character character))
        {
            _owner.OnGatherZoneEnter(character);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (_owner == null) return;
        if (other.TryGetComponent(out Character character))
        {
            _owner.OnGatherZoneExit(character);
        }
    }
}
