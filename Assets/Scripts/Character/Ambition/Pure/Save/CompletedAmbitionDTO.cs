using System;
using System.Collections.Generic;

namespace MWI.Ambition
{
    [Serializable]
    public class CompletedAmbitionDTO
    {
        public string AmbitionSOGuid;
        public List<ContextEntryDTO> FinalContext = new();
        public int CompletedDay;
        public CompletionReason Reason;
    }
}
