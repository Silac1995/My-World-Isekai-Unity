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

    public void Finish() => OnActionFinished?.Invoke();
}