using System;

public abstract class CharacterAction
{
    protected Character character;
    public System.Action OnActionFinished;

    public float Duration { get; set; }

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