using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public abstract class CharacterGameController : MonoBehaviour
{
    [SerializeField] protected CharacterVisual characterVisual;
    [SerializeField] protected Character character;
    [SerializeField] protected NavMeshAgent agent;

    // La pile de comportements
    private Stack<IAIBehaviour> _behavioursStack = new Stack<IAIBehaviour>();

    // Propriété pour obtenir le comportement actuel (le sommet de la pile)
    public IAIBehaviour CurrentBehaviour => _behavioursStack.Count > 0 ? _behavioursStack.Peek() : null;

    // IA - Propriété publique pour la lecture
    protected IAIBehaviour currentBehaviour;

    [Header("Ground Detection")]
    [SerializeField] protected LayerMask groundLayer; // À régler sur "Default" ou "Ground" dans l'Inspector
    [SerializeField] protected float groundCheckOffset = 0.1f; // Distance supplémentaire sous les pieds

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
        // 1. Priorité aux Actions (ton système actuel)
        if (character.CharacterActions.CurrentAction != null)
        {
            StopMovement(); // Méthode pour isoler l'arrêt de l'agent
            return;
        }

        // 2. Exécution du comportement au sommet de la pile
        CurrentBehaviour?.Act(character);

        // Si une action est en cours, on met l'IA en pause
        if (character != null && character.CharacterActions != null && character.CharacterActions.CurrentAction != null)
        {
            // On stoppe le mouvement de l'agent pour que le NPC reste sur place durant l'action
            if (agent != null && agent.isOnNavMesh && !agent.isStopped)
            {
                agent.isStopped = true;
                agent.velocity = Vector3.zero;
            }

            // On ne traite pas le 'Act' du behaviour car l'action est prioritaire
            // On passe directement à la mise à jour des animations/visuels
            UpdateAnimations();
            UpdateFlip();
            return;
        }

        // Si on arrive ici, aucune action n'est en cours : on réactive l'agent
        if (agent != null && agent.isOnNavMesh && agent.isStopped)
        {
            agent.isStopped = false;
        }

        // Exécution normale du comportement IA
        currentBehaviour?.Act(character);

        Move();
        UpdateAnimations();
        UpdateFlip();
    }

    // Ajoute un nouveau comportement et met en pause le précédent
    public void PushBehaviour(IAIBehaviour newBehaviour)
    {
        _behavioursStack.Push(newBehaviour);
        Debug.Log($"<color=cyan>[AI Stack]</color> Push: {newBehaviour.GetType().Name}");
    }

    // Termine le comportement actuel et revient au précédent
    public void PopBehaviour()
    {
        if (_behavioursStack.Count > 0)
        {
            IAIBehaviour old = _behavioursStack.Pop();
            old.Exit(character);
            Debug.Log($"<color=orange>[AI Stack]</color> Pop: {old.GetType().Name}. Retour à: {(CurrentBehaviour != null ? CurrentBehaviour.GetType().Name : "Rien")}");
        }
    }
    public void ResetStackTo(IAIBehaviour baseBehaviour)
    {
        // On nettoie tout avant de mettre le nouveau comportement de base
        while (_behavioursStack.Count > 0)
        {
            IAIBehaviour old = _behavioursStack.Pop();
            old.Exit(character);
        }
        PushBehaviour(baseBehaviour);
    }

    private void StopMovement()
    {
        if (agent != null && agent.isOnNavMesh)
        {
            agent.isStopped = true;
            agent.velocity = Vector3.zero;
        }
    }

    protected virtual void UpdateAnimations()
    {
        if (Animator == null) return;

        bool grounded = IsGrounded();
        float speed = 0f;

        // Détection de la vitesse selon le type de déplacement
        if (agent != null && agent.isOnNavMesh)
        {
            speed = agent.velocity.magnitude;
        }
        else if (character.Rigidbody != null)
        {
            speed = character.Rigidbody.linearVelocity.magnitude;
        }

        // Zone morte pour éviter les tremblements
        if (speed < 0.15f) speed = 0f;

        // Utilisation des Hashes
        Animator.SetFloat(CharacterAnimator.VelocityX, speed);
        Animator.SetBool(CharacterAnimator.IsGrounded, grounded);

        // Sécurité IsDoingAction : Si aucune action n'est dans le CharacterActions
        if (character.CharacterActions.CurrentAction == null)
        {
            Animator.SetBool(CharacterAnimator.IsDoingAction, false);
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
                transform.position = hit.position;
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

    protected bool IsGrounded()
    {
        // On part du centre du collider (ou du transform)
        Vector3 origin = transform.position + Vector3.up * 0.1f;

        // On tire vers le bas. La distance doit être un poil plus longue 
        // que la distance entre le centre et le bas du perso.
        float distance = 0.2f;

        bool hit = Physics.Raycast(origin, Vector3.down, distance, groundLayer);

        // Debug visuel pour voir le rayon dans la scène
        Debug.DrawRay(origin, Vector3.down * distance, hit ? Color.green : Color.red);

        return hit;
    }
}