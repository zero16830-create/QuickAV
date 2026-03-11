using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace UnityAV
{
    /// <summary>
    /// 负责初始化 Unity 与 native 插件之间的公共桥接。
    /// </summary>
    internal static class NativeInitializer
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
#else
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
#endif
        private delegate void LogDelegate(IntPtr message);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_GetRenderEventFunc")]
        private static extern IntPtr GetRenderEventFunc();

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_GetRenderEventBaseId")]
        private static extern int GetRenderEventBaseId();

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_DebugInitialize")]
        private static extern void DebugInitialize(bool cacheLogs);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_DebugTeardown")]
        private static extern void DebugTeardown();

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_DebugClearCallbacks")]
        private static extern void DeregisterAllCallbacks();

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_DebugRegisterLogCallback")]
        private static extern void RegisterLogCallback(LogDelegate callback);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_DebugRegisterWarningCallback")]
        private static extern void RegisterWarningCallback(LogDelegate callback);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_DebugRegisterErrorCallback")]
        private static extern void RegisterErrorCallback(LogDelegate callback);

        private static readonly LogDelegate DebugLogThunkDelegate = DebugLogThunk;
        private static readonly LogDelegate WarningLogThunkDelegate = WarningLogThunk;
        private static readonly LogDelegate ErrorMethodThunkDelegate = ErrorMethodThunk;
        private static readonly object RenderEventPlayersLock = new object();
        private static readonly HashSet<int> RenderEventPlayerIds = new HashSet<int>();

        private static bool Initialized;
        private static RenderEventDriver RenderDriver;
        private static int CachedRenderEventBaseId = int.MinValue;

        public static void Initialize(MonoBehaviour monoBehaviour)
        {
            Initialize(monoBehaviour, true);
        }

        public static void InitializePullOnly(MonoBehaviour monoBehaviour)
        {
            Initialize(monoBehaviour, false);
        }

        private static void Initialize(MonoBehaviour monoBehaviour, bool issueRenderEvents)
        {
            if (monoBehaviour == null)
            {
                throw new ArgumentNullException("monoBehaviour");
            }

            if (!Initialized)
            {
                DebugInitialize(true);
                RegisterLogCallback(DebugLogThunkDelegate);
                RegisterWarningCallback(WarningLogThunkDelegate);
                RegisterErrorCallback(ErrorMethodThunkDelegate);
                Initialized = true;
            }

            if (issueRenderEvents)
            {
                EnsureRenderDriver();
            }
        }

        public static void Teardown()
        {
            if (RenderDriver != null)
            {
                UnityEngine.Object.Destroy(RenderDriver.gameObject);
                RenderDriver = null;
            }

            lock (RenderEventPlayersLock)
            {
                RenderEventPlayerIds.Clear();
            }
            CachedRenderEventBaseId = int.MinValue;

            if (!Initialized)
            {
                return;
            }

            DebugTeardown();
            Initialized = false;
        }

        public static void RegisterPlayerRenderEvent(int playerId)
        {
            if (playerId < 0)
            {
                return;
            }

            lock (RenderEventPlayersLock)
            {
                RenderEventPlayerIds.Add(playerId);
            }
        }

        public static void UnregisterPlayerRenderEvent(int playerId)
        {
            if (playerId < 0)
            {
                return;
            }

            lock (RenderEventPlayersLock)
            {
                RenderEventPlayerIds.Remove(playerId);
            }
        }

        private static void EnsureRenderDriver()
        {
            if (RenderDriver != null)
            {
                return;
            }

            var host = new GameObject("UnityAV.Native.RenderDriver");
            host.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(host);
            RenderDriver = host.AddComponent<RenderEventDriver>();
            RenderDriver.StartDriving();
        }

        private static void OnRenderDriverDestroyed(RenderEventDriver driver)
        {
            if (ReferenceEquals(RenderDriver, driver))
            {
                RenderDriver = null;
            }
        }

        private static void DebugLogThunk(IntPtr message)
        {
            Debug.Log("rustav_native: " + PtrToString(message));
        }

        private static void WarningLogThunk(IntPtr message)
        {
            Debug.LogWarning("rustav_native: " + PtrToString(message));
        }

        private static void ErrorMethodThunk(IntPtr message)
        {
            Debug.LogError("rustav_native: " + PtrToString(message));
        }

        private static string PtrToString(IntPtr message)
        {
            return message == IntPtr.Zero ? string.Empty : (Marshal.PtrToStringAnsi(message) ?? string.Empty);
        }

        private static int[] SnapshotRenderEventPlayerIds()
        {
            lock (RenderEventPlayersLock)
            {
                if (RenderEventPlayerIds.Count == 0)
                {
                    return Array.Empty<int>();
                }

                var ids = new int[RenderEventPlayerIds.Count];
                RenderEventPlayerIds.CopyTo(ids);
                return ids;
            }
        }

        private static int ResolveRenderEventBaseId()
        {
            if (CachedRenderEventBaseId != int.MinValue)
            {
                return CachedRenderEventBaseId;
            }

            try
            {
                CachedRenderEventBaseId = GetRenderEventBaseId();
            }
            catch (EntryPointNotFoundException)
            {
                CachedRenderEventBaseId = -1;
            }

            return CachedRenderEventBaseId;
        }

        private sealed class RenderEventDriver : MonoBehaviour
        {
            private static readonly WaitForEndOfFrame EndOfFrameYield = new WaitForEndOfFrame();
            private Coroutine _coroutine;

            public void StartDriving()
            {
                if (_coroutine == null)
                {
                    _coroutine = StartCoroutine(CallPluginAtEndOfFrames());
                }
            }

            private void OnDestroy()
            {
                if (_coroutine != null)
                {
                    StopCoroutine(_coroutine);
                    _coroutine = null;
                }

                NativeInitializer.OnRenderDriverDestroyed(this);
            }

            private static IEnumerator CallPluginAtEndOfFrames()
            {
                while (true)
                {
                    yield return EndOfFrameYield;
                    var callback = GetRenderEventFunc();
                    if (callback != IntPtr.Zero)
                    {
                        var renderEventBaseId = ResolveRenderEventBaseId();
                        var playerIds = SnapshotRenderEventPlayerIds();
                        if (renderEventBaseId >= 0 && playerIds.Length > 0)
                        {
                            for (var index = 0; index < playerIds.Length; index += 1)
                            {
                                var playerId = playerIds[index];
                                if (playerId < 0)
                                {
                                    continue;
                                }

                                GL.IssuePluginEvent(callback, renderEventBaseId + playerId);
                            }
                        }
                        else
                        {
                            GL.IssuePluginEvent(callback, 1);
                        }
                    }
                }
            }
        }
    }
}
