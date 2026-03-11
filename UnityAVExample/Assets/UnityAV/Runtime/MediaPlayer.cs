using System;
using System.Collections;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace UnityAV
{
    /// <summary>
    /// 使用原生纹理直连模式的播放器。
    /// 当前主要服务 Windows 纹理互操作增强场景，不作为 Android/iOS 主播放入口。
    /// </summary>
    public class MediaPlayer : MonoBehaviour
    {
        private const int DefaultWidth = 1024;
        private const int DefaultHeight = 1024;
        private const int InvalidPlayerId = -1;

        public enum NativeVideoPresentationPathKind
        {
            None = 0,
            DirectBind = 1,
            DirectShader = 2,
            Compute = 3,
            RenderEventPass = 4,
        }

        public enum NativeVideoActivationDecisionKind
        {
            NotRequested = 0,
            InvalidTarget = 1,
            CapsUnavailable = 2,
            UnsupportedTarget = 3,
            HardwareDecodeUnavailable = 4,
            StrictZeroCopyUnavailable = 5,
            AcquireReleaseUnavailable = 6,
            CreateFailed = 7,
            Active = 8,
        }

        public struct PlayerRuntimeHealth
        {
            public MediaSourceConnectionState SourceConnectionState;
            public bool IsConnected;
            public bool IsPlaying;
            public bool IsRealtime;
            public long SourcePacketCount;
            public long SourceTimeoutCount;
            public long SourceReconnectCount;
            public double SourceLastActivityAgeSec;
            public double CurrentTimeSec;
        }

        public struct NativeVideoInteropInfo
        {
            public MediaBackendKind BackendKind;
            public NativeVideoPlatformKind PlatformKind;
            public NativeVideoSurfaceKind SurfaceKind;
            public bool Supported;
            public bool HardwareDecodeSupported;
            public bool ZeroCopySupported;
            public bool SourceSurfaceZeroCopySupported;
            public bool FallbackCopyPathSupported;
            public bool PresentedFrameDirectBindable;
            public bool PresentedFrameStrictZeroCopySupported;
            public bool SourcePlaneTexturesSupported;
            public bool SourcePlaneViewsSupported;
            public bool AcquireReleaseSupported;
            public uint Flags;
        }

        public struct NativeVideoFrameInfo
        {
            public NativeVideoSurfaceKind SurfaceKind;
            public NativeVideoPixelFormatKind PixelFormat;
            public IntPtr NativeHandle;
            public IntPtr AuxiliaryHandle;
            public int Width;
            public int Height;
            public double TimeSec;
            public long FrameIndex;
            public uint Flags;
        }

        public struct NativeVideoPlaneTexturesInfo
        {
            public NativeVideoSurfaceKind SurfaceKind;
            public NativeVideoPixelFormatKind SourcePixelFormat;
            public IntPtr YNativeHandle;
            public IntPtr YAuxiliaryHandle;
            public int YWidth;
            public int YHeight;
            public NativeVideoPlaneTextureFormatKind YTextureFormat;
            public IntPtr UVNativeHandle;
            public IntPtr UVAuxiliaryHandle;
            public int UVWidth;
            public int UVHeight;
            public NativeVideoPlaneTextureFormatKind UVTextureFormat;
            public double TimeSec;
            public long FrameIndex;
            public uint Flags;
        }

        public struct NativeVideoPlaneViewsInfo
        {
            public NativeVideoSurfaceKind SurfaceKind;
            public NativeVideoPixelFormatKind SourcePixelFormat;
            public IntPtr YNativeHandle;
            public IntPtr YAuxiliaryHandle;
            public int YWidth;
            public int YHeight;
            public NativeVideoPlaneTextureFormatKind YTextureFormat;
            public NativeVideoPlaneResourceKindKind YResourceKind;
            public IntPtr UVNativeHandle;
            public IntPtr UVAuxiliaryHandle;
            public int UVWidth;
            public int UVHeight;
            public NativeVideoPlaneTextureFormatKind UVTextureFormat;
            public NativeVideoPlaneResourceKindKind UVResourceKind;
            public double TimeSec;
            public long FrameIndex;
            public uint Flags;
        }

        /// <summary>
        /// The uri of the media to stream
        /// </summary>
        [Header("Media Properties:")]
        public string Uri;

        /// <summary>
        /// Preferred native backend.
        /// </summary>
        public MediaBackendKind PreferredBackend = MediaBackendKind.Auto;

        /// <summary>
        /// Whether fallback is forbidden when PreferredBackend is specified.
        /// </summary>
        public bool StrictBackend;

        /// <summary>
        /// Should the media be looped?
        /// </summary>
        public bool Loop;

        /// <summary>
        /// Should the media play as soon as it's loaded?
        /// </summary>
        public bool AutoPlay;

        /// <summary>
        /// The width of the texture in pixels
        /// </summary>
        [Header("Video Target Properties:")]
        [Range(2, 4096)]
        public int Width = DefaultWidth;

        /// <summary>
        /// The height of the texture in pixels
        /// </summary>
        [Range(2, 4096)]
        public int Height = DefaultHeight;

        /// <summary>
        /// The material to apply any streaming video to
        /// </summary>
        public Material TargetMaterial;

        /// <summary>
        /// 是否优先尝试 NativeVideo / 硬解增强路径。
        /// 失败时会自动回退到现有纹理上传路径。
        /// </summary>
        public bool PreferNativeVideo = true;

        /// <summary>
        /// 是否要求增强路径具备零 CPU 拷贝能力。
        /// </summary>
        public bool RequireNativeVideoZeroCopy;

        /// <summary>
        /// 是否要求增强路径启用硬件解码。
        /// </summary>
        public bool RequireNativeVideoHardwareDecode = true;

        /// <summary>
        /// 是否优先尝试 Unity 材质直接采样 source plane textures。
        /// 仅在 Windows NativeVideo + NV12 source plane textures 可用时生效。
        /// </summary>
        public bool PreferNativeVideoUnityDirectShader = true;

        /// <summary>
        /// 是否优先尝试直接通过 RenderEvent 写入 Unity RenderTarget。
        /// 仅在 Windows NativeVideo + direct target present 可用时生效。
        /// </summary>
        public bool PreferNativeVideoRenderEventPass = true;

        /// <summary>
        /// 是否优先尝试 Unity Compute Shader 消费 source plane textures。
        /// 仅在 Windows NativeVideo + NV12 source plane textures 可用时生效。
        /// </summary>
        public bool PreferNativeVideoUnityCompute;

        /// <summary>
        /// 可选的 NV12 直接采样 Shader。为空时会尝试从 Resources/NV12Direct 加载。
        /// </summary>
        public Shader NativeVideoNv12DirectShader;

        /// <summary>
        /// 可选的 NV12 -> RGBA Compute Shader。为空时会尝试从 Resources/NV12ToRGBA 加载。
        /// </summary>
        public ComputeShader NativeVideoNv12ComputeShader;

        private Texture _targetTexture;
        private Texture2D _boundNativeTexture;
        private Texture2D _nativePlaneTextureY;
        private Texture2D _nativePlaneTextureUV;
        private RenderTexture _nativeVideoComputeOutput;
        private int _id = InvalidPlayerId;
        private bool _playRequested;
        private bool _resumeAfterPause;
        private MediaBackendKind _actualBackendKind = MediaBackendKind.Auto;
        private bool _nativeVideoPathActive;
        private MediaNativeInteropCommon.NativeVideoInteropCapsView _nativeVideoInteropCaps;
        private float _playRequestedRealtimeAt = -1f;
        private float _firstNativeVideoFrameRealtimeAt = -1f;
        private NativeVideoFrameInfo _lastNativeVideoFrameInfo;
        private bool _hasLastNativeVideoFrameInfo;
        private long _lastAcquiredNativeFrameIndex = -1;
        private long _nativeVideoFrameAcquireCount;
        private long _nativeVideoFrameReleaseCount;
        private bool _nativeVideoBindingWarningIssued;
        private bool _nativeVideoDirectShaderWarningIssued;
        private bool _nativeVideoComputeWarningIssued;
        private bool _nativeTextureBound;
        private bool _nativePlaneTexturesBound;
        private bool _nativeVideoDirectShaderPathActive;
        private bool _nativeVideoComputePathActive;
        private bool _nativeVideoSourceSurfaceZeroCopyActive;
        private bool _nativeVideoSourcePlaneTexturesZeroCopyActive;
        private long _nativeTextureBindCount;
        private long _nativePlaneTextureBindCount;
        private long _nativeVideoDirectShaderBindCount;
        private NativeVideoPresentationPathKind _nativeVideoPresentationPath =
            NativeVideoPresentationPathKind.None;
        private NativeVideoActivationDecisionKind _nativeVideoActivationDecision =
            NativeVideoActivationDecisionKind.NotRequested;
        private IntPtr _lastBoundNativeHandle = IntPtr.Zero;
        private IntPtr _lastBoundNativePlaneYHandle = IntPtr.Zero;
        private IntPtr _lastBoundNativePlaneUVHandle = IntPtr.Zero;
        private int _nativeVideoComputeKernel = -1;
        private Shader _originalTargetMaterialShader;
        private bool _capturedTargetMaterialShader;

        private static readonly int UseNativeVideoPlaneTexturesPropertyId =
            Shader.PropertyToID("_UseNativeVideoPlaneTextures");
        private static readonly int YPlanePropertyId = Shader.PropertyToID("_YPlane");
        private static readonly int UVPlanePropertyId = Shader.PropertyToID("_UVPlane");
        private static readonly int FlipVerticalPropertyId = Shader.PropertyToID("_FlipVertical");

        public MediaBackendKind ActualBackendKind
        {
            get { return _actualBackendKind; }
        }

        public bool IsNativeVideoPathActive
        {
            get { return _nativeVideoPathActive; }
        }

        public bool HasPresentedNativeVideoFrame
        {
            get { return _lastAcquiredNativeFrameIndex >= 0; }
        }

        public long NativeVideoFrameAcquireCount
        {
            get { return _nativeVideoFrameAcquireCount; }
        }

        public long NativeVideoFrameReleaseCount
        {
            get { return _nativeVideoFrameReleaseCount; }
        }

        public bool IsNativeTextureBound
        {
            get { return _nativeTextureBound; }
        }

        public long NativeTextureBindCount
        {
            get { return _nativeTextureBindCount; }
        }

        public bool HasBoundNativeVideoPlaneTextures
        {
            get { return _nativePlaneTexturesBound; }
        }

        public bool IsNativeVideoComputePathActive
        {
            get { return _nativeVideoComputePathActive; }
        }

        public bool IsNativeVideoDirectShaderPathActive
        {
            get { return _nativeVideoDirectShaderPathActive; }
        }

        public long NativeVideoPlaneTextureBindCount
        {
            get { return _nativePlaneTextureBindCount; }
        }

        public long NativeVideoDirectShaderBindCount
        {
            get { return _nativeVideoDirectShaderBindCount; }
        }

        public NativeVideoPresentationPathKind NativeVideoPresentationPath
        {
            get { return _nativeVideoPresentationPath; }
        }

        public NativeVideoActivationDecisionKind NativeVideoActivationDecision
        {
            get { return _nativeVideoActivationDecision; }
        }

        public bool IsNativeVideoStrictZeroCopyActive
        {
            get
            {
                var presentedFrameStrictZeroCopy =
                    _hasLastNativeVideoFrameInfo
                    && (_nativeVideoPresentationPath == NativeVideoPresentationPathKind.DirectBind
                        || _nativeVideoPresentationPath
                            == NativeVideoPresentationPathKind.RenderEventPass)
                    && HasNativeVideoFrameFlag(
                        _lastNativeVideoFrameInfo.Flags,
                        MediaNativeInteropCommon.NativeVideoFrameFlagZeroCopy)
                    && !HasNativeVideoFrameFlag(
                        _lastNativeVideoFrameInfo.Flags,
                        MediaNativeInteropCommon.NativeVideoFrameFlagCpuFallback);

                var directShaderStrictZeroCopy =
                    _nativeVideoPresentationPath == NativeVideoPresentationPathKind.DirectShader
                    && _nativePlaneTexturesBound
                    && _nativeVideoDirectShaderPathActive
                    && !_nativeVideoComputePathActive
                    && _nativeVideoSourcePlaneTexturesZeroCopyActive;

                return presentedFrameStrictZeroCopy || directShaderStrictZeroCopy;
            }
        }

        public bool IsNativeVideoZeroCpuCopyActive
        {
            get
            {
                return _nativeVideoPathActive
                    && _nativeVideoActivationDecision == NativeVideoActivationDecisionKind.Active
                    && _nativeVideoSourceSurfaceZeroCopyActive;
            }
        }

        public bool IsNativeVideoSourcePlaneTexturesZeroCopyActive
        {
            get { return _nativeVideoSourcePlaneTexturesZeroCopyActive; }
        }

        public float StartupElapsedSeconds
        {
            get
            {
                if (_playRequestedRealtimeAt < 0f)
                {
                    return 0f;
                }

                return Mathf.Max(0f, UnityEngine.Time.realtimeSinceStartup - _playRequestedRealtimeAt);
            }
        }

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerCreateTexture")]
        private static extern int GetPlayer(string uri, IntPtr texturePointer);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerCreateTextureEx")]
        private static extern int GetPlayerEx(
            string uri,
            IntPtr texturePointer,
            ref MediaNativeInteropCommon.RustAVPlayerOpenOptions options);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerGetNativeVideoInteropCaps")]
        private static extern int GetNativeVideoInteropCaps(
            int backendKind,
            string path,
            ref MediaNativeInteropCommon.RustAVNativeVideoTarget target,
            ref MediaNativeInteropCommon.RustAVNativeVideoInteropCaps caps);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerCreateNativeVideoOutput")]
        private static extern int GetNativeVideoPlayer(
            string uri,
            ref MediaNativeInteropCommon.RustAVNativeVideoTarget target);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerCreateNativeVideoOutputEx")]
        private static extern int GetNativeVideoPlayerEx(
            string uri,
            ref MediaNativeInteropCommon.RustAVNativeVideoTarget target,
            ref MediaNativeInteropCommon.RustAVPlayerOpenOptions options);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerAcquireNativeVideoFrame")]
        private static extern int AcquireNativeVideoFrame(
            int id,
            ref MediaNativeInteropCommon.RustAVNativeVideoFrame frame);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerAcquireNativeVideoSourceFrame")]
        private static extern int AcquireNativeVideoSourceFrame(
            int id,
            ref MediaNativeInteropCommon.RustAVNativeVideoFrame frame);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerGetNativeVideoSourcePlaneTextures")]
        private static extern int GetNativeVideoSourcePlaneTextures(
            int id,
            ref MediaNativeInteropCommon.RustAVNativeVideoPlaneTextures textures);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerGetNativeVideoSourcePlaneViews")]
        private static extern int GetNativeVideoSourcePlaneViews(
            int id,
            ref MediaNativeInteropCommon.RustAVNativeVideoPlaneViews views);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerReleaseNativeVideoFrame")]
        private static extern int ReleaseNativeVideoFrame(
            int id,
            long frameIndex);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerRelease")]
        private static extern int ReleasePlayer(int id);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerUpdate")]
        private static extern int UpdatePlayer(int id);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerGetDuration")]
        private static extern double Duration(int id);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerGetTime")]
        private static extern double Time(int id);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerPlay")]
        private static extern int Play(int id);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerStop")]
        private static extern int Stop(int id);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerSeek")]
        private static extern int Seek(int id, double time);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerSetLoop")]
        private static extern int SetLoop(int id, bool loop);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerGetBackendKind")]
        private static extern int GetPlayerBackendKind(int id);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerGetHealthSnapshotV2")]
        private static extern int GetPlayerHealthSnapshotV2(
            int id,
            ref MediaNativeInteropCommon.RustAVPlayerHealthSnapshotV2 snapshot);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_GetBackendRuntimeDiagnostic")]
        private static extern int GetBackendRuntimeDiagnostic(
            int backendKind,
            string path,
            bool requireAudioExport,
            StringBuilder destination,
            int destinationLength);

        /// <summary>
        /// Begins or resumes playback
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        public void Play()
        {
            if (!ValidatePlayerId(_id))
            {
                throw new InvalidOperationException($"{nameof(MediaPlayer)} has no " +
                    "underlying valid native player.");
            }

            var result = Play(_id);

            if (result < 0)
            {
                throw new Exception($"Failed to play with error {result}");
            }

            if (_playRequestedRealtimeAt < 0f)
            {
                _playRequestedRealtimeAt = UnityEngine.Time.realtimeSinceStartup;
            }

            _playRequested = true;
        }

        /// <summary>
        /// Stops playback
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown if MediaPlayer failed to 
        /// obtain a native player</exception>
        public void Stop()
        {
            if (!ValidatePlayerId(_id))
            {
                throw new InvalidOperationException($"{nameof(MediaPlayer)} has no " +
                    "underlying valid native player.");
            }

            var result = Stop(_id);

            if (result < 0)
            {
                throw new Exception($"Failed to stop with error {result}");
            }

            _playRequested = false;
        }

        /// <summary>
        /// Evaluates the duration
        /// </summary>
        /// <returns>The duration in seconds</returns>
        /// <exception cref="InvalidOperationException">Thrown if MediaPlayer failed to 
        /// obtain a native player</exception>
        public double Duration()
        {
            if (!ValidatePlayerId(_id))
            {
                throw new InvalidOperationException($"{nameof(MediaPlayer)} has no " +
                    "underlying valid native player.");
            }

            var result = Duration(_id);

            if (result < 0)
            {
                throw new Exception("Failed to get duration");
            }

            return result;
        }

        /// <summary>
        /// Evaluates the current time
        /// </summary>
        /// <returns>The time in seconds since playback start</returns>
        /// <exception cref="InvalidOperationException">Thrown if MediaPlayer failed to 
        /// obtain a native player</exception>
        public double Time()
        {
            if (!ValidatePlayerId(_id))
            {
                throw new InvalidOperationException($"{nameof(MediaPlayer)} has no " +
                    "underlying valid native player.");
            }

            var result = Time(_id);

            if (result < 0)
            {
                throw new Exception("Failed to get time");
            }

            return result;
        }

        /// <summary>
        /// Seeks the playback
        /// </summary>
        /// <param name="time">The time to seek to in seconds</param>
        /// <exception cref="InvalidOperationException">Thrown if MediaPlayer failed to 
        /// obtain a native player</exception>
        public void Seek(double time)
        {
            if (!ValidatePlayerId(_id))
            {
                throw new InvalidOperationException($"{nameof(MediaPlayer)} has no " +
                    "underlying valid native player.");
            }

            var result = Seek(_id, time);

            if (result < 0)
            {
                throw new Exception($"Failed to seek with error {result}");
            }
        }

        public bool TryGetRuntimeHealth(out PlayerRuntimeHealth health)
        {
            health = default(PlayerRuntimeHealth);
            if (!ValidatePlayerId(_id))
            {
                return false;
            }

            MediaNativeInteropCommon.RuntimeHealthView runtimeHealth;
            if (!MediaNativeInteropCommon.TryReadRuntimeHealth(
                GetPlayerHealthSnapshotV2,
                _id,
                out runtimeHealth))
            {
                return false;
            }

            health = new PlayerRuntimeHealth
            {
                SourceConnectionState = runtimeHealth.SourceConnectionState,
                IsConnected = runtimeHealth.IsConnected,
                IsPlaying = runtimeHealth.IsPlaying,
                IsRealtime = runtimeHealth.IsRealtime,
                SourcePacketCount = runtimeHealth.SourcePacketCount,
                SourceTimeoutCount = runtimeHealth.SourceTimeoutCount,
                SourceReconnectCount = runtimeHealth.SourceReconnectCount,
                SourceLastActivityAgeSec = runtimeHealth.SourceLastActivityAgeSec,
                CurrentTimeSec = runtimeHealth.CurrentTimeSec,
            };
            return true;
        }

        public bool TryGetNativeVideoInteropInfo(out NativeVideoInteropInfo info)
        {
            info = default(NativeVideoInteropInfo);
            if (!_nativeVideoInteropCaps.Supported && _nativeVideoInteropCaps.Flags == 0)
            {
                return false;
            }

            info = new NativeVideoInteropInfo
            {
                BackendKind = _nativeVideoInteropCaps.BackendKind,
                PlatformKind = _nativeVideoInteropCaps.PlatformKind,
                SurfaceKind = _nativeVideoInteropCaps.SurfaceKind,
                Supported = _nativeVideoInteropCaps.Supported,
                HardwareDecodeSupported = _nativeVideoInteropCaps.HardwareDecodeSupported,
                ZeroCopySupported = _nativeVideoInteropCaps.ZeroCopySupported,
                SourceSurfaceZeroCopySupported = _nativeVideoInteropCaps.SourceSurfaceZeroCopySupported,
                FallbackCopyPathSupported =
                    (_nativeVideoInteropCaps.Flags & MediaNativeInteropCommon.NativeVideoCapFlagFallbackCopyPath) != 0,
                PresentedFrameDirectBindable = _nativeVideoInteropCaps.PresentedFrameDirectBindable,
                PresentedFrameStrictZeroCopySupported = _nativeVideoInteropCaps.PresentedFrameStrictZeroCopySupported,
                SourcePlaneTexturesSupported = _nativeVideoInteropCaps.SourcePlaneTexturesSupported,
                SourcePlaneViewsSupported = _nativeVideoInteropCaps.SourcePlaneViewsSupported,
                AcquireReleaseSupported = _nativeVideoInteropCaps.AcquireReleaseSupported,
                Flags = _nativeVideoInteropCaps.Flags,
            };
            return true;
        }

        public bool TryAcquireNativeVideoFrameInfo(out NativeVideoFrameInfo frameInfo)
        {
            frameInfo = default(NativeVideoFrameInfo);
            if (!_nativeVideoPathActive || !ValidatePlayerId(_id))
            {
                return false;
            }

            MediaNativeInteropCommon.NativeVideoFrameView frameView;
            if (!MediaNativeInteropCommon.TryAcquireNativeVideoFrame(
                AcquireNativeVideoFrame,
                _id,
                out frameView))
            {
                return false;
            }

            frameInfo = new NativeVideoFrameInfo
            {
                SurfaceKind = frameView.SurfaceKind,
                PixelFormat = MediaNativeInteropCommon.ToPublicNativeVideoPixelFormat(
                    frameView.PixelFormat),
                NativeHandle = frameView.NativeHandle,
                AuxiliaryHandle = frameView.AuxiliaryHandle,
                Width = frameView.Width,
                Height = frameView.Height,
                TimeSec = frameView.TimeSec,
                FrameIndex = frameView.FrameIndex,
                Flags = frameView.Flags,
            };
            return true;
        }

        public bool ReleaseNativeVideoFrameInfo(long frameIndex)
        {
            if (!_nativeVideoPathActive || !ValidatePlayerId(_id))
            {
                return false;
            }

            return MediaNativeInteropCommon.TryReleaseNativeVideoFrame(
                ReleaseNativeVideoFrame,
                _id,
                frameIndex);
        }

        public bool TryAcquireNativeVideoSourceFrameInfo(out NativeVideoFrameInfo frameInfo)
        {
            frameInfo = default(NativeVideoFrameInfo);
            if (!_nativeVideoPathActive || !ValidatePlayerId(_id))
            {
                return false;
            }

            MediaNativeInteropCommon.NativeVideoFrameView frameView;
            if (!MediaNativeInteropCommon.TryAcquireNativeVideoFrame(
                AcquireNativeVideoSourceFrame,
                _id,
                out frameView))
            {
                return false;
            }

            frameInfo = new NativeVideoFrameInfo
            {
                SurfaceKind = frameView.SurfaceKind,
                PixelFormat = MediaNativeInteropCommon.ToPublicNativeVideoPixelFormat(
                    frameView.PixelFormat),
                NativeHandle = frameView.NativeHandle,
                AuxiliaryHandle = frameView.AuxiliaryHandle,
                Width = frameView.Width,
                Height = frameView.Height,
                TimeSec = frameView.TimeSec,
                FrameIndex = frameView.FrameIndex,
                Flags = frameView.Flags,
            };
            _nativeVideoSourceSurfaceZeroCopyActive = HasNativeVideoFrameFlag(
                frameInfo.Flags,
                MediaNativeInteropCommon.NativeVideoFrameFlagZeroCopy);
            return true;
        }

        public bool TryAcquireNativeVideoSourcePlaneTexturesInfo(
            out NativeVideoPlaneTexturesInfo texturesInfo)
        {
            texturesInfo = default(NativeVideoPlaneTexturesInfo);
            if (!_nativeVideoPathActive || !ValidatePlayerId(_id))
            {
                return false;
            }

            MediaNativeInteropCommon.NativeVideoPlaneTexturesView texturesView;
            if (!MediaNativeInteropCommon.TryReadNativeVideoSourcePlaneTextures(
                GetNativeVideoSourcePlaneTextures,
                _id,
                out texturesView))
            {
                return false;
            }

            texturesInfo = new NativeVideoPlaneTexturesInfo
            {
                SurfaceKind = texturesView.SurfaceKind,
                SourcePixelFormat = MediaNativeInteropCommon.ToPublicNativeVideoPixelFormat(
                    texturesView.SourcePixelFormat),
                YNativeHandle = texturesView.YNativeHandle,
                YAuxiliaryHandle = texturesView.YAuxiliaryHandle,
                YWidth = texturesView.YWidth,
                YHeight = texturesView.YHeight,
                YTextureFormat = MediaNativeInteropCommon.ToPublicNativeVideoPlaneTextureFormat(
                    texturesView.YTextureFormat),
                UVNativeHandle = texturesView.UVNativeHandle,
                UVAuxiliaryHandle = texturesView.UVAuxiliaryHandle,
                UVWidth = texturesView.UVWidth,
                UVHeight = texturesView.UVHeight,
                UVTextureFormat = MediaNativeInteropCommon.ToPublicNativeVideoPlaneTextureFormat(
                    texturesView.UVTextureFormat),
                TimeSec = texturesView.TimeSec,
                FrameIndex = texturesView.FrameIndex,
                Flags = texturesView.Flags,
            };
            _nativeVideoSourcePlaneTexturesZeroCopyActive = HasNativeVideoFrameFlag(
                texturesInfo.Flags,
                MediaNativeInteropCommon.NativeVideoFrameFlagZeroCopy);
            return true;
        }

        public bool TryAcquireNativeVideoSourcePlaneViewsInfo(
            out NativeVideoPlaneViewsInfo viewsInfo)
        {
            viewsInfo = default(NativeVideoPlaneViewsInfo);
            if (!_nativeVideoPathActive || !ValidatePlayerId(_id))
            {
                return false;
            }

            MediaNativeInteropCommon.NativeVideoPlaneViewsView viewsView;
            if (!MediaNativeInteropCommon.TryReadNativeVideoSourcePlaneViews(
                GetNativeVideoSourcePlaneViews,
                _id,
                out viewsView))
            {
                return false;
            }

            viewsInfo = new NativeVideoPlaneViewsInfo
            {
                SurfaceKind = viewsView.SurfaceKind,
                SourcePixelFormat = MediaNativeInteropCommon.ToPublicNativeVideoPixelFormat(
                    viewsView.SourcePixelFormat),
                YNativeHandle = viewsView.YNativeHandle,
                YAuxiliaryHandle = viewsView.YAuxiliaryHandle,
                YWidth = viewsView.YWidth,
                YHeight = viewsView.YHeight,
                YTextureFormat = MediaNativeInteropCommon.ToPublicNativeVideoPlaneTextureFormat(
                    viewsView.YTextureFormat),
                YResourceKind = MediaNativeInteropCommon.ToPublicNativeVideoPlaneResourceKind(
                    viewsView.YResourceKind),
                UVNativeHandle = viewsView.UVNativeHandle,
                UVAuxiliaryHandle = viewsView.UVAuxiliaryHandle,
                UVWidth = viewsView.UVWidth,
                UVHeight = viewsView.UVHeight,
                UVTextureFormat = MediaNativeInteropCommon.ToPublicNativeVideoPlaneTextureFormat(
                    viewsView.UVTextureFormat),
                UVResourceKind = MediaNativeInteropCommon.ToPublicNativeVideoPlaneResourceKind(
                    viewsView.UVResourceKind),
                TimeSec = viewsView.TimeSec,
                FrameIndex = viewsView.FrameIndex,
                Flags = viewsView.Flags,
            };
            return true;
        }

        public bool TryGetPresentedNativeVideoTimeSec(out double presentedVideoTimeSec)
        {
            presentedVideoTimeSec = _hasLastNativeVideoFrameInfo
                ? _lastNativeVideoFrameInfo.TimeSec
                : -1.0;
            return presentedVideoTimeSec >= 0.0;
        }

        public bool TryGetLastNativeVideoFrameInfo(out NativeVideoFrameInfo frameInfo)
        {
            frameInfo = _lastNativeVideoFrameInfo;
            return _hasLastNativeVideoFrameInfo;
        }

        private IEnumerator Start()
        {
            NativeInitializer.Initialize(this);

            MediaSourceResolver.PreparedMediaSource preparedSource = null;
            Exception resolveError = null;
            yield return MediaSourceResolver.PreparePlayableSource(
                Uri,
                value => preparedSource = value,
                error => resolveError = error);

            if (resolveError != null)
            {
                throw resolveError;
            }

            InitializeNativePlayer(preparedSource);
        }

        private void Update()
        {
            if (!ValidatePlayerId(_id))
            {
                return;
            }

            UpdatePlayer(_id);
            UpdateNativeVideoFrame();
        }

        private void InitializeNativePlayer(MediaSourceResolver.PreparedMediaSource preparedSource)
        {
            var uri = preparedSource.PlaybackUri;
            try
            {
                _targetTexture = CreateNativeVideoTargetTexture();
                var targetHandle = _targetTexture != null
                    ? _targetTexture.GetNativeTexturePtr()
                    : IntPtr.Zero;
                var auxiliaryHandle = ResolveNativeVideoAuxiliaryHandle(_targetTexture);
                Debug.Log(
                    "[MediaPlayer] create_target_texture"
                    + " texture_type=" + (_targetTexture != null ? _targetTexture.GetType().Name : "null")
                    + " target_handle=0x" + targetHandle.ToInt64().ToString("X")
                    + " auxiliary_handle=0x" + auxiliaryHandle.ToInt64().ToString("X")
                    + " graphics_format=" + DescribeTextureGraphicsFormat(_targetTexture)
                    + " msaa=" + DescribeTextureMsaa(_targetTexture)
                    + " use_mip_map=" + DescribeTextureUseMipMap(_targetTexture)
                    + " random_write=" + DescribeTextureRandomWrite(_targetTexture)
                    + " size=" + Width + "x" + Height);

                var openOptions = MediaNativeInteropCommon.CreateOpenOptions(
                    PreferredBackend,
                    StrictBackend);
                var nativeTargetExtraFlags = ResolveNativeVideoTargetExtraFlags();
                var nativeTarget = MediaNativeInteropCommon.CreateDefaultNativeVideoTarget(
                    targetHandle,
                    auxiliaryHandle,
                    Width,
                    Height,
                    nativeTargetExtraFlags);
                _nativeVideoInteropCaps = default(MediaNativeInteropCommon.NativeVideoInteropCapsView);
                _nativeVideoPathActive = false;
                _nativeVideoActivationDecision = PreferNativeVideo
                    ? NativeVideoActivationDecisionKind.CreateFailed
                    : NativeVideoActivationDecisionKind.NotRequested;

                if (PreferNativeVideo)
                {
                    TryCreateNativeVideoPlayer(uri, ref nativeTarget, ref openOptions);
                }

                if (!ValidatePlayerId(_id))
                {
                    Debug.Log(
                        "[MediaPlayer] fallback_texture_player_create"
                        + " target_handle=0x" + targetHandle.ToInt64().ToString("X")
                        + " texture_type=" + (_targetTexture != null ? _targetTexture.GetType().Name : "null"));
                    try
                    {
                        _id = GetPlayerEx(uri, targetHandle, ref openOptions);
                    }
                    catch (EntryPointNotFoundException)
                    {
                        _id = GetPlayer(uri, targetHandle);
                    }
                }

                if (ValidatePlayerId(_id))
                {
                    NativeInitializer.RegisterPlayerRenderEvent(_id);
                    _actualBackendKind = ReadActualBackendKind();
                    Debug.Log(
                        "[MediaPlayer] player_created requested_backend=" + PreferredBackend
                        + " actual_backend=" + _actualBackendKind
                        + " strict_backend=" + StrictBackend
                        + " native_video_requested=" + PreferNativeVideo
                        + " native_video_active=" + _nativeVideoPathActive
                        + " native_video_activation_decision=" + _nativeVideoActivationDecision
                        + " unity_direct_shader_requested="
                        + PreferNativeVideoUnityDirectShader
                        + " unity_compute_requested=" + PreferNativeVideoUnityCompute
                        + " source_plane_textures_supported="
                        + _nativeVideoInteropCaps.SourcePlaneTexturesSupported);
                    ApplyPresentedTexture(_targetTexture);
                    SetLoop(_id, Loop);
                }
                else
                {
                    var diagnostic = ReadBackendRuntimeDiagnostic(uri);
                    if (string.IsNullOrEmpty(diagnostic))
                    {
                        throw new Exception($"Failed to create player with error: {_id}");
                    }

                    throw new Exception($"Failed to create player with error: {_id}; {diagnostic}");
                }

                if (AutoPlay)
                {
                    Play();
                }
            }
            catch
            {
                ReleaseNativePlayerSilently();
                ReleaseManagedResources();
                throw;
            }
        }

        private void UpdateNativeVideoFrame()
        {
            if (!_nativeVideoPathActive)
            {
                return;
            }

            NativeVideoFrameInfo frameInfo;
            if (!TryAcquireNativeVideoFrameInfo(out frameInfo))
            {
                return;
            }

            try
            {
                if (frameInfo.FrameIndex == _lastAcquiredNativeFrameIndex)
                {
                    return;
                }

                _lastAcquiredNativeFrameIndex = frameInfo.FrameIndex;
                _lastNativeVideoFrameInfo = frameInfo;
                _hasLastNativeVideoFrameInfo = true;
                _nativeVideoFrameAcquireCount += 1;

                if (_firstNativeVideoFrameRealtimeAt < 0f)
                {
                    _firstNativeVideoFrameRealtimeAt = UnityEngine.Time.realtimeSinceStartup;
                    Debug.Log(
                        "[MediaPlayer] first_native_video_frame startup_seconds="
                        + StartupElapsedSeconds.ToString("F3")
                        + " frame_time=" + frameInfo.TimeSec.ToString("F3")
                        + " frame_index=" + frameInfo.FrameIndex
                        + " surface=" + frameInfo.SurfaceKind
                        + " pixel_format=" + frameInfo.PixelFormat
                        + " flags=0x" + frameInfo.Flags.ToString("X"));
                }

                var handled = false;
                if (PreferNativeVideoRenderEventPass)
                {
                    handled = TryUseNativeRenderTarget(frameInfo);
                }

                if (PreferNativeVideoUnityDirectShader)
                {
                    handled = handled || TryBindNativeVideoPlaneTexturesDirect(frameInfo);
                }

                if (!handled && PreferNativeVideoUnityCompute)
                {
                    handled = TryRenderNativeVideoPlaneTextures(frameInfo);
                }

                if (!handled && CanDirectlyBindNativeFrame(frameInfo))
                {
                    handled = TryBindNativeTexture(frameInfo);
                }

                if (!handled && !_nativeVideoBindingWarningIssued)
                {
                    _nativeVideoBindingWarningIssued = true;
                    Debug.LogWarning(
                        "[MediaPlayer] native_video_frame acquired without supported presentation path"
                        + " surface=" + frameInfo.SurfaceKind
                        + " pixel_format=" + frameInfo.PixelFormat
                        + " unity_direct_shader_requested="
                        + PreferNativeVideoUnityDirectShader
                        + " unity_compute_requested=" + PreferNativeVideoUnityCompute
                        + " source_plane_textures_supported="
                        + _nativeVideoInteropCaps.SourcePlaneTexturesSupported
                        + " flags=0x" + frameInfo.Flags.ToString("X"));
                }
            }
            finally
            {
                if (ReleaseNativeVideoFrameInfo(frameInfo.FrameIndex))
                {
                    _nativeVideoFrameReleaseCount += 1;
                }
            }
        }

        private void OnDestroy()
        {
            ReleaseNativePlayer();
            ReleaseManagedResources();
        }

        private void OnApplicationQuit()
        {
            ReleaseNativePlayer();
            ReleaseManagedResources();
            NativeInitializer.Teardown();
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (ValidatePlayerId(_id))
            {
                if (pauseStatus)
                {
                    _resumeAfterPause = _playRequested;
                    if (_resumeAfterPause)
                    {
                        Stop();
                    }
                }
                else if (_resumeAfterPause)
                {
                    Play();
                    _resumeAfterPause = false;
                }
            }
        }

        private static bool ValidatePlayerId(int id)
        {
            return id >= 0;
        }

        private static bool CanDirectlyBindNativeFrame(NativeVideoFrameInfo frameInfo)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            return frameInfo.SurfaceKind == NativeVideoSurfaceKind.D3D11Texture2D
                && frameInfo.PixelFormat == NativeVideoPixelFormatKind.Rgba32
                && frameInfo.NativeHandle != IntPtr.Zero;
#else
            return false;
#endif
        }

        private bool TryUseNativeRenderTarget(NativeVideoFrameInfo frameInfo)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            if (_targetTexture == null)
            {
                return false;
            }

            var targetHandle = _targetTexture.GetNativeTexturePtr();
            var auxiliaryHandle = ResolveNativeVideoAuxiliaryHandle(_targetTexture);
            if (targetHandle == IntPtr.Zero
                || frameInfo.SurfaceKind != NativeVideoSurfaceKind.D3D11Texture2D
                || frameInfo.PixelFormat != NativeVideoPixelFormatKind.Rgba32
                || (frameInfo.NativeHandle != targetHandle
                    && frameInfo.AuxiliaryHandle != targetHandle
                    && frameInfo.AuxiliaryHandle != auxiliaryHandle))
            {
                return false;
            }

            ApplyPresentedTexture(_targetTexture);

            if (_nativeVideoPresentationPath != NativeVideoPresentationPathKind.RenderEventPass)
            {
                Debug.Log(
                    "[MediaPlayer] native_render_target_present startup_seconds="
                    + StartupElapsedSeconds.ToString("F3")
                    + " frame_index=" + frameInfo.FrameIndex
                    + " handle=0x" + frameInfo.NativeHandle.ToInt64().ToString("X")
                    + " flags=0x" + frameInfo.Flags.ToString("X"));
            }

            _nativeTextureBound = false;
            _nativePlaneTexturesBound = false;
            _nativeVideoDirectShaderPathActive = false;
            _nativeVideoComputePathActive = false;
            _nativeVideoPresentationPath = NativeVideoPresentationPathKind.RenderEventPass;
            return true;
#else
            return false;
#endif
        }

        private static IntPtr ResolveNativeVideoAuxiliaryHandle(Texture targetTexture)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            if (targetTexture is RenderTexture renderTexture)
            {
                try
                {
                    return renderTexture.colorBuffer.GetNativeRenderBufferPtr();
                }
                catch (Exception)
                {
                    return IntPtr.Zero;
                }
            }
#endif

            return IntPtr.Zero;
        }

        private static bool CanUseUnityComputePlaneTextures(NativeVideoPlaneTexturesInfo texturesInfo)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            return texturesInfo.SurfaceKind == NativeVideoSurfaceKind.D3D11Texture2D
                && texturesInfo.SourcePixelFormat == NativeVideoPixelFormatKind.Nv12
                && texturesInfo.YNativeHandle != IntPtr.Zero
                && texturesInfo.UVNativeHandle != IntPtr.Zero
                && texturesInfo.YTextureFormat == NativeVideoPlaneTextureFormatKind.R8Unorm
                && texturesInfo.UVTextureFormat == NativeVideoPlaneTextureFormatKind.Rg8Unorm
                && texturesInfo.YWidth > 0
                && texturesInfo.YHeight > 0
                && texturesInfo.UVWidth > 0
                && texturesInfo.UVHeight > 0;
#else
            return false;
#endif
        }

        private void ApplyPresentedTexture(Texture texture)
        {
            DisableNativeVideoPlaneTextureMode();
            if (TargetMaterial == null)
            {
                return;
            }

            if (!ReferenceEquals(TargetMaterial.mainTexture, texture))
            {
                TargetMaterial.mainTexture = texture;
            }
        }

        private bool TryBindNativeTexture(NativeVideoFrameInfo frameInfo)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            try
            {
                var requiresRecreate = _boundNativeTexture == null
                    || _boundNativeTexture.width != frameInfo.Width
                    || _boundNativeTexture.height != frameInfo.Height;

                if (requiresRecreate)
                {
                    ReleaseBoundNativeTexture();
                    _boundNativeTexture = Texture2D.CreateExternalTexture(
                        frameInfo.Width,
                        frameInfo.Height,
                        TextureFormat.ARGB32,
                        false,
                        false,
                        frameInfo.NativeHandle);
                    _boundNativeTexture.filterMode = FilterMode.Bilinear;
                    _boundNativeTexture.name = Uri + "#NativeVideo";
                    _lastBoundNativeHandle = frameInfo.NativeHandle;
                }
                else if (_lastBoundNativeHandle != frameInfo.NativeHandle)
                {
                    _boundNativeTexture.UpdateExternalTexture(frameInfo.NativeHandle);
                    _lastBoundNativeHandle = frameInfo.NativeHandle;
                }

                ApplyPresentedTexture(_boundNativeTexture);

                if (!_nativeTextureBound)
                {
                    Debug.Log(
                        "[MediaPlayer] native_texture_bound startup_seconds="
                        + StartupElapsedSeconds.ToString("F3")
                        + " frame_index=" + frameInfo.FrameIndex
                        + " surface=" + frameInfo.SurfaceKind
                        + " pixel_format=" + frameInfo.PixelFormat
                        + " handle=0x" + frameInfo.NativeHandle.ToInt64().ToString("X"));
                }

                _nativeTextureBound = true;
                _nativePlaneTexturesBound = false;
                _nativeVideoDirectShaderPathActive = false;
                _nativeVideoComputePathActive = false;
                _nativeVideoPresentationPath = NativeVideoPresentationPathKind.DirectBind;
                _nativeTextureBindCount += 1;
                return true;
            }
            catch (Exception exception)
            {
                if (!_nativeVideoBindingWarningIssued)
                {
                    _nativeVideoBindingWarningIssued = true;
                    Debug.LogWarning(
                        "[MediaPlayer] direct_texture_binding failed surface="
                        + frameInfo.SurfaceKind
                        + " pixel_format=" + frameInfo.PixelFormat
                        + " handle=0x" + frameInfo.NativeHandle.ToInt64().ToString("X")
                        + " error=" + exception.Message);
                }

                _nativeTextureBound = false;
                _nativePlaneTexturesBound = false;
                _nativeVideoPresentationPath = NativeVideoPresentationPathKind.None;
                ApplyPresentedTexture(_targetTexture);
                return false;
            }
#endif
            return false;
        }

        private bool TryBindNativeVideoPlaneTexturesDirect(NativeVideoFrameInfo frameInfo)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            if (!_nativeVideoInteropCaps.SourcePlaneTexturesSupported)
            {
                return false;
            }

            var directShader = ResolveNativeVideoNv12DirectShader();
            if (directShader == null)
            {
                if (!_nativeVideoDirectShaderWarningIssued)
                {
                    _nativeVideoDirectShaderWarningIssued = true;
                    Debug.LogWarning(
                        "[MediaPlayer] unity_direct_shader requested but NV12Direct shader is unavailable");
                }

                return false;
            }

            NativeVideoPlaneTexturesInfo texturesInfo;
            if (!TryAcquireNativeVideoSourcePlaneTexturesInfo(out texturesInfo))
            {
                return false;
            }

            if (!CanUseUnityComputePlaneTextures(texturesInfo))
            {
                if (!_nativeVideoDirectShaderWarningIssued)
                {
                    _nativeVideoDirectShaderWarningIssued = true;
                    Debug.LogWarning(
                        "[MediaPlayer] unity_direct_shader requested but source plane textures are not usable"
                        + " surface=" + texturesInfo.SurfaceKind
                        + " source_pixel_format=" + texturesInfo.SourcePixelFormat
                        + " y_handle=0x" + texturesInfo.YNativeHandle.ToInt64().ToString("X")
                        + " uv_handle=0x" + texturesInfo.UVNativeHandle.ToInt64().ToString("X"));
                }

                return false;
            }

            try
            {
                if (!EnsureNativeVideoDirectShaderMaterial(directShader))
                {
                    return false;
                }

                EnsureNativePlaneTextureBindings(texturesInfo);
                BindNativeVideoPlaneTexturesToMaterial();

                if (!_nativeVideoDirectShaderPathActive)
                {
                    Debug.Log(
                        "[MediaPlayer] unity_direct_shader_bound startup_seconds="
                        + StartupElapsedSeconds.ToString("F3")
                        + " frame_index=" + frameInfo.FrameIndex
                        + " source_pixel_format=" + texturesInfo.SourcePixelFormat
                        + " y_handle=0x" + texturesInfo.YNativeHandle.ToInt64().ToString("X")
                        + " uv_handle=0x" + texturesInfo.UVNativeHandle.ToInt64().ToString("X")
                        + " source_flags=0x" + texturesInfo.Flags.ToString("X"));
                }

                _nativeVideoDirectShaderPathActive = true;
                _nativePlaneTexturesBound = true;
                _nativePlaneTextureBindCount += 1;
                _nativeVideoDirectShaderBindCount += 1;
                _nativeVideoPresentationPath = NativeVideoPresentationPathKind.DirectShader;
                _nativeTextureBound = false;
                _nativeVideoComputePathActive = false;
                return true;
            }
            catch (Exception exception)
            {
                if (!_nativeVideoDirectShaderWarningIssued)
                {
                    _nativeVideoDirectShaderWarningIssued = true;
                    Debug.LogWarning(
                        "[MediaPlayer] unity_direct_shader failed source_surface="
                        + texturesInfo.SurfaceKind
                        + " source_pixel_format=" + texturesInfo.SourcePixelFormat
                        + " error=" + exception.Message);
                }

                _nativeVideoDirectShaderPathActive = false;
                _nativeVideoPresentationPath = NativeVideoPresentationPathKind.None;
                _nativePlaneTexturesBound = false;
                DisableNativeVideoPlaneTextureMode();
                ApplyPresentedTexture(_targetTexture);
                return false;
            }
#else
            return false;
#endif
        }

        private bool TryRenderNativeVideoPlaneTextures(NativeVideoFrameInfo frameInfo)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            if (!_nativeVideoInteropCaps.SourcePlaneTexturesSupported)
            {
                return false;
            }

            var computeShader = ResolveNativeVideoNv12ComputeShader();
            if (computeShader == null)
            {
                if (!_nativeVideoComputeWarningIssued)
                {
                    _nativeVideoComputeWarningIssued = true;
                    Debug.LogWarning(
                        "[MediaPlayer] unity_compute requested but NV12ToRGBA compute shader is unavailable");
                }

                return false;
            }

            NativeVideoPlaneTexturesInfo texturesInfo;
            if (!TryAcquireNativeVideoSourcePlaneTexturesInfo(out texturesInfo))
            {
                return false;
            }

            if (!CanUseUnityComputePlaneTextures(texturesInfo))
            {
                if (!_nativeVideoComputeWarningIssued)
                {
                    _nativeVideoComputeWarningIssued = true;
                    Debug.LogWarning(
                        "[MediaPlayer] unity_compute requested but source plane textures are not usable"
                        + " surface=" + texturesInfo.SurfaceKind
                        + " source_pixel_format=" + texturesInfo.SourcePixelFormat
                        + " y_handle=0x" + texturesInfo.YNativeHandle.ToInt64().ToString("X")
                        + " uv_handle=0x" + texturesInfo.UVNativeHandle.ToInt64().ToString("X"));
                }

                return false;
            }

            try
            {
                EnsureNativeVideoComputeResources(
                    computeShader,
                    texturesInfo.YWidth,
                    texturesInfo.YHeight);
                EnsureNativePlaneTextureBindings(texturesInfo);
                computeShader.SetTexture(_nativeVideoComputeKernel, "YPlane", _nativePlaneTextureY);
                computeShader.SetTexture(_nativeVideoComputeKernel, "UVPlane", _nativePlaneTextureUV);
                var threadGroupsX = Mathf.CeilToInt(texturesInfo.YWidth / 16.0f);
                var threadGroupsY = Mathf.CeilToInt(texturesInfo.YHeight / 16.0f);
                computeShader.Dispatch(_nativeVideoComputeKernel, threadGroupsX, threadGroupsY, 1);
                ApplyPresentedTexture(_nativeVideoComputeOutput);

                if (!_nativeVideoComputePathActive)
                {
                    Debug.Log(
                        "[MediaPlayer] unity_compute_bound startup_seconds="
                        + StartupElapsedSeconds.ToString("F3")
                        + " frame_index=" + frameInfo.FrameIndex
                        + " source_pixel_format=" + texturesInfo.SourcePixelFormat
                        + " y_handle=0x" + texturesInfo.YNativeHandle.ToInt64().ToString("X")
                        + " uv_handle=0x" + texturesInfo.UVNativeHandle.ToInt64().ToString("X"));
                }

                _nativeVideoComputePathActive = true;
                _nativeVideoDirectShaderPathActive = false;
                _nativePlaneTexturesBound = true;
                _nativePlaneTextureBindCount += 1;
                _nativeVideoPresentationPath = NativeVideoPresentationPathKind.Compute;
                _nativeTextureBound = false;
                return true;
            }
            catch (Exception exception)
            {
                if (!_nativeVideoComputeWarningIssued)
                {
                    _nativeVideoComputeWarningIssued = true;
                    Debug.LogWarning(
                        "[MediaPlayer] unity_compute failed source_surface="
                        + texturesInfo.SurfaceKind
                        + " source_pixel_format=" + texturesInfo.SourcePixelFormat
                        + " error=" + exception.Message);
                }

                _nativeVideoComputePathActive = false;
                _nativeVideoPresentationPath = NativeVideoPresentationPathKind.None;
                _nativePlaneTexturesBound = false;
                ApplyPresentedTexture(_targetTexture);
                return false;
            }
#else
            return false;
#endif
        }

        private ComputeShader ResolveNativeVideoNv12ComputeShader()
        {
            if (NativeVideoNv12ComputeShader != null)
            {
                return NativeVideoNv12ComputeShader;
            }

            NativeVideoNv12ComputeShader = Resources.Load<ComputeShader>("NV12ToRGBA");
            return NativeVideoNv12ComputeShader;
        }

        private Shader ResolveNativeVideoNv12DirectShader()
        {
            if (NativeVideoNv12DirectShader != null)
            {
                return NativeVideoNv12DirectShader;
            }

            NativeVideoNv12DirectShader = Resources.Load<Shader>("NV12Direct");
            return NativeVideoNv12DirectShader;
        }

        private bool EnsureNativeVideoDirectShaderMaterial(Shader shader)
        {
            if (TargetMaterial == null || shader == null)
            {
                return false;
            }

            if (!_capturedTargetMaterialShader)
            {
                _originalTargetMaterialShader = TargetMaterial.shader;
                _capturedTargetMaterialShader = true;
            }

            if (!ReferenceEquals(TargetMaterial.shader, shader))
            {
                TargetMaterial.shader = shader;
            }

            return true;
        }

        private void BindNativeVideoPlaneTexturesToMaterial()
        {
            if (TargetMaterial == null)
            {
                return;
            }

            TargetMaterial.SetFloat(UseNativeVideoPlaneTexturesPropertyId, 1.0f);
            TargetMaterial.SetFloat(FlipVerticalPropertyId, 1.0f);
            TargetMaterial.SetTexture(YPlanePropertyId, _nativePlaneTextureY);
            TargetMaterial.SetTexture(UVPlanePropertyId, _nativePlaneTextureUV);
        }

        private void DisableNativeVideoPlaneTextureMode()
        {
            if (TargetMaterial == null)
            {
                return;
            }

            TargetMaterial.SetFloat(UseNativeVideoPlaneTexturesPropertyId, 0.0f);
            TargetMaterial.SetTexture(YPlanePropertyId, null);
            TargetMaterial.SetTexture(UVPlanePropertyId, null);
        }

        private void RestoreTargetMaterialShader()
        {
            if (TargetMaterial != null
                && _capturedTargetMaterialShader
                && _originalTargetMaterialShader != null
                && !ReferenceEquals(TargetMaterial.shader, _originalTargetMaterialShader))
            {
                TargetMaterial.shader = _originalTargetMaterialShader;
            }

            _capturedTargetMaterialShader = false;
            _originalTargetMaterialShader = null;
        }

        private void EnsureNativeVideoComputeResources(
            ComputeShader computeShader,
            int width,
            int height)
        {
            if (_nativeVideoComputeKernel < 0)
            {
                _nativeVideoComputeKernel = computeShader.FindKernel("CSMain");
            }

            if (_nativeVideoComputeOutput == null
                || _nativeVideoComputeOutput.width != width
                || _nativeVideoComputeOutput.height != height)
            {
                ReleaseNativeVideoComputeOutput();
                _nativeVideoComputeOutput = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
                {
                    enableRandomWrite = true,
                    filterMode = FilterMode.Bilinear,
                    name = Uri + "#NativeVideoCompute"
                };
                _nativeVideoComputeOutput.Create();
            }

            computeShader.SetInt("Width", width);
            computeShader.SetInt("Height", height);
            computeShader.SetTexture(_nativeVideoComputeKernel, "Result", _nativeVideoComputeOutput);
        }

        private void EnsureNativePlaneTextureBindings(NativeVideoPlaneTexturesInfo texturesInfo)
        {
            var requiresYRecreate = _nativePlaneTextureY == null
                || _nativePlaneTextureY.width != texturesInfo.YWidth
                || _nativePlaneTextureY.height != texturesInfo.YHeight;
            if (requiresYRecreate)
            {
                ReleaseNativePlaneTexture(ref _nativePlaneTextureY);
                _nativePlaneTextureY = Texture2D.CreateExternalTexture(
                    texturesInfo.YWidth,
                    texturesInfo.YHeight,
                    TextureFormat.R8,
                    false,
                    false,
                    texturesInfo.YNativeHandle);
                _nativePlaneTextureY.filterMode = FilterMode.Bilinear;
                _nativePlaneTextureY.name = Uri + "#NativeVideoY";
                _lastBoundNativePlaneYHandle = texturesInfo.YNativeHandle;
            }
            else if (_lastBoundNativePlaneYHandle != texturesInfo.YNativeHandle)
            {
                _nativePlaneTextureY.UpdateExternalTexture(texturesInfo.YNativeHandle);
                _lastBoundNativePlaneYHandle = texturesInfo.YNativeHandle;
            }

            var requiresUVRecreate = _nativePlaneTextureUV == null
                || _nativePlaneTextureUV.width != texturesInfo.UVWidth
                || _nativePlaneTextureUV.height != texturesInfo.UVHeight;
            if (requiresUVRecreate)
            {
                ReleaseNativePlaneTexture(ref _nativePlaneTextureUV);
                _nativePlaneTextureUV = Texture2D.CreateExternalTexture(
                    texturesInfo.UVWidth,
                    texturesInfo.UVHeight,
                    TextureFormat.RG16,
                    false,
                    false,
                    texturesInfo.UVNativeHandle);
                _nativePlaneTextureUV.filterMode = FilterMode.Bilinear;
                _nativePlaneTextureUV.name = Uri + "#NativeVideoUV";
                _lastBoundNativePlaneUVHandle = texturesInfo.UVNativeHandle;
            }
            else if (_lastBoundNativePlaneUVHandle != texturesInfo.UVNativeHandle)
            {
                _nativePlaneTextureUV.UpdateExternalTexture(texturesInfo.UVNativeHandle);
                _lastBoundNativePlaneUVHandle = texturesInfo.UVNativeHandle;
            }
        }

        private void ReleaseBoundNativeTexture()
        {
            if (_boundNativeTexture == null)
            {
                return;
            }

            if (TargetMaterial != null && ReferenceEquals(TargetMaterial.mainTexture, _boundNativeTexture))
            {
                TargetMaterial.mainTexture = null;
            }

            Destroy(_boundNativeTexture);
            _boundNativeTexture = null;
            _lastBoundNativeHandle = IntPtr.Zero;
        }

        private void ReleaseBoundNativePlaneTextures()
        {
            ReleaseNativePlaneTexture(ref _nativePlaneTextureY);
            ReleaseNativePlaneTexture(ref _nativePlaneTextureUV);
            _lastBoundNativePlaneYHandle = IntPtr.Zero;
            _lastBoundNativePlaneUVHandle = IntPtr.Zero;
        }

        private void ReleaseNativePlaneTexture(ref Texture2D texture)
        {
            if (texture == null)
            {
                return;
            }

            Destroy(texture);
            texture = null;
        }

        private void ReleaseNativeVideoComputeOutput()
        {
            if (_nativeVideoComputeOutput == null)
            {
                return;
            }

            _nativeVideoComputeOutput.Release();
            Destroy(_nativeVideoComputeOutput);
            _nativeVideoComputeOutput = null;
        }

        private static bool HasNativeVideoFrameFlag(uint flags, uint expectedFlag)
        {
            return (flags & expectedFlag) == expectedFlag;
        }

        private void ResetNativeVideoTelemetry()
        {
            _playRequestedRealtimeAt = -1f;
            _firstNativeVideoFrameRealtimeAt = -1f;
            _lastNativeVideoFrameInfo = default(NativeVideoFrameInfo);
            _hasLastNativeVideoFrameInfo = false;
            _lastAcquiredNativeFrameIndex = -1;
            _nativeVideoFrameAcquireCount = 0;
            _nativeVideoFrameReleaseCount = 0;
            _nativeVideoBindingWarningIssued = false;
            _nativeVideoDirectShaderWarningIssued = false;
            _nativeVideoComputeWarningIssued = false;
            _nativeTextureBound = false;
            _nativePlaneTexturesBound = false;
            _nativeVideoDirectShaderPathActive = false;
            _nativeVideoComputePathActive = false;
            _nativeVideoSourceSurfaceZeroCopyActive = false;
            _nativeVideoSourcePlaneTexturesZeroCopyActive = false;
            _nativeTextureBindCount = 0;
            _nativePlaneTextureBindCount = 0;
            _nativeVideoDirectShaderBindCount = 0;
            _nativeVideoPresentationPath = NativeVideoPresentationPathKind.None;
            _nativeVideoActivationDecision = NativeVideoActivationDecisionKind.NotRequested;
            _lastBoundNativeHandle = IntPtr.Zero;
            _lastBoundNativePlaneYHandle = IntPtr.Zero;
            _lastBoundNativePlaneUVHandle = IntPtr.Zero;
        }

        private void ReleaseNativePlayer()
        {
            if (!ValidatePlayerId(_id))
            {
                return;
            }

            NativeInitializer.UnregisterPlayerRenderEvent(_id);
            var result = ReleasePlayer(_id);
            _id = InvalidPlayerId;
            _playRequested = false;
            _resumeAfterPause = false;
            _actualBackendKind = MediaBackendKind.Auto;
            _nativeVideoPathActive = false;
            _nativeVideoInteropCaps = default(MediaNativeInteropCommon.NativeVideoInteropCapsView);
            ResetNativeVideoTelemetry();

            if (result < 0)
            {
                throw new Exception($"Failed to release player with error: {result}");
            }
        }

        private void ReleaseNativePlayerSilently()
        {
            if (!ValidatePlayerId(_id))
            {
                return;
            }

            try
            {
                NativeInitializer.UnregisterPlayerRenderEvent(_id);
                ReleasePlayer(_id);
            }
            catch
            {
            }

            _id = InvalidPlayerId;
            _playRequested = false;
            _resumeAfterPause = false;
            _actualBackendKind = MediaBackendKind.Auto;
            _nativeVideoPathActive = false;
            _nativeVideoInteropCaps = default(MediaNativeInteropCommon.NativeVideoInteropCapsView);
            ResetNativeVideoTelemetry();
        }

        private void ReleaseManagedResources()
        {
            if (TargetMaterial != null
                && (ReferenceEquals(TargetMaterial.mainTexture, _targetTexture)
                    || ReferenceEquals(TargetMaterial.mainTexture, _boundNativeTexture)
                    || ReferenceEquals(TargetMaterial.mainTexture, _nativeVideoComputeOutput)))
            {
                TargetMaterial.mainTexture = null;
            }

            DisableNativeVideoPlaneTextureMode();
            ReleaseBoundNativeTexture();
            ReleaseBoundNativePlaneTextures();
            ReleaseNativeVideoComputeOutput();
            RestoreTargetMaterialShader();
            _nativeVideoComputeKernel = -1;

            if (_targetTexture != null)
            {
                Destroy(_targetTexture);
                _targetTexture = null;
            }
        }

        private Texture CreateNativeVideoTargetTexture()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            if (PreferNativeVideo)
            {
                var descriptor = new RenderTextureDescriptor(Width, Height)
                {
                    depthBufferBits = 0,
                    msaaSamples = 1,
                    volumeDepth = 1,
                    mipCount = 1,
                    graphicsFormat = GraphicsFormat.R8G8B8A8_UNorm,
                    sRGB = false,
                    useMipMap = false,
                    autoGenerateMips = false,
                    enableRandomWrite = false,
                    dimension = UnityEngine.Rendering.TextureDimension.Tex2D
                };
                var target = new RenderTexture(descriptor)
                {
                    filterMode = FilterMode.Bilinear,
                    name = Uri + "#NativeVideoTarget"
                };
                target.Create();
                return target;
            }
#endif

            return new Texture2D(Width, Height, TextureFormat.ARGB32, false)
            {
                filterMode = FilterMode.Bilinear,
                name = Uri
            };
        }

        private uint ResolveNativeVideoTargetExtraFlags()
        {
            var flags = MediaNativeInteropCommon.NativeVideoTargetFlagNone;
            if (!PreferNativeVideoRenderEventPass
                && (PreferNativeVideoUnityDirectShader || PreferNativeVideoUnityCompute))
            {
                flags |= MediaNativeInteropCommon.NativeVideoTargetFlagDisableDirectTargetPresent;
            }

            return flags;
        }

        private static string DescribeTextureGraphicsFormat(Texture texture)
        {
            if (texture is RenderTexture renderTexture)
            {
                return renderTexture.graphicsFormat.ToString();
            }

            return "n/a";
        }

        private static string DescribeTextureMsaa(Texture texture)
        {
            if (texture is RenderTexture renderTexture)
            {
                return renderTexture.antiAliasing.ToString();
            }

            return "n/a";
        }

        private static string DescribeTextureUseMipMap(Texture texture)
        {
            if (texture is RenderTexture renderTexture)
            {
                return renderTexture.useMipMap.ToString();
            }

            return "n/a";
        }

        private static string DescribeTextureRandomWrite(Texture texture)
        {
            if (texture is RenderTexture renderTexture)
            {
                return renderTexture.enableRandomWrite.ToString();
            }

            return "n/a";
        }

        private MediaBackendKind ReadActualBackendKind()
        {
            if (!ValidatePlayerId(_id))
            {
                return MediaBackendKind.Auto;
            }

            try
            {
                return MediaNativeInteropCommon.NormalizeBackendKind(
                    GetPlayerBackendKind(_id),
                    PreferredBackend);
            }
            catch (EntryPointNotFoundException)
            {
                return PreferredBackend;
            }
        }

        private string ReadBackendRuntimeDiagnostic(string uri)
        {
            return MediaNativeInteropCommon.ReadBackendRuntimeDiagnostic(
                GetBackendRuntimeDiagnostic,
                PreferredBackend,
                uri,
                false);
        }

        private void TryCreateNativeVideoPlayer(
            string uri,
            ref MediaNativeInteropCommon.RustAVNativeVideoTarget nativeTarget,
            ref MediaNativeInteropCommon.RustAVPlayerOpenOptions openOptions)
        {
            Debug.Log(
                "[MediaPlayer] native_video_create_begin"
                + " uri=" + uri
                + " backend=" + PreferredBackend
                + " strict_backend=" + StrictBackend
                + " texture_type=" + (_targetTexture != null ? _targetTexture.GetType().Name : "null")
                + " target_handle=0x" + nativeTarget.TargetHandle.ToString("X")
                + " size=" + nativeTarget.Width + "x" + nativeTarget.Height
                + " platform=" + nativeTarget.PlatformKind
                + " surface=" + nativeTarget.SurfaceKind
                + " pixel_format=" + nativeTarget.PixelFormat
                + " flags=0x" + nativeTarget.Flags.ToString("X"));
            if (nativeTarget.TargetHandle == 0
                || nativeTarget.PlatformKind == (int)NativeVideoPlatformKind.Unknown
                || nativeTarget.SurfaceKind == (int)NativeVideoSurfaceKind.Unknown)
            {
                _nativeVideoActivationDecision = NativeVideoActivationDecisionKind.InvalidTarget;
                Debug.LogWarning("[MediaPlayer] native_video_create_skipped reason=invalid-target");
                return;
            }

            MediaNativeInteropCommon.NativeVideoInteropCapsView capsView;
            if (!MediaNativeInteropCommon.TryReadNativeVideoInteropCaps(
                GetNativeVideoInteropCaps,
                PreferredBackend,
                uri,
                ref nativeTarget,
                out capsView))
            {
                _nativeVideoActivationDecision = NativeVideoActivationDecisionKind.CapsUnavailable;
                Debug.LogWarning("[MediaPlayer] native_video_caps_unavailable");
                return;
            }

            _nativeVideoInteropCaps = capsView;
            Debug.Log(
                "[MediaPlayer] native_video_caps"
                + " supported=" + capsView.Supported
                + " hardware_decode_supported=" + capsView.HardwareDecodeSupported
                + " zero_copy_supported=" + capsView.ZeroCopySupported
                + " acquire_release_supported=" + capsView.AcquireReleaseSupported
                + " source_surface_zero_copy_supported=" + capsView.SourceSurfaceZeroCopySupported
                + " presented_direct_bindable_supported=" + capsView.PresentedFrameDirectBindable
                + " presented_strict_zero_copy_supported=" + capsView.PresentedFrameStrictZeroCopySupported
                + " source_plane_textures_supported=" + capsView.SourcePlaneTexturesSupported
                + " source_plane_views_supported=" + capsView.SourcePlaneViewsSupported
                + " flags=0x" + capsView.Flags.ToString("X"));
            if (!capsView.Supported)
            {
                _nativeVideoActivationDecision = NativeVideoActivationDecisionKind.UnsupportedTarget;
                Debug.LogWarning("[MediaPlayer] native_video_create_skipped reason=unsupported-target");
                return;
            }

            if (RequireNativeVideoHardwareDecode && !capsView.HardwareDecodeSupported)
            {
                _nativeVideoActivationDecision = NativeVideoActivationDecisionKind.HardwareDecodeUnavailable;
                Debug.LogWarning("[MediaPlayer] native_video_create_skipped reason=hardware-decode-unavailable");
                return;
            }

            if (RequireNativeVideoZeroCopy && !capsView.ZeroCopySupported)
            {
                _nativeVideoActivationDecision = NativeVideoActivationDecisionKind.StrictZeroCopyUnavailable;
                Debug.LogWarning("[MediaPlayer] native_video_create_skipped reason=strict-zero-copy-unavailable");
                return;
            }

            if (!capsView.AcquireReleaseSupported)
            {
                _nativeVideoActivationDecision = NativeVideoActivationDecisionKind.AcquireReleaseUnavailable;
                Debug.LogWarning("[MediaPlayer] native_video_create_skipped reason=acquire-release-unavailable");
                return;
            }

            try
            {
                _id = GetNativeVideoPlayerEx(uri, ref nativeTarget, ref openOptions);
            }
            catch (EntryPointNotFoundException)
            {
                try
                {
                    _id = GetNativeVideoPlayer(uri, ref nativeTarget);
                }
                catch (EntryPointNotFoundException)
                {
                    _id = InvalidPlayerId;
                }
            }

            if (ValidatePlayerId(_id))
            {
                _nativeVideoPathActive = true;
                _nativeVideoActivationDecision = NativeVideoActivationDecisionKind.Active;
                Debug.Log(
                    "[MediaPlayer] native_video_create_result"
                    + " player_id=" + _id
                    + " native_video_active=" + _nativeVideoPathActive);
            }
            else
            {
                _nativeVideoActivationDecision = NativeVideoActivationDecisionKind.CreateFailed;
                Debug.LogWarning(
                    "[MediaPlayer] native_video_create_failed"
                    + " player_id=" + _id
                    + " target_handle=0x" + nativeTarget.TargetHandle.ToString("X")
                    + " texture_type=" + (_targetTexture != null ? _targetTexture.GetType().Name : "null"));
            }
        }
    }
}
