using System.Collections.Generic;
using System.Linq;
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
        if (character.CharacterActions.CurrentAction != null)
        {
            StopMovement();
            UpdateAnimations();
            UpdateFlip();
            return;
        }

        // --- GESTION DE LA TERMINAISON AUTOMATIQUE ---
        if (CurrentBehaviour != null && CurrentBehaviour.IsFinished)
        {
            PopBehaviour();
            return; // On attend la frame suivante ou on continue
        }

        // Réactivation de l'agent (logique précédente)
        if (!character.IsPlayer() && agent != null && agent.isOnNavMesh)
        {
            if (!(CurrentBehaviour is InteractBehaviour))
                agent.isStopped = false;
        }

        CurrentBehaviour?.Act(character);

        Move();
        UpdateAnimations();
        UpdateFlip();
    }

    // Modifier SetBehaviour pour qu'il soit compatible avec ta pile
    public void SetBehaviour(IAIBehaviour behaviour)
    {
        if (character != null && character.IsPlayer()) return;

        // Si on veut vraiment "forcer" un comportement unique, on Reset la pile
        // Mais pour une interaction, on préfère PUSH pour revenir au Wander après
        PushBehaviour(behaviour);
    }

    public void PushBehaviour(IAIBehaviour newBehaviour)
    {
        // 1. PROTECTION JOUEUR : On ne touche à rien
        if (character.IsPlayer())
        {
            _behavioursStack.Push(newBehaviour);
            return;
        }

        // 2. LOGIQUE NPC : On ne force plus le isStopped ici !
        // On laisse le behaviour décider s'il doit bouger ou non.
        if (agent != null && agent.isOnNavMesh)
        {
            // On ne fait rien par défaut, MoveToTarget s'occupera de piloter l'agent.
        }

        _behavioursStack.Push(newBehaviour);
        Debug.Log($"<color=cyan>[AI Stack]</color> Push: {newBehaviour.GetType().Name}");
    }

    // Termine le comportement actuel et revient au précédent
    public void PopBehaviour()
    {
        if (character.IsPlayer())
        {
            if (_behavioursStack.Count > 0) _behavioursStack.Pop();
            return;
        }

        if (_behavioursStack.Count > 0)
        {
            IAIBehaviour old = _behavioursStack.Pop();
            old.Exit(character); // L'exit nettoie le chemin
            Debug.Log($"<color=orange>[AI Stack]</color> Pop: {old.GetType().Name}.");
        }

        // --- ICI ON RÉACTIVE PROPREMENT ---
        if (agent != null && agent.isOnNavMesh)
        {
            // On force l'arrêt des commandes précédentes
            agent.velocity = Vector3.zero;
            // On autorise à nouveau le mouvement
            agent.isStopped = false;
        }

        if (_behavioursStack.Count == 0)
        {
            if (character.TryGetComponent<NPCController>(out var npc))
            {
                ResetStackTo(new WanderBehaviour(npc));
            }
        }
        else
        {
            // On force le comportement restant à reprendre la main immédiatement
            CurrentBehaviour?.Act(character);
        }
    }
    public void ResetStackTo(IAIBehaviour baseBehaviour)
    {
        while (_behavioursStack.Count > 0)
        {
            IAIBehaviour old = _behavioursStack.Pop();
            old.Exit(character);
        }

        // On libère l'agent AVANT de push le nouveau comportement
        if (agent != null && agent.isOnNavMesh)
        {
            agent.isStopped = false;
        }

        _behavioursStack.Push(baseBehaviour);
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
        if (characterVisual == null) return;

        // On priorise la vélocité réelle de l'agent pour le flip
        if (agent != null && agent.velocity.sqrMagnitude > 0.01f)
        {
            characterVisual.UpdateFlip(agent.velocity);
        }
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
    /// <summary>
    /// Vérifie si un comportement d'un certain type est présent dans la pile.
    /// Exemple : HasBehaviour<MoveToTargetBehaviour>()
    /// </summary>
    public bool HasBehaviour<T>() where T : IAIBehaviour
    {
        return _behavioursStack.Any(b => b is T);
    }

    // À ajouter dans CharacterGameController.cs
    public List<string> GetBehaviourStackNames()
    {
        // ToArray() crée une copie de la pile, du sommet vers le bas
        return _behavioursStack.Select(b => b.GetType().Name).ToList();
    }
}