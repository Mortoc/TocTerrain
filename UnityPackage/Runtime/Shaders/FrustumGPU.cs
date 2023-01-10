using System.Runtime.InteropServices;
using Unity.Mathematics;

namespace TocTerrain
{
    /// <summary>
    /// Copy of FrustumGPU from HDRP/Runtime/Core/Utilities/GeometryUtils.cs, which Unity set internal for some reason.
    /// </summary>
    // GenerateHLSL isn't required here cause the GPU side of the one defined in GeometryUtils is available.
    //[GenerateHLSL(PackingRules.Exact, false)]
    [StructLayout(LayoutKind.Sequential)]
    public struct FrustumGPU
    {
        // 6 frustum planes
        public float3 normal0;
        public float dist0;
        public float3 normal1;
        public float dist1;
        public float3 normal2;
        public float dist2;
        public float3 normal3;
        public float dist3;
        public float3 normal4;
        public float dist4;
        public float3 normal5;
        public float dist5;
        
        // 8 corners
        public float4 corner0;
        public float4 corner1;
        public float4 corner2;
        public float4 corner3;
        public float4 corner4;
        public float4 corner5;
        public float4 corner6;
        public float4 corner7;
    }
}