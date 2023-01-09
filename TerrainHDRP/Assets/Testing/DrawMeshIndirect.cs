using System.Runtime.InteropServices;
using TocTerrain;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

[ExecuteAlways]
public class DrawMeshIndirect : MonoBehaviour, ISerializationCallbackReceiver
{
    public Mesh SourceMesh;
    public Material Material;
    private Material _matCopy;
    public ComputeShader ComputeMeshShader;
    private ComputeShader _computeShaderCopy;

    private Mesh _singleTriangleMesh;
    
    private GraphicsBuffer _coarseVertsBuffer;
    private GraphicsBuffer _coarseNormsBuffer;
    private GraphicsBuffer _coarseUvsBuffer;
    private GraphicsBuffer _coarseIndicesBuffer;
    private GraphicsBuffer _finalVertDataOut;
    private GraphicsBuffer _drawArgsBuffer;
    private uint[] _drawArgs;
    private GraphicsBuffer _triangleCounter;
    private int _triangleCount;
    
    private GeneratedVertex[] _finalVertDataOutCpuBuffer;
    
    private static class ShaderID
    {
        public static readonly int VertsIn = Shader.PropertyToID("VertsIn");
        public static readonly int NormsIn = Shader.PropertyToID("NormsIn");
        public static readonly int UVsIn = Shader.PropertyToID("UVsIn");
        public static readonly int IndicesIn = Shader.PropertyToID("IndicesIn");
        public static readonly int VertsOut = Shader.PropertyToID("VertsOut");
        public static readonly int TriangleCount = Shader.PropertyToID("TriangleCount");
        public static readonly int DrawArgs = Shader.PropertyToID("DrawArgs");
        public static readonly int VertData = Shader.PropertyToID("VertData");
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
        _matCopy = Instantiate(Material);

        SetupBuffers();
        RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
    }
    
    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
        
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
            _singleTriangleMesh.GetIndexCount(0),
            (uint)_triangleCount * 3, // set by the compute shader
            _singleTriangleMesh.GetIndexStart(0),
            _singleTriangleMesh.GetBaseVertex(0),
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
        
        // Output buffer
        _finalVertDataOut = new GraphicsBuffer(
            GraphicsBuffer.Target.Structured, 
            _triangleCount * 3, 
            Marshal.SizeOf<GeneratedVertex>()
        );
        _finalVertDataOut.name = "Vert Data Out";
        _finalVertDataOutCpuBuffer = new GeneratedVertex[_triangleCount * 3];
        _finalVertDataOut.SetData(_finalVertDataOutCpuBuffer); // clear the output memory

        AssignBuffersToShaders();
    }

    private void AssignBuffersToShaders()
    {
        // Assign buffers to the compute shader
        _computeShaderCopy.SetBuffer(0, ShaderID.VertsIn, _coarseVertsBuffer);
        _computeShaderCopy.SetBuffer(0, ShaderID.NormsIn, _coarseNormsBuffer);
        _computeShaderCopy.SetBuffer(0, ShaderID.UVsIn, _coarseUvsBuffer);
        _computeShaderCopy.SetBuffer(0, ShaderID.IndicesIn, _coarseIndicesBuffer);
        _computeShaderCopy.SetBuffer(0, ShaderID.VertsOut, _finalVertDataOut);
        _computeShaderCopy.SetBuffer(0, ShaderID.DrawArgs, _drawArgsBuffer);
        _computeShaderCopy.SetInt(ShaderID.TriangleCount, _triangleCount);
        
        // Assign buffers to the material shader
        _matCopy.SetBuffer(ShaderID.VertData, _finalVertDataOut);
        _matCopy.SetInteger(ShaderID.TriangleCount, _triangleCount);
    }

    private void Update()
    {
        // Starting the compute shader during update so it has some time to run before the camera renders
        // _computeShaderCopy.GetKernelThreadGroupSizes(0, out var groupSize, out _, out _);
        // var threadGroupCount = Mathf.CeilToInt(_triangleCount / (float)groupSize);
        // _computeShaderCopy.Dispatch(0, threadGroupCount, 1, 1);
        Debug.Log($"Updating Mesh with {_triangleCount} dispatch size");
        _computeShaderCopy.Dispatch(0, _triangleCount, 1, 1);
    }
    
    private void OnBeginCameraRendering(ScriptableRenderContext ctx, Camera camera)
    {
        // Debug read the vert data back to verify it
        _finalVertDataOut.GetData(_finalVertDataOutCpuBuffer);
        var drawArgs = new uint[5];
        // _triangleCounter.GetData(count);

        var vertCount = 0;
        foreach (var gVert in _finalVertDataOutCpuBuffer)
        {
            if (math.length(gVert.Position) > 0.0f)
            {
                vertCount++;
            }
        }
        
        //Debug.Log($"Counter: {count[0]}");
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

    public void OnBeforeSerialize()
    {
        // Saving the scene un-assigns all the buffers from the shaders for some reason
        AssignBuffersToShaders();
    }

    public void OnAfterDeserialize()
    {
        AssignBuffersToShaders();
    }
}
