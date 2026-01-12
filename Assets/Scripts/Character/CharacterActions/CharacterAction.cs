using System;

public abstract class CharacterAction
{
    protected Character character;
    public Action OnActionFinished;

    public float Duration { get; protected set; }

    protected CharacterAction(Character character, float duration = 0f)
    {
        this.character = character;
        this.Duration = duration;
    }

    public abstract void OnStart();
    public abstract void OnApplyEffect();

    // On change 'protected' par 'public' pour que le Controller puisse l'appeler
    public void Finish() => OnActionFinished?.Invoke();
}