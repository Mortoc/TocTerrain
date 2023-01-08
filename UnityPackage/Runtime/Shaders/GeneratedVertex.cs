using Unity.Mathematics;
using UnityEngine.Rendering;

namespace TocTerrain
{
    [GenerateHLSL(PackingRules.Exact, false)]
    public struct GeneratedVertex
    {
        public float3 Position;
        public float3 Normal;
        public float3 Color;
        public float2 UV;
    }
}
