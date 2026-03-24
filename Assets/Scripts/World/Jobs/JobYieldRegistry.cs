using System;
using System.Collections.Generic;
using UnityEngine;

namespace MWI.WorldSystem
{
    [Serializable]
    public class YieldOutput
    {
        public string ResourceId;
        public int BaseAmountPerDay;
        [Range(0f, 1f)]
        public float SkillMultiplierWeight;
    }

    [Serializable]
    public class JobYieldRecipe
    {
        public JobType Job;
        public List<YieldOutput> Outputs = new List<YieldOutput>();
    }

    [CreateAssetMenu(fileName = "JobYieldRegistry", menuName = "MWI/World/JobYieldRegistry")]
    public class JobYieldRegistry : ScriptableObject
    {
        [SerializeField] private List<JobYieldRecipe> _recipes = new List<JobYieldRecipe>();

        private Dictionary<JobType, JobYieldRecipe> _lookup;

        public void Initialize()
        {
            _lookup = new Dictionary<JobType, JobYieldRecipe>();
            foreach (var r in _recipes)
            {
                _lookup[r.Job] = r;
            }
        }

        public JobYieldRecipe GetYieldFor(JobType jobType)
        {
            if (_lookup == null) Initialize();
            return _lookup.TryGetValue(jobType, out var recipe) ? recipe : null;
        }
    }
}
