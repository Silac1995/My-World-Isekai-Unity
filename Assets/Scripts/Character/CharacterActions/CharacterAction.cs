using System;

public abstract class CharacterAction
{
    protected Character character;
    public System.Action OnActionFinished;

    public float Duration { get; set; }

    /// <summary>
    /// Si true, le CharacterGameController déclenchera le booléen 'isDoingAction' dans l'Animator.
    /// Les actions de combat l'outrepassent pour éviter les conflits d'animation.
    /// </summary>
    public virtual bool ShouldPlayGenericActionAnimation => true;

    /// <summary>
    /// Nom de l'action pour l'affichage UI.
    /// </summary>
    public virtual string ActionName => GetType().Name;

    /// <summary>
    /// Si true, cette action gère sa propre réplication via des RPC spécifiques (ex: BroadcastAttackRpc).
    /// CharacterActions ne la répliquera donc pas via BroadcastActionVisualsClientRpc.
    /// </summary>
    public virtual bool IsReplicatedInternally => false;

    protected CharacterAction(Character character, float duration = 0f)
    {
        this.character = character;
        this.Duration = duration;
    }

    // Nouvelle méthode de validation
    public virtual bool CanExecute() => true;

    public abstract void OnStart();
    public abstract void OnApplyEffect();

    /// <summary>
    /// Appelé quand l'action est annulée (ex: ClearCurrentAction).
    /// Permet de désabonner des événements pour éviter les memory leaks.
    /// </summary>
    public virtual void OnCancel() { }

    public void Finish() => OnActionFinished?.Invoke();
}
