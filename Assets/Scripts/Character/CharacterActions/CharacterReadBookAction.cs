using UnityEngine;

public class CharacterReadBookAction : CharacterAction
{
    private readonly BookInstance _book;
    private readonly CharacterBookKnowledge _bookKnowledge;
    private bool _isCompleted;

    public override string ActionName => $"Reading {_book.CustomizedName}";

    public CharacterReadBookAction(Character character, BookInstance book) : base(character)
    {
        _book = book;
        _bookKnowledge = character.CharacterBookKnowledge;
        Duration = float.MaxValue;
        _isCompleted = false;
    }

    public override bool CanExecute()
    {
        return _book != null
            && _bookKnowledge != null
            && !_bookKnowledge.IsCompleted(_book.ContentId);
    }

    public override void OnStart()
    {
        _bookKnowledge.SetActiveReading(this);
        Debug.Log($"[ReadBook] {character.CharacterName} starts reading '{_book.CustomizedName}' " +
                  $"(Progress: {_bookKnowledge.GetProgress(_book.ContentId):P0})");
    }

    public void TickReading(float deltaTime)
    {
        if (_isCompleted) return;

        float readingSpeed = _bookKnowledge.GetReadingSpeed();
        bool completed = _bookKnowledge.AddProgress(_book, readingSpeed * deltaTime);

        if (completed)
        {
            _isCompleted = true;
            OnApplyEffect();
            Finish();
        }
    }

    public override void OnApplyEffect()
    {
        Debug.Log($"[ReadBook] {character.CharacterName} finished reading '{_book.CustomizedName}'");
    }

    public override void OnCancel()
    {
        _bookKnowledge.ClearActiveReading();
        Debug.Log($"[ReadBook] {character.CharacterName} stopped reading '{_book.CustomizedName}' " +
                  $"(Progress: {_bookKnowledge.GetProgress(_book.ContentId):P0})");
    }
}
