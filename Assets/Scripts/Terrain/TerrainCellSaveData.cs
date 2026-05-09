using System;
using Unity.Netcode;

namespace MWI.Terrain
{
    [Serializable]
    public struct TerrainCellSaveData : INetworkSerializable
    {
        public string BaseTypeId;
        public string CurrentTypeId;
        public float Moisture;
        public float Temperature;
        public float SnowDepth;
        public float Fertility;
        public bool IsPlowed;
        public string PlantedCropId;
        public float GrowthTimer;
        public float TimeSinceLastWatered;

        public static TerrainCellSaveData FromCell(TerrainCell cell)
        {
            return new TerrainCellSaveData
            {
                BaseTypeId = cell.BaseTypeId,
                CurrentTypeId = cell.CurrentTypeId,
                Moisture = cell.Moisture,
                Temperature = cell.Temperature,
                SnowDepth = cell.SnowDepth,
                Fertility = cell.Fertility,
                IsPlowed = cell.IsPlowed,
                PlantedCropId = cell.PlantedCropId,
                GrowthTimer = cell.GrowthTimer,
                TimeSinceLastWatered = cell.TimeSinceLastWatered
            };
        }

        public TerrainCell ToCell()
        {
            return new TerrainCell
            {
                BaseTypeId = BaseTypeId,
                CurrentTypeId = CurrentTypeId,
                Moisture = Moisture,
                Temperature = Temperature,
                SnowDepth = SnowDepth,
                Fertility = Fertility,
                IsPlowed = IsPlowed,
                PlantedCropId = PlantedCropId,
                GrowthTimer = GrowthTimer,
                TimeSinceLastWatered = TimeSinceLastWatered
            };
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            // Strings require manual read/write — not supported by SerializeValue directly
            if (serializer.IsWriter)
            {
                var writer = serializer.GetFastBufferWriter();
                writer.WriteValueSafe(BaseTypeId ?? string.Empty);
                writer.WriteValueSafe(CurrentTypeId ?? string.Empty);
                writer.WriteValueSafe(PlantedCropId ?? string.Empty);
            }
            else
            {
                var reader = serializer.GetFastBufferReader();
                reader.ReadValueSafe(out BaseTypeId);
                reader.ReadValueSafe(out CurrentTypeId);
                reader.ReadValueSafe(out PlantedCropId);
            }

            serializer.SerializeValue(ref Moisture);
            serializer.SerializeValue(ref Temperature);
            serializer.SerializeValue(ref SnowDepth);
            serializer.SerializeValue(ref Fertility);
            serializer.SerializeValue(ref IsPlowed);
            serializer.SerializeValue(ref GrowthTimer);
            serializer.SerializeValue(ref TimeSinceLastWatered);
        }
    }
}
