using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;

public abstract class CharacterGameController : MonoBehaviour
{
    [SerializeField] protected CharacterVisual _characterVisual;
    [SerializeField] protected Character _character;
    [SerializeField] protected CharacterMovement _characterMovement;
    protected bool _wasDoingAction;

    private Stack<IAIBehaviour> _behavioursStack = new Stack<IAIBehaviour>();
    public IAIBehaviour CurrentBehaviour => _behavioursStack.Count > 0 ? _behavioursStack.Peek() : null;

    // --- PROPRI?T?S DE COMPATIBILIT? (Pour corriger tes erreurs) ---
    public Character Character => _character;
    public NavMeshAgent Agent => _characterMovement != null ? _characterMovement.Agent : null;
    public CharacterMovement CharacterMovement => _characterMovement;
    public Animator Animator => (_characterVisual != null && _characterVisual.CharacterAnimator != null)
                                 ? _characterVisual.CharacterAnimator.Animator
                                 : null;

    public virtual void Initialize()
    {
        if (_character != null && !_character.IsAlive()) enabled = false;
    }

    protected virtual void Update()
    {
        if (_character.CharacterActions.CurrentAction != null)
        {
            _wasDoingAction = true;
            _characterMovement.Stop();

            UpdateAnimations();
            // On ne flip pas pendant une action pour éviter de glitcher l'animation d'attaque (ou autre)
            return;
        }

        // --- NOUVEAU : Cleanup précis après l'action ---
        if (_wasDoingAction)
        {
            _wasDoingAction = false;
            _characterMovement.Resume();
            
            // On nettoie les drapeaux et triggers pour éviter les répétitions
            Animator.SetBool(CharacterAnimator.IsDoingAction, false);
            _character.CharacterVisual?.CharacterAnimator?.ResetActionTriggers();
        }

        if (CurrentBehaviour != null)
        {
            if (CurrentBehaviour.IsFinished)
            {
                PopBehaviour();
                return;
            }

            // L'IA gère son propre Resume/Stop
            CurrentBehaviour.Act(_character);
        }

        UpdateAnimations();
        UpdateFlip();
    }

    // --- M?THODES REQUISES PAR TES BEHAVIOURS ET INTERACTIONS ---

    public void SetBehaviour(IAIBehaviour behaviour) => ResetStackTo(behaviour);

    public void PushBehaviour(IAIBehaviour newBehaviour)
    {
        _characterMovement.Resume();
        _behavioursStack.Push(newBehaviour);
    }

    public void PopBehaviour()
    {
        if (_behavioursStack.Count > 0)
        {
            IAIBehaviour old = _behavioursStack.Pop();
            old.Exit(_character);
        }

        _characterMovement.Resume();

        if (_behavioursStack.Count == 0 && _character.TryGetComponent<NPCController>(out var npc))
        {
            ResetStackTo(new WanderBehaviour(npc));
        }
    }

    public void ClearBehaviours()
    {
        // 1. On vide proprement la pile actuelle
        while (_behavioursStack.Count > 0)
        {
            IAIBehaviour old = _behavioursStack.Pop();
            old.Exit(_character);
        }

        // 2. On force l'arret physique et du NavMesh
        if (_characterMovement != null)
        {
            _characterMovement.Stop();
            // On s'assure que l'agent n'a plus de destination residuelle
            if (Agent != null && Agent.isOnNavMesh)
            {
                Agent.ResetPath();
            }
        }
    }

    public void ResetStackTo(IAIBehaviour baseBehaviour)
    {
        ClearBehaviours();
        _characterMovement.Resume();
        _behavioursStack.Push(baseBehaviour);
    }

    public bool HasBehaviour<T>() where T : IAIBehaviour => _behavioursStack.Any(b => b is T);

    public T GetCurrentBehaviour<T>() where T : class, IAIBehaviour => CurrentBehaviour as T;

    public List<string> GetBehaviourStackNames() => _behavioursStack.Select(b => b.GetType().Name).ToList();

    // --- LOGIQUE VISUELLE ---

    protected virtual void UpdateAnimations()
    {
        if (Animator == null || _characterMovement == null) return;

        // R?cup?ration de la vitesse physique r?elle
        Vector3 velocity = _characterMovement.GetVelocity();

        // On calcule la magnitude sur le plan horizontal (X, Z)
        float speed = new Vector3(velocity.x, 0, velocity.z).magnitude;

        // Appliquer une zone morte pour ?viter que l'animator ne tremble
        // Mais attention : si ta condition est "Greater than 0", 
        // il faut que speed soit bien ? 0 quand on s'arr?te.
        if (speed < 0.1f) speed = 0f;

        // Envoi ? l'Animator : utilise exactement le hash VelocityX
        Animator.SetFloat(CharacterAnimator.VelocityX, speed);

        // Sol
        Animator.SetBool(CharacterAnimator.IsGrounded, _characterMovement.IsGrounded());
    }

    protected virtual void UpdateFlip()
    {
        if (_characterVisual == null) return;
        Vector3 velocity = _characterMovement.GetVelocity();
        if (velocity.sqrMagnitude > 0.01f) _characterVisual.UpdateFlip(velocity);
    }

    protected bool IsGrounded() => _characterMovement != null && _characterMovement.IsGrounded();
}
