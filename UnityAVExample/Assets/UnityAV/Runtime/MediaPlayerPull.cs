using System;
using System.Collections;
using System.Runtime.InteropServices;
using System.Text;
using DiagnosticsStopwatch = System.Diagnostics.Stopwatch;
using UnityEngine;

namespace UnityAV
{
    /// <summary>
    /// 使用拉帧/拉音频模式的播放器。
    /// 这是 Windows/iOS/Android 的统一主播放路径，优先级高于纹理直连模式。
    /// </summary>
    public class MediaPlayerPull : MonoBehaviour
    {
        private const int DefaultWidth = 1024;
        private const int DefaultHeight = 1024;
        private const int InvalidPlayerId = -1;
        private const int StreamingAudioClipLengthSeconds = 1800;
        private const int DefaultRealtimeSteadyAdditionalAudioSinkDelayMilliseconds = 60;
        private const double RealtimeObservedAudioClockClampSeconds = 0.180;
        private const int MaxAudioCopyBytes = 64 * 1024;
        private const int MaxAudioCopyIterations = 16;

        private enum RustAVAudioSampleFormat
        {
            Unknown = 0,
            Float32 = 1
        }

        public enum PullVideoRendererKind
        {
            Cpu = 0,
            Wgpu = 1
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RustAVFrameMeta
        {
            public int Width;
            public int Height;
            public int Format;
            public int Stride;
            public int DataSize;
            public double TimeSec;
            public long FrameIndex;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RustAVAudioMeta
        {
            public int SampleRate;
            public int Channels;
            public int BytesPerSample;
            public int SampleFormat;
            public int BufferedBytes;
            public double TimeSec;
            public long FrameIndex;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RustAVStreamInfo
        {
            public uint StructSize;
            public uint StructVersion;
            public int StreamIndex;
            public int CodecType;
            public int Width;
            public int Height;
            public int SampleRate;
            public int Channels;
        }

        public struct PlayerRuntimeHealth
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
        }

        private enum RustAVPlayerState
        {
            Idle = 0,
            Connecting = 1,
            Ready = 2,
            Playing = 3,
            Paused = 4,
            Shutdown = 5,
            Ended = 6
        }

        private enum RustAVPlayerStopReason
        {
            None = 0,
            UserStop = 1,
            EndOfStream = 2
        }

        /// <summary>
        /// 媒体地址，支持本地文件、RTSP、RTMP。
        /// </summary>
        [Header("Media Properties:")]
        public string Uri;

        /// <summary>
        /// 优先使用的底层后端。
        /// </summary>
        public MediaBackendKind PreferredBackend = MediaBackendKind.Auto;

        /// <summary>
        /// 是否严格要求指定后端；开启后不允许静默回退。
        /// </summary>
        public bool StrictBackend;

        /// <summary>
        /// 是否循环播放。
        /// </summary>
        public bool Loop;

        /// <summary>
        /// 是否在创建后立即播放。
        /// </summary>
        public bool AutoPlay = true;

        /// <summary>
        /// 目标纹理宽度。
        /// </summary>
        [Header("Video Target Properties:")]
        [Range(2, 4096)]
        public int Width = DefaultWidth;

        /// <summary>
        /// 目标纹理高度。
        /// </summary>
        [Range(2, 4096)]
        public int Height = DefaultHeight;

        /// <summary>
        /// 拉帧模式下使用的内部视频渲染器。
        /// Cpu 表示传统 CPU/RGBA 导出路径；
        /// Wgpu 表示使用 Rust 侧 wgpu 渲染后再导出 RGBA。
        /// </summary>
        public PullVideoRendererKind VideoRenderer = PullVideoRendererKind.Cpu;

        /// <summary>
        /// 用于显示视频的材质。
        /// </summary>
        public Material TargetMaterial;

        /// <summary>
        /// 是否启用音频输出。
        /// </summary>
        [Header("Audio Properties:")]
        public bool EnableAudio = true;

        /// <summary>
        /// 是否在缓冲足够后自动启动 Unity 音频播放。
        /// </summary>
        public bool AutoStartAudio = true;

        /// <summary>
        /// 对实时流额外补偿的音频输出延迟，覆盖 Unity 混音线程和设备调度抖动。
        /// </summary>
        [Range(0, 500)]
        public int RealtimeAdditionalAudioSinkDelayMilliseconds =
            DefaultRealtimeSteadyAdditionalAudioSinkDelayMilliseconds;

        private Texture2D _targetTexture;
        private int _id = InvalidPlayerId;
        private long _lastFrameIndex = -1;
        private double _lastPresentedVideoTimeSec = -1.0;
        private byte[] _videoBytes = new byte[0];
        private bool _isRealtimeSource;

        private AudioSource _audioSource;
        private AudioClip _audioClip;
        private byte[] _audioBytes = new byte[0];
        private float[] _audioFloats = new float[0];
        private float[] _audioRing = new float[0];
        private int _audioReadIndex;
        private int _audioWriteIndex;
        private int _audioBufferedSamples;
        private int _audioChannels;
        private int _audioSourceSampleRate;
        private int _audioSampleRate;
        private int _audioBytesPerSample;
        private bool _playRequested;
        private bool _resumeAfterPause;
        private MediaBackendKind _actualBackendKind = MediaBackendKind.Auto;
        private PullVideoRendererKind _actualVideoRenderer = PullVideoRendererKind.Cpu;
        private float _playRequestedRealtimeAt = -1f;
        private float _firstVideoFrameRealtimeAt = -1f;
        private float _firstAudioStartRealtimeAt = -1f;
        private float _firstPositivePlaybackTimeRealtimeAt = -1f;
        private double _latestQueuedAudioEndTimeSec = -1.0;
        private double _nextBufferedAudioTimeSec = -1.0;
        private double _audioPlaybackAnchorTimeSec = -1.0;
        private double _audioPlaybackAnchorDspTimeSec = -1.0;
        private double _audioPlaybackAnchorPitch = 1.0;
        private int _audioSetPositionCount;
        private int _audioReadCallbackCount;
        private int _audioReadUnderflowCount;
        private int _audioLastReadRequestSamples;
        private int _audioLastReadFilledSamples;
        private int _audioHighWaterSamples;
        private int _nativeBufferedAudioBytes;
        private int _nativeBufferedAudioHighWaterBytes;
        private int _audioLastReportedUnderflowCount;
        private float _nextAudioDiagnosticRealtimeAt;
        private int _videoDiagnosticUpdateCount;
        private int _videoDiagnosticPresentedCount;
        private long _videoDiagnosticPresentedBytes;
        private int _videoDiagnosticIntervalSampleCount;
        private double _videoDiagnosticPresentedRealtimeDeltaSumSec;
        private double _videoDiagnosticPresentedRealtimeDeltaMaxSec;
        private double _videoDiagnosticPresentedSourceDeltaSumSec;
        private double _videoDiagnosticPresentedSourceDeltaMaxSec;
        private double _videoDiagnosticPresentedOverrunPositiveSumSec;
        private double _videoDiagnosticPresentedOverrunMaxSec;
        private int _videoDiagnosticPresentedLongIntervalCount;
        private long _videoDiagnosticPresentedLifetimeCount;
        private long _videoDiagnosticPresentedLifetimeBytes;
        private int _videoDiagnosticIntervalLifetimeSampleCount;
        private double _videoDiagnosticPresentedRealtimeLifetimeSumSec;
        private double _videoDiagnosticPresentedRealtimeLifetimeMaxSec;
        private double _videoDiagnosticPresentedSourceLifetimeSumSec;
        private double _videoDiagnosticPresentedSourceLifetimeMaxSec;
        private double _videoDiagnosticPresentedOverrunPositiveLifetimeSumSec;
        private double _videoDiagnosticPresentedOverrunLifetimeMaxSec;
        private int _videoDiagnosticPresentedLongIntervalLifetimeCount;
        private double _updatePlayerElapsedMsAccum;
        private double _updateVideoFrameElapsedMsAccum;
        private double _updateAudioBufferElapsedMsAccum;
        private float _lastVideoDiagnosticRealtimeAt = -1f;
        private float _nextVideoDiagnosticRealtimeAt;
        private float _lastPresentedVideoRealtimeAt = -1f;
        private MediaNativeInteropCommon.AudioOutputPolicyView _audioOutputPolicy;
        private bool _hasAudioOutputPolicy;
        private bool _audioOutputPolicyMissingLogged;
        private readonly object _audioLock = new object();
        private float[] _audioPlaybackFloats = new float[0];
        private float _appliedPassiveAvSyncAudioResamplePitch = 1.0f;
        private bool _appliedPassiveAvSyncAudioResampleActive;
        private bool _hasAppliedPassiveAvSyncAudioResampleState;

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerCreatePullRGBA")]
        private static extern int CreatePlayerPullRGBA(string uri, int targetWidth, int targetHeight);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerCreatePullRGBAEx")]
        private static extern int CreatePlayerPullRGBAEx(
            string uri,
            int targetWidth,
            int targetHeight,
            ref MediaNativeInteropCommon.RustAVPlayerOpenOptions options);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerCreateWgpuRGBA")]
        private static extern int CreatePlayerWgpuRGBA(string uri, int targetWidth, int targetHeight);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerCreateWgpuRGBAEx")]
        private static extern int CreatePlayerWgpuRGBAEx(
            string uri,
            int targetWidth,
            int targetHeight,
            ref MediaNativeInteropCommon.RustAVPlayerOpenOptions options);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerOpenSession")]
        private static extern int OpenPlayerSession(
            string uri,
            ref MediaNativeInteropCommon.RustAVPlayerSessionOpenOptions options);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerRelease")]
        private static extern int ReleasePlayer(int id);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerUpdate")]
        private static extern int UpdatePlayer(int id);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerGetDuration")]
        private static extern double Duration(int id);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerGetTime")]
        private static extern double Time(int id);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerSetLoop")]
        private static extern int SetLoop(int id, bool loop);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerSetAudioSinkDelaySeconds")]
        private static extern int SetAudioSinkDelaySeconds(int id, double delaySec);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerGetFrameMetaRGBA")]
        private static extern int GetFrameMetaRGBA(int id, out RustAVFrameMeta outMeta);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerCopyFrameRGBA")]
        private static extern int CopyFrameRGBA(int id, byte[] destination, int destinationLength);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerGetAudioMetaPCM")]
        private static extern int GetAudioMetaPCM(int id, out RustAVAudioMeta outMeta);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerCopyAudioPCM")]
        private static extern int CopyAudioPCM(int id, byte[] destination, int destinationLength);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerGetStreamInfo")]
        private static extern int GetStreamInfo(int id, int streamIndex, ref RustAVStreamInfo outInfo);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerGetBackendKind")]
        private static extern int GetPlayerBackendKind(int id);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerGetHealthSnapshotV2")]
        private static extern int GetPlayerHealthSnapshotV2(
            int id,
            ref MediaNativeInteropCommon.RustAVPlayerHealthSnapshotV2 snapshot);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerGetLatestVideoFrameContract")]
        private static extern int GetLatestVideoFrameContract(
            int id,
            ref MediaNativeInteropCommon.RustAVVideoFrameContract contract);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerGetNativeVideoBridgeDescriptor")]
        private static extern int GetNativeVideoBridgeDescriptor(
            int id,
            ref MediaNativeInteropCommon.RustAVNativeVideoBridgeDescriptor descriptor);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerGetNativeVideoPathSelection")]
        private static extern int GetNativeVideoPathSelection(
            int id,
            ref MediaNativeInteropCommon.RustAVNativeVideoPathSelection selection);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerGetWgpuRenderDescriptor")]
        private static extern int GetWgpuRenderDescriptor(
            int id,
            ref MediaNativeInteropCommon.RustAVWgpuRenderDescriptor descriptor);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerGetWgpuRenderStateView")]
        private static extern int GetWgpuRenderStateView(
            int id,
            ref MediaNativeInteropCommon.RustAVWgpuRenderStateView state);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerGetLatestSourceVideoFrameContract")]
        private static extern int GetLatestSourceVideoFrameContract(
            int id,
            ref MediaNativeInteropCommon.RustAVVideoFrameContract contract);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerGetPlaybackTimingContract")]
        private static extern int GetPlaybackTimingContract(
            int id,
            ref MediaNativeInteropCommon.RustAVPlaybackTimingContract contract);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerGetAvSyncContract")]
        private static extern int GetAvSyncContract(
            int id,
            ref MediaNativeInteropCommon.RustAVAvSyncContract contract);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerGetAudioOutputPolicy")]
        private static extern int GetAudioOutputPolicy(
            int id,
            ref MediaNativeInteropCommon.RustAVAudioOutputPolicy policy);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerGetSourceTimelineContract")]
        private static extern int GetSourceTimelineContract(
            int id,
            ref MediaNativeInteropCommon.RustAVSourceTimelineContract contract);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerGetPlayerSessionContract")]
        private static extern int GetPlayerSessionContract(
            int id,
            ref MediaNativeInteropCommon.RustAVPlayerSessionContract contract);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerReportAudioStartupState")]
        private static extern int ReportAudioStartupState(
            int id,
            int audioSampleRate,
            int audioChannels,
            int bufferedSamples,
            double startupElapsedMilliseconds,
            bool hasPresentedVideoFrame,
            bool requiresPresentedVideoFrame,
            bool androidFileRateBridgeActive);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerGetAvSyncEnterpriseMetrics")]
        private static extern int GetAvSyncEnterpriseMetrics(
            int id,
            ref MediaNativeInteropCommon.RustAVAvSyncEnterpriseMetrics metrics);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerGetPassiveAvSyncSnapshot")]
        private static extern int GetPassiveAvSyncSnapshot(
            int id,
            ref MediaNativeInteropCommon.RustAVPassiveAvSyncSnapshot snapshot);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_GetBackendRuntimeDiagnostic")]
        private static extern int GetBackendRuntimeDiagnostic(
            int backendKind,
            string path,
            bool requireAudioExport,
            StringBuilder destination,
            int destinationLength);

        public MediaBackendKind ActualBackendKind
        {
            get { return _actualBackendKind; }
        }

        public PullVideoRendererKind ActualVideoRenderer
        {
            get { return _actualVideoRenderer; }
        }

        public bool HasPresentedVideoFrame
        {
            get { return _lastFrameIndex >= 0; }
        }

        public bool TryGetPresentedVideoTimeSec(out double presentedVideoTimeSec)
        {
            presentedVideoTimeSec = _lastPresentedVideoTimeSec;
            return presentedVideoTimeSec >= 0.0;
        }

        public bool IsAudioOutputActive
        {
            get { return _audioSource != null && _audioSource.isPlaying; }
        }

        public bool HasStartedPlayback
        {
            get
            {
                return HasPresentedVideoFrame
                    || IsAudioOutputActive
                    || _firstPositivePlaybackTimeRealtimeAt >= 0f;
            }
        }

        public bool TryGetEstimatedAudioPresentation(
            out double presentedTimeSec,
            out double pipelineDelaySec)
        {
            presentedTimeSec = -1.0;
            pipelineDelaySec = 0.0;

            if (!EnableAudio || _audioSampleRate <= 0 || _audioChannels <= 0)
            {
                return false;
            }

            var outputDelaySec = ComputeUnityAudioOutputDelaySeconds();
            var pipelineDelayEstimateSec = ComputeUnityAudioPipelineDelaySeconds();
            var latestQueuedAudioEndTimeSec = _latestQueuedAudioEndTimeSec;
            var hasQueuedTailEstimate = latestQueuedAudioEndTimeSec >= 0.0;
            var queuedTailEstimateSec = hasQueuedTailEstimate
                ? Math.Max(0.0, latestQueuedAudioEndTimeSec - pipelineDelayEstimateSec)
                : -1.0;

            double nextBufferedAudioTimeSec;
            lock (_audioLock)
            {
                nextBufferedAudioTimeSec = _nextBufferedAudioTimeSec;
            }

            if (_audioSource != null && _audioSource.isPlaying)
            {
                var dspAnchoredEstimateSec = TryGetDspAnchoredAudioPresentationTimeSec();
                var conservativeEstimateSec = dspAnchoredEstimateSec;
                if (nextBufferedAudioTimeSec >= 0.0)
                {
                    var readHeadEstimateSec = Math.Max(0.0, nextBufferedAudioTimeSec - outputDelaySec);
                    conservativeEstimateSec = conservativeEstimateSec >= 0.0
                        ? Math.Min(conservativeEstimateSec, readHeadEstimateSec)
                        : readHeadEstimateSec;
                }

                if (conservativeEstimateSec >= 0.0)
                {
                    pipelineDelaySec = outputDelaySec;
                    presentedTimeSec = hasQueuedTailEstimate
                        ? Math.Min(conservativeEstimateSec, queuedTailEstimateSec)
                        : conservativeEstimateSec;
                    ClampRealtimeObservedAudioLead(ref presentedTimeSec);
                    return true;
                }
            }

            if (!hasQueuedTailEstimate)
            {
                return false;
            }

            pipelineDelaySec = pipelineDelayEstimateSec;
            presentedTimeSec = queuedTailEstimateSec;
            ClampRealtimeObservedAudioLead(ref presentedTimeSec);
            return true;
        }

        private void ClampRealtimeObservedAudioLead(ref double presentedTimeSec)
        {
            if (!_isRealtimeSource || presentedTimeSec < 0.0 || !ValidatePlayerId(_id))
            {
                return;
            }

            var nativePlaybackTimeSec = Time(_id);
            if (nativePlaybackTimeSec < 0.0)
            {
                return;
            }

            var minAllowedAudioPresentationSec =
                Math.Max(0.0, nativePlaybackTimeSec - RealtimeObservedAudioClockClampSeconds);
            var maxAllowedAudioPresentationSec =
                nativePlaybackTimeSec + RealtimeObservedAudioClockClampSeconds;
            if (presentedTimeSec < minAllowedAudioPresentationSec)
            {
                presentedTimeSec = minAllowedAudioPresentationSec;
            }
            else if (presentedTimeSec > maxAllowedAudioPresentationSec)
            {
                presentedTimeSec = maxAllowedAudioPresentationSec;
            }
        }

        private double TryGetDspAnchoredAudioPresentationTimeSec()
        {
            if (_audioPlaybackAnchorTimeSec < 0.0 || _audioPlaybackAnchorDspTimeSec < 0.0)
            {
                return -1.0;
            }

            var elapsedSec = AudioSettings.dspTime - _audioPlaybackAnchorDspTimeSec;
            if (elapsedSec < -0.050)
            {
                return -1.0;
            }

            return Math.Max(
                0.0,
                _audioPlaybackAnchorTimeSec
                    + (Math.Max(0.0, elapsedSec) * Math.Max(0.0, _audioPlaybackAnchorPitch)));
        }

        private void RefreshAudioPlaybackAnchor()
        {
            if (_audioSource == null || !_audioSource.isPlaying)
            {
                return;
            }

            double nextBufferedAudioTimeSec;
            lock (_audioLock)
            {
                nextBufferedAudioTimeSec = _nextBufferedAudioTimeSec;
            }

            if (nextBufferedAudioTimeSec < 0.0)
            {
                return;
            }

            var outputDelaySec = ComputeUnityAudioOutputDelaySeconds();
            _audioPlaybackAnchorTimeSec = nextBufferedAudioTimeSec;
            _audioPlaybackAnchorDspTimeSec = AudioSettings.dspTime + outputDelaySec;
            _audioPlaybackAnchorPitch = ResolveCurrentAudioPitch();
        }

        private void ResetAudioPlaybackAnchor()
        {
            _audioPlaybackAnchorTimeSec = -1.0;
            _audioPlaybackAnchorDspTimeSec = -1.0;
            _audioPlaybackAnchorPitch = 1.0;
        }

        private float ResolveCurrentAudioPitch()
        {
            if (_audioSource == null)
            {
                return 1.0f;
            }

            return Mathf.Clamp(_audioSource.pitch, 0.995f, 1.005f);
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
                State = runtimeHealth.State,
                RuntimeState = runtimeHealth.RuntimeState,
                PlaybackIntent = runtimeHealth.PlaybackIntent,
                StopReason = runtimeHealth.StopReason,
                SourceConnectionState = runtimeHealth.SourceConnectionState,
                IsConnected = runtimeHealth.IsConnected,
                IsPlaying = runtimeHealth.IsPlaying,
                IsRealtime = runtimeHealth.IsRealtime,
                CanSeek = runtimeHealth.CanSeek,
                IsLooping = runtimeHealth.IsLooping,
                StreamCount = runtimeHealth.StreamCount,
                VideoDecoderCount = runtimeHealth.VideoDecoderCount,
                HasAudioDecoder = runtimeHealth.HasAudioDecoder,
                SourcePacketCount = runtimeHealth.SourcePacketCount,
                SourceTimeoutCount = runtimeHealth.SourceTimeoutCount,
                SourceReconnectCount = runtimeHealth.SourceReconnectCount,
                DurationSec = runtimeHealth.DurationSec,
                SourceLastActivityAgeSec = runtimeHealth.SourceLastActivityAgeSec,
                CurrentTimeSec = runtimeHealth.CurrentTimeSec,
                ExternalTimeSec = runtimeHealth.ExternalTimeSec,
            };
            return true;
        }

        internal bool TryGetLatestVideoFrameContract(
            out MediaNativeInteropCommon.VideoFrameContractView contract)
        {
            contract = default(MediaNativeInteropCommon.VideoFrameContractView);
            return ValidatePlayerId(_id)
                && MediaNativeInteropCommon.TryReadVideoFrameContract(
                    GetLatestVideoFrameContract,
                    _id,
                    out contract);
        }

        internal bool TryGetLatestSourceVideoFrameContract(
            out MediaNativeInteropCommon.VideoFrameContractView contract)
        {
            contract = default(MediaNativeInteropCommon.VideoFrameContractView);
            return ValidatePlayerId(_id)
                && MediaNativeInteropCommon.TryReadVideoFrameContract(
                    GetLatestSourceVideoFrameContract,
                    _id,
                    out contract);
        }

        internal bool TryGetPlaybackTimingContract(
            out MediaNativeInteropCommon.PlaybackTimingContractView contract)
        {
            contract = default(MediaNativeInteropCommon.PlaybackTimingContractView);
            return ValidatePlayerId(_id)
                && MediaNativeInteropCommon.TryReadPlaybackTimingContract(
                    GetPlaybackTimingContract,
                    _id,
                    out contract);
        }

        internal bool TryGetAvSyncContract(out MediaNativeInteropCommon.AvSyncContractView contract)
        {
            contract = default(MediaNativeInteropCommon.AvSyncContractView);
            return ValidatePlayerId(_id)
                && MediaNativeInteropCommon.TryReadAvSyncContract(
                    GetAvSyncContract,
                    _id,
                    out contract);
        }

        internal bool TryGetSourceTimelineContract(
            out MediaNativeInteropCommon.SourceTimelineContractView contract)
        {
            contract = default(MediaNativeInteropCommon.SourceTimelineContractView);
            return ValidatePlayerId(_id)
                && MediaNativeInteropCommon.TryReadSourceTimelineContract(
                    GetSourceTimelineContract,
                    _id,
                    out contract);
        }

        internal bool TryGetPlayerSessionContract(
            out MediaNativeInteropCommon.PlayerSessionContractView contract)
        {
            contract = default(MediaNativeInteropCommon.PlayerSessionContractView);
            return ValidatePlayerId(_id)
                && MediaNativeInteropCommon.TryReadPlayerSessionContract(
                    GetPlayerSessionContract,
                    _id,
                    out contract);
        }

        internal bool TryReportAudioStartupState(
            MediaNativeInteropCommon.AudioStartupObservationView observation)
        {
            return ValidatePlayerId(_id)
                && MediaNativeInteropCommon.TryReportAudioStartupState(
                    ReportAudioStartupState,
                    _id,
                    observation);
        }

        internal bool TryGetAvSyncEnterpriseMetrics(
            out MediaNativeInteropCommon.AvSyncEnterpriseMetricsView metrics)
        {
            metrics = default(MediaNativeInteropCommon.AvSyncEnterpriseMetricsView);
            return ValidatePlayerId(_id)
                && MediaNativeInteropCommon.TryReadAvSyncEnterpriseMetrics(
                    GetAvSyncEnterpriseMetrics,
                    _id,
                    out metrics);
        }

        internal bool TryGetPassiveAvSyncSnapshot(
            out MediaNativeInteropCommon.PassiveAvSyncSnapshotView snapshot)
        {
            snapshot = default(MediaNativeInteropCommon.PassiveAvSyncSnapshotView);
            return ValidatePlayerId(_id)
                && MediaNativeInteropCommon.TryReadPassiveAvSyncSnapshot(
                    GetPassiveAvSyncSnapshot,
                    _id,
                    out snapshot);
        }

        internal bool TryGetAudioOutputPolicy(
            out MediaNativeInteropCommon.AudioOutputPolicyView policy)
        {
            policy = default(MediaNativeInteropCommon.AudioOutputPolicyView);
            return ValidatePlayerId(_id)
                && MediaNativeInteropCommon.TryReadAudioOutputPolicy(
                    GetAudioOutputPolicy,
                    _id,
                    out policy);
        }

        internal bool TryGetNativeVideoBridgeDescriptor(
            out MediaNativeInteropCommon.NativeVideoBridgeDescriptorView descriptor)
        {
            descriptor = default(MediaNativeInteropCommon.NativeVideoBridgeDescriptorView);
            return ValidatePlayerId(_id)
                && MediaNativeInteropCommon.TryReadNativeVideoBridgeDescriptor(
                    GetNativeVideoBridgeDescriptor,
                    _id,
                    out descriptor);
        }

        internal bool TryGetNativeVideoPathSelection(
            out MediaNativeInteropCommon.NativeVideoPathSelectionView selection)
        {
            selection = default(MediaNativeInteropCommon.NativeVideoPathSelectionView);
            return ValidatePlayerId(_id)
                && MediaNativeInteropCommon.TryReadNativeVideoPathSelection(
                    GetNativeVideoPathSelection,
                    _id,
                    out selection);
        }

        internal bool TryGetWgpuRenderDescriptor(
            out MediaNativeInteropCommon.WgpuRenderDescriptorView descriptor)
        {
            descriptor = default(MediaNativeInteropCommon.WgpuRenderDescriptorView);
            return ValidatePlayerId(_id)
                && MediaNativeInteropCommon.TryReadWgpuRenderDescriptor(
                    GetWgpuRenderDescriptor,
                    _id,
                    out descriptor);
        }

        internal bool TryGetWgpuRenderStateView(
            out MediaNativeInteropCommon.WgpuRenderStateView state)
        {
            state = default(MediaNativeInteropCommon.WgpuRenderStateView);
            return ValidatePlayerId(_id)
                && MediaNativeInteropCommon.TryReadWgpuRenderStateView(
                    GetWgpuRenderStateView,
                    _id,
                    out state);
        }

        private void RefreshAudioOutputPolicy()
        {
            if (TryGetAudioOutputPolicy(out var policy))
            {
                _audioOutputPolicy = policy;
                _hasAudioOutputPolicy = true;
                _audioOutputPolicyMissingLogged = false;
                Debug.Log(
                    "[MediaPlayerPull] audio_output_policy_loaded file_start_ms="
                    + policy.FileStartThresholdMilliseconds
                    + " android_file_start_ms="
                    + policy.AndroidFileStartThresholdMilliseconds
                    + " file_ring_ms="
                    + policy.FileRingCapacityMilliseconds
                    + " android_file_ring_ms="
                    + policy.AndroidFileRingCapacityMilliseconds
                    + " realtime_start_ms="
                    + policy.RealtimeStartThresholdMilliseconds
                    + " realtime_ring_ms="
                    + policy.RealtimeRingCapacityMilliseconds
                    + " realtime_buffer_ms="
                    + policy.RealtimeBufferedCeilingMilliseconds
                    + " realtime_requires_video_frame="
                    + policy.RealtimeStartRequiresVideoFrame
                    + " android_file_rate_bridge="
                    + policy.AllowAndroidFileOutputRateBridge
                    + " realtime_backend_delay_ms="
                    + policy.RealtimeBackendAdditionalSinkDelayMilliseconds);
                return;
            }

            _hasAudioOutputPolicy = false;
            _audioOutputPolicy = default(MediaNativeInteropCommon.AudioOutputPolicyView);
        }

        private bool TryGetRequiredAudioOutputPolicy(
            string operation,
            out MediaNativeInteropCommon.AudioOutputPolicyView policy)
        {
            policy = default(MediaNativeInteropCommon.AudioOutputPolicyView);
            if (!_hasAudioOutputPolicy && ValidatePlayerId(_id))
            {
                RefreshAudioOutputPolicy();
            }

            if (_hasAudioOutputPolicy)
            {
                policy = _audioOutputPolicy;
                return true;
            }

            if (!_audioOutputPolicyMissingLogged)
            {
                Debug.LogWarning(
                    "[MediaPlayerPull] audio_output_policy_missing"
                    + " operation=" + operation
                    + " player_id=" + _id
                    + " is_realtime=" + _isRealtimeSource
                    + " play_requested=" + _playRequested);
                _audioOutputPolicyMissingLogged = true;
            }

            return false;
        }

        private int GetConfiguredRealtimeSteadyAdditionalAudioSinkDelayMilliseconds(
            MediaNativeInteropCommon.AudioOutputPolicyView policy)
        {
            if (RealtimeAdditionalAudioSinkDelayMilliseconds
                != DefaultRealtimeSteadyAdditionalAudioSinkDelayMilliseconds)
            {
                return RealtimeAdditionalAudioSinkDelayMilliseconds;
            }

            return policy.RealtimeSteadyAdditionalSinkDelayMilliseconds;
        }

        /// <summary>
        /// 开始或恢复播放。
        /// </summary>
        public void Play()
        {
            MediaNativeInteropCommon.PlayPlayerSession(_id, nameof(MediaPlayerPull));
            _playRequested = true;
            _playRequestedRealtimeAt = UnityEngine.Time.realtimeSinceStartup;
            TryStartAudioSource();
        }

        public void Prepare()
        {
            MediaNativeInteropCommon.PreparePlayerSession(_id, nameof(MediaPlayerPull));
        }

        public void Pause()
        {
            MediaNativeInteropCommon.PausePlayerSession(_id, nameof(MediaPlayerPull));

            _playRequested = false;
            ResetStartupTelemetry();
            if (_audioSource != null)
            {
                _audioSource.Pause();
            }
            UpdateNativeAudioSinkDelay();
        }

        public void Close()
        {
            ReleaseNativePlayer();
            ReleaseManagedResources();
        }

        /// <summary>
        /// 停止播放。
        /// </summary>
        public void Stop()
        {
            MediaNativeInteropCommon.StopPlayerSession(_id, nameof(MediaPlayerPull));

            _playRequested = false;
            ResetStartupTelemetry();
            if (_audioSource != null)
            {
                _audioSource.Pause();
            }
            UpdateNativeAudioSinkDelay();
        }

        /// <summary>
        /// 获取媒体总时长。
        /// </summary>
        public double Duration()
        {
            if (!ValidatePlayerId(_id))
            {
                throw new InvalidOperationException(nameof(MediaPlayerPull) +
                    " has no underlying valid native player.");
            }

            var result = Duration(_id);
            if (result < 0)
            {
                throw new Exception("Failed to get duration");
            }

            return result;
        }

        /// <summary>
        /// 获取当前播放时间。
        /// </summary>
        public double Time()
        {
            if (!ValidatePlayerId(_id))
            {
                throw new InvalidOperationException(nameof(MediaPlayerPull) +
                    " has no underlying valid native player.");
            }

            var result = Time(_id);
            if (result < 0)
            {
                throw new Exception("Failed to get time");
            }

            return result;
        }

        /// <summary>
        /// 获取主视频流的原始宽高。
        /// </summary>
        public bool TryGetPrimaryVideoSize(out int width, out int height)
        {
            width = 0;
            height = 0;

            if (!ValidatePlayerId(_id))
            {
                return false;
            }

            var info = new RustAVStreamInfo
            {
                StructSize = (uint)Marshal.SizeOf(typeof(RustAVStreamInfo)),
                StructVersion = 1u
            };

            var result = GetStreamInfo(_id, 0, ref info);
            if (result < 0 || info.Width <= 0 || info.Height <= 0)
            {
                return false;
            }

            width = info.Width;
            height = info.Height;
            return true;
        }

        /// <summary>
        /// 执行 seek，并清空 Unity 侧旧音频缓冲。
        /// </summary>
        public void Seek(double time)
        {
            MediaNativeInteropCommon.SeekPlayerSession(_id, time, nameof(MediaPlayerPull));

            ClearAudioBuffer();
            if (_audioSource != null)
            {
                _audioSource.Stop();
            }
            UpdateNativeAudioSinkDelay();
            TryStartAudioSource();
        }

        private IEnumerator Start()
        {
            Debug.Log("[MediaPlayerPull] startup_enter");
            try
            {
                Debug.Log("[MediaPlayerPull] native_initialize_begin");
                NativeInitializer.InitializePullOnly(this);
                Debug.Log("[MediaPlayerPull] native_initialize_complete");
            }
            catch (Exception ex)
            {
                Debug.LogError("[MediaPlayerPull] native_initialize_failed " + ex);
                throw;
            }

            MediaSourceResolver.PreparedMediaSource preparedSource = null;
            Exception resolveError = null;
            Debug.Log("[MediaPlayerPull] source_prepare_begin uri=" + Uri);
            yield return MediaSourceResolver.PreparePlayableSource(
                Uri,
                value => preparedSource = value,
                error => resolveError = error);
            Debug.Log("[MediaPlayerPull] source_prepare_complete uri=" + Uri);

            if (resolveError != null)
            {
                Debug.LogError("[MediaPlayerPull] source_prepare_failed " + resolveError);
                throw resolveError;
            }

            var uri = preparedSource.PlaybackUri;
            try
            {
                _isRealtimeSource = preparedSource.IsRealtimeSource;
                EnsureAudioSource();
                var diagnostic = ReadBackendRuntimeDiagnostic(uri);

                _targetTexture = new Texture2D(Width, Height, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Bilinear,
                    name = Uri
                };

                _id = CreateNativePlayer(uri);
                if (!ValidatePlayerId(_id))
                {
                    throw new Exception(
                        "Failed to create pull player with error: " + _id
                        + " requested_backend=" + PreferredBackend
                        + " strict_backend=" + StrictBackend
                        + " diagnostic=" + diagnostic);
                }
                _actualBackendKind = ReadActualBackendKind();
                RefreshAudioOutputPolicy();
                ResetStartupTelemetry();
                Debug.Log(
                    "[MediaPlayerPull] player_created requested_backend=" + PreferredBackend
                    + " actual_backend=" + _actualBackendKind
                    + " strict_backend=" + StrictBackend
                    + " requested_video_renderer=" + VideoRenderer
                    + " actual_video_renderer=" + _actualVideoRenderer);

                if (TargetMaterial != null)
                {
                    TargetMaterial.mainTexture = _targetTexture;
                }

                SetLoop(_id, Loop);
                Prepare();

                if (AutoPlay)
                {
                    Play();
                }
            }
            catch
            {
                ReleaseNativePlayer();
                ReleaseManagedResources();
                throw;
            }
        }

        private void Update()
        {
            if (!ValidatePlayerId(_id))
            {
                return;
            }

            _videoDiagnosticUpdateCount += 1;

            var updatePlayerStartTicks = DiagnosticsStopwatch.GetTimestamp();
            UpdatePlayer(_id);
            _updatePlayerElapsedMsAccum += ElapsedMilliseconds(updatePlayerStartTicks);

            var updateVideoStartTicks = DiagnosticsStopwatch.GetTimestamp();
            UpdateVideoFrame();
            _updateVideoFrameElapsedMsAccum += ElapsedMilliseconds(updateVideoStartTicks);

            var updateAudioStartTicks = DiagnosticsStopwatch.GetTimestamp();
            UpdateAudioBuffer();
            _updateAudioBufferElapsedMsAccum += ElapsedMilliseconds(updateAudioStartTicks);
            UpdatePlaybackEndState();
            RecordPositivePlaybackTimeIfNeeded();
            UpdatePassiveAvSyncAudioResample();
            UpdateNativeAudioSinkDelay();
            EmitAudioDiagnostics();
            EmitVideoDiagnostics();
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (!ValidatePlayerId(_id))
            {
                return;
            }

            if (pauseStatus)
            {
                _resumeAfterPause = _playRequested;
                if (_resumeAfterPause)
                {
                    Pause();
                }
            }
            else if (_resumeAfterPause)
            {
                Play();
                _resumeAfterPause = false;
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

        private void EnsureAudioSource()
        {
            if (_audioSource != null)
            {
                return;
            }

            _audioSource = GetComponent<AudioSource>();
            if (_audioSource == null)
            {
                _audioSource = gameObject.AddComponent<AudioSource>();
            }

            _audioSource.playOnAwake = false;
            _audioSource.loop = true;
            _audioSource.spatialBlend = 0f;
        }

        private void ResetAudioDiagnostics()
        {
            _audioSetPositionCount = 0;
            _audioReadCallbackCount = 0;
            _audioReadUnderflowCount = 0;
            _audioLastReadRequestSamples = 0;
            _audioLastReadFilledSamples = 0;
            _audioHighWaterSamples = 0;
            _nativeBufferedAudioBytes = 0;
            _nativeBufferedAudioHighWaterBytes = 0;
            _audioLastReportedUnderflowCount = 0;
            _nextAudioDiagnosticRealtimeAt = 0f;
        }

        private void ResetVideoDiagnostics()
        {
            _videoDiagnosticUpdateCount = 0;
            _videoDiagnosticPresentedCount = 0;
            _videoDiagnosticPresentedBytes = 0;
            _videoDiagnosticIntervalSampleCount = 0;
            _videoDiagnosticPresentedRealtimeDeltaSumSec = 0.0;
            _videoDiagnosticPresentedRealtimeDeltaMaxSec = 0.0;
            _videoDiagnosticPresentedSourceDeltaSumSec = 0.0;
            _videoDiagnosticPresentedSourceDeltaMaxSec = 0.0;
            _videoDiagnosticPresentedOverrunPositiveSumSec = 0.0;
            _videoDiagnosticPresentedOverrunMaxSec = 0.0;
            _videoDiagnosticPresentedLongIntervalCount = 0;
            _videoDiagnosticPresentedLifetimeCount = 0;
            _videoDiagnosticPresentedLifetimeBytes = 0;
            _videoDiagnosticIntervalLifetimeSampleCount = 0;
            _videoDiagnosticPresentedRealtimeLifetimeSumSec = 0.0;
            _videoDiagnosticPresentedRealtimeLifetimeMaxSec = 0.0;
            _videoDiagnosticPresentedSourceLifetimeSumSec = 0.0;
            _videoDiagnosticPresentedSourceLifetimeMaxSec = 0.0;
            _videoDiagnosticPresentedOverrunPositiveLifetimeSumSec = 0.0;
            _videoDiagnosticPresentedOverrunLifetimeMaxSec = 0.0;
            _videoDiagnosticPresentedLongIntervalLifetimeCount = 0;
            _updatePlayerElapsedMsAccum = 0.0;
            _updateVideoFrameElapsedMsAccum = 0.0;
            _updateAudioBufferElapsedMsAccum = 0.0;
            _lastVideoDiagnosticRealtimeAt = -1f;
            _nextVideoDiagnosticRealtimeAt = 0f;
            _lastPresentedVideoRealtimeAt = -1f;
        }

        private static double ElapsedMilliseconds(long startTicks)
        {
            return (DiagnosticsStopwatch.GetTimestamp() - startTicks) * 1000.0
                / DiagnosticsStopwatch.Frequency;
        }

        private void ObserveNativeBufferedAudioBytes(int bufferedBytes)
        {
            var normalized = Math.Max(0, bufferedBytes);
            _nativeBufferedAudioBytes = normalized;
            _nativeBufferedAudioHighWaterBytes =
                Math.Max(_nativeBufferedAudioHighWaterBytes, normalized);
        }

        private double BufferedAudioSecondsFromBytes(int bufferedBytes)
        {
            if (bufferedBytes <= 0
                || _audioSourceSampleRate <= 0
                || _audioChannels <= 0
                || _audioBytesPerSample <= 0)
            {
                return 0.0;
            }

            var bytesPerSecond = _audioSourceSampleRate * _audioChannels * _audioBytesPerSample;
            if (bytesPerSecond <= 0)
            {
                return 0.0;
            }

            return (double)bufferedBytes / bytesPerSecond;
        }

        private void EmitAudioDiagnostics()
        {
            if (!EnableAudio || _audioSampleRate <= 0 || _audioChannels <= 0)
            {
                return;
            }

            var now = UnityEngine.Time.realtimeSinceStartup;
            if (now < _nextAudioDiagnosticRealtimeAt)
            {
                return;
            }

            int callbackCount;
            int underflowCount;
            int requestedSamples;
            int filledSamples;
            int bufferedSamples;
            int highWaterSamples;
            lock (_audioLock)
            {
                callbackCount = _audioReadCallbackCount;
                underflowCount = _audioReadUnderflowCount;
                requestedSamples = _audioLastReadRequestSamples;
                filledSamples = _audioLastReadFilledSamples;
                bufferedSamples = _audioBufferedSamples;
                highWaterSamples = _audioHighWaterSamples;
            }

            var shouldLog = StartupElapsedSeconds <= 20f
                || callbackCount <= 0
                || underflowCount != _audioLastReportedUnderflowCount;
            if (!shouldLog)
            {
                _nextAudioDiagnosticRealtimeAt = now + 1f;
                return;
            }

            var bufferedMilliseconds = 0.0;
            var highWaterMilliseconds = 0.0;
            var nativeBufferedMilliseconds =
                BufferedAudioSecondsFromBytes(_nativeBufferedAudioBytes) * 1000.0;
            var nativeHighWaterMilliseconds =
                BufferedAudioSecondsFromBytes(_nativeBufferedAudioHighWaterBytes) * 1000.0;
            var scale = _audioSampleRate * _audioChannels;
            if (scale > 0)
            {
                bufferedMilliseconds = bufferedSamples * 1000.0 / scale;
                highWaterMilliseconds = highWaterSamples * 1000.0 / scale;
            }

            int dspBufferLength;
            int dspBufferCount;
            AudioSettings.GetDSPBufferSize(out dspBufferLength, out dspBufferCount);

            Debug.Log(
                "[MediaPlayerPull] audio_diag callbacks="
                + callbackCount
                + " underflows=" + underflowCount
                + " last_request=" + requestedSamples
                + " last_filled=" + filledSamples
                + " native_buffered_ms=" + nativeBufferedMilliseconds.ToString("F1")
                + " native_high_water_ms=" + nativeHighWaterMilliseconds.ToString("F1")
                + " buffered_ms=" + bufferedMilliseconds.ToString("F1")
                + " high_water_ms=" + highWaterMilliseconds.ToString("F1")
                + " source_hz=" + _audioSourceSampleRate
                + " clip_hz=" + _audioSampleRate
                + " output_hz=" + AudioSettings.outputSampleRate
                + " dsp_buffer=" + dspBufferLength + "x" + dspBufferCount
                + " set_position_count=" + _audioSetPositionCount
                + " is_playing=" + (_audioSource != null && _audioSource.isPlaying));

            _audioLastReportedUnderflowCount = underflowCount;
            _nextAudioDiagnosticRealtimeAt = now + 1f;
        }

        private void EmitVideoDiagnostics()
        {
            var now = UnityEngine.Time.realtimeSinceStartup;
            if (_lastVideoDiagnosticRealtimeAt < 0f)
            {
                _lastVideoDiagnosticRealtimeAt = now;
                _nextVideoDiagnosticRealtimeAt = now + 1f;
                return;
            }

            if (now < _nextVideoDiagnosticRealtimeAt)
            {
                return;
            }

            var elapsedSeconds = Math.Max(0.001f, now - _lastVideoDiagnosticRealtimeAt);
            var updateFps = _videoDiagnosticUpdateCount / elapsedSeconds;
            var presentedFps = _videoDiagnosticPresentedCount / elapsedSeconds;
            var updatePlayerAverageMs = _videoDiagnosticUpdateCount > 0
                ? _updatePlayerElapsedMsAccum / _videoDiagnosticUpdateCount
                : 0.0;
            var updateVideoAverageMs = _videoDiagnosticUpdateCount > 0
                ? _updateVideoFrameElapsedMsAccum / _videoDiagnosticUpdateCount
                : 0.0;
            var updateAudioAverageMs = _videoDiagnosticUpdateCount > 0
                ? _updateAudioBufferElapsedMsAccum / _videoDiagnosticUpdateCount
                : 0.0;
            var uploadMegabytesPerSecond = elapsedSeconds > 0f
                ? (_videoDiagnosticPresentedBytes / 1024.0 / 1024.0) / elapsedSeconds
                : 0.0;
            var presentedIntervalAverageMs = _videoDiagnosticIntervalSampleCount > 0
                ? (_videoDiagnosticPresentedRealtimeDeltaSumSec / _videoDiagnosticIntervalSampleCount) * 1000.0
                : 0.0;
            var sourceIntervalAverageMs = _videoDiagnosticIntervalSampleCount > 0
                ? (_videoDiagnosticPresentedSourceDeltaSumSec / _videoDiagnosticIntervalSampleCount) * 1000.0
                : 0.0;
            var overrunAverageMs = _videoDiagnosticIntervalSampleCount > 0
                ? (_videoDiagnosticPresentedOverrunPositiveSumSec / _videoDiagnosticIntervalSampleCount) * 1000.0
                : 0.0;
            var presentedIntervalAverageTotalMs = _videoDiagnosticIntervalLifetimeSampleCount > 0
                ? (_videoDiagnosticPresentedRealtimeLifetimeSumSec / _videoDiagnosticIntervalLifetimeSampleCount) * 1000.0
                : 0.0;
            var sourceIntervalAverageTotalMs = _videoDiagnosticIntervalLifetimeSampleCount > 0
                ? (_videoDiagnosticPresentedSourceLifetimeSumSec / _videoDiagnosticIntervalLifetimeSampleCount) * 1000.0
                : 0.0;
            var overrunAverageTotalMs = _videoDiagnosticIntervalLifetimeSampleCount > 0
                ? (_videoDiagnosticPresentedOverrunPositiveLifetimeSumSec / _videoDiagnosticIntervalLifetimeSampleCount) * 1000.0
                : 0.0;

            Debug.Log(
                "[MediaPlayerPull] video_diag update_fps="
                + updateFps.ToString("F1")
                + " presented_fps=" + presentedFps.ToString("F1")
                + " presented_total=" + _videoDiagnosticPresentedLifetimeCount
                + " presented_interval_count=" + _videoDiagnosticIntervalSampleCount
                + " presented_interval_ms_avg=" + presentedIntervalAverageMs.ToString("F2")
                + " presented_interval_ms_max=" + (_videoDiagnosticPresentedRealtimeDeltaMaxSec * 1000.0).ToString("F2")
                + " presented_interval_total_count=" + _videoDiagnosticIntervalLifetimeSampleCount
                + " presented_interval_ms_avg_total=" + presentedIntervalAverageTotalMs.ToString("F2")
                + " presented_interval_ms_max_total=" + (_videoDiagnosticPresentedRealtimeLifetimeMaxSec * 1000.0).ToString("F2")
                + " source_interval_ms_avg=" + sourceIntervalAverageMs.ToString("F2")
                + " source_interval_ms_max=" + (_videoDiagnosticPresentedSourceDeltaMaxSec * 1000.0).ToString("F2")
                + " source_interval_ms_avg_total=" + sourceIntervalAverageTotalMs.ToString("F2")
                + " source_interval_ms_max_total=" + (_videoDiagnosticPresentedSourceLifetimeMaxSec * 1000.0).ToString("F2")
                + " overrun_ms_avg=" + overrunAverageMs.ToString("F2")
                + " overrun_ms_max=" + (_videoDiagnosticPresentedOverrunMaxSec * 1000.0).ToString("F2")
                + " overrun_ms_avg_total=" + overrunAverageTotalMs.ToString("F2")
                + " overrun_ms_max_total=" + (_videoDiagnosticPresentedOverrunLifetimeMaxSec * 1000.0).ToString("F2")
                + " long_interval_count=" + _videoDiagnosticPresentedLongIntervalCount
                + " long_interval_total=" + _videoDiagnosticPresentedLongIntervalLifetimeCount
                + " update_player_ms_avg=" + updatePlayerAverageMs.ToString("F2")
                + " update_video_ms_avg=" + updateVideoAverageMs.ToString("F2")
                + " update_audio_ms_avg=" + updateAudioAverageMs.ToString("F2")
                + " upload_mib_per_sec=" + uploadMegabytesPerSecond.ToString("F1")
                + " frame_index=" + _lastFrameIndex
                + " frame_time=" + _lastPresentedVideoTimeSec.ToString("F3")
                + " texture=" + HasPresentedVideoFrame);

            _videoDiagnosticUpdateCount = 0;
            _videoDiagnosticPresentedCount = 0;
            _videoDiagnosticPresentedBytes = 0;
            _videoDiagnosticIntervalSampleCount = 0;
            _videoDiagnosticPresentedRealtimeDeltaSumSec = 0.0;
            _videoDiagnosticPresentedRealtimeDeltaMaxSec = 0.0;
            _videoDiagnosticPresentedSourceDeltaSumSec = 0.0;
            _videoDiagnosticPresentedSourceDeltaMaxSec = 0.0;
            _videoDiagnosticPresentedOverrunPositiveSumSec = 0.0;
            _videoDiagnosticPresentedOverrunMaxSec = 0.0;
            _videoDiagnosticPresentedLongIntervalCount = 0;
            _updatePlayerElapsedMsAccum = 0.0;
            _updateVideoFrameElapsedMsAccum = 0.0;
            _updateAudioBufferElapsedMsAccum = 0.0;
            _lastVideoDiagnosticRealtimeAt = now;
            _nextVideoDiagnosticRealtimeAt = now + 1f;
        }

        private static bool ValidatePlayerId(int id)
        {
            return id >= 0;
        }

        private void UpdateVideoFrame()
        {
            RustAVFrameMeta meta;
            var status = GetFrameMetaRGBA(_id, out meta);
            if (status <= 0 || meta.FrameIndex == _lastFrameIndex || meta.DataSize <= 0)
            {
                return;
            }

            if (_videoBytes.Length != meta.DataSize)
            {
                _videoBytes = new byte[meta.DataSize];
            }

            var copied = CopyFrameRGBA(_id, _videoBytes, _videoBytes.Length);
            if (copied != meta.DataSize)
            {
                return;
            }

            _targetTexture.LoadRawTextureData(_videoBytes);
            _targetTexture.Apply(false, false);

            var nowRealtime = UnityEngine.Time.realtimeSinceStartup;
            if (_lastPresentedVideoRealtimeAt >= 0f && _lastPresentedVideoTimeSec >= 0.0)
            {
                var realtimeDeltaSec = Math.Max(0.0, nowRealtime - _lastPresentedVideoRealtimeAt);
                var sourceDeltaSec = Math.Max(0.0, meta.TimeSec - _lastPresentedVideoTimeSec);
                if (realtimeDeltaSec > 0.0 && sourceDeltaSec > 0.0)
                {
                    _videoDiagnosticIntervalSampleCount += 1;
                    _videoDiagnosticPresentedRealtimeDeltaSumSec += realtimeDeltaSec;
                    _videoDiagnosticPresentedRealtimeDeltaMaxSec =
                        Math.Max(_videoDiagnosticPresentedRealtimeDeltaMaxSec, realtimeDeltaSec);
                    _videoDiagnosticPresentedSourceDeltaSumSec += sourceDeltaSec;
                    _videoDiagnosticPresentedSourceDeltaMaxSec =
                        Math.Max(_videoDiagnosticPresentedSourceDeltaMaxSec, sourceDeltaSec);
                    _videoDiagnosticIntervalLifetimeSampleCount += 1;
                    _videoDiagnosticPresentedRealtimeLifetimeSumSec += realtimeDeltaSec;
                    _videoDiagnosticPresentedRealtimeLifetimeMaxSec =
                        Math.Max(_videoDiagnosticPresentedRealtimeLifetimeMaxSec, realtimeDeltaSec);
                    _videoDiagnosticPresentedSourceLifetimeSumSec += sourceDeltaSec;
                    _videoDiagnosticPresentedSourceLifetimeMaxSec =
                        Math.Max(_videoDiagnosticPresentedSourceLifetimeMaxSec, sourceDeltaSec);

                    var overrunSec = Math.Max(0.0, realtimeDeltaSec - sourceDeltaSec);
                    _videoDiagnosticPresentedOverrunPositiveSumSec += overrunSec;
                    _videoDiagnosticPresentedOverrunMaxSec =
                        Math.Max(_videoDiagnosticPresentedOverrunMaxSec, overrunSec);
                    _videoDiagnosticPresentedOverrunPositiveLifetimeSumSec += overrunSec;
                    _videoDiagnosticPresentedOverrunLifetimeMaxSec =
                        Math.Max(_videoDiagnosticPresentedOverrunLifetimeMaxSec, overrunSec);

                    // Android 侧优先记录明显长间隔，后续由验证脚本结合源帧率做正式判定。
                    var longIntervalThresholdSec = Math.Max(sourceDeltaSec * 1.5, sourceDeltaSec + 0.008);
                    if (realtimeDeltaSec > longIntervalThresholdSec)
                    {
                        _videoDiagnosticPresentedLongIntervalCount += 1;
                        _videoDiagnosticPresentedLongIntervalLifetimeCount += 1;
                    }
                }
            }

            _lastFrameIndex = meta.FrameIndex;
            _lastPresentedVideoTimeSec = meta.TimeSec;
            _lastPresentedVideoRealtimeAt = nowRealtime;
            _videoDiagnosticPresentedCount += 1;
            _videoDiagnosticPresentedBytes += meta.DataSize;
            _videoDiagnosticPresentedLifetimeCount += 1;
            _videoDiagnosticPresentedLifetimeBytes += meta.DataSize;
            if (_firstVideoFrameRealtimeAt < 0f)
            {
                _firstVideoFrameRealtimeAt = nowRealtime;
                Debug.Log(
                    "[MediaPlayerPull] first_video_frame startup_seconds="
                    + StartupElapsedSeconds.ToString("F3")
                    + " frame_time="
                    + meta.TimeSec.ToString("F3"));
            }
        }

        private void UpdateAudioBuffer()
        {
            if (!EnableAudio || !ValidatePlayerId(_id))
            {
                return;
            }

            var latestNativeBufferedBytes = 0;
            for (var iteration = 0; iteration < MaxAudioCopyIterations; iteration++)
            {
                RustAVAudioMeta meta;
                var status = GetAudioMetaPCM(_id, out meta);
                if (status <= 0 || meta.BufferedBytes <= 0)
                {
                    latestNativeBufferedBytes = 0;
                    break;
                }

                if (!EnsureAudioFormat(meta))
                {
                    break;
                }

                latestNativeBufferedBytes = meta.BufferedBytes;

                var maxBufferedSamples = CalculateBufferedAudioCeilingSamples();
                var bufferedSamples = 0;
                lock (_audioLock)
                {
                    bufferedSamples = _audioBufferedSamples;
                }
                if (maxBufferedSamples > 0 && bufferedSamples >= maxBufferedSamples)
                {
                    break;
                }

                var bytesPerInterleavedSample = meta.BytesPerSample * meta.Channels;
                if (bytesPerInterleavedSample <= 0)
                {
                    break;
                }

                var bytesToCopy = Math.Min(meta.BufferedBytes, MaxAudioCopyBytes);
                if (maxBufferedSamples > 0)
                {
                    var remainingSamples = Math.Max(0, maxBufferedSamples - bufferedSamples);
                    var remainingBytes = remainingSamples * meta.BytesPerSample;
                    if (remainingBytes <= 0)
                    {
                        break;
                    }

                    bytesToCopy = Math.Min(bytesToCopy, remainingBytes);
                }
                bytesToCopy -= bytesToCopy % bytesPerInterleavedSample;
                if (bytesToCopy <= 0)
                {
                    break;
                }

                if (_audioBytes.Length != bytesToCopy)
                {
                    _audioBytes = new byte[bytesToCopy];
                }

                var copied = CopyAudioPCM(_id, _audioBytes, _audioBytes.Length);
                if (copied <= 0)
                {
                    break;
                }

                latestNativeBufferedBytes = Math.Max(0, meta.BufferedBytes - copied);

                var sampleCount = copied / meta.BytesPerSample;
                if (_audioFloats.Length != sampleCount)
                {
                    _audioFloats = new float[sampleCount];
                }

                Buffer.BlockCopy(_audioBytes, 0, _audioFloats, 0, copied);
                var playbackSamples = _audioFloats;
                var playbackSampleCount = sampleCount;
                if (_audioSourceSampleRate > 0
                    && _audioSampleRate > 0
                    && _audioSourceSampleRate != _audioSampleRate)
                {
                    playbackSampleCount = ResampleInterleavedAudioForPlayback(
                        _audioFloats,
                        sampleCount,
                        _audioSourceSampleRate,
                        _audioSampleRate,
                        _audioChannels);
                    if (playbackSampleCount <= 0)
                    {
                        break;
                    }

                    playbackSamples = _audioPlaybackFloats;
                }

                WriteAudioSamples(playbackSamples, playbackSampleCount, meta.TimeSec);

                if (copied < bytesToCopy)
                {
                    break;
                }
            }

            ObserveNativeBufferedAudioBytes(latestNativeBufferedBytes);
            TryStartAudioSource();
        }

        private void UpdatePlaybackEndState()
        {
            if (_isRealtimeSource || !ValidatePlayerId(_id) || _audioSource == null || !_audioSource.isPlaying)
            {
                return;
            }

            PlayerRuntimeHealth health;
            if (!TryGetRuntimeHealth(out health) || health.IsLooping)
            {
                return;
            }

            var reachedEndOfStream = health.StopReason == (int)RustAVPlayerStopReason.EndOfStream
                || health.State == (int)RustAVPlayerState.Ended
                || health.RuntimeState == (int)RustAVPlayerState.Ended
                || (!health.IsPlaying
                    && health.DurationSec > 0.0
                    && health.CurrentTimeSec >= health.DurationSec - 0.050);
            if (!reachedEndOfStream)
            {
                return;
            }

            var bufferedSamples = 0;
            lock (_audioLock)
            {
                bufferedSamples = _audioBufferedSamples;
            }

            if (_nativeBufferedAudioBytes > 0 || bufferedSamples > 0)
            {
                return;
            }

            _audioSource.Stop();
            ClearAudioBuffer();
            _playRequested = false;
            Debug.Log(
                "[MediaPlayerPull] playback_ended unity_audio_stopped current_time="
                + health.CurrentTimeSec.ToString("F3")
                + " duration="
                + health.DurationSec.ToString("F3")
                + " stop_reason="
                + health.StopReason);
        }

        private bool EnsureAudioFormat(RustAVAudioMeta meta)
        {
            if (meta.SampleRate <= 0
                || meta.Channels <= 0
                || meta.BytesPerSample != 4
                || meta.SampleFormat != (int)RustAVAudioSampleFormat.Float32)
            {
                return false;
            }

            if (!TryGetRequiredAudioOutputPolicy(nameof(EnsureAudioFormat), out var policy))
            {
                return false;
            }

            var playbackSampleRate = DeterminePlaybackSampleRate(meta.SampleRate, policy);
            if (playbackSampleRate <= 0)
            {
                return false;
            }
            if (_audioClip != null
                && _audioSourceSampleRate == meta.SampleRate
                && _audioSampleRate == playbackSampleRate
                && _audioChannels == meta.Channels
                && _audioBytesPerSample == meta.BytesPerSample)
            {
                return true;
            }

            _audioSourceSampleRate = meta.SampleRate;
            _audioSampleRate = playbackSampleRate;
            _audioChannels = meta.Channels;
            _audioBytesPerSample = meta.BytesPerSample;

            var ringCapacityMilliseconds =
                MediaNativeInteropCommon.ResolveAudioRingCapacityMilliseconds(
                    policy,
                    _isRealtimeSource,
                    IsAndroidFileAudioOutputRateBridgeActive(policy));
            var ringCapacity = Math.Max(
                (_audioSampleRate * _audioChannels * ringCapacityMilliseconds) / 1000,
                4096);
            lock (_audioLock)
            {
                _audioRing = new float[ringCapacity];
                _audioReadIndex = 0;
                _audioWriteIndex = 0;
                _audioBufferedSamples = 0;
                _audioHighWaterSamples = 0;
                _latestQueuedAudioEndTimeSec = -1.0;
                _nextBufferedAudioTimeSec = -1.0;
            }
            ResetAudioPlaybackAnchor();
            ResetAudioDiagnostics();

            if (_audioClip != null)
            {
                Destroy(_audioClip);
            }

            _audioClip = AudioClip.Create(
                Uri + "_PullAudio",
                _audioSampleRate * StreamingAudioClipLengthSeconds,
                _audioChannels,
                _audioSampleRate,
                true,
                OnAudioRead,
                OnAudioSetPosition);

            _audioSource.clip = _audioClip;
            _audioSource.loop = true;
            LogAudioOutputPolicyApplied(policy);
            if (_audioSourceSampleRate != _audioSampleRate)
            {
                Debug.Log(
                    "[MediaPlayerPull] audio_rate_bridge source_hz="
                    + _audioSourceSampleRate
                    + " clip_hz=" + _audioSampleRate
                    + " output_hz=" + AudioSettings.outputSampleRate
                    + " realtime=" + _isRealtimeSource);
            }
            return true;
        }

        private void LogAudioOutputPolicyApplied(
            MediaNativeInteropCommon.AudioOutputPolicyView policy)
        {
            var androidFileBridgeActive = IsAndroidFileAudioOutputRateBridgeActive(policy);
            var startThresholdMilliseconds =
                MediaNativeInteropCommon.ResolveAudioStartThresholdMilliseconds(
                    policy,
                    _isRealtimeSource,
                    androidFileBridgeActive);
            var ringCapacityMilliseconds =
                MediaNativeInteropCommon.ResolveAudioRingCapacityMilliseconds(
                    policy,
                    _isRealtimeSource,
                    androidFileBridgeActive);
            var bufferedCeilingMilliseconds =
                MediaNativeInteropCommon.ResolveAudioBufferedCeilingMilliseconds(
                    policy,
                    _isRealtimeSource,
                    androidFileBridgeActive);

            Debug.Log(
                "[MediaPlayerPull] audio_output_policy_applied source_sample_rate="
                + _audioSourceSampleRate
                + " clip_sample_rate=" + _audioSampleRate
                + " output_sample_rate=" + AudioSettings.outputSampleRate
                + " start_threshold_ms=" + startThresholdMilliseconds
                + " ring_capacity_ms=" + ringCapacityMilliseconds
                + " buffered_ceiling_ms=" + bufferedCeilingMilliseconds
                + " android_file_bridge_active=" + androidFileBridgeActive
                + " allow_android_file_output_rate_bridge="
                + policy.AllowAndroidFileOutputRateBridge
                + " realtime_start_requires_video_frame="
                + policy.RealtimeStartRequiresVideoFrame
                + " is_realtime=" + _isRealtimeSource);
        }

        private void WriteAudioSamples(float[] samples, int sampleCount, double chunkStartTimeSec)
        {
            if (samples == null || sampleCount <= 0)
            {
                return;
            }

            lock (_audioLock)
            {
                if (_audioRing == null || _audioRing.Length == 0)
                {
                    return;
                }

                var secondsPerSample = SecondsPerInterleavedSample();
                var canTrackAudioTime = chunkStartTimeSec >= 0.0 && secondsPerSample > 0.0;
                if (canTrackAudioTime && (_nextBufferedAudioTimeSec < 0.0 || _audioBufferedSamples <= 0))
                {
                    _nextBufferedAudioTimeSec = chunkStartTimeSec;
                }

                if (sampleCount >= _audioRing.Length)
                {
                    Array.Copy(
                        samples,
                        sampleCount - _audioRing.Length,
                        _audioRing,
                        0,
                        _audioRing.Length);
                    _audioReadIndex = 0;
                    _audioWriteIndex = 0;
                    _audioBufferedSamples = _audioRing.Length;
                    if (canTrackAudioTime)
                    {
                        _nextBufferedAudioTimeSec = chunkStartTimeSec
                            + (sampleCount - _audioRing.Length) * secondsPerSample;
                    }
                    TrimBufferedAudioSamplesIfNeeded();
                    RefreshBufferedAudioTailLocked();
                    return;
                }

                var freeSamples = _audioRing.Length - _audioBufferedSamples;
                if (sampleCount > freeSamples)
                {
                    var dropSamples = sampleCount - freeSamples;
                    _audioReadIndex = (_audioReadIndex + dropSamples) % _audioRing.Length;
                    _audioBufferedSamples -= dropSamples;
                    AdvanceBufferedAudioHeadLocked(dropSamples);
                }

                var firstCopy = Math.Min(sampleCount, _audioRing.Length - _audioWriteIndex);
                Array.Copy(samples, 0, _audioRing, _audioWriteIndex, firstCopy);

                var secondCopy = sampleCount - firstCopy;
                if (secondCopy > 0)
                {
                    Array.Copy(samples, firstCopy, _audioRing, 0, secondCopy);
                }

                _audioWriteIndex = (_audioWriteIndex + sampleCount) % _audioRing.Length;
                _audioBufferedSamples += sampleCount;
                TrimBufferedAudioSamplesIfNeeded();
                _audioHighWaterSamples = Math.Max(_audioHighWaterSamples, _audioBufferedSamples);
                RefreshBufferedAudioTailLocked();
            }
        }

        private void TrimBufferedAudioSamplesIfNeeded()
        {
            if (_audioRing == null || _audioRing.Length == 0)
            {
                return;
            }

            var maxBufferedSamples = CalculateBufferedAudioCeilingSamples();
            if (maxBufferedSamples <= 0 || _audioBufferedSamples <= maxBufferedSamples)
            {
                return;
            }

            var dropSamples = _audioBufferedSamples - maxBufferedSamples;
            _audioReadIndex = (_audioReadIndex + dropSamples) % _audioRing.Length;
            _audioBufferedSamples = maxBufferedSamples;
            AdvanceBufferedAudioHeadLocked(dropSamples);
            RefreshBufferedAudioTailLocked();
        }

        private int CalculateBufferedAudioCeilingSamples()
        {
            if (!TryGetRequiredAudioOutputPolicy(
                    nameof(CalculateBufferedAudioCeilingSamples),
                    out var policy))
            {
                return 0;
            }
            return MediaNativeInteropCommon.ResolveAudioBufferedCeilingSamples(
                policy,
                _isRealtimeSource,
                IsAndroidFileAudioOutputRateBridgeActive(policy),
                _audioSource != null && _audioSource.isPlaying,
                _audioSampleRate,
                _audioChannels,
                StartupElapsedSeconds * 1000f,
                !ShouldRequirePresentedVideoFrameBeforeAudioStart(policy) || HasPresentedVideoFrame);
        }

        private void OnAudioRead(float[] data)
        {
            if (data == null || data.Length == 0)
            {
                return;
            }

            Array.Clear(data, 0, data.Length);

            lock (_audioLock)
            {
                _audioReadCallbackCount += 1;
                _audioLastReadRequestSamples = data.Length;
                if (_audioBufferedSamples <= 0 || _audioRing == null || _audioRing.Length == 0)
                {
                    _audioLastReadFilledSamples = 0;
                    _audioReadUnderflowCount += 1;
                    return;
                }

                var samplesToRead = Math.Min(data.Length, _audioBufferedSamples);
                var firstCopy = Math.Min(samplesToRead, _audioRing.Length - _audioReadIndex);
                Array.Copy(_audioRing, _audioReadIndex, data, 0, firstCopy);

                var secondCopy = samplesToRead - firstCopy;
                if (secondCopy > 0)
                {
                    Array.Copy(_audioRing, 0, data, firstCopy, secondCopy);
                }

                _audioReadIndex = (_audioReadIndex + samplesToRead) % _audioRing.Length;
                _audioBufferedSamples -= samplesToRead;
                _audioLastReadFilledSamples = samplesToRead;
                if (samplesToRead < data.Length)
                {
                    _audioReadUnderflowCount += 1;
                }
                AdvanceBufferedAudioHeadLocked(samplesToRead);
                RefreshBufferedAudioTailLocked();
            }
        }

        private void OnAudioSetPosition(int position)
        {
            _audioSetPositionCount += 1;
            if (_audioSetPositionCount <= 3)
            {
                Debug.Log(
                    "[MediaPlayerPull] audio_set_position position="
                    + position
                    + " count=" + _audioSetPositionCount);
            }
        }

        private void ClearAudioBuffer()
        {
            lock (_audioLock)
            {
                _audioReadIndex = 0;
                _audioWriteIndex = 0;
                _audioBufferedSamples = 0;
                _audioHighWaterSamples = 0;
                _latestQueuedAudioEndTimeSec = -1.0;
                _nextBufferedAudioTimeSec = -1.0;
                if (_audioRing != null && _audioRing.Length > 0)
                {
                    Array.Clear(_audioRing, 0, _audioRing.Length);
                }
            }

            ResetAudioPlaybackAnchor();
            ResetAudioDiagnostics();
            UpdateNativeAudioSinkDelay();
        }

        private void TryStartAudioSource()
        {
            if (!EnableAudio || !AutoStartAudio || !_playRequested || _audioSource == null || _audioClip == null)
            {
                return;
            }

            if (_audioSource.isPlaying)
            {
                return;
            }

            if (!TryGetRequiredAudioOutputPolicy(nameof(TryStartAudioSource), out var policy))
            {
                return;
            }

            MediaNativeInteropCommon.AudioStartupObservationView observation;
            lock (_audioLock)
            {
                var androidFileBridgeActive = IsAndroidFileAudioOutputRateBridgeActive(policy);
                observation = MediaNativeInteropCommon.CreateAudioStartupObservation(
                    _audioSampleRate,
                    _audioChannels,
                    _audioBufferedSamples,
                    StartupElapsedSeconds * 1000f,
                    HasPresentedVideoFrame,
                    ShouldRequirePresentedVideoFrameBeforeAudioStart(policy),
                    androidFileBridgeActive);
            }

            if (!TryReportAudioStartupState(observation)) {
                return;
            }

            if (!TryGetPlayerSessionContract(out var playerSessionContract)
                || !playerSessionContract.AudioStartStateReported
                || !playerSessionContract.ShouldStartAudio)
            {
                return;
            }

            Debug.Log(
                "[MediaPlayerPull] audio_start_runtime_gate"
                + " state_reported=" + playerSessionContract.AudioStartStateReported
                + " should_start=" + playerSessionContract.ShouldStartAudio
                + " block_reason=" + playerSessionContract.AudioStartBlockReason
                + " required_buffered_samples=" + playerSessionContract.RequiredBufferedSamples
                + " reported_buffered_samples=" + playerSessionContract.ReportedBufferedSamples
                + " requires_presented_video_frame=" + playerSessionContract.RequiresPresentedVideoFrame
                + " has_presented_video_frame=" + playerSessionContract.HasPresentedVideoFrame
                + " android_file_rate_bridge_active=" + playerSessionContract.AndroidFileRateBridgeActive);
            _audioSource.Play();
            RefreshAudioPlaybackAnchor();
            UpdatePassiveAvSyncAudioResample();
            if (_firstAudioStartRealtimeAt < 0f)
            {
                _firstAudioStartRealtimeAt = UnityEngine.Time.realtimeSinceStartup;
                Debug.Log(
                    "[MediaPlayerPull] first_audio_start startup_seconds="
                    + StartupElapsedSeconds.ToString("F3"));
            }
            UpdateNativeAudioSinkDelay();
        }

        private void UpdatePassiveAvSyncAudioResample()
        {
            if (_audioSource == null)
            {
                return;
            }

            var hasSnapshot = TryGetPassiveAvSyncSnapshot(out var snapshot);
            var command = MediaNativeInteropCommon.ResolvePassiveAvSyncAudioResampleCommand(
                EnableAudio,
                _isRealtimeSource,
                _audioSource.isPlaying,
                hasSnapshot,
                snapshot);
            ApplyPassiveAvSyncAudioResample(command);
        }

        private void ApplyPassiveAvSyncAudioResample(
            MediaNativeInteropCommon.PassiveAvSyncAudioResampleCommandView command)
        {
            if (_audioSource == null)
            {
                return;
            }

            var changed = MediaNativeInteropCommon.ShouldApplyPassiveAvSyncAudioResampleCommand(
                _hasAppliedPassiveAvSyncAudioResampleState,
                _appliedPassiveAvSyncAudioResamplePitch,
                _appliedPassiveAvSyncAudioResampleActive,
                command);
            if (!changed)
            {
                return;
            }

            if (_audioSource.isPlaying)
            {
                var currentPresentationTimeSec = TryGetDspAnchoredAudioPresentationTimeSec();
                if (currentPresentationTimeSec >= 0.0)
                {
                    _audioPlaybackAnchorTimeSec = currentPresentationTimeSec;
                    _audioPlaybackAnchorDspTimeSec = AudioSettings.dspTime;
                }
            }

            _audioPlaybackAnchorPitch = command.Pitch;
            _audioSource.pitch = command.Pitch;
            _appliedPassiveAvSyncAudioResamplePitch = command.Pitch;
            _appliedPassiveAvSyncAudioResampleActive = command.Active;
            _hasAppliedPassiveAvSyncAudioResampleState = true;

            Debug.Log(
                "[MediaPlayerPull] passive_av_sync_audio_resample_applied pitch="
                + command.Pitch.ToString("F6")
                + " active=" + command.Active
                + " source=" + command.Source
                + " playing=" + _audioSource.isPlaying
                + " realtime=" + _isRealtimeSource);
        }

        private void RecordPositivePlaybackTimeIfNeeded()
        {
            if (_firstPositivePlaybackTimeRealtimeAt >= 0f || !ValidatePlayerId(_id))
            {
                return;
            }

            double playbackTime;
            try
            {
                playbackTime = Time(_id);
            }
            catch (Exception)
            {
                return;
            }

            if (playbackTime <= 0.0)
            {
                return;
            }

            _firstPositivePlaybackTimeRealtimeAt = UnityEngine.Time.realtimeSinceStartup;
            Debug.Log(
                "[MediaPlayerPull] first_positive_playback_time startup_seconds="
                + StartupElapsedSeconds.ToString("F3")
                + " playback_time="
                + playbackTime.ToString("F3"));
        }

        private void UpdateNativeAudioSinkDelay()
        {
            if (!ValidatePlayerId(_id))
            {
                return;
            }

            SetAudioSinkDelaySeconds(_id, ComputeUnityAudioPipelineDelaySeconds());
        }

        private double ComputeUnityAudioPipelineDelaySeconds()
        {
            var delaySec = 0.0;
            var audioStarted = _audioSource != null && _audioSource.isPlaying;
            if (EnableAudio && _audioSampleRate > 0 && _audioChannels > 0)
            {
                delaySec += MediaNativeInteropCommon.ResolveBufferedAudioSecondsFromBytes(
                    _nativeBufferedAudioBytes,
                    _audioSourceSampleRate,
                    _audioChannels,
                    _audioBytesPerSample);
                if (!_isRealtimeSource || audioStarted)
                {
                    lock (_audioLock)
                    {
                        delaySec += MediaNativeInteropCommon.ResolveBufferedAudioSecondsFromSamples(
                            _audioBufferedSamples,
                            _audioSampleRate,
                            _audioChannels);
                    }
                }

                int dspBufferLength;
                int dspBufferCount;
                AudioSettings.GetDSPBufferSize(out dspBufferLength, out dspBufferCount);
                delaySec += MediaNativeInteropCommon.ResolveUnityDspBufferedSeconds(
                    _audioSampleRate,
                    dspBufferLength,
                    dspBufferCount);
            }

            var realtimeAdditionalDelayMilliseconds =
                GetRealtimeAdditionalAudioSinkDelayMilliseconds(audioStarted);
            if (realtimeAdditionalDelayMilliseconds > 0)
            {
                delaySec += (double)realtimeAdditionalDelayMilliseconds / 1000.0;
            }

            return delaySec;
        }

        private double ComputeUnityAudioOutputDelaySeconds()
        {
            var delaySec = 0.0;
            if (EnableAudio && _audioSampleRate > 0 && _audioChannels > 0)
            {
                int dspBufferLength;
                int dspBufferCount;
                AudioSettings.GetDSPBufferSize(out dspBufferLength, out dspBufferCount);
                delaySec += MediaNativeInteropCommon.ResolveUnityDspBufferedSeconds(
                    _audioSampleRate,
                    dspBufferLength,
                    dspBufferCount);
            }

            var realtimeAdditionalDelayMilliseconds =
                GetRealtimeAdditionalAudioSinkDelayMilliseconds(true);
            if (realtimeAdditionalDelayMilliseconds > 0)
            {
                delaySec += (double)realtimeAdditionalDelayMilliseconds / 1000.0;
            }

            return delaySec;
        }

        private int GetRealtimeAdditionalAudioSinkDelayMilliseconds(bool audioStarted)
        {
            if (!TryGetRequiredAudioOutputPolicy(
                    nameof(GetRealtimeAdditionalAudioSinkDelayMilliseconds),
                    out var policy))
            {
                return 0;
            }
            return MediaNativeInteropCommon.ResolveRealtimeAdditionalSinkDelayMilliseconds(
                policy,
                _isRealtimeSource,
                audioStarted,
                true,
                GetConfiguredRealtimeSteadyAdditionalAudioSinkDelayMilliseconds(policy));
        }

        private double SecondsPerInterleavedSample()
        {
            if (_audioSampleRate <= 0 || _audioChannels <= 0)
            {
                return 0.0;
            }

            return 1.0 / (_audioSampleRate * _audioChannels);
        }

        private bool ShouldRequirePresentedVideoFrameBeforeAudioStart(
            MediaNativeInteropCommon.AudioOutputPolicyView policy)
        {
            return _isRealtimeSource && policy.RealtimeStartRequiresVideoFrame;
        }

        private bool IsAndroidFileAudioOutputRateBridgeActive(
            MediaNativeInteropCommon.AudioOutputPolicyView policy)
        {
            return MediaNativeInteropCommon.ResolveAndroidFileAudioOutputRateBridgeActive(
                policy,
                _isRealtimeSource,
                Application.platform,
                _audioSourceSampleRate,
                _audioSampleRate);
        }

        private int DeterminePlaybackSampleRate(
            int sourceSampleRate,
            MediaNativeInteropCommon.AudioOutputPolicyView policy)
        {
            return MediaNativeInteropCommon.ResolvePlaybackSampleRate(
                policy,
                _isRealtimeSource,
                Application.platform,
                sourceSampleRate,
                AudioSettings.outputSampleRate);
        }

        private int ResampleInterleavedAudioForPlayback(
            float[] source,
            int sourceSampleCount,
            int sourceSampleRate,
            int playbackSampleRate,
            int channels)
        {
            if (source == null
                || sourceSampleCount <= 0
                || sourceSampleRate <= 0
                || playbackSampleRate <= 0
                || channels <= 0
                || sourceSampleRate == playbackSampleRate)
            {
                return sourceSampleCount;
            }

            var sourceFrameCount = sourceSampleCount / channels;
            if (sourceFrameCount <= 1)
            {
                return sourceSampleCount;
            }

            var playbackFrameCount =
                (int)Math.Round(sourceFrameCount * (double)playbackSampleRate / sourceSampleRate);
            playbackFrameCount = Math.Max(playbackFrameCount, 1);
            var playbackSampleCount = playbackFrameCount * channels;
            if (_audioPlaybackFloats.Length != playbackSampleCount)
            {
                _audioPlaybackFloats = new float[playbackSampleCount];
            }

            var frameScale = (double)sourceSampleRate / playbackSampleRate;
            for (var frameIndex = 0; frameIndex < playbackFrameCount; frameIndex++)
            {
                var sourceFramePosition = frameIndex * frameScale;
                var sourceFrame0 = Math.Min((int)sourceFramePosition, sourceFrameCount - 1);
                var sourceFrame1 = Math.Min(sourceFrame0 + 1, sourceFrameCount - 1);
                var blend = sourceFramePosition - sourceFrame0;
                var destinationOffset = frameIndex * channels;
                var sourceOffset0 = sourceFrame0 * channels;
                var sourceOffset1 = sourceFrame1 * channels;
                for (var channelIndex = 0; channelIndex < channels; channelIndex++)
                {
                    var sample0 = source[sourceOffset0 + channelIndex];
                    var sample1 = source[sourceOffset1 + channelIndex];
                    _audioPlaybackFloats[destinationOffset + channelIndex] =
                        (float)(sample0 + ((sample1 - sample0) * blend));
                }
            }

            return playbackSampleCount;
        }

        private void RefreshBufferedAudioTailLocked()
        {
            if (_audioBufferedSamples <= 0 || _nextBufferedAudioTimeSec < 0.0)
            {
                _latestQueuedAudioEndTimeSec = -1.0;
                return;
            }

            var secondsPerSample = SecondsPerInterleavedSample();
            if (secondsPerSample <= 0.0)
            {
                _latestQueuedAudioEndTimeSec = -1.0;
                return;
            }

            _latestQueuedAudioEndTimeSec =
                _nextBufferedAudioTimeSec + _audioBufferedSamples * secondsPerSample;
        }

        private void AdvanceBufferedAudioHeadLocked(int consumedSamples)
        {
            if (consumedSamples <= 0 || _nextBufferedAudioTimeSec < 0.0)
            {
                return;
            }

            var secondsPerSample = SecondsPerInterleavedSample();
            if (secondsPerSample <= 0.0)
            {
                return;
            }

            _nextBufferedAudioTimeSec += consumedSamples * secondsPerSample;
        }

        private void ResetStartupTelemetry()
        {
            _playRequestedRealtimeAt = -1f;
            _firstVideoFrameRealtimeAt = -1f;
            _firstAudioStartRealtimeAt = -1f;
            _firstPositivePlaybackTimeRealtimeAt = -1f;
            ResetAudioPlaybackAnchor();
            ApplyPassiveAvSyncAudioResample(
                MediaNativeInteropCommon.CreatePassiveAvSyncAudioResampleCommand(
                    1.0f,
                    false,
                    "reset"));
            ResetVideoDiagnostics();
        }

        private void ReleaseNativePlayer()
        {
            if (!ValidatePlayerId(_id))
            {
                return;
            }

            if (_audioSource != null)
            {
                _audioSource.Stop();
            }

            ResetAudioPlaybackAnchor();
            SetAudioSinkDelaySeconds(_id, 0.0);
            MediaNativeInteropCommon.ClosePlayerSessionSilently(_id);
            _id = InvalidPlayerId;
            _hasAudioOutputPolicy = false;
            _audioOutputPolicy = default(MediaNativeInteropCommon.AudioOutputPolicyView);
            _audioOutputPolicyMissingLogged = false;
            _actualBackendKind = MediaBackendKind.Auto;
            _actualVideoRenderer = PullVideoRendererKind.Cpu;
            _playRequested = false;
            _resumeAfterPause = false;
            ResetStartupTelemetry();
        }

        private void ReleaseManagedResources()
        {
            if (_audioSource != null)
            {
                _audioSource.Stop();
            }

            ResetAudioPlaybackAnchor();

            if (TargetMaterial != null && ReferenceEquals(TargetMaterial.mainTexture, _targetTexture))
            {
                TargetMaterial.mainTexture = null;
            }

            if (_audioClip != null)
            {
                Destroy(_audioClip);
                _audioClip = null;
            }

            if (_targetTexture != null)
            {
                Destroy(_targetTexture);
                _targetTexture = null;
            }

            _videoBytes = new byte[0];
            _lastFrameIndex = -1;
            _lastPresentedVideoTimeSec = -1.0;
            _audioBytes = new byte[0];
            _audioFloats = new float[0];
            _latestQueuedAudioEndTimeSec = -1.0;
            ResetAudioPlaybackAnchor();
            lock (_audioLock)
            {
                _audioRing = new float[0];
                _audioReadIndex = 0;
                _audioWriteIndex = 0;
                _audioBufferedSamples = 0;
                _audioHighWaterSamples = 0;
                _nextBufferedAudioTimeSec = -1.0;
            }
            ResetAudioDiagnostics();
            ResetVideoDiagnostics();
        }

        private int CreateNativePlayer(string uri)
        {
            try
            {
                return CreatePlayerViaSessionOpen(uri);
            }
            catch (EntryPointNotFoundException)
            {
                Debug.LogWarning(
                    "[MediaPlayerPull] session open entrypoint missing, fallback to legacy create path");
                return CreateLegacyNativePlayer(uri);
            }
        }

        private int CreatePlayerViaSessionOpen(string uri)
        {
            var runtimePreferredBackend =
                MediaNativeInteropCommon.ResolveRuntimePreferredBackend(PreferredBackend);
            var outputKind = VideoRenderer == PullVideoRendererKind.Wgpu
                ? MediaNativeInteropCommon.RustAVPlayerSessionOutputKind.WgpuRgba
                : MediaNativeInteropCommon.RustAVPlayerSessionOutputKind.PullRgba;
            // Pull 路径历史上默认就会同时导出音频，这里保持兼容，不收窄旧行为。
            const uint outputFlags = MediaNativeInteropCommon.PlayerSessionOpenFlagAudioExport;
            var options = MediaNativeInteropCommon.CreateSessionOpenOptions(
                runtimePreferredBackend,
                StrictBackend,
                outputKind,
                Width,
                Height,
                outputFlags);
            _actualVideoRenderer = VideoRenderer == PullVideoRendererKind.Wgpu
                ? PullVideoRendererKind.Wgpu
                : PullVideoRendererKind.Cpu;
            return OpenPlayerSession(uri, ref options);
        }

        private int CreateLegacyNativePlayer(string uri)
        {
            var runtimePreferredBackend =
                MediaNativeInteropCommon.ResolveRuntimePreferredBackend(PreferredBackend);
            var options = MediaNativeInteropCommon.CreateOpenOptions(
                runtimePreferredBackend,
                StrictBackend);
            if (VideoRenderer == PullVideoRendererKind.Wgpu)
            {
                try
                {
                    _actualVideoRenderer = PullVideoRendererKind.Wgpu;
                    return CreatePlayerWgpuRGBAEx(uri, Width, Height, ref options);
                }
                catch (EntryPointNotFoundException)
                {
                    try
                    {
                        _actualVideoRenderer = PullVideoRendererKind.Wgpu;
                        return CreatePlayerWgpuRGBA(uri, Width, Height);
                    }
                    catch (EntryPointNotFoundException)
                    {
                        Debug.LogWarning(
                            "[MediaPlayerPull] wgpu entrypoint missing, fallback to cpu renderer");
                    }
                }
            }

            try
            {
                _actualVideoRenderer = PullVideoRendererKind.Cpu;
                return CreatePlayerPullRGBAEx(uri, Width, Height, ref options);
            }
            catch (EntryPointNotFoundException)
            {
                _actualVideoRenderer = PullVideoRendererKind.Cpu;
                return CreatePlayerPullRGBA(uri, Width, Height);
            }
        }

        private string ReadBackendRuntimeDiagnostic(string uri)
        {
            var runtimePreferredBackend =
                MediaNativeInteropCommon.ResolveRuntimePreferredBackend(PreferredBackend);
            return MediaNativeInteropCommon.ReadBackendRuntimeDiagnostic(
                GetBackendRuntimeDiagnostic,
                runtimePreferredBackend,
                uri,
                EnableAudio);
        }

        private MediaBackendKind ReadActualBackendKind()
        {
            try
            {
                return MediaNativeInteropCommon.NormalizeBackendKind(
                    GetPlayerBackendKind(_id),
                    MediaNativeInteropCommon.ResolveRuntimePreferredBackend(PreferredBackend));
            }
            catch (EntryPointNotFoundException)
            {
                return MediaNativeInteropCommon.ResolveRuntimePreferredBackend(PreferredBackend);
            }
        }
    }
}
