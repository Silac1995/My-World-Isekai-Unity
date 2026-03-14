using System;

public abstract class CharacterAction
{
    protected Character character;
    public System.Action OnActionFinished;

    public float Duration { get; set; }

    /// <summary>
    /// Si true, le CharacterGameController dclenchera le boolen 'isDoingAction' dans l'Animator.
    /// Les actions de combat l'outrepassent pour viter les conflits d'animation.
    /// </summary>
    public virtual bool ShouldPlayGenericActionAnimation => true;

    protected CharacterAction(Character character, float duration = 0f)
    {
        this.character = character;
        this.Duration = duration;
    }

    // Nouvelle mthode de validation
    public virtual bool CanExecute() => true;

    public abstract void OnStart();
    public abstract void OnApplyEffect();

    /// <summary>
    /// Appel quand l'action est annule (ex: ClearCurrentAction).
    /// Permet de dsabonner des vnements pour viter les memory leaks.
    /// </summary>
    public virtual void OnCancel() { }

    public void Finish() => OnActionFinished?.Invoke();
}
