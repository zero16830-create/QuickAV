using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace UnityAV
{
    public enum MediaBackendKind
    {
        Auto = 0,
        Ffmpeg = 1,
        Gstreamer = 2,
    }

    public enum MediaSourceConnectionState
    {
        Unknown = -1,
        Disconnected = 0,
        Connecting = 1,
        Connected = 2,
        Reconnecting = 3,
        Checking = 4,
    }

    public enum NativeVideoPlatformKind
    {
        Unknown = 0,
        Windows = 1,
        Ios = 2,
        Android = 3,
    }

    public enum NativeVideoSurfaceKind
    {
        Unknown = 0,
        D3D11Texture2D = 1,
        MetalTexture = 2,
        CVPixelBuffer = 3,
        AndroidSurfaceTexture = 4,
        AndroidHardwareBuffer = 5,
    }

    public enum NativeVideoPathKind
    {
        Unknown = 0,
        NativeBridgeZeroCopy = 1,
        NativeBridgePlanes = 2,
        WgpuRenderCore = 3,
        CpuFallback = 4,
    }

    public enum NativeVideoTargetProviderKind
    {
        Auto = 0,
        UnityExternalTexture = 1,
        IosMetalTexture = 2,
        IosCVPixelBuffer = 3,
        AndroidSurfaceTexture = 4,
        AndroidHardwareBuffer = 5,
    }

    public enum NativeVideoPixelFormatKind
    {
        Unknown = -1,
        Yuv420p = 0,
        Rgba32 = 1,
        Nv12 = 2,
        P010 = 3,
    }

    public enum NativeVideoColorRangeKind
    {
        Unknown = -1,
        Limited = 0,
        Full = 1,
    }

    public enum NativeVideoColorMatrixKind
    {
        Unknown = -1,
        Bt601 = 0,
        Bt709 = 1,
        Bt2020Ncl = 2,
        Bt2020Cl = 3,
        Smpte240M = 4,
        Rgb = 5,
    }

    public enum NativeVideoColorPrimariesKind
    {
        Unknown = -1,
        Bt601 = 0,
        Bt709 = 1,
        Bt2020 = 2,
        DciP3 = 3,
    }

    public enum NativeVideoTransferCharacteristicKind
    {
        Unknown = -1,
        Bt1886 = 0,
        Srgb = 1,
        Linear = 2,
        Smpte240M = 3,
        Pq = 4,
        Hlg = 5,
    }

    public enum NativeVideoDynamicRangeKind
    {
        Unknown = 0,
        Sdr = 1,
        Hdr10 = 2,
        Hlg = 3,
        DolbyVision = 4,
    }

    public enum NativeVideoPlaneTextureFormatKind
    {
        Unknown = 0,
        R8Unorm = 1,
        Rg8Unorm = 2,
        R16Unorm = 3,
        Rg16Unorm = 4,
    }

    public enum NativeVideoPlaneResourceKindKind
    {
        Unknown = 0,
        D3D11Texture2D = 1,
        D3D11ShaderResourceView = 2,
    }

    internal static class MediaNativeInteropCommon
    {
        internal const uint RustAVPlayerOpenOptionsVersion = 1u;
        internal const uint RustAVPlayerSessionOpenOptionsVersion = 1u;
        internal const uint RustAVPlayerHealthSnapshotV2Version = 2u;
        internal const uint RustAVVideoFrameContractVersion = 1u;
        internal const uint RustAVPlaybackTimingContractVersion = 2u;
        internal const uint RustAVAvSyncContractVersion = 1u;
        internal const uint RustAVAudioOutputPolicyVersion = 2u;
        internal const uint RustAVSourceTimelineContractVersion = 1u;
        internal const uint RustAVPlayerSessionContractVersion = 2u;
        internal const uint RustAVAvSyncEnterpriseMetricsVersion = 2u;
        internal const uint RustAVPassiveAvSyncSnapshotVersion = 1u;
        internal const uint RustAVVideoColorInfoVersion = 1u;
        internal const uint RustAVNativeVideoTargetVersion = 1u;
        internal const uint RustAVNativeVideoInteropCapsVersion = 1u;
        internal const uint RustAVNativeVideoBridgeDescriptorVersion = 1u;
        internal const uint RustAVNativeVideoPathSelectionVersion = 1u;
        internal const uint RustAVNativeVideoFrameVersion = 1u;
        internal const uint RustAVNativeVideoPlaneTexturesVersion = 1u;
        internal const uint RustAVNativeVideoPlaneViewsVersion = 1u;
        internal const uint RustAVWgpuRenderDescriptorVersion = 1u;
        internal const uint RustAVWgpuRenderStateViewVersion = 1u;
        internal const int BackendDiagnosticBufferLength = 512;
        internal const float MinimumValidationPlaybackAdvanceSeconds = 1.0f;
        internal const uint NativeVideoTargetFlagNone = 0u;
        internal const uint NativeVideoTargetFlagExternalTexture = 1u << 0;
        internal const uint NativeVideoTargetFlagUnityOwnedTexture = 1u << 1;
        internal const uint NativeVideoTargetFlagDisableDirectTargetPresent = 1u << 2;
        internal const uint NativeVideoCapFlagTargetBindingSupported = 1u << 0;
        internal const uint NativeVideoCapFlagFrameAcquireSupported = 1u << 1;
        internal const uint NativeVideoCapFlagFrameReleaseSupported = 1u << 2;
        internal const uint NativeVideoCapFlagFallbackCopyPath = 1u << 3;
        internal const uint NativeVideoCapFlagExternalTextureTarget = 1u << 4;
        internal const uint NativeVideoCapFlagSourceSurfaceZeroCopy = 1u << 5;
        internal const uint NativeVideoCapFlagPresentedFrameDirectBindable = 1u << 6;
        internal const uint NativeVideoCapFlagPresentedFrameStrictZeroCopy = 1u << 7;
        internal const uint NativeVideoCapFlagSourcePlaneTexturesSupported = 1u << 8;
        internal const uint NativeVideoCapFlagSourcePlaneViewsSupported = 1u << 9;
        internal const uint NativeVideoCapFlagContractTargetSupported = 1u << 10;
        internal const uint NativeVideoCapFlagRuntimeBridgePending = 1u << 11;
        internal const uint NativeVideoFrameFlagNone = 0u;
        internal const uint NativeVideoFrameFlagHasFrame = 1u << 0;
        internal const uint NativeVideoFrameFlagHardwareDecode = 1u << 1;
        internal const uint NativeVideoFrameFlagZeroCopy = 1u << 2;
        internal const uint NativeVideoFrameFlagCpuFallback = 1u << 3;
        internal const uint PlayerSessionOpenFlagNone = 0u;
        internal const uint PlayerSessionOpenFlagAudioExport = 1u << 0;
        internal const uint PlayerSessionOpenFlagWgpuUploadOnly = 1u << 1;

        internal enum NativeVideoPixelFormat
        {
            Unknown = -1,
            Yuv420p = 0,
            Rgba32 = 1,
            Nv12 = 2,
            P010 = 3,
        }

        internal enum NativeVideoBridgeState
        {
            Unsupported = 0,
            ContractPending = 1,
            RuntimeReady = 2,
        }

        internal enum NativeVideoBridgeRuntimeKind
        {
            None = 0,
            WindowsD3d11TextureInterop = 1,
            IosMetalTexture = 2,
            IosCvPixelBuffer = 3,
            AndroidSurfaceTexture = 4,
            AndroidHardwareBuffer = 5,
        }

        internal enum NativeVideoPath
        {
            Unknown = 0,
            NativeBridgeZeroCopy = 1,
            NativeBridgePlanes = 2,
            WgpuRenderCore = 3,
            CpuFallback = 4,
        }

        internal enum WgpuRenderPath
        {
            CpuPlanar = 0,
            NativeSurfaceBridge = 1,
        }

        internal enum WgpuExternalTextureFormat
        {
            None = 0,
            Rgba = 1,
            Yu12 = 2,
            Nv12 = 3,
        }

        internal enum WgpuRenderError
        {
            None = 0,
            NativeSurfaceImportPending = 1,
            UnsupportedSourceFormat = 2,
            InvalidPlaneCount = 3,
            InvalidPlaneData = 4,
            UnsupportedTextureFormat = 5,
            UnsupportedOutputFormat = 6,
            AdapterUnavailable = 7,
            RequestDevice = 8,
            PollFailed = 9,
            MapFailed = 10,
            ReadbackFailed = 11,
            Other = 12,
        }

        internal enum NativeVideoColorRange
        {
            Unknown = -1,
            Limited = 0,
            Full = 1,
        }

        internal enum NativeVideoColorMatrix
        {
            Unknown = -1,
            Bt601 = 0,
            Bt709 = 1,
            Bt2020Ncl = 2,
            Bt2020Cl = 3,
            Smpte240M = 4,
            Rgb = 5,
        }

        internal enum NativeVideoColorPrimaries
        {
            Unknown = -1,
            Bt601 = 0,
            Bt709 = 1,
            Bt2020 = 2,
            DciP3 = 3,
        }

        internal enum NativeVideoTransferCharacteristic
        {
            Unknown = -1,
            Bt1886 = 0,
            Srgb = 1,
            Linear = 2,
            Smpte240M = 3,
            Pq = 4,
            Hlg = 5,
        }

        internal enum NativeVideoDynamicRange
        {
            Unknown = 0,
            Sdr = 1,
            Hdr10 = 2,
            Hlg = 3,
            DolbyVision = 4,
        }

        internal enum NativeVideoPlaneTextureFormat
        {
            Unknown = 0,
            R8Unorm = 1,
            Rg8Unorm = 2,
            R16Unorm = 3,
            Rg16Unorm = 4,
        }

        internal enum VideoFrameMemoryKind
        {
            Unknown = 0,
            CpuPlanar = 1,
            NativeSurface = 2,
        }

        internal enum AvSyncMasterClockKind
        {
            Unknown = 0,
            Audio = 1,
            Video = 2,
            External = 3,
        }

        internal enum NativeVideoPlaneResourceKind
        {
            Unknown = 0,
            D3D11Texture2D = 1,
            D3D11ShaderResourceView = 2,
        }

        internal enum RustAVPlayerSessionOutputKind
        {
            Unknown = 0,
            Texture = 1,
            PullRgba = 2,
            WgpuRgba = 3,
            NativeVideo = 4,
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct RustAVPlayerOpenOptions
        {
            public uint StructSize;
            public uint StructVersion;
            public int BackendKind;
            public int StrictBackend;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct RustAVPlayerSessionOpenOptions
        {
            public uint StructSize;
            public uint StructVersion;
            public int OutputKind;
            public int BackendKind;
            public int StrictBackend;
            public int TargetWidth;
            public int TargetHeight;
            public uint OutputFlags;
            public IntPtr TargetTexture;
            public IntPtr NativeVideoTarget;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct RustAVNativeVideoTarget
        {
            public uint StructSize;
            public uint StructVersion;
            public int PlatformKind;
            public int SurfaceKind;
            public ulong TargetHandle;
            public ulong AuxiliaryHandle;
            public int Width;
            public int Height;
            public int PixelFormat;
            public uint Flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct RustAVVideoColorInfo
        {
            public uint StructSize;
            public uint StructVersion;
            public int Range;
            public int Matrix;
            public int Primaries;
            public int Transfer;
            public int BitDepth;
            public int DynamicRange;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct RustAVNativeVideoInteropCaps
        {
            public uint StructSize;
            public uint StructVersion;
            public int BackendKind;
            public int PlatformKind;
            public int SurfaceKind;
            public int Supported;
            public int HardwareDecodeSupported;
            public int ZeroCopySupported;
            public int AcquireReleaseSupported;
            public uint Flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct RustAVNativeVideoBridgeDescriptor
        {
            public uint StructSize;
            public uint StructVersion;
            public int BackendKind;
            public int TargetPlatformKind;
            public int TargetSurfaceKind;
            public int TargetWidth;
            public int TargetHeight;
            public int TargetPixelFormat;
            public uint TargetFlags;
            public int PlatformKind;
            public int SurfaceKind;
            public int State;
            public int RuntimeKind;
            public int Supported;
            public int HardwareDecodeSupported;
            public int ZeroCopySupported;
            public int AcquireReleaseSupported;
            public uint CapsFlags;
            public int TargetValid;
            public int RequestedExternalTextureTarget;
            public int DirectTargetPresentAllowed;
            public int TargetBindingSupported;
            public int ExternalTextureTargetSupported;
            public int FrameAcquireSupported;
            public int FrameReleaseSupported;
            public int FallbackCopyPath;
            public int SourceSurfaceZeroCopy;
            public int PresentedFrameDirectBindable;
            public int PresentedFrameStrictZeroCopy;
            public int SourcePlaneTexturesSupported;
            public int SourcePlaneViewsSupported;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct RustAVNativeVideoPathSelection
        {
            public uint StructSize;
            public uint StructVersion;
            public int Kind;
            public int HasSourceFrame;
            public int HasPresentedFrame;
            public int SourceMemoryKind;
            public int PresentedMemoryKind;
            public int BridgeState;
            public int SourceSurfaceZeroCopy;
            public int SourcePlaneTexturesSupported;
            public int TargetZeroCopy;
            public int CpuFallback;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct RustAVWgpuRenderDescriptor
        {
            public uint StructSize;
            public uint StructVersion;
            public int OutputWidth;
            public int OutputHeight;
            public int RuntimeReady;
            public int SupportsYuv420p;
            public int SupportsNv12;
            public int SupportsP010;
            public int SupportsRgba32;
            public int SupportsExternalTextureRgba;
            public int SupportsExternalTextureYu12;
            public int ReadbackExportSupported;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct RustAVWgpuRenderStateView
        {
            public uint StructSize;
            public uint StructVersion;
            public int HasSourceContract;
            public int HasPresentedContract;
            public int SourceMemoryKind;
            public int PresentedMemoryKind;
            public int SourcePixelFormat;
            public int PresentedPixelFormat;
            public int RenderPath;
            public int ExternalTextureFormat;
            public int HasRenderedFrame;
            public long RenderedFrameIndex;
            public double RenderedTimeSec;
            public int HasRenderError;
            public int RenderErrorKind;
            public int UploadPlaneCount;
            public int SourceZeroCopy;
            public int CpuFallback;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct RustAVNativeVideoFrame
        {
            public uint StructSize;
            public uint StructVersion;
            public int SurfaceKind;
            public ulong NativeHandle;
            public ulong AuxiliaryHandle;
            public int Width;
            public int Height;
            public int PixelFormat;
            public double TimeSec;
            public long FrameIndex;
            public uint Flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct RustAVNativeVideoPlaneTextures
        {
            public uint StructSize;
            public uint StructVersion;
            public int SurfaceKind;
            public int SourcePixelFormat;
            public ulong YNativeHandle;
            public ulong YAuxiliaryHandle;
            public int YWidth;
            public int YHeight;
            public int YTextureFormat;
            public ulong UVNativeHandle;
            public ulong UVAuxiliaryHandle;
            public int UVWidth;
            public int UVHeight;
            public int UVTextureFormat;
            public double TimeSec;
            public long FrameIndex;
            public uint Flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct RustAVNativeVideoPlaneViews
        {
            public uint StructSize;
            public uint StructVersion;
            public int SurfaceKind;
            public int SourcePixelFormat;
            public ulong YNativeHandle;
            public ulong YAuxiliaryHandle;
            public int YWidth;
            public int YHeight;
            public int YTextureFormat;
            public int YResourceKind;
            public ulong UVNativeHandle;
            public ulong UVAuxiliaryHandle;
            public int UVWidth;
            public int UVHeight;
            public int UVTextureFormat;
            public int UVResourceKind;
            public double TimeSec;
            public long FrameIndex;
            public uint Flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct RustAVPlayerHealthSnapshotV2
        {
            public uint StructSize;
            public uint StructVersion;
            public int State;
            public int RuntimeState;
            public int PlaybackIntent;
            public int StopReason;
            public int SourceConnectionState;
            public int IsConnected;
            public int IsPlaying;
            public int IsRealtime;
            public int CanSeek;
            public int IsLooping;
            public int StreamCount;
            public int VideoDecoderCount;
            public int HasAudioDecoder;
            public double DurationSec;
            public double CurrentTimeSec;
            public double AudioTimeSec;
            public double AudioPresentedTimeSec;
            public double AudioSinkDelaySec;
            public double ExternalTimeSec;
            public double VideoSyncCompensationSec;
            public long ConnectAttemptCount;
            public long VideoDecoderRecreateCount;
            public long AudioDecoderRecreateCount;
            public long VideoFrameDropCount;
            public long AudioFrameDropCount;
            public long SourcePacketCount;
            public long SourceTimeoutCount;
            public long SourceReconnectCount;
            public int SourceIsCheckingConnection;
            public double SourceLastActivityAgeSec;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct RustAVVideoFrameContract
        {
            public uint StructSize;
            public uint StructVersion;
            public int MemoryKind;
            public int SurfaceKind;
            public int PixelFormat;
            public int Width;
            public int Height;
            public int PlaneCount;
            public int HardwareDecode;
            public int ZeroCopy;
            public int CpuFallback;
            public int NativeHandlePresent;
            public int AuxiliaryHandlePresent;
            public int ColorRange;
            public int ColorMatrix;
            public int ColorPrimaries;
            public int ColorTransfer;
            public int ColorBitDepth;
            public int ColorDynamicRange;
            public int HasColorDynamicRangeOverride;
            public int ColorDynamicRangeOverride;
            public double TimeSec;
            public int HasFrameIndex;
            public long FrameIndex;
            public int HasNominalFps;
            public double NominalFps;
            public int HasTimelineOriginSec;
            public double TimelineOriginSec;
            public ulong SeekEpoch;
            public int Discontinuity;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct RustAVPlaybackTimingContract
        {
            public uint StructSize;
            public uint StructVersion;
            public double MasterTimeSec;
            public double ExternalTimeSec;
            public int HasAudioTimeSec;
            public double AudioTimeSec;
            public int HasAudioPresentedTimeSec;
            public double AudioPresentedTimeSec;
            public double AudioSinkDelaySec;
            public int HasAudioClock;
            public long MasterTimeUs;
            public long ExternalTimeUs;
            public int HasAudioTimeUs;
            public long AudioTimeUs;
            public int HasAudioPresentedTimeUs;
            public long AudioPresentedTimeUs;
            public long AudioSinkDelayUs;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct RustAVAvSyncContract
        {
            public uint StructSize;
            public uint StructVersion;
            public int MasterClock;
            public int HasAudioClockSec;
            public double AudioClockSec;
            public int HasVideoClockSec;
            public double VideoClockSec;
            public double DriftMs;
            public int StartupWarmupComplete;
            public ulong DropTotal;
            public ulong DuplicateTotal;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct RustAVAudioOutputPolicy
        {
            public uint StructSize;
            public uint StructVersion;
            public int FileStartThresholdMilliseconds;
            public int AndroidFileStartThresholdMilliseconds;
            public int RealtimeStartThresholdMilliseconds;
            public int RealtimeStartupGraceMilliseconds;
            public int RealtimeStartupMinimumThresholdMilliseconds;
            public int FileRingCapacityMilliseconds;
            public int AndroidFileRingCapacityMilliseconds;
            public int RealtimeRingCapacityMilliseconds;
            public int FileBufferedCeilingMilliseconds;
            public int AndroidFileBufferedCeilingMilliseconds;
            public int RealtimeBufferedCeilingMilliseconds;
            public int RealtimeStartupAdditionalSinkDelayMilliseconds;
            public int RealtimeSteadyAdditionalSinkDelayMilliseconds;
            public int RealtimeBackendAdditionalSinkDelayMilliseconds;
            public int RealtimeStartRequiresVideoFrame;
            public int AllowAndroidFileOutputRateBridge;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct RustAVSourceTimelineContract
        {
            public uint StructSize;
            public uint StructVersion;
            public int Model;
            public int AnchorKind;
            public int HasCurrentSourceTimeUs;
            public long CurrentSourceTimeUs;
            public int HasTimelineOriginUs;
            public long TimelineOriginUs;
            public int HasAnchorValueUs;
            public long AnchorValueUs;
            public int HasAnchorMonoUs;
            public long AnchorMonoUs;
            public int IsRealtime;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct RustAVPlayerSessionContract
        {
            public uint StructSize;
            public uint StructVersion;
            public int LifecycleState;
            public int PublicState;
            public int RuntimeState;
            public int PlaybackIntent;
            public int StopReason;
            public int SourceConnectionState;
            public int CanSeek;
            public int IsRealtime;
            public int IsBuffering;
            public int IsSyncing;
            public int AudioStartStateReported;
            public int ShouldStartAudio;
            public int AudioStartBlockReason;
            public int RequiredBufferedSamples;
            public int ReportedBufferedSamples;
            public int RequiresPresentedVideoFrame;
            public int HasPresentedVideoFrame;
            public int AndroidFileRateBridgeActive;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct RustAVAvSyncEnterpriseMetrics
        {
            public uint StructSize;
            public uint StructVersion;
            public uint SampleCount;
            public long WindowSpanUs;
            public long LatestRawOffsetUs;
            public long LatestSmoothOffsetUs;
            public double DriftSlopePpm;
            public double DriftProjected2hMs;
            public long OffsetAbsP95Us;
            public long OffsetAbsP99Us;
            public long OffsetAbsMaxUs;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct RustAVPassiveAvSyncSnapshot
        {
            public uint StructSize;
            public uint StructVersion;
            public long RawOffsetUs;
            public long SmoothOffsetUs;
            public double DriftPpm;
            public long DriftInterceptUs;
            public uint DriftSampleCount;
            public int VideoSchedule;
            public double AudioResampleRatio;
            public int AudioResampleActive;
            public int ShouldRebuildAnchor;
        }

        internal delegate int BackendRuntimeDiagnosticDelegate(
            int backendKind,
            string path,
            bool requireAudioExport,
            StringBuilder destination,
            int destinationLength);

        internal delegate int GetPlayerHealthSnapshotDelegate(
            int playerId,
            ref RustAVPlayerHealthSnapshotV2 snapshot);

        internal delegate int GetVideoFrameContractDelegate(
            int playerId,
            ref RustAVVideoFrameContract contract);

        internal delegate int GetPlaybackTimingContractDelegate(
            int playerId,
            ref RustAVPlaybackTimingContract contract);

        internal delegate int GetAvSyncContractDelegate(
            int playerId,
            ref RustAVAvSyncContract contract);

        internal delegate int GetAudioOutputPolicyDelegate(
            int playerId,
            ref RustAVAudioOutputPolicy policy);

        internal delegate int GetSourceTimelineContractDelegate(
            int playerId,
            ref RustAVSourceTimelineContract contract);

        internal delegate int GetPlayerSessionContractDelegate(
            int playerId,
            ref RustAVPlayerSessionContract contract);

        internal delegate int ReportAudioStartupStateDelegate(
            int playerId,
            int audioSampleRate,
            int audioChannels,
            int bufferedSamples,
            double startupElapsedMilliseconds,
            bool hasPresentedVideoFrame,
            bool requiresPresentedVideoFrame,
            bool androidFileRateBridgeActive);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerPlay")]
        private static extern int PlayPlayerNative(int id);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerPrepare")]
        private static extern int PreparePlayerNative(int id);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerPause")]
        private static extern int PausePlayerNative(int id);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerStop")]
        private static extern int StopPlayerNative(int id);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerClose")]
        private static extern int ClosePlayerNative(int id);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerSeek")]
        private static extern int SeekPlayerNative(int id, double time);

        internal delegate int GetAvSyncEnterpriseMetricsDelegate(
            int playerId,
            ref RustAVAvSyncEnterpriseMetrics metrics);

        internal delegate int GetPassiveAvSyncSnapshotDelegate(
            int playerId,
            ref RustAVPassiveAvSyncSnapshot snapshot);

        internal delegate int GetNativeVideoInteropCapsDelegate(
            int backendKind,
            string path,
            ref RustAVNativeVideoTarget target,
            ref RustAVNativeVideoInteropCaps caps);

        internal delegate int GetNativeVideoBridgeDescriptorDelegate(
            int playerId,
            ref RustAVNativeVideoBridgeDescriptor descriptor);

        internal delegate int GetNativeVideoPathSelectionDelegate(
            int playerId,
            ref RustAVNativeVideoPathSelection selection);

        internal delegate int GetWgpuRenderDescriptorDelegate(
            int playerId,
            ref RustAVWgpuRenderDescriptor descriptor);

        internal delegate int GetWgpuRenderStateViewDelegate(
            int playerId,
            ref RustAVWgpuRenderStateView state);

        internal delegate int GetNativeVideoColorInfoDelegate(
            int playerId,
            ref RustAVVideoColorInfo info);

        internal delegate int AcquireNativeVideoFrameDelegate(
            int playerId,
            ref RustAVNativeVideoFrame frame);

        internal delegate int GetNativeVideoSourcePlaneTexturesDelegate(
            int playerId,
            ref RustAVNativeVideoPlaneTextures textures);

        internal delegate int GetNativeVideoSourcePlaneViewsDelegate(
            int playerId,
            ref RustAVNativeVideoPlaneViews views);

        internal delegate int ReleaseNativeVideoFrameDelegate(
            int playerId,
            long frameIndex);

        internal struct RuntimeHealthView
        {
            public int State;
            public int RuntimeState;
            public int PlaybackIntent;
            public int StopReason;
            public MediaSourceConnectionState SourceConnectionState;
            public bool IsConnected;
            public bool IsPlaying;
            public bool IsRealtime;
            public bool CanSeek;
            public bool IsLooping;
            public int StreamCount;
            public int VideoDecoderCount;
            public bool HasAudioDecoder;
            public long SourcePacketCount;
            public long SourceTimeoutCount;
            public long SourceReconnectCount;
            public double DurationSec;
            public double SourceLastActivityAgeSec;
            public double CurrentTimeSec;
            public double ExternalTimeSec;
            public double AudioTimeSec;
            public double AudioPresentedTimeSec;
            public double AudioSinkDelaySec;
        }

        internal struct RuntimeHealthObservationView
        {
            public bool Available;
            public int State;
            public int RuntimeState;
            public int PlaybackIntent;
            public int StopReason;
            public string SourceState;
            public bool IsConnected;
            public bool IsPlaying;
            public bool IsRealtime;
            public bool CanSeek;
            public bool IsLooping;
            public int StreamCount;
            public int VideoDecoderCount;
            public bool HasAudioDecoder;
            public string SourcePackets;
            public string SourceTimeouts;
            public string SourceReconnects;
            public double DurationSec;
            public double SourceLastActivityAgeSec;
            public double CurrentTimeSec;
        }

        internal struct BackendRuntimeObservationView
        {
            public bool Available;
            public string RequestedBackend;
            public string ActualBackend;
            public string RequestedVideoRenderer;
            public string ActualVideoRenderer;
        }

        internal struct NativeVideoRuntimeObservationView
        {
            public bool Available;
            public string ActualRenderer;
            public string PresentationPath;
            public string ActivationDecision;
        }

        internal struct NativeVideoInteropCapsView
        {
            public MediaBackendKind BackendKind;
            public NativeVideoPlatformKind PlatformKind;
            public NativeVideoSurfaceKind SurfaceKind;
            public bool Supported;
            public bool ContractTargetSupported;
            public bool HardwareDecodeSupported;
            public bool ZeroCopySupported;
            public bool SourceSurfaceZeroCopySupported;
            public bool ExternalTextureTarget;
            public bool PresentedFrameDirectBindable;
            public bool PresentedFrameStrictZeroCopySupported;
            public bool SourcePlaneTexturesSupported;
            public bool SourcePlaneViewsSupported;
            public bool AcquireReleaseSupported;
            public bool RuntimeBridgePending;
            public uint Flags;
        }

        internal struct NativeVideoBridgeDescriptorView
        {
            public MediaBackendKind BackendKind;
            public NativeVideoPlatformKind TargetPlatformKind;
            public NativeVideoSurfaceKind TargetSurfaceKind;
            public int TargetWidth;
            public int TargetHeight;
            public NativeVideoPixelFormat TargetPixelFormat;
            public uint TargetFlags;
            public NativeVideoPlatformKind PlatformKind;
            public NativeVideoSurfaceKind SurfaceKind;
            public NativeVideoBridgeState State;
            public NativeVideoBridgeRuntimeKind RuntimeKind;
            public bool Supported;
            public bool HardwareDecodeSupported;
            public bool ZeroCopySupported;
            public bool AcquireReleaseSupported;
            public uint CapsFlags;
            public bool TargetValid;
            public bool RequestedExternalTextureTarget;
            public bool DirectTargetPresentAllowed;
            public bool TargetBindingSupported;
            public bool ExternalTextureTargetSupported;
            public bool FrameAcquireSupported;
            public bool FrameReleaseSupported;
            public bool FallbackCopyPath;
            public bool SourceSurfaceZeroCopy;
            public bool PresentedFrameDirectBindable;
            public bool PresentedFrameStrictZeroCopy;
            public bool SourcePlaneTexturesSupported;
            public bool SourcePlaneViewsSupported;
        }

        internal struct NativeVideoBridgeDescriptorObservationView
        {
            public bool Available;
            public string State;
            public string RuntimeKind;
            public bool ZeroCopySupported;
            public bool PresentedFrameDirectBindable;
            public bool SourcePlaneTexturesSupported;
            public bool FallbackCopyPath;
        }

        internal struct NativeVideoPathSelectionView
        {
            public NativeVideoPathKind Kind;
            public bool HasSourceFrame;
            public bool HasPresentedFrame;
            public VideoFrameMemoryKind SourceMemoryKind;
            public VideoFrameMemoryKind PresentedMemoryKind;
            public NativeVideoBridgeState BridgeState;
            public bool SourceSurfaceZeroCopy;
            public bool SourcePlaneTexturesSupported;
            public bool TargetZeroCopy;
            public bool CpuFallback;
        }

        internal struct NativeVideoPathSelectionObservationView
        {
            public bool Available;
            public string Kind;
            public string SourceMemoryKind;
            public string PresentedMemoryKind;
            public bool TargetZeroCopy;
            public bool SourcePlaneTexturesSupported;
            public bool CpuFallback;
        }

        internal struct WgpuRenderDescriptorView
        {
            public int OutputWidth;
            public int OutputHeight;
            public bool RuntimeReady;
            public bool SupportsYuv420p;
            public bool SupportsNv12;
            public bool SupportsP010;
            public bool SupportsRgba32;
            public bool SupportsExternalTextureRgba;
            public bool SupportsExternalTextureYu12;
            public bool ReadbackExportSupported;
        }

        internal struct WgpuRenderDescriptorObservationView
        {
            public bool Available;
            public bool RuntimeReady;
            public int OutputWidth;
            public int OutputHeight;
            public bool SupportsYuv420p;
            public bool SupportsNv12;
            public bool SupportsP010;
            public bool SupportsRgba32;
            public bool SupportsExternalTextureRgba;
            public bool SupportsExternalTextureYu12;
            public bool ReadbackExportSupported;
        }

        internal struct WgpuRenderStateView
        {
            public bool HasSourceContract;
            public bool HasPresentedContract;
            public VideoFrameMemoryKind SourceMemoryKind;
            public VideoFrameMemoryKind PresentedMemoryKind;
            public NativeVideoPixelFormat SourcePixelFormat;
            public NativeVideoPixelFormat PresentedPixelFormat;
            public WgpuRenderPath RenderPath;
            public WgpuExternalTextureFormat ExternalTextureFormat;
            public bool HasRenderedFrame;
            public long RenderedFrameIndex;
            public double RenderedTimeSec;
            public bool HasRenderError;
            public WgpuRenderError RenderErrorKind;
            public int UploadPlaneCount;
            public bool SourceZeroCopy;
            public bool CpuFallback;
        }

        internal struct WgpuRenderStateObservationView
        {
            public bool Available;
            public string RenderPath;
            public string SourceMemoryKind;
            public string PresentedMemoryKind;
            public string SourcePixelFormat;
            public string PresentedPixelFormat;
            public string ExternalTextureFormat;
            public bool HasRenderedFrame;
            public long RenderedFrameIndex;
            public double RenderedTimeSec;
            public bool HasRenderError;
            public string RenderErrorKind;
            public int UploadPlaneCount;
            public bool SourceZeroCopy;
            public bool CpuFallback;
        }

        internal struct NativeVideoFrameView
        {
            public NativeVideoSurfaceKind SurfaceKind;
            public IntPtr NativeHandle;
            public IntPtr AuxiliaryHandle;
            public int Width;
            public int Height;
            public NativeVideoPixelFormat PixelFormat;
            public double TimeSec;
            public long FrameIndex;
            public uint Flags;
        }

        internal struct NativeVideoPlaneTexturesView
        {
            public NativeVideoSurfaceKind SurfaceKind;
            public NativeVideoPixelFormat SourcePixelFormat;
            public IntPtr YNativeHandle;
            public IntPtr YAuxiliaryHandle;
            public int YWidth;
            public int YHeight;
            public NativeVideoPlaneTextureFormat YTextureFormat;
            public IntPtr UVNativeHandle;
            public IntPtr UVAuxiliaryHandle;
            public int UVWidth;
            public int UVHeight;
            public NativeVideoPlaneTextureFormat UVTextureFormat;
            public double TimeSec;
            public long FrameIndex;
            public uint Flags;
        }

        internal struct NativeVideoPlaneViewsView
        {
            public NativeVideoSurfaceKind SurfaceKind;
            public NativeVideoPixelFormat SourcePixelFormat;
            public IntPtr YNativeHandle;
            public IntPtr YAuxiliaryHandle;
            public int YWidth;
            public int YHeight;
            public NativeVideoPlaneTextureFormat YTextureFormat;
            public NativeVideoPlaneResourceKind YResourceKind;
            public IntPtr UVNativeHandle;
            public IntPtr UVAuxiliaryHandle;
            public int UVWidth;
            public int UVHeight;
            public NativeVideoPlaneTextureFormat UVTextureFormat;
            public NativeVideoPlaneResourceKind UVResourceKind;
            public double TimeSec;
            public long FrameIndex;
            public uint Flags;
        }

        internal struct VideoColorInfoView
        {
            public NativeVideoColorRange Range;
            public NativeVideoColorMatrix Matrix;
            public NativeVideoColorPrimaries Primaries;
            public NativeVideoTransferCharacteristic Transfer;
            public int BitDepth;
            public NativeVideoDynamicRange DynamicRange;
        }

        internal struct VideoFrameContractView
        {
            public VideoFrameMemoryKind MemoryKind;
            public NativeVideoSurfaceKind SurfaceKind;
            public NativeVideoPixelFormat PixelFormat;
            public int Width;
            public int Height;
            public int PlaneCount;
            public bool HardwareDecode;
            public bool ZeroCopy;
            public bool CpuFallback;
            public bool NativeHandlePresent;
            public bool AuxiliaryHandlePresent;
            public VideoColorInfoView Color;
            public bool HasColorDynamicRangeOverride;
            public NativeVideoDynamicRange ColorDynamicRangeOverride;
            public double TimeSec;
            public bool HasFrameIndex;
            public long FrameIndex;
            public bool HasNominalFps;
            public double NominalFps;
            public bool HasTimelineOriginSec;
            public double TimelineOriginSec;
            public ulong SeekEpoch;
            public bool Discontinuity;
        }

        internal struct VideoFrameObservationView
        {
            public bool Available;
            public string MemoryKind;
            public string DynamicRange;
            public double NominalFps;
        }

        internal struct PlaybackTimingContractView
        {
            public bool HasMicrosecondMirror;
            public double MasterTimeSec;
            public long MasterTimeUs;
            public double ExternalTimeSec;
            public long ExternalTimeUs;
            public bool HasAudioTimeSec;
            public double AudioTimeSec;
            public bool HasAudioTimeUs;
            public long AudioTimeUs;
            public bool HasAudioPresentedTimeSec;
            public double AudioPresentedTimeSec;
            public bool HasAudioPresentedTimeUs;
            public long AudioPresentedTimeUs;
            public double AudioSinkDelaySec;
            public long AudioSinkDelayUs;
            public bool HasAudioClock;
        }

        internal struct PlaybackTimingObservationView
        {
            public bool Available;
            public bool HasMicrosecondMirror;
            public double MasterTimeSec;
            public long MasterTimeUs;
            public double ExternalTimeSec;
            public long ExternalTimeUs;
            public bool HasAudioTimeSec;
            public double AudioTimeSec;
            public bool HasAudioTimeUs;
            public long AudioTimeUs;
            public bool HasAudioPresentedTimeSec;
            public double AudioPresentedTimeSec;
            public bool HasAudioPresentedTimeUs;
            public long AudioPresentedTimeUs;
            public double AudioSinkDelaySec;
            public long AudioSinkDelayUs;
            public bool HasAudioClock;
        }

        internal struct PlaybackTimingAuditStringsView
        {
            public bool Available;
            public string HasMicrosecondMirror;
            public string MasterTimeSec;
            public string MasterTimeUs;
            public string ExternalTimeSec;
            public string ExternalTimeUs;
            public string HasAudioTimeSec;
            public string AudioTimeSec;
            public string HasAudioTimeUs;
            public string AudioTimeUs;
            public string HasAudioPresentedTimeSec;
            public string AudioPresentedTimeSec;
            public string HasAudioPresentedTimeUs;
            public string AudioPresentedTimeUs;
            public string AudioSinkDelaySec;
            public string AudioSinkDelayUs;
            public string HasAudioClock;
        }

        internal struct AvSyncContractView
        {
            public AvSyncMasterClockKind MasterClock;
            public bool HasAudioClockSec;
            public double AudioClockSec;
            public bool HasVideoClockSec;
            public double VideoClockSec;
            public double DriftMs;
            public bool StartupWarmupComplete;
            public ulong DropTotal;
            public ulong DuplicateTotal;
        }

        internal struct AvSyncContractObservationView
        {
            public bool Available;
            public string MasterClock;
            public bool HasAudioClockSec;
            public double AudioClockSec;
            public bool HasVideoClockSec;
            public double VideoClockSec;
            public double ClockDeltaMs;
            public double DriftMs;
            public bool StartupWarmupComplete;
            public ulong DropTotal;
            public ulong DuplicateTotal;
        }

        internal struct AudioOutputPolicyView
        {
            public int FileStartThresholdMilliseconds;
            public int AndroidFileStartThresholdMilliseconds;
            public int RealtimeStartThresholdMilliseconds;
            public int RealtimeStartupGraceMilliseconds;
            public int RealtimeStartupMinimumThresholdMilliseconds;
            public int FileRingCapacityMilliseconds;
            public int AndroidFileRingCapacityMilliseconds;
            public int RealtimeRingCapacityMilliseconds;
            public int FileBufferedCeilingMilliseconds;
            public int AndroidFileBufferedCeilingMilliseconds;
            public int RealtimeBufferedCeilingMilliseconds;
            public int RealtimeStartupAdditionalSinkDelayMilliseconds;
            public int RealtimeSteadyAdditionalSinkDelayMilliseconds;
            public int RealtimeBackendAdditionalSinkDelayMilliseconds;
            public bool RealtimeStartRequiresVideoFrame;
            public bool AllowAndroidFileOutputRateBridge;
        }

        internal struct AudioOutputPolicyObservationView
        {
            public bool Available;
            public int FileStartThresholdMilliseconds;
            public int AndroidFileStartThresholdMilliseconds;
            public int RealtimeStartThresholdMilliseconds;
            public int RealtimeStartupGraceMilliseconds;
            public int RealtimeStartupMinimumThresholdMilliseconds;
            public int FileRingCapacityMilliseconds;
            public int AndroidFileRingCapacityMilliseconds;
            public int RealtimeRingCapacityMilliseconds;
            public int FileBufferedCeilingMilliseconds;
            public int AndroidFileBufferedCeilingMilliseconds;
            public int RealtimeBufferedCeilingMilliseconds;
            public int RealtimeStartupAdditionalSinkDelayMilliseconds;
            public int RealtimeSteadyAdditionalSinkDelayMilliseconds;
            public int RealtimeBackendAdditionalSinkDelayMilliseconds;
            public bool RealtimeStartRequiresVideoFrame;
            public bool AllowAndroidFileOutputRateBridge;
        }

        internal struct AudioOutputPolicyAuditStringsView
        {
            public bool Available;
            public string FileStartThresholdMilliseconds;
            public string AndroidFileStartThresholdMilliseconds;
            public string RealtimeStartThresholdMilliseconds;
            public string RealtimeStartupGraceMilliseconds;
            public string RealtimeStartupMinimumThresholdMilliseconds;
            public string FileRingCapacityMilliseconds;
            public string AndroidFileRingCapacityMilliseconds;
            public string RealtimeRingCapacityMilliseconds;
            public string FileBufferedCeilingMilliseconds;
            public string AndroidFileBufferedCeilingMilliseconds;
            public string RealtimeBufferedCeilingMilliseconds;
            public string RealtimeStartupAdditionalSinkDelayMilliseconds;
            public string RealtimeSteadyAdditionalSinkDelayMilliseconds;
            public string RealtimeBackendAdditionalSinkDelayMilliseconds;
            public string RealtimeStartRequiresVideoFrame;
            public string AllowAndroidFileOutputRateBridge;
        }

        internal struct SourceTimelineContractView
        {
            public int Model;
            public int AnchorKind;
            public bool HasCurrentSourceTimeUs;
            public long CurrentSourceTimeUs;
            public bool HasTimelineOriginUs;
            public long TimelineOriginUs;
            public bool HasAnchorValueUs;
            public long AnchorValueUs;
            public bool HasAnchorMonoUs;
            public long AnchorMonoUs;
            public bool IsRealtime;
        }

        internal struct PlayerSessionContractView
        {
            public int LifecycleState;
            public int PublicState;
            public int RuntimeState;
            public int PlaybackIntent;
            public int StopReason;
            public MediaSourceConnectionState SourceConnectionState;
            public bool CanSeek;
            public bool IsRealtime;
            public bool IsBuffering;
            public bool IsSyncing;
            public bool AudioStartStateReported;
            public bool ShouldStartAudio;
            public int AudioStartBlockReason;
            public int RequiredBufferedSamples;
            public int ReportedBufferedSamples;
            public bool RequiresPresentedVideoFrame;
            public bool HasPresentedVideoFrame;
            public bool AndroidFileRateBridgeActive;
        }

        internal struct PlayerSessionObservationView
        {
            public bool Available;
            public string LifecycleState;
            public int PublicState;
            public int RuntimeState;
            public int PlaybackIntent;
            public int StopReason;
            public string SourceState;
            public bool CanSeek;
            public bool IsRealtime;
            public bool IsBuffering;
            public bool IsSyncing;
            public bool AudioStartStateReported;
            public bool ShouldStartAudio;
            public int AudioStartBlockReason;
            public int RequiredBufferedSamples;
            public int ReportedBufferedSamples;
            public bool RequiresPresentedVideoFrame;
            public bool HasPresentedVideoFrame;
            public bool AndroidFileRateBridgeActive;
        }

        internal struct PlayerSessionAuditStringsView
        {
            public bool Available;
            public string LifecycleState;
            public string PublicState;
            public string RuntimeState;
            public string PlaybackIntent;
            public string StopReason;
            public string SourceState;
            public string CanSeek;
            public string IsRealtime;
            public string IsBuffering;
            public string IsSyncing;
        }

        internal struct SourceTimelineObservationView
        {
            public bool Available;
            public string Model;
            public string AnchorKind;
            public bool HasCurrentSourceTimeUs;
            public long CurrentSourceTimeUs;
            public bool HasTimelineOriginUs;
            public long TimelineOriginUs;
            public bool HasAnchorValueUs;
            public long AnchorValueUs;
            public bool HasAnchorMonoUs;
            public long AnchorMonoUs;
            public bool IsRealtime;
        }

        internal struct SourceTimelineAuditStringsView
        {
            public bool Available;
            public string Model;
            public string AnchorKind;
            public string HasCurrentSourceTimeUs;
            public string CurrentSourceTimeUs;
            public string HasTimelineOriginUs;
            public string TimelineOriginUs;
            public string HasAnchorValueUs;
            public string AnchorValueUs;
            public string HasAnchorMonoUs;
            public string AnchorMonoUs;
            public string IsRealtime;
        }

        internal struct AudioStartupObservationView
        {
            public int AudioSampleRate;
            public int AudioChannels;
            public int BufferedSamples;
            public double StartupElapsedMilliseconds;
            public bool HasPresentedVideoFrame;
            public bool RequiresPresentedVideoFrame;
            public bool AndroidFileRateBridgeActive;
        }

        internal struct ReferencePlaybackObservationView
        {
            public double ReferenceTimeSec;
            public string ReferenceKind;
            public bool HasSample;
        }

        internal struct PlaybackStartObservationView
        {
            public bool Started;
            public string Source;
        }

        internal struct ValidationWindowStartObservationView
        {
            public bool ShouldStart;
            public string Reason;
        }

        internal struct ValidationResultObservationView
        {
            public bool Passed;
            public string Reason;
            public double PlaybackAdvanceSeconds;
        }

        internal struct ValidationVideoTextureObservationView
        {
            public bool HasTexture;
            public int TextureWidth;
            public int TextureHeight;
        }

        internal struct ValidationWindowEvidenceObservationView
        {
            public bool ObservedTextureDuringWindow;
            public bool ObservedAudioDuringWindow;
            public bool ObservedStartedDuringWindow;
            public bool ObservedNativeFrameDuringWindow;
            public double MaxObservedPlaybackTime;
        }

        internal struct PullValidationGateInputsView
        {
            public bool HasTexture;
            public bool RequireAudioOutput;
            public bool AudioEnabled;
            public bool AudioPlaying;
            public bool Started;
            public bool HasPresentedNativeVideoFrame;
            public double PlaybackTimeSec;
        }

        internal struct PullValidationAudioGatePolicyView
        {
            public bool RequireAudioOutput;
            public bool AudioEnabled;
            public bool ConfiguredRequireAudioOutput;
            public string Source;
            public bool RuntimeContractAvailable;
            public bool RuntimeStateReported;
            public bool RuntimeShouldPlay;
            public int RuntimeBlockReason;
        }

        internal struct ValidationAudioPlaybackObservationView
        {
            public bool SourcePresent;
            public bool Playing;
        }

        internal struct MediaPlayerValidationGateInputsView
        {
            public bool HasTexture;
            public bool AudioPlaying;
            public bool Started;
            public bool RuntimePlaybackConfirmed;
            public bool HasPresentedNativeVideoFrame;
            public double PlaybackTimeSec;
        }

        internal struct ValidationSummaryHeaderView
        {
            public bool Passed;
            public string Reason;
            public string Uri;
            public string RequestedBackend;
            public string ActualBackend;
            public bool IncludeVideoRenderer;
            public string RequestedVideoRenderer;
            public string ActualVideoRenderer;
            public bool IncludeRequireAudioOutput;
            public bool RequireAudioOutput;
            public bool IncludeConfiguredRequireAudioOutput;
            public bool ConfiguredRequireAudioOutput;
            public bool IncludeAudioGateConfiguredAudioEnabled;
            public bool AudioGateConfiguredAudioEnabled;
            public bool IncludeAudioGateSource;
            public string AudioGateSource;
            public bool IncludeAudioGateRuntimeContractAvailable;
            public bool AudioGateRuntimeContractAvailable;
            public bool IncludeAudioGateRuntimeStateReported;
            public bool AudioGateRuntimeStateReported;
            public bool IncludeAudioGateRuntimeShouldPlay;
            public bool AudioGateRuntimeShouldPlay;
            public bool IncludeAudioGateRuntimeBlockReason;
            public string AudioGateRuntimeBlockReason;
            public double PlaybackAdvanceSeconds;
        }

        internal struct ValidationSummaryWindowView
        {
            public bool HasTexture;
            public bool AudioPlaying;
            public bool Started;
            public bool IncludeAudioSourcePresent;
            public bool AudioSourcePresent;
            public bool IncludeHasAudioListener;
            public bool HasAudioListener;
            public bool ObservedTextureDuringWindow;
            public bool ObservedAudioDuringWindow;
            public bool ObservedStartedDuringWindow;
            public bool IncludeObservedNativeFrameDuringWindow;
            public bool ObservedNativeFrameDuringWindow;
            public bool IncludeValidationWindowStartReason;
            public string ValidationWindowStartReason;
        }

        internal struct ValidationSummaryPlayerSessionView
        {
            public bool Available;
            public string LifecycleState;
            public string PublicState;
            public string RuntimeState;
            public string PlaybackIntent;
            public string StopReason;
            public string SourceState;
            public string CanSeek;
            public string IsRealtime;
            public string IsBuffering;
            public string IsSyncing;
        }

        internal struct ValidationSummaryPlayerSessionExtendedView
        {
            public bool Available;
            public string LifecycleState;
            public string PublicState;
            public string RuntimeState;
            public string PlaybackIntent;
            public string StopReason;
            public string SourceState;
            public string CanSeek;
            public string IsRealtime;
            public string IsBuffering;
            public string IsSyncing;
            public string AudioStartStateReported;
            public string ShouldStartAudio;
            public string AudioStartBlockReason;
            public string RequiredBufferedSamples;
            public string ReportedBufferedSamples;
            public string RequiresPresentedVideoFrame;
            public string HasPresentedVideoFrame;
            public string AndroidFileRateBridgeActive;
        }

        internal struct ValidationSummaryFrameContractView
        {
            public bool Available;
            public string MemoryKind;
            public string DynamicRange;
            public string NominalFps;
        }

        internal struct ValidationSummaryAvSyncContractView
        {
            public bool Available;
            public string MasterClock;
            public string DriftMs;
            public string ClockDeltaMs;
            public string DropTotal;
            public string DuplicateTotal;
        }

        internal struct ValidationSummarySourceRuntimeView
        {
            public string State;
            public string Packets;
            public string Timeouts;
            public string Reconnects;
            public bool IncludeLastActivityAgeSeconds;
            public string LastActivityAgeSeconds;
        }

        internal struct ValidationSummaryNativeVideoRuntimeView
        {
            public bool Active;
            public string ActivationDecision;
            public bool HasPresentedFrame;
        }

        internal struct ValidationSummaryRuntimeHealthView
        {
            public bool Available;
            public string State;
            public string RuntimeState;
            public string PlaybackIntent;
            public string StreamCount;
            public string VideoDecoderCount;
            public string HasAudioDecoder;
        }

        internal struct ValidationSummaryPathSelectionView
        {
            public bool Available;
            public string Kind;
        }

        internal struct ValidationSummaryEnterpriseMetricsView
        {
            public string SessionId;
            public string SourceType;
            public string AudioClockUs;
            public string VideoPtsUs;
            public string OffsetUs;
            public string OffsetSmoothUs;
            public string DriftPpm;
            public string AudioBufferMs;
            public string VideoQueueDepth;
            public string DropFrames;
            public string DuplicateFrames;
            public string RebufferCount;
            public string SyncRebuildCount;
            public string AudioResampleRatio;
            public string Platform;
            public string DeviceModel;
            public string AvOffsetMeanMs;
            public string AvOffsetP95Ms;
            public string AvOffsetP99Ms;
        }

        internal struct AvSyncEnterpriseMetricsView
        {
            public uint SampleCount;
            public long WindowSpanUs;
            public long LatestRawOffsetUs;
            public long LatestSmoothOffsetUs;
            public double DriftSlopePpm;
            public double DriftProjected2hMs;
            public long OffsetAbsP95Us;
            public long OffsetAbsP99Us;
            public long OffsetAbsMaxUs;
        }

        internal struct AvSyncEnterpriseObservationView
        {
            public bool Available;
            public uint SampleCount;
            public long WindowSpanUs;
            public long LatestRawOffsetUs;
            public long LatestSmoothOffsetUs;
            public double DriftSlopePpm;
            public double DriftProjected2hMs;
            public long OffsetAbsP95Us;
            public long OffsetAbsP99Us;
            public long OffsetAbsMaxUs;
        }

        internal struct AvSyncEnterpriseAuditStringsView
        {
            public bool Available;
            public string SampleCount;
            public string WindowSpanUs;
            public string LatestRawOffsetUs;
            public string LatestSmoothOffsetUs;
            public string DriftSlopePpm;
            public string DriftProjected2hMs;
            public string OffsetAbsP95Us;
            public string OffsetAbsP99Us;
            public string OffsetAbsMaxUs;
        }

        internal struct PassiveAvSyncSnapshotView
        {
            public long RawOffsetUs;
            public long SmoothOffsetUs;
            public double DriftPpm;
            public long DriftInterceptUs;
            public uint DriftSampleCount;
            public string VideoSchedule;
            public double AudioResampleRatio;
            public bool AudioResampleActive;
            public bool ShouldRebuildAnchor;
        }

        internal struct PassiveAvSyncObservationView
        {
            public bool Available;
            public long RawOffsetUs;
            public long SmoothOffsetUs;
            public double DriftPpm;
            public long DriftInterceptUs;
            public uint DriftSampleCount;
            public string VideoSchedule;
            public double AudioResampleRatio;
            public bool AudioResampleActive;
            public bool ShouldRebuildAnchor;
        }

        internal struct PassiveAvSyncAudioResampleCommandView
        {
            public float Pitch;
            public bool Active;
            public string Source;
        }

        internal struct PassiveAvSyncAuditStringsView
        {
            public bool Available;
            public string RawOffsetUs;
            public string SmoothOffsetUs;
            public string DriftPpm;
            public string DriftInterceptUs;
            public string DriftSampleCount;
            public string VideoSchedule;
            public string AudioResampleRatio;
            public string AudioResampleActive;
            public string ShouldRebuildAnchor;
        }

        internal struct AudioStartRuntimeCommandView
        {
            public bool ShouldPlay;
            public string Source;
            public bool ContractAvailable;
            public bool StateReported;
            public bool ContractShouldStart;
            public int BlockReason;
            public int RequiredBufferedSamples;
            public int ReportedBufferedSamples;
            public bool RequiresPresentedVideoFrame;
            public bool HasPresentedVideoFrame;
            public bool AndroidFileRateBridgeActive;
        }

        internal struct NativeVideoStartupWarmupCommandView
        {
            public bool WarmupEnabled;
            public bool ShouldHold;
            public bool ContractAvailable;
            public bool ContractComplete;
            public string Source;
        }

        internal static bool TryReadRuntimeHealth(
            GetPlayerHealthSnapshotDelegate getPlayerHealthSnapshot,
            int playerId,
            out RuntimeHealthView health)
        {
            health = default(RuntimeHealthView);

            try
            {
                var snapshot = CreateHealthSnapshot();
                var result = getPlayerHealthSnapshot(playerId, ref snapshot);
                if (result < 0)
                {
                    return false;
                }

                health = new RuntimeHealthView
                {
                    State = snapshot.State,
                    RuntimeState = snapshot.RuntimeState,
                    PlaybackIntent = snapshot.PlaybackIntent,
                    StopReason = snapshot.StopReason,
                    SourceConnectionState = NormalizeSourceConnectionState(snapshot.SourceConnectionState),
                    IsConnected = snapshot.IsConnected != 0,
                    IsPlaying = snapshot.IsPlaying != 0,
                    IsRealtime = snapshot.IsRealtime != 0,
                    CanSeek = snapshot.CanSeek != 0,
                    IsLooping = snapshot.IsLooping != 0,
                    StreamCount = snapshot.StreamCount,
                    VideoDecoderCount = snapshot.VideoDecoderCount,
                    HasAudioDecoder = snapshot.HasAudioDecoder != 0,
                    SourcePacketCount = snapshot.SourcePacketCount,
                    SourceTimeoutCount = snapshot.SourceTimeoutCount,
                    SourceReconnectCount = snapshot.SourceReconnectCount,
                    DurationSec = snapshot.DurationSec,
                    SourceLastActivityAgeSec = snapshot.SourceLastActivityAgeSec,
                    CurrentTimeSec = snapshot.CurrentTimeSec,
                    ExternalTimeSec = snapshot.ExternalTimeSec,
                    AudioTimeSec = snapshot.AudioTimeSec,
                    AudioPresentedTimeSec = snapshot.AudioPresentedTimeSec,
                    AudioSinkDelaySec = snapshot.AudioSinkDelaySec,
                };
                return true;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
            catch (DllNotFoundException)
            {
                return false;
            }
        }

        internal static bool TryReadVideoFrameContract(
            GetVideoFrameContractDelegate getVideoFrameContract,
            int playerId,
            out VideoFrameContractView contract)
        {
            contract = default(VideoFrameContractView);

            try
            {
                var nativeContract = CreateVideoFrameContract();
                var result = getVideoFrameContract(playerId, ref nativeContract);
                if (result <= 0)
                {
                    return false;
                }

                contract = NormalizeVideoFrameContract(nativeContract);
                return true;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
            catch (DllNotFoundException)
            {
                return false;
            }
        }

        internal static VideoFrameObservationView CreateVideoFrameObservation(
            bool available,
            VideoFrameContractView contract)
        {
            if (!available)
            {
                return new VideoFrameObservationView
                {
                    Available = false,
                    MemoryKind = "Unavailable",
                    DynamicRange = "Unavailable",
                };
            }

            return new VideoFrameObservationView
            {
                Available = true,
                MemoryKind = contract.MemoryKind.ToString(),
                DynamicRange = contract.Color.DynamicRange.ToString(),
                NominalFps = contract.HasNominalFps ? contract.NominalFps : 0.0,
            };
        }

        internal static RuntimeHealthObservationView CreateRuntimeHealthObservation(
            bool available,
            MediaPlayerPull.PlayerRuntimeHealth health)
        {
            if (!available)
            {
                return new RuntimeHealthObservationView
                {
                    Available = false,
                    State = -1,
                    RuntimeState = -1,
                    PlaybackIntent = -1,
                    StopReason = -1,
                    SourceState = "Unavailable",
                    SourcePackets = "-1",
                    SourceTimeouts = "-1",
                    SourceReconnects = "-1",
                    DurationSec = -1.0,
                    SourceLastActivityAgeSec = -1.0,
                    CurrentTimeSec = -1.0,
                };
            }

            return new RuntimeHealthObservationView
            {
                Available = true,
                State = health.State,
                RuntimeState = health.RuntimeState,
                PlaybackIntent = health.PlaybackIntent,
                StopReason = health.StopReason,
                SourceState = health.SourceConnectionState.ToString(),
                IsConnected = health.IsConnected,
                IsPlaying = health.IsPlaying,
                IsRealtime = health.IsRealtime,
                CanSeek = health.CanSeek,
                IsLooping = health.IsLooping,
                StreamCount = health.StreamCount,
                VideoDecoderCount = health.VideoDecoderCount,
                HasAudioDecoder = health.HasAudioDecoder,
                SourcePackets = health.SourcePacketCount.ToString(),
                SourceTimeouts = health.SourceTimeoutCount.ToString(),
                SourceReconnects = health.SourceReconnectCount.ToString(),
                DurationSec = health.DurationSec,
                SourceLastActivityAgeSec = health.SourceLastActivityAgeSec,
                CurrentTimeSec = health.CurrentTimeSec,
            };
        }

        internal static RuntimeHealthObservationView CreateRuntimeHealthObservation(
            bool available,
            MediaPlayer.PlayerRuntimeHealth health)
        {
            if (!available)
            {
                return new RuntimeHealthObservationView
                {
                    Available = false,
                    State = -1,
                    RuntimeState = -1,
                    PlaybackIntent = -1,
                    StopReason = -1,
                    SourceState = "Unavailable",
                    SourcePackets = "-1",
                    SourceTimeouts = "-1",
                    SourceReconnects = "-1",
                    DurationSec = -1.0,
                    SourceLastActivityAgeSec = -1.0,
                    CurrentTimeSec = -1.0,
                };
            }

            return new RuntimeHealthObservationView
            {
                Available = true,
                State = -1,
                RuntimeState = -1,
                PlaybackIntent = -1,
                StopReason = -1,
                SourceState = health.SourceConnectionState.ToString(),
                IsConnected = health.IsConnected,
                IsPlaying = health.IsPlaying,
                IsRealtime = health.IsRealtime,
                CanSeek = false,
                IsLooping = false,
                StreamCount = -1,
                VideoDecoderCount = -1,
                HasAudioDecoder = false,
                SourcePackets = health.SourcePacketCount.ToString(),
                SourceTimeouts = health.SourceTimeoutCount.ToString(),
                SourceReconnects = health.SourceReconnectCount.ToString(),
                DurationSec = -1.0,
                SourceLastActivityAgeSec = health.SourceLastActivityAgeSec,
                CurrentTimeSec = health.CurrentTimeSec,
            };
        }

        internal static BackendRuntimeObservationView CreatePullBackendRuntimeObservation(
            bool available,
            MediaBackendKind requestedBackend,
            MediaBackendKind actualBackend,
            MediaPlayerPull.PullVideoRendererKind requestedVideoRenderer,
            MediaPlayerPull.PullVideoRendererKind actualVideoRenderer)
        {
            if (!available)
            {
                return new BackendRuntimeObservationView
                {
                    Available = false,
                    RequestedBackend = "Unavailable",
                    ActualBackend = "Unavailable",
                    RequestedVideoRenderer = "Unavailable",
                    ActualVideoRenderer = "Unavailable",
                };
            }

            return new BackendRuntimeObservationView
            {
                Available = true,
                RequestedBackend = requestedBackend.ToString(),
                ActualBackend = actualBackend.ToString(),
                RequestedVideoRenderer = requestedVideoRenderer.ToString(),
                ActualVideoRenderer = actualVideoRenderer.ToString(),
            };
        }

        internal static BackendRuntimeObservationView CreateMediaPlayerBackendRuntimeObservation(
            bool available,
            MediaBackendKind requestedBackend,
            MediaBackendKind actualBackend)
        {
            if (!available)
            {
                return new BackendRuntimeObservationView
                {
                    Available = false,
                    RequestedBackend = "Unavailable",
                    ActualBackend = "Unavailable",
                    RequestedVideoRenderer = "Unavailable",
                    ActualVideoRenderer = "Unavailable",
                };
            }

            return new BackendRuntimeObservationView
            {
                Available = true,
                RequestedBackend = requestedBackend.ToString(),
                ActualBackend = actualBackend.ToString(),
                RequestedVideoRenderer = "Unavailable",
                ActualVideoRenderer = "Unavailable",
            };
        }

        internal static NativeVideoRuntimeObservationView CreateNativeVideoRuntimeObservation(
            bool available,
            bool nativeVideoActive,
            MediaPlayer.NativeVideoPresentationPathKind presentationPath,
            MediaPlayer.NativeVideoActivationDecisionKind activationDecision)
        {
            if (!available)
            {
                return new NativeVideoRuntimeObservationView
                {
                    Available = false,
                    ActualRenderer = "Unavailable",
                    PresentationPath = "Unavailable",
                    ActivationDecision = "Unavailable",
                };
            }

            var presentationPathString = presentationPath.ToString();
            return new NativeVideoRuntimeObservationView
            {
                Available = true,
                ActualRenderer = nativeVideoActive
                    ? presentationPathString
                    : "TextureFallback",
                PresentationPath = presentationPathString,
                ActivationDecision = activationDecision.ToString(),
            };
        }

        internal static bool TryReadPlaybackTimingContract(
            GetPlaybackTimingContractDelegate getPlaybackTimingContract,
            int playerId,
            out PlaybackTimingContractView contract)
        {
            contract = default(PlaybackTimingContractView);

            try
            {
                var nativeContract = CreatePlaybackTimingContract();
                var result = getPlaybackTimingContract(playerId, ref nativeContract);
                if (result < 0)
                {
                    return false;
                }

                contract = NormalizePlaybackTimingContract(nativeContract);
                return true;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
            catch (DllNotFoundException)
            {
                return false;
            }
        }

        internal static bool TryReadAvSyncContract(
            GetAvSyncContractDelegate getAvSyncContract,
            int playerId,
            out AvSyncContractView contract)
        {
            contract = default(AvSyncContractView);

            try
            {
                var nativeContract = CreateAvSyncContract();
                var result = getAvSyncContract(playerId, ref nativeContract);
                if (result < 0)
                {
                    return false;
                }

                contract = NormalizeAvSyncContract(nativeContract);
                return true;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
            catch (DllNotFoundException)
            {
                return false;
            }
        }

        internal static bool TryReadAudioOutputPolicy(
            GetAudioOutputPolicyDelegate getAudioOutputPolicy,
            int playerId,
            out AudioOutputPolicyView policy)
        {
            policy = default(AudioOutputPolicyView);

            try
            {
                var nativePolicy = CreateAudioOutputPolicy();
                var result = getAudioOutputPolicy(playerId, ref nativePolicy);
                if (result < 0)
                {
                    return false;
                }

                policy = NormalizeAudioOutputPolicy(nativePolicy);
                return true;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
            catch (DllNotFoundException)
            {
                return false;
            }
        }

        internal static void ResetAudioOutputPolicyState(
            ref AudioOutputPolicyView policy,
            ref bool hasPolicy,
            ref bool missingLogged)
        {
            policy = default(AudioOutputPolicyView);
            hasPolicy = false;
            missingLogged = false;
        }

        internal static AudioOutputPolicyObservationView CreateAudioOutputPolicyObservation(
            bool available,
            AudioOutputPolicyView policy)
        {
            if (!available)
            {
                return default(AudioOutputPolicyObservationView);
            }

            return new AudioOutputPolicyObservationView
            {
                Available = true,
                FileStartThresholdMilliseconds = policy.FileStartThresholdMilliseconds,
                AndroidFileStartThresholdMilliseconds = policy.AndroidFileStartThresholdMilliseconds,
                RealtimeStartThresholdMilliseconds = policy.RealtimeStartThresholdMilliseconds,
                RealtimeStartupGraceMilliseconds = policy.RealtimeStartupGraceMilliseconds,
                RealtimeStartupMinimumThresholdMilliseconds =
                    policy.RealtimeStartupMinimumThresholdMilliseconds,
                FileRingCapacityMilliseconds = policy.FileRingCapacityMilliseconds,
                AndroidFileRingCapacityMilliseconds = policy.AndroidFileRingCapacityMilliseconds,
                RealtimeRingCapacityMilliseconds = policy.RealtimeRingCapacityMilliseconds,
                FileBufferedCeilingMilliseconds = policy.FileBufferedCeilingMilliseconds,
                AndroidFileBufferedCeilingMilliseconds =
                    policy.AndroidFileBufferedCeilingMilliseconds,
                RealtimeBufferedCeilingMilliseconds =
                    policy.RealtimeBufferedCeilingMilliseconds,
                RealtimeStartupAdditionalSinkDelayMilliseconds =
                    policy.RealtimeStartupAdditionalSinkDelayMilliseconds,
                RealtimeSteadyAdditionalSinkDelayMilliseconds =
                    policy.RealtimeSteadyAdditionalSinkDelayMilliseconds,
                RealtimeBackendAdditionalSinkDelayMilliseconds =
                    policy.RealtimeBackendAdditionalSinkDelayMilliseconds,
                RealtimeStartRequiresVideoFrame = policy.RealtimeStartRequiresVideoFrame,
                AllowAndroidFileOutputRateBridge = policy.AllowAndroidFileOutputRateBridge,
            };
        }

        internal static PlaybackTimingObservationView CreatePlaybackTimingObservation(
            bool available,
            PlaybackTimingContractView contract)
        {
            if (!available)
            {
                return default(PlaybackTimingObservationView);
            }

            return new PlaybackTimingObservationView
            {
                Available = true,
                HasMicrosecondMirror = contract.HasMicrosecondMirror,
                MasterTimeSec = contract.MasterTimeSec,
                MasterTimeUs = contract.MasterTimeUs,
                ExternalTimeSec = contract.ExternalTimeSec,
                ExternalTimeUs = contract.ExternalTimeUs,
                HasAudioTimeSec = contract.HasAudioTimeSec,
                AudioTimeSec = contract.AudioTimeSec,
                HasAudioTimeUs = contract.HasAudioTimeUs,
                AudioTimeUs = contract.AudioTimeUs,
                HasAudioPresentedTimeSec = contract.HasAudioPresentedTimeSec,
                AudioPresentedTimeSec = contract.AudioPresentedTimeSec,
                HasAudioPresentedTimeUs = contract.HasAudioPresentedTimeUs,
                AudioPresentedTimeUs = contract.AudioPresentedTimeUs,
                AudioSinkDelaySec = contract.AudioSinkDelaySec,
                AudioSinkDelayUs = contract.AudioSinkDelayUs,
                HasAudioClock = contract.HasAudioClock,
            };
        }

        internal static PlaybackTimingAuditStringsView CreatePlaybackTimingAuditStrings(
            bool available,
            PlaybackTimingContractView contract)
        {
            var observation = CreatePlaybackTimingObservation(available, contract);
            if (!observation.Available)
            {
                return new PlaybackTimingAuditStringsView
                {
                    Available = false,
                    HasMicrosecondMirror = "False",
                    MasterTimeSec = "n/a",
                    MasterTimeUs = "n/a",
                    ExternalTimeSec = "n/a",
                    ExternalTimeUs = "n/a",
                    HasAudioTimeSec = "False",
                    AudioTimeSec = "n/a",
                    HasAudioTimeUs = "False",
                    AudioTimeUs = "n/a",
                    HasAudioPresentedTimeSec = "False",
                    AudioPresentedTimeSec = "n/a",
                    HasAudioPresentedTimeUs = "False",
                    AudioPresentedTimeUs = "n/a",
                    AudioSinkDelaySec = "n/a",
                    AudioSinkDelayUs = "n/a",
                    HasAudioClock = "False",
                };
            }

            return new PlaybackTimingAuditStringsView
            {
                Available = true,
                HasMicrosecondMirror = observation.HasMicrosecondMirror.ToString(),
                MasterTimeSec = observation.MasterTimeSec.ToString("F3"),
                MasterTimeUs = observation.MasterTimeUs.ToString(),
                ExternalTimeSec = observation.ExternalTimeSec.ToString("F3"),
                ExternalTimeUs = observation.ExternalTimeUs.ToString(),
                HasAudioTimeSec = observation.HasAudioTimeSec.ToString(),
                AudioTimeSec = observation.HasAudioTimeSec
                    ? observation.AudioTimeSec.ToString("F3")
                    : "n/a",
                HasAudioTimeUs = observation.HasAudioTimeUs.ToString(),
                AudioTimeUs = observation.HasAudioTimeUs
                    ? observation.AudioTimeUs.ToString()
                    : "n/a",
                HasAudioPresentedTimeSec = observation.HasAudioPresentedTimeSec.ToString(),
                AudioPresentedTimeSec = observation.HasAudioPresentedTimeSec
                    ? observation.AudioPresentedTimeSec.ToString("F3")
                    : "n/a",
                HasAudioPresentedTimeUs = observation.HasAudioPresentedTimeUs.ToString(),
                AudioPresentedTimeUs = observation.HasAudioPresentedTimeUs
                    ? observation.AudioPresentedTimeUs.ToString()
                    : "n/a",
                AudioSinkDelaySec = observation.AudioSinkDelaySec.ToString("F3"),
                AudioSinkDelayUs = observation.AudioSinkDelayUs.ToString(),
                HasAudioClock = observation.HasAudioClock.ToString(),
            };
        }

        internal static AvSyncContractObservationView CreateAvSyncContractObservation(
            bool available,
            AvSyncContractView contract)
        {
            if (!available)
            {
                return new AvSyncContractObservationView
                {
                    Available = false,
                    MasterClock = "Unavailable",
                };
            }

            var hasAudioClockSec = contract.HasAudioClockSec;
            var audioClockSec = hasAudioClockSec ? contract.AudioClockSec : 0.0;
            var hasVideoClockSec = contract.HasVideoClockSec;
            var videoClockSec = hasVideoClockSec ? contract.VideoClockSec : 0.0;
            var clockDeltaMs = hasAudioClockSec && hasVideoClockSec
                ? (audioClockSec - videoClockSec) * 1000.0
                : 0.0;

            return new AvSyncContractObservationView
            {
                Available = true,
                MasterClock = contract.MasterClock.ToString(),
                HasAudioClockSec = hasAudioClockSec,
                AudioClockSec = audioClockSec,
                HasVideoClockSec = hasVideoClockSec,
                VideoClockSec = videoClockSec,
                ClockDeltaMs = clockDeltaMs,
                DriftMs = contract.DriftMs,
                StartupWarmupComplete = contract.StartupWarmupComplete,
                DropTotal = contract.DropTotal,
                DuplicateTotal = contract.DuplicateTotal,
            };
        }

        internal static NativeVideoBridgeDescriptorObservationView CreateNativeVideoBridgeDescriptorObservation(
            bool available,
            NativeVideoBridgeDescriptorView descriptor)
        {
            if (!available)
            {
                return new NativeVideoBridgeDescriptorObservationView
                {
                    Available = false,
                    State = "Unavailable",
                    RuntimeKind = "Unavailable",
                };
            }

            return new NativeVideoBridgeDescriptorObservationView
            {
                Available = true,
                State = descriptor.State.ToString(),
                RuntimeKind = descriptor.RuntimeKind.ToString(),
                ZeroCopySupported = descriptor.ZeroCopySupported,
                PresentedFrameDirectBindable = descriptor.PresentedFrameDirectBindable,
                SourcePlaneTexturesSupported = descriptor.SourcePlaneTexturesSupported,
                FallbackCopyPath = descriptor.FallbackCopyPath,
            };
        }

        internal static NativeVideoPathSelectionObservationView CreateNativeVideoPathSelectionObservation(
            bool available,
            NativeVideoPathSelectionView selection)
        {
            if (!available)
            {
                return new NativeVideoPathSelectionObservationView
                {
                    Available = false,
                    Kind = "Unavailable",
                    SourceMemoryKind = "Unavailable",
                    PresentedMemoryKind = "Unavailable",
                };
            }

            return new NativeVideoPathSelectionObservationView
            {
                Available = true,
                Kind = selection.Kind.ToString(),
                SourceMemoryKind = selection.SourceMemoryKind.ToString(),
                PresentedMemoryKind = selection.PresentedMemoryKind.ToString(),
                TargetZeroCopy = selection.TargetZeroCopy,
                SourcePlaneTexturesSupported = selection.SourcePlaneTexturesSupported,
                CpuFallback = selection.CpuFallback,
            };
        }

        internal static AudioOutputPolicyAuditStringsView CreateAudioOutputPolicyAuditStrings(
            bool available,
            AudioOutputPolicyView policy)
        {
            var observation = CreateAudioOutputPolicyObservation(available, policy);
            return new AudioOutputPolicyAuditStringsView
            {
                Available = observation.Available,
                FileStartThresholdMilliseconds = observation.Available
                    ? observation.FileStartThresholdMilliseconds.ToString()
                    : "n/a",
                AndroidFileStartThresholdMilliseconds = observation.Available
                    ? observation.AndroidFileStartThresholdMilliseconds.ToString()
                    : "n/a",
                RealtimeStartThresholdMilliseconds = observation.Available
                    ? observation.RealtimeStartThresholdMilliseconds.ToString()
                    : "n/a",
                RealtimeStartupGraceMilliseconds = observation.Available
                    ? observation.RealtimeStartupGraceMilliseconds.ToString()
                    : "n/a",
                RealtimeStartupMinimumThresholdMilliseconds = observation.Available
                    ? observation.RealtimeStartupMinimumThresholdMilliseconds.ToString()
                    : "n/a",
                FileRingCapacityMilliseconds = observation.Available
                    ? observation.FileRingCapacityMilliseconds.ToString()
                    : "n/a",
                AndroidFileRingCapacityMilliseconds = observation.Available
                    ? observation.AndroidFileRingCapacityMilliseconds.ToString()
                    : "n/a",
                RealtimeRingCapacityMilliseconds = observation.Available
                    ? observation.RealtimeRingCapacityMilliseconds.ToString()
                    : "n/a",
                FileBufferedCeilingMilliseconds = observation.Available
                    ? observation.FileBufferedCeilingMilliseconds.ToString()
                    : "n/a",
                AndroidFileBufferedCeilingMilliseconds = observation.Available
                    ? observation.AndroidFileBufferedCeilingMilliseconds.ToString()
                    : "n/a",
                RealtimeBufferedCeilingMilliseconds = observation.Available
                    ? observation.RealtimeBufferedCeilingMilliseconds.ToString()
                    : "n/a",
                RealtimeStartupAdditionalSinkDelayMilliseconds = observation.Available
                    ? observation.RealtimeStartupAdditionalSinkDelayMilliseconds.ToString()
                    : "n/a",
                RealtimeSteadyAdditionalSinkDelayMilliseconds = observation.Available
                    ? observation.RealtimeSteadyAdditionalSinkDelayMilliseconds.ToString()
                    : "n/a",
                RealtimeBackendAdditionalSinkDelayMilliseconds = observation.Available
                    ? observation.RealtimeBackendAdditionalSinkDelayMilliseconds.ToString()
                    : "n/a",
                RealtimeStartRequiresVideoFrame =
                    observation.RealtimeStartRequiresVideoFrame.ToString(),
                AllowAndroidFileOutputRateBridge =
                    observation.AllowAndroidFileOutputRateBridge.ToString(),
            };
        }

        internal static PlayerSessionObservationView CreatePlayerSessionObservation(
            bool available,
            PlayerSessionContractView contract)
        {
            if (!available)
            {
                return new PlayerSessionObservationView
                {
                    Available = false,
                    LifecycleState = "Unavailable",
                    PublicState = -1,
                    RuntimeState = -1,
                    PlaybackIntent = -1,
                    StopReason = -1,
                    SourceState = "Unavailable",
                    CanSeek = false,
                    IsRealtime = false,
                    IsBuffering = false,
                    IsSyncing = false,
                    AudioStartStateReported = false,
                    ShouldStartAudio = false,
                    AudioStartBlockReason = -1,
                    RequiredBufferedSamples = 0,
                    ReportedBufferedSamples = 0,
                    RequiresPresentedVideoFrame = false,
                    HasPresentedVideoFrame = false,
                    AndroidFileRateBridgeActive = false,
                };
            }

            return new PlayerSessionObservationView
            {
                Available = true,
                LifecycleState = FormatPlayerSessionLifecycleState(contract.LifecycleState),
                PublicState = contract.PublicState,
                RuntimeState = contract.RuntimeState,
                PlaybackIntent = contract.PlaybackIntent,
                StopReason = contract.StopReason,
                SourceState = contract.SourceConnectionState.ToString(),
                CanSeek = contract.CanSeek,
                IsRealtime = contract.IsRealtime,
                IsBuffering = contract.IsBuffering,
                IsSyncing = contract.IsSyncing,
                AudioStartStateReported = contract.AudioStartStateReported,
                ShouldStartAudio = contract.ShouldStartAudio,
                AudioStartBlockReason = contract.AudioStartBlockReason,
                RequiredBufferedSamples = contract.RequiredBufferedSamples,
                ReportedBufferedSamples = contract.ReportedBufferedSamples,
                RequiresPresentedVideoFrame = contract.RequiresPresentedVideoFrame,
                HasPresentedVideoFrame = contract.HasPresentedVideoFrame,
                AndroidFileRateBridgeActive = contract.AndroidFileRateBridgeActive,
            };
        }

        internal static PlayerSessionAuditStringsView CreatePlayerSessionAuditStrings(
            bool available,
            PlayerSessionContractView contract)
        {
            if (!available)
            {
                return new PlayerSessionAuditStringsView
                {
                    Available = false,
                    LifecycleState = "n/a",
                    PublicState = "n/a",
                    RuntimeState = "n/a",
                    PlaybackIntent = "n/a",
                    StopReason = "n/a",
                    SourceState = "n/a",
                    CanSeek = "False",
                    IsRealtime = "False",
                    IsBuffering = "False",
                    IsSyncing = "False",
                };
            }

            return new PlayerSessionAuditStringsView
            {
                Available = true,
                LifecycleState = FormatPlayerSessionLifecycleState(contract.LifecycleState),
                PublicState = FormatPlayerSessionState(contract.PublicState),
                RuntimeState = FormatPlayerSessionState(contract.RuntimeState),
                PlaybackIntent = FormatPlayerSessionPlaybackIntent(contract.PlaybackIntent),
                StopReason = FormatPlayerSessionStopReason(contract.StopReason),
                SourceState = contract.SourceConnectionState.ToString(),
                CanSeek = contract.CanSeek.ToString(),
                IsRealtime = contract.IsRealtime.ToString(),
                IsBuffering = contract.IsBuffering.ToString(),
                IsSyncing = contract.IsSyncing.ToString(),
            };
        }

        internal static PlayerSessionAuditStringsView CreatePlayerSessionAuditStringsFallback(
            bool available,
            string lifecycleState,
            string publicState,
            string runtimeState,
            string playbackIntent,
            string stopReason,
            string sourceState,
            string canSeek,
            string isRealtime,
            string isBuffering,
            string isSyncing)
        {
            return new PlayerSessionAuditStringsView
            {
                Available = available,
                LifecycleState = lifecycleState,
                PublicState = publicState,
                RuntimeState = runtimeState,
                PlaybackIntent = playbackIntent,
                StopReason = stopReason,
                SourceState = sourceState,
                CanSeek = canSeek,
                IsRealtime = isRealtime,
                IsBuffering = isBuffering,
                IsSyncing = isSyncing,
            };
        }

        internal static PlayerSessionAuditStringsView CreateDefaultPlayerSessionAuditStrings()
        {
            return CreatePlayerSessionAuditStringsFallback(
                false,
                "n/a",
                "n/a",
                "n/a",
                "n/a",
                "n/a",
                "n/a",
                "False",
                "False",
                "False",
                "False");
        }

        internal static PlayerSessionAuditStringsView CreateResolvedPlayerSessionAuditStrings(
            string observedLifecycleState,
            string observedPublicState,
            string observedRuntimeState,
            string observedPlaybackIntent,
            string observedStopReason,
            string observedSourceState,
            string observedCanSeek,
            string observedIsRealtime,
            string observedIsBuffering,
            string observedIsSyncing,
            PlayerSessionAuditStringsView fallback)
        {
            return new PlayerSessionAuditStringsView
            {
                Available = fallback.Available,
                LifecycleState = ResolveAuditString(observedLifecycleState, fallback.LifecycleState),
                PublicState = ResolveAuditString(observedPublicState, fallback.PublicState),
                RuntimeState = ResolveAuditString(observedRuntimeState, fallback.RuntimeState),
                PlaybackIntent = ResolveAuditString(observedPlaybackIntent, fallback.PlaybackIntent),
                StopReason = ResolveAuditString(observedStopReason, fallback.StopReason),
                SourceState = ResolveAuditString(observedSourceState, fallback.SourceState),
                CanSeek = ResolveAuditString(observedCanSeek, fallback.CanSeek),
                IsRealtime = ResolveAuditString(observedIsRealtime, fallback.IsRealtime),
                IsBuffering = ResolveAuditString(observedIsBuffering, fallback.IsBuffering),
                IsSyncing = ResolveAuditString(observedIsSyncing, fallback.IsSyncing),
            };
        }

        internal static SourceTimelineObservationView CreateSourceTimelineObservation(
            bool available,
            SourceTimelineContractView contract)
        {
            if (!available)
            {
                return new SourceTimelineObservationView
                {
                    Available = false,
                    Model = "Unavailable",
                    AnchorKind = "Unavailable",
                    HasCurrentSourceTimeUs = false,
                    CurrentSourceTimeUs = 0,
                    HasTimelineOriginUs = false,
                    TimelineOriginUs = 0,
                    HasAnchorValueUs = false,
                    AnchorValueUs = 0,
                    HasAnchorMonoUs = false,
                    AnchorMonoUs = 0,
                    IsRealtime = false,
                };
            }

            return new SourceTimelineObservationView
            {
                Available = true,
                Model = FormatSourceTimelineModel(contract.Model),
                AnchorKind = FormatSourceTimelineAnchorKind(contract.AnchorKind),
                HasCurrentSourceTimeUs = contract.HasCurrentSourceTimeUs,
                CurrentSourceTimeUs = contract.CurrentSourceTimeUs,
                HasTimelineOriginUs = contract.HasTimelineOriginUs,
                TimelineOriginUs = contract.TimelineOriginUs,
                HasAnchorValueUs = contract.HasAnchorValueUs,
                AnchorValueUs = contract.AnchorValueUs,
                HasAnchorMonoUs = contract.HasAnchorMonoUs,
                AnchorMonoUs = contract.AnchorMonoUs,
                IsRealtime = contract.IsRealtime,
            };
        }

        internal static SourceTimelineAuditStringsView CreateSourceTimelineAuditStrings(
            bool available,
            SourceTimelineContractView contract)
        {
            var observation = CreateSourceTimelineObservation(available, contract);
            if (!observation.Available)
            {
                return new SourceTimelineAuditStringsView
                {
                    Available = false,
                    Model = "n/a",
                    AnchorKind = "n/a",
                    HasCurrentSourceTimeUs = "False",
                    CurrentSourceTimeUs = "n/a",
                    HasTimelineOriginUs = "False",
                    TimelineOriginUs = "n/a",
                    HasAnchorValueUs = "False",
                    AnchorValueUs = "n/a",
                    HasAnchorMonoUs = "False",
                    AnchorMonoUs = "n/a",
                    IsRealtime = "False",
                };
            }

            return new SourceTimelineAuditStringsView
            {
                Available = true,
                Model = observation.Model,
                AnchorKind = observation.AnchorKind,
                HasCurrentSourceTimeUs = observation.HasCurrentSourceTimeUs.ToString(),
                CurrentSourceTimeUs = observation.CurrentSourceTimeUs.ToString(),
                HasTimelineOriginUs = observation.HasTimelineOriginUs.ToString(),
                TimelineOriginUs = observation.TimelineOriginUs.ToString(),
                HasAnchorValueUs = observation.HasAnchorValueUs.ToString(),
                AnchorValueUs = observation.AnchorValueUs.ToString(),
                HasAnchorMonoUs = observation.HasAnchorMonoUs.ToString(),
                AnchorMonoUs = observation.AnchorMonoUs.ToString(),
                IsRealtime = observation.IsRealtime.ToString(),
            };
        }

        internal static AvSyncEnterpriseObservationView CreateAvSyncEnterpriseObservation(
            bool available,
            AvSyncEnterpriseMetricsView metrics)
        {
            if (!available)
            {
                return default(AvSyncEnterpriseObservationView);
            }

            return new AvSyncEnterpriseObservationView
            {
                Available = true,
                SampleCount = metrics.SampleCount,
                WindowSpanUs = metrics.WindowSpanUs,
                LatestRawOffsetUs = metrics.LatestRawOffsetUs,
                LatestSmoothOffsetUs = metrics.LatestSmoothOffsetUs,
                DriftSlopePpm = metrics.DriftSlopePpm,
                DriftProjected2hMs = metrics.DriftProjected2hMs,
                OffsetAbsP95Us = metrics.OffsetAbsP95Us,
                OffsetAbsP99Us = metrics.OffsetAbsP99Us,
                OffsetAbsMaxUs = metrics.OffsetAbsMaxUs,
            };
        }

        internal static AvSyncEnterpriseAuditStringsView CreateAvSyncEnterpriseAuditStrings(
            bool available,
            AvSyncEnterpriseMetricsView metrics)
        {
            var observation = CreateAvSyncEnterpriseObservation(available, metrics);
            if (!observation.Available)
            {
                return new AvSyncEnterpriseAuditStringsView
                {
                    Available = false,
                    SampleCount = "n/a",
                    WindowSpanUs = "n/a",
                    LatestRawOffsetUs = "n/a",
                    LatestSmoothOffsetUs = "n/a",
                    DriftSlopePpm = "n/a",
                    DriftProjected2hMs = "n/a",
                    OffsetAbsP95Us = "n/a",
                    OffsetAbsP99Us = "n/a",
                    OffsetAbsMaxUs = "n/a",
                };
            }

            return new AvSyncEnterpriseAuditStringsView
            {
                Available = true,
                SampleCount = observation.SampleCount.ToString(),
                WindowSpanUs = observation.WindowSpanUs.ToString(),
                LatestRawOffsetUs = observation.LatestRawOffsetUs.ToString(),
                LatestSmoothOffsetUs = observation.LatestSmoothOffsetUs.ToString(),
                DriftSlopePpm = observation.DriftSlopePpm.ToString("F3"),
                DriftProjected2hMs = observation.DriftProjected2hMs.ToString("F3"),
                OffsetAbsP95Us = observation.OffsetAbsP95Us.ToString(),
                OffsetAbsP99Us = observation.OffsetAbsP99Us.ToString(),
                OffsetAbsMaxUs = observation.OffsetAbsMaxUs.ToString(),
            };
        }

        internal static WgpuRenderDescriptorObservationView CreateWgpuRenderDescriptorObservation(
            bool available,
            WgpuRenderDescriptorView descriptor)
        {
            if (!available)
            {
                return default(WgpuRenderDescriptorObservationView);
            }

            return new WgpuRenderDescriptorObservationView
            {
                Available = true,
                RuntimeReady = descriptor.RuntimeReady,
                OutputWidth = descriptor.OutputWidth,
                OutputHeight = descriptor.OutputHeight,
                SupportsYuv420p = descriptor.SupportsYuv420p,
                SupportsNv12 = descriptor.SupportsNv12,
                SupportsP010 = descriptor.SupportsP010,
                SupportsRgba32 = descriptor.SupportsRgba32,
                SupportsExternalTextureRgba = descriptor.SupportsExternalTextureRgba,
                SupportsExternalTextureYu12 = descriptor.SupportsExternalTextureYu12,
                ReadbackExportSupported = descriptor.ReadbackExportSupported,
            };
        }

        internal static PassiveAvSyncObservationView CreatePassiveAvSyncObservation(
            bool available,
            PassiveAvSyncSnapshotView snapshot)
        {
            if (!available)
            {
                return new PassiveAvSyncObservationView
                {
                    Available = false,
                    VideoSchedule = "Unavailable",
                    AudioResampleRatio = 1.0,
                };
            }

            return new PassiveAvSyncObservationView
            {
                Available = true,
                RawOffsetUs = snapshot.RawOffsetUs,
                SmoothOffsetUs = snapshot.SmoothOffsetUs,
                DriftPpm = snapshot.DriftPpm,
                DriftInterceptUs = snapshot.DriftInterceptUs,
                DriftSampleCount = snapshot.DriftSampleCount,
                VideoSchedule = snapshot.VideoSchedule,
                AudioResampleRatio = snapshot.AudioResampleRatio,
                AudioResampleActive = snapshot.AudioResampleActive,
                ShouldRebuildAnchor = snapshot.ShouldRebuildAnchor,
            };
        }

        internal static WgpuRenderStateObservationView CreateWgpuRenderStateObservation(
            bool available,
            WgpuRenderStateView state)
        {
            if (!available)
            {
                return new WgpuRenderStateObservationView
                {
                    Available = false,
                    RenderPath = "Unavailable",
                    SourceMemoryKind = "Unavailable",
                    PresentedMemoryKind = "Unavailable",
                    SourcePixelFormat = "Unavailable",
                    PresentedPixelFormat = "Unavailable",
                    ExternalTextureFormat = "Unavailable",
                    RenderErrorKind = "Unavailable",
                };
            }

            return new WgpuRenderStateObservationView
            {
                Available = true,
                RenderPath = state.RenderPath.ToString(),
                SourceMemoryKind = state.SourceMemoryKind.ToString(),
                PresentedMemoryKind = state.PresentedMemoryKind.ToString(),
                SourcePixelFormat = state.SourcePixelFormat.ToString(),
                PresentedPixelFormat = state.PresentedPixelFormat.ToString(),
                ExternalTextureFormat = state.ExternalTextureFormat.ToString(),
                HasRenderedFrame = state.HasRenderedFrame,
                RenderedFrameIndex = state.RenderedFrameIndex,
                RenderedTimeSec = state.RenderedTimeSec,
                HasRenderError = state.HasRenderError,
                RenderErrorKind = state.RenderErrorKind.ToString(),
                UploadPlaneCount = state.UploadPlaneCount,
                SourceZeroCopy = state.SourceZeroCopy,
                CpuFallback = state.CpuFallback,
            };
        }

        internal static PassiveAvSyncAuditStringsView CreatePassiveAvSyncAuditStrings(
            bool available,
            PassiveAvSyncSnapshotView snapshot)
        {
            if (!available)
            {
                return new PassiveAvSyncAuditStringsView
                {
                    Available = false,
                    RawOffsetUs = "n/a",
                    SmoothOffsetUs = "n/a",
                    DriftPpm = "n/a",
                    DriftInterceptUs = "n/a",
                    DriftSampleCount = "n/a",
                    VideoSchedule = "n/a",
                    AudioResampleRatio = "n/a",
                    AudioResampleActive = "False",
                    ShouldRebuildAnchor = "False",
                };
            }

            return new PassiveAvSyncAuditStringsView
            {
                Available = true,
                RawOffsetUs = snapshot.RawOffsetUs.ToString(),
                SmoothOffsetUs = snapshot.SmoothOffsetUs.ToString(),
                DriftPpm = snapshot.DriftPpm.ToString("F3"),
                DriftInterceptUs = snapshot.DriftInterceptUs.ToString(),
                DriftSampleCount = snapshot.DriftSampleCount.ToString(),
                VideoSchedule = snapshot.VideoSchedule ?? "Unavailable",
                AudioResampleRatio = snapshot.AudioResampleRatio.ToString("F6"),
                AudioResampleActive = snapshot.AudioResampleActive.ToString(),
                ShouldRebuildAnchor = snapshot.ShouldRebuildAnchor.ToString(),
            };
        }

        internal static bool RefreshAudioOutputPolicyState(
            GetAudioOutputPolicyDelegate getAudioOutputPolicy,
            int playerId,
            ref AudioOutputPolicyView policy,
            ref bool hasPolicy,
            ref bool missingLogged,
            Action<AudioOutputPolicyView> onLoaded)
        {
            if (TryReadAudioOutputPolicy(getAudioOutputPolicy, playerId, out var loadedPolicy))
            {
                policy = loadedPolicy;
                hasPolicy = true;
                missingLogged = false;
                onLoaded?.Invoke(loadedPolicy);
                return true;
            }

            ResetAudioOutputPolicyState(
                ref policy,
                ref hasPolicy,
                ref missingLogged);
            return false;
        }

        internal static bool TryGetRequiredAudioOutputPolicy(
            GetAudioOutputPolicyDelegate getAudioOutputPolicy,
            string logPrefix,
            string operation,
            int playerId,
            bool playerIdValid,
            bool isRealtimeSource,
            bool playRequested,
            ref AudioOutputPolicyView cachedPolicy,
            ref bool hasCachedPolicy,
            ref bool missingLogged,
            out AudioOutputPolicyView policy,
            Action<AudioOutputPolicyView> onLoaded)
        {
            policy = default(AudioOutputPolicyView);
            if (!hasCachedPolicy && playerIdValid)
            {
                RefreshAudioOutputPolicyState(
                    getAudioOutputPolicy,
                    playerId,
                    ref cachedPolicy,
                    ref hasCachedPolicy,
                    ref missingLogged,
                    onLoaded);
            }

            if (hasCachedPolicy)
            {
                policy = cachedPolicy;
                return true;
            }

            if (!missingLogged)
            {
                Debug.LogWarning(
                    "[" + logPrefix + "] audio_output_policy_missing"
                    + " operation=" + operation
                    + " player_id=" + playerId
                    + " is_realtime=" + isRealtimeSource
                    + " play_requested=" + playRequested);
                missingLogged = true;
            }

            return false;
        }

        internal static bool TryReadSourceTimelineContract(
            GetSourceTimelineContractDelegate getSourceTimelineContract,
            int playerId,
            out SourceTimelineContractView contract)
        {
            contract = default(SourceTimelineContractView);

            try
            {
                var nativeContract = CreateSourceTimelineContract();
                var result = getSourceTimelineContract(playerId, ref nativeContract);
                if (result < 0)
                {
                    return false;
                }

                contract = NormalizeSourceTimelineContract(nativeContract);
                return true;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
            catch (DllNotFoundException)
            {
                return false;
            }
        }

        internal static bool TryReadPlayerSessionContract(
            GetPlayerSessionContractDelegate getPlayerSessionContract,
            int playerId,
            out PlayerSessionContractView contract)
        {
            contract = default(PlayerSessionContractView);

            try
            {
                var nativeContract = CreatePlayerSessionContract();
                var result = getPlayerSessionContract(playerId, ref nativeContract);
                if (result < 0)
                {
                    return false;
                }

                contract = NormalizePlayerSessionContract(nativeContract);
                return true;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
            catch (DllNotFoundException)
            {
                return false;
            }
        }

        internal static bool TryReportAudioStartupState(
            ReportAudioStartupStateDelegate reportAudioStartupState,
            int playerId,
            AudioStartupObservationView observation)
        {
            try
            {
                var result = reportAudioStartupState(
                    playerId,
                    observation.AudioSampleRate,
                    observation.AudioChannels,
                    observation.BufferedSamples,
                    observation.StartupElapsedMilliseconds,
                    observation.HasPresentedVideoFrame,
                    observation.RequiresPresentedVideoFrame,
                    observation.AndroidFileRateBridgeActive);
                return result >= 0;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
            catch (DllNotFoundException)
            {
                return false;
            }
        }

        internal static bool TryReadAvSyncEnterpriseMetrics(
            GetAvSyncEnterpriseMetricsDelegate getAvSyncEnterpriseMetrics,
            int playerId,
            out AvSyncEnterpriseMetricsView metrics)
        {
            metrics = default(AvSyncEnterpriseMetricsView);

            try
            {
                var nativeMetrics = CreateAvSyncEnterpriseMetrics();
                var result = getAvSyncEnterpriseMetrics(playerId, ref nativeMetrics);
                if (result < 0)
                {
                    return false;
                }

                metrics = NormalizeAvSyncEnterpriseMetrics(nativeMetrics);
                return true;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
            catch (DllNotFoundException)
            {
                return false;
            }
        }

        internal static bool TryReadPassiveAvSyncSnapshot(
            GetPassiveAvSyncSnapshotDelegate getPassiveAvSyncSnapshot,
            int playerId,
            out PassiveAvSyncSnapshotView snapshot)
        {
            snapshot = default(PassiveAvSyncSnapshotView);

            try
            {
                var nativeSnapshot = CreatePassiveAvSyncSnapshot();
                var result = getPassiveAvSyncSnapshot(playerId, ref nativeSnapshot);
                if (result <= 0)
                {
                    return false;
                }

                snapshot = NormalizePassiveAvSyncSnapshot(nativeSnapshot);
                return true;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
            catch (DllNotFoundException)
            {
                return false;
            }
        }

        internal static RustAVPlayerOpenOptions CreateOpenOptions(
            MediaBackendKind preferredBackend,
            bool strictBackend)
        {
            return new RustAVPlayerOpenOptions
            {
                StructSize = (uint)Marshal.SizeOf(typeof(RustAVPlayerOpenOptions)),
                StructVersion = RustAVPlayerOpenOptionsVersion,
                BackendKind = (int)preferredBackend,
                StrictBackend = strictBackend ? 1 : 0,
            };
        }

        internal static RustAVPlayerSessionOpenOptions CreateSessionOpenOptions(
            MediaBackendKind preferredBackend,
            bool strictBackend,
            RustAVPlayerSessionOutputKind outputKind,
            int targetWidth,
            int targetHeight,
            uint outputFlags = PlayerSessionOpenFlagNone,
            IntPtr targetTexture = default,
            IntPtr nativeVideoTarget = default)
        {
            return new RustAVPlayerSessionOpenOptions
            {
                StructSize = (uint)Marshal.SizeOf(typeof(RustAVPlayerSessionOpenOptions)),
                StructVersion = RustAVPlayerSessionOpenOptionsVersion,
                OutputKind = (int)outputKind,
                BackendKind = (int)preferredBackend,
                StrictBackend = strictBackend ? 1 : 0,
                TargetWidth = targetWidth,
                TargetHeight = targetHeight,
                OutputFlags = outputFlags,
                TargetTexture = targetTexture,
                NativeVideoTarget = nativeVideoTarget,
            };
        }

        internal static void EnsureValidPlayerSessionId(int playerId, string playerTypeName)
        {
            if (playerId < 0)
            {
                throw new InvalidOperationException(
                    playerTypeName + " has no underlying valid native player.");
            }
        }

        internal static void PlayPlayerSession(int playerId, string playerTypeName)
        {
            EnsureValidPlayerSessionId(playerId, playerTypeName);
            ThrowLifecycleCommandFailure("play", PlayPlayerNative(playerId));
        }

        internal static void PreparePlayerSession(int playerId, string playerTypeName)
        {
            EnsureValidPlayerSessionId(playerId, playerTypeName);
            ThrowLifecycleCommandFailure("prepare", PreparePlayerNative(playerId));
        }

        internal static void PausePlayerSession(int playerId, string playerTypeName)
        {
            EnsureValidPlayerSessionId(playerId, playerTypeName);
            ThrowLifecycleCommandFailure("pause", PausePlayerNative(playerId));
        }

        internal static void StopPlayerSession(int playerId, string playerTypeName)
        {
            EnsureValidPlayerSessionId(playerId, playerTypeName);
            ThrowLifecycleCommandFailure("stop", StopPlayerNative(playerId));
        }

        internal static void SeekPlayerSession(
            int playerId,
            double time,
            string playerTypeName)
        {
            EnsureValidPlayerSessionId(playerId, playerTypeName);
            ThrowLifecycleCommandFailure("seek", SeekPlayerNative(playerId, time));
        }

        internal static int ClosePlayerSession(int playerId, string playerTypeName)
        {
            EnsureValidPlayerSessionId(playerId, playerTypeName);
            return ClosePlayerNative(playerId);
        }

        internal static void ClosePlayerSessionSilently(int playerId)
        {
            try
            {
                if (playerId >= 0)
                {
                    ClosePlayerNative(playerId);
                }
            }
            catch
            {
            }
        }

        internal static void ThrowLifecycleCommandFailure(string action, int result)
        {
            if (result < 0)
            {
                throw new Exception($"Failed to {action} with error {result}");
            }
        }

        internal static RustAVPlayerHealthSnapshotV2 CreateHealthSnapshot()
        {
            return new RustAVPlayerHealthSnapshotV2
            {
                StructSize = (uint)Marshal.SizeOf(typeof(RustAVPlayerHealthSnapshotV2)),
                StructVersion = RustAVPlayerHealthSnapshotV2Version,
            };
        }

        internal static RustAVNativeVideoTarget CreateNativeVideoTarget(
            NativeVideoPlatformKind platformKind,
            NativeVideoSurfaceKind surfaceKind,
            IntPtr targetHandle,
            IntPtr auxiliaryHandle,
            int width,
            int height,
            uint extraFlags = NativeVideoTargetFlagNone)
        {
            var flags = NativeVideoTargetFlagExternalTexture;
            if (auxiliaryHandle != IntPtr.Zero)
            {
                flags |= NativeVideoTargetFlagUnityOwnedTexture;
            }
            flags |= extraFlags;

            return new RustAVNativeVideoTarget
            {
                StructSize = (uint)Marshal.SizeOf(typeof(RustAVNativeVideoTarget)),
                StructVersion = RustAVNativeVideoTargetVersion,
                PlatformKind = (int)platformKind,
                SurfaceKind = (int)surfaceKind,
                TargetHandle = unchecked((ulong)targetHandle.ToInt64()),
                AuxiliaryHandle = unchecked((ulong)auxiliaryHandle.ToInt64()),
                Width = width,
                Height = height,
                PixelFormat = (int)NativeVideoPixelFormat.Rgba32,
                Flags = flags,
            };
        }

        internal static RustAVNativeVideoTarget CreateDefaultNativeVideoTarget(
            IntPtr targetHandle,
            IntPtr auxiliaryHandle,
            int width,
            int height,
            NativeVideoSurfaceKind preferredSurfaceKind = NativeVideoSurfaceKind.Unknown,
            uint extraFlags = NativeVideoTargetFlagNone)
        {
            var platformKind = DetectNativeVideoPlatformKind();
            return CreateNativeVideoTarget(
                platformKind,
                ResolvePreferredNativeVideoSurfaceKind(platformKind, preferredSurfaceKind),
                targetHandle,
                auxiliaryHandle,
                width,
                height,
                extraFlags);
        }

        internal static RustAVNativeVideoInteropCaps CreateNativeVideoInteropCaps()
        {
            return new RustAVNativeVideoInteropCaps
            {
                StructSize = (uint)Marshal.SizeOf(typeof(RustAVNativeVideoInteropCaps)),
                StructVersion = RustAVNativeVideoInteropCapsVersion,
            };
        }

        internal static RustAVNativeVideoBridgeDescriptor CreateNativeVideoBridgeDescriptor()
        {
            return new RustAVNativeVideoBridgeDescriptor
            {
                StructSize = (uint)Marshal.SizeOf(typeof(RustAVNativeVideoBridgeDescriptor)),
                StructVersion = RustAVNativeVideoBridgeDescriptorVersion,
            };
        }

        internal static RustAVNativeVideoPathSelection CreateNativeVideoPathSelection()
        {
            return new RustAVNativeVideoPathSelection
            {
                StructSize = (uint)Marshal.SizeOf(typeof(RustAVNativeVideoPathSelection)),
                StructVersion = RustAVNativeVideoPathSelectionVersion,
            };
        }

        internal static RustAVWgpuRenderDescriptor CreateWgpuRenderDescriptor()
        {
            return new RustAVWgpuRenderDescriptor
            {
                StructSize = (uint)Marshal.SizeOf(typeof(RustAVWgpuRenderDescriptor)),
                StructVersion = RustAVWgpuRenderDescriptorVersion,
            };
        }

        internal static RustAVWgpuRenderStateView CreateWgpuRenderStateView()
        {
            return new RustAVWgpuRenderStateView
            {
                StructSize = (uint)Marshal.SizeOf(typeof(RustAVWgpuRenderStateView)),
                StructVersion = RustAVWgpuRenderStateViewVersion,
            };
        }

        internal static RustAVVideoColorInfo CreateVideoColorInfo()
        {
            return new RustAVVideoColorInfo
            {
                StructSize = (uint)Marshal.SizeOf(typeof(RustAVVideoColorInfo)),
                StructVersion = RustAVVideoColorInfoVersion,
            };
        }

        internal static RustAVVideoFrameContract CreateVideoFrameContract()
        {
            return new RustAVVideoFrameContract
            {
                StructSize = (uint)Marshal.SizeOf(typeof(RustAVVideoFrameContract)),
                StructVersion = RustAVVideoFrameContractVersion,
            };
        }

        internal static RustAVPlaybackTimingContract CreatePlaybackTimingContract()
        {
            return new RustAVPlaybackTimingContract
            {
                StructSize = (uint)Marshal.SizeOf(typeof(RustAVPlaybackTimingContract)),
                StructVersion = RustAVPlaybackTimingContractVersion,
            };
        }

        internal static RustAVAvSyncContract CreateAvSyncContract()
        {
            return new RustAVAvSyncContract
            {
                StructSize = (uint)Marshal.SizeOf(typeof(RustAVAvSyncContract)),
                StructVersion = RustAVAvSyncContractVersion,
            };
        }

        internal static RustAVAudioOutputPolicy CreateAudioOutputPolicy()
        {
            return new RustAVAudioOutputPolicy
            {
                StructSize = (uint)Marshal.SizeOf(typeof(RustAVAudioOutputPolicy)),
                StructVersion = RustAVAudioOutputPolicyVersion,
            };
        }

        internal static RustAVSourceTimelineContract CreateSourceTimelineContract()
        {
            return new RustAVSourceTimelineContract
            {
                StructSize = (uint)Marshal.SizeOf(typeof(RustAVSourceTimelineContract)),
                StructVersion = RustAVSourceTimelineContractVersion,
            };
        }

        internal static RustAVPlayerSessionContract CreatePlayerSessionContract()
        {
            return new RustAVPlayerSessionContract
            {
                StructSize = (uint)Marshal.SizeOf(typeof(RustAVPlayerSessionContract)),
                StructVersion = RustAVPlayerSessionContractVersion,
            };
        }

        internal static RustAVAvSyncEnterpriseMetrics CreateAvSyncEnterpriseMetrics()
        {
            return new RustAVAvSyncEnterpriseMetrics
            {
                StructSize = (uint)Marshal.SizeOf(typeof(RustAVAvSyncEnterpriseMetrics)),
                StructVersion = RustAVAvSyncEnterpriseMetricsVersion,
            };
        }

        internal static RustAVPassiveAvSyncSnapshot CreatePassiveAvSyncSnapshot()
        {
            return new RustAVPassiveAvSyncSnapshot
            {
                StructSize = (uint)Marshal.SizeOf(typeof(RustAVPassiveAvSyncSnapshot)),
                StructVersion = RustAVPassiveAvSyncSnapshotVersion,
            };
        }

        internal static RustAVNativeVideoFrame CreateNativeVideoFrame()
        {
            return new RustAVNativeVideoFrame
            {
                StructSize = (uint)Marshal.SizeOf(typeof(RustAVNativeVideoFrame)),
                StructVersion = RustAVNativeVideoFrameVersion,
            };
        }

        internal static RustAVNativeVideoPlaneTextures CreateNativeVideoPlaneTextures()
        {
            return new RustAVNativeVideoPlaneTextures
            {
                StructSize = (uint)Marshal.SizeOf(typeof(RustAVNativeVideoPlaneTextures)),
                StructVersion = RustAVNativeVideoPlaneTexturesVersion,
            };
        }

        internal static RustAVNativeVideoPlaneViews CreateNativeVideoPlaneViews()
        {
            return new RustAVNativeVideoPlaneViews
            {
                StructSize = (uint)Marshal.SizeOf(typeof(RustAVNativeVideoPlaneViews)),
                StructVersion = RustAVNativeVideoPlaneViewsVersion,
            };
        }

        internal static MediaBackendKind NormalizeBackendKind(
            int rawValue,
            MediaBackendKind fallback)
        {
            switch (rawValue)
            {
                case 1:
                    return MediaBackendKind.Ffmpeg;
                case 2:
                    return MediaBackendKind.Gstreamer;
                case 0:
                    return MediaBackendKind.Auto;
                default:
                    return fallback;
            }
        }

        internal static MediaSourceConnectionState NormalizeSourceConnectionState(int rawValue)
        {
            switch (rawValue)
            {
                case 0:
                    return MediaSourceConnectionState.Disconnected;
                case 1:
                    return MediaSourceConnectionState.Connecting;
                case 2:
                    return MediaSourceConnectionState.Connected;
                case 3:
                    return MediaSourceConnectionState.Reconnecting;
                case 4:
                    return MediaSourceConnectionState.Checking;
                default:
                    return MediaSourceConnectionState.Unknown;
            }
        }

        internal static string ReadBackendRuntimeDiagnostic(
            BackendRuntimeDiagnosticDelegate getBackendRuntimeDiagnostic,
            MediaBackendKind preferredBackend,
            string uri,
            bool requireAudioExport)
        {
            if (string.IsNullOrEmpty(uri))
            {
                return string.Empty;
            }

            try
            {
                var buffer = new StringBuilder(BackendDiagnosticBufferLength);
                var result = getBackendRuntimeDiagnostic(
                    (int)preferredBackend,
                    uri,
                    requireAudioExport,
                    buffer,
                    buffer.Capacity);
                if (result >= 0 && buffer.Length > 0)
                {
                    return buffer.ToString();
                }
            }
            catch (EntryPointNotFoundException)
            {
                return string.Empty;
            }
            catch (DllNotFoundException)
            {
                return string.Empty;
            }

            return string.Empty;
        }

        internal static bool TryReadNativeVideoInteropCaps(
            GetNativeVideoInteropCapsDelegate getNativeVideoInteropCaps,
            MediaBackendKind preferredBackend,
            string uri,
            ref RustAVNativeVideoTarget target,
            out NativeVideoInteropCapsView caps)
        {
            caps = default(NativeVideoInteropCapsView);

            try
            {
                var nativeCaps = CreateNativeVideoInteropCaps();
                var result = getNativeVideoInteropCaps(
                    (int)preferredBackend,
                    uri,
                    ref target,
                    ref nativeCaps);
                if (result < 0)
                {
                    return false;
                }

                caps = new NativeVideoInteropCapsView
                {
                    BackendKind = NormalizeBackendKind(nativeCaps.BackendKind, preferredBackend),
                    PlatformKind = NormalizeNativeVideoPlatformKind(nativeCaps.PlatformKind),
                    SurfaceKind = NormalizeNativeVideoSurfaceKind(nativeCaps.SurfaceKind),
                    Supported = nativeCaps.Supported != 0,
                    ContractTargetSupported =
                        (nativeCaps.Flags & NativeVideoCapFlagContractTargetSupported) != 0,
                    HardwareDecodeSupported = nativeCaps.HardwareDecodeSupported != 0,
                    ZeroCopySupported = nativeCaps.ZeroCopySupported != 0,
                    SourceSurfaceZeroCopySupported =
                        (nativeCaps.Flags & NativeVideoCapFlagSourceSurfaceZeroCopy) != 0,
                    ExternalTextureTarget =
                        (nativeCaps.Flags & NativeVideoCapFlagExternalTextureTarget) != 0,
                    PresentedFrameDirectBindable =
                        (nativeCaps.Flags & NativeVideoCapFlagPresentedFrameDirectBindable) != 0,
                    PresentedFrameStrictZeroCopySupported =
                        (nativeCaps.Flags & NativeVideoCapFlagPresentedFrameStrictZeroCopy) != 0,
                    SourcePlaneTexturesSupported =
                        (nativeCaps.Flags & NativeVideoCapFlagSourcePlaneTexturesSupported) != 0,
                    SourcePlaneViewsSupported =
                        (nativeCaps.Flags & NativeVideoCapFlagSourcePlaneViewsSupported) != 0,
                    AcquireReleaseSupported = nativeCaps.AcquireReleaseSupported != 0,
                    RuntimeBridgePending =
                        (nativeCaps.Flags & NativeVideoCapFlagRuntimeBridgePending) != 0,
                    Flags = nativeCaps.Flags,
                };
                return true;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
            catch (DllNotFoundException)
            {
                return false;
            }
        }

        internal static MediaBackendKind ResolveRuntimePreferredBackend(MediaBackendKind preferredBackend)
        {
            if (preferredBackend != MediaBackendKind.Auto)
            {
                return preferredBackend;
            }

            return Application.platform == RuntimePlatform.Android
                ? MediaBackendKind.Ffmpeg
                : preferredBackend;
        }

        internal static bool TryReadNativeVideoBridgeDescriptor(
            GetNativeVideoBridgeDescriptorDelegate getNativeVideoBridgeDescriptor,
            int playerId,
            out NativeVideoBridgeDescriptorView descriptor)
        {
            descriptor = default(NativeVideoBridgeDescriptorView);

            try
            {
                var nativeDescriptor = CreateNativeVideoBridgeDescriptor();
                var result = getNativeVideoBridgeDescriptor(playerId, ref nativeDescriptor);
                if (result <= 0)
                {
                    return false;
                }

                descriptor = new NativeVideoBridgeDescriptorView
                {
                    BackendKind =
                        NormalizeBackendKind(nativeDescriptor.BackendKind, MediaBackendKind.Auto),
                    TargetPlatformKind =
                        NormalizeNativeVideoPlatformKind(nativeDescriptor.TargetPlatformKind),
                    TargetSurfaceKind =
                        NormalizeNativeVideoSurfaceKind(nativeDescriptor.TargetSurfaceKind),
                    TargetWidth = nativeDescriptor.TargetWidth,
                    TargetHeight = nativeDescriptor.TargetHeight,
                    TargetPixelFormat =
                        NormalizeNativeVideoPixelFormat(nativeDescriptor.TargetPixelFormat),
                    TargetFlags = nativeDescriptor.TargetFlags,
                    PlatformKind = NormalizeNativeVideoPlatformKind(nativeDescriptor.PlatformKind),
                    SurfaceKind = NormalizeNativeVideoSurfaceKind(nativeDescriptor.SurfaceKind),
                    State = NormalizeNativeVideoBridgeState(nativeDescriptor.State),
                    RuntimeKind =
                        NormalizeNativeVideoBridgeRuntimeKind(nativeDescriptor.RuntimeKind),
                    Supported = nativeDescriptor.Supported != 0,
                    HardwareDecodeSupported = nativeDescriptor.HardwareDecodeSupported != 0,
                    ZeroCopySupported = nativeDescriptor.ZeroCopySupported != 0,
                    AcquireReleaseSupported = nativeDescriptor.AcquireReleaseSupported != 0,
                    CapsFlags = nativeDescriptor.CapsFlags,
                    TargetValid = nativeDescriptor.TargetValid != 0,
                    RequestedExternalTextureTarget =
                        nativeDescriptor.RequestedExternalTextureTarget != 0,
                    DirectTargetPresentAllowed =
                        nativeDescriptor.DirectTargetPresentAllowed != 0,
                    TargetBindingSupported = nativeDescriptor.TargetBindingSupported != 0,
                    ExternalTextureTargetSupported =
                        nativeDescriptor.ExternalTextureTargetSupported != 0,
                    FrameAcquireSupported = nativeDescriptor.FrameAcquireSupported != 0,
                    FrameReleaseSupported = nativeDescriptor.FrameReleaseSupported != 0,
                    FallbackCopyPath = nativeDescriptor.FallbackCopyPath != 0,
                    SourceSurfaceZeroCopy = nativeDescriptor.SourceSurfaceZeroCopy != 0,
                    PresentedFrameDirectBindable =
                        nativeDescriptor.PresentedFrameDirectBindable != 0,
                    PresentedFrameStrictZeroCopy =
                        nativeDescriptor.PresentedFrameStrictZeroCopy != 0,
                    SourcePlaneTexturesSupported =
                        nativeDescriptor.SourcePlaneTexturesSupported != 0,
                    SourcePlaneViewsSupported = nativeDescriptor.SourcePlaneViewsSupported != 0,
                };
                return true;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
            catch (DllNotFoundException)
            {
                return false;
            }
        }

        internal static bool TryReadNativeVideoPathSelection(
            GetNativeVideoPathSelectionDelegate getNativeVideoPathSelection,
            int playerId,
            out NativeVideoPathSelectionView selection)
        {
            selection = default(NativeVideoPathSelectionView);

            try
            {
                var nativeSelection = CreateNativeVideoPathSelection();
                var result = getNativeVideoPathSelection(playerId, ref nativeSelection);
                if (result < 0)
                {
                    return false;
                }

                selection = new NativeVideoPathSelectionView
                {
                    Kind = NormalizeNativeVideoPathKind(nativeSelection.Kind),
                    HasSourceFrame = nativeSelection.HasSourceFrame != 0,
                    HasPresentedFrame = nativeSelection.HasPresentedFrame != 0,
                    SourceMemoryKind = NormalizeVideoFrameMemoryKind(nativeSelection.SourceMemoryKind),
                    PresentedMemoryKind = NormalizeVideoFrameMemoryKind(nativeSelection.PresentedMemoryKind),
                    BridgeState = NormalizeNativeVideoBridgeState(nativeSelection.BridgeState),
                    SourceSurfaceZeroCopy = nativeSelection.SourceSurfaceZeroCopy != 0,
                    SourcePlaneTexturesSupported = nativeSelection.SourcePlaneTexturesSupported != 0,
                    TargetZeroCopy = nativeSelection.TargetZeroCopy != 0,
                    CpuFallback = nativeSelection.CpuFallback != 0,
                };
                return true;
            }
            catch (DllNotFoundException)
            {
                return false;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
        }

        internal static bool TryReadWgpuRenderDescriptor(
            GetWgpuRenderDescriptorDelegate getWgpuRenderDescriptor,
            int playerId,
            out WgpuRenderDescriptorView descriptor)
        {
            descriptor = default(WgpuRenderDescriptorView);

            try
            {
                var nativeDescriptor = CreateWgpuRenderDescriptor();
                var result = getWgpuRenderDescriptor(playerId, ref nativeDescriptor);
                if (result < 0)
                {
                    return false;
                }

                descriptor = new WgpuRenderDescriptorView
                {
                    OutputWidth = nativeDescriptor.OutputWidth,
                    OutputHeight = nativeDescriptor.OutputHeight,
                    RuntimeReady = nativeDescriptor.RuntimeReady != 0,
                    SupportsYuv420p = nativeDescriptor.SupportsYuv420p != 0,
                    SupportsNv12 = nativeDescriptor.SupportsNv12 != 0,
                    SupportsP010 = nativeDescriptor.SupportsP010 != 0,
                    SupportsRgba32 = nativeDescriptor.SupportsRgba32 != 0,
                    SupportsExternalTextureRgba =
                        nativeDescriptor.SupportsExternalTextureRgba != 0,
                    SupportsExternalTextureYu12 =
                        nativeDescriptor.SupportsExternalTextureYu12 != 0,
                    ReadbackExportSupported =
                        nativeDescriptor.ReadbackExportSupported != 0,
                };
                return true;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
            catch (DllNotFoundException)
            {
                return false;
            }
        }

        internal static bool TryReadWgpuRenderStateView(
            GetWgpuRenderStateViewDelegate getWgpuRenderStateView,
            int playerId,
            out WgpuRenderStateView state)
        {
            state = default(WgpuRenderStateView);

            try
            {
                var nativeState = CreateWgpuRenderStateView();
                var result = getWgpuRenderStateView(playerId, ref nativeState);
                if (result < 0)
                {
                    return false;
                }

                state = new WgpuRenderStateView
                {
                    HasSourceContract = nativeState.HasSourceContract != 0,
                    HasPresentedContract = nativeState.HasPresentedContract != 0,
                    SourceMemoryKind =
                        NormalizeVideoFrameMemoryKind(nativeState.SourceMemoryKind),
                    PresentedMemoryKind =
                        NormalizeVideoFrameMemoryKind(nativeState.PresentedMemoryKind),
                    SourcePixelFormat =
                        NormalizeNativeVideoPixelFormat(nativeState.SourcePixelFormat),
                    PresentedPixelFormat =
                        NormalizeNativeVideoPixelFormat(nativeState.PresentedPixelFormat),
                    RenderPath = NormalizeWgpuRenderPath(nativeState.RenderPath),
                    ExternalTextureFormat =
                        NormalizeWgpuExternalTextureFormat(nativeState.ExternalTextureFormat),
                    HasRenderedFrame = nativeState.HasRenderedFrame != 0,
                    RenderedFrameIndex = nativeState.RenderedFrameIndex,
                    RenderedTimeSec = nativeState.RenderedTimeSec,
                    HasRenderError = nativeState.HasRenderError != 0,
                    RenderErrorKind = NormalizeWgpuRenderError(nativeState.RenderErrorKind),
                    UploadPlaneCount = nativeState.UploadPlaneCount,
                    SourceZeroCopy = nativeState.SourceZeroCopy != 0,
                    CpuFallback = nativeState.CpuFallback != 0,
                };
                return true;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
            catch (DllNotFoundException)
            {
                return false;
            }
        }

        internal static bool TryReadVideoColorInfo(
            GetNativeVideoColorInfoDelegate getNativeVideoColorInfo,
            int playerId,
            out VideoColorInfoView info)
        {
            info = default(VideoColorInfoView);

            try
            {
                var nativeInfo = CreateVideoColorInfo();
                var result = getNativeVideoColorInfo(playerId, ref nativeInfo);
                if (result <= 0)
                {
                    return false;
                }

                info = NormalizeVideoColorInfo(nativeInfo);
                return true;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
            catch (DllNotFoundException)
            {
                return false;
            }
        }

        internal static bool TryAcquireNativeVideoFrame(
            AcquireNativeVideoFrameDelegate acquireNativeVideoFrame,
            int playerId,
            out NativeVideoFrameView frame)
        {
            frame = default(NativeVideoFrameView);

            try
            {
                var nativeFrame = CreateNativeVideoFrame();
                var result = acquireNativeVideoFrame(playerId, ref nativeFrame);
                if (result <= 0)
                {
                    return false;
                }

                frame = new NativeVideoFrameView
                {
                    SurfaceKind = NormalizeNativeVideoSurfaceKind(nativeFrame.SurfaceKind),
                    NativeHandle = new IntPtr(unchecked((long)nativeFrame.NativeHandle)),
                    AuxiliaryHandle = new IntPtr(unchecked((long)nativeFrame.AuxiliaryHandle)),
                    Width = nativeFrame.Width,
                    Height = nativeFrame.Height,
                    PixelFormat = NormalizeNativeVideoPixelFormat(nativeFrame.PixelFormat),
                    TimeSec = nativeFrame.TimeSec,
                    FrameIndex = nativeFrame.FrameIndex,
                    Flags = nativeFrame.Flags,
                };
                return true;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
            catch (DllNotFoundException)
            {
                return false;
            }
        }

        internal static bool TryReleaseNativeVideoFrame(
            ReleaseNativeVideoFrameDelegate releaseNativeVideoFrame,
            int playerId,
            long frameIndex)
        {
            if (frameIndex < 0)
            {
                return false;
            }

            try
            {
                return releaseNativeVideoFrame(playerId, frameIndex) >= 0;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
            catch (DllNotFoundException)
            {
                return false;
            }
        }

        internal static bool TryReadNativeVideoSourcePlaneTextures(
            GetNativeVideoSourcePlaneTexturesDelegate getNativeVideoSourcePlaneTextures,
            int playerId,
            out NativeVideoPlaneTexturesView textures)
        {
            textures = default(NativeVideoPlaneTexturesView);

            try
            {
                var nativeTextures = CreateNativeVideoPlaneTextures();
                var result = getNativeVideoSourcePlaneTextures(playerId, ref nativeTextures);
                if (result <= 0)
                {
                    return false;
                }

                textures = new NativeVideoPlaneTexturesView
                {
                    SurfaceKind = NormalizeNativeVideoSurfaceKind(nativeTextures.SurfaceKind),
                    SourcePixelFormat =
                        NormalizeNativeVideoPixelFormat(nativeTextures.SourcePixelFormat),
                    YNativeHandle = new IntPtr(unchecked((long)nativeTextures.YNativeHandle)),
                    YAuxiliaryHandle =
                        new IntPtr(unchecked((long)nativeTextures.YAuxiliaryHandle)),
                    YWidth = nativeTextures.YWidth,
                    YHeight = nativeTextures.YHeight,
                    YTextureFormat =
                        NormalizeNativeVideoPlaneTextureFormat(nativeTextures.YTextureFormat),
                    UVNativeHandle = new IntPtr(unchecked((long)nativeTextures.UVNativeHandle)),
                    UVAuxiliaryHandle =
                        new IntPtr(unchecked((long)nativeTextures.UVAuxiliaryHandle)),
                    UVWidth = nativeTextures.UVWidth,
                    UVHeight = nativeTextures.UVHeight,
                    UVTextureFormat =
                        NormalizeNativeVideoPlaneTextureFormat(nativeTextures.UVTextureFormat),
                    TimeSec = nativeTextures.TimeSec,
                    FrameIndex = nativeTextures.FrameIndex,
                    Flags = nativeTextures.Flags,
                };
                return true;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
            catch (DllNotFoundException)
            {
                return false;
            }
        }

        internal static bool TryReadNativeVideoSourcePlaneViews(
            GetNativeVideoSourcePlaneViewsDelegate getNativeVideoSourcePlaneViews,
            int playerId,
            out NativeVideoPlaneViewsView views)
        {
            views = default(NativeVideoPlaneViewsView);

            try
            {
                var nativeViews = CreateNativeVideoPlaneViews();
                var result = getNativeVideoSourcePlaneViews(playerId, ref nativeViews);
                if (result <= 0)
                {
                    return false;
                }

                views = new NativeVideoPlaneViewsView
                {
                    SurfaceKind = NormalizeNativeVideoSurfaceKind(nativeViews.SurfaceKind),
                    SourcePixelFormat =
                        NormalizeNativeVideoPixelFormat(nativeViews.SourcePixelFormat),
                    YNativeHandle = new IntPtr(unchecked((long)nativeViews.YNativeHandle)),
                    YAuxiliaryHandle =
                        new IntPtr(unchecked((long)nativeViews.YAuxiliaryHandle)),
                    YWidth = nativeViews.YWidth,
                    YHeight = nativeViews.YHeight,
                    YTextureFormat =
                        NormalizeNativeVideoPlaneTextureFormat(nativeViews.YTextureFormat),
                    YResourceKind =
                        NormalizeNativeVideoPlaneResourceKind(nativeViews.YResourceKind),
                    UVNativeHandle = new IntPtr(unchecked((long)nativeViews.UVNativeHandle)),
                    UVAuxiliaryHandle =
                        new IntPtr(unchecked((long)nativeViews.UVAuxiliaryHandle)),
                    UVWidth = nativeViews.UVWidth,
                    UVHeight = nativeViews.UVHeight,
                    UVTextureFormat =
                        NormalizeNativeVideoPlaneTextureFormat(nativeViews.UVTextureFormat),
                    UVResourceKind =
                        NormalizeNativeVideoPlaneResourceKind(nativeViews.UVResourceKind),
                    TimeSec = nativeViews.TimeSec,
                    FrameIndex = nativeViews.FrameIndex,
                    Flags = nativeViews.Flags,
                };
                return true;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
            catch (DllNotFoundException)
            {
                return false;
            }
        }

        internal static NativeVideoPlatformKind DetectNativeVideoPlatformKind()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            return NativeVideoPlatformKind.Windows;
#elif UNITY_IOS
            return NativeVideoPlatformKind.Ios;
#elif UNITY_ANDROID
            return NativeVideoPlatformKind.Android;
#else
            return NativeVideoPlatformKind.Unknown;
#endif
        }

        internal static NativeVideoSurfaceKind DetectDefaultNativeVideoSurfaceKind()
        {
            return GetDefaultNativeVideoSurfaceKindForPlatform(DetectNativeVideoPlatformKind());
        }

        internal static NativeVideoSurfaceKind GetDefaultNativeVideoSurfaceKindForPlatform(
            NativeVideoPlatformKind platformKind)
        {
            switch (platformKind)
            {
                case NativeVideoPlatformKind.Windows:
                    return NativeVideoSurfaceKind.D3D11Texture2D;
                case NativeVideoPlatformKind.Ios:
                    return NativeVideoSurfaceKind.MetalTexture;
                case NativeVideoPlatformKind.Android:
                    return NativeVideoSurfaceKind.AndroidSurfaceTexture;
                default:
                    return NativeVideoSurfaceKind.Unknown;
            }
        }

        internal static NativeVideoSurfaceKind ResolvePreferredNativeVideoSurfaceKind(
            NativeVideoPlatformKind platformKind,
            NativeVideoSurfaceKind preferredSurfaceKind)
        {
            if (preferredSurfaceKind == NativeVideoSurfaceKind.Unknown)
            {
                return GetDefaultNativeVideoSurfaceKindForPlatform(platformKind);
            }

            if (IsNativeVideoSurfaceKindSupportedByPlatform(platformKind, preferredSurfaceKind))
            {
                return preferredSurfaceKind;
            }

            return GetDefaultNativeVideoSurfaceKindForPlatform(platformKind);
        }

        internal static NativeVideoTargetProviderKind ResolveNativeVideoTargetProviderKind(
            NativeVideoPlatformKind platformKind,
            NativeVideoSurfaceKind surfaceKind)
        {
            switch (platformKind)
            {
                case NativeVideoPlatformKind.Windows:
                    return NativeVideoTargetProviderKind.UnityExternalTexture;
                case NativeVideoPlatformKind.Ios:
                    if (surfaceKind == NativeVideoSurfaceKind.CVPixelBuffer)
                    {
                        return NativeVideoTargetProviderKind.IosCVPixelBuffer;
                    }
                    if (surfaceKind == NativeVideoSurfaceKind.MetalTexture)
                    {
                        return NativeVideoTargetProviderKind.IosMetalTexture;
                    }
                    break;
                case NativeVideoPlatformKind.Android:
                    if (surfaceKind == NativeVideoSurfaceKind.AndroidHardwareBuffer)
                    {
                        return NativeVideoTargetProviderKind.AndroidHardwareBuffer;
                    }
                    if (surfaceKind == NativeVideoSurfaceKind.AndroidSurfaceTexture)
                    {
                        return NativeVideoTargetProviderKind.AndroidSurfaceTexture;
                    }
                    break;
            }

            return NativeVideoTargetProviderKind.Auto;
        }

        internal static string DescribeNativeVideoTargetProvider(
            NativeVideoPlatformKind platformKind,
            NativeVideoSurfaceKind surfaceKind)
        {
            return ResolveNativeVideoTargetProviderKind(platformKind, surfaceKind).ToString();
        }

        internal static bool IsNativeVideoExternalTextureTargetSurface(
            NativeVideoSurfaceKind surfaceKind)
        {
            return surfaceKind == NativeVideoSurfaceKind.D3D11Texture2D
                || surfaceKind == NativeVideoSurfaceKind.MetalTexture;
        }

        internal static bool IsNativeVideoSurfaceKindSupportedByPlatform(
            NativeVideoPlatformKind platformKind,
            NativeVideoSurfaceKind surfaceKind)
        {
            switch (platformKind)
            {
                case NativeVideoPlatformKind.Windows:
                    return surfaceKind == NativeVideoSurfaceKind.D3D11Texture2D;
                case NativeVideoPlatformKind.Ios:
                    return surfaceKind == NativeVideoSurfaceKind.MetalTexture
                        || surfaceKind == NativeVideoSurfaceKind.CVPixelBuffer;
                case NativeVideoPlatformKind.Android:
                    return surfaceKind == NativeVideoSurfaceKind.AndroidSurfaceTexture
                        || surfaceKind == NativeVideoSurfaceKind.AndroidHardwareBuffer;
                default:
                    return false;
            }
        }

        internal static bool IsNativeVideoContractBringUpPlatform(NativeVideoPlatformKind platformKind)
        {
            return platformKind == NativeVideoPlatformKind.Ios
                || platformKind == NativeVideoPlatformKind.Android;
        }

        internal static bool IsNativeVideoPresentationPathImplementedForPlatform(
            NativeVideoPlatformKind platformKind,
            NativeVideoSurfaceKind surfaceKind)
        {
            switch (platformKind)
            {
                case NativeVideoPlatformKind.Windows:
                    return surfaceKind == NativeVideoSurfaceKind.D3D11Texture2D;
                case NativeVideoPlatformKind.Ios:
                case NativeVideoPlatformKind.Android:
                    return false;
                default:
                    return false;
            }
        }

        internal static string DescribeNativeVideoTargetContract(
            NativeVideoPlatformKind platformKind,
            NativeVideoSurfaceKind surfaceKind)
        {
            return platformKind + "/" + surfaceKind;
        }

        internal static string DescribeNativeVideoSurfaceSelection(
            NativeVideoPlatformKind platformKind,
            NativeVideoSurfaceKind preferredSurfaceKind,
            NativeVideoSurfaceKind resolvedSurfaceKind)
        {
            return "platform=" + platformKind
                + " preferred=" + preferredSurfaceKind
                + " resolved=" + resolvedSurfaceKind
                + " provider=" + DescribeNativeVideoTargetProvider(platformKind, resolvedSurfaceKind);
        }

        internal static string DescribeNativeVideoPresentationAvailability(
            NativeVideoPlatformKind platformKind,
            NativeVideoSurfaceKind surfaceKind)
        {
            return "platform=" + platformKind
                + " surface=" + surfaceKind
                + " implemented="
                + IsNativeVideoPresentationPathImplementedForPlatform(platformKind, surfaceKind);
        }

        internal static NativeVideoPlatformKind NormalizeNativeVideoPlatformKind(int rawValue)
        {
            switch (rawValue)
            {
                case 1:
                    return NativeVideoPlatformKind.Windows;
                case 2:
                    return NativeVideoPlatformKind.Ios;
                case 3:
                    return NativeVideoPlatformKind.Android;
                default:
                    return NativeVideoPlatformKind.Unknown;
            }
        }

        internal static NativeVideoSurfaceKind NormalizeNativeVideoSurfaceKind(int rawValue)
        {
            switch (rawValue)
            {
                case 1:
                    return NativeVideoSurfaceKind.D3D11Texture2D;
                case 2:
                    return NativeVideoSurfaceKind.MetalTexture;
                case 3:
                    return NativeVideoSurfaceKind.CVPixelBuffer;
                case 4:
                    return NativeVideoSurfaceKind.AndroidSurfaceTexture;
                case 5:
                    return NativeVideoSurfaceKind.AndroidHardwareBuffer;
                default:
                    return NativeVideoSurfaceKind.Unknown;
            }
        }

        internal static NativeVideoBridgeState NormalizeNativeVideoBridgeState(int rawValue)
        {
            switch (rawValue)
            {
                case 1:
                    return NativeVideoBridgeState.ContractPending;
                case 2:
                    return NativeVideoBridgeState.RuntimeReady;
                default:
                    return NativeVideoBridgeState.Unsupported;
            }
        }

        internal static NativeVideoBridgeRuntimeKind NormalizeNativeVideoBridgeRuntimeKind(
            int rawValue)
        {
            switch (rawValue)
            {
                case 1:
                    return NativeVideoBridgeRuntimeKind.WindowsD3d11TextureInterop;
                case 2:
                    return NativeVideoBridgeRuntimeKind.IosMetalTexture;
                case 3:
                    return NativeVideoBridgeRuntimeKind.IosCvPixelBuffer;
                case 4:
                    return NativeVideoBridgeRuntimeKind.AndroidSurfaceTexture;
                case 5:
                    return NativeVideoBridgeRuntimeKind.AndroidHardwareBuffer;
                default:
                    return NativeVideoBridgeRuntimeKind.None;
            }
        }

        internal static NativeVideoPathKind NormalizeNativeVideoPathKind(int rawValue)
        {
            switch (rawValue)
            {
                case 1:
                    return NativeVideoPathKind.NativeBridgeZeroCopy;
                case 2:
                    return NativeVideoPathKind.NativeBridgePlanes;
                case 3:
                    return NativeVideoPathKind.WgpuRenderCore;
                case 4:
                    return NativeVideoPathKind.CpuFallback;
                default:
                    return NativeVideoPathKind.Unknown;
            }
        }

        internal static WgpuRenderPath NormalizeWgpuRenderPath(int rawValue)
        {
            switch (rawValue)
            {
                case 1:
                    return WgpuRenderPath.NativeSurfaceBridge;
                default:
                    return WgpuRenderPath.CpuPlanar;
            }
        }

        internal static WgpuExternalTextureFormat NormalizeWgpuExternalTextureFormat(int rawValue)
        {
            switch (rawValue)
            {
                case 1:
                    return WgpuExternalTextureFormat.Rgba;
                case 2:
                    return WgpuExternalTextureFormat.Yu12;
                case 3:
                    return WgpuExternalTextureFormat.Nv12;
                default:
                    return WgpuExternalTextureFormat.None;
            }
        }

        internal static WgpuRenderError NormalizeWgpuRenderError(int rawValue)
        {
            switch (rawValue)
            {
                case 1:
                    return WgpuRenderError.NativeSurfaceImportPending;
                case 2:
                    return WgpuRenderError.UnsupportedSourceFormat;
                case 3:
                    return WgpuRenderError.InvalidPlaneCount;
                case 4:
                    return WgpuRenderError.InvalidPlaneData;
                case 5:
                    return WgpuRenderError.UnsupportedTextureFormat;
                case 6:
                    return WgpuRenderError.UnsupportedOutputFormat;
                case 7:
                    return WgpuRenderError.AdapterUnavailable;
                case 8:
                    return WgpuRenderError.RequestDevice;
                case 9:
                    return WgpuRenderError.PollFailed;
                case 10:
                    return WgpuRenderError.MapFailed;
                case 11:
                    return WgpuRenderError.ReadbackFailed;
                case 12:
                    return WgpuRenderError.Other;
                default:
                    return WgpuRenderError.None;
            }
        }

        internal static NativeVideoPixelFormat NormalizeNativeVideoPixelFormat(int rawValue)
        {
            switch (rawValue)
            {
                case 0:
                    return NativeVideoPixelFormat.Yuv420p;
                case 1:
                    return NativeVideoPixelFormat.Rgba32;
                case 2:
                    return NativeVideoPixelFormat.Nv12;
                case 3:
                    return NativeVideoPixelFormat.P010;
                default:
                    return NativeVideoPixelFormat.Unknown;
            }
        }

        internal static NativeVideoColorRange NormalizeNativeVideoColorRange(int rawValue)
        {
            switch (rawValue)
            {
                case 0:
                    return NativeVideoColorRange.Limited;
                case 1:
                    return NativeVideoColorRange.Full;
                default:
                    return NativeVideoColorRange.Unknown;
            }
        }

        internal static NativeVideoColorMatrix NormalizeNativeVideoColorMatrix(int rawValue)
        {
            switch (rawValue)
            {
                case 0:
                    return NativeVideoColorMatrix.Bt601;
                case 1:
                    return NativeVideoColorMatrix.Bt709;
                case 2:
                    return NativeVideoColorMatrix.Bt2020Ncl;
                case 3:
                    return NativeVideoColorMatrix.Bt2020Cl;
                case 4:
                    return NativeVideoColorMatrix.Smpte240M;
                case 5:
                    return NativeVideoColorMatrix.Rgb;
                default:
                    return NativeVideoColorMatrix.Unknown;
            }
        }

        internal static NativeVideoColorPrimaries NormalizeNativeVideoColorPrimaries(int rawValue)
        {
            switch (rawValue)
            {
                case 0:
                    return NativeVideoColorPrimaries.Bt601;
                case 1:
                    return NativeVideoColorPrimaries.Bt709;
                case 2:
                    return NativeVideoColorPrimaries.Bt2020;
                case 3:
                    return NativeVideoColorPrimaries.DciP3;
                default:
                    return NativeVideoColorPrimaries.Unknown;
            }
        }

        internal static NativeVideoTransferCharacteristic NormalizeNativeVideoTransferCharacteristic(
            int rawValue)
        {
            switch (rawValue)
            {
                case 0:
                    return NativeVideoTransferCharacteristic.Bt1886;
                case 1:
                    return NativeVideoTransferCharacteristic.Srgb;
                case 2:
                    return NativeVideoTransferCharacteristic.Linear;
                case 3:
                    return NativeVideoTransferCharacteristic.Smpte240M;
                case 4:
                    return NativeVideoTransferCharacteristic.Pq;
                case 5:
                    return NativeVideoTransferCharacteristic.Hlg;
                default:
                    return NativeVideoTransferCharacteristic.Unknown;
            }
        }

        internal static NativeVideoDynamicRange NormalizeNativeVideoDynamicRange(int rawValue)
        {
            switch (rawValue)
            {
                case 1:
                    return NativeVideoDynamicRange.Sdr;
                case 2:
                    return NativeVideoDynamicRange.Hdr10;
                case 3:
                    return NativeVideoDynamicRange.Hlg;
                case 4:
                    return NativeVideoDynamicRange.DolbyVision;
                default:
                    return NativeVideoDynamicRange.Unknown;
            }
        }

        internal static VideoFrameMemoryKind NormalizeVideoFrameMemoryKind(int rawValue)
        {
            switch (rawValue)
            {
                case 1:
                    return VideoFrameMemoryKind.CpuPlanar;
                case 2:
                    return VideoFrameMemoryKind.NativeSurface;
                default:
                    return VideoFrameMemoryKind.Unknown;
            }
        }

        internal static AvSyncMasterClockKind NormalizeAvSyncMasterClockKind(int rawValue)
        {
            switch (rawValue)
            {
                case 1:
                    return AvSyncMasterClockKind.Audio;
                case 2:
                    return AvSyncMasterClockKind.Video;
                case 3:
                    return AvSyncMasterClockKind.External;
                default:
                    return AvSyncMasterClockKind.Unknown;
            }
        }

        internal static VideoColorInfoView NormalizeVideoColorInfo(RustAVVideoColorInfo info)
        {
            return new VideoColorInfoView
            {
                Range = NormalizeNativeVideoColorRange(info.Range),
                Matrix = NormalizeNativeVideoColorMatrix(info.Matrix),
                Primaries = NormalizeNativeVideoColorPrimaries(info.Primaries),
                Transfer = NormalizeNativeVideoTransferCharacteristic(info.Transfer),
                BitDepth = info.BitDepth,
                DynamicRange = NormalizeNativeVideoDynamicRange(info.DynamicRange),
            };
        }

        internal static VideoFrameContractView NormalizeVideoFrameContract(
            RustAVVideoFrameContract contract)
        {
            return new VideoFrameContractView
            {
                MemoryKind = NormalizeVideoFrameMemoryKind(contract.MemoryKind),
                SurfaceKind = NormalizeNativeVideoSurfaceKind(contract.SurfaceKind),
                PixelFormat = NormalizeNativeVideoPixelFormat(contract.PixelFormat),
                Width = contract.Width,
                Height = contract.Height,
                PlaneCount = contract.PlaneCount,
                HardwareDecode = contract.HardwareDecode != 0,
                ZeroCopy = contract.ZeroCopy != 0,
                CpuFallback = contract.CpuFallback != 0,
                NativeHandlePresent = contract.NativeHandlePresent != 0,
                AuxiliaryHandlePresent = contract.AuxiliaryHandlePresent != 0,
                Color = new VideoColorInfoView
                {
                    Range = NormalizeNativeVideoColorRange(contract.ColorRange),
                    Matrix = NormalizeNativeVideoColorMatrix(contract.ColorMatrix),
                    Primaries = NormalizeNativeVideoColorPrimaries(contract.ColorPrimaries),
                    Transfer = NormalizeNativeVideoTransferCharacteristic(contract.ColorTransfer),
                    BitDepth = contract.ColorBitDepth,
                    DynamicRange = NormalizeNativeVideoDynamicRange(contract.ColorDynamicRange),
                },
                HasColorDynamicRangeOverride = contract.HasColorDynamicRangeOverride != 0,
                ColorDynamicRangeOverride =
                    NormalizeNativeVideoDynamicRange(contract.ColorDynamicRangeOverride),
                TimeSec = contract.TimeSec,
                HasFrameIndex = contract.HasFrameIndex != 0,
                FrameIndex = contract.FrameIndex,
                HasNominalFps = contract.HasNominalFps != 0,
                NominalFps = contract.NominalFps,
                HasTimelineOriginSec = contract.HasTimelineOriginSec != 0,
                TimelineOriginSec = contract.TimelineOriginSec,
                SeekEpoch = contract.SeekEpoch,
                Discontinuity = contract.Discontinuity != 0,
            };
        }

        internal static PlaybackTimingContractView NormalizePlaybackTimingContract(
            RustAVPlaybackTimingContract contract)
        {
            var hasMicrosecondMirror = contract.StructVersion >= 2u;
            return new PlaybackTimingContractView
            {
                HasMicrosecondMirror = hasMicrosecondMirror,
                MasterTimeSec = contract.MasterTimeSec,
                MasterTimeUs = hasMicrosecondMirror
                    ? contract.MasterTimeUs
                    : SecondsToMicroseconds(contract.MasterTimeSec),
                ExternalTimeSec = contract.ExternalTimeSec,
                ExternalTimeUs = hasMicrosecondMirror
                    ? contract.ExternalTimeUs
                    : SecondsToMicroseconds(contract.ExternalTimeSec),
                HasAudioTimeSec = contract.HasAudioTimeSec != 0,
                AudioTimeSec = contract.AudioTimeSec,
                HasAudioTimeUs = hasMicrosecondMirror
                    ? contract.HasAudioTimeUs != 0
                    : contract.HasAudioTimeSec != 0,
                AudioTimeUs = hasMicrosecondMirror
                    ? contract.AudioTimeUs
                    : SecondsToMicroseconds(contract.AudioTimeSec),
                HasAudioPresentedTimeSec = contract.HasAudioPresentedTimeSec != 0,
                AudioPresentedTimeSec = contract.AudioPresentedTimeSec,
                HasAudioPresentedTimeUs = hasMicrosecondMirror
                    ? contract.HasAudioPresentedTimeUs != 0
                    : contract.HasAudioPresentedTimeSec != 0,
                AudioPresentedTimeUs = hasMicrosecondMirror
                    ? contract.AudioPresentedTimeUs
                    : SecondsToMicroseconds(contract.AudioPresentedTimeSec),
                AudioSinkDelaySec = contract.AudioSinkDelaySec,
                AudioSinkDelayUs = hasMicrosecondMirror
                    ? contract.AudioSinkDelayUs
                    : SecondsToMicroseconds(contract.AudioSinkDelaySec),
                HasAudioClock = contract.HasAudioClock != 0,
            };
        }

        private static long SecondsToMicroseconds(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0.0)
            {
                return 0L;
            }

            var micros = seconds * 1000000.0;
            if (micros >= long.MaxValue)
            {
                return long.MaxValue;
            }

            return (long)Math.Round(micros, MidpointRounding.AwayFromZero);
        }

        internal static AvSyncContractView NormalizeAvSyncContract(RustAVAvSyncContract contract)
        {
            return new AvSyncContractView
            {
                MasterClock = NormalizeAvSyncMasterClockKind(contract.MasterClock),
                HasAudioClockSec = contract.HasAudioClockSec != 0,
                AudioClockSec = contract.AudioClockSec,
                HasVideoClockSec = contract.HasVideoClockSec != 0,
                VideoClockSec = contract.VideoClockSec,
                DriftMs = contract.DriftMs,
                StartupWarmupComplete = contract.StartupWarmupComplete != 0,
                DropTotal = contract.DropTotal,
                DuplicateTotal = contract.DuplicateTotal,
            };
        }

        internal static AudioOutputPolicyView NormalizeAudioOutputPolicy(
            RustAVAudioOutputPolicy policy)
        {
            return new AudioOutputPolicyView
            {
                FileStartThresholdMilliseconds = policy.FileStartThresholdMilliseconds,
                AndroidFileStartThresholdMilliseconds = policy.AndroidFileStartThresholdMilliseconds,
                RealtimeStartThresholdMilliseconds = policy.RealtimeStartThresholdMilliseconds,
                RealtimeStartupGraceMilliseconds = policy.RealtimeStartupGraceMilliseconds,
                RealtimeStartupMinimumThresholdMilliseconds = policy.RealtimeStartupMinimumThresholdMilliseconds,
                FileRingCapacityMilliseconds = policy.FileRingCapacityMilliseconds,
                AndroidFileRingCapacityMilliseconds = policy.AndroidFileRingCapacityMilliseconds,
                RealtimeRingCapacityMilliseconds = policy.RealtimeRingCapacityMilliseconds,
                FileBufferedCeilingMilliseconds = policy.FileBufferedCeilingMilliseconds,
                AndroidFileBufferedCeilingMilliseconds = policy.AndroidFileBufferedCeilingMilliseconds,
                RealtimeBufferedCeilingMilliseconds = policy.RealtimeBufferedCeilingMilliseconds,
                RealtimeStartupAdditionalSinkDelayMilliseconds = policy.RealtimeStartupAdditionalSinkDelayMilliseconds,
                RealtimeSteadyAdditionalSinkDelayMilliseconds = policy.RealtimeSteadyAdditionalSinkDelayMilliseconds,
                RealtimeBackendAdditionalSinkDelayMilliseconds = policy.RealtimeBackendAdditionalSinkDelayMilliseconds,
                RealtimeStartRequiresVideoFrame = policy.RealtimeStartRequiresVideoFrame != 0,
                AllowAndroidFileOutputRateBridge = policy.AllowAndroidFileOutputRateBridge != 0,
            };
        }

        internal static bool ResolveAndroidFileAudioOutputRateBridgeActive(
            AudioOutputPolicyView policy,
            bool isRealtimeSource,
            RuntimePlatform platform,
            int sourceSampleRate,
            int playbackSampleRate)
        {
            return !isRealtimeSource
                && platform == RuntimePlatform.Android
                && policy.AllowAndroidFileOutputRateBridge
                && sourceSampleRate > 0
                && playbackSampleRate > 0
                && sourceSampleRate != playbackSampleRate;
        }

        internal static int ResolvePlaybackSampleRate(
            AudioOutputPolicyView policy,
            bool isRealtimeSource,
            RuntimePlatform platform,
            int sourceSampleRate,
            int outputSampleRate)
        {
            if (sourceSampleRate <= 0)
            {
                return sourceSampleRate;
            }

            if (isRealtimeSource
                || platform != RuntimePlatform.Android
                || !policy.AllowAndroidFileOutputRateBridge)
            {
                return sourceSampleRate;
            }

            if (outputSampleRate <= 0 || outputSampleRate >= sourceSampleRate)
            {
                return sourceSampleRate;
            }

            if (sourceSampleRate % outputSampleRate != 0)
            {
                return sourceSampleRate;
            }

            return outputSampleRate;
        }

        internal static int ResolveAudioStartThresholdMilliseconds(
            AudioOutputPolicyView policy,
            bool isRealtimeSource,
            bool androidFileBridgeActive)
        {
            if (isRealtimeSource)
            {
                return policy.RealtimeStartThresholdMilliseconds;
            }

            return androidFileBridgeActive
                ? policy.AndroidFileStartThresholdMilliseconds
                : policy.FileStartThresholdMilliseconds;
        }

        internal static int ResolveAudioRingCapacityMilliseconds(
            AudioOutputPolicyView policy,
            bool isRealtimeSource,
            bool androidFileBridgeActive)
        {
            if (isRealtimeSource)
            {
                return policy.RealtimeRingCapacityMilliseconds;
            }

            return androidFileBridgeActive
                ? policy.AndroidFileRingCapacityMilliseconds
                : policy.FileRingCapacityMilliseconds;
        }

        internal static int ResolveAudioBufferedCeilingMilliseconds(
            AudioOutputPolicyView policy,
            bool isRealtimeSource,
            bool androidFileBridgeActive)
        {
            if (isRealtimeSource)
            {
                return policy.RealtimeBufferedCeilingMilliseconds;
            }

            return androidFileBridgeActive
                ? policy.AndroidFileBufferedCeilingMilliseconds
                : policy.FileBufferedCeilingMilliseconds;
        }

        internal static int ResolveRealtimeStartupGraceMilliseconds(
            AudioOutputPolicyView policy)
        {
            return policy.RealtimeStartupGraceMilliseconds;
        }

        internal static int ResolveRealtimeStartupMinimumThresholdMilliseconds(
            AudioOutputPolicyView policy)
        {
            return policy.RealtimeStartupMinimumThresholdMilliseconds;
        }

        internal static bool ResolveRealtimeStartRequiresVideoFrame(
            AudioOutputPolicyView policy)
        {
            return policy.RealtimeStartRequiresVideoFrame;
        }

        internal static int ResolveRealtimeStartupAdditionalSinkDelayMilliseconds(
            AudioOutputPolicyView policy)
        {
            return policy.RealtimeStartupAdditionalSinkDelayMilliseconds;
        }

        internal static int ResolveRealtimeSteadyAdditionalSinkDelayMilliseconds(
            AudioOutputPolicyView policy)
        {
            return policy.RealtimeSteadyAdditionalSinkDelayMilliseconds;
        }

        internal static int ResolveRealtimeBackendAdditionalSinkDelayMilliseconds(
            AudioOutputPolicyView policy)
        {
            return policy.RealtimeBackendAdditionalSinkDelayMilliseconds;
        }

        internal static int ResolveAudioBufferSamples(
            int audioSampleRate,
            int audioChannels,
            int milliseconds)
        {
            if (audioSampleRate <= 0 || audioChannels <= 0 || milliseconds <= 0)
            {
                return 0;
            }

            var sampleCount =
                ((long)audioSampleRate * audioChannels * milliseconds) / 1000L;
            sampleCount = Math.Max(sampleCount, audioChannels);
            return (int)Math.Min(sampleCount, int.MaxValue);
        }

        internal static int ResolveAudioStartThresholdSamples(
            AudioOutputPolicyView policy,
            bool isRealtimeSource,
            bool androidFileBridgeActive,
            int audioSampleRate,
            int audioChannels,
            float startupElapsedMilliseconds,
            bool hasPresentedStartupFrame)
        {
            if (audioSampleRate <= 0 || audioChannels <= 0)
            {
                return 0;
            }

            var thresholdSamples = ResolveAudioBufferSamples(
                audioSampleRate,
                audioChannels,
                ResolveAudioStartThresholdMilliseconds(
                    policy,
                    isRealtimeSource,
                    androidFileBridgeActive));
            if (!isRealtimeSource)
            {
                return thresholdSamples;
            }

            if (!hasPresentedStartupFrame
                || startupElapsedMilliseconds < policy.RealtimeStartupGraceMilliseconds)
            {
                return thresholdSamples;
            }

            var relaxedThresholdSamples = ResolveAudioBufferSamples(
                audioSampleRate,
                audioChannels,
                policy.RealtimeStartupMinimumThresholdMilliseconds);
            return Math.Max(Math.Min(thresholdSamples, relaxedThresholdSamples), audioChannels);
        }

        internal static AudioStartupObservationView CreateAudioStartupObservation(
            int audioSampleRate,
            int audioChannels,
            int bufferedSamples,
            float startupElapsedMilliseconds,
            bool hasPresentedVideoFrame,
            bool requiresPresentedVideoFrame,
            bool androidFileRateBridgeActive)
        {
            return new AudioStartupObservationView
            {
                AudioSampleRate = Mathf.Max(0, audioSampleRate),
                AudioChannels = Mathf.Max(0, audioChannels),
                BufferedSamples = Mathf.Max(0, bufferedSamples),
                StartupElapsedMilliseconds = Math.Max(0.0, startupElapsedMilliseconds),
                HasPresentedVideoFrame = hasPresentedVideoFrame,
                RequiresPresentedVideoFrame = requiresPresentedVideoFrame,
                AndroidFileRateBridgeActive = androidFileRateBridgeActive,
            };
        }

        internal static ReferencePlaybackObservationView CreateReferencePlaybackObservation(
            double playbackTimeSec,
            bool hasPresentedVideoTime,
            double presentedVideoTimeSec,
            bool hasRuntimeHealth,
            double healthCurrentTimeSec,
            bool healthIsRealtime,
            double realtimeLagToleranceSeconds)
        {
            var referenceTimeSec = hasPresentedVideoTime
                ? presentedVideoTimeSec
                : playbackTimeSec;
            if (hasRuntimeHealth)
            {
                if (referenceTimeSec < 0.0)
                {
                    referenceTimeSec = healthCurrentTimeSec;
                }
                else if (healthIsRealtime
                    && healthCurrentTimeSec > referenceTimeSec + realtimeLagToleranceSeconds)
                {
                    referenceTimeSec = healthCurrentTimeSec;
                }
            }

            var referenceKind = hasPresentedVideoTime
                ? "presented_video"
                : "playback_time";
            if (hasRuntimeHealth)
            {
                if (playbackTimeSec < 0.0 && referenceTimeSec >= 0.0)
                {
                    referenceKind = "health_current_time";
                }
                else if (healthIsRealtime
                    && playbackTimeSec >= 0.0
                    && healthCurrentTimeSec > playbackTimeSec + realtimeLagToleranceSeconds)
                {
                    referenceKind = "health_current_time";
                }
            }

            return new ReferencePlaybackObservationView
            {
                ReferenceTimeSec = referenceTimeSec,
                ReferenceKind = referenceKind,
                HasSample = referenceTimeSec >= 0.0,
            };
        }

        private static string ResolveAuditString(string observed, string fallback)
        {
            if (!string.IsNullOrWhiteSpace(observed)
                && !string.Equals(observed, "n/a", StringComparison.OrdinalIgnoreCase))
            {
                return observed;
            }

            if (!string.IsNullOrWhiteSpace(fallback))
            {
                return fallback;
            }

            return "n/a";
        }

        internal static ValidationWindowStartObservationView
            CreatePullValidationWindowStartObservation(
                PullValidationGateInputsView inputs,
                float startupElapsedSeconds,
                float startupTimeoutSeconds)
        {
            return CreatePullValidationWindowStartObservation(
                inputs.HasTexture,
                inputs.RequireAudioOutput,
                inputs.AudioPlaying,
                startupElapsedSeconds,
                startupTimeoutSeconds);
        }

        internal static PullValidationAudioGatePolicyView CreatePullValidationAudioGatePolicy(
            bool requireAudioOutput,
            bool audioEnabled,
            AudioStartRuntimeCommandView runtimeCommand)
        {
            var effectiveRequireAudioOutput = false;
            var source = "config_disabled";
            if (requireAudioOutput)
            {
                if (runtimeCommand.ContractAvailable
                    && runtimeCommand.StateReported)
                {
                    effectiveRequireAudioOutput = runtimeCommand.ShouldPlay;
                    source = "runtime_" + runtimeCommand.Source;
                }
                else
                {
                    effectiveRequireAudioOutput = audioEnabled;
                    source = "player_enable_audio";
                }
            }

            return new PullValidationAudioGatePolicyView
            {
                RequireAudioOutput = effectiveRequireAudioOutput,
                AudioEnabled = audioEnabled,
                ConfiguredRequireAudioOutput = requireAudioOutput,
                Source = source,
                RuntimeContractAvailable = runtimeCommand.ContractAvailable,
                RuntimeStateReported = runtimeCommand.StateReported,
                RuntimeShouldPlay = runtimeCommand.ShouldPlay,
                RuntimeBlockReason = runtimeCommand.BlockReason,
            };
        }

        internal static ValidationAudioPlaybackObservationView CreateValidationAudioPlaybackObservation(
            AudioSource audioSource)
        {
            return new ValidationAudioPlaybackObservationView
            {
                SourcePresent = audioSource != null,
                Playing = audioSource != null && audioSource.isPlaying,
            };
        }

        internal static double ResolveValidationGatePlaybackTime(
            double playbackTimeSec,
            double referencePlaybackTimeSec)
        {
            return referencePlaybackTimeSec >= 0.0
                ? referencePlaybackTimeSec
                : playbackTimeSec;
        }

        internal static bool IsPlayerSessionRuntimePlaybackConfirmed(
            bool available,
            PlayerSessionContractView contract)
        {
            if (!available)
            {
                return false;
            }

            return contract.LifecycleState == 6
                || contract.PublicState == 3
                || contract.RuntimeState == 3;
        }

        internal static ValidationWindowStartObservationView
            CreatePullValidationWindowStartObservation(
                bool hasTexture,
                bool requireAudioOutput,
                bool audioPlaying,
                float startupElapsedSeconds,
                float startupTimeoutSeconds)
        {
            var outputsReady = hasTexture
                && (!requireAudioOutput || audioPlaying);
            if (outputsReady)
            {
                return new ValidationWindowStartObservationView
                {
                    ShouldStart = true,
                    Reason = "av-output-start",
                };
            }

            if (startupElapsedSeconds >= startupTimeoutSeconds)
            {
                return new ValidationWindowStartObservationView
                {
                    ShouldStart = true,
                    Reason = "startup-timeout",
                };
            }

            return new ValidationWindowStartObservationView
            {
                ShouldStart = false,
                Reason = string.Empty,
            };
        }

        internal static ValidationWindowStartObservationView
            CreateMediaPlayerValidationWindowStartObservation(
                MediaPlayerValidationGateInputsView inputs,
                float startupElapsedSeconds,
                float startupTimeoutSeconds)
        {
            return CreateMediaPlayerValidationWindowStartObservation(
                inputs.Started || inputs.RuntimePlaybackConfirmed,
                inputs.PlaybackTimeSec,
                inputs.HasPresentedNativeVideoFrame,
                startupElapsedSeconds,
                startupTimeoutSeconds);
        }

        internal static ValidationWindowStartObservationView
            CreateMediaPlayerValidationWindowStartObservation(
                bool started,
                double playbackTimeSec,
                bool hasPresentedNativeVideoFrame,
                float startupElapsedSeconds,
                float startupTimeoutSeconds)
        {
            var outputsReady = started
                || playbackTimeSec >= 0.1
                || hasPresentedNativeVideoFrame;
            if (outputsReady)
            {
                return new ValidationWindowStartObservationView
                {
                    ShouldStart = true,
                    Reason = "playback-start",
                };
            }

            if (startupElapsedSeconds >= startupTimeoutSeconds)
            {
                return new ValidationWindowStartObservationView
                {
                    ShouldStart = true,
                    Reason = "startup-timeout",
                };
            }

            return new ValidationWindowStartObservationView
            {
                ShouldStart = false,
                Reason = string.Empty,
            };
        }

        private static double ComputeValidationPlaybackAdvanceSeconds(
            double initialPlaybackTimeSec,
            double maxObservedPlaybackTimeSec)
        {
            if (maxObservedPlaybackTimeSec >= 0.0
                && initialPlaybackTimeSec >= 0.0)
            {
                return maxObservedPlaybackTimeSec - initialPlaybackTimeSec;
            }

            return 0.0;
        }

        internal static ValidationResultObservationView
            CreatePullValidationResultObservation(
                string validationWindowStartReason,
                ValidationWindowEvidenceObservationView evidenceObservation,
                bool requireAudioOutput,
                double minimumPlaybackAdvanceSeconds,
                double validationWindowInitialPlaybackTimeSec)
        {
            return CreatePullValidationResultObservation(
                validationWindowStartReason,
                evidenceObservation.ObservedStartedDuringWindow,
                evidenceObservation.ObservedTextureDuringWindow,
                evidenceObservation.ObservedAudioDuringWindow,
                requireAudioOutput,
                minimumPlaybackAdvanceSeconds,
                validationWindowInitialPlaybackTimeSec,
                evidenceObservation.MaxObservedPlaybackTime);
        }

        internal static ValidationResultObservationView
            CreatePullValidationResultObservation(
                string validationWindowStartReason,
                bool observedStartedDuringWindow,
                bool observedTextureDuringWindow,
                bool observedAudioDuringWindow,
                bool requireAudioOutput,
                double minimumPlaybackAdvanceSeconds,
                double validationWindowInitialPlaybackTimeSec,
                double maxObservedPlaybackTimeSec)
        {
            var playbackAdvanceSeconds = ComputeValidationPlaybackAdvanceSeconds(
                validationWindowInitialPlaybackTimeSec,
                maxObservedPlaybackTimeSec);

            if (validationWindowStartReason == "startup-timeout"
                && !observedStartedDuringWindow)
            {
                return new ValidationResultObservationView
                {
                    Passed = false,
                    Reason = "startup-timeout-no-playback",
                    PlaybackAdvanceSeconds = playbackAdvanceSeconds,
                };
            }

            if (!observedStartedDuringWindow)
            {
                return new ValidationResultObservationView
                {
                    Passed = false,
                    Reason = "playback-not-started",
                    PlaybackAdvanceSeconds = playbackAdvanceSeconds,
                };
            }

            if (!observedTextureDuringWindow)
            {
                return new ValidationResultObservationView
                {
                    Passed = false,
                    Reason = "missing-video-frame",
                    PlaybackAdvanceSeconds = playbackAdvanceSeconds,
                };
            }

            if (requireAudioOutput
                && !observedAudioDuringWindow)
            {
                return new ValidationResultObservationView
                {
                    Passed = false,
                    Reason = "audio-not-playing",
                    PlaybackAdvanceSeconds = playbackAdvanceSeconds,
                };
            }

            if (playbackAdvanceSeconds < minimumPlaybackAdvanceSeconds)
            {
                return new ValidationResultObservationView
                {
                    Passed = false,
                    Reason = "playback-stalled",
                    PlaybackAdvanceSeconds = playbackAdvanceSeconds,
                };
            }

            return new ValidationResultObservationView
            {
                Passed = true,
                Reason = "steady-playback",
                PlaybackAdvanceSeconds = playbackAdvanceSeconds,
            };
        }

        internal static ValidationResultObservationView
            CreateMediaPlayerValidationResultObservation(
                string validationWindowStartReason,
                ValidationWindowEvidenceObservationView evidenceObservation,
                double minimumPlaybackAdvanceSeconds,
                double validationWindowInitialPlaybackTimeSec)
        {
            return CreateMediaPlayerValidationResultObservation(
                validationWindowStartReason,
                evidenceObservation.ObservedStartedDuringWindow,
                evidenceObservation.ObservedTextureDuringWindow,
                evidenceObservation.ObservedNativeFrameDuringWindow,
                evidenceObservation.ObservedAudioDuringWindow,
                minimumPlaybackAdvanceSeconds,
                validationWindowInitialPlaybackTimeSec,
                evidenceObservation.MaxObservedPlaybackTime);
        }

        internal static ValidationResultObservationView
            CreateMediaPlayerValidationResultObservation(
                string validationWindowStartReason,
                bool observedStartedDuringWindow,
                bool observedTextureDuringWindow,
                bool observedNativeFrameDuringWindow,
                bool observedAudioDuringWindow,
                double minimumPlaybackAdvanceSeconds,
                double validationWindowInitialPlaybackTimeSec,
                double maxObservedPlaybackTimeSec)
        {
            var playbackAdvanceSeconds = ComputeValidationPlaybackAdvanceSeconds(
                validationWindowInitialPlaybackTimeSec,
                maxObservedPlaybackTimeSec);

            if (validationWindowStartReason == "startup-timeout"
                && !observedStartedDuringWindow)
            {
                return new ValidationResultObservationView
                {
                    Passed = false,
                    Reason = "startup-timeout-no-playback",
                    PlaybackAdvanceSeconds = playbackAdvanceSeconds,
                };
            }

            if (!observedStartedDuringWindow)
            {
                return new ValidationResultObservationView
                {
                    Passed = false,
                    Reason = "playback-not-started",
                    PlaybackAdvanceSeconds = playbackAdvanceSeconds,
                };
            }

            if (playbackAdvanceSeconds < minimumPlaybackAdvanceSeconds)
            {
                return new ValidationResultObservationView
                {
                    Passed = false,
                    Reason = "playback-stalled",
                    PlaybackAdvanceSeconds = playbackAdvanceSeconds,
                };
            }

            if (!observedTextureDuringWindow && !observedNativeFrameDuringWindow)
            {
                return new ValidationResultObservationView
                {
                    Passed = false,
                    Reason = "missing-video-signal",
                    PlaybackAdvanceSeconds = playbackAdvanceSeconds,
                };
            }

            return new ValidationResultObservationView
            {
                Passed = true,
                Reason = "steady-playback",
                PlaybackAdvanceSeconds = playbackAdvanceSeconds,
            };
        }

        internal static string CreateValidationWindowStartedLogLine(
            string logPrefix,
            string reason,
            float startupElapsedSeconds)
        {
            return string.Format(
                "[{0}] validation_window_started reason={1} startup_elapsed={2:F3}s",
                logPrefix,
                reason,
                startupElapsedSeconds);
        }

        internal static string CreateValidationResultFailedLogLine(
            string logPrefix,
            ValidationResultObservationView resultObservation)
        {
            if (resultObservation.Reason == "playback-stalled")
            {
                return string.Format(
                    "[{0}] result=failed reason=playback-stalled advance={1:F3}s",
                    logPrefix,
                    resultObservation.PlaybackAdvanceSeconds);
            }

            return string.Format(
                "[{0}] result=failed reason={1}",
                logPrefix,
                resultObservation.Reason);
        }

        internal static string CreateValidationResultPassedLogLine(
            string logPrefix,
            ValidationResultObservationView resultObservation,
            string sourceState,
            string sourceTimeouts,
            string sourceReconnects)
        {
            return string.Format(
                "[{0}] result=passed reason={1} advance={2:F3}s sourceState={3} sourceTimeouts={4} sourceReconnects={5}",
                logPrefix,
                resultObservation.Reason,
                resultObservation.PlaybackAdvanceSeconds,
                sourceState,
                sourceTimeouts,
                sourceReconnects);
        }

        internal static string CreateValidationCompleteLogLine(string logPrefix)
        {
            return string.Format("[{0}] complete", logPrefix);
        }

        internal static string CreateSummaryWrittenLogLine(
            string logPrefix,
            string summaryPath)
        {
            return string.Format("[{0}] summary_written={1}", logPrefix, summaryPath);
        }

        internal static string CreateSummaryWriteFailedLogLine(
            string logPrefix,
            string errorMessage)
        {
            return string.Format("[{0}] summary_write_failed {1}", logPrefix, errorMessage);
        }

        internal static string CreateTimeReadFailedLogLine(
            string logPrefix,
            string errorMessage)
        {
            return string.Format("[{0}] time read failed: {1}", logPrefix, errorMessage);
        }

        internal static string CreateOverrideUriLogLine(
            string logPrefix,
            string overrideUri)
        {
            return string.Format("[{0}] override uri={1}", logPrefix, overrideUri);
        }

        internal static string CreateValidationStartEnterLogLine(string logPrefix)
        {
            return string.Format("[{0}] start_enter", logPrefix);
        }

        internal static string CreateValidationStartLogLine(
            string logPrefix,
            float validationSeconds,
            int requestedWidth,
            int requestedHeight,
            bool explicitWindow,
            string videoRenderer,
            bool requireAudioOutput)
        {
            return string.Format(
                "[{0}] start validation seconds={1:F1} requestedWindow={2}x{3} explicitWindow={4} video_renderer={5} require_audio_output={6}",
                logPrefix,
                validationSeconds,
                requestedWidth,
                requestedHeight,
                explicitWindow,
                videoRenderer,
                requireAudioOutput);
        }

        internal static string CreateOverrideBackendLogLine(
            string logPrefix,
            string backend,
            bool strict)
        {
            return string.Format(
                "[{0}] override backend={1} strict={2}",
                logPrefix,
                backend,
                strict);
        }

        internal static string CreateOverrideVideoRendererLogLine(
            string logPrefix,
            string videoRenderer)
        {
            return string.Format(
                "[{0}] override video_renderer={1}",
                logPrefix,
                videoRenderer);
        }

        internal static string CreateOverrideLoopLogLine(
            string logPrefix,
            bool loopEnabled)
        {
            return string.Format("[{0}] override loop={1}", logPrefix, loopEnabled);
        }

        internal static string CreateMissingComponentLogLine(
            string logPrefix,
            string componentName)
        {
            return string.Format("[{0}] missing {1}", logPrefix, componentName);
        }

        internal static string CreateRunInBackgroundEnabledLogLine(string logPrefix)
        {
            return string.Format("[{0}] runInBackground=True", logPrefix);
        }

        internal static string CreateFramePacingLogLine(
            string logPrefix,
            int targetFrameRate,
            int vSyncCount)
        {
            return string.Format(
                "[{0}] frame_pacing targetFrameRate={1} vSyncCount={2}",
                logPrefix,
                targetFrameRate,
                vSyncCount);
        }

        internal static string CreateWindowConfiguredLogLine(
            string logPrefix,
            int width,
            int height,
            string reason,
            bool fullscreen,
            FullScreenMode mode)
        {
            return string.Format(
                "[{0}] window_configured={1}x{2} reason={3} fullscreen={4} mode={5}",
                logPrefix,
                width,
                height,
                reason,
                fullscreen,
                mode);
        }

        internal static string CreateIgnoreUnknownBackendLogLine(
            string logPrefix,
            string rawValue)
        {
            return string.Format("[{0}] ignore unknown backend={1}", logPrefix, rawValue);
        }

        internal static string CreateIgnoreUnknownVideoRendererLogLine(
            string logPrefix,
            string rawValue)
        {
            return string.Format("[{0}] ignore unknown video_renderer={1}", logPrefix, rawValue);
        }

        internal static string CreateAndroidStopAfterSummaryFailedLogLine(
            string logPrefix,
            string errorMessage)
        {
            return string.Format(
                "[{0}] android_stop_after_summary_failed {1}",
                logPrefix,
                errorMessage);
        }

        internal static string CreateAndroidMoveTaskToBackSkippedLogLine(
            string logPrefix,
            string reason)
        {
            return string.Format(
                "[{0}] android_move_task_to_back skipped {1}",
                logPrefix,
                reason);
        }

        internal static string CreateAndroidMoveTaskToBackLogLine(
            string logPrefix,
            bool moved)
        {
            return string.Format("[{0}] android_move_task_to_back={1}", logPrefix, moved);
        }

        internal static string CreateAndroidMoveTaskToBackFailedLogLine(
            string logPrefix,
            string errorMessage)
        {
            return string.Format(
                "[{0}] android_move_task_to_back_failed {1}",
                logPrefix,
                errorMessage);
        }

        internal static string CreateValidationStatusLogLine(
            string logPrefix,
            double playbackTime,
            bool hasTexture,
            bool audioPlaying,
            bool started,
            double startupElapsedSeconds,
            object sourceState,
            object sourcePackets,
            object sourceTimeouts,
            object sourceReconnects,
            int windowWidth,
            int windowHeight,
            int textureWidth,
            int textureHeight,
            bool fullscreen,
            FullScreenMode fullscreenMode,
            object actualBackend,
            object requestedVideoRenderer,
            object actualVideoRenderer,
            bool hasFrameContract,
            object frameContractMemoryKind,
            object frameContractDynamicRange,
            double frameContractNominalFps,
            bool hasPlaybackTimingContract,
            double playbackContractMasterTimeSec,
            bool hasAvSyncContract,
            object avSyncContractMasterClock,
            double avSyncContractDriftMs,
            bool hasBridgeDescriptor,
            object bridgeDescriptorState,
            object bridgeDescriptorRuntimeKind,
            bool bridgeDescriptorZeroCopySupported,
            bool bridgeDescriptorDirectBindable,
            bool bridgeDescriptorSourcePlaneTexturesSupported,
            bool bridgeDescriptorFallbackCopyPath,
            bool hasPathSelection,
            object pathSelectionKind,
            object pathSelectionSourceMemoryKind,
            object pathSelectionPresentedMemoryKind,
            bool pathSelectionTargetZeroCopy,
            bool pathSelectionSourcePlaneTexturesSupported,
            bool pathSelectionCpuFallback,
            bool hasSourceTimelineContract,
            object sourceTimelineModel,
            bool hasPlayerSessionContract,
            object playerSessionLifecycleState,
            bool hasAvSyncEnterpriseMetrics,
            uint avSyncEnterpriseSampleCount)
        {
            return string.Format(
                "[{0}] time={1:F3}s texture={2} audioPlaying={3} started={4} startupElapsed={5:F3}s sourceState={6} sourcePackets={7} sourceTimeouts={8} sourceReconnects={9} window={10}x{11} textureSize={12}x{13} fullscreen={14} mode={15} backend={16} requested_renderer={17} actual_renderer={18} frame_contract_available={19} frame_contract_memory={20} frame_contract_dynamic_range={21} frame_contract_nominal_fps={22:F2} playback_contract_available={23} playback_contract_master_sec={24:F3} av_sync_contract_available={25} av_sync_contract_master={26} av_sync_contract_drift_ms={27:F1} bridge_descriptor_available={28} bridge_descriptor_state={29} bridge_descriptor_runtime={30} bridge_descriptor_zero_copy={31} bridge_descriptor_direct_bindable={32} bridge_descriptor_source_plane_textures={33} bridge_descriptor_fallback_copy={34} path_selection_available={35} path_selection_kind={36} path_selection_source_memory={37} path_selection_presented_memory={38} path_selection_target_zero_copy={39} path_selection_source_plane_textures={40} path_selection_cpu_fallback={41} source_timeline_available={42} source_timeline_model={43} player_session_available={44} player_session_lifecycle={45} av_sync_enterprise_available={46} av_sync_enterprise_sample_count={47}",
                logPrefix,
                playbackTime,
                hasTexture,
                audioPlaying,
                started,
                startupElapsedSeconds,
                sourceState,
                sourcePackets,
                sourceTimeouts,
                sourceReconnects,
                windowWidth,
                windowHeight,
                textureWidth,
                textureHeight,
                fullscreen,
                fullscreenMode,
                actualBackend,
                requestedVideoRenderer,
                actualVideoRenderer,
                hasFrameContract,
                frameContractMemoryKind,
                frameContractDynamicRange,
                frameContractNominalFps,
                hasPlaybackTimingContract,
                playbackContractMasterTimeSec,
                hasAvSyncContract,
                avSyncContractMasterClock,
                avSyncContractDriftMs,
                hasBridgeDescriptor,
                bridgeDescriptorState,
                bridgeDescriptorRuntimeKind,
                bridgeDescriptorZeroCopySupported,
                bridgeDescriptorDirectBindable,
                bridgeDescriptorSourcePlaneTexturesSupported,
                bridgeDescriptorFallbackCopyPath,
                hasPathSelection,
                pathSelectionKind,
                pathSelectionSourceMemoryKind,
                pathSelectionPresentedMemoryKind,
                pathSelectionTargetZeroCopy,
                pathSelectionSourcePlaneTexturesSupported,
                pathSelectionCpuFallback,
                hasSourceTimelineContract,
                sourceTimelineModel,
                hasPlayerSessionContract,
                playerSessionLifecycleState,
                hasAvSyncEnterpriseMetrics,
                avSyncEnterpriseSampleCount);
        }

        internal static string CreateRuntimeHealthLogLine(
            string logPrefix,
            object runtimeStatePublic,
            object runtimeStateInternal,
            object playbackIntent,
            int streamCount,
            int videoDecoderCount,
            bool hasAudioDecoder,
            double sourceLastActivityAgeSec)
        {
            return string.Format(
                "[{0}] runtime_health state={1} runtime_state={2} playback_intent={3} stream_count={4} video_decoder_count={5} has_audio_decoder={6} source_last_activity_age_sec={7:F3}",
                logPrefix,
                runtimeStatePublic,
                runtimeStateInternal,
                playbackIntent,
                streamCount,
                videoDecoderCount,
                hasAudioDecoder,
                sourceLastActivityAgeSec);
        }

        internal static string CreateWgpuDescriptorLogLine(
            string logPrefix,
            bool wgpuRuntimeReady,
            int wgpuOutputWidth,
            int wgpuOutputHeight,
            bool wgpuSupportsYuv420p,
            bool wgpuSupportsNv12,
            bool wgpuSupportsP010,
            bool wgpuSupportsRgba32,
            bool wgpuSupportsExternalTextureRgba,
            bool wgpuSupportsExternalTextureYu12,
            bool wgpuReadbackExportSupported)
        {
            return string.Format(
                "[{0}] wgpu_descriptor runtime_ready={1} output_width={2} output_height={3} supports_yuv420p={4} supports_nv12={5} supports_p010={6} supports_rgba32={7} supports_external_texture_rgba={8} supports_external_texture_yu12={9} readback_export_supported={10}",
                logPrefix,
                wgpuRuntimeReady,
                wgpuOutputWidth,
                wgpuOutputHeight,
                wgpuSupportsYuv420p,
                wgpuSupportsNv12,
                wgpuSupportsP010,
                wgpuSupportsRgba32,
                wgpuSupportsExternalTextureRgba,
                wgpuSupportsExternalTextureYu12,
                wgpuReadbackExportSupported);
        }

        internal static string CreateWgpuStateLogLine(
            string logPrefix,
            object wgpuRenderPath,
            object wgpuSourceMemoryKind,
            object wgpuPresentedMemoryKind,
            object wgpuSourcePixelFormat,
            object wgpuPresentedPixelFormat,
            object wgpuExternalTextureFormat,
            bool wgpuHasRenderedFrame,
            long wgpuRenderedFrameIndex,
            double wgpuRenderedTimeSec,
            bool wgpuHasRenderError,
            object wgpuRenderErrorKind,
            int wgpuUploadPlaneCount,
            bool wgpuSourceZeroCopy,
            bool wgpuCpuFallback)
        {
            return string.Format(
                "[{0}] wgpu_state render_path={1} source_memory={2} presented_memory={3} source_pixel_format={4} presented_pixel_format={5} external_texture_format={6} has_rendered_frame={7} rendered_frame_index={8} rendered_time_sec={9:F3} has_render_error={10} render_error_kind={11} upload_plane_count={12} source_zero_copy={13} cpu_fallback={14}",
                logPrefix,
                wgpuRenderPath,
                wgpuSourceMemoryKind,
                wgpuPresentedMemoryKind,
                wgpuSourcePixelFormat,
                wgpuPresentedPixelFormat,
                wgpuExternalTextureFormat,
                wgpuHasRenderedFrame,
                wgpuRenderedFrameIndex,
                wgpuRenderedTimeSec,
                wgpuHasRenderError,
                wgpuRenderErrorKind,
                wgpuUploadPlaneCount,
                wgpuSourceZeroCopy,
                wgpuCpuFallback);
        }

        internal static string CreatePlaybackContractLogLine(
            string logPrefix,
            double masterTimeSec,
            long masterTimeUs,
            double externalTimeSec,
            long externalTimeUs,
            bool hasAudioTimeSec,
            double audioTimeSec,
            bool hasAudioTimeUs,
            long audioTimeUs,
            bool hasAudioPresentedTimeSec,
            double audioPresentedTimeSec,
            bool hasAudioPresentedTimeUs,
            long audioPresentedTimeUs,
            double audioSinkDelayMs,
            long audioSinkDelayUs,
            bool hasAudioClock,
            bool hasMicrosecondMirror)
        {
            return string.Format(
                "[{0}] playback_contract master_sec={1:F3} master_us={2} external_sec={3:F3} external_us={4} has_audio_time_sec={5} audio_time_sec={6:F3} has_audio_time_us={7} audio_time_us={8} has_audio_presented_time_sec={9} audio_presented_time_sec={10:F3} has_audio_presented_time_us={11} audio_presented_time_us={12} audio_sink_delay_ms={13:F1} audio_sink_delay_us={14} has_audio_clock={15} has_us_mirror={16}",
                logPrefix,
                masterTimeSec,
                masterTimeUs,
                externalTimeSec,
                externalTimeUs,
                hasAudioTimeSec,
                audioTimeSec,
                hasAudioTimeUs,
                audioTimeUs,
                hasAudioPresentedTimeSec,
                audioPresentedTimeSec,
                hasAudioPresentedTimeUs,
                audioPresentedTimeUs,
                audioSinkDelayMs,
                audioSinkDelayUs,
                hasAudioClock,
                hasMicrosecondMirror);
        }

        internal static string CreateAvSyncContractLogLine(
            string logPrefix,
            object masterClock,
            bool hasAudioClockSec,
            double audioClockSec,
            bool hasVideoClockSec,
            double videoClockSec,
            double clockDeltaMs,
            double driftMs,
            bool startupWarmupComplete,
            ulong dropTotal,
            ulong duplicateTotal)
        {
            return string.Format(
                "[{0}] av_sync_contract master={1} has_audio_clock_sec={2} audio_clock_sec={3:F3} has_video_clock_sec={4} video_clock_sec={5:F3} clock_delta_ms={6:F1} drift_ms={7:F1} warmup_complete={8} drop_total={9} duplicate_total={10}",
                logPrefix,
                masterClock,
                hasAudioClockSec,
                audioClockSec,
                hasVideoClockSec,
                videoClockSec,
                clockDeltaMs,
                driftMs,
                startupWarmupComplete,
                dropTotal,
                duplicateTotal);
        }

        internal static string CreateAvSyncContractSampleLogLine(
            string logPrefix,
            double clockDeltaMs,
            double audioClockSec,
            double videoClockSec)
        {
            return string.Format(
                "[{0}] av_sync_contract_sample delta_ms={1:F1} audio_clock_sec={2:F3} video_clock_sec={3:F3}",
                logPrefix,
                clockDeltaMs,
                audioClockSec,
                videoClockSec);
        }

        internal static string CreateSourceTimelineLogLine(
            string logPrefix,
            object model,
            object anchorKind,
            bool hasCurrentSourceTimeUs,
            long currentSourceTimeUs,
            bool hasTimelineOriginUs,
            long timelineOriginUs,
            bool hasAnchorValueUs,
            long anchorValueUs,
            bool hasAnchorMonoUs,
            long anchorMonoUs,
            bool isRealtime)
        {
            return string.Format(
                "[{0}] source_timeline model={1} anchor_kind={2} has_current_source_time_us={3} current_source_time_us={4} has_timeline_origin_us={5} timeline_origin_us={6} has_anchor_value_us={7} anchor_value_us={8} has_anchor_mono_us={9} anchor_mono_us={10} is_realtime={11}",
                logPrefix,
                model,
                anchorKind,
                hasCurrentSourceTimeUs,
                currentSourceTimeUs,
                hasTimelineOriginUs,
                timelineOriginUs,
                hasAnchorValueUs,
                anchorValueUs,
                hasAnchorMonoUs,
                anchorMonoUs,
                isRealtime);
        }

        internal static string CreateValidationPlayerSessionLogLine(
            string logPrefix,
            object lifecycleState,
            object publicState,
            object runtimeState,
            object playbackIntent,
            object stopReason,
            object sourceState,
            bool canSeek,
            bool isRealtime,
            bool isBuffering,
            bool isSyncing)
        {
            return string.Format(
                "[{0}] player_session lifecycle_state={1} public_state={2} runtime_state={3} playback_intent={4} stop_reason={5} source_state={6} can_seek={7} is_realtime={8} is_buffering={9} is_syncing={10}",
                logPrefix,
                lifecycleState,
                publicState,
                runtimeState,
                playbackIntent,
                stopReason,
                sourceState,
                canSeek,
                isRealtime,
                isBuffering,
                isSyncing);
        }

        internal static string CreateAudioOutputPolicyLogLine(
            string logPrefix,
            int fileStartThresholdMs,
            int androidFileStartThresholdMs,
            int realtimeStartThresholdMs,
            int realtimeStartupGraceMs,
            int realtimeStartupMinimumThresholdMs,
            int fileRingCapacityMs,
            int androidFileRingCapacityMs,
            int realtimeRingCapacityMs,
            int fileBufferedCeilingMs,
            int androidFileBufferedCeilingMs,
            int realtimeBufferedCeilingMs,
            int realtimeStartupAdditionalSinkDelayMs,
            int realtimeSteadyAdditionalSinkDelayMs,
            int realtimeBackendAdditionalSinkDelayMs,
            bool realtimeStartRequiresVideoFrame,
            bool allowAndroidFileOutputRateBridge)
        {
            return string.Format(
                "[{0}] audio_output_policy file_start_ms={1} android_file_start_ms={2} realtime_start_ms={3} realtime_startup_grace_ms={4} realtime_startup_minimum_threshold_ms={5} file_ring_capacity_ms={6} android_file_ring_capacity_ms={7} realtime_ring_capacity_ms={8} file_buffered_ceiling_ms={9} android_file_buffered_ceiling_ms={10} realtime_buffered_ceiling_ms={11} realtime_startup_additional_sink_delay_ms={12} realtime_steady_additional_sink_delay_ms={13} realtime_backend_additional_sink_delay_ms={14} realtime_start_requires_video_frame={15} allow_android_file_output_rate_bridge={16}",
                logPrefix,
                fileStartThresholdMs,
                androidFileStartThresholdMs,
                realtimeStartThresholdMs,
                realtimeStartupGraceMs,
                realtimeStartupMinimumThresholdMs,
                fileRingCapacityMs,
                androidFileRingCapacityMs,
                realtimeRingCapacityMs,
                fileBufferedCeilingMs,
                androidFileBufferedCeilingMs,
                realtimeBufferedCeilingMs,
                realtimeStartupAdditionalSinkDelayMs,
                realtimeSteadyAdditionalSinkDelayMs,
                realtimeBackendAdditionalSinkDelayMs,
                realtimeStartRequiresVideoFrame,
                allowAndroidFileOutputRateBridge);
        }

        internal static string CreateAvSyncEnterpriseLogLine(
            string logPrefix,
            uint sampleCount,
            long windowSpanUs,
            long latestRawOffsetUs,
            long latestSmoothOffsetUs,
            double driftSlopePpm,
            double driftProjected2hMs,
            long offsetAbsP95Us,
            long offsetAbsP99Us,
            long offsetAbsMaxUs)
        {
            return string.Format(
                "[{0}] av_sync_enterprise sample_count={1} window_span_us={2} latest_raw_offset_us={3} latest_smooth_offset_us={4} drift_slope_ppm={5:F3} drift_projected_2h_ms={6:F3} offset_abs_p95_us={7} offset_abs_p99_us={8} offset_abs_max_us={9}",
                logPrefix,
                sampleCount,
                windowSpanUs,
                latestRawOffsetUs,
                latestSmoothOffsetUs,
                driftSlopePpm,
                driftProjected2hMs,
                offsetAbsP95Us,
                offsetAbsP99Us,
                offsetAbsMaxUs);
        }

        internal static string CreatePassiveAvSyncLogLine(
            string logPrefix,
            long rawOffsetUs,
            long smoothOffsetUs,
            double driftPpm,
            long driftInterceptUs,
            uint driftSampleCount,
            object videoSchedule,
            double audioResampleRatio,
            bool audioResampleActive,
            bool shouldRebuildAnchor)
        {
            return string.Format(
                "[{0}] passive_av_sync raw_offset_us={1} smooth_offset_us={2} drift_ppm={3:F3} drift_intercept_us={4} drift_sample_count={5} video_schedule={6} audio_resample_ratio={7:F6} audio_resample_active={8} should_rebuild_anchor={9}",
                logPrefix,
                rawOffsetUs,
                smoothOffsetUs,
                driftPpm,
                driftInterceptUs,
                driftSampleCount,
                videoSchedule,
                audioResampleRatio,
                audioResampleActive,
                shouldRebuildAnchor);
        }

        internal static string CreateAvSyncLogLine(
            string logPrefix,
            double deltaMilliseconds,
            double audioPresentedTimeSec,
            double referencePlaybackTimeSec,
            object referencePlaybackKind,
            double playbackTime,
            double presentedVideoTimeSec,
            double playbackContractAudioTimeSec,
            double playbackContractAudioPresentedTimeSec,
            double playbackContractAudioSinkDelayMs,
            double audioPipelineDelayMs)
        {
            return string.Format(
                "[{0}] av_sync delta_ms={1:F1} audio_presented_sec={2:F3} reference_sec={3:F3} reference_kind={4} playback_sec={5:F3} presented_video_sec={6:F3} contract_audio_time_sec={7:F3} contract_audio_presented_sec={8:F3} contract_audio_sink_delay_ms={9:F1} audio_pipeline_delay_ms={10:F1}",
                logPrefix,
                deltaMilliseconds,
                audioPresentedTimeSec,
                referencePlaybackTimeSec,
                referencePlaybackKind,
                playbackTime,
                presentedVideoTimeSec,
                playbackContractAudioTimeSec,
                playbackContractAudioPresentedTimeSec,
                playbackContractAudioSinkDelayMs,
                audioPipelineDelayMs);
        }

        internal static string CreateRealtimeLatencyLogLine(
            string logPrefix,
            double realtimeLatencyMilliseconds,
            double publisherElapsedTimeSec,
            double realtimeReferenceTimeSec)
        {
            return string.Format(
                "[{0}] realtime_latency latency_ms={1:F1} publisher_elapsed_sec={2:F3} reference_sec={3:F3}",
                logPrefix,
                realtimeLatencyMilliseconds,
                publisherElapsedTimeSec,
                realtimeReferenceTimeSec);
        }

        internal static string CreateRealtimeProbeLogLine(
            string logPrefix,
            long realtimeProbeUnixMs,
            double realtimeReferenceTimeSec)
        {
            return string.Format(
                "[{0}] realtime_probe unix_ms={1} reference_sec={2:F3}",
                logPrefix,
                realtimeProbeUnixMs,
                realtimeReferenceTimeSec);
        }

        internal static string CreateMediaPlayerAuditStartLogLine(
            string logPrefix,
            float validationSeconds,
            int requestedWidth,
            int requestedHeight,
            bool explicitWindow)
        {
            return string.Format(
                "[{0}] start validation seconds={1:F1} requestedWindow={2}x{3} explicitWindow={4} media_player_audit=True",
                logPrefix,
                validationSeconds,
                requestedWidth,
                requestedHeight,
                explicitWindow);
        }

        internal static string CreateMediaPlayerAuditStatusLogLine(
            string logPrefix,
            double playbackTime,
            bool hasTexture,
            bool audioPlaying,
            bool started,
            double startupElapsedSeconds,
            string sourceState,
            string sourcePackets,
            string sourceTimeouts,
            string sourceReconnects,
            int windowWidth,
            int windowHeight,
            int textureWidth,
            int textureHeight,
            bool fullscreen,
            FullScreenMode fullscreenMode,
            string actualBackend,
            string requestedRenderer,
            string actualRenderer,
            PlaybackTimingAuditStringsView playbackContract,
            SourceTimelineAuditStringsView sourceTimeline,
            PlayerSessionAuditStringsView playerSession,
            AudioOutputPolicyAuditStringsView audioOutputPolicy,
            AvSyncEnterpriseAuditStringsView avSyncEnterprise)
        {
            return string.Format(
                "[{0}] time={1:F3}s texture={2} audioPlaying={3} started={4} startupElapsed={5:F3}s sourceState={6} sourcePackets={7} sourceTimeouts={8} sourceReconnects={9} window={10}x{11} textureSize={12}x{13} fullscreen={14} mode={15} backend={16} requested_renderer={17} actual_renderer={18} playback_contract_available={19} playback_contract_master_sec={20} playback_contract_has_us_mirror={21} source_timeline_available={22} source_timeline_model={23} source_timeline_anchor_kind={24} player_session_available={25} player_session_lifecycle={26} player_session_is_realtime={27} audio_output_policy_available={28} av_sync_enterprise_available={29} av_sync_enterprise_sample_count={30} av_sync_enterprise_drift_projected_2h_ms={31}",
                logPrefix,
                playbackTime,
                hasTexture,
                audioPlaying,
                started,
                startupElapsedSeconds,
                sourceState,
                sourcePackets,
                sourceTimeouts,
                sourceReconnects,
                windowWidth,
                windowHeight,
                textureWidth,
                textureHeight,
                fullscreen,
                fullscreenMode,
                actualBackend,
                requestedRenderer,
                actualRenderer,
                playbackContract.Available,
                playbackContract.MasterTimeSec,
                playbackContract.HasMicrosecondMirror,
                sourceTimeline.Available,
                sourceTimeline.Model,
                sourceTimeline.AnchorKind,
                playerSession.Available,
                playerSession.LifecycleState,
                playerSession.IsRealtime,
                audioOutputPolicy.Available,
                avSyncEnterprise.Available,
                avSyncEnterprise.SampleCount,
                avSyncEnterprise.DriftProjected2hMs);
        }

        internal static string CreateMediaPlayerAuditDetailLogLine(
            string logPrefix,
            bool audioSourcePresent,
            bool hasAudioListener,
            bool nativeVideoActive,
            string nativeActivationDecision,
            bool hasPresentedNativeVideoFrame,
            string actualBackend)
        {
            return string.Format(
                "[{0}] media_player_audit audioSourcePresent={1} hasAudioListener={2} nativeVideoActive={3} nativeActivationDecision={4} nativeFrame={5} actualBackend={6}",
                logPrefix,
                audioSourcePresent,
                hasAudioListener,
                nativeVideoActive,
                nativeActivationDecision,
                hasPresentedNativeVideoFrame,
                actualBackend);
        }

        internal static string CreatePlayerSessionDetailLogLine(
            string logPrefix,
            string lifecycleState,
            string publicState,
            string runtimeState,
            string playbackIntent,
            string stopReason,
            string sourceState,
            string canSeek,
            string isRealtime,
            string isBuffering,
            string isSyncing)
        {
            return string.Format(
                "[{0}] player_session_detail lifecycle={1} public={2} runtime={3} intent={4} stop_reason={5} source_state={6} can_seek={7} realtime={8} buffering={9} syncing={10}",
                logPrefix,
                lifecycleState,
                publicState,
                runtimeState,
                playbackIntent,
                stopReason,
                sourceState,
                canSeek,
                isRealtime,
                isBuffering,
                isSyncing);
        }

        internal static ValidationVideoTextureObservationView
            CreatePullValidationVideoTextureObservation(
                bool hasPresentedVideoFrame,
                Texture texture)
        {
            var hasTexture = hasPresentedVideoFrame
                && texture != null;
            return new ValidationVideoTextureObservationView
            {
                HasTexture = hasTexture,
                TextureWidth = hasTexture ? texture.width : 0,
                TextureHeight = hasTexture ? texture.height : 0,
            };
        }

        internal static ValidationVideoTextureObservationView
            CreateMediaPlayerValidationVideoTextureObservation(
                Texture texture)
        {
            var hasTexture = texture != null;
            return new ValidationVideoTextureObservationView
            {
                HasTexture = hasTexture,
                TextureWidth = hasTexture ? texture.width : 0,
                TextureHeight = hasTexture ? texture.height : 0,
            };
        }

        internal static ValidationWindowEvidenceObservationView
            AccumulatePullValidationWindowEvidenceObservation(
                ValidationWindowEvidenceObservationView currentObservation,
                PullValidationGateInputsView inputs)
        {
            return AccumulateValidationWindowEvidenceObservation(
                currentObservation.ObservedTextureDuringWindow,
                currentObservation.ObservedAudioDuringWindow,
                currentObservation.ObservedStartedDuringWindow,
                currentObservation.ObservedNativeFrameDuringWindow,
                currentObservation.MaxObservedPlaybackTime,
                inputs.HasTexture,
                inputs.AudioPlaying,
                inputs.Started,
                inputs.HasPresentedNativeVideoFrame,
                inputs.PlaybackTimeSec);
        }

        internal static ValidationWindowEvidenceObservationView
            AccumulateMediaPlayerValidationWindowEvidenceObservation(
                ValidationWindowEvidenceObservationView currentObservation,
                MediaPlayerValidationGateInputsView inputs)
        {
            return AccumulateValidationWindowEvidenceObservation(
                currentObservation.ObservedTextureDuringWindow,
                currentObservation.ObservedAudioDuringWindow,
                currentObservation.ObservedStartedDuringWindow,
                currentObservation.ObservedNativeFrameDuringWindow,
                currentObservation.MaxObservedPlaybackTime,
                inputs.HasTexture,
                inputs.AudioPlaying,
                inputs.Started || inputs.RuntimePlaybackConfirmed,
                inputs.HasPresentedNativeVideoFrame,
                inputs.PlaybackTimeSec);
        }

        internal static ValidationWindowEvidenceObservationView
            AccumulateValidationWindowEvidenceObservation(
                bool observedTextureDuringWindow,
                bool observedAudioDuringWindow,
                bool observedStartedDuringWindow,
                bool observedNativeFrameDuringWindow,
                double maxObservedPlaybackTimeSec,
                bool hasTexture,
                bool audioPlaying,
                bool started,
                bool hasPresentedNativeVideoFrame,
                double playbackTimeSec)
        {
            return new ValidationWindowEvidenceObservationView
            {
                ObservedTextureDuringWindow =
                    observedTextureDuringWindow || hasTexture,
                ObservedAudioDuringWindow =
                    observedAudioDuringWindow || audioPlaying,
                ObservedStartedDuringWindow =
                    observedStartedDuringWindow || started,
                ObservedNativeFrameDuringWindow =
                    observedNativeFrameDuringWindow || hasPresentedNativeVideoFrame,
                MaxObservedPlaybackTime =
                    playbackTimeSec > maxObservedPlaybackTimeSec
                        ? playbackTimeSec
                        : maxObservedPlaybackTimeSec,
            };
        }

        internal static void AppendValidationSummaryHeader(
            StringBuilder builder,
            ValidationSummaryHeaderView summary)
        {
            builder.AppendLine("validation_result=" + (summary.Passed ? "passed" : "failed"));
            builder.AppendLine("reason=" + summary.Reason);
            builder.AppendLine("uri=" + summary.Uri);
            builder.AppendLine("requested_backend=" + summary.RequestedBackend);
            builder.AppendLine("actual_backend=" + summary.ActualBackend);
            if (summary.IncludeVideoRenderer)
            {
                builder.AppendLine("requested_video_renderer=" + summary.RequestedVideoRenderer);
                builder.AppendLine("actual_video_renderer=" + summary.ActualVideoRenderer);
            }

            if (summary.IncludeRequireAudioOutput)
            {
                builder.AppendLine("require_audio_output=" + summary.RequireAudioOutput);
            }

            if (summary.IncludeConfiguredRequireAudioOutput)
            {
                builder.AppendLine(
                    "configured_require_audio_output="
                    + summary.ConfiguredRequireAudioOutput);
            }

            if (summary.IncludeAudioGateConfiguredAudioEnabled)
            {
                builder.AppendLine(
                    "audio_gate_configured_audio_enabled="
                    + summary.AudioGateConfiguredAudioEnabled);
            }

            if (summary.IncludeAudioGateSource)
            {
                builder.AppendLine("audio_gate_source=" + summary.AudioGateSource);
            }

            if (summary.IncludeAudioGateRuntimeContractAvailable)
            {
                builder.AppendLine(
                    "audio_gate_runtime_contract_available="
                    + summary.AudioGateRuntimeContractAvailable);
            }

            if (summary.IncludeAudioGateRuntimeStateReported)
            {
                builder.AppendLine(
                    "audio_gate_runtime_state_reported="
                    + summary.AudioGateRuntimeStateReported);
            }

            if (summary.IncludeAudioGateRuntimeShouldPlay)
            {
                builder.AppendLine(
                    "audio_gate_runtime_should_play="
                    + summary.AudioGateRuntimeShouldPlay);
            }

            if (summary.IncludeAudioGateRuntimeBlockReason)
            {
                builder.AppendLine(
                    "audio_gate_runtime_block_reason="
                    + summary.AudioGateRuntimeBlockReason);
            }

            builder.AppendLine("playback_advance_sec=" + summary.PlaybackAdvanceSeconds.ToString("F3"));
        }

        internal static void AppendValidationSummaryWindow(
            StringBuilder builder,
            ValidationSummaryWindowView summary)
        {
            builder.AppendLine("has_texture=" + summary.HasTexture);
            builder.AppendLine("audio_playing=" + summary.AudioPlaying);
            builder.AppendLine("started=" + summary.Started);
            if (summary.IncludeAudioSourcePresent)
            {
                builder.AppendLine("audio_source_present=" + summary.AudioSourcePresent);
            }
            if (summary.IncludeHasAudioListener)
            {
                builder.AppendLine("has_audio_listener=" + summary.HasAudioListener);
            }
            builder.AppendLine("observed_texture_during_window=" + summary.ObservedTextureDuringWindow);
            builder.AppendLine("observed_audio_during_window=" + summary.ObservedAudioDuringWindow);
            builder.AppendLine("observed_started_during_window=" + summary.ObservedStartedDuringWindow);
            if (summary.IncludeObservedNativeFrameDuringWindow)
            {
                builder.AppendLine("observed_native_frame_during_window=" + summary.ObservedNativeFrameDuringWindow);
            }
            if (summary.IncludeValidationWindowStartReason)
            {
                builder.AppendLine("validation_window_start_reason=" + summary.ValidationWindowStartReason);
            }
        }

        internal static ValidationSummaryPlayerSessionView CreateValidationSummaryPlayerSession(
            bool observedAvailable,
            PlayerSessionAuditStringsView resolved,
            PlayerSessionAuditStringsView fallback)
        {
            return new ValidationSummaryPlayerSessionView
            {
                Available = observedAvailable,
                LifecycleState = resolved.LifecycleState,
                PublicState = resolved.PublicState,
                RuntimeState = resolved.RuntimeState,
                PlaybackIntent = resolved.PlaybackIntent,
                StopReason = resolved.StopReason,
                SourceState = resolved.SourceState,
                CanSeek = observedAvailable ? resolved.CanSeek : fallback.CanSeek,
                IsRealtime = observedAvailable ? resolved.IsRealtime : fallback.IsRealtime,
                IsBuffering = observedAvailable ? resolved.IsBuffering : fallback.IsBuffering,
                IsSyncing = observedAvailable ? resolved.IsSyncing : fallback.IsSyncing,
            };
        }

        internal static ValidationSummaryPlayerSessionView CreateValidationSummaryPlayerSession(
            bool observedAvailable,
            string observedLifecycleState,
            string observedPublicState,
            string observedRuntimeState,
            string observedPlaybackIntent,
            string observedStopReason,
            string observedSourceState,
            string observedCanSeek,
            string observedIsRealtime,
            string observedIsBuffering,
            string observedIsSyncing,
            PlayerSessionAuditStringsView fallback)
        {
            var resolved = CreateResolvedPlayerSessionAuditStrings(
                observedLifecycleState,
                observedPublicState,
                observedRuntimeState,
                observedPlaybackIntent,
                observedStopReason,
                observedSourceState,
                observedCanSeek,
                observedIsRealtime,
                observedIsBuffering,
                observedIsSyncing,
                fallback);
            return CreateValidationSummaryPlayerSession(observedAvailable, resolved, fallback);
        }

        internal static PlaybackTimingAuditStringsView CreateObservedPlaybackTimingAuditStrings(
            bool available,
            string masterTimeSec,
            string masterTimeUs,
            string externalTimeSec,
            string externalTimeUs,
            string hasMicrosecondMirror)
        {
            return new PlaybackTimingAuditStringsView
            {
                Available = available,
                MasterTimeSec = masterTimeSec,
                MasterTimeUs = masterTimeUs,
                ExternalTimeSec = externalTimeSec,
                ExternalTimeUs = externalTimeUs,
                HasMicrosecondMirror = hasMicrosecondMirror,
            };
        }

        internal static PlaybackTimingAuditStringsView CreateDefaultObservedPlaybackTimingAuditStrings()
        {
            return CreateObservedPlaybackTimingAuditStrings(
                false,
                "n/a",
                "n/a",
                "n/a",
                "n/a",
                "False");
        }

        internal static PlaybackTimingAuditStringsView CreateObservedPlaybackTimingAuditStringsExtended(
            bool available,
            double masterTimeSec,
            long masterTimeUs,
            double externalTimeSec,
            long externalTimeUs,
            bool hasAudioTimeSec,
            double audioTimeSec,
            bool hasAudioTimeUs,
            long audioTimeUs,
            bool hasAudioPresentedTimeSec,
            double audioPresentedTimeSec,
            bool hasAudioPresentedTimeUs,
            long audioPresentedTimeUs,
            double audioSinkDelaySec,
            long audioSinkDelayUs,
            bool hasMicrosecondMirror,
            bool hasAudioClock)
        {
            return CreateObservedPlaybackTimingAuditStringsExtended(
                available,
                masterTimeSec.ToString("F3"),
                masterTimeUs.ToString(),
                externalTimeSec.ToString("F3"),
                externalTimeUs.ToString(),
                hasAudioTimeSec.ToString(),
                audioTimeSec.ToString("F3"),
                hasAudioTimeUs.ToString(),
                audioTimeUs.ToString(),
                hasAudioPresentedTimeSec.ToString(),
                audioPresentedTimeSec.ToString("F3"),
                hasAudioPresentedTimeUs.ToString(),
                audioPresentedTimeUs.ToString(),
                (audioSinkDelaySec * 1000.0).ToString("F1"),
                audioSinkDelayUs.ToString(),
                hasMicrosecondMirror.ToString(),
                hasAudioClock.ToString());
        }

        internal static PlaybackTimingAuditStringsView CreateObservedPlaybackTimingAuditStringsExtended(
            bool available,
            string masterTimeSec,
            string masterTimeUs,
            string externalTimeSec,
            string externalTimeUs,
            string hasAudioTimeSec,
            string audioTimeSec,
            string hasAudioTimeUs,
            string audioTimeUs,
            string hasAudioPresentedTimeSec,
            string audioPresentedTimeSec,
            string hasAudioPresentedTimeUs,
            string audioPresentedTimeUs,
            string audioSinkDelaySec,
            string audioSinkDelayUs,
            string hasMicrosecondMirror,
            string hasAudioClock)
        {
            return new PlaybackTimingAuditStringsView
            {
                Available = available,
                MasterTimeSec = masterTimeSec,
                MasterTimeUs = masterTimeUs,
                ExternalTimeSec = externalTimeSec,
                ExternalTimeUs = externalTimeUs,
                HasAudioTimeSec = hasAudioTimeSec,
                AudioTimeSec = audioTimeSec,
                HasAudioTimeUs = hasAudioTimeUs,
                AudioTimeUs = audioTimeUs,
                HasAudioPresentedTimeSec = hasAudioPresentedTimeSec,
                AudioPresentedTimeSec = audioPresentedTimeSec,
                HasAudioPresentedTimeUs = hasAudioPresentedTimeUs,
                AudioPresentedTimeUs = audioPresentedTimeUs,
                AudioSinkDelaySec = audioSinkDelaySec,
                AudioSinkDelayUs = audioSinkDelayUs,
                HasMicrosecondMirror = hasMicrosecondMirror,
                HasAudioClock = hasAudioClock,
            };
        }

        internal static SourceTimelineAuditStringsView CreateObservedSourceTimelineAuditStrings(
            bool available,
            string model,
            string anchorKind,
            bool hasCurrentSourceTimeUs,
            long currentSourceTimeUs,
            bool hasTimelineOriginUs,
            long timelineOriginUs,
            bool hasAnchorValueUs,
            long anchorValueUs,
            bool hasAnchorMonoUs,
            long anchorMonoUs,
            bool isRealtime)
        {
            return CreateObservedSourceTimelineAuditStrings(
                available,
                model,
                anchorKind,
                hasCurrentSourceTimeUs.ToString(),
                currentSourceTimeUs.ToString(),
                hasTimelineOriginUs.ToString(),
                timelineOriginUs.ToString(),
                hasAnchorValueUs.ToString(),
                anchorValueUs.ToString(),
                hasAnchorMonoUs.ToString(),
                anchorMonoUs.ToString(),
                isRealtime.ToString());
        }

        internal static SourceTimelineAuditStringsView CreateObservedSourceTimelineAuditStrings(
            bool available,
            string model,
            string anchorKind,
            string hasCurrentSourceTimeUs,
            string currentSourceTimeUs,
            string hasTimelineOriginUs,
            string timelineOriginUs,
            string hasAnchorValueUs,
            string anchorValueUs,
            string hasAnchorMonoUs,
            string anchorMonoUs,
            string isRealtime)
        {
            return new SourceTimelineAuditStringsView
            {
                Available = available,
                Model = model,
                AnchorKind = anchorKind,
                HasCurrentSourceTimeUs = hasCurrentSourceTimeUs,
                CurrentSourceTimeUs = currentSourceTimeUs,
                HasTimelineOriginUs = hasTimelineOriginUs,
                TimelineOriginUs = timelineOriginUs,
                HasAnchorValueUs = hasAnchorValueUs,
                AnchorValueUs = anchorValueUs,
                HasAnchorMonoUs = hasAnchorMonoUs,
                AnchorMonoUs = anchorMonoUs,
                IsRealtime = isRealtime,
            };
        }

        internal static SourceTimelineAuditStringsView CreateDefaultObservedSourceTimelineAuditStrings()
        {
            return CreateObservedSourceTimelineAuditStrings(
                false,
                "n/a",
                "n/a",
                "False",
                "n/a",
                "False",
                "n/a",
                "False",
                "n/a",
                "False",
                "n/a",
                "False");
        }

        internal static PassiveAvSyncAuditStringsView CreateObservedPassiveAvSyncAuditStrings(
            bool available,
            long rawOffsetUs,
            long smoothOffsetUs,
            double driftPpm,
            long driftInterceptUs,
            uint driftSampleCount,
            string videoSchedule,
            double audioResampleRatio,
            bool audioResampleActive,
            bool shouldRebuildAnchor)
        {
            return CreateObservedPassiveAvSyncAuditStrings(
                available,
                rawOffsetUs.ToString(),
                smoothOffsetUs.ToString("F3"),
                driftPpm.ToString("F3"),
                driftInterceptUs.ToString(),
                driftSampleCount.ToString(),
                videoSchedule,
                audioResampleRatio.ToString("F6"),
                audioResampleActive.ToString(),
                shouldRebuildAnchor.ToString());
        }

        internal static PassiveAvSyncAuditStringsView CreateObservedPassiveAvSyncAuditStrings(
            bool available,
            string rawOffsetUs,
            string smoothOffsetUs,
            string driftPpm,
            string driftInterceptUs,
            string driftSampleCount,
            string videoSchedule,
            string audioResampleRatio,
            string audioResampleActive,
            string shouldRebuildAnchor)
        {
            return new PassiveAvSyncAuditStringsView
            {
                Available = available,
                RawOffsetUs = rawOffsetUs,
                SmoothOffsetUs = smoothOffsetUs,
                DriftPpm = driftPpm,
                DriftInterceptUs = driftInterceptUs,
                DriftSampleCount = driftSampleCount,
                VideoSchedule = videoSchedule,
                AudioResampleRatio = audioResampleRatio,
                AudioResampleActive = audioResampleActive,
                ShouldRebuildAnchor = shouldRebuildAnchor,
            };
        }

        internal static PassiveAvSyncAuditStringsView CreateDefaultObservedPassiveAvSyncAuditStrings()
        {
            return CreateObservedPassiveAvSyncAuditStrings(
                false,
                "n/a",
                "n/a",
                "n/a",
                "n/a",
                "n/a",
                "n/a",
                "n/a",
                "False",
                "False");
        }

        internal static AvSyncEnterpriseAuditStringsView CreateObservedAvSyncEnterpriseAuditStrings(
            bool available,
            string sampleCount,
            string driftProjected2hMs)
        {
            return new AvSyncEnterpriseAuditStringsView
            {
                Available = available,
                SampleCount = sampleCount,
                DriftProjected2hMs = driftProjected2hMs,
            };
        }

        internal static AvSyncEnterpriseAuditStringsView CreateDefaultObservedAvSyncEnterpriseAuditStrings()
        {
            return CreateObservedAvSyncEnterpriseAuditStrings(
                false,
                "n/a",
                "n/a");
        }

        internal static AvSyncEnterpriseAuditStringsView CreateObservedAvSyncEnterpriseAuditStringsExtended(
            bool available,
            uint sampleCount,
            long windowSpanUs,
            long latestRawOffsetUs,
            long latestSmoothOffsetUs,
            double driftSlopePpm,
            double driftProjected2hMs,
            long offsetAbsP95Us,
            long offsetAbsP99Us,
            long offsetAbsMaxUs)
        {
            return CreateObservedAvSyncEnterpriseAuditStringsExtended(
                available,
                sampleCount.ToString(),
                windowSpanUs.ToString(),
                latestRawOffsetUs.ToString(),
                latestSmoothOffsetUs.ToString(),
                driftSlopePpm.ToString("F3"),
                driftProjected2hMs.ToString("F3"),
                offsetAbsP95Us.ToString(),
                offsetAbsP99Us.ToString(),
                offsetAbsMaxUs.ToString());
        }

        internal static AvSyncEnterpriseAuditStringsView CreateObservedAvSyncEnterpriseAuditStringsExtended(
            bool available,
            string sampleCount,
            string windowSpanUs,
            string latestRawOffsetUs,
            string latestSmoothOffsetUs,
            string driftSlopePpm,
            string driftProjected2hMs,
            string offsetAbsP95Us,
            string offsetAbsP99Us,
            string offsetAbsMaxUs)
        {
            return new AvSyncEnterpriseAuditStringsView
            {
                Available = available,
                SampleCount = sampleCount,
                WindowSpanUs = windowSpanUs,
                LatestRawOffsetUs = latestRawOffsetUs,
                LatestSmoothOffsetUs = latestSmoothOffsetUs,
                DriftSlopePpm = driftSlopePpm,
                DriftProjected2hMs = driftProjected2hMs,
                OffsetAbsP95Us = offsetAbsP95Us,
                OffsetAbsP99Us = offsetAbsP99Us,
                OffsetAbsMaxUs = offsetAbsMaxUs,
            };
        }

        internal static AudioOutputPolicyAuditStringsView CreateObservedAudioOutputPolicyAuditStrings(
            bool available,
            int fileStartThresholdMilliseconds,
            int androidFileStartThresholdMilliseconds,
            int realtimeStartThresholdMilliseconds,
            int realtimeStartupGraceMilliseconds,
            int realtimeStartupMinimumThresholdMilliseconds,
            int fileRingCapacityMilliseconds,
            int androidFileRingCapacityMilliseconds,
            int realtimeRingCapacityMilliseconds,
            int fileBufferedCeilingMilliseconds,
            int androidFileBufferedCeilingMilliseconds,
            int realtimeBufferedCeilingMilliseconds,
            int realtimeStartupAdditionalSinkDelayMilliseconds,
            int realtimeSteadyAdditionalSinkDelayMilliseconds,
            int realtimeBackendAdditionalSinkDelayMilliseconds,
            bool realtimeStartRequiresVideoFrame,
            bool allowAndroidFileOutputRateBridge)
        {
            return CreateObservedAudioOutputPolicyAuditStrings(
                available,
                fileStartThresholdMilliseconds.ToString(),
                androidFileStartThresholdMilliseconds.ToString(),
                realtimeStartThresholdMilliseconds.ToString(),
                realtimeStartupGraceMilliseconds.ToString(),
                realtimeStartupMinimumThresholdMilliseconds.ToString(),
                fileRingCapacityMilliseconds.ToString(),
                androidFileRingCapacityMilliseconds.ToString(),
                realtimeRingCapacityMilliseconds.ToString(),
                fileBufferedCeilingMilliseconds.ToString(),
                androidFileBufferedCeilingMilliseconds.ToString(),
                realtimeBufferedCeilingMilliseconds.ToString(),
                realtimeStartupAdditionalSinkDelayMilliseconds.ToString(),
                realtimeSteadyAdditionalSinkDelayMilliseconds.ToString(),
                realtimeBackendAdditionalSinkDelayMilliseconds.ToString(),
                realtimeStartRequiresVideoFrame.ToString(),
                allowAndroidFileOutputRateBridge.ToString());
        }

        internal static AudioOutputPolicyAuditStringsView CreateObservedAudioOutputPolicyAuditStrings(
            bool available,
            string fileStartThresholdMilliseconds,
            string androidFileStartThresholdMilliseconds,
            string realtimeStartThresholdMilliseconds,
            string realtimeStartupGraceMilliseconds,
            string realtimeStartupMinimumThresholdMilliseconds,
            string fileRingCapacityMilliseconds,
            string androidFileRingCapacityMilliseconds,
            string realtimeRingCapacityMilliseconds,
            string fileBufferedCeilingMilliseconds,
            string androidFileBufferedCeilingMilliseconds,
            string realtimeBufferedCeilingMilliseconds,
            string realtimeStartupAdditionalSinkDelayMilliseconds,
            string realtimeSteadyAdditionalSinkDelayMilliseconds,
            string realtimeBackendAdditionalSinkDelayMilliseconds,
            string realtimeStartRequiresVideoFrame,
            string allowAndroidFileOutputRateBridge)
        {
            return new AudioOutputPolicyAuditStringsView
            {
                Available = available,
                FileStartThresholdMilliseconds = fileStartThresholdMilliseconds,
                AndroidFileStartThresholdMilliseconds = androidFileStartThresholdMilliseconds,
                RealtimeStartThresholdMilliseconds = realtimeStartThresholdMilliseconds,
                RealtimeStartupGraceMilliseconds = realtimeStartupGraceMilliseconds,
                RealtimeStartupMinimumThresholdMilliseconds = realtimeStartupMinimumThresholdMilliseconds,
                FileRingCapacityMilliseconds = fileRingCapacityMilliseconds,
                AndroidFileRingCapacityMilliseconds = androidFileRingCapacityMilliseconds,
                RealtimeRingCapacityMilliseconds = realtimeRingCapacityMilliseconds,
                FileBufferedCeilingMilliseconds = fileBufferedCeilingMilliseconds,
                AndroidFileBufferedCeilingMilliseconds = androidFileBufferedCeilingMilliseconds,
                RealtimeBufferedCeilingMilliseconds = realtimeBufferedCeilingMilliseconds,
                RealtimeStartupAdditionalSinkDelayMilliseconds = realtimeStartupAdditionalSinkDelayMilliseconds,
                RealtimeSteadyAdditionalSinkDelayMilliseconds = realtimeSteadyAdditionalSinkDelayMilliseconds,
                RealtimeBackendAdditionalSinkDelayMilliseconds = realtimeBackendAdditionalSinkDelayMilliseconds,
                RealtimeStartRequiresVideoFrame = realtimeStartRequiresVideoFrame,
                AllowAndroidFileOutputRateBridge = allowAndroidFileOutputRateBridge,
            };
        }

        internal static AudioOutputPolicyAuditStringsView CreateDefaultObservedAudioOutputPolicyAuditStrings()
        {
            return CreateObservedAudioOutputPolicyAuditStrings(
                false,
                "n/a",
                "n/a",
                "n/a",
                "n/a",
                "n/a",
                "n/a",
                "n/a",
                "n/a",
                "n/a",
                "n/a",
                "n/a",
                "n/a",
                "n/a",
                "n/a",
                "False",
                "False");
        }

        internal static void AppendValidationSummaryPlayerSession(
            StringBuilder builder,
            ValidationSummaryPlayerSessionView summary)
        {
            builder.AppendLine("player_session_available=" + summary.Available);
            builder.AppendLine("player_session_lifecycle_state=" + summary.LifecycleState);
            builder.AppendLine("player_session_public_state=" + summary.PublicState);
            builder.AppendLine("player_session_runtime_state=" + summary.RuntimeState);
            builder.AppendLine("player_session_playback_intent=" + summary.PlaybackIntent);
            builder.AppendLine("player_session_stop_reason=" + summary.StopReason);
            builder.AppendLine("player_session_source_state=" + summary.SourceState);
            builder.AppendLine("player_session_can_seek=" + summary.CanSeek);
            builder.AppendLine("player_session_is_realtime=" + summary.IsRealtime);
            builder.AppendLine("player_session_is_buffering=" + summary.IsBuffering);
            builder.AppendLine("player_session_is_syncing=" + summary.IsSyncing);
        }

        internal static ValidationSummaryPlayerSessionExtendedView CreateValidationSummaryPlayerSessionExtended(
            bool available,
            string lifecycleState,
            string publicState,
            string runtimeState,
            string playbackIntent,
            string stopReason,
            string sourceState,
            string canSeek,
            string isRealtime,
            string isBuffering,
            string isSyncing,
            string audioStartStateReported,
            string shouldStartAudio,
            string audioStartBlockReason,
            string requiredBufferedSamples,
            string reportedBufferedSamples,
            string requiresPresentedVideoFrame,
            string hasPresentedVideoFrame,
            string androidFileRateBridgeActive)
        {
            return new ValidationSummaryPlayerSessionExtendedView
            {
                Available = available,
                LifecycleState = lifecycleState,
                PublicState = publicState,
                RuntimeState = runtimeState,
                PlaybackIntent = playbackIntent,
                StopReason = stopReason,
                SourceState = sourceState,
                CanSeek = canSeek,
                IsRealtime = isRealtime,
                IsBuffering = isBuffering,
                IsSyncing = isSyncing,
                AudioStartStateReported = audioStartStateReported,
                ShouldStartAudio = shouldStartAudio,
                AudioStartBlockReason = audioStartBlockReason,
                RequiredBufferedSamples = requiredBufferedSamples,
                ReportedBufferedSamples = reportedBufferedSamples,
                RequiresPresentedVideoFrame = requiresPresentedVideoFrame,
                HasPresentedVideoFrame = hasPresentedVideoFrame,
                AndroidFileRateBridgeActive = androidFileRateBridgeActive,
            };
        }

        internal static ValidationSummaryPlayerSessionExtendedView CreateValidationSummaryPlayerSessionExtended(
            bool available,
            string lifecycleState,
            int publicState,
            int runtimeState,
            int playbackIntent,
            int stopReason,
            string sourceState,
            bool canSeek,
            bool isRealtime,
            bool isBuffering,
            bool isSyncing,
            bool audioStartStateReported,
            bool shouldStartAudio,
            int audioStartBlockReason,
            int requiredBufferedSamples,
            int reportedBufferedSamples,
            bool requiresPresentedVideoFrame,
            bool hasPresentedVideoFrame,
            bool androidFileRateBridgeActive)
        {
            return CreateValidationSummaryPlayerSessionExtended(
                available,
                lifecycleState,
                publicState.ToString(),
                runtimeState.ToString(),
                playbackIntent.ToString(),
                stopReason.ToString(),
                sourceState,
                canSeek.ToString(),
                isRealtime.ToString(),
                isBuffering.ToString(),
                isSyncing.ToString(),
                audioStartStateReported.ToString(),
                shouldStartAudio.ToString(),
                audioStartBlockReason.ToString(),
                requiredBufferedSamples.ToString(),
                reportedBufferedSamples.ToString(),
                requiresPresentedVideoFrame.ToString(),
                hasPresentedVideoFrame.ToString(),
                androidFileRateBridgeActive.ToString());
        }

        internal static void AppendValidationSummaryPlayerSessionExtended(
            StringBuilder builder,
            ValidationSummaryPlayerSessionExtendedView summary)
        {
            builder.AppendLine("player_session_available=" + summary.Available);
            builder.AppendLine("player_session_lifecycle_state=" + summary.LifecycleState);
            builder.AppendLine("player_session_public_state=" + summary.PublicState);
            builder.AppendLine("player_session_runtime_state=" + summary.RuntimeState);
            builder.AppendLine("player_session_playback_intent=" + summary.PlaybackIntent);
            builder.AppendLine("player_session_stop_reason=" + summary.StopReason);
            builder.AppendLine("player_session_source_state=" + summary.SourceState);
            builder.AppendLine("player_session_can_seek=" + summary.CanSeek);
            builder.AppendLine("player_session_is_realtime=" + summary.IsRealtime);
            builder.AppendLine("player_session_is_buffering=" + summary.IsBuffering);
            builder.AppendLine("player_session_is_syncing=" + summary.IsSyncing);
            builder.AppendLine("player_session_audio_start_state_reported=" + summary.AudioStartStateReported);
            builder.AppendLine("player_session_should_start_audio=" + summary.ShouldStartAudio);
            builder.AppendLine("player_session_audio_start_block_reason=" + summary.AudioStartBlockReason);
            builder.AppendLine("player_session_required_buffered_samples=" + summary.RequiredBufferedSamples);
            builder.AppendLine("player_session_reported_buffered_samples=" + summary.ReportedBufferedSamples);
            builder.AppendLine("player_session_requires_presented_video_frame=" + summary.RequiresPresentedVideoFrame);
            builder.AppendLine("player_session_has_presented_video_frame=" + summary.HasPresentedVideoFrame);
            builder.AppendLine("player_session_android_file_rate_bridge_active=" + summary.AndroidFileRateBridgeActive);
        }

        internal static ValidationSummarySourceRuntimeView CreateValidationSummarySourceRuntime(
            string state,
            string packets,
            string timeouts,
            string reconnects)
        {
            return new ValidationSummarySourceRuntimeView
            {
                State = state,
                Packets = packets,
                Timeouts = timeouts,
                Reconnects = reconnects,
                IncludeLastActivityAgeSeconds = false,
                LastActivityAgeSeconds = string.Empty,
            };
        }

        internal static ValidationSummarySourceRuntimeView CreateValidationSummarySourceRuntime(
            string state,
            string packets,
            string timeouts,
            string reconnects,
            double lastActivityAgeSeconds)
        {
            return new ValidationSummarySourceRuntimeView
            {
                State = state,
                Packets = packets,
                Timeouts = timeouts,
                Reconnects = reconnects,
                IncludeLastActivityAgeSeconds = true,
                LastActivityAgeSeconds = lastActivityAgeSeconds.ToString("F3"),
            };
        }

        internal static void AppendValidationSummarySourceRuntime(
            StringBuilder builder,
            ValidationSummarySourceRuntimeView summary)
        {
            builder.AppendLine("source_state=" + summary.State);
            builder.AppendLine("source_packets=" + summary.Packets);
            builder.AppendLine("source_timeouts=" + summary.Timeouts);
            builder.AppendLine("source_reconnects=" + summary.Reconnects);
            if (summary.IncludeLastActivityAgeSeconds)
            {
                builder.AppendLine("source_last_activity_age_sec=" + summary.LastActivityAgeSeconds);
            }
        }

        internal static ValidationSummaryNativeVideoRuntimeView CreateValidationSummaryNativeVideoRuntime(
            bool active,
            string activationDecision,
            bool hasPresentedFrame)
        {
            return new ValidationSummaryNativeVideoRuntimeView
            {
                Active = active,
                ActivationDecision = activationDecision,
                HasPresentedFrame = hasPresentedFrame,
            };
        }

        internal static void AppendValidationSummaryNativeVideoRuntime(
            StringBuilder builder,
            ValidationSummaryNativeVideoRuntimeView summary)
        {
            builder.AppendLine("native_video_active=" + summary.Active);
            builder.AppendLine("native_activation_decision=" + summary.ActivationDecision);
            builder.AppendLine("has_presented_native_video_frame=" + summary.HasPresentedFrame);
        }

        internal static ValidationSummaryRuntimeHealthView CreateValidationSummaryRuntimeHealth(
            bool available,
            int state,
            int runtimeState,
            int playbackIntent,
            int streamCount,
            int videoDecoderCount,
            bool hasAudioDecoder)
        {
            return new ValidationSummaryRuntimeHealthView
            {
                Available = available,
                State = state.ToString(),
                RuntimeState = runtimeState.ToString(),
                PlaybackIntent = playbackIntent.ToString(),
                StreamCount = streamCount.ToString(),
                VideoDecoderCount = videoDecoderCount.ToString(),
                HasAudioDecoder = hasAudioDecoder.ToString(),
            };
        }

        internal static void AppendValidationSummaryRuntimeHealth(
            StringBuilder builder,
            ValidationSummaryRuntimeHealthView summary)
        {
            builder.AppendLine("runtime_health_available=" + summary.Available);
            builder.AppendLine("state=" + summary.State);
            builder.AppendLine("runtime_state=" + summary.RuntimeState);
            builder.AppendLine("playback_intent=" + summary.PlaybackIntent);
            builder.AppendLine("stream_count=" + summary.StreamCount);
            builder.AppendLine("video_decoder_count=" + summary.VideoDecoderCount);
            builder.AppendLine("has_audio_decoder=" + summary.HasAudioDecoder);
        }

        internal static void AppendValidationSummaryPathSelection(
            StringBuilder builder,
            ValidationSummaryPathSelectionView summary)
        {
            builder.AppendLine("path_selection_available=" + summary.Available);
            builder.AppendLine("path_selection_kind=" + summary.Kind);
        }

        internal static ValidationSummaryEnterpriseMetricsView
            CreateValidationSummaryEnterpriseMetrics(
                int sessionId,
                string sourceTimelineModel,
                string uri,
                bool hasAvSyncContract,
                bool hasAvSyncAudioClockSec,
                double avSyncAudioClockSec,
                bool hasAvSyncVideoClockSec,
                double avSyncVideoClockSec,
                ulong avSyncDropTotal,
                ulong avSyncDuplicateTotal,
                bool hasPlaybackTimingContract,
                bool hasPlaybackAudioPresentedTimeUs,
                long playbackAudioPresentedTimeUs,
                bool hasPlaybackAudioTimeUs,
                long playbackAudioTimeUs,
                bool hasPresentedVideoTime,
                double presentedVideoTimeSec,
                bool hasPassiveAvSyncSnapshot,
                long passiveRawOffsetUs,
                long passiveSmoothOffsetUs,
                double passiveDriftPpm,
                double passiveAudioResampleRatio,
                bool hasAvSyncEnterpriseMetrics,
                double avSyncEnterpriseDriftSlopePpm,
                long avSyncEnterpriseOffsetAbsP95Us,
                long avSyncEnterpriseOffsetAbsP99Us,
                int reportedBufferedSamples,
                int audioSampleRate,
                int audioChannels,
                RuntimePlatform platform,
                string deviceModel)
        {
            return new ValidationSummaryEnterpriseMetricsView
            {
                SessionId = sessionId >= 0 ? sessionId.ToString() : "n/a",
                SourceType = ResolveValidationSummarySourceType(sourceTimelineModel, uri),
                AudioClockUs = ResolveValidationSummaryAudioClockUs(
                    hasAvSyncContract,
                    hasAvSyncAudioClockSec,
                    avSyncAudioClockSec,
                    hasPlaybackTimingContract,
                    hasPlaybackAudioPresentedTimeUs,
                    playbackAudioPresentedTimeUs,
                    hasPlaybackAudioTimeUs,
                    playbackAudioTimeUs),
                VideoPtsUs = ResolveValidationSummaryVideoPtsUs(
                    hasAvSyncContract,
                    hasAvSyncVideoClockSec,
                    avSyncVideoClockSec,
                    hasPresentedVideoTime,
                    presentedVideoTimeSec),
                OffsetUs = ResolveValidationSummaryOffsetUs(
                    hasAvSyncContract,
                    hasAvSyncAudioClockSec,
                    avSyncAudioClockSec,
                    hasAvSyncVideoClockSec,
                    avSyncVideoClockSec,
                    hasPassiveAvSyncSnapshot,
                    passiveRawOffsetUs),
                OffsetSmoothUs = hasPassiveAvSyncSnapshot
                    ? passiveSmoothOffsetUs.ToString()
                    : "n/a",
                DriftPpm = ResolveValidationSummaryDriftPpm(
                    hasPassiveAvSyncSnapshot,
                    passiveDriftPpm,
                    hasAvSyncEnterpriseMetrics,
                    avSyncEnterpriseDriftSlopePpm),
                AudioBufferMs = ResolveValidationSummaryBufferedMilliseconds(
                    reportedBufferedSamples,
                    audioSampleRate,
                    audioChannels),
                VideoQueueDepth = "n/a",
                DropFrames = hasAvSyncContract ? avSyncDropTotal.ToString() : "n/a",
                DuplicateFrames = hasAvSyncContract ? avSyncDuplicateTotal.ToString() : "n/a",
                RebufferCount = "n/a",
                SyncRebuildCount = "n/a",
                AudioResampleRatio = hasPassiveAvSyncSnapshot
                    ? passiveAudioResampleRatio.ToString("F6")
                    : "n/a",
                Platform = platform.ToString(),
                DeviceModel = ResolveValidationSummaryDeviceModel(deviceModel),
                AvOffsetMeanMs = "n/a",
                AvOffsetP95Ms = hasAvSyncEnterpriseMetrics
                    ? FormatValidationSummaryMillisecondsFromMicroseconds(
                        avSyncEnterpriseOffsetAbsP95Us)
                    : "n/a",
                AvOffsetP99Ms = hasAvSyncEnterpriseMetrics
                    ? FormatValidationSummaryMillisecondsFromMicroseconds(
                        avSyncEnterpriseOffsetAbsP99Us)
                    : "n/a",
            };
        }

        internal static void AppendValidationSummaryEnterpriseMetrics(
            StringBuilder builder,
            ValidationSummaryEnterpriseMetricsView summary)
        {
            builder.AppendLine("session_id=" + summary.SessionId);
            builder.AppendLine("source_type=" + summary.SourceType);
            builder.AppendLine("audio_clock_us=" + summary.AudioClockUs);
            builder.AppendLine("video_pts_us=" + summary.VideoPtsUs);
            builder.AppendLine("offset_us=" + summary.OffsetUs);
            builder.AppendLine("offset_smooth_us=" + summary.OffsetSmoothUs);
            builder.AppendLine("drift_ppm=" + summary.DriftPpm);
            builder.AppendLine("audio_buffer_ms=" + summary.AudioBufferMs);
            builder.AppendLine("video_queue_depth=" + summary.VideoQueueDepth);
            builder.AppendLine("drop_frames=" + summary.DropFrames);
            builder.AppendLine("duplicate_frames=" + summary.DuplicateFrames);
            builder.AppendLine("rebuffer_count=" + summary.RebufferCount);
            builder.AppendLine("sync_rebuild_count=" + summary.SyncRebuildCount);
            builder.AppendLine("audio_resample_ratio=" + summary.AudioResampleRatio);
            builder.AppendLine("platform=" + summary.Platform);
            builder.AppendLine("device_model=" + summary.DeviceModel);
            builder.AppendLine("av_offset_mean_ms=" + summary.AvOffsetMeanMs);
            builder.AppendLine("av_offset_p95_ms=" + summary.AvOffsetP95Ms);
            builder.AppendLine("av_offset_p99_ms=" + summary.AvOffsetP99Ms);
        }

        internal static ValidationSummaryFrameContractView CreateValidationSummaryFrameContract(
            bool available,
            string memoryKind,
            string dynamicRange,
            string nominalFps)
        {
            return new ValidationSummaryFrameContractView
            {
                Available = available,
                MemoryKind = memoryKind,
                DynamicRange = dynamicRange,
                NominalFps = nominalFps,
            };
        }

        internal static ValidationSummaryFrameContractView CreateValidationSummaryFrameContract(
            bool available,
            string memoryKind,
            string dynamicRange,
            double nominalFps)
        {
            return CreateValidationSummaryFrameContract(
                available,
                memoryKind,
                dynamicRange,
                nominalFps.ToString("F2"));
        }

        internal static void AppendValidationSummaryFrameContract(
            StringBuilder builder,
            ValidationSummaryFrameContractView summary)
        {
            builder.AppendLine("frame_contract_available=" + summary.Available);
            builder.AppendLine("frame_contract_memory=" + summary.MemoryKind);
            builder.AppendLine("frame_contract_dynamic_range=" + summary.DynamicRange);
            builder.AppendLine("frame_contract_nominal_fps=" + summary.NominalFps);
        }

        internal static void AppendValidationSummaryPlaybackContract(
            StringBuilder builder,
            PlaybackTimingAuditStringsView summary)
        {
            builder.AppendLine("playback_contract_available=" + summary.Available);
            builder.AppendLine("playback_contract_master_sec=" + summary.MasterTimeSec);
            builder.AppendLine("playback_contract_master_us=" + summary.MasterTimeUs);
            builder.AppendLine("playback_contract_external_sec=" + summary.ExternalTimeSec);
            builder.AppendLine("playback_contract_external_us=" + summary.ExternalTimeUs);
            builder.AppendLine("playback_contract_has_us_mirror=" + summary.HasMicrosecondMirror);
        }

        internal static void AppendValidationSummaryPlaybackContractExtended(
            StringBuilder builder,
            PlaybackTimingAuditStringsView summary)
        {
            builder.AppendLine("playback_contract_available=" + summary.Available);
            builder.AppendLine("playback_contract_master_sec=" + summary.MasterTimeSec);
            builder.AppendLine("playback_contract_master_us=" + summary.MasterTimeUs);
            builder.AppendLine("playback_contract_external_sec=" + summary.ExternalTimeSec);
            builder.AppendLine("playback_contract_external_us=" + summary.ExternalTimeUs);
            builder.AppendLine("playback_contract_has_audio_time_sec=" + summary.HasAudioTimeSec);
            builder.AppendLine("playback_contract_audio_time_sec=" + summary.AudioTimeSec);
            builder.AppendLine("playback_contract_has_audio_time_us=" + summary.HasAudioTimeUs);
            builder.AppendLine("playback_contract_audio_time_us=" + summary.AudioTimeUs);
            builder.AppendLine("playback_contract_has_audio_presented_time_sec=" + summary.HasAudioPresentedTimeSec);
            builder.AppendLine("playback_contract_audio_presented_time_sec=" + summary.AudioPresentedTimeSec);
            builder.AppendLine("playback_contract_has_audio_presented_time_us=" + summary.HasAudioPresentedTimeUs);
            builder.AppendLine("playback_contract_audio_presented_time_us=" + summary.AudioPresentedTimeUs);
            builder.AppendLine("playback_contract_audio_sink_delay_ms=" + summary.AudioSinkDelaySec);
            builder.AppendLine("playback_contract_audio_sink_delay_us=" + summary.AudioSinkDelayUs);
            builder.AppendLine("playback_contract_has_us_mirror=" + summary.HasMicrosecondMirror);
            builder.AppendLine("playback_contract_has_audio_clock=" + summary.HasAudioClock);
        }

        internal static ValidationSummaryAvSyncContractView CreateValidationSummaryAvSyncContract(
            bool available,
            string masterClock,
            string driftMs,
            string clockDeltaMs,
            string dropTotal,
            string duplicateTotal)
        {
            return new ValidationSummaryAvSyncContractView
            {
                Available = available,
                MasterClock = masterClock,
                DriftMs = driftMs,
                ClockDeltaMs = clockDeltaMs,
                DropTotal = dropTotal,
                DuplicateTotal = duplicateTotal,
            };
        }

        internal static ValidationSummaryAvSyncContractView CreateValidationSummaryAvSyncContract(
            bool available,
            string masterClock,
            double driftMs,
            double clockDeltaMs,
            ulong dropTotal,
            ulong duplicateTotal)
        {
            return CreateValidationSummaryAvSyncContract(
                available,
                masterClock,
                driftMs.ToString("F1"),
                clockDeltaMs.ToString("F1"),
                dropTotal.ToString(),
                duplicateTotal.ToString());
        }

        internal static void AppendValidationSummaryAvSyncContract(
            StringBuilder builder,
            ValidationSummaryAvSyncContractView summary)
        {
            builder.AppendLine("av_sync_contract_available=" + summary.Available);
            builder.AppendLine("av_sync_contract_master_clock=" + summary.MasterClock);
            builder.AppendLine("av_sync_contract_drift_ms=" + summary.DriftMs);
            builder.AppendLine("av_sync_contract_clock_delta_ms=" + summary.ClockDeltaMs);
            builder.AppendLine("av_sync_contract_drop_total=" + summary.DropTotal);
            builder.AppendLine("av_sync_contract_duplicate_total=" + summary.DuplicateTotal);
        }

        internal static void AppendValidationSummarySourceTimeline(
            StringBuilder builder,
            SourceTimelineAuditStringsView summary)
        {
            builder.AppendLine("source_timeline_available=" + summary.Available);
            builder.AppendLine("source_timeline_model=" + summary.Model);
            builder.AppendLine("source_timeline_anchor_kind=" + summary.AnchorKind);
            builder.AppendLine("source_timeline_is_realtime=" + summary.IsRealtime);
            builder.AppendLine("source_timeline_has_current_source_time_us=" + summary.HasCurrentSourceTimeUs);
            builder.AppendLine("source_timeline_current_source_time_us=" + summary.CurrentSourceTimeUs);
            builder.AppendLine("source_timeline_has_timeline_origin_us=" + summary.HasTimelineOriginUs);
            builder.AppendLine("source_timeline_timeline_origin_us=" + summary.TimelineOriginUs);
            builder.AppendLine("source_timeline_has_anchor_value_us=" + summary.HasAnchorValueUs);
            builder.AppendLine("source_timeline_anchor_value_us=" + summary.AnchorValueUs);
            builder.AppendLine("source_timeline_has_anchor_mono_us=" + summary.HasAnchorMonoUs);
            builder.AppendLine("source_timeline_anchor_mono_us=" + summary.AnchorMonoUs);
        }

        internal static void AppendValidationSummaryPassiveAvSync(
            StringBuilder builder,
            PassiveAvSyncAuditStringsView summary)
        {
            builder.AppendLine("passive_av_sync_available=" + summary.Available);
            builder.AppendLine("passive_av_sync_raw_offset_us=" + summary.RawOffsetUs);
            builder.AppendLine("passive_av_sync_smooth_offset_us=" + summary.SmoothOffsetUs);
            builder.AppendLine("passive_av_sync_drift_ppm=" + summary.DriftPpm);
            builder.AppendLine("passive_av_sync_drift_intercept_us=" + summary.DriftInterceptUs);
            builder.AppendLine("passive_av_sync_drift_sample_count=" + summary.DriftSampleCount);
            builder.AppendLine("passive_av_sync_video_schedule=" + summary.VideoSchedule);
            builder.AppendLine("passive_av_sync_audio_resample_ratio=" + summary.AudioResampleRatio);
            builder.AppendLine("passive_av_sync_audio_resample_active=" + summary.AudioResampleActive);
            builder.AppendLine("passive_av_sync_should_rebuild_anchor=" + summary.ShouldRebuildAnchor);
        }

        internal static void AppendValidationSummaryAvSyncEnterprise(
            StringBuilder builder,
            AvSyncEnterpriseAuditStringsView summary)
        {
            builder.AppendLine("av_sync_enterprise_available=" + summary.Available);
            builder.AppendLine("av_sync_enterprise_sample_count=" + summary.SampleCount);
            builder.AppendLine("av_sync_enterprise_drift_projected_2h_ms=" + summary.DriftProjected2hMs);
        }

        internal static void AppendValidationSummaryAvSyncEnterpriseExtended(
            StringBuilder builder,
            AvSyncEnterpriseAuditStringsView summary)
        {
            builder.AppendLine("av_sync_enterprise_available=" + summary.Available);
            builder.AppendLine("av_sync_enterprise_sample_count=" + summary.SampleCount);
            builder.AppendLine("av_sync_enterprise_window_span_us=" + summary.WindowSpanUs);
            builder.AppendLine("av_sync_enterprise_latest_raw_offset_us=" + summary.LatestRawOffsetUs);
            builder.AppendLine("av_sync_enterprise_latest_smooth_offset_us=" + summary.LatestSmoothOffsetUs);
            builder.AppendLine("av_sync_enterprise_drift_slope_ppm=" + summary.DriftSlopePpm);
            builder.AppendLine("av_sync_enterprise_drift_projected_2h_ms=" + summary.DriftProjected2hMs);
            builder.AppendLine("av_sync_enterprise_offset_abs_p95_us=" + summary.OffsetAbsP95Us);
            builder.AppendLine("av_sync_enterprise_offset_abs_p99_us=" + summary.OffsetAbsP99Us);
            builder.AppendLine("av_sync_enterprise_offset_abs_max_us=" + summary.OffsetAbsMaxUs);
        }

        internal static void AppendValidationSummaryAudioOutputPolicy(
            StringBuilder builder,
            AudioOutputPolicyAuditStringsView summary)
        {
            builder.AppendLine("audio_output_policy_available=" + summary.Available);
            builder.AppendLine("audio_output_policy_file_start_ms=" + summary.FileStartThresholdMilliseconds);
            builder.AppendLine("audio_output_policy_android_file_start_ms=" + summary.AndroidFileStartThresholdMilliseconds);
            builder.AppendLine("audio_output_policy_realtime_start_ms=" + summary.RealtimeStartThresholdMilliseconds);
            builder.AppendLine("audio_output_policy_realtime_startup_grace_ms=" + summary.RealtimeStartupGraceMilliseconds);
            builder.AppendLine("audio_output_policy_realtime_startup_minimum_threshold_ms=" + summary.RealtimeStartupMinimumThresholdMilliseconds);
            builder.AppendLine("audio_output_policy_file_ring_capacity_ms=" + summary.FileRingCapacityMilliseconds);
            builder.AppendLine("audio_output_policy_android_file_ring_capacity_ms=" + summary.AndroidFileRingCapacityMilliseconds);
            builder.AppendLine("audio_output_policy_realtime_ring_capacity_ms=" + summary.RealtimeRingCapacityMilliseconds);
            builder.AppendLine("audio_output_policy_file_buffered_ceiling_ms=" + summary.FileBufferedCeilingMilliseconds);
            builder.AppendLine("audio_output_policy_android_file_buffered_ceiling_ms=" + summary.AndroidFileBufferedCeilingMilliseconds);
            builder.AppendLine("audio_output_policy_realtime_buffered_ceiling_ms=" + summary.RealtimeBufferedCeilingMilliseconds);
            builder.AppendLine("audio_output_policy_realtime_startup_additional_sink_delay_ms=" + summary.RealtimeStartupAdditionalSinkDelayMilliseconds);
            builder.AppendLine("audio_output_policy_realtime_steady_additional_sink_delay_ms=" + summary.RealtimeSteadyAdditionalSinkDelayMilliseconds);
            builder.AppendLine("audio_output_policy_realtime_backend_additional_sink_delay_ms=" + summary.RealtimeBackendAdditionalSinkDelayMilliseconds);
            builder.AppendLine("audio_output_policy_realtime_start_requires_video_frame=" + summary.RealtimeStartRequiresVideoFrame);
            builder.AppendLine("audio_output_policy_allow_android_file_output_rate_bridge=" + summary.AllowAndroidFileOutputRateBridge);
        }

        internal static PlaybackStartObservationView CreatePlaybackStartObservation(
            bool hasReportedStarted,
            bool reportedStarted,
            bool runtimeHealthAvailable,
            bool runtimeHealthIsPlaying,
            double playbackTimeSec,
            bool preferReportedStarted)
        {
            if (preferReportedStarted && hasReportedStarted)
            {
                return new PlaybackStartObservationView
                {
                    Started = reportedStarted,
                    Source = "reported_flag",
                };
            }

            if (runtimeHealthAvailable)
            {
                return new PlaybackStartObservationView
                {
                    Started = runtimeHealthIsPlaying,
                    Source = "runtime_health",
                };
            }

            if (hasReportedStarted)
            {
                return new PlaybackStartObservationView
                {
                    Started = reportedStarted,
                    Source = "reported_flag",
                };
            }

            return new PlaybackStartObservationView
            {
                Started = playbackTimeSec >= 0.0,
                Source = "playback_time",
            };
        }

        private static string ResolveValidationSummarySourceType(
            string sourceTimelineModel,
            string uri)
        {
            switch (sourceTimelineModel)
            {
                case "FileMediaPtsUs":
                    return "file";
                case "RtspRtpNtpMono":
                    return "rtsp";
                case "RtmpBaseMonoUs":
                    return "rtmp";
            }

            if (string.IsNullOrWhiteSpace(uri))
            {
                return "unknown";
            }

            Uri parsedUri;
            if (Uri.TryCreate(uri, UriKind.Absolute, out parsedUri))
            {
                if (parsedUri.IsFile)
                {
                    return "file";
                }

                if (string.Equals(parsedUri.Scheme, "rtsp", StringComparison.OrdinalIgnoreCase))
                {
                    return "rtsp";
                }

                if (string.Equals(parsedUri.Scheme, "rtmp", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(parsedUri.Scheme, "rtmps", StringComparison.OrdinalIgnoreCase))
                {
                    return "rtmp";
                }
            }

            return "unknown";
        }

        private static string ResolveValidationSummaryAudioClockUs(
            bool hasAvSyncContract,
            bool hasAvSyncAudioClockSec,
            double avSyncAudioClockSec,
            bool hasPlaybackTimingContract,
            bool hasPlaybackAudioPresentedTimeUs,
            long playbackAudioPresentedTimeUs,
            bool hasPlaybackAudioTimeUs,
            long playbackAudioTimeUs)
        {
            if (hasAvSyncContract && hasAvSyncAudioClockSec)
            {
                return FormatValidationSummaryMicrosecondsFromSeconds(avSyncAudioClockSec);
            }

            if (hasPlaybackTimingContract && hasPlaybackAudioPresentedTimeUs)
            {
                return playbackAudioPresentedTimeUs.ToString();
            }

            if (hasPlaybackTimingContract && hasPlaybackAudioTimeUs)
            {
                return playbackAudioTimeUs.ToString();
            }

            return "n/a";
        }

        private static string ResolveValidationSummaryVideoPtsUs(
            bool hasAvSyncContract,
            bool hasAvSyncVideoClockSec,
            double avSyncVideoClockSec,
            bool hasPresentedVideoTime,
            double presentedVideoTimeSec)
        {
            if (hasAvSyncContract && hasAvSyncVideoClockSec)
            {
                return FormatValidationSummaryMicrosecondsFromSeconds(avSyncVideoClockSec);
            }

            if (hasPresentedVideoTime)
            {
                return FormatValidationSummaryMicrosecondsFromSeconds(presentedVideoTimeSec);
            }

            return "n/a";
        }

        private static string ResolveValidationSummaryOffsetUs(
            bool hasAvSyncContract,
            bool hasAvSyncAudioClockSec,
            double avSyncAudioClockSec,
            bool hasAvSyncVideoClockSec,
            double avSyncVideoClockSec,
            bool hasPassiveAvSyncSnapshot,
            long passiveRawOffsetUs)
        {
            if (hasAvSyncContract && hasAvSyncAudioClockSec && hasAvSyncVideoClockSec)
            {
                return Math.Round(
                    (avSyncAudioClockSec - avSyncVideoClockSec) * 1000000.0).ToString();
            }

            if (hasPassiveAvSyncSnapshot)
            {
                return passiveRawOffsetUs.ToString();
            }

            return "n/a";
        }

        private static string ResolveValidationSummaryDriftPpm(
            bool hasPassiveAvSyncSnapshot,
            double passiveDriftPpm,
            bool hasAvSyncEnterpriseMetrics,
            double avSyncEnterpriseDriftSlopePpm)
        {
            if (hasPassiveAvSyncSnapshot)
            {
                return passiveDriftPpm.ToString("F3");
            }

            if (hasAvSyncEnterpriseMetrics)
            {
                return avSyncEnterpriseDriftSlopePpm.ToString("F3");
            }

            return "n/a";
        }

        private static string ResolveValidationSummaryBufferedMilliseconds(
            int bufferedSamples,
            int audioSampleRate,
            int audioChannels)
        {
            if (bufferedSamples <= 0 || audioSampleRate <= 0 || audioChannels <= 0)
            {
                return "n/a";
            }

            return ((double)bufferedSamples * 1000.0 / (audioSampleRate * audioChannels))
                .ToString("F3");
        }

        private static string ResolveValidationSummaryDeviceModel(string deviceModel)
        {
            return string.IsNullOrWhiteSpace(deviceModel)
                ? "unknown"
                : deviceModel.Trim();
        }

        private static string FormatValidationSummaryMicrosecondsFromSeconds(double valueSec)
        {
            if (double.IsNaN(valueSec) || double.IsInfinity(valueSec))
            {
                return "n/a";
            }

            return Math.Round(valueSec * 1000000.0).ToString();
        }

        private static string FormatValidationSummaryMillisecondsFromMicroseconds(long valueUs)
        {
            return (valueUs / 1000.0).ToString("F3");
        }

        internal static AudioStartRuntimeCommandView ResolveAudioStartRuntimeCommand(
            bool contractAvailable,
            PlayerSessionContractView contract)
        {
            if (!contractAvailable)
            {
                return CreateAudioStartRuntimeCommand(false, "missing_contract", false, false, false, 0, 0, 0, false, false, false);
            }

            if (!contract.AudioStartStateReported)
            {
                return CreateAudioStartRuntimeCommand(
                    false,
                    "state_not_reported",
                    true,
                    contract.AudioStartStateReported,
                    contract.ShouldStartAudio,
                    contract.AudioStartBlockReason,
                    contract.RequiredBufferedSamples,
                    contract.ReportedBufferedSamples,
                    contract.RequiresPresentedVideoFrame,
                    contract.HasPresentedVideoFrame,
                    contract.AndroidFileRateBridgeActive);
            }

            if (!contract.ShouldStartAudio)
            {
                return CreateAudioStartRuntimeCommand(
                    false,
                    "blocked",
                    true,
                    contract.AudioStartStateReported,
                    contract.ShouldStartAudio,
                    contract.AudioStartBlockReason,
                    contract.RequiredBufferedSamples,
                    contract.ReportedBufferedSamples,
                    contract.RequiresPresentedVideoFrame,
                    contract.HasPresentedVideoFrame,
                    contract.AndroidFileRateBridgeActive);
            }

            return CreateAudioStartRuntimeCommand(
                true,
                "controller",
                true,
                contract.AudioStartStateReported,
                contract.ShouldStartAudio,
                contract.AudioStartBlockReason,
                contract.RequiredBufferedSamples,
                contract.ReportedBufferedSamples,
                contract.RequiresPresentedVideoFrame,
                contract.HasPresentedVideoFrame,
                contract.AndroidFileRateBridgeActive);
        }

        internal static AudioStartRuntimeCommandView CreateAudioStartRuntimeCommand(
            bool shouldPlay,
            string source,
            bool contractAvailable,
            bool stateReported,
            bool contractShouldStart,
            int blockReason,
            int requiredBufferedSamples,
            int reportedBufferedSamples,
            bool requiresPresentedVideoFrame,
            bool hasPresentedVideoFrame,
            bool androidFileRateBridgeActive)
        {
            return new AudioStartRuntimeCommandView
            {
                ShouldPlay = shouldPlay,
                Source = string.IsNullOrWhiteSpace(source) ? "unknown" : source,
                ContractAvailable = contractAvailable,
                StateReported = stateReported,
                ContractShouldStart = contractShouldStart,
                BlockReason = blockReason,
                RequiredBufferedSamples = requiredBufferedSamples,
                ReportedBufferedSamples = reportedBufferedSamples,
                RequiresPresentedVideoFrame = requiresPresentedVideoFrame,
                HasPresentedVideoFrame = hasPresentedVideoFrame,
                AndroidFileRateBridgeActive = androidFileRateBridgeActive,
            };
        }

        internal static float ResolvePassiveAvSyncAudioResamplePitch(
            PassiveAvSyncSnapshotView snapshot,
            bool isRealtimeSource,
            out bool active)
        {
            active = false;
            if (!isRealtimeSource || !snapshot.AudioResampleActive)
            {
                return 1.0f;
            }

            if (double.IsNaN(snapshot.AudioResampleRatio)
                || double.IsInfinity(snapshot.AudioResampleRatio)
                || snapshot.AudioResampleRatio <= 0.0)
            {
                return 1.0f;
            }

            var clampedRatio = Math.Max(0.995, Math.Min(1.005, snapshot.AudioResampleRatio));
            if (Math.Abs(clampedRatio - 1.0) < 0.0001)
            {
                return 1.0f;
            }

            active = true;
            return (float)clampedRatio;
        }

        internal static PassiveAvSyncAudioResampleCommandView ResolvePassiveAvSyncAudioResampleCommand(
            bool enableAudio,
            bool isRealtimeSource,
            bool isAudioPlaying,
            bool hasSnapshot,
            PassiveAvSyncSnapshotView snapshot)
        {
            if (!enableAudio)
            {
                return CreatePassiveAvSyncAudioResampleCommand(1.0f, false, "disabled");
            }

            if (!isRealtimeSource)
            {
                return CreatePassiveAvSyncAudioResampleCommand(1.0f, false, "non_realtime");
            }

            if (!isAudioPlaying)
            {
                return CreatePassiveAvSyncAudioResampleCommand(1.0f, false, "not_playing");
            }

            if (!hasSnapshot)
            {
                return CreatePassiveAvSyncAudioResampleCommand(1.0f, false, "missing_snapshot");
            }

            var nextPitch = ResolvePassiveAvSyncAudioResamplePitch(
                snapshot,
                isRealtimeSource,
                out var nextActive);
            return CreatePassiveAvSyncAudioResampleCommand(nextPitch, nextActive, "controller");
        }

        internal static bool ShouldApplyPassiveAvSyncAudioResampleCommand(
            bool hasAppliedState,
            float appliedPitch,
            bool appliedActive,
            PassiveAvSyncAudioResampleCommandView command)
        {
            if (!hasAppliedState)
            {
                return true;
            }

            return Math.Abs(appliedPitch - command.Pitch) > 0.0001f
                || appliedActive != command.Active;
        }

        internal static PassiveAvSyncAudioResampleCommandView CreatePassiveAvSyncAudioResampleCommand(
            float pitch,
            bool active,
            string source)
        {
            return new PassiveAvSyncAudioResampleCommandView
            {
                Pitch = Mathf.Clamp(pitch, 0.995f, 1.005f),
                Active = active,
                Source = string.IsNullOrWhiteSpace(source) ? "unknown" : source,
            };
        }

        internal static int ResolveAudioBufferedCeilingSamples(
            AudioOutputPolicyView policy,
            bool isRealtimeSource,
            bool androidFileBridgeActive,
            bool audioStarted,
            int audioSampleRate,
            int audioChannels,
            float startupElapsedMilliseconds,
            bool hasPresentedStartupFrame)
        {
            if (audioSampleRate <= 0 || audioChannels <= 0)
            {
                return 0;
            }

            var steadyStateSamples = ResolveAudioBufferSamples(
                audioSampleRate,
                audioChannels,
                ResolveAudioBufferedCeilingMilliseconds(
                    policy,
                    isRealtimeSource,
                    androidFileBridgeActive));
            if (audioStarted)
            {
                return steadyStateSamples;
            }

            return ResolveAudioStartThresholdSamples(
                policy,
                isRealtimeSource,
                androidFileBridgeActive,
                audioSampleRate,
                audioChannels,
                startupElapsedMilliseconds,
                hasPresentedStartupFrame);
        }

        internal static int ResolveRealtimeAdditionalSinkDelayMilliseconds(
            AudioOutputPolicyView policy,
            bool isRealtimeSource,
            bool audioStarted,
            bool includeBackendAdditionalDelay,
            int steadyAdditionalSinkDelayMilliseconds)
        {
            if (!isRealtimeSource)
            {
                return 0;
            }

            var delayMilliseconds = audioStarted
                ? steadyAdditionalSinkDelayMilliseconds
                : policy.RealtimeStartupAdditionalSinkDelayMilliseconds;
            if (includeBackendAdditionalDelay)
            {
                delayMilliseconds += policy.RealtimeBackendAdditionalSinkDelayMilliseconds;
            }

            return delayMilliseconds;
        }

        internal static NativeVideoStartupWarmupCommandView ResolveNativeVideoStartupWarmupCommand(
            bool isRealtimeSource,
            bool nativeVideoPathActive,
            bool externalTextureTarget,
            bool contractWarmupAvailable,
            bool contractWarmupComplete)
        {
            if (isRealtimeSource)
            {
                return CreateNativeVideoStartupWarmupCommand(
                    false,
                    false,
                    contractWarmupAvailable,
                    contractWarmupComplete,
                    "realtime");
            }

            if (!nativeVideoPathActive)
            {
                return CreateNativeVideoStartupWarmupCommand(
                    false,
                    false,
                    contractWarmupAvailable,
                    contractWarmupComplete,
                    "inactive_path");
            }

            if (!externalTextureTarget)
            {
                return CreateNativeVideoStartupWarmupCommand(
                    false,
                    false,
                    contractWarmupAvailable,
                    contractWarmupComplete,
                    "non_external_texture_target");
            }

            if (!contractWarmupAvailable)
            {
                return CreateNativeVideoStartupWarmupCommand(
                    false,
                    false,
                    false,
                    false,
                    "missing_contract");
            }

            var shouldHold = !contractWarmupComplete;
            return CreateNativeVideoStartupWarmupCommand(
                shouldHold,
                shouldHold,
                true,
                contractWarmupComplete,
                "contract");
        }

        internal static NativeVideoStartupWarmupCommandView CreateNativeVideoStartupWarmupCommand(
            bool warmupEnabled,
            bool shouldHold,
            bool contractAvailable,
            bool contractComplete,
            string source)
        {
            return new NativeVideoStartupWarmupCommandView
            {
                WarmupEnabled = warmupEnabled,
                ShouldHold = shouldHold,
                ContractAvailable = contractAvailable,
                ContractComplete = contractComplete,
                Source = string.IsNullOrWhiteSpace(source) ? "unknown" : source,
            };
        }

        internal static double ResolveBufferedAudioSecondsFromBytes(
            int bufferedBytes,
            int audioSampleRate,
            int audioChannels,
            int audioBytesPerSample)
        {
            if (bufferedBytes <= 0
                || audioSampleRate <= 0
                || audioChannels <= 0
                || audioBytesPerSample <= 0)
            {
                return 0.0;
            }

            var bytesPerSecond =
                (long)audioSampleRate * audioChannels * audioBytesPerSample;
            if (bytesPerSecond <= 0)
            {
                return 0.0;
            }

            return bufferedBytes / (double)bytesPerSecond;
        }

        internal static double ResolveBufferedAudioSecondsFromSamples(
            int bufferedSamples,
            int audioSampleRate,
            int audioChannels)
        {
            if (bufferedSamples <= 0 || audioSampleRate <= 0 || audioChannels <= 0)
            {
                return 0.0;
            }

            var samplesPerSecond = (long)audioSampleRate * audioChannels;
            if (samplesPerSecond <= 0)
            {
                return 0.0;
            }

            return bufferedSamples / (double)samplesPerSecond;
        }

        internal static double ResolveUnityDspBufferedSeconds(
            int audioSampleRate,
            int dspBufferLength,
            int dspBufferCount)
        {
            if (audioSampleRate <= 0 || dspBufferLength <= 0 || dspBufferCount <= 0)
            {
                return 0.0;
            }

            return (double)(dspBufferLength * dspBufferCount) / audioSampleRate;
        }

        internal static SourceTimelineContractView NormalizeSourceTimelineContract(
            RustAVSourceTimelineContract contract)
        {
            return new SourceTimelineContractView
            {
                Model = contract.Model,
                AnchorKind = contract.AnchorKind,
                HasCurrentSourceTimeUs = contract.HasCurrentSourceTimeUs != 0,
                CurrentSourceTimeUs = contract.CurrentSourceTimeUs,
                HasTimelineOriginUs = contract.HasTimelineOriginUs != 0,
                TimelineOriginUs = contract.TimelineOriginUs,
                HasAnchorValueUs = contract.HasAnchorValueUs != 0,
                AnchorValueUs = contract.AnchorValueUs,
                HasAnchorMonoUs = contract.HasAnchorMonoUs != 0,
                AnchorMonoUs = contract.AnchorMonoUs,
                IsRealtime = contract.IsRealtime != 0,
            };
        }

        internal static PlayerSessionContractView NormalizePlayerSessionContract(
            RustAVPlayerSessionContract contract)
        {
            return new PlayerSessionContractView
            {
                LifecycleState = contract.LifecycleState,
                PublicState = contract.PublicState,
                RuntimeState = contract.RuntimeState,
                PlaybackIntent = contract.PlaybackIntent,
                StopReason = contract.StopReason,
                SourceConnectionState = NormalizeSourceConnectionState(contract.SourceConnectionState),
                CanSeek = contract.CanSeek != 0,
                IsRealtime = contract.IsRealtime != 0,
                IsBuffering = contract.IsBuffering != 0,
                IsSyncing = contract.IsSyncing != 0,
                AudioStartStateReported = contract.AudioStartStateReported != 0,
                ShouldStartAudio = contract.ShouldStartAudio != 0,
                AudioStartBlockReason = contract.AudioStartBlockReason,
                RequiredBufferedSamples = contract.RequiredBufferedSamples,
                ReportedBufferedSamples = contract.ReportedBufferedSamples,
                RequiresPresentedVideoFrame = contract.RequiresPresentedVideoFrame != 0,
                HasPresentedVideoFrame = contract.HasPresentedVideoFrame != 0,
                AndroidFileRateBridgeActive = contract.AndroidFileRateBridgeActive != 0,
            };
        }

        internal static AvSyncEnterpriseMetricsView NormalizeAvSyncEnterpriseMetrics(
            RustAVAvSyncEnterpriseMetrics metrics)
        {
            return new AvSyncEnterpriseMetricsView
            {
                SampleCount = metrics.SampleCount,
                WindowSpanUs = metrics.WindowSpanUs,
                LatestRawOffsetUs = metrics.LatestRawOffsetUs,
                LatestSmoothOffsetUs = metrics.LatestSmoothOffsetUs,
                DriftSlopePpm = metrics.DriftSlopePpm,
                DriftProjected2hMs = metrics.DriftProjected2hMs,
                OffsetAbsP95Us = metrics.OffsetAbsP95Us,
                OffsetAbsP99Us = metrics.OffsetAbsP99Us,
                OffsetAbsMaxUs = metrics.OffsetAbsMaxUs,
            };
        }

        internal static PassiveAvSyncSnapshotView NormalizePassiveAvSyncSnapshot(
            RustAVPassiveAvSyncSnapshot snapshot)
        {
            return new PassiveAvSyncSnapshotView
            {
                RawOffsetUs = snapshot.RawOffsetUs,
                SmoothOffsetUs = snapshot.SmoothOffsetUs,
                DriftPpm = snapshot.DriftPpm,
                DriftInterceptUs = snapshot.DriftInterceptUs,
                DriftSampleCount = snapshot.DriftSampleCount,
                VideoSchedule = NormalizePassiveAvSyncVideoSchedule(snapshot.VideoSchedule),
                AudioResampleRatio = snapshot.AudioResampleRatio,
                AudioResampleActive = snapshot.AudioResampleActive != 0,
                ShouldRebuildAnchor = snapshot.ShouldRebuildAnchor != 0,
            };
        }

        internal static string NormalizePassiveAvSyncVideoSchedule(int rawValue)
        {
            switch (rawValue)
            {
                case 0:
                    return "Hold";
                case 1:
                    return "Render";
                case 2:
                    return "RenderLate";
                case 3:
                    return "Drop";
                case 4:
                    return "RebuildSyncAnchor";
                default:
                    return "Unknown";
            }
        }

        internal static string FormatSourceTimelineModel(int value)
        {
            switch (value)
            {
                case 1:
                    return "FileMediaPtsUs";
                case 2:
                    return "RtspRtpNtpMono";
                case 3:
                    return "RtmpBaseMonoUs";
                default:
                    return "Unknown";
            }
        }

        internal static string FormatSourceTimelineAnchorKind(int value)
        {
            switch (value)
            {
                case 1:
                    return "TimelineOrigin";
                case 2:
                    return "RtspPlayRangeOffset";
                case 3:
                    return "RtmpTimestampOrigin";
                default:
                    return "None";
            }
        }

        internal static string FormatPlayerSessionLifecycleState(int value)
        {
            switch (value)
            {
                case 1:
                    return "Idle";
                case 2:
                    return "Opening";
                case 3:
                    return "Prepared";
                case 4:
                    return "Buffering";
                case 5:
                    return "Syncing";
                case 6:
                    return "Playing";
                case 7:
                    return "Paused";
                case 8:
                    return "Stopped";
                case 9:
                    return "Closed";
                case 10:
                    return "ErrorRecovering";
                default:
                    return "Unknown";
            }
        }

        internal static string FormatPlayerSessionState(int value)
        {
            switch (value)
            {
                case 0:
                    return "Idle";
                case 1:
                    return "Connecting";
                case 2:
                    return "Ready";
                case 3:
                    return "Playing";
                case 4:
                    return "Paused";
                case 5:
                    return "Shutdown";
                case 6:
                    return "Ended";
                default:
                    return value.ToString();
            }
        }

        internal static string FormatPlayerSessionPlaybackIntent(int value)
        {
            switch (value)
            {
                case 0:
                    return "Stopped";
                case 1:
                    return "PlayRequested";
                default:
                    return value.ToString();
            }
        }

        internal static string FormatPlayerSessionStopReason(int value)
        {
            switch (value)
            {
                case 0:
                    return "None";
                case 1:
                    return "UserStop";
                case 2:
                    return "EndOfStream";
                default:
                    return value.ToString();
            }
        }

        internal static NativeVideoPlaneTextureFormat NormalizeNativeVideoPlaneTextureFormat(
            int rawValue)
        {
            switch (rawValue)
            {
                case 1:
                    return NativeVideoPlaneTextureFormat.R8Unorm;
                case 2:
                    return NativeVideoPlaneTextureFormat.Rg8Unorm;
                case 3:
                    return NativeVideoPlaneTextureFormat.R16Unorm;
                case 4:
                    return NativeVideoPlaneTextureFormat.Rg16Unorm;
                default:
                    return NativeVideoPlaneTextureFormat.Unknown;
            }
        }

        internal static NativeVideoPlaneResourceKind NormalizeNativeVideoPlaneResourceKind(
            int rawValue)
        {
            switch (rawValue)
            {
                case 1:
                    return NativeVideoPlaneResourceKind.D3D11Texture2D;
                case 2:
                    return NativeVideoPlaneResourceKind.D3D11ShaderResourceView;
                default:
                    return NativeVideoPlaneResourceKind.Unknown;
            }
        }

        internal static NativeVideoPixelFormatKind ToPublicNativeVideoPixelFormat(
            NativeVideoPixelFormat pixelFormat)
        {
            switch (pixelFormat)
            {
                case NativeVideoPixelFormat.Yuv420p:
                    return NativeVideoPixelFormatKind.Yuv420p;
                case NativeVideoPixelFormat.Rgba32:
                    return NativeVideoPixelFormatKind.Rgba32;
                case NativeVideoPixelFormat.Nv12:
                    return NativeVideoPixelFormatKind.Nv12;
                case NativeVideoPixelFormat.P010:
                    return NativeVideoPixelFormatKind.P010;
                default:
                    return NativeVideoPixelFormatKind.Unknown;
            }
        }

        internal static NativeVideoColorRangeKind ToPublicNativeVideoColorRange(
            NativeVideoColorRange colorRange)
        {
            switch (colorRange)
            {
                case NativeVideoColorRange.Limited:
                    return NativeVideoColorRangeKind.Limited;
                case NativeVideoColorRange.Full:
                    return NativeVideoColorRangeKind.Full;
                default:
                    return NativeVideoColorRangeKind.Unknown;
            }
        }

        internal static NativeVideoColorMatrixKind ToPublicNativeVideoColorMatrix(
            NativeVideoColorMatrix colorMatrix)
        {
            switch (colorMatrix)
            {
                case NativeVideoColorMatrix.Bt601:
                    return NativeVideoColorMatrixKind.Bt601;
                case NativeVideoColorMatrix.Bt709:
                    return NativeVideoColorMatrixKind.Bt709;
                case NativeVideoColorMatrix.Bt2020Ncl:
                    return NativeVideoColorMatrixKind.Bt2020Ncl;
                case NativeVideoColorMatrix.Bt2020Cl:
                    return NativeVideoColorMatrixKind.Bt2020Cl;
                case NativeVideoColorMatrix.Smpte240M:
                    return NativeVideoColorMatrixKind.Smpte240M;
                case NativeVideoColorMatrix.Rgb:
                    return NativeVideoColorMatrixKind.Rgb;
                default:
                    return NativeVideoColorMatrixKind.Unknown;
            }
        }

        internal static NativeVideoColorPrimariesKind ToPublicNativeVideoColorPrimaries(
            NativeVideoColorPrimaries primaries)
        {
            switch (primaries)
            {
                case NativeVideoColorPrimaries.Bt601:
                    return NativeVideoColorPrimariesKind.Bt601;
                case NativeVideoColorPrimaries.Bt709:
                    return NativeVideoColorPrimariesKind.Bt709;
                case NativeVideoColorPrimaries.Bt2020:
                    return NativeVideoColorPrimariesKind.Bt2020;
                case NativeVideoColorPrimaries.DciP3:
                    return NativeVideoColorPrimariesKind.DciP3;
                default:
                    return NativeVideoColorPrimariesKind.Unknown;
            }
        }

        internal static NativeVideoTransferCharacteristicKind ToPublicNativeVideoTransferCharacteristic(
            NativeVideoTransferCharacteristic transfer)
        {
            switch (transfer)
            {
                case NativeVideoTransferCharacteristic.Bt1886:
                    return NativeVideoTransferCharacteristicKind.Bt1886;
                case NativeVideoTransferCharacteristic.Srgb:
                    return NativeVideoTransferCharacteristicKind.Srgb;
                case NativeVideoTransferCharacteristic.Linear:
                    return NativeVideoTransferCharacteristicKind.Linear;
                case NativeVideoTransferCharacteristic.Smpte240M:
                    return NativeVideoTransferCharacteristicKind.Smpte240M;
                case NativeVideoTransferCharacteristic.Pq:
                    return NativeVideoTransferCharacteristicKind.Pq;
                case NativeVideoTransferCharacteristic.Hlg:
                    return NativeVideoTransferCharacteristicKind.Hlg;
                default:
                    return NativeVideoTransferCharacteristicKind.Unknown;
            }
        }

        internal static NativeVideoDynamicRangeKind ToPublicNativeVideoDynamicRange(
            NativeVideoDynamicRange dynamicRange)
        {
            switch (dynamicRange)
            {
                case NativeVideoDynamicRange.Sdr:
                    return NativeVideoDynamicRangeKind.Sdr;
                case NativeVideoDynamicRange.Hdr10:
                    return NativeVideoDynamicRangeKind.Hdr10;
                case NativeVideoDynamicRange.Hlg:
                    return NativeVideoDynamicRangeKind.Hlg;
                case NativeVideoDynamicRange.DolbyVision:
                    return NativeVideoDynamicRangeKind.DolbyVision;
                default:
                    return NativeVideoDynamicRangeKind.Unknown;
            }
        }

        internal static MediaPlayer.NativeVideoColorInfo ToPublicNativeVideoColorInfo(
            VideoColorInfoView colorInfo)
        {
            return new MediaPlayer.NativeVideoColorInfo
            {
                Range = ToPublicNativeVideoColorRange(colorInfo.Range),
                Matrix = ToPublicNativeVideoColorMatrix(colorInfo.Matrix),
                Primaries = ToPublicNativeVideoColorPrimaries(colorInfo.Primaries),
                Transfer = ToPublicNativeVideoTransferCharacteristic(colorInfo.Transfer),
                BitDepth = colorInfo.BitDepth,
                DynamicRange = ToPublicNativeVideoDynamicRange(colorInfo.DynamicRange),
            };
        }

        internal static NativeVideoPlaneTextureFormatKind ToPublicNativeVideoPlaneTextureFormat(
            NativeVideoPlaneTextureFormat textureFormat)
        {
            switch (textureFormat)
            {
                case NativeVideoPlaneTextureFormat.R8Unorm:
                    return NativeVideoPlaneTextureFormatKind.R8Unorm;
                case NativeVideoPlaneTextureFormat.Rg8Unorm:
                    return NativeVideoPlaneTextureFormatKind.Rg8Unorm;
                case NativeVideoPlaneTextureFormat.R16Unorm:
                    return NativeVideoPlaneTextureFormatKind.R16Unorm;
                case NativeVideoPlaneTextureFormat.Rg16Unorm:
                    return NativeVideoPlaneTextureFormatKind.Rg16Unorm;
                default:
                    return NativeVideoPlaneTextureFormatKind.Unknown;
            }
        }

        internal static NativeVideoPlaneResourceKindKind ToPublicNativeVideoPlaneResourceKind(
            NativeVideoPlaneResourceKind resourceKind)
        {
            switch (resourceKind)
            {
                case NativeVideoPlaneResourceKind.D3D11Texture2D:
                    return NativeVideoPlaneResourceKindKind.D3D11Texture2D;
                case NativeVideoPlaneResourceKind.D3D11ShaderResourceView:
                    return NativeVideoPlaneResourceKindKind.D3D11ShaderResourceView;
                default:
                    return NativeVideoPlaneResourceKindKind.Unknown;
            }
        }
    }

    internal static class MediaSourceResolver
    {
        private const string PreparedStreamingAssetsRootName = "RustAVPreparedStreamingAssets";
        private const string PreparedStreamingAssetsTempExtension = ".downloading";
        private const int UnityWebRequestStartupRetryFrames = 90;
        private const int StreamingAssetCopyChunkSize = 64 * 1024;
        private const int StreamingAssetCopyYieldThresholdBytes = 2 * 1024 * 1024;

        internal sealed class PreparedMediaSource
        {
            internal PreparedMediaSource(
                string originalUri,
                string playbackUri,
                bool isRealtimeSource,
                bool isPreparedStreamingAsset)
            {
                OriginalUri = originalUri;
                PlaybackUri = playbackUri;
                IsRealtimeSource = isRealtimeSource;
                IsPreparedStreamingAsset = isPreparedStreamingAsset;
            }

            internal string OriginalUri { get; private set; }

            internal string PlaybackUri { get; private set; }

            internal bool IsRealtimeSource { get; private set; }

            internal bool IsPreparedStreamingAsset { get; private set; }
        }

        internal static bool IsRemoteUri(string uri)
        {
            if (string.IsNullOrWhiteSpace(uri))
            {
                return false;
            }

            Uri parsedUri;
            if (!Uri.TryCreate(uri, UriKind.Absolute, out parsedUri))
            {
                return false;
            }

            if (parsedUri.IsFile)
            {
                return false;
            }

            if (string.Equals(parsedUri.Scheme, "jar", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }

        internal static IEnumerator PreparePlayableUri(
            string uri,
            Action<string> onResolved,
            Action<Exception> onError)
        {
            yield return PreparePlayableSource(
                uri,
                source => onResolved(source.PlaybackUri),
                onError);
        }

        internal static IEnumerator PreparePlayableSource(
            string uri,
            Action<PreparedMediaSource> onResolved,
            Action<Exception> onError)
        {
            if (string.IsNullOrWhiteSpace(uri))
            {
                onError(new ArgumentException("媒体地址不能为空。", "uri"));
                yield break;
            }

            if (IsRemoteUri(uri))
            {
                onResolved(
                    new PreparedMediaSource(
                        uri,
                        uri,
                        true,
                        false));
                yield break;
            }

            string absolutePath;
            Exception absolutePathError;
            if (TryResolveAbsolutePath(uri, out absolutePath, out absolutePathError))
            {
                if (absolutePathError != null)
                {
                    onError(absolutePathError);
                }
                else
                {
                    onResolved(
                        new PreparedMediaSource(
                            uri,
                            absolutePath,
                            false,
                            false));
                }
                yield break;
            }

            var streamingAssetSource = ResolveStreamingAssetSource(uri);
            if (File.Exists(streamingAssetSource))
            {
                onResolved(
                    new PreparedMediaSource(
                        uri,
                        Path.GetFullPath(streamingAssetSource),
                        false,
                        false));
                yield break;
            }

            if (!RequiresStagingCopy(streamingAssetSource))
            {
                onError(new FileNotFoundException(streamingAssetSource + " not found."));
                yield break;
            }

            var preparedPath = BuildPreparedStreamingAssetPath(uri);
            if (File.Exists(preparedPath))
            {
                onResolved(
                    new PreparedMediaSource(
                        uri,
                        preparedPath,
                        false,
                        true));
                yield break;
            }
            var preparedDirectory = Path.GetDirectoryName(preparedPath);
            if (!string.IsNullOrEmpty(preparedDirectory))
            {
                Directory.CreateDirectory(preparedDirectory);
            }

            var preparedTempPath = preparedPath + PreparedStreamingAssetsTempExtension;
            TryDeleteFile(preparedTempPath);

            if (Application.platform == RuntimePlatform.Android)
            {
                var androidStageSucceeded = false;
                Exception androidStageError = null;
                yield return StageAndroidStreamingAssetToFile(
                    uri,
                    preparedTempPath,
                    () => androidStageSucceeded = true,
                    ex => androidStageError = ex);

                if (androidStageSucceeded)
                {
                    TryDeleteFile(preparedPath);
                    File.Move(preparedTempPath, preparedPath);
                    onResolved(
                        new PreparedMediaSource(
                            uri,
                            preparedPath,
                            false,
                            true));
                    yield break;
                }

                if (androidStageError != null)
                {
                    onError(androidStageError);
                    yield break;
                }
            }

            var requestType = Type.GetType(
                "UnityEngine.Networking.UnityWebRequest, UnityEngine.UnityWebRequestModule");
            if (requestType == null)
            {
                onError(
                    new InvalidOperationException(
                        "UnityWebRequest 模块不可用，无法准备打包媒体资源。"));
                yield break;
            }

            var getMethod = requestType.GetMethod("Get", new[] { typeof(string) });
            var sendWebRequestMethod = requestType.GetMethod("SendWebRequest", Type.EmptyTypes);
            var resultProperty = requestType.GetProperty("result");
            var errorProperty = requestType.GetProperty("error");
            var downloadHandlerProperty = requestType.GetProperty("downloadHandler");
            var disposeMethod = requestType.GetMethod("Dispose", Type.EmptyTypes);
            var downloadHandlerFileType = Type.GetType(
                "UnityEngine.Networking.DownloadHandlerFile, UnityEngine.UnityWebRequestModule");
            var successValue = resultProperty != null
                ? Enum.Parse(resultProperty.PropertyType, "Success")
                : null;

            if (getMethod == null
                || sendWebRequestMethod == null
                || resultProperty == null
                || errorProperty == null
                || downloadHandlerProperty == null
                || successValue == null)
            {
                onError(
                    new InvalidOperationException(
                        "UnityWebRequest 反射接口不完整，无法准备打包媒体资源。"));
                yield break;
            }

            var request = default(object);
            Exception requestCreateError = null;
            for (var attempt = 0; attempt < UnityWebRequestStartupRetryFrames; attempt++)
            {
                var shouldRetryRequestCreate = false;
                try
                {
                    request = getMethod.Invoke(null, new object[] { streamingAssetSource });
                    requestCreateError = null;
                    break;
                }
                catch (Exception ex)
                {
                    requestCreateError = UnwrapReflectionException(ex);
                    if (!IsUnityWebRequestStartupUnavailable(requestCreateError)
                        || attempt >= UnityWebRequestStartupRetryFrames - 1)
                    {
                        break;
                    }

                    shouldRetryRequestCreate = true;
                }

                if (shouldRetryRequestCreate)
                {
                    yield return null;
                }
            }

            if (request == null)
            {
                onError(
                    requestCreateError
                    ?? new InvalidOperationException(
                        "UnityWebRequest.Get 返回空对象。"));
                yield break;
            }

            var usingDownloadHandlerFile = false;
            if (downloadHandlerFileType != null
                && downloadHandlerProperty != null
                && downloadHandlerProperty.CanWrite)
            {
                var downloadHandlerFile = Activator.CreateInstance(
                    downloadHandlerFileType,
                    new object[] { preparedTempPath });
                if (downloadHandlerFile != null)
                {
                    var removeFileOnAbortProperty =
                        downloadHandlerFileType.GetProperty("removeFileOnAbort");
                    if (removeFileOnAbortProperty != null && removeFileOnAbortProperty.CanWrite)
                    {
                        removeFileOnAbortProperty.SetValue(downloadHandlerFile, true, null);
                    }

                    var previousDownloadHandler = downloadHandlerProperty.GetValue(request, null);
                    var previousDisposable = previousDownloadHandler as IDisposable;
                    if (previousDisposable != null)
                    {
                        previousDisposable.Dispose();
                    }

                    downloadHandlerProperty.SetValue(request, downloadHandlerFile, null);
                    usingDownloadHandlerFile = true;
                }
            }

            var downloadSucceeded = false;
            try
            {
                object asyncOperation = null;
                Exception sendError = null;
                for (var attempt = 0; attempt < UnityWebRequestStartupRetryFrames; attempt++)
                {
                    var shouldRetrySend = false;
                    try
                    {
                        asyncOperation = sendWebRequestMethod.Invoke(request, null);
                        sendError = null;
                        break;
                    }
                    catch (Exception ex)
                    {
                        sendError = UnwrapReflectionException(ex);
                        if (!IsUnityWebRequestStartupUnavailable(sendError)
                            || attempt >= UnityWebRequestStartupRetryFrames - 1)
                        {
                            break;
                        }

                        shouldRetrySend = true;
                    }

                    if (shouldRetrySend)
                    {
                        yield return null;
                    }
                }

                if (sendError != null)
                {
                    onError(sendError);
                    yield break;
                }

                if (asyncOperation != null)
                {
                    yield return asyncOperation;
                }

                var resultValue = resultProperty.GetValue(request, null);
                if (!Equals(resultValue, successValue))
                {
                    var errorMessage = errorProperty.GetValue(request, null) as string;
                    onError(
                        new IOException(
                            "Failed to load packaged media: "
                            + streamingAssetSource
                            + " error="
                            + errorMessage));
                    yield break;
                }

                if (usingDownloadHandlerFile)
                {
                    if (!File.Exists(preparedTempPath))
                    {
                        onError(
                            new IOException(
                                "打包媒体资源分段落盘失败，临时文件不存在。"));
                        yield break;
                    }

                    var preparedInfo = new FileInfo(preparedTempPath);
                    if (preparedInfo.Length <= 0)
                    {
                        onError(
                            new IOException(
                                "打包媒体资源下载结果为空。"));
                        yield break;
                    }

                    TryDeleteFile(preparedPath);
                    File.Move(preparedTempPath, preparedPath);
                }
                else
                {
                    var downloadHandler = downloadHandlerProperty.GetValue(request, null);
                    if (downloadHandler == null)
                    {
                        onError(
                            new IOException(
                                "UnityWebRequest 缺少 DownloadHandler。"));
                        yield break;
                    }

                    var dataProperty = downloadHandler.GetType().GetProperty("data");
                    var data = dataProperty != null
                        ? dataProperty.GetValue(downloadHandler, null) as byte[]
                        : null;
                    if (data == null || data.Length == 0)
                    {
                        onError(
                            new IOException(
                                "打包媒体资源下载结果为空。"));
                        yield break;
                    }

                    File.WriteAllBytes(preparedPath, data);
                }

                downloadSucceeded = true;
            }
            finally
            {
                if (!downloadSucceeded)
                {
                    TryDeleteFile(preparedTempPath);
                }

                var disposable = request as IDisposable;
                if (disposable != null)
                {
                    disposable.Dispose();
                }
                else if (disposeMethod != null)
                {
                    disposeMethod.Invoke(request, null);
                }
            }

            onResolved(
                new PreparedMediaSource(
                    uri,
                preparedPath,
                false,
                true));
        }

        private static IEnumerator StageAndroidStreamingAssetToFile(
            string uri,
            string preparedTempPath,
            Action onSuccess,
            Action<Exception> onError)
        {
            object inputStream = null;
            FileStream outputStream = null;
            Exception stageError = null;

            var relativePath = NormalizeRelativePath(uri);
            try
            {
                var unityPlayerClass = CreateAndroidJavaClass("com.unity3d.player.UnityPlayer");
                if (unityPlayerClass == null)
                {
                    stageError =
                        new InvalidOperationException(
                            "Android JNI 模块不可用，无法准备打包媒体资源。");
                }
                else
                {
                    using (var unityPlayerDisposable = unityPlayerClass as IDisposable)
                    {
                        var activity = AndroidJavaGetStaticObject(unityPlayerClass, "currentActivity");
                        if (activity == null)
                        {
                            stageError =
                                new InvalidOperationException(
                                    "Android currentActivity 不可用，无法准备打包媒体资源。");
                        }
                        else
                        {
                            using (var activityDisposable = activity as IDisposable)
                            {
                                var assetManager = AndroidJavaCallObject(activity, "getAssets");
                                if (assetManager == null)
                                {
                                    stageError =
                                        new InvalidOperationException(
                                            "Android AssetManager 不可用，无法准备打包媒体资源。");
                                }
                                else
                                {
                                    using (var assetManagerDisposable = assetManager as IDisposable)
                                    {
                                        inputStream = AndroidJavaCallObject(
                                            assetManager,
                                            "open",
                                            relativePath);
                                    }
                                }
                            }
                        }
                    }
                }

                if (stageError == null && inputStream == null)
                {
                    stageError =
                        new IOException(
                            "Android AssetInputStream 打开失败。");
                }

                if (stageError == null)
                {
                    outputStream = new FileStream(
                        preparedTempPath,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.None);
                }
            }
            catch (Exception ex)
            {
                stageError =
                    new IOException(
                        "Android 打包媒体资源复制失败: " + ex.Message,
                        ex);
            }

            if (stageError != null)
            {
                if (outputStream != null)
                {
                    outputStream.Dispose();
                    outputStream = null;
                }

                if (inputStream != null)
                {
                    var disposableInputStream = inputStream as IDisposable;
                    if (disposableInputStream != null)
                    {
                        disposableInputStream.Dispose();
                    }
                }

                onError(stageError);
                yield break;
            }

            try
            {
                var buffer = new byte[StreamingAssetCopyChunkSize];
                var copiedSinceYield = 0;
                while (true)
                {
                    int bytesRead;
                    try
                    {
                        bytesRead = AndroidJavaCallInt(inputStream, "read", buffer);
                    }
                    catch (Exception ex)
                    {
                        stageError =
                            new IOException(
                                "Android 打包媒体资源读取失败: " + ex.Message,
                                ex);
                        break;
                    }

                    if (bytesRead < 0)
                    {
                        break;
                    }

                    if (bytesRead == 0)
                    {
                        yield return null;
                        continue;
                    }

                    outputStream.Write(buffer, 0, bytesRead);
                    copiedSinceYield += bytesRead;
                    if (copiedSinceYield >= StreamingAssetCopyYieldThresholdBytes)
                    {
                        copiedSinceYield = 0;
                        yield return null;
                    }
                }

                if (stageError != null)
                {
                    onError(stageError);
                    yield break;
                }

                outputStream.Flush();
                if (outputStream.Length <= 0)
                {
                    onError(
                        new IOException(
                            "Android 打包媒体资源复制结果为空。"));
                    yield break;
                }

                onSuccess();
            }
            finally
            {
                if (outputStream != null)
                {
                    outputStream.Dispose();
                }

                if (inputStream != null)
                {
                    try
                    {
                        AndroidJavaCallVoid(inputStream, "close");
                    }
                    catch
                    {
                    }

                    var disposableInputStream = inputStream as IDisposable;
                    if (disposableInputStream != null)
                    {
                        disposableInputStream.Dispose();
                    }
                }
            }
        }

        internal static bool TryReadAndroidIntentStringExtra(string extraName, out string value)
        {
            value = string.Empty;
            if (Application.platform != RuntimePlatform.Android
                || string.IsNullOrEmpty(extraName))
            {
                return false;
            }

            try
            {
                var unityPlayerClass = CreateAndroidJavaClass("com.unity3d.player.UnityPlayer");
                if (unityPlayerClass == null)
                {
                    return false;
                }

                using (var unityPlayerDisposable = unityPlayerClass as IDisposable)
                {
                    var activity = AndroidJavaGetStaticObject(unityPlayerClass, "currentActivity");
                    if (activity == null)
                    {
                        return false;
                    }

                    using (var activityDisposable = activity as IDisposable)
                    {
                        var intent = AndroidJavaCallObject(activity, "getIntent");
                        if (intent == null)
                        {
                            return false;
                        }

                        using (var intentDisposable = intent as IDisposable)
                        {
                            if (!AndroidJavaCallBool(intent, "hasExtra", extraName))
                            {
                                return false;
                            }

                            var rawValue = AndroidJavaCallString(intent, "getStringExtra", extraName);
                            if (string.IsNullOrEmpty(rawValue))
                            {
                                return false;
                            }

                            value = rawValue;
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    "[MediaRuntimeCommon] android_intent_extra_read_failed"
                    + " extra=" + extraName
                    + " error=" + ex.GetType().Name
                    + " message=" + ex.Message);
                value = string.Empty;
                return false;
            }
        }

        private static object CreateAndroidJavaClass(string className)
        {
            var androidJavaClassType = Type.GetType(
                "UnityEngine.AndroidJavaClass, UnityEngine.AndroidJNIModule");
            return androidJavaClassType != null
                ? Activator.CreateInstance(androidJavaClassType, new object[] { className })
                : null;
        }

        private static object CreateAndroidJavaObject(string className, params object[] args)
        {
            var androidJavaObjectType = GetAndroidJavaObjectRuntimeType();
            return androidJavaObjectType != null
                ? Activator.CreateInstance(
                    androidJavaObjectType,
                    new object[] { className, args ?? new object[0] })
                : null;
        }

        private static object AndroidJavaGetStaticObject(object javaClass, string fieldName)
        {
            if (javaClass == null)
            {
                return null;
            }

            var method = javaClass
                .GetType()
                .GetMethods()
                .FirstOrDefault(candidate =>
                    string.Equals(candidate.Name, "GetStatic", StringComparison.Ordinal)
                    && candidate.IsGenericMethodDefinition
                    && candidate.GetParameters().Length == 1);
            method = method != null
                ? method.MakeGenericMethod(GetAndroidJavaObjectRuntimeType())
                : null;
            return method != null
                ? method.Invoke(javaClass, new object[] { fieldName })
                : null;
        }

        private static object AndroidJavaCallObject(object javaObject, string methodName, params object[] args)
        {
            return AndroidJavaCallGeneric(GetAndroidJavaObjectRuntimeType(), javaObject, methodName, args);
        }

        private static int AndroidJavaCallInt(object javaObject, string methodName, params object[] args)
        {
            var result = AndroidJavaCallGeneric(typeof(int), javaObject, methodName, args);
            return result is int value ? value : 0;
        }

        private static bool AndroidJavaCallBool(object javaObject, string methodName, params object[] args)
        {
            var result = AndroidJavaCallGeneric(typeof(bool), javaObject, methodName, args);
            return result is bool value && value;
        }

        private static string AndroidJavaCallString(object javaObject, string methodName, params object[] args)
        {
            var result = AndroidJavaCallGeneric(typeof(string), javaObject, methodName, args);
            return result as string ?? string.Empty;
        }

        private static void AndroidJavaCallVoid(object javaObject, string methodName, params object[] args)
        {
            if (javaObject == null)
            {
                return;
            }

            var method = javaObject
                .GetType()
                .GetMethods()
                .FirstOrDefault(candidate =>
                    string.Equals(candidate.Name, "Call", StringComparison.Ordinal)
                    && !candidate.IsGenericMethod
                    && candidate.GetParameters().Length == 2
                    && candidate.GetParameters()[0].ParameterType == typeof(string)
                    && candidate.GetParameters()[1].ParameterType == typeof(object[]));
            method?.Invoke(javaObject, new object[] { methodName, args });
        }

        private static object AndroidJavaCallGeneric(
            Type returnType,
            object javaObject,
            string methodName,
            params object[] args)
        {
            if (javaObject == null || returnType == null)
            {
                return null;
            }

            var method = javaObject
                .GetType()
                .GetMethods()
                .FirstOrDefault(candidate =>
                    string.Equals(candidate.Name, "Call", StringComparison.Ordinal)
                    && candidate.IsGenericMethodDefinition
                    && candidate.GetParameters().Length == 2
                    && candidate.GetParameters()[0].ParameterType == typeof(string)
                    && candidate.GetParameters()[1].ParameterType == typeof(object[]));
            if (method == null)
            {
                throw new MissingMethodException("AndroidJavaObject.Call<T> 不可用。");
            }

            return method.MakeGenericMethod(returnType).Invoke(
                javaObject,
                new object[] { methodName, args });
        }

        private static Type GetAndroidJavaObjectRuntimeType()
        {
            return Type.GetType(
                "UnityEngine.AndroidJavaObject, UnityEngine.AndroidJNIModule");
        }

        private static Exception UnwrapReflectionException(Exception exception)
        {
            var current = exception;
            while (current is System.Reflection.TargetInvocationException
                   && current.InnerException != null)
            {
                current = current.InnerException;
            }

            return current;
        }

        private static bool IsUnityWebRequestStartupUnavailable(Exception exception)
        {
            if (exception == null)
            {
                return false;
            }

            var message = exception.Message;
            return !string.IsNullOrEmpty(message)
                && message.IndexOf(
                    "UnityWebRequest system is not yet available",
                    StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void TryDeleteFile(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return;
            }

            try
            {
                File.Delete(path);
            }
            catch
            {
                // 临时文件清理由后续下载覆盖或下次启动重试兜底。
            }
        }

        private static bool TryResolveAbsolutePath(
            string uri,
            out string resolvedPath,
            out Exception error)
        {
            resolvedPath = string.Empty;
            error = null;

            if (Path.IsPathRooted(uri))
            {
                if (Application.platform == RuntimePlatform.Android)
                {
                    string androidResolvedPath;
                    if (TryResolveAndroidReadableAbsolutePath(uri, out androidResolvedPath))
                    {
                        resolvedPath = androidResolvedPath;
                        return true;
                    }
                }

                resolvedPath = Path.GetFullPath(uri);
                if (!File.Exists(resolvedPath))
                {
                    error = new FileNotFoundException(resolvedPath + " not found.");
                }
                return true;
            }

            Uri parsedUri;
            if (!Uri.TryCreate(uri, UriKind.Absolute, out parsedUri))
            {
                return false;
            }

            if (!parsedUri.IsFile)
            {
                return false;
            }

            resolvedPath = parsedUri.LocalPath;
            if (Application.platform == RuntimePlatform.Android)
            {
                string androidResolvedPath;
                if (TryResolveAndroidReadableAbsolutePath(resolvedPath, out androidResolvedPath))
                {
                    resolvedPath = androidResolvedPath;
                    return true;
                }
            }

            if (!File.Exists(resolvedPath))
            {
                error = new FileNotFoundException(resolvedPath + " not found.");
            }
            return true;
        }

        private static string ResolveStreamingAssetSource(string uri)
        {
            Uri parsedUri;
            if (Uri.TryCreate(uri, UriKind.Absolute, out parsedUri)
                && string.Equals(parsedUri.Scheme, "jar", StringComparison.OrdinalIgnoreCase))
            {
                return uri;
            }

            return CombineStreamingAssetsUri(uri);
        }

        private static string CombineStreamingAssetsUri(string uri)
        {
            var normalizedRelativePath = NormalizeRelativePath(uri);
            Uri parsedRoot;
            if (Uri.TryCreate(Application.streamingAssetsPath, UriKind.Absolute, out parsedRoot)
                && !parsedRoot.IsFile)
            {
                var root = Application.streamingAssetsPath.Replace('\\', '/').TrimEnd('/');
                return root + "/" + EncodeRelativeUriPath(normalizedRelativePath);
            }

            return Path.GetFullPath(
                Path.Combine(
                    Application.streamingAssetsPath,
                    normalizedRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static bool RequiresStagingCopy(string sourcePath)
        {
            Uri parsedUri;
            if (!Uri.TryCreate(sourcePath, UriKind.Absolute, out parsedUri))
            {
                return false;
            }

            return !parsedUri.IsFile;
        }

        private static string BuildPreparedStreamingAssetPath(string uri)
        {
            var normalizedRelativePath = NormalizeRelativePath(uri);
            var preparedRoot = GetPreparedStreamingAssetsRootPath();
            var preparedPath = Path.GetFullPath(
                Path.Combine(
                    preparedRoot,
                    normalizedRelativePath.Replace('/', Path.DirectorySeparatorChar)));

            if (!preparedPath.StartsWith(preparedRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "StreamingAssets 路径非法，包含越界段。");
            }

            return preparedPath;
        }

        private static string GetPreparedStreamingAssetsRootPath()
        {
            var rootBasePath = Application.persistentDataPath;
            if (Application.platform == RuntimePlatform.Android)
            {
                string androidFilesDirectory;
                if (TryGetAndroidInternalFilesDirectory(out androidFilesDirectory))
                {
                    rootBasePath = androidFilesDirectory;
                }
            }

            return Path.GetFullPath(
                Path.Combine(
                    rootBasePath,
                    PreparedStreamingAssetsRootName,
                    BuildPreparedSourceNamespaceKey()));
        }

        private static bool TryGetAndroidInternalFilesDirectory(out string path)
        {
            path = string.Empty;
            if (Application.platform != RuntimePlatform.Android)
            {
                return false;
            }

            try
            {
                var unityPlayerClass = CreateAndroidJavaClass("com.unity3d.player.UnityPlayer");
                if (unityPlayerClass == null)
                {
                    return false;
                }

                using (var unityPlayerDisposable = unityPlayerClass as IDisposable)
                {
                    var activity = AndroidJavaGetStaticObject(unityPlayerClass, "currentActivity");
                    if (activity == null)
                    {
                        return false;
                    }

                    using (var activityDisposable = activity as IDisposable)
                    {
                        var filesDir = AndroidJavaCallObject(activity, "getFilesDir");
                        if (filesDir == null)
                        {
                            return false;
                        }

                        using (var filesDirDisposable = filesDir as IDisposable)
                        {
                            var absolutePath = AndroidJavaCallString(filesDir, "getAbsolutePath");
                            if (string.IsNullOrEmpty(absolutePath))
                            {
                                return false;
                            }

                            path = absolutePath;
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    "[MediaRuntimeCommon] android_internal_files_dir_failed"
                    + " error=" + ex.GetType().Name
                    + " message=" + ex.Message);
                path = string.Empty;
                return false;
            }
        }

        private static bool TryGetAndroidExternalFilesDirectory(out string path)
        {
            path = string.Empty;
            if (Application.platform != RuntimePlatform.Android)
            {
                return false;
            }

            try
            {
                var unityPlayerClass = CreateAndroidJavaClass("com.unity3d.player.UnityPlayer");
                if (unityPlayerClass == null)
                {
                    return false;
                }

                using (var unityPlayerDisposable = unityPlayerClass as IDisposable)
                {
                    var activity = AndroidJavaGetStaticObject(unityPlayerClass, "currentActivity");
                    if (activity == null)
                    {
                        return false;
                    }

                    using (var activityDisposable = activity as IDisposable)
                    {
                        var externalFilesDir = AndroidJavaCallObject(
                            activity,
                            "getExternalFilesDir",
                            new object[] { null });
                        if (externalFilesDir == null)
                        {
                            return false;
                        }

                        using (var externalFilesDirDisposable = externalFilesDir as IDisposable)
                        {
                            var absolutePath = AndroidJavaCallString(
                                externalFilesDir,
                                "getAbsolutePath");
                            if (string.IsNullOrEmpty(absolutePath))
                            {
                                return false;
                            }

                            path = absolutePath.Replace('\\', '/');
                            Debug.Log(
                                "[MediaRuntimeCommon] android_external_files_dir=" + path);
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    "[MediaRuntimeCommon] android_external_files_dir_failed"
                    + " error=" + ex.GetType().Name
                    + " message=" + ex.Message);
                path = string.Empty;
                return false;
            }
        }

        private static bool TryResolveAndroidReadableAbsolutePath(
            string uri,
            out string resolvedPath)
        {
            resolvedPath = string.Empty;
            if (Application.platform != RuntimePlatform.Android || string.IsNullOrEmpty(uri))
            {
                return false;
            }

            var normalizedInput = uri.Replace('\\', '/');
            var candidates = new List<string>();
            AddDistinctAndroidPathCandidate(candidates, normalizedInput);

            const string SdcardPrefix = "/sdcard/";
            const string StoragePrefix = "/storage/emulated/0/";
            var packageIdentifier = Application.identifier ?? string.Empty;

            if (normalizedInput.StartsWith(SdcardPrefix, StringComparison.OrdinalIgnoreCase))
            {
                AddDistinctAndroidPathCandidate(
                    candidates,
                    StoragePrefix + normalizedInput.Substring(SdcardPrefix.Length));
            }
            else if (normalizedInput.StartsWith(StoragePrefix, StringComparison.OrdinalIgnoreCase))
            {
                AddDistinctAndroidPathCandidate(
                    candidates,
                    SdcardPrefix + normalizedInput.Substring(StoragePrefix.Length));
            }

            string internalFilesDirectory;
            if (TryGetAndroidInternalFilesDirectory(out internalFilesDirectory))
            {
                Debug.Log(
                    "[MediaRuntimeCommon] android_absolute_path_probe"
                    + " input=" + normalizedInput
                    + " internalFilesDir=" + internalFilesDirectory);

                var packageInternalUserPrefix = "/data/user/0/"
                    + packageIdentifier
                    + "/files/";
                var packageInternalDataPrefix = "/data/data/"
                    + packageIdentifier
                    + "/files/";

                if (normalizedInput.StartsWith(packageInternalUserPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    AddDistinctAndroidPathCandidate(
                        candidates,
                        CombineAndroidPath(
                            internalFilesDirectory,
                            normalizedInput.Substring(packageInternalUserPrefix.Length)));
                }

                if (normalizedInput.StartsWith(packageInternalDataPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    AddDistinctAndroidPathCandidate(
                        candidates,
                        CombineAndroidPath(
                            internalFilesDirectory,
                            normalizedInput.Substring(packageInternalDataPrefix.Length)));
                }

                if (!normalizedInput.StartsWith(StoragePrefix, StringComparison.OrdinalIgnoreCase)
                    && !normalizedInput.StartsWith(SdcardPrefix, StringComparison.OrdinalIgnoreCase)
                    && !normalizedInput.StartsWith("/data/", StringComparison.OrdinalIgnoreCase))
                {
                    AddDistinctAndroidPathCandidate(
                        candidates,
                        CombineAndroidPath(
                            internalFilesDirectory,
                            normalizedInput.TrimStart('/')));
                }
            }

            string externalFilesDirectory;
            if (TryGetAndroidExternalFilesDirectory(out externalFilesDirectory))
            {
                Debug.Log(
                    "[MediaRuntimeCommon] android_absolute_path_probe"
                    + " input=" + normalizedInput
                    + " externalFilesDir=" + externalFilesDirectory);
                var packageExternalPrefix = StoragePrefix
                    + "Android/data/"
                    + packageIdentifier
                    + "/files/";
                var packageSdcardPrefix = SdcardPrefix
                    + "Android/data/"
                    + packageIdentifier
                    + "/files/";

                if (normalizedInput.StartsWith(packageExternalPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    AddDistinctAndroidPathCandidate(
                        candidates,
                        CombineAndroidPath(
                            externalFilesDirectory,
                            normalizedInput.Substring(packageExternalPrefix.Length)));
                }

                if (normalizedInput.StartsWith(packageSdcardPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    AddDistinctAndroidPathCandidate(
                        candidates,
                        CombineAndroidPath(
                            externalFilesDirectory,
                            normalizedInput.Substring(packageSdcardPrefix.Length)));
                }
            }

            for (var index = 0; index < candidates.Count; index++)
            {
                string androidReadablePath;
                if (TryGetAndroidReadableFilePath(candidates[index], out androidReadablePath))
                {
                    Debug.Log(
                        "[MediaRuntimeCommon] android_absolute_path_resolved"
                        + " input=" + normalizedInput
                        + " candidate=" + candidates[index]
                        + " resolved=" + androidReadablePath);
                    resolvedPath = androidReadablePath;
                    return true;
                }
            }

            Debug.LogWarning(
                "[MediaRuntimeCommon] android_absolute_path_probe_failed"
                + " input=" + normalizedInput
                + " candidates=" + string.Join(" | ", candidates.ToArray()));
            return false;
        }

        private static void AddDistinctAndroidPathCandidate(
            List<string> candidates,
            string candidate)
        {
            if (candidates == null || string.IsNullOrEmpty(candidate))
            {
                return;
            }

            var normalizedCandidate = candidate.Replace('\\', '/');
            for (var index = 0; index < candidates.Count; index++)
            {
                if (string.Equals(
                    candidates[index],
                    normalizedCandidate,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            candidates.Add(normalizedCandidate);
        }

        private static string CombineAndroidPath(string root, string relativePath)
        {
            var normalizedRoot = (root ?? string.Empty).Replace('\\', '/').TrimEnd('/');
            var normalizedRelativePath = (relativePath ?? string.Empty)
                .Replace('\\', '/')
                .TrimStart('/');
            return string.IsNullOrEmpty(normalizedRelativePath)
                ? normalizedRoot
                : normalizedRoot + "/" + normalizedRelativePath;
        }

        private static bool TryGetAndroidReadableFilePath(
            string path,
            out string resolvedPath)
        {
            resolvedPath = string.Empty;
            if (Application.platform != RuntimePlatform.Android || string.IsNullOrEmpty(path))
            {
                return false;
            }

            try
            {
                var file = CreateAndroidJavaObject("java.io.File", path);
                if (file == null)
                {
                    return false;
                }

                using (var fileDisposable = file as IDisposable)
                {
                    var exists = AndroidJavaCallBool(file, "exists");
                    var canRead = AndroidJavaCallBool(file, "canRead");
                    Debug.Log(
                        "[MediaRuntimeCommon] android_file_probe"
                        + " path=" + path
                        + " exists=" + exists
                        + " canRead=" + canRead);
                    if (!exists || !canRead)
                    {
                        return false;
                    }

                    var canonicalPath = string.Empty;
                    try
                    {
                        canonicalPath = AndroidJavaCallString(file, "getCanonicalPath");
                    }
                    catch
                    {
                    }

                    if (string.IsNullOrEmpty(canonicalPath))
                    {
                        canonicalPath = AndroidJavaCallString(file, "getAbsolutePath");
                    }

                    if (string.IsNullOrEmpty(canonicalPath))
                    {
                        return false;
                    }

                    resolvedPath = canonicalPath.Replace('\\', '/');
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    "[MediaRuntimeCommon] android_readable_file_probe_failed"
                    + " path=" + path
                    + " error=" + ex.GetType().Name
                    + " message=" + ex.Message);
                resolvedPath = string.Empty;
                return false;
            }
        }

        private static string EncodeRelativeUriPath(string relativePath)
        {
            var segments = relativePath.Split('/');
            for (var index = 0; index < segments.Length; index++)
            {
                segments[index] = Uri.EscapeDataString(
                    Uri.UnescapeDataString(segments[index]));
            }

            return string.Join("/", segments);
        }

        private static string BuildPreparedSourceNamespaceKey()
        {
            var identity = string.Join(
                "|",
                Application.identifier ?? string.Empty,
                Application.version ?? string.Empty,
                Application.streamingAssetsPath ?? string.Empty);

            unchecked
            {
                ulong hash = 1469598103934665603UL;
                for (var index = 0; index < identity.Length; index++)
                {
                    hash ^= identity[index];
                    hash *= 1099511628211UL;
                }

                return hash.ToString("x16");
            }
        }

        private static string NormalizeRelativePath(string uri)
        {
            var normalized = uri.Replace('\\', '/').TrimStart('/');
            if (string.IsNullOrWhiteSpace(normalized))
            {
                throw new ArgumentException("媒体地址不能为空。", "uri");
            }

            if (normalized.Contains("../") || normalized.Contains("..\\"))
            {
                throw new InvalidOperationException(
                    "媒体地址不能包含父目录跳转。");
            }

            return normalized;
        }
    }
}
