using System;
using System.Collections;
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

        private static bool Initialized;
        private static RenderEventDriver RenderDriver;

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

            if (!Initialized)
            {
                return;
            }

            DebugTeardown();
            Initialized = false;
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
                        GL.IssuePluginEvent(callback, 1);
                    }
                }
            }
        }
    }
}
