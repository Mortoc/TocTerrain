using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TocTerrain
{
    /// <summary>
    /// Provides a safer interface for Object.Destroy.
    /// </summary>
    public static class SafeDestroy
    {
        private static void InternalDestroy(Object obj)
        {
            #if UNITY_EDITOR
            if (Application.isPlaying)
            {
                Object.Destroy(obj);
            }
            else
            {
                Object.DestroyImmediate(obj);
            }
            #else
            Object.Destroy(obj);
            #endif
        }
        
        public static void Asset(Object asset)
        {
            InternalDestroy(asset);
        }

        public static void Component(Component component)
        {
            InternalDestroy(component);
        }
        
        public static void GameObject(GameObject gameObject)
        {
            InternalDestroy(gameObject);
        }
        
        public static void Buffer(GraphicsBuffer buffer)
        {
            if (buffer != null)
            {
                buffer.Dispose();
            }
        }
        
        public static void Buffer(ComputeBuffer buffer)
        {
            if (buffer != null)
            {
                buffer.Dispose();
            }
        }
    }
}