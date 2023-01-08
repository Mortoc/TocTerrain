#ifndef TESSELATION_SHADER_GRAPH_FUNCS
#define TESSELATION_SHADER_GRAPH_FUNCS

#include "GeneratedVertex.cs.hlsl"

RWStructuredBuffer<GeneratedVertex> VertData;

// ShaderGraph custom function to load the final tessellated vertex
void LoadInstancedTerrainVertex_float(in uint vertexID, out float3 position, out float3 normal, out float2 uv)
{
    uint vertCount, stride; VertData.GetDimensions(vertCount, stride);
    if (vertexID >= vertCount)
    {
        return;
    }

    float phase = (float)vertexID / (float)vertCount;
    float phi = phase * 2.0 * 3.1415926535897932384626433832795;
    
    // position = VertData[vertexID].positionWS;
    // normal = VertData[vertexID].normalWS;
    // uv = VertData[vertexID].uv;

    position = float3(sin(phi), 0.0, cos(phi));
    normal = normalize(position);
    uv = float2(0.0, 0.0);
}

#endif