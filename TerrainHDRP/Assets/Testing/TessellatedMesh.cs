using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using TocTerrain;
using Unity.Mathematics;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using static Unity.Mathematics.math;

[ExecuteAlways]
public class TessellatedMesh : MonoBehaviour
{
    public float DisplacementScale = 1.0f;
    public Mesh SourceMesh;
    public Material Material;
    private Material _matCopy;
    public ComputeShader ComputeMeshShader;
    private ComputeShader _computeShaderCopy;
    private int _lodKernel;
    private int _drawBuffersKernel;
    private readonly List<int> _allKernelIndices = new ();

    private GraphicsBuffer _frustumBuffer;
    private FrustumGPU[] _frustum;

    private Mesh _singleTriangleMesh;
    
    private GraphicsBuffer _coarseVertsBuffer;
    private GraphicsBuffer _coarseNormsBuffer;
    private GraphicsBuffer _coarseUvsBuffer;
    private GraphicsBuffer _coarseIndicesBuffer;
    private GraphicsBuffer _finalVertDataOut;
    private GraphicsBuffer _drawArgsBuffer;
    private GraphicsBuffer _subdInBuffer;
    private GraphicsBuffer _subdOutBuffer;
    
    private uint[] _drawArgs;
    private int _triangleCount;
    
    private GraphicsBuffer _debugCounter;
    
    private GeneratedVertex[] _finalVertDataOutCpuBuffer;

    private const int INITIAL_SUBD_BUFFER_SIZE = 1024 * 1024;
    
    private static class ShaderID
    {
        public static readonly int VertsIn = Shader.PropertyToID("VertsIn");
        public static readonly int NormsIn = Shader.PropertyToID("NormsIn");
        public static readonly int UVsIn = Shader.PropertyToID("UVsIn");
        public static readonly int IndicesIn = Shader.PropertyToID("IndicesIn");
        public static readonly int VertsOut = Shader.PropertyToID("VertsOut");
        public static readonly int TriangleCount = Shader.PropertyToID("TriangleCount");
        public static readonly int Transform = Shader.PropertyToID("Transform");
        public static readonly int DrawArgs = Shader.PropertyToID("DrawArgs");
        public static readonly int VertData = Shader.PropertyToID("VertData");
        public static readonly int SubdIn = Shader.PropertyToID("SubdIn");
        public static readonly int SubdOut = Shader.PropertyToID("SubdOut");
        public static readonly int FrustumData = Shader.PropertyToID("FrustumData");
        public static readonly int DebugCounter = Shader.PropertyToID("DebugCounter");
        public static readonly int DisplacementScale = Shader.PropertyToID("DisplacementScale");
        public static readonly int CameraPos = Shader.PropertyToID("CameraPos");
    }


    public bool RequiredAssetsAreAssigned => SourceMesh && Material && ComputeMeshShader;
    
    void OnEnable()
    {
        if (!RequiredAssetsAreAssigned)
        {
            return;
        }

        // Single Triangle mesh that gets instanced for each triangle in the SourceMesh
        if (!_singleTriangleMesh)
        {
            _singleTriangleMesh = new Mesh();
            _singleTriangleMesh.vertices = new[]
            {
                new Vector3 { x = 0.0f, y = 0.0f, z = 0.0f },
                new Vector3 { x = 1.0f, y = 0.0f, z = 0.0f },
                new Vector3 { x = 0.0f, y = 0.0f, z = 1.0f },
            };
            _singleTriangleMesh.triangles = new[] { 0, 1, 2 };
            _singleTriangleMesh.UploadMeshData(true);
        }

        _computeShaderCopy = Instantiate(ComputeMeshShader);
        _lodKernel = _computeShaderCopy.FindKernel("LodKernel");
        _allKernelIndices.Add(_lodKernel);
        //_drawBuffersKernel = _computeShaderCopy.FindKernel("BuildDrawBuffersKernel");
        //_allKernelIds.Add(_drawBuffersKernel);
        
        _matCopy = Instantiate(Material);

        SetupBuffers();
        RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
    }
    
    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
        
        _allKernelIndices.Clear();
        
        SafeDestroy.Asset(_matCopy);
        SafeDestroy.Asset(_computeShaderCopy);
        
        SafeDestroy.Buffer(_coarseVertsBuffer);
        _coarseVertsBuffer = null;
        
        SafeDestroy.Buffer(_coarseNormsBuffer);
        _coarseNormsBuffer = null;
        
        SafeDestroy.Buffer(_coarseUvsBuffer);
        _coarseUvsBuffer = null;
        
        SafeDestroy.Buffer(_coarseIndicesBuffer);
        _coarseIndicesBuffer = null;
        
        SafeDestroy.Buffer(_finalVertDataOut);
        _finalVertDataOut = null;
        
        SafeDestroy.Buffer(_drawArgsBuffer);
        _drawArgsBuffer = null;
    
        SafeDestroy.Buffer(_subdInBuffer);
        _subdInBuffer = null;
    
        SafeDestroy.Buffer(_subdOutBuffer);
        _subdOutBuffer = null;
        
        SafeDestroy.Buffer(_frustumBuffer);
        _frustumBuffer = null;
        
        SafeDestroy.Buffer(_debugCounter);
        _debugCounter = null;
    }
    
    private void SetupBuffers()
    {
        _triangleCount = SourceMesh.triangles.Length / 3;
        
        // Draw Args
        if (_drawArgsBuffer != null)
        {
            SafeDestroy.Buffer(_drawArgsBuffer);
        }
        _drawArgs = new[]
        {
            // Indices per instance
            _singleTriangleMesh.GetIndexCount(0),
            // Instance count (set by the compute shader)
            (uint)_triangleCount * 3,
            // Start index location
            _singleTriangleMesh.GetIndexStart(0),
            // Base vertex location
            _singleTriangleMesh.GetBaseVertex(0),
            // Start instance location
            0u
        };
        _drawArgsBuffer = new GraphicsBuffer(
            GraphicsBuffer.Target.IndirectArguments,
            1, 
            _drawArgs.Length * sizeof(uint)
        );
        _drawArgsBuffer.SetData(_drawArgs);
        
        // verts
        _coarseVertsBuffer = new GraphicsBuffer(
            GraphicsBuffer.Target.Structured, 
            SourceMesh.vertexCount, 
            sizeof(float) * 3
        );
        _coarseVertsBuffer.name = "Coarse Verts";
        _coarseVertsBuffer.SetData(SourceMesh.vertices);
            
        // normals
        _coarseNormsBuffer = new GraphicsBuffer(
            GraphicsBuffer.Target.Structured, 
            SourceMesh.vertexCount, 
            sizeof(float) * 3
        );
        _coarseNormsBuffer.name = "Coarse Normals";
        _coarseNormsBuffer.SetData(SourceMesh.normals);
        
        // uvs
        _coarseUvsBuffer = new GraphicsBuffer(
            GraphicsBuffer.Target.Structured, 
            SourceMesh.vertexCount, 
            sizeof(float) * 2
        );
        _coarseUvsBuffer.name = "Coarse UVs";
        _coarseUvsBuffer.SetData(SourceMesh.uv);
            
        // indices
        _coarseIndicesBuffer = new GraphicsBuffer(
            GraphicsBuffer.Target.Structured, 
            (int)SourceMesh.GetIndexCount(0), 
            sizeof(int)
        );
        _coarseIndicesBuffer.name = "Coarse Indices";
        _coarseIndicesBuffer.SetData(SourceMesh.GetIndices(0));
        
        // Subd Buffers
        _subdInBuffer = new GraphicsBuffer(
            GraphicsBuffer.Target.Structured,
            INITIAL_SUBD_BUFFER_SIZE,
            sizeof(uint) * 2
        );
        _subdInBuffer.name = "SubD Buffer 1";
        _subdOutBuffer = new GraphicsBuffer(
            GraphicsBuffer.Target.Structured,
            INITIAL_SUBD_BUFFER_SIZE,
            sizeof(uint) * 2
        );
        _subdOutBuffer.name = "SubD Buffer 2";
        
        // Frustum
        _frustum = new FrustumGPU[1];
        _frustumBuffer = new GraphicsBuffer(
            GraphicsBuffer.Target.Structured,
            1,
            Marshal.SizeOf<FrustumGPU>()
        );
        _frustumBuffer.name = "Frustum Buffer";
        
        // Output buffer
        _finalVertDataOut = new GraphicsBuffer(
            GraphicsBuffer.Target.Structured, 
            _triangleCount * 3, 
            Marshal.SizeOf<GeneratedVertex>()
        );
        _finalVertDataOut.name = "Vert Data Out";
        _finalVertDataOutCpuBuffer = new GeneratedVertex[_triangleCount * 3];
        //_finalVertDataOut.SetData(_finalVertDataOutCpuBuffer); // clear the output memory
        
        // Debug Buffer
        _debugCounter = new GraphicsBuffer(
            GraphicsBuffer.Target.Counter,
            1,
            sizeof(uint)
        );
        _debugCounter.name = "Debug Counter";

        AssignBuffersToShaders();
    }

    private void AssignBuffersToShaders()
    {
        if (!_computeShaderCopy || !_matCopy)
        {
            return;
        }
        
        // Assign buffers to the compute shader
        foreach (var idx in _allKernelIndices)
        {
            _computeShaderCopy.SetBuffer(idx, ShaderID.VertsIn, _coarseVertsBuffer);
            _computeShaderCopy.SetBuffer(idx, ShaderID.NormsIn, _coarseNormsBuffer);
            _computeShaderCopy.SetBuffer(idx, ShaderID.UVsIn, _coarseUvsBuffer);
            _computeShaderCopy.SetBuffer(idx, ShaderID.IndicesIn, _coarseIndicesBuffer);
            _computeShaderCopy.SetBuffer(idx, ShaderID.VertsOut, _finalVertDataOut);
            _computeShaderCopy.SetBuffer(idx, ShaderID.DrawArgs, _drawArgsBuffer);
            _computeShaderCopy.SetBuffer(idx, ShaderID.FrustumData, _frustumBuffer);   
            _computeShaderCopy.SetBuffer(idx, ShaderID.DebugCounter, _debugCounter);   
        }
        _computeShaderCopy.SetInt(ShaderID.TriangleCount, _triangleCount);
        
        // Assign buffers to the material shader
        _matCopy.SetBuffer(ShaderID.VertData, _finalVertDataOut);
    }
    

    private void UpdateDynamicShaderData(Camera renderCam)
    {
        _debugCounter.SetData(new uint[] {0});
        _matCopy.SetFloat(ShaderID.DisplacementScale, DisplacementScale);
        _computeShaderCopy.SetMatrix(ShaderID.Transform, transform.localToWorldMatrix);
        
        // Camera
        var hdCamera = HDCamera.GetOrCreate(renderCam);
        //var hdCamera = HDCamera.GetOrCreate(Camera.main);
        SetupFrustum(hdCamera);
        var cameraHPos = float4(renderCam.transform.position, 1.0f);
        _matCopy.SetVector(ShaderID.CameraPos, cameraHPos);
        _computeShaderCopy.SetVector(ShaderID.CameraPos, cameraHPos);
    }

    private void SetupFrustum(HDCamera hdCamera)
    {
        // Plane 0
        _frustum[0].normal0 = hdCamera.frustum.planes[0].normal;
        _frustum[0].dist0 = hdCamera.frustum.planes[0].distance;

        // Plane 1
        _frustum[0].normal1 = hdCamera.frustum.planes[1].normal;
        _frustum[0].dist1 = hdCamera.frustum.planes[1].distance;

        // Plane 2
        _frustum[0].normal2 = hdCamera.frustum.planes[2].normal;
        _frustum[0].dist2 = hdCamera.frustum.planes[2].distance;

        // Plane 3
        _frustum[0].normal3 = hdCamera.frustum.planes[3].normal;
        _frustum[0].dist3 = hdCamera.frustum.planes[3].distance;

        // Plane 4
        _frustum[0].normal4 = hdCamera.frustum.planes[4].normal;
        _frustum[0].dist4 = hdCamera.frustum.planes[4].distance;

        // Plane 5
        _frustum[0].normal5 = hdCamera.frustum.planes[5].normal;
        _frustum[0].dist5 = hdCamera.frustum.planes[5].distance;

        // Corners
        _frustum[0].corner0 = float4(hdCamera.frustum.corners[0], 1.0f);
        _frustum[0].corner1 = float4(hdCamera.frustum.corners[1], 1.0f);
        _frustum[0].corner2 = float4(hdCamera.frustum.corners[2], 1.0f);
        _frustum[0].corner3 = float4(hdCamera.frustum.corners[3], 1.0f);
        _frustum[0].corner4 = float4(hdCamera.frustum.corners[4], 1.0f);
        _frustum[0].corner5 = float4(hdCamera.frustum.corners[5], 1.0f);
        _frustum[0].corner6 = float4(hdCamera.frustum.corners[6], 1.0f);
        _frustum[0].corner7 = float4(hdCamera.frustum.corners[7], 1.0f);

        // Copy the data to the GPU
        _frustumBuffer.SetData(_frustum);
    }

    private void OnBeginCameraRendering(ScriptableRenderContext ctx, Camera renderCam)
    {
        // TODO: Convert this to a RenderGraph method, see HDRenderPipeline.WaterSystem for an example.
        
        #if UNITY_EDITOR
            // When the scene is saved, the material loses all bound buffers for some reason, this hack fixes it. Not 
            // necessary in builds.
            AssignBuffersToShaders();
        #endif
        
        UpdateDynamicShaderData(renderCam);
        
        // _computeShaderCopy.GetKernelThreadGroupSizes(0, out var groupSize, out _, out _);
        // var threadGroupCount = Mathf.CeilToInt(_triangleCount / (float)groupSize);
        // _computeShaderCopy.Dispatch(0, threadGroupCount, 1, 1);
        Debug.Log($"Updating Mesh with {_triangleCount} dispatch size");
        _computeShaderCopy.Dispatch(_lodKernel, _triangleCount, 1, 1);
        
        // Debug read the vert data back to verify it
        _finalVertDataOut.GetData(_finalVertDataOutCpuBuffer);

        var counter = new[] { 0 };
        _debugCounter.GetData(counter);
        Debug.Log($"Counter Value {counter[0]}");

        var vertCount = 0;
        foreach (var gVert in _finalVertDataOutCpuBuffer)
        {
            if (length(gVert.Position) > 0.0f)
            {
                vertCount++;
            }
        }
        
        Debug.Log(
     $"Buffers Valid: {_coarseVertsBuffer.IsValid() && _coarseIndicesBuffer.IsValid() && _finalVertDataOut.IsValid()} | " +
            $"Tri Count: {_triangleCount} | " +
            $"Draw Args {_drawArgs[0]}, {_drawArgs[1]}, {_drawArgs[2]}, {_drawArgs[3]}, {_drawArgs[4]} | " +
            $"Verts with Data: {vertCount}"
        );
        
        // Draw each final triangle of the mesh
        Graphics.DrawMeshInstancedIndirect(
            _singleTriangleMesh, 
            0, 
            _matCopy, 
            new Bounds(Vector3.zero, Vector3.one * 10000f), 
            _drawArgsBuffer, 
            0, 
            null, 
            ShadowCastingMode.On, 
            true, 
            0, 
            null, 
            LightProbeUsage.BlendProbes,
            null
        );
    }

}
