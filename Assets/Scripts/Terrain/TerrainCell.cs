using System;

namespace MWI.Terrain
{
    [Serializable]
    public struct TerrainCell
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

        public TerrainType GetBaseType() => TerrainTypeRegistry.Get(BaseTypeId);
        public TerrainType GetCurrentType() => TerrainTypeRegistry.Get(CurrentTypeId);
    }
}
