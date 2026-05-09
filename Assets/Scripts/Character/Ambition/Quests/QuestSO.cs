using System.Collections.Generic;
using UnityEngine;

namespace MWI.Ambition
{
    /// <summary>
    /// Authored quest definition. Owns a polymorphic list of Tasks (inline-authored via
    /// [SerializeReference] in the inspector — no per-task asset bloat) and an Ordering
    /// policy that controls how the runtime AmbitionQuest ticks them.
    /// </summary>
    [CreateAssetMenu(menuName = "MWI/Ambition/QuestSO", fileName = "Quest_New")]
    public class QuestSO : ScriptableObject
    {
        [SerializeField] private string _displayName;
        [TextArea, SerializeField] private string _description;
        [SerializeReference] private List<TaskBase> _tasks = new();
        [SerializeField] private TaskOrderingMode _ordering = TaskOrderingMode.Sequential;

        public string DisplayName => _displayName;
        public string Description => _description;
        public IReadOnlyList<TaskBase> Tasks => _tasks;
        public TaskOrderingMode Ordering => _ordering;
    }
}
