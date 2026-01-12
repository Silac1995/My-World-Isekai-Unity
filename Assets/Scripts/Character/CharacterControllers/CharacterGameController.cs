using UnityEngine;
using UnityEngine.AI;

public abstract class CharacterGameController : MonoBehaviour
{
    [SerializeField] protected CharacterVisual characterVisual;
    [SerializeField] protected Character character;
    [SerializeField] protected NavMeshAgent agent;

    // IA - Propriété publique pour la lecture
    protected IAIBehaviour currentBehaviour;
    public IAIBehaviour CurrentBehaviour => currentBehaviour;

    public Character Character => character;

    // On passe maintenant par le CharacterVisual pour récupérer l'Animator
    public Animator Animator => (characterVisual != null && characterVisual.CharacterAnimator != null)
                                 ? characterVisual.CharacterAnimator.Animator
                                 : null;

    public NavMeshAgent Agent => agent;

    public virtual void Initialize()
    {
        character = GetComponent<Character>();

        // On s'assure de bien récupérer le visual qui contient le CharacterAnimator
        characterVisual = GetComponentInChildren<CharacterVisual>();

        agent = GetComponent<NavMeshAgent>();
        if (agent == null)
            agent = gameObject.AddComponent<NavMeshAgent>();

        agent.updateRotation = false;
        agent.updateUpAxis = false;

        if (character != null && !character.IsAlive())
            enabled = false;

        agent.speed = character != null ? character.MovementSpeed : 3.5f;

    }

    protected virtual void Update()
    {
        currentBehaviour?.Act(character);

        Move();
        UpdateAnimations();
        UpdateFlip();
    }

    protected virtual void UpdateAnimations()
    {
        // On vérifie le mouvement de l'agent (IA) ET du Rigidbody (Joueur)
        bool isMoving = (agent != null && agent.velocity.magnitude > 0.1f) ||
                        (character.Rigidbody != null && character.Rigidbody.linearVelocity.magnitude > 0.1f);

        if (isMoving && character.CharacterActions.CurrentAction != null)
        {
            character.CharacterActions.ClearCurrentAction();
        }

        if (Animator != null)
        {
            Animator.SetBool("isWalking", isMoving);
        }
    }

    protected virtual void UpdateFlip()
    {
        if (characterVisual != null && agent != null && agent.velocity.sqrMagnitude > 0.01f)
            characterVisual.UpdateFlip(agent.velocity);
    }

    public virtual void Move()
    {
        if (agent != null && !agent.isOnNavMesh)
        {
            if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 5f, NavMesh.AllAreas))
            {
                float offset = 0.5f;
                transform.position = hit.position + Vector3.up * offset;
                agent.Warp(transform.position);
            }
        }
    }

    public void SetBehaviour(IAIBehaviour behaviour)
    {
        if (character != null && character.IsPlayer())
        {
            Debug.Log($"<color=yellow>[AI]</color> Changement de comportement ignoré pour le Joueur ({gameObject.name}).");
            return;
        }

        if (currentBehaviour != null)
        {
            currentBehaviour.Exit(character);
        }

        string behaviourName = behaviour != null ? behaviour.GetType().Name : "None";
        Debug.Log($"<color=cyan>[AI]</color> {gameObject.name} change de comportement pour : {behaviourName}");

        currentBehaviour = behaviour;
    }
}