using System;
using System.Collections;
using UnityEngine;

namespace UnityAV
{
    /// <summary>
    /// 用于场景级验证的最小运行时驱动。
    /// 它会周期性输出播放时间、纹理和音频状态，并在超时后自动退出。
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public class CodexValidationDriver : MonoBehaviour
    {
        private const float MinimumPlaybackAdvanceSeconds = 1.0f;
        private const int PreviewTargetFrameRate = 120;

        public MediaPlayerPull Player;
        public float ValidationSeconds = 6f;
        public float StartupTimeoutSeconds = 10f;
        public float LogIntervalSeconds = 1f;
        public float RealtimeReferenceLagToleranceSeconds = 0.10f;
        public string UriArgumentName = "-uri=";
        public string BackendArgumentName = "-backend=";
        public string VideoRendererArgumentName = "-videoRenderer=";
        public string LoopArgumentName = "-loop=";
        public string ValidationSecondsArgumentName = "-validationSeconds=";
        public string StartupTimeoutSecondsArgumentName = "-startupTimeoutSeconds=";
        public string WindowWidthArgumentName = "-windowWidth=";
        public string WindowHeightArgumentName = "-windowHeight=";
        public string PublisherStartUnixMsArgumentName = "-publisherStartUnixMs=";
        public bool ForceWindowedMode = true;
        public Transform VideoSurface;
        public Camera ValidationCamera;

        private float _lastLogTime;
        private float _startTime;
        private int _requestedWindowWidth;
        private int _requestedWindowHeight;
        private bool _hasExplicitWindowWidth;
        private bool _hasExplicitWindowHeight;
        private bool _windowConfigured;
        private bool _sourceSizedWindowApplied;
        private bool _validationWindowStarted;
        private float _validationWindowStartTime;
        private string _validationWindowStartReason = string.Empty;
        private double _validationWindowInitialPlaybackTime = -1.0;
        private double _maxObservedPlaybackTime = -1.0;
        private long _publisherStartUnixMs = -1;
        private bool _hasPublisherStartUnixMs;
        private bool _observedTextureDuringWindow;
        private bool _observedAudioDuringWindow;
        private bool _observedStartedDuringWindow;

        private void Awake()
        {
            if (Player == null)
            {
                Player = GetComponent<MediaPlayerPull>();
            }

            if (Player == null)
            {
                return;
            }

            var overrideUri = TryReadStringArgument(UriArgumentName);
            if (!string.IsNullOrEmpty(overrideUri))
            {
                Player.Uri = overrideUri;
                Debug.Log("[CodexValidation] override uri=" + overrideUri);
            }

            var overrideBackend = TryReadStringArgument(BackendArgumentName);
            MediaBackendKind parsedBackend;
            if (TryParseBackend(overrideBackend, out parsedBackend))
            {
                Player.PreferredBackend = parsedBackend;
                Player.StrictBackend = parsedBackend != MediaBackendKind.Auto;
                Debug.Log(
                    "[CodexValidation] override backend=" + parsedBackend
                    + " strict=" + Player.StrictBackend);
            }

            var overrideVideoRenderer = TryReadStringArgument(VideoRendererArgumentName);
            MediaPlayerPull.PullVideoRendererKind parsedVideoRenderer;
            if (TryParseVideoRenderer(overrideVideoRenderer, out parsedVideoRenderer))
            {
                Player.VideoRenderer = parsedVideoRenderer;
                Debug.Log("[CodexValidation] override video_renderer=" + parsedVideoRenderer);
            }

            bool hasExplicitLoopValue;
            Player.Loop = TryReadBoolArgument(LoopArgumentName, Player.Loop, out hasExplicitLoopValue);
            if (hasExplicitLoopValue)
            {
                Debug.Log("[CodexValidation] override loop=" + Player.Loop);
            }

            ValidationSeconds = TryReadFloatArgument(
                ValidationSecondsArgumentName,
                ValidationSeconds);
            StartupTimeoutSeconds = TryReadFloatArgument(
                StartupTimeoutSecondsArgumentName,
                StartupTimeoutSeconds);
            _publisherStartUnixMs = TryReadLongArgument(
                PublisherStartUnixMsArgumentName,
                -1L,
                out _hasPublisherStartUnixMs);

            _requestedWindowWidth = TryReadIntArgument(WindowWidthArgumentName, Player.Width, out _hasExplicitWindowWidth);
            _requestedWindowHeight = TryReadIntArgument(WindowHeightArgumentName, Player.Height, out _hasExplicitWindowHeight);

            if (_requestedWindowWidth > 0)
            {
                Player.Width = _requestedWindowWidth;
            }

            if (_requestedWindowHeight > 0)
            {
                Player.Height = _requestedWindowHeight;
            }
        }

        private void Start()
        {
            if (Player == null)
            {
                Player = GetComponent<MediaPlayerPull>();
            }

            if (Player == null)
            {
                Debug.LogError("[CodexValidation] missing MediaPlayerPull");
                StartCoroutine(QuitAfterDelay(1f, 2));
                return;
            }

            // 场景验证需要在无人值守运行时维持稳定时钟，避免失焦后被系统节流。
            Application.runInBackground = true;
            Debug.Log("[CodexValidation] runInBackground=True");
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = PreviewTargetFrameRate;
            Debug.Log(
                "[CodexValidation] frame_pacing targetFrameRate="
                + Application.targetFrameRate
                + " vSyncCount=" + QualitySettings.vSyncCount);

            _lastLogTime = Time.realtimeSinceStartup;
            _startTime = _lastLogTime;
            Debug.Log(
                string.Format(
                    "[CodexValidation] start validation seconds={0:F1} requestedWindow={1}x{2} explicitWindow={3} video_renderer={4}",
                    ValidationSeconds,
                    Player.Width,
                    Player.Height,
                    HasExplicitWindowOverride(),
                    Player.VideoRenderer));

            if (HasExplicitWindowOverride())
            {
                ConfigureWindow(Player.Width, Player.Height, "explicit-override");
                _windowConfigured = true;
            }
            StartCoroutine(RunValidation());
        }

        private IEnumerator RunValidation()
        {
            var startTime = Time.realtimeSinceStartup;
            while (true)
            {
                var now = Time.realtimeSinceStartup;
                var snapshot = CaptureSnapshot();
                if (!_validationWindowStarted)
                {
                    var startupElapsed = now - startTime;
                    var outputsReady = snapshot.HasTexture
                        && (!Player.EnableAudio || snapshot.AudioPlaying);
                    if (outputsReady)
                    {
                        StartValidationWindow(
                            now,
                            startupElapsed,
                            "av-output-start",
                            snapshot.PlaybackTime);
                    }
                    else if (startupElapsed >= StartupTimeoutSeconds)
                    {
                        StartValidationWindow(
                            now,
                            startupElapsed,
                            "startup-timeout",
                            snapshot.PlaybackTime);
                    }
                }
                else
                {
                    RecordValidationObservation(snapshot);
                }

                if (_validationWindowStarted
                    && now - _validationWindowStartTime >= ValidationSeconds)
                {
                    break;
                }

                if (Time.realtimeSinceStartup - _lastLogTime >= LogIntervalSeconds)
                {
                    EmitStatus();
                    _lastLogTime = Time.realtimeSinceStartup;
                }

                yield return null;
            }

            var finalSnapshot = EmitStatus();
            var validationPassed = EvaluateValidationResult(finalSnapshot);
            var exitCode = validationPassed ? 0 : 2;
            yield return QuitAfterDelay(0.5f, exitCode);
        }

        private ValidationSnapshot EmitStatus()
        {
            var snapshot = CaptureSnapshot();

            Debug.Log(string.Format(
                "[CodexValidation] time={0:F3}s texture={1} audioPlaying={2} started={3} startupElapsed={4:F3}s sourceState={5} sourcePackets={6} sourceTimeouts={7} sourceReconnects={8} window={9}x{10} textureSize={11}x{12} fullscreen={13} mode={14} backend={15} requested_renderer={16} actual_renderer={17} frame_contract_available={18} frame_contract_memory={19} frame_contract_dynamic_range={20} frame_contract_nominal_fps={21:F2} playback_contract_available={22} playback_contract_master_sec={23:F3} av_sync_contract_available={24} av_sync_contract_master={25} av_sync_contract_drift_ms={26:F1} bridge_descriptor_available={27} bridge_descriptor_state={28} bridge_descriptor_runtime={29} bridge_descriptor_zero_copy={30} bridge_descriptor_direct_bindable={31} bridge_descriptor_source_plane_textures={32} bridge_descriptor_fallback_copy={33} path_selection_available={34} path_selection_kind={35} path_selection_source_memory={36} path_selection_presented_memory={37} path_selection_target_zero_copy={38} path_selection_source_plane_textures={39} path_selection_cpu_fallback={40}",
                snapshot.PlaybackTime,
                snapshot.HasTexture,
                snapshot.AudioPlaying,
                snapshot.Started,
                Player.StartupElapsedSeconds,
                snapshot.SourceState,
                snapshot.SourcePackets,
                snapshot.SourceTimeouts,
                snapshot.SourceReconnects,
                Screen.width,
                Screen.height,
                snapshot.TextureWidth,
                snapshot.TextureHeight,
                Screen.fullScreen,
                Screen.fullScreenMode,
                Player.ActualBackendKind,
                Player.VideoRenderer,
                Player.ActualVideoRenderer,
                snapshot.HasFrameContract,
                snapshot.FrameContractMemoryKind,
                snapshot.FrameContractDynamicRange,
                snapshot.FrameContractNominalFps,
                snapshot.HasPlaybackTimingContract,
                snapshot.PlaybackContractMasterTimeSec,
                snapshot.HasAvSyncContract,
                snapshot.AvSyncContractMasterClock,
                snapshot.AvSyncContractDriftMs,
                snapshot.HasBridgeDescriptor,
                snapshot.BridgeDescriptorState,
                snapshot.BridgeDescriptorRuntimeKind,
                snapshot.BridgeDescriptorZeroCopySupported,
                snapshot.BridgeDescriptorDirectBindable,
                snapshot.BridgeDescriptorSourcePlaneTexturesSupported,
                snapshot.BridgeDescriptorFallbackCopyPath,
                snapshot.HasPathSelection,
                snapshot.PathSelectionKind,
                snapshot.PathSelectionSourceMemoryKind,
                snapshot.PathSelectionPresentedMemoryKind,
                snapshot.PathSelectionTargetZeroCopy,
                snapshot.PathSelectionSourcePlaneTexturesSupported,
                snapshot.PathSelectionCpuFallback));
            if (snapshot.HasWgpuRenderDescriptor)
            {
                Debug.Log(string.Format(
                    "[CodexValidation] wgpu_descriptor runtime_ready={0} output_width={1} output_height={2} supports_yuv420p={3} supports_nv12={4} supports_p010={5} supports_rgba32={6} supports_external_texture_rgba={7} supports_external_texture_yu12={8} readback_export_supported={9}",
                    snapshot.WgpuRuntimeReady,
                    snapshot.WgpuOutputWidth,
                    snapshot.WgpuOutputHeight,
                    snapshot.WgpuSupportsYuv420p,
                    snapshot.WgpuSupportsNv12,
                    snapshot.WgpuSupportsP010,
                    snapshot.WgpuSupportsRgba32,
                    snapshot.WgpuSupportsExternalTextureRgba,
                    snapshot.WgpuSupportsExternalTextureYu12,
                    snapshot.WgpuReadbackExportSupported));
            }
            if (snapshot.HasWgpuRenderState)
            {
                Debug.Log(string.Format(
                    "[CodexValidation] wgpu_state render_path={0} source_memory={1} presented_memory={2} source_pixel_format={3} presented_pixel_format={4} external_texture_format={5} has_rendered_frame={6} rendered_frame_index={7} rendered_time_sec={8:F3} has_render_error={9} render_error_kind={10} upload_plane_count={11} source_zero_copy={12} cpu_fallback={13}",
                    snapshot.WgpuRenderPath,
                    snapshot.WgpuSourceMemoryKind,
                    snapshot.WgpuPresentedMemoryKind,
                    snapshot.WgpuSourcePixelFormat,
                    snapshot.WgpuPresentedPixelFormat,
                    snapshot.WgpuExternalTextureFormat,
                    snapshot.WgpuHasRenderedFrame,
                    snapshot.WgpuRenderedFrameIndex,
                    snapshot.WgpuRenderedTimeSec,
                    snapshot.WgpuHasRenderError,
                    snapshot.WgpuRenderErrorKind,
                    snapshot.WgpuUploadPlaneCount,
                    snapshot.WgpuSourceZeroCopy,
                    snapshot.WgpuCpuFallback));
            }
            if (snapshot.HasPlaybackTimingContract)
            {
                Debug.Log(string.Format(
                    "[CodexValidation] playback_contract master_sec={0:F3} external_sec={1:F3} has_audio_time_sec={2} audio_time_sec={3:F3} has_audio_presented_time_sec={4} audio_presented_time_sec={5:F3} audio_sink_delay_ms={6:F1} has_audio_clock={7}",
                    snapshot.PlaybackContractMasterTimeSec,
                    snapshot.PlaybackContractExternalTimeSec,
                    snapshot.PlaybackContractHasAudioTimeSec,
                    snapshot.PlaybackContractAudioTimeSec,
                    snapshot.PlaybackContractHasAudioPresentedTimeSec,
                    snapshot.PlaybackContractAudioPresentedTimeSec,
                    snapshot.PlaybackContractAudioSinkDelaySec * 1000.0,
                    snapshot.PlaybackContractHasAudioClock));
            }
            if (snapshot.HasAvSyncContract)
            {
                Debug.Log(string.Format(
                    "[CodexValidation] av_sync_contract master={0} has_audio_clock_sec={1} audio_clock_sec={2:F3} has_video_clock_sec={3} video_clock_sec={4:F3} clock_delta_ms={5:F1} drift_ms={6:F1} warmup_complete={7} drop_total={8} duplicate_total={9}",
                    snapshot.AvSyncContractMasterClock,
                    snapshot.AvSyncContractHasAudioClockSec,
                    snapshot.AvSyncContractAudioClockSec,
                    snapshot.AvSyncContractHasVideoClockSec,
                    snapshot.AvSyncContractVideoClockSec,
                    snapshot.AvSyncContractClockDeltaMs,
                    snapshot.AvSyncContractDriftMs,
                    snapshot.AvSyncContractStartupWarmupComplete,
                    snapshot.AvSyncContractDropTotal,
                    snapshot.AvSyncContractDuplicateTotal));
                if (snapshot.AvSyncContractHasAudioClockSec
                    && snapshot.AvSyncContractHasVideoClockSec)
                {
                    Debug.Log(string.Format(
                        "[CodexValidation] av_sync_contract_sample delta_ms={0:F1} audio_clock_sec={1:F3} video_clock_sec={2:F3}",
                        snapshot.AvSyncContractClockDeltaMs,
                        snapshot.AvSyncContractAudioClockSec,
                        snapshot.AvSyncContractVideoClockSec));
                }
            }
            if (snapshot.HasAvSyncSample)
            {
                Debug.Log(string.Format(
                    "[CodexValidation] av_sync delta_ms={0:F1} audio_presented_sec={1:F3} reference_sec={2:F3} reference_kind={3} playback_sec={4:F3} presented_video_sec={5:F3} contract_audio_time_sec={6:F3} contract_audio_presented_sec={7:F3} contract_audio_sink_delay_ms={8:F1} audio_pipeline_delay_ms={9:F1}",
                    snapshot.AvSyncDeltaMilliseconds,
                    snapshot.AudioPresentedTimeSec,
                    snapshot.ReferencePlaybackTimeSec,
                    snapshot.ReferencePlaybackKind,
                    snapshot.PlaybackTime,
                    snapshot.PresentedVideoTimeSec,
                    snapshot.PlaybackContractAudioTimeSec,
                    snapshot.PlaybackContractAudioPresentedTimeSec,
                    snapshot.PlaybackContractAudioSinkDelaySec * 1000.0,
                    snapshot.AudioPipelineDelaySec * 1000.0));
            }
            if (snapshot.HasRealtimeLatencySample)
            {
                Debug.Log(string.Format(
                    "[CodexValidation] realtime_latency latency_ms={0:F1} publisher_elapsed_sec={1:F3} reference_sec={2:F3}",
                    snapshot.RealtimeLatencyMilliseconds,
                    snapshot.PublisherElapsedTimeSec,
                    snapshot.RealtimeReferenceTimeSec));
            }
            if (snapshot.HasRealtimeProbeSample)
            {
                Debug.Log(string.Format(
                    "[CodexValidation] realtime_probe unix_ms={0} reference_sec={1:F3}",
                    snapshot.RealtimeProbeUnixMs,
                    snapshot.RealtimeReferenceTimeSec));
            }

            return snapshot;
        }

        private ValidationSnapshot CaptureSnapshot()
        {
            var playbackTime = SafeReadPlaybackTime();
            var hasTexture = Player.HasPresentedVideoFrame
                && Player.TargetMaterial != null
                && Player.TargetMaterial.mainTexture != null;
            var audioSource = Player.GetComponent<AudioSource>();
            var audioPlaying = audioSource != null && audioSource.isPlaying;
            var textureWidth = hasTexture ? Player.TargetMaterial.mainTexture.width : 0;
            var textureHeight = hasTexture ? Player.TargetMaterial.mainTexture.height : 0;
            double audioPresentedTimeSec;
            double audioPipelineDelaySec;
            var hasAudioPresentation = Player.TryGetEstimatedAudioPresentation(
                out audioPresentedTimeSec,
                out audioPipelineDelaySec);

            MediaPlayerPull.PlayerRuntimeHealth health;
            var hasHealth = Player.TryGetRuntimeHealth(out health);
            double presentedVideoTimeSec;
            var hasPresentedVideoTime = Player.TryGetPresentedVideoTimeSec(out presentedVideoTimeSec);
            var referencePlaybackTime = hasPresentedVideoTime
                ? presentedVideoTimeSec
                : playbackTime;
            if (hasHealth)
            {
                if (referencePlaybackTime < 0.0)
                {
                    referencePlaybackTime = health.CurrentTimeSec;
                }
                else if (health.IsRealtime
                    && health.CurrentTimeSec > referencePlaybackTime + RealtimeReferenceLagToleranceSeconds)
                {
                    referencePlaybackTime = health.CurrentTimeSec;
                }
            }

            var referencePlaybackKind = hasPresentedVideoTime
                ? "presented_video"
                : "playback_time";
            if (hasHealth)
            {
                if (playbackTime < 0.0 && referencePlaybackTime >= 0.0)
                {
                    referencePlaybackKind = "health_current_time";
                }
                else if (health.IsRealtime
                    && playbackTime >= 0.0
                    && health.CurrentTimeSec > playbackTime + RealtimeReferenceLagToleranceSeconds)
                {
                    referencePlaybackKind = "health_current_time";
                }
            }

            var hasAvSyncSample = hasAudioPresentation && referencePlaybackTime >= 0.0;
            var avSyncDeltaMilliseconds = hasAvSyncSample
                ? (audioPresentedTimeSec - referencePlaybackTime) * 1000.0
                : 0.0;
            MediaNativeInteropCommon.VideoFrameContractView frameContract;
            var hasFrameContract = Player.TryGetLatestVideoFrameContract(out frameContract);
            MediaNativeInteropCommon.PlaybackTimingContractView playbackTimingContract;
            var hasPlaybackTimingContract = Player.TryGetPlaybackTimingContract(
                out playbackTimingContract);
            MediaNativeInteropCommon.AvSyncContractView avSyncContract;
            var hasAvSyncContract = Player.TryGetAvSyncContract(out avSyncContract);
            var hasAvSyncContractAudioClockSec = hasAvSyncContract && avSyncContract.HasAudioClockSec;
            var avSyncContractAudioClockSec = hasAvSyncContractAudioClockSec
                ? avSyncContract.AudioClockSec
                : 0.0;
            var hasAvSyncContractVideoClockSec = hasAvSyncContract && avSyncContract.HasVideoClockSec;
            var avSyncContractVideoClockSec = hasAvSyncContractVideoClockSec
                ? avSyncContract.VideoClockSec
                : 0.0;
            var avSyncContractClockDeltaMs = hasAvSyncContractAudioClockSec && hasAvSyncContractVideoClockSec
                ? (avSyncContractAudioClockSec - avSyncContractVideoClockSec) * 1000.0
                : 0.0;
            MediaNativeInteropCommon.NativeVideoBridgeDescriptorView bridgeDescriptor;
            var hasBridgeDescriptor = Player.TryGetNativeVideoBridgeDescriptor(out bridgeDescriptor);
            MediaNativeInteropCommon.NativeVideoPathSelectionView pathSelection;
            var hasPathSelection = Player.TryGetNativeVideoPathSelection(out pathSelection);
            MediaNativeInteropCommon.WgpuRenderDescriptorView wgpuDescriptor;
            var hasWgpuRenderDescriptor = Player.TryGetWgpuRenderDescriptor(out wgpuDescriptor);
            MediaNativeInteropCommon.WgpuRenderStateView wgpuState;
            var hasWgpuRenderState = Player.TryGetWgpuRenderStateView(out wgpuState);
            var hasRealtimeLatencySample = false;
            var realtimeLatencyMilliseconds = 0.0;
            var publisherElapsedTimeSec = 0.0;
            var hasRealtimeProbeSample = false;
            long realtimeProbeUnixMs = 0;
            if (hasHealth
                && health.IsRealtime
                && referencePlaybackTime >= 0.0)
            {
                realtimeProbeUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                hasRealtimeProbeSample = true;
            }
            if (_hasPublisherStartUnixMs
                && hasHealth
                && health.IsRealtime
                && referencePlaybackTime >= 0.0)
            {
                var nowUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                if (nowUnixMs >= _publisherStartUnixMs)
                {
                    publisherElapsedTimeSec =
                        (nowUnixMs - _publisherStartUnixMs) / 1000.0;
                    realtimeLatencyMilliseconds =
                        (publisherElapsedTimeSec - referencePlaybackTime) * 1000.0;
                    hasRealtimeLatencySample = true;
                }
            }

            return new ValidationSnapshot
            {
                PlaybackTime = playbackTime,
                HasTexture = hasTexture,
                AudioPlaying = audioPlaying,
                Started = Player.HasStartedPlayback,
                TextureWidth = textureWidth,
                TextureHeight = textureHeight,
                SourceState = hasHealth ? health.SourceConnectionState.ToString() : "Unavailable",
                SourcePackets = hasHealth ? health.SourcePacketCount.ToString() : "-1",
                SourceTimeouts = hasHealth ? health.SourceTimeoutCount.ToString() : "-1",
                SourceReconnects = hasHealth ? health.SourceReconnectCount.ToString() : "-1",
                HasAvSyncSample = hasAvSyncSample,
                AudioPresentedTimeSec = audioPresentedTimeSec,
                AudioPipelineDelaySec = audioPipelineDelaySec,
                AvSyncDeltaMilliseconds = avSyncDeltaMilliseconds,
                HasPresentedVideoTime = hasPresentedVideoTime,
                PresentedVideoTimeSec = hasPresentedVideoTime ? presentedVideoTimeSec : -1.0,
                ReferencePlaybackTimeSec = referencePlaybackTime,
                ReferencePlaybackKind = referencePlaybackKind,
                HasFrameContract = hasFrameContract,
                FrameContractMemoryKind = hasFrameContract ? frameContract.MemoryKind.ToString() : "Unavailable",
                FrameContractDynamicRange =
                    hasFrameContract ? frameContract.Color.DynamicRange.ToString() : "Unavailable",
                FrameContractNominalFps =
                    hasFrameContract && frameContract.HasNominalFps ? frameContract.NominalFps : 0.0,
                HasPlaybackTimingContract = hasPlaybackTimingContract,
                PlaybackContractMasterTimeSec =
                    hasPlaybackTimingContract ? playbackTimingContract.MasterTimeSec : 0.0,
                PlaybackContractExternalTimeSec =
                    hasPlaybackTimingContract ? playbackTimingContract.ExternalTimeSec : 0.0,
                PlaybackContractHasAudioTimeSec =
                    hasPlaybackTimingContract && playbackTimingContract.HasAudioTimeSec,
                PlaybackContractAudioTimeSec =
                    hasPlaybackTimingContract && playbackTimingContract.HasAudioTimeSec
                        ? playbackTimingContract.AudioTimeSec
                        : 0.0,
                PlaybackContractHasAudioPresentedTimeSec =
                    hasPlaybackTimingContract && playbackTimingContract.HasAudioPresentedTimeSec,
                PlaybackContractAudioPresentedTimeSec =
                    hasPlaybackTimingContract && playbackTimingContract.HasAudioPresentedTimeSec
                        ? playbackTimingContract.AudioPresentedTimeSec
                        : 0.0,
                PlaybackContractAudioSinkDelaySec =
                    hasPlaybackTimingContract ? playbackTimingContract.AudioSinkDelaySec : 0.0,
                PlaybackContractHasAudioClock =
                    hasPlaybackTimingContract && playbackTimingContract.HasAudioClock,
                HasAvSyncContract = hasAvSyncContract,
                AvSyncContractMasterClock =
                    hasAvSyncContract ? avSyncContract.MasterClock.ToString() : "Unavailable",
                AvSyncContractHasAudioClockSec = hasAvSyncContractAudioClockSec,
                AvSyncContractAudioClockSec = avSyncContractAudioClockSec,
                AvSyncContractHasVideoClockSec = hasAvSyncContractVideoClockSec,
                AvSyncContractVideoClockSec = avSyncContractVideoClockSec,
                AvSyncContractClockDeltaMs = avSyncContractClockDeltaMs,
                AvSyncContractDriftMs = hasAvSyncContract ? avSyncContract.DriftMs : 0.0,
                AvSyncContractStartupWarmupComplete =
                    hasAvSyncContract && avSyncContract.StartupWarmupComplete,
                AvSyncContractDropTotal = hasAvSyncContract ? avSyncContract.DropTotal : 0UL,
                AvSyncContractDuplicateTotal = hasAvSyncContract ? avSyncContract.DuplicateTotal : 0UL,
                HasBridgeDescriptor = hasBridgeDescriptor,
                BridgeDescriptorState =
                    hasBridgeDescriptor ? bridgeDescriptor.State.ToString() : "Unavailable",
                BridgeDescriptorRuntimeKind =
                    hasBridgeDescriptor ? bridgeDescriptor.RuntimeKind.ToString() : "Unavailable",
                BridgeDescriptorZeroCopySupported =
                    hasBridgeDescriptor && bridgeDescriptor.ZeroCopySupported,
                BridgeDescriptorDirectBindable =
                    hasBridgeDescriptor && bridgeDescriptor.PresentedFrameDirectBindable,
                BridgeDescriptorSourcePlaneTexturesSupported =
                    hasBridgeDescriptor && bridgeDescriptor.SourcePlaneTexturesSupported,
                BridgeDescriptorFallbackCopyPath =
                    hasBridgeDescriptor && bridgeDescriptor.FallbackCopyPath,
                HasPathSelection = hasPathSelection,
                PathSelectionKind = hasPathSelection ? pathSelection.Kind.ToString() : "Unavailable",
                PathSelectionSourceMemoryKind =
                    hasPathSelection ? pathSelection.SourceMemoryKind.ToString() : "Unavailable",
                PathSelectionPresentedMemoryKind =
                    hasPathSelection ? pathSelection.PresentedMemoryKind.ToString() : "Unavailable",
                PathSelectionTargetZeroCopy = hasPathSelection && pathSelection.TargetZeroCopy,
                PathSelectionSourcePlaneTexturesSupported =
                    hasPathSelection && pathSelection.SourcePlaneTexturesSupported,
                PathSelectionCpuFallback = hasPathSelection && pathSelection.CpuFallback,
                HasWgpuRenderDescriptor = hasWgpuRenderDescriptor,
                WgpuRuntimeReady = hasWgpuRenderDescriptor && wgpuDescriptor.RuntimeReady,
                WgpuOutputWidth = hasWgpuRenderDescriptor ? wgpuDescriptor.OutputWidth : 0,
                WgpuOutputHeight = hasWgpuRenderDescriptor ? wgpuDescriptor.OutputHeight : 0,
                WgpuSupportsYuv420p = hasWgpuRenderDescriptor && wgpuDescriptor.SupportsYuv420p,
                WgpuSupportsNv12 = hasWgpuRenderDescriptor && wgpuDescriptor.SupportsNv12,
                WgpuSupportsP010 = hasWgpuRenderDescriptor && wgpuDescriptor.SupportsP010,
                WgpuSupportsRgba32 = hasWgpuRenderDescriptor && wgpuDescriptor.SupportsRgba32,
                WgpuSupportsExternalTextureRgba =
                    hasWgpuRenderDescriptor && wgpuDescriptor.SupportsExternalTextureRgba,
                WgpuSupportsExternalTextureYu12 =
                    hasWgpuRenderDescriptor && wgpuDescriptor.SupportsExternalTextureYu12,
                WgpuReadbackExportSupported =
                    hasWgpuRenderDescriptor && wgpuDescriptor.ReadbackExportSupported,
                HasWgpuRenderState = hasWgpuRenderState,
                WgpuRenderPath = hasWgpuRenderState ? wgpuState.RenderPath.ToString() : "Unavailable",
                WgpuSourceMemoryKind =
                    hasWgpuRenderState ? wgpuState.SourceMemoryKind.ToString() : "Unavailable",
                WgpuPresentedMemoryKind =
                    hasWgpuRenderState ? wgpuState.PresentedMemoryKind.ToString() : "Unavailable",
                WgpuSourcePixelFormat =
                    hasWgpuRenderState ? wgpuState.SourcePixelFormat.ToString() : "Unavailable",
                WgpuPresentedPixelFormat =
                    hasWgpuRenderState ? wgpuState.PresentedPixelFormat.ToString() : "Unavailable",
                WgpuExternalTextureFormat =
                    hasWgpuRenderState ? wgpuState.ExternalTextureFormat.ToString() : "Unavailable",
                WgpuHasRenderedFrame = hasWgpuRenderState && wgpuState.HasRenderedFrame,
                WgpuRenderedFrameIndex = hasWgpuRenderState ? wgpuState.RenderedFrameIndex : 0,
                WgpuRenderedTimeSec = hasWgpuRenderState ? wgpuState.RenderedTimeSec : 0.0,
                WgpuHasRenderError = hasWgpuRenderState && wgpuState.HasRenderError,
                WgpuRenderErrorKind =
                    hasWgpuRenderState ? wgpuState.RenderErrorKind.ToString() : "Unavailable",
                WgpuUploadPlaneCount = hasWgpuRenderState ? wgpuState.UploadPlaneCount : 0,
                WgpuSourceZeroCopy = hasWgpuRenderState && wgpuState.SourceZeroCopy,
                WgpuCpuFallback = hasWgpuRenderState && wgpuState.CpuFallback,
                HasRealtimeLatencySample = hasRealtimeLatencySample,
                RealtimeLatencyMilliseconds = realtimeLatencyMilliseconds,
                PublisherElapsedTimeSec = publisherElapsedTimeSec,
                RealtimeReferenceTimeSec = referencePlaybackTime,
                HasRealtimeProbeSample = hasRealtimeProbeSample,
                RealtimeProbeUnixMs = realtimeProbeUnixMs,
            };
        }

        private void StartValidationWindow(
            float now,
            float startupElapsed,
            string reason,
            double playbackTime)
        {
            _validationWindowStarted = true;
            _validationWindowStartTime = now;
            _validationWindowStartReason = reason;
            _validationWindowInitialPlaybackTime = playbackTime;
            _maxObservedPlaybackTime = playbackTime;
            Debug.Log(
                string.Format(
                    "[CodexValidation] validation_window_started reason={0} startup_elapsed={1:F3}s",
                    reason,
                    startupElapsed));
        }

        private void RecordValidationObservation(ValidationSnapshot snapshot)
        {
            _observedTextureDuringWindow |= snapshot.HasTexture;
            _observedAudioDuringWindow |= snapshot.AudioPlaying;
            _observedStartedDuringWindow |= snapshot.Started;
            if (snapshot.PlaybackTime > _maxObservedPlaybackTime)
            {
                _maxObservedPlaybackTime = snapshot.PlaybackTime;
            }
        }

        private bool EvaluateValidationResult(ValidationSnapshot finalSnapshot)
        {
            RecordValidationObservation(finalSnapshot);

            var playbackAdvance = 0.0;
            if (_maxObservedPlaybackTime >= 0.0 && _validationWindowInitialPlaybackTime >= 0.0)
            {
                playbackAdvance = _maxObservedPlaybackTime - _validationWindowInitialPlaybackTime;
            }

            if (_validationWindowStartReason == "startup-timeout"
                && !_observedStartedDuringWindow)
            {
                Debug.LogError(
                    "[CodexValidation] result=failed reason=startup-timeout-no-playback");
                return false;
            }

            if (!_observedStartedDuringWindow)
            {
                Debug.LogError(
                    "[CodexValidation] result=failed reason=playback-not-started");
                return false;
            }

            if (!_observedTextureDuringWindow)
            {
                Debug.LogError(
                    "[CodexValidation] result=failed reason=missing-video-frame");
                return false;
            }

            if (!_observedAudioDuringWindow)
            {
                Debug.LogError(
                    "[CodexValidation] result=failed reason=audio-not-playing");
                return false;
            }

            if (playbackAdvance < MinimumPlaybackAdvanceSeconds)
            {
                Debug.LogError(
                    string.Format(
                        "[CodexValidation] result=failed reason=playback-stalled advance={0:F3}s",
                        playbackAdvance));
                return false;
            }

            Debug.Log(
                string.Format(
                    "[CodexValidation] result=passed reason=steady-playback advance={0:F3}s sourceState={1} sourceTimeouts={2} sourceReconnects={3}",
                    playbackAdvance,
                    finalSnapshot.SourceState,
                    finalSnapshot.SourceTimeouts,
                    finalSnapshot.SourceReconnects));
            Debug.Log("[CodexValidation] complete");
            return true;
        }

        private double SafeReadPlaybackTime()
        {
            try
            {
                return Player.Time();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[CodexValidation] time read failed: " + ex.Message);
                return -1.0;
            }
        }

        private void Update()
        {
            TryConfigureWindow();
        }

        private void TryConfigureWindow()
        {
            if (Player == null)
            {
                return;
            }

            int width;
            int height;

            if (Player.TryGetPrimaryVideoSize(out width, out height))
            {
                ConfigureView(width, height);

                if (HasExplicitWindowOverride())
                {
                    _sourceSizedWindowApplied = true;
                    return;
                }

                if (!_sourceSizedWindowApplied || Screen.width != width || Screen.height != height)
                {
                    ConfigureWindow(width, height, "source");
                    ConfigureView(width, height);
                    _windowConfigured = true;
                    _sourceSizedWindowApplied = true;
                }
                return;
            }

            if (_windowConfigured || Player.TargetMaterial == null || HasExplicitWindowOverride())
            {
                return;
            }

            var texture = Player.TargetMaterial.mainTexture;
            if (texture == null || texture.width <= 0 || texture.height <= 0)
            {
                return;
            }

            if (Time.realtimeSinceStartup - _startTime < 1.0f)
            {
                return;
            }

            ConfigureWindow(texture.width, texture.height, "texture-fallback");
            ConfigureView(texture.width, texture.height);
            _windowConfigured = true;
        }

        private bool HasExplicitWindowOverride()
        {
            return _hasExplicitWindowWidth || _hasExplicitWindowHeight;
        }

        private void ConfigureWindow(int width, int height, string source)
        {
            if (width <= 0 || height <= 0)
            {
                return;
            }

            if (ForceWindowedMode)
            {
                Screen.fullScreenMode = FullScreenMode.Windowed;
                Screen.fullScreen = false;
            }

            Screen.SetResolution(width, height, false);
            Debug.Log(
                string.Format(
                    "[CodexValidation] window_configured={0}x{1} reason={2} fullscreen={3} mode={4}",
                    width,
                    height,
                    source,
                    Screen.fullScreen,
                    Screen.fullScreenMode));
        }

        private void ConfigureView(int width, int height)
        {
            if (width <= 0 || height <= 0)
            {
                return;
            }

            var aspect = (float)width / height;

            if (VideoSurface != null)
            {
                VideoSurface.localScale = new Vector3(aspect, 1f, 1f);
            }

            if (ValidationCamera != null)
            {
                ValidationCamera.orthographic = true;
                ValidationCamera.orthographicSize = 0.5f;
            }
        }

        private static string TryReadStringArgument(string prefix)
        {
            var args = Environment.GetCommandLineArgs();
            if (args == null)
            {
                return string.Empty;
            }

            foreach (var arg in args)
            {
                if (arg != null && arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return arg.Substring(prefix.Length);
                }
            }

            return string.Empty;
        }

        private static int TryReadIntArgument(string prefix, int fallback, out bool hasExplicitValue)
        {
            var value = TryReadStringArgument(prefix);
            hasExplicitValue = !string.IsNullOrEmpty(value);
            int parsed;
            if (string.IsNullOrEmpty(value) || !int.TryParse(value, out parsed) || parsed <= 0)
            {
                return fallback;
            }

            return parsed;
        }

        private static float TryReadFloatArgument(string prefix, float fallback)
        {
            var value = TryReadStringArgument(prefix);
            float parsed;
            if (string.IsNullOrEmpty(value)
                || !float.TryParse(value, out parsed)
                || parsed <= 0f)
            {
                return fallback;
            }

            return parsed;
        }

        private static bool TryReadBoolArgument(string prefix, bool fallback, out bool hasExplicitValue)
        {
            var value = TryReadStringArgument(prefix);
            hasExplicitValue = !string.IsNullOrEmpty(value);
            bool parsed;
            if (string.IsNullOrEmpty(value) || !bool.TryParse(value, out parsed))
            {
                return fallback;
            }

            return parsed;
        }

        private static long TryReadLongArgument(string prefix, long fallback, out bool hasExplicitValue)
        {
            var value = TryReadStringArgument(prefix);
            hasExplicitValue = !string.IsNullOrEmpty(value);
            long parsed;
            if (string.IsNullOrEmpty(value) || !long.TryParse(value, out parsed))
            {
                return fallback;
            }

            return parsed;
        }

        private static bool TryParseBackend(string rawValue, out MediaBackendKind backend)
        {
            backend = MediaBackendKind.Auto;
            if (string.IsNullOrEmpty(rawValue))
            {
                return false;
            }

            switch (rawValue.Trim().ToLowerInvariant())
            {
                case "auto":
                    backend = MediaBackendKind.Auto;
                    return true;
                case "ffmpeg":
                    backend = MediaBackendKind.Ffmpeg;
                    return true;
                case "gstreamer":
                    backend = MediaBackendKind.Gstreamer;
                    return true;
                default:
                    Debug.LogWarning("[CodexValidation] ignore unknown backend=" + rawValue);
                    return false;
            }
        }

        private static bool TryParseVideoRenderer(
            string rawValue,
            out MediaPlayerPull.PullVideoRendererKind renderer)
        {
            renderer = MediaPlayerPull.PullVideoRendererKind.Cpu;
            if (string.IsNullOrEmpty(rawValue))
            {
                return false;
            }

            switch (rawValue.Trim().ToLowerInvariant())
            {
                case "cpu":
                    renderer = MediaPlayerPull.PullVideoRendererKind.Cpu;
                    return true;
                case "wgpu":
                    renderer = MediaPlayerPull.PullVideoRendererKind.Wgpu;
                    return true;
                default:
                    Debug.LogWarning("[CodexValidation] ignore unknown video_renderer=" + rawValue);
                    return false;
            }
        }

        private IEnumerator QuitAfterDelay(float seconds, int exitCode)
        {
            yield return new WaitForSeconds(seconds);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
            Environment.Exit(exitCode);
#endif
        }

        private struct ValidationSnapshot
        {
            public double PlaybackTime;
            public bool HasTexture;
            public bool AudioPlaying;
            public bool Started;
            public int TextureWidth;
            public int TextureHeight;
            public string SourceState;
            public string SourcePackets;
            public string SourceTimeouts;
            public string SourceReconnects;
            public bool HasAvSyncSample;
            public double AudioPresentedTimeSec;
            public double AudioPipelineDelaySec;
            public double AvSyncDeltaMilliseconds;
            public bool HasPresentedVideoTime;
            public double PresentedVideoTimeSec;
            public double ReferencePlaybackTimeSec;
            public string ReferencePlaybackKind;
            public bool HasFrameContract;
            public string FrameContractMemoryKind;
            public string FrameContractDynamicRange;
            public double FrameContractNominalFps;
            public bool HasPlaybackTimingContract;
            public double PlaybackContractMasterTimeSec;
            public double PlaybackContractExternalTimeSec;
            public bool PlaybackContractHasAudioTimeSec;
            public double PlaybackContractAudioTimeSec;
            public bool PlaybackContractHasAudioPresentedTimeSec;
            public double PlaybackContractAudioPresentedTimeSec;
            public double PlaybackContractAudioSinkDelaySec;
            public bool PlaybackContractHasAudioClock;
            public bool HasAvSyncContract;
            public string AvSyncContractMasterClock;
            public bool AvSyncContractHasAudioClockSec;
            public double AvSyncContractAudioClockSec;
            public bool AvSyncContractHasVideoClockSec;
            public double AvSyncContractVideoClockSec;
            public double AvSyncContractClockDeltaMs;
            public double AvSyncContractDriftMs;
            public bool AvSyncContractStartupWarmupComplete;
            public ulong AvSyncContractDropTotal;
            public ulong AvSyncContractDuplicateTotal;
            public bool HasBridgeDescriptor;
            public string BridgeDescriptorState;
            public string BridgeDescriptorRuntimeKind;
            public bool BridgeDescriptorZeroCopySupported;
            public bool BridgeDescriptorDirectBindable;
            public bool BridgeDescriptorSourcePlaneTexturesSupported;
            public bool BridgeDescriptorFallbackCopyPath;
            public bool HasPathSelection;
            public string PathSelectionKind;
            public string PathSelectionSourceMemoryKind;
            public string PathSelectionPresentedMemoryKind;
            public bool PathSelectionTargetZeroCopy;
            public bool PathSelectionSourcePlaneTexturesSupported;
            public bool PathSelectionCpuFallback;
            public bool HasWgpuRenderDescriptor;
            public bool WgpuRuntimeReady;
            public int WgpuOutputWidth;
            public int WgpuOutputHeight;
            public bool WgpuSupportsYuv420p;
            public bool WgpuSupportsNv12;
            public bool WgpuSupportsP010;
            public bool WgpuSupportsRgba32;
            public bool WgpuSupportsExternalTextureRgba;
            public bool WgpuSupportsExternalTextureYu12;
            public bool WgpuReadbackExportSupported;
            public bool HasWgpuRenderState;
            public string WgpuRenderPath;
            public string WgpuSourceMemoryKind;
            public string WgpuPresentedMemoryKind;
            public string WgpuSourcePixelFormat;
            public string WgpuPresentedPixelFormat;
            public string WgpuExternalTextureFormat;
            public bool WgpuHasRenderedFrame;
            public long WgpuRenderedFrameIndex;
            public double WgpuRenderedTimeSec;
            public bool WgpuHasRenderError;
            public string WgpuRenderErrorKind;
            public int WgpuUploadPlaneCount;
            public bool WgpuSourceZeroCopy;
            public bool WgpuCpuFallback;
            public bool HasRealtimeLatencySample;
            public double RealtimeLatencyMilliseconds;
            public double PublisherElapsedTimeSec;
            public double RealtimeReferenceTimeSec;
            public bool HasRealtimeProbeSample;
            public long RealtimeProbeUnixMs;
        }
    }
}
