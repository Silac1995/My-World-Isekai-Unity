using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "NewBook", menuName = "Scriptable Objects/Item/Book")]
public class BookSO : MiscSO, IAbilitySource
{
    [Header("Book Content")]
    [SerializeField, TextArea(3, 10)]
    private List<string> _pages = new List<string>();

    [Header("Teaching")]
    [SerializeField]
    [Tooltip("Optional: the ability this book teaches when fully read.")]
    private AbilitySO _teachesAbility;

    [SerializeField]
    [Tooltip("Optional: the skill this book teaches when fully read.")]
    private SkillSO _teachesSkill;

    [Header("Reading")]
    [SerializeField]
    [Tooltip("Total reading progress required to complete the book.")]
    private float _readingDifficulty = 100f;

    [SerializeField]
    [Tooltip("If true, characters can write custom content into this book.")]
    private bool _isWritable = false;

    public IReadOnlyList<string> Pages => _pages.AsReadOnly();
    public AbilitySO TeachesAbility => _teachesAbility;
    public SkillSO TeachesSkill => _teachesSkill;
    public float ReadingDifficulty => _readingDifficulty;
    public bool IsWritable => _isWritable;
    public bool TeachesSomething => _teachesAbility != null || _teachesSkill != null;

    public override System.Type InstanceType => typeof(BookInstance);
    public override ItemInstance CreateInstance() => new BookInstance(this);

    // IAbilitySource
    public AbilitySO GetAbility() => _teachesAbility;
    public bool CanLearnFrom(Character learner)
    {
        if (_teachesAbility == null) return false;
        return !learner.CharacterAbilities.KnowsAbility(_teachesAbility);
    }
}
