using System;

namespace MWI.Ambition
{
    [Serializable]
    public class ContextEntryDTO
    {
        public string Key;
        public ContextValueKind Kind;
        public string SerializedValue; // CharacterId UUID, asset GUID, primitive string, enum name
    }
}
