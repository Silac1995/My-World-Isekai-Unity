using UnityEngine;
using UnityEngine.AI;

public abstract class CharacterGameController : MonoBehaviour
{
    [SerializeField] protected CharacterVisual characterVisual;

    [SerializeField] protected Character character;
    [SerializeField] protected Animator animator;
    [SerializeField] protected NavMeshAgent agent;

    // IA
    protected IAIBehaviour currentBehaviour;

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

        if (!character.IsAlive())
            enabled = false;

        agent.speed = character != null ? character.MovementSpeed : 3.5f;
    }

    // Tout le monde appelle Move à chaque frame
    protected virtual void Update()
    {
        Move();
        UpdateAnimations();
        UpdateFlip();
    }

    protected virtual void UpdateAnimations()
    {
        if (animator != null && agent != null)
            animator.SetBool("isWalking", agent.velocity.magnitude > 0.1f);
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
            // Corrige la position sur le NavMesh si possible
            if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 5f, NavMesh.AllAreas))
            {
                float offset = 0.5f; // ou une valeur paramétrable si pivot au milieu
                transform.position = hit.position + Vector3.up * offset;

                agent.Warp(transform.position);
                Debug.Log($"Personnage repositionné sur le NavMesh : {transform.position}");
            }
        }

        // Applique le comportement IA si défini
        currentBehaviour?.Act(character);
    }


    public void SetBehaviour(IAIBehaviour behaviour)
    {
        Debug.Log($"SetBehaviour");
        currentBehaviour = behaviour;
    }
}
