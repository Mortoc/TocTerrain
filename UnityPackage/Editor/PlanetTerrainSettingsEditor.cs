using System.Collections;
using UnityEditor;
using UnityEngine;

namespace TocTerrain.EditorTools
{
    [CustomEditor(typeof(PlanetTerrainSettings))]
    public class PlanetTerrainSettingsEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            if (GUILayout.Button("Build"))
            {
                
            }
        }
    }
}
