using UnityEngine;

public abstract class InteractableObject : MonoBehaviour
{
    [Header("Interaction")]
    public string interactionPrompt = "Press E to interact";
    [SerializeField] private Collider _interactionZone;
    [SerializeField] private GameObject _rootgameObject;
    [SerializeField] private Rigidbody _rigidbody;

    public Rigidbody Rigidbody => _rigidbody;
    
    public GameObject RootGameObject
    {
        get
        {
            // If the field is empty, fall back to the default root
            if (_rootgameObject == null)
                _rootgameObject = transform.root.gameObject;
            return _rootgameObject;
        }
    }
    public Collider InteractionZone => _interactionZone;

    /// <summary>
    /// Canonical proximity gate. Returns true iff the given character's world-space
    /// position sits inside this interactable's <see cref="InteractionZone"/> (AABB
    /// containment). One-sided containment per the Interactable System rule —
    /// NOT a mutual zone-vs-zone overlap.
    ///
    /// Reads <see cref="Character.transform.position"/> (not <c>Character.Rigidbody.position</c>)
    /// so the check matches what <c>ClientNetworkTransform</c> syncs directly to the server.
    /// On a server-authoritative RPC validating a client's proximity, the client's
    /// Rigidbody on the server is kinematic and <c>rb.position</c> trails
    /// <c>transform.position</c> by up to one physics tick, which was enough to
    /// false-negative right-at-the-edge punches despite the client UI showing the
    /// prompt. <c>transform.position</c> is the source of truth on both peers.
    ///
    /// Use this from any code path (player input, server RPCs, GOAP, BT actions)
    /// that needs to decide whether a character is close enough to interact.
    /// If the interactable has no <see cref="InteractionZone"/> assigned, returns false.
    /// </summary>
    public bool IsCharacterInInteractionZone(Character character)
    {
        if (character == null || _interactionZone == null) return false;
        return _interactionZone.bounds.Contains(character.transform.position);
    }

    // We pass the Character that triggers the action
    public abstract void Interact(Character interactor);

    // The hover methods can also be updated to track who is hovering
    public virtual void OnCharacterEnter(Character interactor) { }
    public virtual void OnCharacterExit(Character interactor) { }

    public virtual System.Collections.Generic.List<InteractionOption> GetHoldInteractionOptions(Character interactor)
    {
        return null;
    }

    public virtual System.Collections.Generic.List<InteractionOption> GetDialogueInteractionOptions(Character interactor)
    {
        return null;
    }
}