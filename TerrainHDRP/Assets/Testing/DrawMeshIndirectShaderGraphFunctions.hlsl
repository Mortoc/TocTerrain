#ifndef DRAW_MESH_INDIRECT_SHADER_GRAPH_FUNCTIONS
#define DRAW_MESH_INDIRECT_SHADER_GRAPH_FUNCTIONS

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.mortoc.terrain/Runtime/Shaders/GeneratedVertex.cs.hlsl"

StructuredBuffer<GeneratedVertex> VertData;
StructuredBuffer<uint> _PerInstanceData;

#if UNITY_ANY_INSTANCING_ENABLED

	void VertInstancingSetup() {
		// There's an example for setting the local matrix in the file:
		// https://github.com/Unity-Technologies/Graphics/blob/master/Packages/com.unity.shadergraph/Editor/Generation/Targets/BuiltIn/ShaderLibrary/ParticlesInstancing.hlsl
		// VertInstancingMatrices(unity_ObjectToWorld, unity_WorldToObject);
	}

#endif


void LoadInstancedVertex_float(
	in uint instanceID, in uint vertexID,
	out float3 position, out float3 normal, out float3 color, out float2 uv)
{
    uint idx = (instanceID * 3) + vertexID;
    position = VertData[idx].Position;
    normal = VertData[idx].Normal;
	color = VertData[idx].Color;
    uv = VertData[idx].UV;
}

#endif
