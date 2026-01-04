using UnityEngine;
using UnityEngine.AI;

public abstract class CharacterGameController : MonoBehaviour
{
    [SerializeField] protected CharacterVisual characterVisual;
    [SerializeField] protected Character character;
    [SerializeField] protected Animator animator;
    [SerializeField] protected NavMeshAgent agent;

    // IA - Propriété publique pour la lecture (utile pour le UI_CharacterDebugScript !)
    protected IAIBehaviour currentBehaviour;
    public IAIBehaviour CurrentBehaviour => currentBehaviour;

    public Character Character => character;
    public Animator Animator => animator;
    public NavMeshAgent Agent => agent;

    public virtual void Initialize()
    {
        character = GetComponent<Character>();
        characterVisual = GetComponentInChildren<CharacterVisual>();

        agent = GetComponent<NavMeshAgent>();
        if (agent == null)
            agent = gameObject.AddComponent<NavMeshAgent>();

        // IMPORTANT : Si tu utilises un rig 2D, assure-toi que l'agent 
        // n'essaie pas de faire tourner le transform lui-même.
        agent.updateRotation = false;
        agent.updateUpAxis = false;

        if (character != null && !character.IsAlive())
            enabled = false;

        agent.speed = character != null ? character.MovementSpeed : 3.5f;
    }

    protected virtual void Update()
    {
        // On exécute le comportement avant de calculer les mouvements/animations
        currentBehaviour?.Act(character);

        Move();
        UpdateAnimations();
        UpdateFlip();
    }

    protected virtual void UpdateAnimations()
    {
        if (animator != null && agent != null)
        {
            // magnitude > 0.1f suffit pour détecter le mouvement sur le NavMesh
            animator.SetBool("isWalking", agent.velocity.magnitude > 0.1f);
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
        // 1. APPEL DE EXIT : On prévient l'ancien comportement qu'il s'arrête
        // C'est ici que la coroutine de Wander sera stoppée
        if (currentBehaviour != null)
        {
            currentBehaviour.Exit(character);
        }

        string behaviourName = behaviour != null ? behaviour.GetType().Name : "None";
        Debug.Log($"{gameObject.name} change de comportement pour : {behaviourName}");

        // 2. CHANGEMENT : On assigne le nouveau
        currentBehaviour = behaviour;
    }
}