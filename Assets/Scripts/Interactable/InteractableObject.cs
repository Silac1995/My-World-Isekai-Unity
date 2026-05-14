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
    /// X/Z position sits inside this interactable's <see cref="InteractionZone"/>
    /// projected on the ground plane. One-sided containment per the Interactable
    /// System rule — NOT a mutual zone-vs-zone overlap.
    ///
    /// **2D X-Z check, not 3D AABB.** Matches the established construction-loop
    /// convention (<c>BuildingInteractable</c> "2D X-Z proximity check" in
    /// wiki/CLAUDE.md). The 3D variant was Y-fragile: characters stand at
    /// <c>transform.position.y ≈ 0</c> (feet on ground), which sits exactly on
    /// <c>bounds.min.y</c> for most interactable colliders authored from y=0
    /// upward — float precision on networked transforms then tipped the value a
    /// hair below zero on joining clients, false-negativing the gate even when
    /// the character was visually inside the zone. The 2D projection eliminates
    /// that whole class of failure with no downside: every interactable in this
    /// project is floor-anchored and characters always walk on the ground, so Y
    /// containment was never carrying useful information.
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
    public virtual bool IsCharacterInInteractionZone(Character character)
    {
        if (character == null || _interactionZone == null) return false;
        var pos = character.transform.position;
        var b = _interactionZone.bounds;
        return pos.x >= b.min.x && pos.x <= b.max.x
            && pos.z >= b.min.z && pos.z <= b.max.z;
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