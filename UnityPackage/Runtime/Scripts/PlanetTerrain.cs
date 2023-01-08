using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace TocTerrain
{
    [ExecuteAlways]
    [RequireComponent(typeof(CustomPassVolume))]
    public class PlanetTerrain : MonoBehaviour
    {
        [SerializeField] private PlanetTerrainSettings _settings;
        [SerializeField] private Material _material;

        private void OnEnable()
        {
            if (!_settings || !_material)
            {
                enabled = false;
                Debug.LogWarning(
                    $"Cannot enable {nameof(PlanetTerrain)} until Settings and Material are set", 
                    this
                );
                return;
            }

            transform.localScale = Vector3.one * _settings.PlanetRadius;
        }
    }
}
