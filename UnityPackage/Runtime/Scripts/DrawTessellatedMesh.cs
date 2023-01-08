using System;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace TocTerrain
{
    public class DrawTessellatedMesh : CustomPass
    {
        // Size of each of the subdivision key buffers, needs 1 uint per final tessellated triangle
        private const int SUBD_BUFFER_SIZE = 1024 * 1024 * 16;

        // Start size of the output buffer for the final vertices. Will automatically reallocate at
        // a larger size when more space is required.
        private const int INITIAL_VERT_OUTPUT_COUNT = 1024 * 512;
        private const float OUTPUT_BUFFER_GROWTH_FACTOR = 1.5f;
        
        private GraphicsBuffer _coarseVertsBuffer;
        private GraphicsBuffer _coarseNormsBuffer;
        private GraphicsBuffer _coarseUvsBuffer;
        private GraphicsBuffer _coarseIndicesBuffer;
        private GraphicsBuffer _finalVertDataOut;

        private GraphicsBuffer _subdBufferIn;
        private GraphicsBuffer _subdBufferOut;
        
        private GraphicsBuffer _subdTriangleCount;
        private int[] _subdTriangleCountOut = new int[1];
        
        private ComputeShader _computeShader;
        private int _lodKernelId;
        private uint3 _lodKernelThreadGroupSizes;
        private int _renderKernelId;
        private uint3 _renderKernelThreadGroupSizes;
        
        private uint _coarseTriangleCount;
        private Bounds _bounds;

        public Mesh SourceMesh;
        public float TriTargetPixelSize = 16.0f;
        public ComputeShader TessellationShader;
        
        private static class ShaderIDs
        {
            public static readonly int TargetPixelSize = Shader.PropertyToID("TargetPixelSize");
            public static readonly int VertsIn = Shader.PropertyToID("VertsIn");
            public static readonly int NormsIn = Shader.PropertyToID("NormsIn");
            public static readonly int UVsIn = Shader.PropertyToID("UVsIn");
            public static readonly int IndicesIn = Shader.PropertyToID("IndicesIn");
            public static readonly int VertsOut = Shader.PropertyToID("VertsOut");
            public static readonly int Transform = Shader.PropertyToID("PlanetTransform");
            public static readonly int SubDBufferIn = Shader.PropertyToID("SubDBufferIn");
            public static readonly int SubDBufferOut = Shader.PropertyToID("SubDBufferOut");
            public static readonly int SubDTriangleCounter = Shader.PropertyToID("SubDTriangleCounter");
            public static readonly int VertData = Shader.PropertyToID("VertData");
        }

        protected override void Setup(ScriptableRenderContext renderContext, CommandBuffer cmd)
        {
            if (SourceMesh == null)
            {
                throw new ArgumentNullException(nameof(SourceMesh));
            }
            if (SourceMesh.subMeshCount != 1)
            {
                throw new InvalidOperationException(
                    $"{nameof(DrawTessellatedMesh)} only supports input meshes with a single submesh"
                );
            }
            _bounds = SourceMesh.bounds;
            _coarseTriangleCount = SourceMesh.GetIndexCount(0);
            
            // Load the initial coarse mesh data
            // verts
            _coarseVertsBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, 
                SourceMesh.vertexCount, 
                sizeof(float) * 3
            );
            _coarseVertsBuffer.SetData(SourceMesh.vertices);
            
            // normals
            _coarseNormsBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, 
                SourceMesh.vertexCount, 
                sizeof(float) * 3
            );
            _coarseNormsBuffer.SetData(SourceMesh.normals);
            
            // uvs
            _coarseUvsBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, 
                SourceMesh.vertexCount, 
                sizeof(float) * 2
            );
            _coarseUvsBuffer.SetData(SourceMesh.uv);
            
            // indices
            _coarseIndicesBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, 
                (int)SourceMesh.GetIndexCount(0), 
                sizeof(int)
            );
            _coarseIndicesBuffer.SetData(SourceMesh.GetIndices(0));

            // SubD buffers
            _subdBufferIn = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                SUBD_BUFFER_SIZE,
                sizeof(uint)
            );
            _subdBufferOut = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                SUBD_BUFFER_SIZE,
            sizeof(uint)
            );
            _subdTriangleCount = new GraphicsBuffer(
                GraphicsBuffer.Target.Counter,
                1,
                sizeof(uint)
            );
            
            // Output buffer
            _finalVertDataOut = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, 
                INITIAL_VERT_OUTPUT_COUNT, 
                8 * sizeof(float)
            );

            // Setup compute shader buffers
            if (TessellationShader == null)
            {
                throw new ArgumentNullException(nameof(TessellationShader));
            }
            _computeShader = ComputeShader.Instantiate(TessellationShader);

            if (TriTargetPixelSize <= 0.0f)
            {
                throw new ArgumentOutOfRangeException(nameof(TriTargetPixelSize));
            }
            _computeShader.SetFloat(ShaderIDs.TargetPixelSize, TriTargetPixelSize);
            
            // Setup LOD Kernel
            _lodKernelId = FindKernelChecked(_computeShader, "PlanetMeshLodKernel");
            _computeShader.GetKernelThreadGroupSizes(_lodKernelId, out var x, out var y, out var z);
            _lodKernelThreadGroupSizes = new uint3(x, y, z);
            
            // Setup Render Kernel
            _renderKernelId = FindKernelChecked(_computeShader, "PlanetMeshRenderKernel");
            _computeShader.GetKernelThreadGroupSizes(_renderKernelId, out x, out y, out z);
            _renderKernelThreadGroupSizes = new uint3(x, y, z);
            
            // Populate the mesh data for each kernel
            foreach (var kernelId in new[] {_lodKernelId, _renderKernelId})
            {
                _computeShader.SetBuffer(kernelId, ShaderIDs.VertsIn, _coarseVertsBuffer);
                _computeShader.SetBuffer(kernelId, ShaderIDs.NormsIn, _coarseNormsBuffer);
                _computeShader.SetBuffer(kernelId, ShaderIDs.UVsIn, _coarseUvsBuffer);
                _computeShader.SetBuffer(kernelId, ShaderIDs.IndicesIn, _coarseIndicesBuffer);
                _computeShader.SetBuffer(kernelId, ShaderIDs.SubDTriangleCounter, _subdTriangleCount);
                _computeShader.SetBuffer(kernelId, ShaderIDs.VertsOut, _finalVertDataOut);
            }
        }
        
        private int FindKernelChecked(ComputeShader shader, string kernelName)
        {
            var kernelId = shader.FindKernel(kernelName);
            
            if (kernelId == -1)
            {
                throw new InvalidOperationException($"Could not find kernel {kernelName} in compute shader {shader.name}");
            }

            if (!_computeShader.IsSupported(kernelId))
            {
                throw new InvalidOperationException($"{kernelName} is not supported on this platform");
            }

            return kernelId;
        }
        
        public void Update(Transform transform)
        {
            _computeShader.SetMatrix(ShaderIDs.Transform, transform.localToWorldMatrix);
            
            // Swap index buffers
            (_subdBufferIn, _subdBufferOut) = (_subdBufferOut, _subdBufferIn);
            _computeShader.SetBuffer(_lodKernelId, ShaderIDs.SubDBufferIn, _subdBufferIn);
            _computeShader.SetBuffer(_lodKernelId, ShaderIDs.SubDBufferOut, _subdBufferOut);

            RunLodCompute();
            ResizeOutputIfNecessary();
            RunRenderCompute();
        }

        private void RunLodCompute()
        {
            var threadGroupCount = Mathf.CeilToInt(_coarseTriangleCount / (float)_lodKernelThreadGroupSizes.x);
            _computeShader.Dispatch(_lodKernelId, threadGroupCount, 1, 1);
        }

        private void ResizeOutputIfNecessary()
        {
            _subdTriangleCount.GetData(_subdTriangleCountOut);
            
            Debug.Log($"Subd triangle count: {_subdTriangleCountOut[0]}");

            if (_subdTriangleCountOut[0] <= _finalVertDataOut.count)
            {
                return;
            }
            
            _finalVertDataOut.Release();
            _finalVertDataOut = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, 
                Mathf.RoundToInt(Mathf.Max(
                    _subdTriangleCountOut[0], 
                    _finalVertDataOut.count * OUTPUT_BUFFER_GROWTH_FACTOR
                )),
                8 * sizeof(float)
            );
        }

        private void RunRenderCompute()
        {
            _computeShader.GetKernelThreadGroupSizes(_renderKernelId, out var threadGroupSize, out _, out _);
            _computeShader.Dispatch(
                _renderKernelId, 
                Mathf.CeilToInt(_subdTriangleCountOut[0] / (float)threadGroupSize), 
                1, 
                1
            );
        }

        public void Draw(Material material)
        {
            material.SetBuffer(ShaderIDs.VertData, _finalVertDataOut);
            Graphics.DrawProceduralIndirect(
                material,
                _bounds,
                MeshTopology.Triangles,
                _finalVertDataOut
            );
            
            // Graphics.DrawProceduralIndirect(
            //     material, 
            //     _bounds, // TODO: increase bounds by max displacement 
            //     MeshTopology.Triangles,
            //     _subdTriangleCount
            // );
        }

        public void Dispose()
        {
            SafeDestroy.Buffer(_coarseVertsBuffer);
            SafeDestroy.Buffer(_coarseNormsBuffer);
            SafeDestroy.Buffer(_coarseUvsBuffer);
            SafeDestroy.Buffer(_coarseIndicesBuffer);
            SafeDestroy.Buffer(_subdBufferIn);
            SafeDestroy.Buffer(_subdBufferOut);
            SafeDestroy.Buffer(_subdTriangleCount);
            
            SafeDestroy.Asset(_computeShader);
        }
        
    }
}
