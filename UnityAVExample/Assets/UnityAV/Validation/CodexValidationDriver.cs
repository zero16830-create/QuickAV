using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
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
        public string RequireAudioOutputArgumentName = "-requireAudioOutput=";
        public string WindowWidthArgumentName = "-windowWidth=";
        public string WindowHeightArgumentName = "-windowHeight=";
        public string PublisherStartUnixMsArgumentName = "-publisherStartUnixMs=";
        public string AndroidUriExtraName = "rustavUri";
        public string AndroidBackendExtraName = "rustavBackend";
        public string AndroidVideoRendererExtraName = "rustavVideoRenderer";
        public string AndroidLoopExtraName = "rustavLoop";
        public string AndroidValidationSecondsExtraName = "rustavValidationSeconds";
        public string AndroidStartupTimeoutSecondsExtraName = "rustavStartupTimeoutSeconds";
        public string AndroidRequireAudioOutputExtraName = "rustavRequireAudioOutput";
        public string AndroidWindowWidthExtraName = "rustavWindowWidth";
        public string AndroidWindowHeightExtraName = "rustavWindowHeight";
        public string AndroidPublisherStartUnixMsExtraName = "rustavPublisherStartUnixMs";
        public string SummaryFileName = "codex-validation-summary.txt";
        public bool RequireAudioOutput = true;
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
        private Renderer _videoSurfaceRenderer;
        private Material _videoSurfaceRuntimeMaterial;

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

            var overrideUri = TryReadOverrideValue(UriArgumentName, AndroidUriExtraName);
            if (!string.IsNullOrEmpty(overrideUri))
            {
                Player.Uri = overrideUri;
                Debug.Log("[CodexValidation] override uri=" + overrideUri);
            }

            var overrideBackend = TryReadOverrideValue(BackendArgumentName, AndroidBackendExtraName);
            MediaBackendKind parsedBackend;
            if (TryParseBackend(overrideBackend, out parsedBackend))
            {
                Player.PreferredBackend = parsedBackend;
                Player.StrictBackend = parsedBackend != MediaBackendKind.Auto;
                Debug.Log(
                    "[CodexValidation] override backend=" + parsedBackend
                    + " strict=" + Player.StrictBackend);
            }

            var overrideVideoRenderer = TryReadOverrideValue(
                VideoRendererArgumentName,
                AndroidVideoRendererExtraName);
            MediaPlayerPull.PullVideoRendererKind parsedVideoRenderer;
            if (TryParseVideoRenderer(overrideVideoRenderer, out parsedVideoRenderer))
            {
                Player.VideoRenderer = parsedVideoRenderer;
                Debug.Log("[CodexValidation] override video_renderer=" + parsedVideoRenderer);
            }

            bool hasExplicitLoopValue;
            Player.Loop = TryReadBoolArgument(
                LoopArgumentName,
                AndroidLoopExtraName,
                Player.Loop,
                out hasExplicitLoopValue);
            if (hasExplicitLoopValue)
            {
                Debug.Log("[CodexValidation] override loop=" + Player.Loop);
            }

            ValidationSeconds = TryReadFloatArgument(
                ValidationSecondsArgumentName,
                AndroidValidationSecondsExtraName,
                ValidationSeconds);
            StartupTimeoutSeconds = TryReadFloatArgument(
                StartupTimeoutSecondsArgumentName,
                AndroidStartupTimeoutSecondsExtraName,
                StartupTimeoutSeconds);
            bool hasExplicitRequireAudioOutput;
            RequireAudioOutput = TryReadBoolArgument(
                RequireAudioOutputArgumentName,
                AndroidRequireAudioOutputExtraName,
                RequireAudioOutput,
                out hasExplicitRequireAudioOutput);
            _publisherStartUnixMs = TryReadLongArgument(
                PublisherStartUnixMsArgumentName,
                AndroidPublisherStartUnixMsExtraName,
                -1L,
                out _hasPublisherStartUnixMs);

            _requestedWindowWidth = TryReadIntArgument(
                WindowWidthArgumentName,
                AndroidWindowWidthExtraName,
                Player.Width,
                out _hasExplicitWindowWidth);
            _requestedWindowHeight = TryReadIntArgument(
                WindowHeightArgumentName,
                AndroidWindowHeightExtraName,
                Player.Height,
                out _hasExplicitWindowHeight);

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
            Debug.Log("[CodexValidation] start_enter");
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
                    "[CodexValidation] start validation seconds={0:F1} requestedWindow={1}x{2} explicitWindow={3} video_renderer={4} require_audio_output={5}",
                    ValidationSeconds,
                    Player.Width,
                    Player.Height,
                    HasExplicitWindowOverride(),
                    Player.VideoRenderer,
                    RequireAudioOutput));

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
                    var validationWindowStartObservation =
                        MediaNativeInteropCommon.CreatePullValidationWindowStartObservation(
                            snapshot.HasTexture,
                            RequireAudioOutput,
                            Player.EnableAudio,
                            snapshot.AudioPlaying,
                            startupElapsed,
                            StartupTimeoutSeconds);
                    if (validationWindowStartObservation.ShouldStart)
                    {
                        StartValidationWindow(
                            now,
                            startupElapsed,
                            validationWindowStartObservation.Reason,
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
            var validationResult = EvaluateValidationResult(finalSnapshot);
            WriteValidationSummary(validationResult, finalSnapshot);
            var exitCode = validationResult.Passed ? 0 : 2;
            yield return QuitAfterDelay(0.5f, exitCode);
        }

        private ValidationSnapshot EmitStatus()
        {
            var snapshot = CaptureSnapshot();
            var backendRuntimeObservation =
                MediaNativeInteropCommon.CreatePullBackendRuntimeObservation(
                    Player != null,
                    Player != null ? Player.PreferredBackend : default(MediaBackendKind),
                    Player != null ? Player.ActualBackendKind : default(MediaBackendKind),
                    Player != null ? Player.VideoRenderer : default(MediaPlayerPull.PullVideoRendererKind),
                    Player != null ? Player.ActualVideoRenderer : default(MediaPlayerPull.PullVideoRendererKind));

            Debug.Log(string.Format(
                "[CodexValidation] time={0:F3}s texture={1} audioPlaying={2} started={3} startupElapsed={4:F3}s sourceState={5} sourcePackets={6} sourceTimeouts={7} sourceReconnects={8} window={9}x{10} textureSize={11}x{12} fullscreen={13} mode={14} backend={15} requested_renderer={16} actual_renderer={17} frame_contract_available={18} frame_contract_memory={19} frame_contract_dynamic_range={20} frame_contract_nominal_fps={21:F2} playback_contract_available={22} playback_contract_master_sec={23:F3} av_sync_contract_available={24} av_sync_contract_master={25} av_sync_contract_drift_ms={26:F1} bridge_descriptor_available={27} bridge_descriptor_state={28} bridge_descriptor_runtime={29} bridge_descriptor_zero_copy={30} bridge_descriptor_direct_bindable={31} bridge_descriptor_source_plane_textures={32} bridge_descriptor_fallback_copy={33} path_selection_available={34} path_selection_kind={35} path_selection_source_memory={36} path_selection_presented_memory={37} path_selection_target_zero_copy={38} path_selection_source_plane_textures={39} path_selection_cpu_fallback={40} source_timeline_available={41} source_timeline_model={42} player_session_available={43} player_session_lifecycle={44} av_sync_enterprise_available={45} av_sync_enterprise_sample_count={46}",
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
                backendRuntimeObservation.ActualBackend,
                backendRuntimeObservation.RequestedVideoRenderer,
                backendRuntimeObservation.ActualVideoRenderer,
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
                snapshot.PathSelectionCpuFallback,
                snapshot.HasSourceTimelineContract,
                snapshot.SourceTimelineModel,
                snapshot.HasPlayerSessionContract,
                snapshot.PlayerSessionLifecycleState,
                snapshot.HasAvSyncEnterpriseMetrics,
                snapshot.AvSyncEnterpriseSampleCount));
            if (snapshot.HasRuntimeHealth)
            {
                Debug.Log(string.Format(
                    "[CodexValidation] runtime_health state={0} runtime_state={1} playback_intent={2} stream_count={3} video_decoder_count={4} has_audio_decoder={5} source_last_activity_age_sec={6:F3}",
                    snapshot.RuntimeStatePublic,
                    snapshot.RuntimeStateInternal,
                    snapshot.PlaybackIntent,
                    snapshot.StreamCount,
                    snapshot.VideoDecoderCount,
                    snapshot.HasAudioDecoder,
                    snapshot.SourceLastActivityAgeSec));
            }
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
                    "[CodexValidation] playback_contract master_sec={0:F3} master_us={1} external_sec={2:F3} external_us={3} has_audio_time_sec={4} audio_time_sec={5:F3} has_audio_time_us={6} audio_time_us={7} has_audio_presented_time_sec={8} audio_presented_time_sec={9:F3} has_audio_presented_time_us={10} audio_presented_time_us={11} audio_sink_delay_ms={12:F1} audio_sink_delay_us={13} has_audio_clock={14} has_us_mirror={15}",
                    snapshot.PlaybackContractMasterTimeSec,
                    snapshot.PlaybackContractMasterTimeUs,
                    snapshot.PlaybackContractExternalTimeSec,
                    snapshot.PlaybackContractExternalTimeUs,
                    snapshot.PlaybackContractHasAudioTimeSec,
                    snapshot.PlaybackContractAudioTimeSec,
                    snapshot.PlaybackContractHasAudioTimeUs,
                    snapshot.PlaybackContractAudioTimeUs,
                    snapshot.PlaybackContractHasAudioPresentedTimeSec,
                    snapshot.PlaybackContractAudioPresentedTimeSec,
                    snapshot.PlaybackContractHasAudioPresentedTimeUs,
                    snapshot.PlaybackContractAudioPresentedTimeUs,
                    snapshot.PlaybackContractAudioSinkDelaySec * 1000.0,
                    snapshot.PlaybackContractAudioSinkDelayUs,
                    snapshot.PlaybackContractHasAudioClock,
                    snapshot.PlaybackContractHasMicrosecondMirror));
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
            if (snapshot.HasSourceTimelineContract)
            {
                Debug.Log(string.Format(
                    "[CodexValidation] source_timeline model={0} anchor_kind={1} has_current_source_time_us={2} current_source_time_us={3} has_timeline_origin_us={4} timeline_origin_us={5} has_anchor_value_us={6} anchor_value_us={7} has_anchor_mono_us={8} anchor_mono_us={9} is_realtime={10}",
                    snapshot.SourceTimelineModel,
                    snapshot.SourceTimelineAnchorKind,
                    snapshot.SourceTimelineHasCurrentSourceTimeUs,
                    snapshot.SourceTimelineCurrentSourceTimeUs,
                    snapshot.SourceTimelineHasTimelineOriginUs,
                    snapshot.SourceTimelineTimelineOriginUs,
                    snapshot.SourceTimelineHasAnchorValueUs,
                    snapshot.SourceTimelineAnchorValueUs,
                    snapshot.SourceTimelineHasAnchorMonoUs,
                    snapshot.SourceTimelineAnchorMonoUs,
                    snapshot.SourceTimelineIsRealtime));
            }
            if (snapshot.HasPlayerSessionContract)
            {
                Debug.Log(string.Format(
                    "[CodexValidation] player_session lifecycle_state={0} public_state={1} runtime_state={2} playback_intent={3} stop_reason={4} source_state={5} can_seek={6} is_realtime={7} is_buffering={8} is_syncing={9}",
                    snapshot.PlayerSessionLifecycleState,
                    snapshot.PlayerSessionPublicState,
                    snapshot.PlayerSessionRuntimeState,
                    snapshot.PlayerSessionPlaybackIntent,
                    snapshot.PlayerSessionStopReason,
                    snapshot.PlayerSessionSourceState,
                    snapshot.PlayerSessionCanSeek,
                    snapshot.PlayerSessionIsRealtime,
                    snapshot.PlayerSessionIsBuffering,
                    snapshot.PlayerSessionIsSyncing));
            }
            if (snapshot.HasAudioOutputPolicy)
            {
                Debug.Log(string.Format(
                    "[CodexValidation] audio_output_policy file_start_ms={0} android_file_start_ms={1} realtime_start_ms={2} realtime_startup_grace_ms={3} realtime_startup_minimum_threshold_ms={4} file_ring_capacity_ms={5} android_file_ring_capacity_ms={6} realtime_ring_capacity_ms={7} file_buffered_ceiling_ms={8} android_file_buffered_ceiling_ms={9} realtime_buffered_ceiling_ms={10} realtime_startup_additional_sink_delay_ms={11} realtime_steady_additional_sink_delay_ms={12} realtime_backend_additional_sink_delay_ms={13} realtime_start_requires_video_frame={14} allow_android_file_output_rate_bridge={15}",
                    snapshot.AudioOutputPolicyFileStartThresholdMs,
                    snapshot.AudioOutputPolicyAndroidFileStartThresholdMs,
                    snapshot.AudioOutputPolicyRealtimeStartThresholdMs,
                    snapshot.AudioOutputPolicyRealtimeStartupGraceMs,
                    snapshot.AudioOutputPolicyRealtimeStartupMinimumThresholdMs,
                    snapshot.AudioOutputPolicyFileRingCapacityMs,
                    snapshot.AudioOutputPolicyAndroidFileRingCapacityMs,
                    snapshot.AudioOutputPolicyRealtimeRingCapacityMs,
                    snapshot.AudioOutputPolicyFileBufferedCeilingMs,
                    snapshot.AudioOutputPolicyAndroidFileBufferedCeilingMs,
                    snapshot.AudioOutputPolicyRealtimeBufferedCeilingMs,
                    snapshot.AudioOutputPolicyRealtimeStartupAdditionalSinkDelayMs,
                    snapshot.AudioOutputPolicyRealtimeSteadyAdditionalSinkDelayMs,
                    snapshot.AudioOutputPolicyRealtimeBackendAdditionalSinkDelayMs,
                    snapshot.AudioOutputPolicyRealtimeStartRequiresVideoFrame,
                    snapshot.AudioOutputPolicyAllowAndroidFileOutputRateBridge));
            }
            if (snapshot.HasAvSyncEnterpriseMetrics)
            {
                Debug.Log(string.Format(
                    "[CodexValidation] av_sync_enterprise sample_count={0} window_span_us={1} latest_raw_offset_us={2} latest_smooth_offset_us={3} drift_slope_ppm={4:F3} drift_projected_2h_ms={5:F3} offset_abs_p95_us={6} offset_abs_p99_us={7} offset_abs_max_us={8}",
                    snapshot.AvSyncEnterpriseSampleCount,
                    snapshot.AvSyncEnterpriseWindowSpanUs,
                    snapshot.AvSyncEnterpriseLatestRawOffsetUs,
                    snapshot.AvSyncEnterpriseLatestSmoothOffsetUs,
                    snapshot.AvSyncEnterpriseDriftSlopePpm,
                    snapshot.AvSyncEnterpriseDriftProjected2hMs,
                    snapshot.AvSyncEnterpriseOffsetAbsP95Us,
                    snapshot.AvSyncEnterpriseOffsetAbsP99Us,
                    snapshot.AvSyncEnterpriseOffsetAbsMaxUs));
            }
            if (snapshot.HasPassiveAvSyncSnapshot)
            {
                Debug.Log(string.Format(
                    "[CodexValidation] passive_av_sync raw_offset_us={0} smooth_offset_us={1} drift_ppm={2:F3} drift_intercept_us={3} drift_sample_count={4} video_schedule={5} audio_resample_ratio={6:F6} audio_resample_active={7} should_rebuild_anchor={8}",
                    snapshot.PassiveAvSyncRawOffsetUs,
                    snapshot.PassiveAvSyncSmoothOffsetUs,
                    snapshot.PassiveAvSyncDriftPpm,
                    snapshot.PassiveAvSyncDriftInterceptUs,
                    snapshot.PassiveAvSyncDriftSampleCount,
                    snapshot.PassiveAvSyncVideoSchedule,
                    snapshot.PassiveAvSyncAudioResampleRatio,
                    snapshot.PassiveAvSyncAudioResampleActive,
                    snapshot.PassiveAvSyncShouldRebuildAnchor));
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
            TrySyncVisibleSurfaceMaterial();
            var playbackTime = SafeReadPlaybackTime();
            var textureObservation =
                MediaNativeInteropCommon.CreatePullValidationVideoTextureObservation(
                    Player.HasPresentedVideoFrame,
                    Player.TargetMaterial != null ? Player.TargetMaterial.mainTexture : null);
            var audioSource = Player.GetComponent<AudioSource>();
            var audioPlaying = audioSource != null && audioSource.isPlaying;
            double audioPresentedTimeSec;
            double audioPipelineDelaySec;
            var hasAudioPresentation = Player.TryGetEstimatedAudioPresentation(
                out audioPresentedTimeSec,
                out audioPipelineDelaySec);

            MediaPlayerPull.PlayerRuntimeHealth health;
            var hasHealth = Player.TryGetRuntimeHealth(out health);
            double presentedVideoTimeSec;
            var hasPresentedVideoTime = Player.TryGetPresentedVideoTimeSec(out presentedVideoTimeSec);
            var referencePlaybackObservation =
                MediaNativeInteropCommon.CreateReferencePlaybackObservation(
                    playbackTime,
                    hasPresentedVideoTime,
                    presentedVideoTimeSec,
                    hasHealth,
                    hasHealth ? health.CurrentTimeSec : -1.0,
                    hasHealth && health.IsRealtime,
                    RealtimeReferenceLagToleranceSeconds);

            var referencePlaybackTime = referencePlaybackObservation.ReferenceTimeSec;
            var referencePlaybackKind = referencePlaybackObservation.ReferenceKind;
            var hasAvSyncSample = hasAudioPresentation && referencePlaybackObservation.HasSample;
            var avSyncDeltaMilliseconds = hasAvSyncSample
                ? (audioPresentedTimeSec - referencePlaybackTime) * 1000.0
                : 0.0;
            var runtimeHealthObservation =
                MediaNativeInteropCommon.CreateRuntimeHealthObservation(
                    hasHealth,
                    health);
            var playbackStartObservation =
                MediaNativeInteropCommon.CreatePlaybackStartObservation(
                    true,
                    Player.HasStartedPlayback,
                    runtimeHealthObservation.Available,
                    runtimeHealthObservation.IsPlaying,
                    playbackTime,
                    true);
            MediaNativeInteropCommon.VideoFrameContractView frameContract;
            var hasFrameContract = Player.TryGetLatestVideoFrameContract(out frameContract);
            var frameContractObservation =
                MediaNativeInteropCommon.CreateVideoFrameObservation(
                    hasFrameContract,
                    frameContract);
            MediaNativeInteropCommon.PlaybackTimingContractView playbackTimingContract;
            var hasPlaybackTimingContract = Player.TryGetPlaybackTimingContract(
                out playbackTimingContract);
            var playbackTimingObservation =
                MediaNativeInteropCommon.CreatePlaybackTimingObservation(
                    hasPlaybackTimingContract,
                    playbackTimingContract);
            MediaNativeInteropCommon.AvSyncContractView avSyncContract;
            var hasAvSyncContract = Player.TryGetAvSyncContract(out avSyncContract);
            var avSyncContractObservation =
                MediaNativeInteropCommon.CreateAvSyncContractObservation(
                    hasAvSyncContract,
                    avSyncContract);
            MediaNativeInteropCommon.SourceTimelineContractView sourceTimelineContract;
            var hasSourceTimelineContract = Player.TryGetSourceTimelineContract(
                out sourceTimelineContract);
            var sourceTimelineObservation =
                MediaNativeInteropCommon.CreateSourceTimelineObservation(
                    hasSourceTimelineContract,
                    sourceTimelineContract);
            MediaNativeInteropCommon.PlayerSessionContractView playerSessionContract;
            var hasPlayerSessionContract = Player.TryGetPlayerSessionContract(
                out playerSessionContract);
            var playerSessionObservation =
                MediaNativeInteropCommon.CreatePlayerSessionObservation(
                    hasPlayerSessionContract,
                    playerSessionContract);
            MediaNativeInteropCommon.AudioOutputPolicyView audioOutputPolicy;
            var hasAudioOutputPolicy = Player.TryGetAudioOutputPolicy(out audioOutputPolicy);
            var audioOutputPolicyObservation =
                MediaNativeInteropCommon.CreateAudioOutputPolicyObservation(
                    hasAudioOutputPolicy,
                    audioOutputPolicy);
            MediaNativeInteropCommon.AvSyncEnterpriseMetricsView avSyncEnterpriseMetrics;
            var hasAvSyncEnterpriseMetrics = Player.TryGetAvSyncEnterpriseMetrics(
                out avSyncEnterpriseMetrics);
            var avSyncEnterpriseObservation =
                MediaNativeInteropCommon.CreateAvSyncEnterpriseObservation(
                    hasAvSyncEnterpriseMetrics,
                    avSyncEnterpriseMetrics);
            MediaNativeInteropCommon.PassiveAvSyncSnapshotView passiveAvSyncSnapshot;
            var hasPassiveAvSyncSnapshot = Player.TryGetPassiveAvSyncSnapshot(
                out passiveAvSyncSnapshot);
            var passiveAvSyncObservation =
                MediaNativeInteropCommon.CreatePassiveAvSyncObservation(
                    hasPassiveAvSyncSnapshot,
                    passiveAvSyncSnapshot);
            MediaNativeInteropCommon.NativeVideoBridgeDescriptorView bridgeDescriptor;
            var hasBridgeDescriptor = Player.TryGetNativeVideoBridgeDescriptor(out bridgeDescriptor);
            var bridgeDescriptorObservation =
                MediaNativeInteropCommon.CreateNativeVideoBridgeDescriptorObservation(
                    hasBridgeDescriptor,
                    bridgeDescriptor);
            MediaNativeInteropCommon.NativeVideoPathSelectionView pathSelection;
            var hasPathSelection = Player.TryGetNativeVideoPathSelection(out pathSelection);
            var pathSelectionObservation =
                MediaNativeInteropCommon.CreateNativeVideoPathSelectionObservation(
                    hasPathSelection,
                    pathSelection);
            MediaNativeInteropCommon.WgpuRenderDescriptorView wgpuDescriptor;
            var hasWgpuRenderDescriptor = Player.TryGetWgpuRenderDescriptor(out wgpuDescriptor);
            var wgpuDescriptorObservation =
                MediaNativeInteropCommon.CreateWgpuRenderDescriptorObservation(
                    hasWgpuRenderDescriptor,
                    wgpuDescriptor);
            MediaNativeInteropCommon.WgpuRenderStateView wgpuState;
            var hasWgpuRenderState = Player.TryGetWgpuRenderStateView(out wgpuState);
            var wgpuStateObservation =
                MediaNativeInteropCommon.CreateWgpuRenderStateObservation(
                    hasWgpuRenderState,
                    wgpuState);
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
                HasTexture = textureObservation.HasTexture,
                AudioPlaying = audioPlaying,
                Started = playbackStartObservation.Started,
                TextureWidth = textureObservation.TextureWidth,
                TextureHeight = textureObservation.TextureHeight,
                HasRuntimeHealth = runtimeHealthObservation.Available,
                RuntimeStatePublic = runtimeHealthObservation.State,
                RuntimeStateInternal = runtimeHealthObservation.RuntimeState,
                PlaybackIntent = runtimeHealthObservation.PlaybackIntent,
                StreamCount = runtimeHealthObservation.StreamCount,
                VideoDecoderCount = runtimeHealthObservation.VideoDecoderCount,
                HasAudioDecoder = runtimeHealthObservation.HasAudioDecoder,
                SourceState = runtimeHealthObservation.SourceState,
                SourcePackets = runtimeHealthObservation.SourcePackets,
                SourceTimeouts = runtimeHealthObservation.SourceTimeouts,
                SourceReconnects = runtimeHealthObservation.SourceReconnects,
                SourceLastActivityAgeSec = runtimeHealthObservation.SourceLastActivityAgeSec,
                HasAvSyncSample = hasAvSyncSample,
                AudioPresentedTimeSec = audioPresentedTimeSec,
                AudioPipelineDelaySec = audioPipelineDelaySec,
                AvSyncDeltaMilliseconds = avSyncDeltaMilliseconds,
                HasPresentedVideoTime = hasPresentedVideoTime,
                PresentedVideoTimeSec = hasPresentedVideoTime ? presentedVideoTimeSec : -1.0,
                ReferencePlaybackTimeSec = referencePlaybackTime,
                ReferencePlaybackKind = referencePlaybackKind,
                HasFrameContract = frameContractObservation.Available,
                FrameContractMemoryKind = frameContractObservation.MemoryKind,
                FrameContractDynamicRange = frameContractObservation.DynamicRange,
                FrameContractNominalFps = frameContractObservation.NominalFps,
                HasPlaybackTimingContract = playbackTimingObservation.Available,
                PlaybackContractMasterTimeSec = playbackTimingObservation.MasterTimeSec,
                PlaybackContractMasterTimeUs = playbackTimingObservation.MasterTimeUs,
                PlaybackContractExternalTimeSec = playbackTimingObservation.ExternalTimeSec,
                PlaybackContractExternalTimeUs = playbackTimingObservation.ExternalTimeUs,
                PlaybackContractHasAudioTimeSec = playbackTimingObservation.HasAudioTimeSec,
                PlaybackContractAudioTimeSec = playbackTimingObservation.AudioTimeSec,
                PlaybackContractHasAudioTimeUs = playbackTimingObservation.HasAudioTimeUs,
                PlaybackContractAudioTimeUs = playbackTimingObservation.AudioTimeUs,
                PlaybackContractHasAudioPresentedTimeSec =
                    playbackTimingObservation.HasAudioPresentedTimeSec,
                PlaybackContractAudioPresentedTimeSec =
                    playbackTimingObservation.AudioPresentedTimeSec,
                PlaybackContractHasAudioPresentedTimeUs =
                    playbackTimingObservation.HasAudioPresentedTimeUs,
                PlaybackContractAudioPresentedTimeUs =
                    playbackTimingObservation.AudioPresentedTimeUs,
                PlaybackContractAudioSinkDelaySec =
                    playbackTimingObservation.AudioSinkDelaySec,
                PlaybackContractAudioSinkDelayUs =
                    playbackTimingObservation.AudioSinkDelayUs,
                PlaybackContractHasMicrosecondMirror =
                    playbackTimingObservation.HasMicrosecondMirror,
                PlaybackContractHasAudioClock =
                    playbackTimingObservation.HasAudioClock,
                HasAvSyncContract = avSyncContractObservation.Available,
                AvSyncContractMasterClock = avSyncContractObservation.MasterClock,
                AvSyncContractHasAudioClockSec = avSyncContractObservation.HasAudioClockSec,
                AvSyncContractAudioClockSec = avSyncContractObservation.AudioClockSec,
                AvSyncContractHasVideoClockSec = avSyncContractObservation.HasVideoClockSec,
                AvSyncContractVideoClockSec = avSyncContractObservation.VideoClockSec,
                AvSyncContractClockDeltaMs = avSyncContractObservation.ClockDeltaMs,
                AvSyncContractDriftMs = avSyncContractObservation.DriftMs,
                AvSyncContractStartupWarmupComplete =
                    avSyncContractObservation.StartupWarmupComplete,
                AvSyncContractDropTotal = avSyncContractObservation.DropTotal,
                AvSyncContractDuplicateTotal = avSyncContractObservation.DuplicateTotal,
                HasSourceTimelineContract = sourceTimelineObservation.Available,
                SourceTimelineModel = sourceTimelineObservation.Model,
                SourceTimelineAnchorKind = sourceTimelineObservation.AnchorKind,
                SourceTimelineHasCurrentSourceTimeUs =
                    sourceTimelineObservation.HasCurrentSourceTimeUs,
                SourceTimelineCurrentSourceTimeUs =
                    sourceTimelineObservation.CurrentSourceTimeUs,
                SourceTimelineHasTimelineOriginUs =
                    sourceTimelineObservation.HasTimelineOriginUs,
                SourceTimelineTimelineOriginUs =
                    sourceTimelineObservation.TimelineOriginUs,
                SourceTimelineHasAnchorValueUs =
                    sourceTimelineObservation.HasAnchorValueUs,
                SourceTimelineAnchorValueUs =
                    sourceTimelineObservation.AnchorValueUs,
                SourceTimelineHasAnchorMonoUs =
                    sourceTimelineObservation.HasAnchorMonoUs,
                SourceTimelineAnchorMonoUs =
                    sourceTimelineObservation.AnchorMonoUs,
                SourceTimelineIsRealtime =
                    sourceTimelineObservation.IsRealtime,
                HasPlayerSessionContract = playerSessionObservation.Available,
                PlayerSessionLifecycleState = playerSessionObservation.LifecycleState,
                PlayerSessionPublicState = playerSessionObservation.PublicState,
                PlayerSessionRuntimeState = playerSessionObservation.RuntimeState,
                PlayerSessionPlaybackIntent = playerSessionObservation.PlaybackIntent,
                PlayerSessionStopReason = playerSessionObservation.StopReason,
                PlayerSessionSourceState = playerSessionObservation.SourceState,
                PlayerSessionCanSeek = playerSessionObservation.CanSeek,
                PlayerSessionIsRealtime = playerSessionObservation.IsRealtime,
                PlayerSessionIsBuffering = playerSessionObservation.IsBuffering,
                PlayerSessionIsSyncing = playerSessionObservation.IsSyncing,
                PlayerSessionAudioStartStateReported =
                    playerSessionObservation.AudioStartStateReported,
                PlayerSessionShouldStartAudio =
                    playerSessionObservation.ShouldStartAudio,
                PlayerSessionAudioStartBlockReason =
                    playerSessionObservation.AudioStartBlockReason,
                PlayerSessionRequiredBufferedSamples =
                    playerSessionObservation.RequiredBufferedSamples,
                PlayerSessionReportedBufferedSamples =
                    playerSessionObservation.ReportedBufferedSamples,
                PlayerSessionRequiresPresentedVideoFrame =
                    playerSessionObservation.RequiresPresentedVideoFrame,
                PlayerSessionHasPresentedVideoFrame =
                    playerSessionObservation.HasPresentedVideoFrame,
                PlayerSessionAndroidFileRateBridgeActive =
                    playerSessionObservation.AndroidFileRateBridgeActive,
                HasAudioOutputPolicy = audioOutputPolicyObservation.Available,
                AudioOutputPolicyFileStartThresholdMs =
                    audioOutputPolicyObservation.FileStartThresholdMilliseconds,
                AudioOutputPolicyAndroidFileStartThresholdMs =
                    audioOutputPolicyObservation.AndroidFileStartThresholdMilliseconds,
                AudioOutputPolicyRealtimeStartThresholdMs =
                    audioOutputPolicyObservation.RealtimeStartThresholdMilliseconds,
                AudioOutputPolicyRealtimeStartupGraceMs =
                    audioOutputPolicyObservation.RealtimeStartupGraceMilliseconds,
                AudioOutputPolicyRealtimeStartupMinimumThresholdMs =
                    audioOutputPolicyObservation.RealtimeStartupMinimumThresholdMilliseconds,
                AudioOutputPolicyFileRingCapacityMs =
                    audioOutputPolicyObservation.FileRingCapacityMilliseconds,
                AudioOutputPolicyAndroidFileRingCapacityMs =
                    audioOutputPolicyObservation.AndroidFileRingCapacityMilliseconds,
                AudioOutputPolicyRealtimeRingCapacityMs =
                    audioOutputPolicyObservation.RealtimeRingCapacityMilliseconds,
                AudioOutputPolicyFileBufferedCeilingMs =
                    audioOutputPolicyObservation.FileBufferedCeilingMilliseconds,
                AudioOutputPolicyAndroidFileBufferedCeilingMs =
                    audioOutputPolicyObservation.AndroidFileBufferedCeilingMilliseconds,
                AudioOutputPolicyRealtimeBufferedCeilingMs =
                    audioOutputPolicyObservation.RealtimeBufferedCeilingMilliseconds,
                AudioOutputPolicyRealtimeStartupAdditionalSinkDelayMs =
                    audioOutputPolicyObservation.RealtimeStartupAdditionalSinkDelayMilliseconds,
                AudioOutputPolicyRealtimeSteadyAdditionalSinkDelayMs =
                    audioOutputPolicyObservation.RealtimeSteadyAdditionalSinkDelayMilliseconds,
                AudioOutputPolicyRealtimeBackendAdditionalSinkDelayMs =
                    audioOutputPolicyObservation.RealtimeBackendAdditionalSinkDelayMilliseconds,
                AudioOutputPolicyRealtimeStartRequiresVideoFrame =
                    audioOutputPolicyObservation.RealtimeStartRequiresVideoFrame,
                AudioOutputPolicyAllowAndroidFileOutputRateBridge =
                    audioOutputPolicyObservation.AllowAndroidFileOutputRateBridge,
                HasAvSyncEnterpriseMetrics = avSyncEnterpriseObservation.Available,
                AvSyncEnterpriseSampleCount = avSyncEnterpriseObservation.SampleCount,
                AvSyncEnterpriseWindowSpanUs = avSyncEnterpriseObservation.WindowSpanUs,
                AvSyncEnterpriseLatestRawOffsetUs =
                    avSyncEnterpriseObservation.LatestRawOffsetUs,
                AvSyncEnterpriseLatestSmoothOffsetUs =
                    avSyncEnterpriseObservation.LatestSmoothOffsetUs,
                AvSyncEnterpriseDriftSlopePpm =
                    avSyncEnterpriseObservation.DriftSlopePpm,
                AvSyncEnterpriseDriftProjected2hMs =
                    avSyncEnterpriseObservation.DriftProjected2hMs,
                AvSyncEnterpriseOffsetAbsP95Us =
                    avSyncEnterpriseObservation.OffsetAbsP95Us,
                AvSyncEnterpriseOffsetAbsP99Us =
                    avSyncEnterpriseObservation.OffsetAbsP99Us,
                AvSyncEnterpriseOffsetAbsMaxUs =
                    avSyncEnterpriseObservation.OffsetAbsMaxUs,
                HasPassiveAvSyncSnapshot = passiveAvSyncObservation.Available,
                PassiveAvSyncRawOffsetUs = passiveAvSyncObservation.RawOffsetUs,
                PassiveAvSyncSmoothOffsetUs = passiveAvSyncObservation.SmoothOffsetUs,
                PassiveAvSyncDriftPpm = passiveAvSyncObservation.DriftPpm,
                PassiveAvSyncDriftInterceptUs = passiveAvSyncObservation.DriftInterceptUs,
                PassiveAvSyncDriftSampleCount = passiveAvSyncObservation.DriftSampleCount,
                PassiveAvSyncVideoSchedule = passiveAvSyncObservation.VideoSchedule,
                PassiveAvSyncAudioResampleRatio = passiveAvSyncObservation.AudioResampleRatio,
                PassiveAvSyncAudioResampleActive =
                    passiveAvSyncObservation.AudioResampleActive,
                PassiveAvSyncShouldRebuildAnchor =
                    passiveAvSyncObservation.ShouldRebuildAnchor,
                HasBridgeDescriptor = bridgeDescriptorObservation.Available,
                BridgeDescriptorState = bridgeDescriptorObservation.State,
                BridgeDescriptorRuntimeKind = bridgeDescriptorObservation.RuntimeKind,
                BridgeDescriptorZeroCopySupported =
                    bridgeDescriptorObservation.ZeroCopySupported,
                BridgeDescriptorDirectBindable =
                    bridgeDescriptorObservation.PresentedFrameDirectBindable,
                BridgeDescriptorSourcePlaneTexturesSupported =
                    bridgeDescriptorObservation.SourcePlaneTexturesSupported,
                BridgeDescriptorFallbackCopyPath =
                    bridgeDescriptorObservation.FallbackCopyPath,
                HasPathSelection = pathSelectionObservation.Available,
                PathSelectionKind = pathSelectionObservation.Kind,
                PathSelectionSourceMemoryKind = pathSelectionObservation.SourceMemoryKind,
                PathSelectionPresentedMemoryKind = pathSelectionObservation.PresentedMemoryKind,
                PathSelectionTargetZeroCopy = pathSelectionObservation.TargetZeroCopy,
                PathSelectionSourcePlaneTexturesSupported =
                    pathSelectionObservation.SourcePlaneTexturesSupported,
                PathSelectionCpuFallback = pathSelectionObservation.CpuFallback,
                HasWgpuRenderDescriptor = wgpuDescriptorObservation.Available,
                WgpuRuntimeReady = wgpuDescriptorObservation.RuntimeReady,
                WgpuOutputWidth = wgpuDescriptorObservation.OutputWidth,
                WgpuOutputHeight = wgpuDescriptorObservation.OutputHeight,
                WgpuSupportsYuv420p = wgpuDescriptorObservation.SupportsYuv420p,
                WgpuSupportsNv12 = wgpuDescriptorObservation.SupportsNv12,
                WgpuSupportsP010 = wgpuDescriptorObservation.SupportsP010,
                WgpuSupportsRgba32 = wgpuDescriptorObservation.SupportsRgba32,
                WgpuSupportsExternalTextureRgba =
                    wgpuDescriptorObservation.SupportsExternalTextureRgba,
                WgpuSupportsExternalTextureYu12 =
                    wgpuDescriptorObservation.SupportsExternalTextureYu12,
                WgpuReadbackExportSupported =
                    wgpuDescriptorObservation.ReadbackExportSupported,
                HasWgpuRenderState = wgpuStateObservation.Available,
                WgpuRenderPath = wgpuStateObservation.RenderPath,
                WgpuSourceMemoryKind = wgpuStateObservation.SourceMemoryKind,
                WgpuPresentedMemoryKind = wgpuStateObservation.PresentedMemoryKind,
                WgpuSourcePixelFormat = wgpuStateObservation.SourcePixelFormat,
                WgpuPresentedPixelFormat = wgpuStateObservation.PresentedPixelFormat,
                WgpuExternalTextureFormat = wgpuStateObservation.ExternalTextureFormat,
                WgpuHasRenderedFrame = wgpuStateObservation.HasRenderedFrame,
                WgpuRenderedFrameIndex = wgpuStateObservation.RenderedFrameIndex,
                WgpuRenderedTimeSec = wgpuStateObservation.RenderedTimeSec,
                WgpuHasRenderError = wgpuStateObservation.HasRenderError,
                WgpuRenderErrorKind = wgpuStateObservation.RenderErrorKind,
                WgpuUploadPlaneCount = wgpuStateObservation.UploadPlaneCount,
                WgpuSourceZeroCopy = wgpuStateObservation.SourceZeroCopy,
                WgpuCpuFallback = wgpuStateObservation.CpuFallback,
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
            var evidenceObservation =
                MediaNativeInteropCommon.AccumulateValidationWindowEvidenceObservation(
                    _observedTextureDuringWindow,
                    _observedAudioDuringWindow,
                    _observedStartedDuringWindow,
                    false,
                    _maxObservedPlaybackTime,
                    snapshot.HasTexture,
                    snapshot.AudioPlaying,
                    snapshot.Started,
                    false,
                    snapshot.PlaybackTime);
            _observedTextureDuringWindow =
                evidenceObservation.ObservedTextureDuringWindow;
            _observedAudioDuringWindow =
                evidenceObservation.ObservedAudioDuringWindow;
            _observedStartedDuringWindow =
                evidenceObservation.ObservedStartedDuringWindow;
            _maxObservedPlaybackTime =
                evidenceObservation.MaxObservedPlaybackTime;
        }

        private ValidationResultInfo EvaluateValidationResult(ValidationSnapshot finalSnapshot)
        {
            RecordValidationObservation(finalSnapshot);

            var resultObservation =
                MediaNativeInteropCommon.CreatePullValidationResultObservation(
                    _validationWindowStartReason,
                    _observedStartedDuringWindow,
                    _observedTextureDuringWindow,
                    _observedAudioDuringWindow,
                    RequireAudioOutput,
                    Player.EnableAudio,
                    MinimumPlaybackAdvanceSeconds,
                    _validationWindowInitialPlaybackTime,
                    _maxObservedPlaybackTime);
            var playbackAdvance = resultObservation.PlaybackAdvanceSeconds;

            if (!resultObservation.Passed)
            {
                if (resultObservation.Reason == "playback-stalled")
                {
                    Debug.LogError(
                        string.Format(
                            "[CodexValidation] result=failed reason=playback-stalled advance={0:F3}s",
                            playbackAdvance));
                }
                else
                {
                    Debug.LogError(
                        "[CodexValidation] result=failed reason=" + resultObservation.Reason);
                }

                return ValidationResultInfo.Failed(
                    resultObservation.Reason,
                    playbackAdvance);
            }

            Debug.Log(
                string.Format(
                    "[CodexValidation] result=passed reason={0} advance={1:F3}s sourceState={2} sourceTimeouts={3} sourceReconnects={4}",
                    resultObservation.Reason,
                    playbackAdvance,
                    finalSnapshot.SourceState,
                    finalSnapshot.SourceTimeouts,
                    finalSnapshot.SourceReconnects));
            Debug.Log("[CodexValidation] complete");
            return ValidationResultInfo.PassedWithAdvance(playbackAdvance);
        }

        private void WriteValidationSummary(
            ValidationResultInfo result,
            ValidationSnapshot finalSnapshot)
        {
            try
            {
                var summaryPath = Path.Combine(
                    Application.persistentDataPath,
                    SummaryFileName);
                var backendRuntimeObservation =
                    MediaNativeInteropCommon.CreatePullBackendRuntimeObservation(
                        Player != null,
                        Player != null ? Player.PreferredBackend : default(MediaBackendKind),
                        Player != null ? Player.ActualBackendKind : default(MediaBackendKind),
                        Player != null ? Player.VideoRenderer : default(MediaPlayerPull.PullVideoRendererKind),
                        Player != null ? Player.ActualVideoRenderer : default(MediaPlayerPull.PullVideoRendererKind));
                var builder = new StringBuilder();
                MediaNativeInteropCommon.AppendValidationSummaryHeader(
                    builder,
                    new MediaNativeInteropCommon.ValidationSummaryHeaderView
                    {
                        Passed = result.Passed,
                        Reason = result.Reason,
                        Uri = Player != null ? Player.Uri : string.Empty,
                        RequestedBackend = backendRuntimeObservation.RequestedBackend,
                        ActualBackend = backendRuntimeObservation.ActualBackend,
                        IncludeVideoRenderer = true,
                        RequestedVideoRenderer = backendRuntimeObservation.RequestedVideoRenderer,
                        ActualVideoRenderer = backendRuntimeObservation.ActualVideoRenderer,
                        IncludeRequireAudioOutput = true,
                        RequireAudioOutput = RequireAudioOutput,
                        PlaybackAdvanceSeconds = result.PlaybackAdvanceSeconds,
                    });
                MediaNativeInteropCommon.AppendValidationSummaryWindow(
                    builder,
                    new MediaNativeInteropCommon.ValidationSummaryWindowView
                    {
                        HasTexture = finalSnapshot.HasTexture,
                        AudioPlaying = finalSnapshot.AudioPlaying,
                        Started = finalSnapshot.Started,
                        ObservedTextureDuringWindow = _observedTextureDuringWindow,
                        ObservedAudioDuringWindow = _observedAudioDuringWindow,
                        ObservedStartedDuringWindow = _observedStartedDuringWindow,
                        IncludeValidationWindowStartReason =
                            !string.IsNullOrEmpty(_validationWindowStartReason),
                        ValidationWindowStartReason = _validationWindowStartReason,
                    });
                MediaNativeInteropCommon.AppendValidationSummaryRuntimeHealth(
                    builder,
                    new MediaNativeInteropCommon.ValidationSummaryRuntimeHealthView
                    {
                        Available = finalSnapshot.HasRuntimeHealth,
                        State = finalSnapshot.RuntimeStatePublic.ToString(),
                        RuntimeState = finalSnapshot.RuntimeStateInternal.ToString(),
                        PlaybackIntent = finalSnapshot.PlaybackIntent.ToString(),
                        StreamCount = finalSnapshot.StreamCount.ToString(),
                        VideoDecoderCount = finalSnapshot.VideoDecoderCount.ToString(),
                        HasAudioDecoder = finalSnapshot.HasAudioDecoder.ToString(),
                    });
                MediaNativeInteropCommon.AppendValidationSummarySourceRuntime(
                    builder,
                    new MediaNativeInteropCommon.ValidationSummarySourceRuntimeView
                    {
                        State = finalSnapshot.SourceState,
                        Packets = finalSnapshot.SourcePackets,
                        Timeouts = finalSnapshot.SourceTimeouts,
                        Reconnects = finalSnapshot.SourceReconnects,
                        IncludeLastActivityAgeSeconds = true,
                        LastActivityAgeSeconds = finalSnapshot.SourceLastActivityAgeSec.ToString("F3"),
                    });
                MediaNativeInteropCommon.AppendValidationSummaryPathSelection(
                    builder,
                    new MediaNativeInteropCommon.ValidationSummaryPathSelectionView
                    {
                        Available = finalSnapshot.HasPathSelection,
                        Kind = finalSnapshot.PathSelectionKind,
                    });
                var summarySourceTimeline =
                    MediaNativeInteropCommon.CreateObservedSourceTimelineAuditStrings(
                        finalSnapshot.HasSourceTimelineContract,
                        finalSnapshot.SourceTimelineModel,
                        finalSnapshot.SourceTimelineAnchorKind,
                        finalSnapshot.SourceTimelineHasCurrentSourceTimeUs.ToString(),
                        finalSnapshot.SourceTimelineCurrentSourceTimeUs.ToString(),
                        finalSnapshot.SourceTimelineHasTimelineOriginUs.ToString(),
                        finalSnapshot.SourceTimelineTimelineOriginUs.ToString(),
                        finalSnapshot.SourceTimelineHasAnchorValueUs.ToString(),
                        finalSnapshot.SourceTimelineAnchorValueUs.ToString(),
                        finalSnapshot.SourceTimelineHasAnchorMonoUs.ToString(),
                        finalSnapshot.SourceTimelineAnchorMonoUs.ToString(),
                        finalSnapshot.SourceTimelineIsRealtime.ToString());
                var summaryAudioOutputPolicy =
                    MediaNativeInteropCommon.CreateObservedAudioOutputPolicyAuditStrings(
                        finalSnapshot.HasAudioOutputPolicy,
                        finalSnapshot.AudioOutputPolicyFileStartThresholdMs.ToString(),
                        finalSnapshot.AudioOutputPolicyAndroidFileStartThresholdMs.ToString(),
                        finalSnapshot.AudioOutputPolicyRealtimeStartThresholdMs.ToString(),
                        finalSnapshot.AudioOutputPolicyRealtimeStartupGraceMs.ToString(),
                        finalSnapshot.AudioOutputPolicyRealtimeStartupMinimumThresholdMs.ToString(),
                        finalSnapshot.AudioOutputPolicyFileRingCapacityMs.ToString(),
                        finalSnapshot.AudioOutputPolicyAndroidFileRingCapacityMs.ToString(),
                        finalSnapshot.AudioOutputPolicyRealtimeRingCapacityMs.ToString(),
                        finalSnapshot.AudioOutputPolicyFileBufferedCeilingMs.ToString(),
                        finalSnapshot.AudioOutputPolicyAndroidFileBufferedCeilingMs.ToString(),
                        finalSnapshot.AudioOutputPolicyRealtimeBufferedCeilingMs.ToString(),
                        finalSnapshot.AudioOutputPolicyRealtimeStartupAdditionalSinkDelayMs.ToString(),
                        finalSnapshot.AudioOutputPolicyRealtimeSteadyAdditionalSinkDelayMs.ToString(),
                        finalSnapshot.AudioOutputPolicyRealtimeBackendAdditionalSinkDelayMs.ToString(),
                        finalSnapshot.AudioOutputPolicyRealtimeStartRequiresVideoFrame.ToString(),
                        finalSnapshot.AudioOutputPolicyAllowAndroidFileOutputRateBridge.ToString());
                var summaryPassiveAvSync =
                    MediaNativeInteropCommon.CreateObservedPassiveAvSyncAuditStrings(
                        finalSnapshot.HasPassiveAvSyncSnapshot,
                        finalSnapshot.PassiveAvSyncRawOffsetUs.ToString(),
                        finalSnapshot.PassiveAvSyncSmoothOffsetUs.ToString("F3"),
                        finalSnapshot.PassiveAvSyncDriftPpm.ToString("F3"),
                        finalSnapshot.PassiveAvSyncDriftInterceptUs.ToString(),
                        finalSnapshot.PassiveAvSyncDriftSampleCount.ToString(),
                        finalSnapshot.PassiveAvSyncVideoSchedule,
                        finalSnapshot.PassiveAvSyncAudioResampleRatio.ToString("F6"),
                        finalSnapshot.PassiveAvSyncAudioResampleActive.ToString(),
                        finalSnapshot.PassiveAvSyncShouldRebuildAnchor.ToString());
                var summaryPlaybackContract =
                    MediaNativeInteropCommon.CreateObservedPlaybackTimingAuditStringsExtended(
                        finalSnapshot.HasPlaybackTimingContract,
                        finalSnapshot.PlaybackContractMasterTimeSec.ToString("F3"),
                        finalSnapshot.PlaybackContractMasterTimeUs.ToString(),
                        finalSnapshot.PlaybackContractExternalTimeSec.ToString("F3"),
                        finalSnapshot.PlaybackContractExternalTimeUs.ToString(),
                        finalSnapshot.PlaybackContractHasAudioTimeSec.ToString(),
                        finalSnapshot.PlaybackContractAudioTimeSec.ToString("F3"),
                        finalSnapshot.PlaybackContractHasAudioTimeUs.ToString(),
                        finalSnapshot.PlaybackContractAudioTimeUs.ToString(),
                        finalSnapshot.PlaybackContractHasAudioPresentedTimeSec.ToString(),
                        finalSnapshot.PlaybackContractAudioPresentedTimeSec.ToString("F3"),
                        finalSnapshot.PlaybackContractHasAudioPresentedTimeUs.ToString(),
                        finalSnapshot.PlaybackContractAudioPresentedTimeUs.ToString(),
                        (finalSnapshot.PlaybackContractAudioSinkDelaySec * 1000.0).ToString("F1"),
                        finalSnapshot.PlaybackContractAudioSinkDelayUs.ToString(),
                        finalSnapshot.PlaybackContractHasMicrosecondMirror.ToString(),
                        finalSnapshot.PlaybackContractHasAudioClock.ToString());
                var summaryPlayerSession =
                    MediaNativeInteropCommon.CreateValidationSummaryPlayerSessionExtended(
                        finalSnapshot.HasPlayerSessionContract,
                        finalSnapshot.PlayerSessionLifecycleState.ToString(),
                        finalSnapshot.PlayerSessionPublicState.ToString(),
                        finalSnapshot.PlayerSessionRuntimeState.ToString(),
                        finalSnapshot.PlayerSessionPlaybackIntent.ToString(),
                        finalSnapshot.PlayerSessionStopReason.ToString(),
                        finalSnapshot.PlayerSessionSourceState.ToString(),
                        finalSnapshot.PlayerSessionCanSeek.ToString(),
                        finalSnapshot.PlayerSessionIsRealtime.ToString(),
                        finalSnapshot.PlayerSessionIsBuffering.ToString(),
                        finalSnapshot.PlayerSessionIsSyncing.ToString(),
                        finalSnapshot.PlayerSessionAudioStartStateReported.ToString(),
                        finalSnapshot.PlayerSessionShouldStartAudio.ToString(),
                        finalSnapshot.PlayerSessionAudioStartBlockReason.ToString(),
                        finalSnapshot.PlayerSessionRequiredBufferedSamples.ToString(),
                        finalSnapshot.PlayerSessionReportedBufferedSamples.ToString(),
                        finalSnapshot.PlayerSessionRequiresPresentedVideoFrame.ToString(),
                        finalSnapshot.PlayerSessionHasPresentedVideoFrame.ToString(),
                        finalSnapshot.PlayerSessionAndroidFileRateBridgeActive.ToString());
                var summaryAvSyncEnterprise =
                    MediaNativeInteropCommon.CreateObservedAvSyncEnterpriseAuditStringsExtended(
                        finalSnapshot.HasAvSyncEnterpriseMetrics,
                        finalSnapshot.AvSyncEnterpriseSampleCount.ToString(),
                        finalSnapshot.AvSyncEnterpriseWindowSpanUs.ToString(),
                        finalSnapshot.AvSyncEnterpriseLatestRawOffsetUs.ToString(),
                        finalSnapshot.AvSyncEnterpriseLatestSmoothOffsetUs.ToString(),
                        finalSnapshot.AvSyncEnterpriseDriftSlopePpm.ToString("F3"),
                        finalSnapshot.AvSyncEnterpriseDriftProjected2hMs.ToString("F3"),
                        finalSnapshot.AvSyncEnterpriseOffsetAbsP95Us.ToString(),
                        finalSnapshot.AvSyncEnterpriseOffsetAbsP99Us.ToString(),
                        finalSnapshot.AvSyncEnterpriseOffsetAbsMaxUs.ToString());
                var summaryFrameContract =
                    MediaNativeInteropCommon.CreateValidationSummaryFrameContract(
                        finalSnapshot.HasFrameContract,
                        finalSnapshot.FrameContractMemoryKind,
                        finalSnapshot.FrameContractDynamicRange,
                        finalSnapshot.FrameContractNominalFps.ToString("F2"));
                var summaryAvSyncContract =
                    MediaNativeInteropCommon.CreateValidationSummaryAvSyncContract(
                        finalSnapshot.HasAvSyncContract,
                        finalSnapshot.AvSyncContractMasterClock,
                        finalSnapshot.AvSyncContractDriftMs.ToString("F1"),
                        finalSnapshot.AvSyncContractClockDeltaMs.ToString("F1"),
                        finalSnapshot.AvSyncContractDropTotal.ToString(),
                        finalSnapshot.AvSyncContractDuplicateTotal.ToString());
                MediaNativeInteropCommon.AppendValidationSummaryFrameContract(
                    builder,
                    summaryFrameContract);
                MediaNativeInteropCommon.AppendValidationSummaryPlaybackContractExtended(
                    builder,
                    summaryPlaybackContract);
                MediaNativeInteropCommon.AppendValidationSummaryAvSyncContract(
                    builder,
                    summaryAvSyncContract);
                MediaNativeInteropCommon.AppendValidationSummarySourceTimeline(
                    builder,
                    summarySourceTimeline);
                MediaNativeInteropCommon.AppendValidationSummaryPlayerSessionExtended(
                    builder,
                    summaryPlayerSession);
                MediaNativeInteropCommon.AppendValidationSummaryAudioOutputPolicy(
                    builder,
                    summaryAudioOutputPolicy);
                MediaNativeInteropCommon.AppendValidationSummaryAvSyncEnterpriseExtended(
                    builder,
                    summaryAvSyncEnterprise);
                MediaNativeInteropCommon.AppendValidationSummaryPassiveAvSync(
                    builder,
                    summaryPassiveAvSync);
                builder.AppendLine("summary_path=" + summaryPath);
                File.WriteAllText(summaryPath, builder.ToString(), Encoding.UTF8);
                Debug.Log("[CodexValidation] summary_written=" + summaryPath);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[CodexValidation] summary_write_failed " + ex.Message);
            }
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
            TrySyncVisibleSurfaceMaterial();
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

        private void TrySyncVisibleSurfaceMaterial()
        {
            if (Player == null || VideoSurface == null)
            {
                return;
            }

            if (_videoSurfaceRenderer == null)
            {
                _videoSurfaceRenderer = VideoSurface.GetComponent<Renderer>();
            }

            if (_videoSurfaceRenderer == null)
            {
                return;
            }

            if (_videoSurfaceRuntimeMaterial == null)
            {
                _videoSurfaceRuntimeMaterial = _videoSurfaceRenderer.material;
                var unlitTextureShader = Shader.Find("Unlit/Texture");
                if (unlitTextureShader != null
                    && _videoSurfaceRuntimeMaterial.shader != unlitTextureShader)
                {
                    _videoSurfaceRuntimeMaterial.shader = unlitTextureShader;
                }

                _videoSurfaceRuntimeMaterial.color = Color.white;
            }

            var targetTexture = Player.TargetMaterial != null
                ? Player.TargetMaterial.mainTexture
                : null;
            if (!ReferenceEquals(_videoSurfaceRuntimeMaterial.mainTexture, targetTexture))
            {
                _videoSurfaceRuntimeMaterial.mainTexture = targetTexture;
            }
        }

        private string TryReadOverrideValue(string prefix, string androidExtraName)
        {
            var argumentValue = TryReadStringArgument(prefix);
            if (!string.IsNullOrEmpty(argumentValue))
            {
                return argumentValue;
            }

            string androidExtraValue;
            return MediaSourceResolver.TryReadAndroidIntentStringExtra(
                androidExtraName,
                out androidExtraValue)
                ? androidExtraValue
                : string.Empty;
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

        private int TryReadIntArgument(
            string prefix,
            string androidExtraName,
            int fallback,
            out bool hasExplicitValue)
        {
            var value = TryReadOverrideValue(prefix, androidExtraName);
            hasExplicitValue = !string.IsNullOrEmpty(value);
            int parsed;
            if (string.IsNullOrEmpty(value) || !int.TryParse(value, out parsed) || parsed <= 0)
            {
                return fallback;
            }

            return parsed;
        }

        private float TryReadFloatArgument(
            string prefix,
            string androidExtraName,
            float fallback)
        {
            var value = TryReadOverrideValue(prefix, androidExtraName);
            float parsed;
            if (string.IsNullOrEmpty(value)
                || !float.TryParse(value, out parsed)
                || parsed <= 0f)
            {
                return fallback;
            }

            return parsed;
        }

        private bool TryReadBoolArgument(
            string prefix,
            string androidExtraName,
            bool fallback,
            out bool hasExplicitValue)
        {
            var value = TryReadOverrideValue(prefix, androidExtraName);
            hasExplicitValue = !string.IsNullOrEmpty(value);
            bool parsed;
            if (string.IsNullOrEmpty(value) || !bool.TryParse(value, out parsed))
            {
                return fallback;
            }

            return parsed;
        }

        private long TryReadLongArgument(
            string prefix,
            string androidExtraName,
            long fallback,
            out bool hasExplicitValue)
        {
            var value = TryReadOverrideValue(prefix, androidExtraName);
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
#elif UNITY_ANDROID
            if (Player != null)
            {
                try
                {
                    Player.Stop();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[CodexValidation] android_stop_after_summary_failed " + ex.Message);
                }
            }

            TryMoveAndroidValidationTaskToBack();
            enabled = false;
            yield break;
#else
            Application.Quit();
            Environment.Exit(exitCode);
#endif
        }

        private void TryMoveAndroidValidationTaskToBack()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                var unityPlayerClass = CreateAndroidJavaClass("com.unity3d.player.UnityPlayer");
                using (var unityPlayerDisposable = unityPlayerClass as IDisposable)
                {
                    var activity = AndroidJavaGetStaticObject(unityPlayerClass, "currentActivity");
                    using (var activityDisposable = activity as IDisposable)
                    {
                        if (activity == null)
                        {
                            Debug.LogWarning("[CodexValidation] android_move_task_to_back skipped currentActivity_unavailable");
                            return;
                        }

                        var moved = AndroidJavaCallBool(activity, "moveTaskToBack", true);
                        Debug.Log("[CodexValidation] android_move_task_to_back=" + moved);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[CodexValidation] android_move_task_to_back_failed " + ex.Message);
            }
#endif
        }

        private static object CreateAndroidJavaClass(string className)
        {
            var androidJavaClassType = Type.GetType(
                "UnityEngine.AndroidJavaClass, UnityEngine.AndroidJNIModule");
            return androidJavaClassType != null
                ? Activator.CreateInstance(androidJavaClassType, new object[] { className })
                : null;
        }

        private static Type GetAndroidJavaObjectRuntimeType()
        {
            return Type.GetType("UnityEngine.AndroidJavaObject, UnityEngine.AndroidJNIModule");
        }

        private static object AndroidJavaGetStaticObject(object javaClass, string fieldName)
        {
            if (javaClass == null)
            {
                return null;
            }

            var javaObjectType = GetAndroidJavaObjectRuntimeType();
            if (javaObjectType == null)
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
            method = method != null ? method.MakeGenericMethod(javaObjectType) : null;
            return method != null
                ? method.Invoke(javaClass, new object[] { fieldName })
                : null;
        }

        private static bool AndroidJavaCallBool(object javaObject, string methodName, params object[] args)
        {
            if (javaObject == null)
            {
                return false;
            }

            var methods = javaObject
                .GetType()
                .GetMethods()
                .Where(candidate =>
                    string.Equals(candidate.Name, "Call", StringComparison.Ordinal)
                    && candidate.IsGenericMethodDefinition);
            foreach (var candidate in methods)
            {
                var parameters = candidate.GetParameters();
                if (parameters.Length != 2)
                {
                    continue;
                }

                var typed = candidate.MakeGenericMethod(typeof(bool));
                var invokeArgs = new object[] { methodName, args ?? new object[0] };
                var result = typed.Invoke(javaObject, invokeArgs);
                return result is bool value && value;
            }

            return false;
        }

        private struct ValidationSnapshot
        {
            public double PlaybackTime;
            public bool HasTexture;
            public bool AudioPlaying;
            public bool Started;
            public int TextureWidth;
            public int TextureHeight;
            public bool HasRuntimeHealth;
            public int RuntimeStatePublic;
            public int RuntimeStateInternal;
            public int PlaybackIntent;
            public int StreamCount;
            public int VideoDecoderCount;
            public bool HasAudioDecoder;
            public string SourceState;
            public string SourcePackets;
            public string SourceTimeouts;
            public string SourceReconnects;
            public double SourceLastActivityAgeSec;
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
            public long PlaybackContractMasterTimeUs;
            public double PlaybackContractExternalTimeSec;
            public long PlaybackContractExternalTimeUs;
            public bool PlaybackContractHasAudioTimeSec;
            public double PlaybackContractAudioTimeSec;
            public bool PlaybackContractHasAudioTimeUs;
            public long PlaybackContractAudioTimeUs;
            public bool PlaybackContractHasAudioPresentedTimeSec;
            public double PlaybackContractAudioPresentedTimeSec;
            public bool PlaybackContractHasAudioPresentedTimeUs;
            public long PlaybackContractAudioPresentedTimeUs;
            public double PlaybackContractAudioSinkDelaySec;
            public long PlaybackContractAudioSinkDelayUs;
            public bool PlaybackContractHasMicrosecondMirror;
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
            public bool HasSourceTimelineContract;
            public string SourceTimelineModel;
            public string SourceTimelineAnchorKind;
            public bool SourceTimelineHasCurrentSourceTimeUs;
            public long SourceTimelineCurrentSourceTimeUs;
            public bool SourceTimelineHasTimelineOriginUs;
            public long SourceTimelineTimelineOriginUs;
            public bool SourceTimelineHasAnchorValueUs;
            public long SourceTimelineAnchorValueUs;
            public bool SourceTimelineHasAnchorMonoUs;
            public long SourceTimelineAnchorMonoUs;
            public bool SourceTimelineIsRealtime;
            public bool HasPlayerSessionContract;
            public string PlayerSessionLifecycleState;
            public int PlayerSessionPublicState;
            public int PlayerSessionRuntimeState;
            public int PlayerSessionPlaybackIntent;
            public int PlayerSessionStopReason;
            public string PlayerSessionSourceState;
            public bool PlayerSessionCanSeek;
            public bool PlayerSessionIsRealtime;
            public bool PlayerSessionIsBuffering;
            public bool PlayerSessionIsSyncing;
            public bool PlayerSessionAudioStartStateReported;
            public bool PlayerSessionShouldStartAudio;
            public int PlayerSessionAudioStartBlockReason;
            public int PlayerSessionRequiredBufferedSamples;
            public int PlayerSessionReportedBufferedSamples;
            public bool PlayerSessionRequiresPresentedVideoFrame;
            public bool PlayerSessionHasPresentedVideoFrame;
            public bool PlayerSessionAndroidFileRateBridgeActive;
            public bool HasAudioOutputPolicy;
            public int AudioOutputPolicyFileStartThresholdMs;
            public int AudioOutputPolicyAndroidFileStartThresholdMs;
            public int AudioOutputPolicyRealtimeStartThresholdMs;
            public int AudioOutputPolicyRealtimeStartupGraceMs;
            public int AudioOutputPolicyRealtimeStartupMinimumThresholdMs;
            public int AudioOutputPolicyFileRingCapacityMs;
            public int AudioOutputPolicyAndroidFileRingCapacityMs;
            public int AudioOutputPolicyRealtimeRingCapacityMs;
            public int AudioOutputPolicyFileBufferedCeilingMs;
            public int AudioOutputPolicyAndroidFileBufferedCeilingMs;
            public int AudioOutputPolicyRealtimeBufferedCeilingMs;
            public int AudioOutputPolicyRealtimeStartupAdditionalSinkDelayMs;
            public int AudioOutputPolicyRealtimeSteadyAdditionalSinkDelayMs;
            public int AudioOutputPolicyRealtimeBackendAdditionalSinkDelayMs;
            public bool AudioOutputPolicyRealtimeStartRequiresVideoFrame;
            public bool AudioOutputPolicyAllowAndroidFileOutputRateBridge;
            public bool HasAvSyncEnterpriseMetrics;
            public uint AvSyncEnterpriseSampleCount;
            public long AvSyncEnterpriseWindowSpanUs;
            public long AvSyncEnterpriseLatestRawOffsetUs;
            public long AvSyncEnterpriseLatestSmoothOffsetUs;
            public double AvSyncEnterpriseDriftSlopePpm;
            public double AvSyncEnterpriseDriftProjected2hMs;
            public long AvSyncEnterpriseOffsetAbsP95Us;
            public long AvSyncEnterpriseOffsetAbsP99Us;
            public long AvSyncEnterpriseOffsetAbsMaxUs;
            public bool HasPassiveAvSyncSnapshot;
            public long PassiveAvSyncRawOffsetUs;
            public long PassiveAvSyncSmoothOffsetUs;
            public double PassiveAvSyncDriftPpm;
            public long PassiveAvSyncDriftInterceptUs;
            public uint PassiveAvSyncDriftSampleCount;
            public string PassiveAvSyncVideoSchedule;
            public double PassiveAvSyncAudioResampleRatio;
            public bool PassiveAvSyncAudioResampleActive;
            public bool PassiveAvSyncShouldRebuildAnchor;
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

        private struct ValidationResultInfo
        {
            public bool Passed;
            public string Reason;
            public double PlaybackAdvanceSeconds;

            public static ValidationResultInfo Failed(string reason, double playbackAdvanceSeconds)
            {
                return new ValidationResultInfo
                {
                    Passed = false,
                    Reason = reason,
                    PlaybackAdvanceSeconds = playbackAdvanceSeconds
                };
            }

            public static ValidationResultInfo PassedWithAdvance(double playbackAdvanceSeconds)
            {
                return new ValidationResultInfo
                {
                    Passed = true,
                    Reason = "steady-playback",
                    PlaybackAdvanceSeconds = playbackAdvanceSeconds
                };
            }
        }
    }
}
