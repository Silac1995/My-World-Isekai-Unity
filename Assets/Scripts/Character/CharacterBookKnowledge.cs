using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class BookReadingEntry
{
    public string contentId;
    public string bookTitle;
    public float currentProgress;
    public float requiredProgress;
    public bool completed;
}

public class CharacterBookKnowledge : CharacterSystem, ICharacterSaveData<CharacterBookKnowledge.BookKnowledgeSaveData>
{
    [SerializeField] private List<BookReadingEntry> _readingLog = new List<BookReadingEntry>();

    private CharacterReadBookAction _activeReadingAction;

    private const float BASE_READING_RATE = 10f;
    private const float INTELLIGENCE_COEFFICIENT = 0.05f;

    public IReadOnlyList<BookReadingEntry> ReadingLog => _readingLog.AsReadOnly();
    public bool IsReading => _activeReadingAction != null;

    public BookReadingEntry GetOrCreateEntry(BookInstance book)
    {
        var entry = _readingLog.Find(e => e.contentId == book.ContentId);
        if (entry == null)
        {
            entry = new BookReadingEntry
            {
                contentId = book.ContentId,
                bookTitle = book.CustomizedName,
                currentProgress = 0f,
                requiredProgress = book.ReadingDifficulty,
                completed = false
            };
            _readingLog.Add(entry);
        }
        return entry;
    }

    public float GetReadingSpeed()
    {
        float intelligence = _character.Stats.Intelligence.Value;
        return BASE_READING_RATE * (1f + intelligence * INTELLIGENCE_COEFFICIENT);
    }

    public bool AddProgress(BookInstance book, float amount)
    {
        var entry = GetOrCreateEntry(book);
        if (entry.completed) return true;

        entry.currentProgress = Mathf.Min(entry.currentProgress + amount, entry.requiredProgress);

        if (entry.currentProgress >= entry.requiredProgress)
        {
            entry.completed = true;
            OnBookCompleted(book);
            return true;
        }
        return false;
    }

    public bool IsCompleted(string contentId)
    {
        var entry = _readingLog.Find(e => e.contentId == contentId);
        return entry != null && entry.completed;
    }

    public float GetProgress(string contentId)
    {
        var entry = _readingLog.Find(e => e.contentId == contentId);
        if (entry == null) return 0f;
        return entry.requiredProgress > 0f ? entry.currentProgress / entry.requiredProgress : 1f;
    }

    private void OnBookCompleted(BookInstance book)
    {
        if (book.TeachesAbility != null && _character.CharacterAbilities != null)
        {
            if (!_character.CharacterAbilities.KnowsAbility(book.TeachesAbility))
            {
                _character.CharacterAbilities.LearnAbility(book.TeachesAbility);
                Debug.Log($"[BookKnowledge] {_character.CharacterName} learned ability: {book.TeachesAbility.AbilityName} from reading.");
            }
        }

        if (book.TeachesSkill != null)
        {
            Debug.Log($"[BookKnowledge] {_character.CharacterName} learned skill: {book.TeachesSkill.SkillName} from reading.");
            // TODO: Call CharacterSkills.LearnSkill() when API is available
        }
    }

    public void SetActiveReading(CharacterReadBookAction action) => _activeReadingAction = action;
    public void ClearActiveReading() => _activeReadingAction = null;

    private void Update()
    {
        if (_activeReadingAction != null)
        {
            _activeReadingAction.TickReading(Time.deltaTime);
        }
    }

    // === ICharacterSaveData IMPLEMENTATION ===
    public string SaveKey => "CharacterBookKnowledge";
    public int LoadPriority => 50;

    [System.Serializable]
    public class BookKnowledgeSaveData
    {
        public List<BookReadingEntry> readingLog = new List<BookReadingEntry>();
    }

    public BookKnowledgeSaveData Serialize()
    {
        return new BookKnowledgeSaveData { readingLog = _readingLog };
    }

    public void Deserialize(BookKnowledgeSaveData data)
    {
        _readingLog = data.readingLog ?? new List<BookReadingEntry>();
    }

    // Non-generic bridge (explicit interface impl)
    string ICharacterSaveData.SerializeToJson() => CharacterSaveDataHelper.SerializeToJson(this);
    void ICharacterSaveData.DeserializeFromJson(string json) => CharacterSaveDataHelper.DeserializeFromJson(this, json);
}
