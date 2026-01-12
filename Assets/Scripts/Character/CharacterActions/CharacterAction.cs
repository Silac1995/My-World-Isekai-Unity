using System;

public abstract class CharacterAction
{
    protected Character character;
    // On ajoute un callback pour prévenir quand c'est fini
    public Action OnComplete;

    protected CharacterAction(Character character)
    {
        this.character = character;
    }

    public abstract void PerformAction();

    // Méthode utilitaire pour terminer proprement
    protected void Finish() => OnComplete?.Invoke();
}