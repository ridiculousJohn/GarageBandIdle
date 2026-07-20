using System;
using System.Collections.Generic;
using UnityEngine;
using System.Reflection;
using UnityEngine.SceneManagement;
using System.Linq;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace RidiculousGaming.Utilities
{
    public static class SingletonManager
    {
        private static readonly Dictionary<Type, MonoBehaviour> _instances = new();
        private static bool _quitting = false;

        static SingletonManager()
        {
#if UNITY_EDITOR
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode)
                {
                    Clear();
                }
            };
#endif
            _quitting = false;
            Application.quitting += OnQuitting;
            SceneManager.sceneUnloaded += HandleSceneUnloaded;
        }

        // if something is DontDestroyOnLoad, it is moved to a special internal 'DontDestroyOnLoad' scene.
        // Conversely, on a scene unload, if we have a singleton that is a member of the scene being unloaded
        // then we know it necessarily must have not been 'DontDestroyOnLoad' and thus can be destroyed
        private static void HandleSceneUnloaded(Scene scene)
        {
            foreach (var kvp in _instances.ToList())
            {
                if (kvp.Value.gameObject.scene == scene)
                {
                    _instances.Remove(kvp.Key);
                }
            }
        }

        private static void OnQuitting()
        {
            _quitting = true;
            Application.quitting -= OnQuitting;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void ClearOnLoadRuntime()
        {
            Clear();

            // With domain reload disabled statics survive exiting play mode, and
            // Application.quitting fires on play-mode exit: OnQuitting latched
            // _quitting = true and unsubscribed itself, which made GetInstance
            // return null for every play session after the first. This hook runs
            // on every play-mode entry, so undo both here.
            _quitting = false;
            Application.quitting -= OnQuitting;
            Application.quitting += OnQuitting;
        }

        public static bool IsAllocated<T>() where T : MonoBehaviour
        {
            var type = typeof(T);

            // if we already have this registered, just use it
            return _instances.TryGetValue(type, out var instance) && instance != null;
        }

        public static T GetInstance<T>() where T : MonoBehaviour
        {
            var type = typeof(T);

            // if we already have this registered, just use it
            if (_instances.TryGetValue(type, out var instance) && instance != null)
            {
                return (T)instance;
            }

            // if we're quitting, don't create any more singletons
            if (_quitting)
            {
                return null;
            }

            instance = UnityEngine.Object.FindAnyObjectByType<T>();
            var attr = type.GetCustomAttribute<SingletonPropertyAttribute>();
            if (instance == null)
            {
                GameObject go = new($"Singleton: {attr?.SingletonName ?? type.Name}");

                instance = go.AddComponent<T>();
            }

            if (attr != null && attr.DontDestroyOnLoad)
            {
                UnityEngine.Object.DontDestroyOnLoad(instance.gameObject);
            }

            // at this point, we either already had one in the scene but not registered, or didn't have one created
            // in both cases, just register it here, either remembering the one that was already in the scene, or 
            // saving the new one.
            _instances[type] = instance;
            return (T)instance;
        }

        /// <summary>
        /// Destroys the incoming instance if a different one of this type already exists
        /// 
        /// Returns true if the incoming one was destroyed
        /// </summary>
        public static bool DestroyIfRegistered<T>(T instance) where T : MonoBehaviour
        {
            if (_instances.TryGetValue(typeof(T), out var existing))
            {
                if (existing != null && existing != instance)
                {
                    UnityEngine.Object.Destroy(instance.gameObject); // Prevent duplicate
                    return true;
                }
            }

            return false;
        }

        public static void Clear()
        {
            _instances.Clear();
        }

        public static void Unregister(MonoBehaviour singleton)
        {
            if (_instances.TryGetValue(singleton.GetType(), out var instance) && instance != null && singleton != instance)
            {
                Debug.LogWarning($"[SingletonManager] Attempted to unregister a singleton that was not registered {singleton.GetType().Name}");
                return;
            }

            _instances.Remove(singleton.GetType());
        }
    }
}