// Terrain Tesselation system based on GPU Zen 2, Adaptive GPU Tessellation with Compute Shaders
#ifndef TOC_TESSELATION_H
#define TOC_TESSELATION_H

#include "GeneratedVertex.cs.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"


// Subdivision Buffers
StructuredBuffer<uint> SubDBufferIn;
RWStructuredBuffer<uint> SubDBufferOut;
uniform RWStructuredBuffer<uint> SubDTriangleCounter;

// Shared Input
float4x4 PlanetTransform;
StructuredBuffer<float3> VertsIn;
StructuredBuffer<float3> NormsIn;
StructuredBuffer<float2> UVsIn;
StructuredBuffer<uint> IndicesIn;

// Render Kernel Output
RWStructuredBuffer<GeneratedVertex> VertsOut;

// LOD Input
float TargetPixelSize;

// Render Kernel Input
float PlanetRadius;
uint2 VertexIndexKey;
float2 InstancedVertex;


float3x3 BitToTransform(const in uint bit)
{
    // if the bit is on, returns this matrix:
    // -1/2, -1/2,  1/2
    // -1/2,  1/2,  1/2
    //    0,    0,    1
    // 
    // else:
    //  1/2, -1/2,  1/2
    // -1/2, -1/2,  1/2
    //    0,    0,    1

    // These transforms will shrink a given triangle to a half size triangle
    // that occupies either the top or bottom half of the original triangle.
    
    float s = float(bit) - 0.5;
    float3 c0 = float3(s, -0.5, 0);
    float3 c1 = float3(-0.5, -s, 0);
    float3 c2 = float3(0.5, 0.5, 1);
    return float3x3(c0, c1, c2);
}

float3x3 BuildTransformFromKey(in uint key)
{
    float3x3 xform = float3x3(
        1, 0, 0,
        0, 1, 0,
        0, 0, 1
    );
    while (key > 1u)
    {
        xform = BitToTransform(key & 1u) * xform;
        key >>= 1u;
    }
    return xform;
}

uint ParentKey(const in uint key)
{
    return key >> 1u;
}

void ChildrenKeys(const in uint key, out uint children[2])
{
    children[0] = (key << 1u) | 0u;
    children[1] = (key << 1u) | 1u;
}

void WriteKey(const in uint key)
{
    uint idx; InterlockedAdd(SubDTriangleCounter[0], 1u, idx);
    SubDBufferOut[idx] = key;
}

// Is this the 0-th child of its parent?
bool IsChildZeroKey(const in uint key)
{
    return key == 0x1u;
}

// Returns false if this key is as subdivided as possible
bool IsLeafKey(const in uint key)
{
    return firstbithigh(key) == 31;
}

bool IsRootKey(const in uint key)
{
    return key == 1u;
}

// Barycentric interpolation of UV within a triangle defined by 3 vertices
float3 Berp(const in float3 verts[3], const in float2 uv)
{
    return verts[0] + uv.x * (verts[1] - verts[0]) + uv.y * (verts[2] - verts[0]);
}

float2 BerpUv(const in float2 uvs[3], const in float2 berp_uv)
{
    return uvs[0] + berp_uv.x * (uvs[1] - uvs[0]) + berp_uv.y * (uvs[2] - uvs[0]);
}

float DistanceToLod(float z, float targetPixelSize)
{
    float tanFov = 1.0 / UNITY_MATRIX_P[1][1];
    float sz = 2.0 * z * tanFov;
    float szScreenScaled = sz * targetPixelSize * _ScreenSize.z;
    return -log2(saturate(szScreenScaled));
}

// Converts the original triangle into this subdivision's triangle
void Subdivide(const in uint key, const in float3 vertsIn[3], out float3 vertsOut[3])
{
    float3x3 xform = BuildTransformFromKey(key);
    float2 u1 = mul(xform, float3(0.0, 0.0, 1.0)).xy;
    float2 u2 = mul(xform, float3(1.0, 0.0, 1.0)).xy;
    float2 u3 = mul(xform, float3(0.0, 1.0, 1.0)).xy;

    vertsOut[0] = Berp(vertsIn, u1);
    vertsOut[1] = Berp(vertsIn, u2);
    vertsOut[2] = Berp(vertsIn, u3);
}

// Subdivides, keeps or reduces this base-triangle's current subdivision level based
// on the target LOD
void UpdateSubDBuffer(const uint key, const int targetLod)
{
    // update this key
    const int keyLod = firstbithigh(key);

    if (keyLod < targetLod && !IsLeafKey(key))
    {
        // Subdivide
        uint children[2];
        ChildrenKeys(key, children);
        WriteKey(children[0]);
        WriteKey(children[1]);
    }
    else if(keyLod == targetLod)
    {
        // Keep
        WriteKey(key);
    }
    else
    {
        // Merge
        if (IsRootKey(key))
        {
            // Don't try to merge rootKey
            WriteKey(key);
        }
        else if (IsChildZeroKey(key))
        {
            // Only write the parent key for the 0th child,
            // dropping the other child accomplishes the merge
            WriteKey(ParentKey(key));
        }
    }
}


#endif