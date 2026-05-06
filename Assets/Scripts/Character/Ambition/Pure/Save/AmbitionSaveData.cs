using System;
using System.Collections.Generic;

namespace MWI.Ambition
{
    [Serializable]
    public class AmbitionSaveData
    {
        // Active state. Empty / null when Inactive.
        public string ActiveAmbitionSOGuid;
        public List<ContextEntryDTO> Context = new();
        public int CurrentStepIndex;
        public List<TaskStateDTO> TaskStates = new();
        public int AssignedDay;

        // History (always populated, may be empty).
        public List<CompletedAmbitionDTO> History = new();
    }
}
