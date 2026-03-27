using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class BookInstance : MiscInstance
{
    [SerializeField] private string _instanceUid;
    [SerializeField] private string _contentId;

    [SerializeField] private List<string> _customPages = new List<string>();
    [SerializeField] private string _customTeachesAbilityId;
    [SerializeField] private string _customTeachesSkillId;
    [SerializeField] private string _authorName;

    [NonSerialized] private AbilitySO _customTeachesAbility;
    [NonSerialized] private SkillSO _customTeachesSkill;
    [NonSerialized] private bool _customRefsResolved;

    public BookInstance(BookSO bookSO) : base(bookSO)
    {
        _instanceUid = Guid.NewGuid().ToString();
        _contentId = bookSO.name;
    }

    public string InstanceUid => _instanceUid;
    public string ContentId => _contentId;
    public BookSO BookData => (BookSO)_itemSO;

    public IReadOnlyList<string> Pages =>
        _customPages.Count > 0 ? _customPages.AsReadOnly() : BookData.Pages;

    public AbilitySO TeachesAbility
    {
        get
        {
            ResolveCustomRefs();
            return _customTeachesAbility ?? BookData.TeachesAbility;
        }
    }

    public SkillSO TeachesSkill
    {
        get
        {
            ResolveCustomRefs();
            return _customTeachesSkill ?? BookData.TeachesSkill;
        }
    }

    public string AuthorName => _authorName;
    public bool IsCustomBook => _customPages.Count > 0 || !string.IsNullOrEmpty(_authorName);
    public bool TeachesSomething => TeachesAbility != null || TeachesSkill != null;
    public float ReadingDifficulty => BookData.ReadingDifficulty;

    public void FinalizeWriting(string authorName, List<string> pages,
        AbilitySO teachesAbility = null, SkillSO teachesSkill = null)
    {
        _authorName = authorName;
        _customPages = new List<string>(pages);
        _customTeachesAbility = teachesAbility;
        _customTeachesSkill = teachesSkill;
        _customTeachesAbilityId = teachesAbility != null ? teachesAbility.AbilityId : null;
        _customTeachesSkillId = teachesSkill != null ? teachesSkill.SkillID : null;
        _contentId = Guid.NewGuid().ToString();
        _customRefsResolved = true;
    }

    private void ResolveCustomRefs()
    {
        if (_customRefsResolved) return;
        _customRefsResolved = true;

        if (!string.IsNullOrEmpty(_customTeachesAbilityId))
            _customTeachesAbility = Resources.Load<AbilitySO>($"Data/Abilities/{_customTeachesAbilityId}");
        if (!string.IsNullOrEmpty(_customTeachesSkillId))
            _customTeachesSkill = Resources.Load<SkillSO>($"Data/Skills/{_customTeachesSkillId}");
    }
}
