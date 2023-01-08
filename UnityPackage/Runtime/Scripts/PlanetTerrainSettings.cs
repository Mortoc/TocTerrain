using UnityEngine;

namespace TocTerrain
{
    [CreateAssetMenu(fileName = "New Planet Terrain", menuName = "Planet Terrain")]
    public class PlanetTerrainSettings : ScriptableObject
    {
        [Tooltip("Initial sphere mesh. This will be the lowest-detail LOD, further mesh tesselation is done in GPU.")]
        public Mesh InitialMesh;
        
        [Tooltip("Average terrain height for the planet being generated. Can also be thought " +
                 "of as sea level where applicable.")]
        public float PlanetRadius = 20000.0f;

        [Tooltip("Stop subdividing the terrain mesh when the triangles are fewer pixels than this value.")] 
        public float TargetTrianglePixelSize = 24.0f;
        
        // Compute Shader
        public ComputeShader ComputeShader;
    }
}
