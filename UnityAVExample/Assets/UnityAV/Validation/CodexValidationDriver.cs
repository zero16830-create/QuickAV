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
        private const int PreviewTargetFrameRate = 120;
        private const string ValidationLogPrefix = "CodexValidation";

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
        private bool _observedNativeFrameDuringWindow;
        private bool _hasValidationWindowSnapshot;
        private ValidationSnapshot _lastValidationWindowSnapshot;
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

            var overrideUri = MediaSourceResolver.TryReadOverrideValue(
                UriArgumentName,
                AndroidUriExtraName);
            if (!string.IsNullOrEmpty(overrideUri))
            {
                Player.Uri = overrideUri;
                Debug.Log(
                    MediaNativeInteropCommon.CreateOverrideUriLogLine(
                        ValidationLogPrefix,
                        overrideUri));
            }

            var overrideBackend = MediaSourceResolver.TryReadOverrideValue(
                BackendArgumentName,
                AndroidBackendExtraName);
            MediaBackendKind parsedBackend;
            if (MediaSourceResolver.TryParseBackendKind(overrideBackend, out parsedBackend))
            {
                Player.PreferredBackend = parsedBackend;
                Player.StrictBackend = parsedBackend != MediaBackendKind.Auto;
                Debug.Log(
                    MediaNativeInteropCommon.CreateOverrideBackendLogLine(
                        ValidationLogPrefix,
                        parsedBackend.ToString(),
                        Player.StrictBackend));
            }
            else if (!string.IsNullOrEmpty(overrideBackend))
            {
                Debug.LogWarning(
                    MediaNativeInteropCommon.CreateIgnoreUnknownBackendLogLine(
                        ValidationLogPrefix,
                        overrideBackend));
            }

            var overrideVideoRenderer = MediaSourceResolver.TryReadOverrideValue(
                VideoRendererArgumentName,
                AndroidVideoRendererExtraName);
            MediaPlayerPull.PullVideoRendererKind parsedVideoRenderer;
            if (MediaSourceResolver.TryParsePullVideoRendererKind(
                    overrideVideoRenderer,
                    out parsedVideoRenderer))
            {
                Player.VideoRenderer = parsedVideoRenderer;
                Debug.Log(
                    MediaNativeInteropCommon.CreateOverrideVideoRendererLogLine(
                        ValidationLogPrefix,
                        parsedVideoRenderer.ToString()));
            }
            else if (!string.IsNullOrEmpty(overrideVideoRenderer))
            {
                Debug.LogWarning(
                    MediaNativeInteropCommon.CreateIgnoreUnknownVideoRendererLogLine(
                        ValidationLogPrefix,
                        overrideVideoRenderer));
            }

            bool hasExplicitLoopValue;
            Player.Loop = MediaSourceResolver.TryReadBoolOverride(
                LoopArgumentName,
                AndroidLoopExtraName,
                Player.Loop,
                out hasExplicitLoopValue);
            if (hasExplicitLoopValue)
            {
                Debug.Log(
                    MediaNativeInteropCommon.CreateOverrideLoopLogLine(
                        ValidationLogPrefix,
                        Player.Loop));
            }

            ValidationSeconds = MediaSourceResolver.TryReadPositiveFloatOverride(
                ValidationSecondsArgumentName,
                AndroidValidationSecondsExtraName,
                ValidationSeconds);
            StartupTimeoutSeconds = MediaSourceResolver.TryReadPositiveFloatOverride(
                StartupTimeoutSecondsArgumentName,
                AndroidStartupTimeoutSecondsExtraName,
                StartupTimeoutSeconds);
            bool hasExplicitRequireAudioOutput;
            RequireAudioOutput = MediaSourceResolver.TryReadBoolOverride(
                RequireAudioOutputArgumentName,
                AndroidRequireAudioOutputExtraName,
                RequireAudioOutput,
                out hasExplicitRequireAudioOutput);
            _publisherStartUnixMs = MediaSourceResolver.TryReadLongOverride(
                PublisherStartUnixMsArgumentName,
                AndroidPublisherStartUnixMsExtraName,
                -1L,
                out _hasPublisherStartUnixMs);

            _requestedWindowWidth = MediaSourceResolver.TryReadPositiveIntOverride(
                WindowWidthArgumentName,
                AndroidWindowWidthExtraName,
                Player.Width,
                out _hasExplicitWindowWidth);
            _requestedWindowHeight = MediaSourceResolver.TryReadPositiveIntOverride(
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
            Debug.Log(
                MediaNativeInteropCommon.CreateValidationStartEnterLogLine(
                    ValidationLogPrefix));
            if (Player == null)
            {
                Player = GetComponent<MediaPlayerPull>();
            }

            if (Player == null)
            {
                Debug.LogError(
                    MediaNativeInteropCommon.CreateMissingComponentLogLine(
                        ValidationLogPrefix,
                        "MediaPlayerPull"));
                StartCoroutine(QuitAfterDelay(1f, 2));
                return;
            }

            // 场景验证需要在无人值守运行时维持稳定时钟，避免失焦后被系统节流。
            Application.runInBackground = true;
            Debug.Log(
                MediaNativeInteropCommon.CreateRunInBackgroundEnabledLogLine(
                    ValidationLogPrefix));
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = PreviewTargetFrameRate;
            Debug.Log(
                MediaNativeInteropCommon.CreateFramePacingLogLine(
                    ValidationLogPrefix,
                    Application.targetFrameRate,
                    QualitySettings.vSyncCount));

            _lastLogTime = Time.realtimeSinceStartup;
            _startTime = _lastLogTime;
            Debug.Log(
                MediaNativeInteropCommon.CreateValidationStartLogLine(
                    ValidationLogPrefix,
                    ValidationSeconds,
                    Player.Width,
                    Player.Height,
                    MediaSourceResolver.HasExplicitWindowOverride(
                        _hasExplicitWindowWidth,
                        _hasExplicitWindowHeight),
                    Player.VideoRenderer.ToString(),
                    RequireAudioOutput));

            if (MediaSourceResolver.HasExplicitWindowOverride(
                    _hasExplicitWindowWidth,
                    _hasExplicitWindowHeight))
            {
                MediaNativeInteropCommon.ConfigureValidationWindowAndView(
                    ValidationLogPrefix,
                    ForceWindowedMode,
                    VideoSurface,
                    ValidationCamera,
                    Player.Width,
                    Player.Height,
                    "explicit-override");
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
                    var audioGatePolicy = snapshot.AudioGatePolicy;
                    var validationGateInputs =
                        CreateValidationGateInputs(
                            snapshot,
                            audioGatePolicy);
                    var validationWindowStartObservation =
                        MediaNativeInteropCommon.CreatePullValidationWindowStartObservation(
                            validationGateInputs,
                            startupElapsed,
                            StartupTimeoutSeconds);
                    if (validationWindowStartObservation.ShouldStart)
                    {
                        StartValidationWindow(
                            now,
                            startupElapsed,
                            validationWindowStartObservation.Reason,
                            snapshot.ValidationGatePlaybackTimeSec);
                        snapshot = RecordValidationObservation(snapshot);
                    }
                }
                else
                {
                    snapshot = RecordValidationObservation(snapshot);
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
            MediaNativeInteropCommon.EmitPullValidationStatusLogs(
                Debug.Log,
                ValidationLogPrefix,
                CreateEmitStatusView(snapshot));

            return snapshot;
        }

        private MediaNativeInteropCommon.PullValidationEmitStatusView
            CreateEmitStatusView(ValidationSnapshot snapshot)
        {
            return new MediaNativeInteropCommon.PullValidationEmitStatusView
            {
                PlaybackTime = snapshot.PlaybackTime,
                HasTexture = snapshot.HasTexture,
                AudioPlaying = snapshot.AudioPlaying,
                Started = snapshot.Started,
                StartupElapsedSeconds = snapshot.StartupElapsedSeconds,
                SourceState = snapshot.SourceState,
                SourcePackets = snapshot.SourcePackets,
                SourceTimeouts = snapshot.SourceTimeouts,
                SourceReconnects = snapshot.SourceReconnects,
                WindowWidth = Screen.width,
                WindowHeight = Screen.height,
                TextureWidth = snapshot.TextureWidth,
                TextureHeight = snapshot.TextureHeight,
                Fullscreen = Screen.fullScreen,
                FullscreenMode = Screen.fullScreenMode,
                ActualBackend = snapshot.ActualBackend,
                RequestedVideoRenderer = snapshot.RequestedVideoRenderer,
                ActualVideoRenderer = snapshot.ActualVideoRenderer,
                HasFrameContract = snapshot.HasFrameContract,
                FrameContractMemoryKind = snapshot.FrameContractMemoryKind,
                FrameContractDynamicRange = snapshot.FrameContractDynamicRange,
                FrameContractNominalFps = snapshot.FrameContractNominalFps,
                HasPlaybackTimingContract = snapshot.HasPlaybackTimingContract,
                PlaybackContractMasterTimeSec = snapshot.PlaybackContractMasterTimeSec,
                PlaybackContractMasterTimeUs = snapshot.PlaybackContractMasterTimeUs,
                PlaybackContractExternalTimeSec = snapshot.PlaybackContractExternalTimeSec,
                PlaybackContractExternalTimeUs = snapshot.PlaybackContractExternalTimeUs,
                PlaybackContractHasAudioTimeSec = snapshot.PlaybackContractHasAudioTimeSec,
                PlaybackContractAudioTimeSec = snapshot.PlaybackContractAudioTimeSec,
                PlaybackContractHasAudioTimeUs = snapshot.PlaybackContractHasAudioTimeUs,
                PlaybackContractAudioTimeUs = snapshot.PlaybackContractAudioTimeUs,
                PlaybackContractHasAudioPresentedTimeSec =
                    snapshot.PlaybackContractHasAudioPresentedTimeSec,
                PlaybackContractAudioPresentedTimeSec =
                    snapshot.PlaybackContractAudioPresentedTimeSec,
                PlaybackContractHasAudioPresentedTimeUs =
                    snapshot.PlaybackContractHasAudioPresentedTimeUs,
                PlaybackContractAudioPresentedTimeUs =
                    snapshot.PlaybackContractAudioPresentedTimeUs,
                PlaybackContractAudioSinkDelaySec =
                    snapshot.PlaybackContractAudioSinkDelaySec,
                PlaybackContractAudioSinkDelayUs =
                    snapshot.PlaybackContractAudioSinkDelayUs,
                PlaybackContractHasMicrosecondMirror =
                    snapshot.PlaybackContractHasMicrosecondMirror,
                PlaybackContractHasAudioClock = snapshot.PlaybackContractHasAudioClock,
                HasAvSyncContract = snapshot.HasAvSyncContract,
                AvSyncContractMasterClock = snapshot.AvSyncContractMasterClock,
                AvSyncContractHasAudioClockSec = snapshot.AvSyncContractHasAudioClockSec,
                AvSyncContractAudioClockSec = snapshot.AvSyncContractAudioClockSec,
                AvSyncContractHasVideoClockSec = snapshot.AvSyncContractHasVideoClockSec,
                AvSyncContractVideoClockSec = snapshot.AvSyncContractVideoClockSec,
                AvSyncContractClockDeltaMs = snapshot.AvSyncContractClockDeltaMs,
                AvSyncContractDriftMs = snapshot.AvSyncContractDriftMs,
                AvSyncContractStartupWarmupComplete =
                    snapshot.AvSyncContractStartupWarmupComplete,
                AvSyncContractDropTotal = snapshot.AvSyncContractDropTotal,
                AvSyncContractDuplicateTotal = snapshot.AvSyncContractDuplicateTotal,
                HasSourceTimelineContract = snapshot.HasSourceTimelineContract,
                SourceTimelineModel = snapshot.SourceTimelineModel,
                SourceTimelineAnchorKind = snapshot.SourceTimelineAnchorKind,
                SourceTimelineHasCurrentSourceTimeUs =
                    snapshot.SourceTimelineHasCurrentSourceTimeUs,
                SourceTimelineCurrentSourceTimeUs =
                    snapshot.SourceTimelineCurrentSourceTimeUs,
                SourceTimelineHasTimelineOriginUs =
                    snapshot.SourceTimelineHasTimelineOriginUs,
                SourceTimelineTimelineOriginUs = snapshot.SourceTimelineTimelineOriginUs,
                SourceTimelineHasAnchorValueUs = snapshot.SourceTimelineHasAnchorValueUs,
                SourceTimelineAnchorValueUs = snapshot.SourceTimelineAnchorValueUs,
                SourceTimelineHasAnchorMonoUs = snapshot.SourceTimelineHasAnchorMonoUs,
                SourceTimelineAnchorMonoUs = snapshot.SourceTimelineAnchorMonoUs,
                SourceTimelineIsRealtime = snapshot.SourceTimelineIsRealtime,
                HasPlayerSessionContract = snapshot.HasPlayerSessionContract,
                PlayerSessionLifecycleState = snapshot.PlayerSessionLifecycleState,
                PlayerSessionPublicState = snapshot.PlayerSessionPublicState,
                PlayerSessionRuntimeState = snapshot.PlayerSessionRuntimeState,
                PlayerSessionPlaybackIntent = snapshot.PlayerSessionPlaybackIntent,
                PlayerSessionStopReason = snapshot.PlayerSessionStopReason,
                PlayerSessionSourceState = snapshot.PlayerSessionSourceState,
                PlayerSessionCanSeek = snapshot.PlayerSessionCanSeek,
                PlayerSessionIsRealtime = snapshot.PlayerSessionIsRealtime,
                PlayerSessionIsBuffering = snapshot.PlayerSessionIsBuffering,
                PlayerSessionIsSyncing = snapshot.PlayerSessionIsSyncing,
                HasAudioOutputPolicy = snapshot.HasAudioOutputPolicy,
                AudioOutputPolicyFileStartThresholdMs =
                    snapshot.AudioOutputPolicyFileStartThresholdMs,
                AudioOutputPolicyAndroidFileStartThresholdMs =
                    snapshot.AudioOutputPolicyAndroidFileStartThresholdMs,
                AudioOutputPolicyRealtimeStartThresholdMs =
                    snapshot.AudioOutputPolicyRealtimeStartThresholdMs,
                AudioOutputPolicyRealtimeStartupGraceMs =
                    snapshot.AudioOutputPolicyRealtimeStartupGraceMs,
                AudioOutputPolicyRealtimeStartupMinimumThresholdMs =
                    snapshot.AudioOutputPolicyRealtimeStartupMinimumThresholdMs,
                AudioOutputPolicyFileRingCapacityMs =
                    snapshot.AudioOutputPolicyFileRingCapacityMs,
                AudioOutputPolicyAndroidFileRingCapacityMs =
                    snapshot.AudioOutputPolicyAndroidFileRingCapacityMs,
                AudioOutputPolicyRealtimeRingCapacityMs =
                    snapshot.AudioOutputPolicyRealtimeRingCapacityMs,
                AudioOutputPolicyFileBufferedCeilingMs =
                    snapshot.AudioOutputPolicyFileBufferedCeilingMs,
                AudioOutputPolicyAndroidFileBufferedCeilingMs =
                    snapshot.AudioOutputPolicyAndroidFileBufferedCeilingMs,
                AudioOutputPolicyRealtimeBufferedCeilingMs =
                    snapshot.AudioOutputPolicyRealtimeBufferedCeilingMs,
                AudioOutputPolicyRealtimeStartupAdditionalSinkDelayMs =
                    snapshot.AudioOutputPolicyRealtimeStartupAdditionalSinkDelayMs,
                AudioOutputPolicyRealtimeSteadyAdditionalSinkDelayMs =
                    snapshot.AudioOutputPolicyRealtimeSteadyAdditionalSinkDelayMs,
                AudioOutputPolicyRealtimeBackendAdditionalSinkDelayMs =
                    snapshot.AudioOutputPolicyRealtimeBackendAdditionalSinkDelayMs,
                AudioOutputPolicyRealtimeStartRequiresVideoFrame =
                    snapshot.AudioOutputPolicyRealtimeStartRequiresVideoFrame,
                AudioOutputPolicyAllowAndroidFileOutputRateBridge =
                    snapshot.AudioOutputPolicyAllowAndroidFileOutputRateBridge,
                HasAvSyncEnterpriseMetrics = snapshot.HasAvSyncEnterpriseMetrics,
                AvSyncEnterpriseSampleCount = snapshot.AvSyncEnterpriseSampleCount,
                AvSyncEnterpriseWindowSpanUs = snapshot.AvSyncEnterpriseWindowSpanUs,
                AvSyncEnterpriseLatestRawOffsetUs =
                    snapshot.AvSyncEnterpriseLatestRawOffsetUs,
                AvSyncEnterpriseLatestSmoothOffsetUs =
                    snapshot.AvSyncEnterpriseLatestSmoothOffsetUs,
                AvSyncEnterpriseDriftSlopePpm = snapshot.AvSyncEnterpriseDriftSlopePpm,
                AvSyncEnterpriseDriftProjected2hMs =
                    snapshot.AvSyncEnterpriseDriftProjected2hMs,
                AvSyncEnterpriseOffsetAbsP95Us = snapshot.AvSyncEnterpriseOffsetAbsP95Us,
                AvSyncEnterpriseOffsetAbsP99Us = snapshot.AvSyncEnterpriseOffsetAbsP99Us,
                AvSyncEnterpriseOffsetAbsMaxUs = snapshot.AvSyncEnterpriseOffsetAbsMaxUs,
                HasPassiveAvSyncSnapshot = snapshot.HasPassiveAvSyncSnapshot,
                PassiveAvSyncRawOffsetUs = snapshot.PassiveAvSyncRawOffsetUs,
                PassiveAvSyncSmoothOffsetUs = snapshot.PassiveAvSyncSmoothOffsetUs,
                PassiveAvSyncDriftPpm = snapshot.PassiveAvSyncDriftPpm,
                PassiveAvSyncDriftInterceptUs = snapshot.PassiveAvSyncDriftInterceptUs,
                PassiveAvSyncDriftSampleCount = snapshot.PassiveAvSyncDriftSampleCount,
                PassiveAvSyncVideoSchedule = snapshot.PassiveAvSyncVideoSchedule,
                PassiveAvSyncAudioResampleRatio =
                    snapshot.PassiveAvSyncAudioResampleRatio,
                PassiveAvSyncAudioResampleActive =
                    snapshot.PassiveAvSyncAudioResampleActive,
                PassiveAvSyncShouldRebuildAnchor =
                    snapshot.PassiveAvSyncShouldRebuildAnchor,
                HasRuntimeHealth = snapshot.HasRuntimeHealth,
                RuntimeStatePublic = snapshot.RuntimeStatePublic,
                RuntimeStateInternal = snapshot.RuntimeStateInternal,
                PlaybackIntent = snapshot.PlaybackIntent,
                StreamCount = snapshot.StreamCount,
                VideoDecoderCount = snapshot.VideoDecoderCount,
                HasAudioDecoder = snapshot.HasAudioDecoder,
                SourceLastActivityAgeSec = snapshot.SourceLastActivityAgeSec,
                HasWgpuRenderDescriptor = snapshot.HasWgpuRenderDescriptor,
                WgpuRuntimeReady = snapshot.WgpuRuntimeReady,
                WgpuOutputWidth = snapshot.WgpuOutputWidth,
                WgpuOutputHeight = snapshot.WgpuOutputHeight,
                WgpuSupportsYuv420p = snapshot.WgpuSupportsYuv420p,
                WgpuSupportsNv12 = snapshot.WgpuSupportsNv12,
                WgpuSupportsP010 = snapshot.WgpuSupportsP010,
                WgpuSupportsRgba32 = snapshot.WgpuSupportsRgba32,
                WgpuSupportsExternalTextureRgba =
                    snapshot.WgpuSupportsExternalTextureRgba,
                WgpuSupportsExternalTextureYu12 =
                    snapshot.WgpuSupportsExternalTextureYu12,
                WgpuReadbackExportSupported = snapshot.WgpuReadbackExportSupported,
                HasWgpuRenderState = snapshot.HasWgpuRenderState,
                WgpuRenderPath = snapshot.WgpuRenderPath,
                WgpuSourceMemoryKind = snapshot.WgpuSourceMemoryKind,
                WgpuPresentedMemoryKind = snapshot.WgpuPresentedMemoryKind,
                WgpuSourcePixelFormat = snapshot.WgpuSourcePixelFormat,
                WgpuPresentedPixelFormat = snapshot.WgpuPresentedPixelFormat,
                WgpuExternalTextureFormat = snapshot.WgpuExternalTextureFormat,
                WgpuHasRenderedFrame = snapshot.WgpuHasRenderedFrame,
                WgpuRenderedFrameIndex = snapshot.WgpuRenderedFrameIndex,
                WgpuRenderedTimeSec = snapshot.WgpuRenderedTimeSec,
                WgpuHasRenderError = snapshot.WgpuHasRenderError,
                WgpuRenderErrorKind = snapshot.WgpuRenderErrorKind,
                WgpuUploadPlaneCount = snapshot.WgpuUploadPlaneCount,
                WgpuSourceZeroCopy = snapshot.WgpuSourceZeroCopy,
                WgpuCpuFallback = snapshot.WgpuCpuFallback,
                HasAvSyncSample = snapshot.HasAvSyncSample,
                AvSyncDeltaMilliseconds = snapshot.AvSyncDeltaMilliseconds,
                AudioPresentedTimeSec = snapshot.AudioPresentedTimeSec,
                ReferencePlaybackTimeSec = snapshot.ReferencePlaybackTimeSec,
                ReferencePlaybackKind = snapshot.ReferencePlaybackKind,
                PresentedVideoTimeSec = snapshot.PresentedVideoTimeSec,
                AudioPipelineDelaySec = snapshot.AudioPipelineDelaySec,
                HasRealtimeLatencySample = snapshot.HasRealtimeLatencySample,
                RealtimeLatencyMilliseconds = snapshot.RealtimeLatencyMilliseconds,
                PublisherElapsedTimeSec = snapshot.PublisherElapsedTimeSec,
                RealtimeReferenceTimeSec = snapshot.RealtimeReferenceTimeSec,
                HasRealtimeProbeSample = snapshot.HasRealtimeProbeSample,
                RealtimeProbeUnixMs = snapshot.RealtimeProbeUnixMs,
                HasBridgeDescriptor = snapshot.HasBridgeDescriptor,
                BridgeDescriptorState = snapshot.BridgeDescriptorState,
                BridgeDescriptorRuntimeKind = snapshot.BridgeDescriptorRuntimeKind,
                BridgeDescriptorZeroCopySupported =
                    snapshot.BridgeDescriptorZeroCopySupported,
                BridgeDescriptorDirectBindable = snapshot.BridgeDescriptorDirectBindable,
                BridgeDescriptorSourcePlaneTexturesSupported =
                    snapshot.BridgeDescriptorSourcePlaneTexturesSupported,
                BridgeDescriptorFallbackCopyPath =
                    snapshot.BridgeDescriptorFallbackCopyPath,
                HasPathSelection = snapshot.HasPathSelection,
                PathSelectionKind = snapshot.PathSelectionKind,
                PathSelectionSourceMemoryKind = snapshot.PathSelectionSourceMemoryKind,
                PathSelectionPresentedMemoryKind =
                    snapshot.PathSelectionPresentedMemoryKind,
                PathSelectionTargetZeroCopy = snapshot.PathSelectionTargetZeroCopy,
                PathSelectionSourcePlaneTexturesSupported =
                    snapshot.PathSelectionSourcePlaneTexturesSupported,
                PathSelectionCpuFallback = snapshot.PathSelectionCpuFallback,
            };
        }

        private ValidationSnapshot CaptureSnapshot()
        {
            TrySyncVisibleSurfaceMaterial();
            MediaNativeInteropCommon.PlaybackTimingContractView playbackTimingContract;
            var hasPlaybackTimingContract = Player.TryGetPlaybackTimingContract(
                out playbackTimingContract);
            double playbackTime;
            if (!MediaNativeInteropCommon.TryResolvePlaybackTimingContractTime(
                    hasPlaybackTimingContract,
                    playbackTimingContract,
                    MediaNativeInteropCommon.PlaybackTimingPreference.ExternalThenMaster,
                    out playbackTime))
            {
                playbackTime = SafeReadPlaybackTime();
            }
            var audioPlaybackObservation =
                MediaNativeInteropCommon.CreateValidationAudioPlaybackObservation(
                    Player);
            double audioPresentedTimeSec;
            double audioPipelineDelaySec;
            var hasAudioPresentation = Player.TryGetEstimatedAudioPresentation(
                out audioPresentedTimeSec,
                out audioPipelineDelaySec);

            MediaPlayerPull.PlayerRuntimeHealth health;
            var hasHealth = Player.TryGetRuntimeHealth(out health);
            var runtimeHealthObservation =
                MediaNativeInteropCommon.CreateRuntimeHealthObservation(
                    hasHealth,
                    health);
            double presentedVideoTimeSec;
            var hasPresentedVideoTime = Player.TryGetPresentedVideoTimeSec(out presentedVideoTimeSec);
            var referencePlaybackObservation =
                MediaNativeInteropCommon.CreateReferencePlaybackObservation(
                    playbackTime,
                    hasPresentedVideoTime,
                    presentedVideoTimeSec,
                    runtimeHealthObservation,
                    RealtimeReferenceLagToleranceSeconds);

            var referencePlaybackTime = referencePlaybackObservation.ReferenceTimeSec;
            var referencePlaybackKind = referencePlaybackObservation.ReferenceKind;
            var hasAvSyncSample = hasAudioPresentation && referencePlaybackObservation.HasSample;
            var avSyncDeltaMilliseconds = hasAvSyncSample
                ? (audioPresentedTimeSec - referencePlaybackTime) * 1000.0
                : 0.0;
            var playbackStartObservation =
                MediaNativeInteropCommon.CreatePullPlaybackStartObservation(
                    Player,
                    runtimeHealthObservation,
                    playbackTime);
            MediaNativeInteropCommon.VideoFrameContractView frameContract;
            var hasFrameContract = Player.TryGetLatestVideoFrameContract(out frameContract);
            var frameContractObservation =
                MediaNativeInteropCommon.CreateVideoFrameObservation(
                    hasFrameContract,
                    frameContract);
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
            var sourceTimelineProjection =
                MediaNativeInteropCommon.CreateValidationSourceTimelineProjection(
                    sourceTimelineObservation);
            MediaNativeInteropCommon.PlayerSessionContractView playerSessionContract;
            var hasPlayerSessionContract = Player.TryGetPlayerSessionContract(
                out playerSessionContract);
            var playerSessionObservation =
                MediaNativeInteropCommon.CreatePlayerSessionObservation(
                    hasPlayerSessionContract,
                    playerSessionContract);
            var playerSessionProjection =
                MediaNativeInteropCommon.CreateValidationPlayerSessionProjection(
                    playerSessionObservation);
            var audioStartRuntimeCommand =
                MediaNativeInteropCommon.ResolveAudioStartRuntimeCommand(
                    hasPlayerSessionContract,
                    playerSessionContract);
            var currentValidationAudioGatePolicy =
                CreateValidationAudioGatePolicy(
                    audioStartRuntimeCommand);
            MediaNativeInteropCommon.AudioOutputPolicyView audioOutputPolicy;
            var hasAudioOutputPolicy = Player.TryGetAudioOutputPolicy(out audioOutputPolicy);
            var audioOutputPolicyObservation =
                MediaNativeInteropCommon.CreateAudioOutputPolicyObservation(
                    hasAudioOutputPolicy,
                    audioOutputPolicy);
            var audioOutputPolicyProjection =
                MediaNativeInteropCommon.CreateValidationAudioOutputPolicyProjection(
                    audioOutputPolicyObservation);
            MediaNativeInteropCommon.AvSyncEnterpriseMetricsView avSyncEnterpriseMetrics;
            var hasAvSyncEnterpriseMetrics = Player.TryGetAvSyncEnterpriseMetrics(
                out avSyncEnterpriseMetrics);
            var avSyncEnterpriseObservation =
                MediaNativeInteropCommon.CreateAvSyncEnterpriseObservation(
                    hasAvSyncEnterpriseMetrics,
                    avSyncEnterpriseMetrics);
            var avSyncEnterpriseProjection =
                MediaNativeInteropCommon.CreateValidationAvSyncEnterpriseProjection(
                    avSyncEnterpriseObservation);
            MediaNativeInteropCommon.PassiveAvSyncSnapshotView passiveAvSyncSnapshot;
            var hasPassiveAvSyncSnapshot = Player.TryGetPassiveAvSyncSnapshot(
                out passiveAvSyncSnapshot);
            var passiveAvSyncObservation =
                MediaNativeInteropCommon.CreatePassiveAvSyncObservation(
                    hasPassiveAvSyncSnapshot,
                    passiveAvSyncSnapshot);
            var passiveAvSyncProjection =
                MediaNativeInteropCommon.CreateValidationPassiveAvSyncProjection(
                    passiveAvSyncObservation);
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
            var hasPresentedNativeVideoFrame =
                MediaNativeInteropCommon.ResolveHasPresentedNativeVideoFrame(
                    hasPlayerSessionContract,
                    playerSessionContract,
                    hasPathSelection,
                    pathSelection);
            var textureObservation =
                MediaNativeInteropCommon.CreatePullValidationVideoTextureObservation(
                    hasPresentedNativeVideoFrame,
                    Player != null ? Player.TargetMaterial : null);
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
            var backendRuntimeObservation =
                MediaNativeInteropCommon.CreatePullBackendRuntimeObservation(Player);
            var realtimeProbeObservation =
                MediaNativeInteropCommon.CreateRealtimeProbeObservation(
                    runtimeHealthObservation,
                    referencePlaybackTime,
                    _hasPublisherStartUnixMs,
                    _publisherStartUnixMs);
            var validationGatePlaybackTimeSec =
                MediaNativeInteropCommon.ResolveValidationGatePlaybackTime(
                    playbackTime,
                    referencePlaybackTime);
            var playbackContractProjection =
                MediaNativeInteropCommon.CreateValidationPlaybackContractProjection(
                    playbackTimingObservation);
            var avSyncContractProjection =
                MediaNativeInteropCommon.CreateValidationAvSyncContractProjection(
                    avSyncContractObservation);
            var bridgeDescriptorProjection =
                MediaNativeInteropCommon.CreateValidationSummaryBridgeDescriptor(
                    bridgeDescriptorObservation);
            var pathSelectionProjection =
                MediaNativeInteropCommon.CreateValidationSummaryPathSelectionExtended(
                    pathSelectionObservation);

            return new ValidationSnapshot
            {
                Uri = Player != null ? Player.Uri : string.Empty,
                RequestedBackend = backendRuntimeObservation.RequestedBackend,
                ActualBackend = backendRuntimeObservation.ActualBackend,
                RequestedVideoRenderer = backendRuntimeObservation.RequestedVideoRenderer,
                ActualVideoRenderer = backendRuntimeObservation.ActualVideoRenderer,
                StartupElapsedSeconds = Player.StartupElapsedSeconds,
                SessionId = Player != null ? Player.SessionId : -1,
                AudioSampleRate = Player != null ? Player.AudioSampleRate : 0,
                AudioChannels = Player != null ? Player.AudioChannels : 0,
                PlaybackTime = playbackTime,
                ValidationGatePlaybackTimeSec = validationGatePlaybackTimeSec,
                AudioStartRuntimeCommand = audioStartRuntimeCommand,
                AudioGatePolicy = currentValidationAudioGatePolicy,
                HasTexture = textureObservation.HasTexture,
                AudioPlaying = audioPlaybackObservation.Playing,
                Started = playbackStartObservation.Started,
                HasPresentedNativeVideoFrame = hasPresentedNativeVideoFrame,
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
                HasPlaybackTimingContract = playbackContractProjection.Available,
                PlaybackContractMasterTimeSec = playbackContractProjection.MasterTimeSec,
                PlaybackContractMasterTimeUs = playbackContractProjection.MasterTimeUs,
                PlaybackContractExternalTimeSec = playbackContractProjection.ExternalTimeSec,
                PlaybackContractExternalTimeUs = playbackContractProjection.ExternalTimeUs,
                PlaybackContractHasAudioTimeSec = playbackContractProjection.HasAudioTimeSec,
                PlaybackContractAudioTimeSec = playbackContractProjection.AudioTimeSec,
                PlaybackContractHasAudioTimeUs = playbackContractProjection.HasAudioTimeUs,
                PlaybackContractAudioTimeUs = playbackContractProjection.AudioTimeUs,
                PlaybackContractHasAudioPresentedTimeSec =
                    playbackContractProjection.HasAudioPresentedTimeSec,
                PlaybackContractAudioPresentedTimeSec =
                    playbackContractProjection.AudioPresentedTimeSec,
                PlaybackContractHasAudioPresentedTimeUs =
                    playbackContractProjection.HasAudioPresentedTimeUs,
                PlaybackContractAudioPresentedTimeUs =
                    playbackContractProjection.AudioPresentedTimeUs,
                PlaybackContractAudioSinkDelaySec =
                    playbackContractProjection.AudioSinkDelaySec,
                PlaybackContractAudioSinkDelayUs =
                    playbackContractProjection.AudioSinkDelayUs,
                PlaybackContractHasMicrosecondMirror =
                    playbackContractProjection.HasMicrosecondMirror,
                PlaybackContractHasAudioClock =
                    playbackContractProjection.HasAudioClock,
                HasAvSyncContract = avSyncContractProjection.Available,
                AvSyncContractMasterClock = avSyncContractProjection.MasterClock,
                AvSyncContractHasAudioClockSec = avSyncContractProjection.HasAudioClockSec,
                AvSyncContractAudioClockSec = avSyncContractProjection.AudioClockSec,
                AvSyncContractHasVideoClockSec = avSyncContractProjection.HasVideoClockSec,
                AvSyncContractVideoClockSec = avSyncContractProjection.VideoClockSec,
                AvSyncContractClockDeltaMs = avSyncContractProjection.ClockDeltaMs,
                AvSyncContractDriftMs = avSyncContractProjection.DriftMs,
                AvSyncContractStartupWarmupComplete =
                    avSyncContractProjection.StartupWarmupComplete,
                AvSyncContractDropTotal = avSyncContractProjection.DropTotal,
                AvSyncContractDuplicateTotal = avSyncContractProjection.DuplicateTotal,
                HasSourceTimelineContract = sourceTimelineProjection.Available,
                SourceTimelineModel = sourceTimelineProjection.Model,
                SourceTimelineAnchorKind = sourceTimelineProjection.AnchorKind,
                SourceTimelineHasCurrentSourceTimeUs =
                    sourceTimelineProjection.HasCurrentSourceTimeUs,
                SourceTimelineCurrentSourceTimeUs =
                    sourceTimelineProjection.CurrentSourceTimeUs,
                SourceTimelineHasTimelineOriginUs =
                    sourceTimelineProjection.HasTimelineOriginUs,
                SourceTimelineTimelineOriginUs =
                    sourceTimelineProjection.TimelineOriginUs,
                SourceTimelineHasAnchorValueUs =
                    sourceTimelineProjection.HasAnchorValueUs,
                SourceTimelineAnchorValueUs =
                    sourceTimelineProjection.AnchorValueUs,
                SourceTimelineHasAnchorMonoUs =
                    sourceTimelineProjection.HasAnchorMonoUs,
                SourceTimelineAnchorMonoUs =
                    sourceTimelineProjection.AnchorMonoUs,
                SourceTimelineIsRealtime =
                    sourceTimelineProjection.IsRealtime,
                HasPlayerSessionContract = playerSessionProjection.Available,
                PlayerSessionLifecycleState = playerSessionProjection.LifecycleState,
                PlayerSessionPublicState = playerSessionProjection.PublicState,
                PlayerSessionRuntimeState = playerSessionProjection.RuntimeState,
                PlayerSessionPlaybackIntent = playerSessionProjection.PlaybackIntent,
                PlayerSessionStopReason = playerSessionProjection.StopReason,
                PlayerSessionSourceState = playerSessionProjection.SourceState,
                PlayerSessionCanSeek = playerSessionProjection.CanSeek,
                PlayerSessionIsRealtime = playerSessionProjection.IsRealtime,
                PlayerSessionIsBuffering = playerSessionProjection.IsBuffering,
                PlayerSessionIsSyncing = playerSessionProjection.IsSyncing,
                PlayerSessionAudioStartStateReported =
                    playerSessionProjection.AudioStartStateReported,
                PlayerSessionShouldStartAudio =
                    playerSessionProjection.ShouldStartAudio,
                PlayerSessionAudioStartBlockReason =
                    playerSessionProjection.AudioStartBlockReason,
                PlayerSessionRequiredBufferedSamples =
                    playerSessionProjection.RequiredBufferedSamples,
                PlayerSessionReportedBufferedSamples =
                    playerSessionProjection.ReportedBufferedSamples,
                PlayerSessionRequiresPresentedVideoFrame =
                    playerSessionProjection.RequiresPresentedVideoFrame,
                PlayerSessionHasPresentedVideoFrame =
                    playerSessionProjection.HasPresentedVideoFrame,
                PlayerSessionAndroidFileRateBridgeActive =
                    playerSessionProjection.AndroidFileRateBridgeActive,
                HasAudioOutputPolicy = audioOutputPolicyProjection.Available,
                AudioOutputPolicyFileStartThresholdMs =
                    audioOutputPolicyProjection.FileStartThresholdMilliseconds,
                AudioOutputPolicyAndroidFileStartThresholdMs =
                    audioOutputPolicyProjection.AndroidFileStartThresholdMilliseconds,
                AudioOutputPolicyRealtimeStartThresholdMs =
                    audioOutputPolicyProjection.RealtimeStartThresholdMilliseconds,
                AudioOutputPolicyRealtimeStartupGraceMs =
                    audioOutputPolicyProjection.RealtimeStartupGraceMilliseconds,
                AudioOutputPolicyRealtimeStartupMinimumThresholdMs =
                    audioOutputPolicyProjection.RealtimeStartupMinimumThresholdMilliseconds,
                AudioOutputPolicyFileRingCapacityMs =
                    audioOutputPolicyProjection.FileRingCapacityMilliseconds,
                AudioOutputPolicyAndroidFileRingCapacityMs =
                    audioOutputPolicyProjection.AndroidFileRingCapacityMilliseconds,
                AudioOutputPolicyRealtimeRingCapacityMs =
                    audioOutputPolicyProjection.RealtimeRingCapacityMilliseconds,
                AudioOutputPolicyFileBufferedCeilingMs =
                    audioOutputPolicyProjection.FileBufferedCeilingMilliseconds,
                AudioOutputPolicyAndroidFileBufferedCeilingMs =
                    audioOutputPolicyProjection.AndroidFileBufferedCeilingMilliseconds,
                AudioOutputPolicyRealtimeBufferedCeilingMs =
                    audioOutputPolicyProjection.RealtimeBufferedCeilingMilliseconds,
                AudioOutputPolicyRealtimeStartupAdditionalSinkDelayMs =
                    audioOutputPolicyProjection.RealtimeStartupAdditionalSinkDelayMilliseconds,
                AudioOutputPolicyRealtimeSteadyAdditionalSinkDelayMs =
                    audioOutputPolicyProjection.RealtimeSteadyAdditionalSinkDelayMilliseconds,
                AudioOutputPolicyRealtimeBackendAdditionalSinkDelayMs =
                    audioOutputPolicyProjection.RealtimeBackendAdditionalSinkDelayMilliseconds,
                AudioOutputPolicyRealtimeStartRequiresVideoFrame =
                    audioOutputPolicyProjection.RealtimeStartRequiresVideoFrame,
                AudioOutputPolicyAllowAndroidFileOutputRateBridge =
                    audioOutputPolicyProjection.AllowAndroidFileOutputRateBridge,
                HasAvSyncEnterpriseMetrics = avSyncEnterpriseProjection.Available,
                AvSyncEnterpriseSampleCount = avSyncEnterpriseProjection.SampleCount,
                AvSyncEnterpriseWindowSpanUs = avSyncEnterpriseProjection.WindowSpanUs,
                AvSyncEnterpriseLatestRawOffsetUs =
                    avSyncEnterpriseProjection.LatestRawOffsetUs,
                AvSyncEnterpriseLatestSmoothOffsetUs =
                    avSyncEnterpriseProjection.LatestSmoothOffsetUs,
                AvSyncEnterpriseDriftSlopePpm =
                    avSyncEnterpriseProjection.DriftSlopePpm,
                AvSyncEnterpriseDriftProjected2hMs =
                    avSyncEnterpriseProjection.DriftProjected2hMs,
                AvSyncEnterpriseOffsetAbsP95Us =
                    avSyncEnterpriseProjection.OffsetAbsP95Us,
                AvSyncEnterpriseOffsetAbsP99Us =
                    avSyncEnterpriseProjection.OffsetAbsP99Us,
                AvSyncEnterpriseOffsetAbsMaxUs =
                    avSyncEnterpriseProjection.OffsetAbsMaxUs,
                HasPassiveAvSyncSnapshot = passiveAvSyncProjection.Available,
                PassiveAvSyncRawOffsetUs = passiveAvSyncProjection.RawOffsetUs,
                PassiveAvSyncSmoothOffsetUs = passiveAvSyncProjection.SmoothOffsetUs,
                PassiveAvSyncDriftPpm = passiveAvSyncProjection.DriftPpm,
                PassiveAvSyncDriftInterceptUs = passiveAvSyncProjection.DriftInterceptUs,
                PassiveAvSyncDriftSampleCount = passiveAvSyncProjection.DriftSampleCount,
                PassiveAvSyncVideoSchedule = passiveAvSyncProjection.VideoSchedule,
                PassiveAvSyncAudioResampleRatio = passiveAvSyncProjection.AudioResampleRatio,
                PassiveAvSyncAudioResampleActive =
                    passiveAvSyncProjection.AudioResampleActive,
                PassiveAvSyncShouldRebuildAnchor =
                    passiveAvSyncProjection.ShouldRebuildAnchor,
                HasBridgeDescriptor = bridgeDescriptorProjection.Available,
                BridgeDescriptorState = bridgeDescriptorProjection.State,
                BridgeDescriptorRuntimeKind = bridgeDescriptorProjection.RuntimeKind,
                BridgeDescriptorZeroCopySupported =
                    bridgeDescriptorProjection.ZeroCopySupported,
                BridgeDescriptorDirectBindable =
                    bridgeDescriptorProjection.DirectBindable,
                BridgeDescriptorSourcePlaneTexturesSupported =
                    bridgeDescriptorProjection.SourcePlaneTexturesSupported,
                BridgeDescriptorFallbackCopyPath =
                    bridgeDescriptorProjection.FallbackCopyPath,
                HasPathSelection = pathSelectionProjection.Available,
                PathSelectionKind = pathSelectionProjection.Kind,
                PathSelectionSourceMemoryKind = pathSelectionProjection.SourceMemoryKind,
                PathSelectionPresentedMemoryKind = pathSelectionProjection.PresentedMemoryKind,
                PathSelectionTargetZeroCopy = pathSelectionProjection.TargetZeroCopy,
                PathSelectionSourcePlaneTexturesSupported =
                    pathSelectionProjection.SourcePlaneTexturesSupported,
                PathSelectionCpuFallback = pathSelectionProjection.CpuFallback,
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
                HasRealtimeLatencySample = realtimeProbeObservation.HasRealtimeLatencySample,
                RealtimeLatencyMilliseconds =
                    realtimeProbeObservation.RealtimeLatencyMilliseconds,
                PublisherElapsedTimeSec =
                    realtimeProbeObservation.PublisherElapsedTimeSec,
                RealtimeReferenceTimeSec = referencePlaybackTime,
                HasRealtimeProbeSample = realtimeProbeObservation.HasRealtimeProbeSample,
                RealtimeProbeUnixMs = realtimeProbeObservation.RealtimeProbeUnixMs,
            };
        }

        private ValidationSnapshot ResolveValidationWindowSnapshot(ValidationSnapshot fallbackSnapshot)
        {
            return MediaNativeInteropCommon.ResolveValidationWindowSnapshot(
                _hasValidationWindowSnapshot,
                _lastValidationWindowSnapshot,
                fallbackSnapshot);
        }

        private void StartValidationWindow(
            float now,
            float startupElapsed,
            string reason,
            double playbackTime)
        {
            var validationWindowState =
                MediaNativeInteropCommon.CreateStartedValidationWindowState(
                    now,
                    reason,
                    playbackTime);
            _validationWindowStarted = validationWindowState.Started;
            _validationWindowStartTime = validationWindowState.StartTime;
            _validationWindowStartReason = validationWindowState.StartReason;
            _validationWindowInitialPlaybackTime =
                validationWindowState.InitialPlaybackTime;
            _maxObservedPlaybackTime = validationWindowState.MaxObservedPlaybackTime;
            Debug.Log(
                MediaNativeInteropCommon.CreateValidationWindowStartedLogLine(
                    ValidationLogPrefix,
                    reason,
                    startupElapsed));
        }

        private MediaNativeInteropCommon.PullValidationAudioGatePolicyView
            CreateValidationAudioGatePolicy(ValidationSnapshot snapshot)
        {
            return CreateValidationAudioGatePolicy(snapshot.AudioStartRuntimeCommand);
        }

        private MediaNativeInteropCommon.PullValidationAudioGatePolicyView
            CreateValidationAudioGatePolicy(
                MediaNativeInteropCommon.AudioStartRuntimeCommandView runtimeCommand)
        {
            return MediaNativeInteropCommon.CreatePullValidationAudioGatePolicy(
                RequireAudioOutput,
                Player,
                runtimeCommand);
        }

        private MediaNativeInteropCommon.PullValidationGateInputsView
            CreateValidationGateInputs(ValidationSnapshot snapshot)
        {
            return CreateValidationGateInputs(
                snapshot,
                snapshot.AudioGatePolicy);
        }

        private MediaNativeInteropCommon.PullValidationGateInputsView
            CreateValidationGateInputs(
                ValidationSnapshot snapshot,
                MediaNativeInteropCommon.PullValidationAudioGatePolicyView audioGatePolicy)
        {
            return MediaNativeInteropCommon.CreatePullValidationGateInputs(
                snapshot.HasTexture,
                audioGatePolicy,
                snapshot.AudioPlaying,
                snapshot.Started,
                snapshot.HasPresentedNativeVideoFrame,
                snapshot.ValidationGatePlaybackTimeSec);
        }

        private MediaNativeInteropCommon.ValidationWindowEvidenceObservationView
            CreateCurrentValidationEvidenceObservation()
        {
            return MediaNativeInteropCommon.CreateValidationWindowEvidenceObservation(
                _observedTextureDuringWindow,
                _observedAudioDuringWindow,
                _observedStartedDuringWindow,
                _observedNativeFrameDuringWindow,
                _maxObservedPlaybackTime);
        }

        private static MediaNativeInteropCommon.ValidationWindowEvidenceObservationView
            CreateValidationEvidenceObservation(ValidationSnapshot snapshot)
        {
            return MediaNativeInteropCommon.CreateValidationWindowEvidenceObservation(
                snapshot.ObservedTextureDuringWindow,
                snapshot.ObservedAudioDuringWindow,
                snapshot.ObservedStartedDuringWindow,
                snapshot.ObservedNativeFrameDuringWindow,
                snapshot.MaxObservedPlaybackTimeSec);
        }

        private ValidationSnapshot RecordValidationObservation(ValidationSnapshot snapshot)
        {
            var validationGateInputs = CreateValidationGateInputs(snapshot);
            var evidenceObservation =
                MediaNativeInteropCommon.AccumulatePullValidationWindowEvidenceObservation(
                    CreateCurrentValidationEvidenceObservation(),
                    validationGateInputs);
            MediaNativeInteropCommon.ApplyValidationWindowEvidenceObservation(
                evidenceObservation,
                ref _observedTextureDuringWindow,
                ref _observedAudioDuringWindow,
                ref _observedStartedDuringWindow,
                ref _observedNativeFrameDuringWindow,
                ref _maxObservedPlaybackTime);
            snapshot.ObservedTextureDuringWindow =
                evidenceObservation.ObservedTextureDuringWindow;
            snapshot.ObservedAudioDuringWindow =
                evidenceObservation.ObservedAudioDuringWindow;
            snapshot.ObservedStartedDuringWindow =
                evidenceObservation.ObservedStartedDuringWindow;
            snapshot.ObservedNativeFrameDuringWindow =
                evidenceObservation.ObservedNativeFrameDuringWindow;
            snapshot.MaxObservedPlaybackTimeSec =
                evidenceObservation.MaxObservedPlaybackTime;
            snapshot.ValidationWindowStartReason = _validationWindowStartReason;
            snapshot.ValidationWindowInitialPlaybackTimeSec =
                _validationWindowInitialPlaybackTime;
            _lastValidationWindowSnapshot = snapshot;
            _hasValidationWindowSnapshot = true;
            return snapshot;
        }

        private ValidationResultInfo EvaluateValidationResult(ValidationSnapshot finalSnapshot)
        {
            var summarySnapshot = ResolveValidationWindowSnapshot(finalSnapshot);
            var validationGateInputs = CreateValidationGateInputs(summarySnapshot);
            var resultObservation =
                MediaNativeInteropCommon.CreatePullValidationResultObservation(
                    summarySnapshot.ValidationWindowStartReason,
                    CreateValidationEvidenceObservation(summarySnapshot),
                    validationGateInputs.RequireAudioOutput,
                    MediaNativeInteropCommon.MinimumValidationPlaybackAdvanceSeconds,
                    summarySnapshot.ValidationWindowInitialPlaybackTimeSec);
            var playbackAdvance = resultObservation.PlaybackAdvanceSeconds;

            if (!resultObservation.Passed)
            {
                Debug.LogError(
                    MediaNativeInteropCommon.CreateValidationResultFailedLogLine(
                        ValidationLogPrefix,
                        resultObservation));

                return ValidationResultInfo.Failed(
                    resultObservation.Reason,
                    playbackAdvance);
            }

            Debug.Log(
                MediaNativeInteropCommon.CreateValidationResultPassedLogLine(
                    ValidationLogPrefix,
                    resultObservation,
                    summarySnapshot.SourceState,
                    summarySnapshot.SourceTimeouts,
                    summarySnapshot.SourceReconnects));
            Debug.Log(
                MediaNativeInteropCommon.CreateValidationCompleteLogLine(
                    ValidationLogPrefix));
            return ValidationResultInfo.PassedWithAdvance(
                playbackAdvance,
                resultObservation.Reason);
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
                var summarySnapshot = ResolveValidationWindowSnapshot(finalSnapshot);
                var summaryAudioGatePolicy = summarySnapshot.AudioGatePolicy;
                var builder = new StringBuilder();
                var summaryHeader =
                    MediaNativeInteropCommon.CreatePullValidationSummaryHeader(
                        result.Passed,
                        result.Reason,
                        summarySnapshot.Uri,
                        summarySnapshot.RequestedBackend,
                        summarySnapshot.ActualBackend,
                        summarySnapshot.RequestedVideoRenderer,
                        summarySnapshot.ActualVideoRenderer,
                        summaryAudioGatePolicy,
                        result.PlaybackAdvanceSeconds);
                var summaryWindow =
                    MediaNativeInteropCommon.CreatePullValidationSummaryWindow(
                        summarySnapshot.HasTexture,
                        summarySnapshot.AudioPlaying,
                        summarySnapshot.Started,
                        summarySnapshot.ObservedTextureDuringWindow,
                        summarySnapshot.ObservedAudioDuringWindow,
                        summarySnapshot.ObservedStartedDuringWindow,
                        summarySnapshot.ObservedNativeFrameDuringWindow,
                        summarySnapshot.ValidationWindowStartReason);
                var summaryRuntimeHealth =
                    MediaNativeInteropCommon.CreateValidationSummaryRuntimeHealth(
                        summarySnapshot.HasRuntimeHealth,
                        summarySnapshot.RuntimeStatePublic,
                        summarySnapshot.RuntimeStateInternal,
                        summarySnapshot.PlaybackIntent,
                        summarySnapshot.StreamCount,
                        summarySnapshot.VideoDecoderCount,
                        summarySnapshot.HasAudioDecoder);
                var summarySourceRuntime =
                    MediaNativeInteropCommon.CreateValidationSummarySourceRuntime(
                        summarySnapshot.SourceState,
                        summarySnapshot.SourcePackets,
                        summarySnapshot.SourceTimeouts,
                        summarySnapshot.SourceReconnects,
                        summarySnapshot.SourceLastActivityAgeSec);
                var summaryPathSelection =
                    MediaNativeInteropCommon.CreateValidationSummaryPathSelection(
                        summarySnapshot.HasPathSelection,
                        summarySnapshot.PathSelectionKind);
                var summarySourceTimeline =
                    MediaNativeInteropCommon.CreateObservedSourceTimelineAuditStrings(
                        summarySnapshot.HasSourceTimelineContract,
                        summarySnapshot.SourceTimelineModel,
                        summarySnapshot.SourceTimelineAnchorKind,
                        summarySnapshot.SourceTimelineHasCurrentSourceTimeUs,
                        summarySnapshot.SourceTimelineCurrentSourceTimeUs,
                        summarySnapshot.SourceTimelineHasTimelineOriginUs,
                        summarySnapshot.SourceTimelineTimelineOriginUs,
                        summarySnapshot.SourceTimelineHasAnchorValueUs,
                        summarySnapshot.SourceTimelineAnchorValueUs,
                        summarySnapshot.SourceTimelineHasAnchorMonoUs,
                        summarySnapshot.SourceTimelineAnchorMonoUs,
                        summarySnapshot.SourceTimelineIsRealtime);
                var summaryAudioOutputPolicy =
                    MediaNativeInteropCommon.CreateObservedAudioOutputPolicyAuditStrings(
                        summarySnapshot.HasAudioOutputPolicy,
                        summarySnapshot.AudioOutputPolicyFileStartThresholdMs,
                        summarySnapshot.AudioOutputPolicyAndroidFileStartThresholdMs,
                        summarySnapshot.AudioOutputPolicyRealtimeStartThresholdMs,
                        summarySnapshot.AudioOutputPolicyRealtimeStartupGraceMs,
                        summarySnapshot.AudioOutputPolicyRealtimeStartupMinimumThresholdMs,
                        summarySnapshot.AudioOutputPolicyFileRingCapacityMs,
                        summarySnapshot.AudioOutputPolicyAndroidFileRingCapacityMs,
                        summarySnapshot.AudioOutputPolicyRealtimeRingCapacityMs,
                        summarySnapshot.AudioOutputPolicyFileBufferedCeilingMs,
                        summarySnapshot.AudioOutputPolicyAndroidFileBufferedCeilingMs,
                        summarySnapshot.AudioOutputPolicyRealtimeBufferedCeilingMs,
                        summarySnapshot.AudioOutputPolicyRealtimeStartupAdditionalSinkDelayMs,
                        summarySnapshot.AudioOutputPolicyRealtimeSteadyAdditionalSinkDelayMs,
                        summarySnapshot.AudioOutputPolicyRealtimeBackendAdditionalSinkDelayMs,
                        summarySnapshot.AudioOutputPolicyRealtimeStartRequiresVideoFrame,
                        summarySnapshot.AudioOutputPolicyAllowAndroidFileOutputRateBridge);
                var summaryPassiveAvSync =
                    MediaNativeInteropCommon.CreateObservedPassiveAvSyncAuditStrings(
                        summarySnapshot.HasPassiveAvSyncSnapshot,
                        summarySnapshot.PassiveAvSyncRawOffsetUs,
                        summarySnapshot.PassiveAvSyncSmoothOffsetUs,
                        summarySnapshot.PassiveAvSyncDriftPpm,
                        summarySnapshot.PassiveAvSyncDriftInterceptUs,
                        summarySnapshot.PassiveAvSyncDriftSampleCount,
                        summarySnapshot.PassiveAvSyncVideoSchedule,
                        summarySnapshot.PassiveAvSyncAudioResampleRatio,
                        summarySnapshot.PassiveAvSyncAudioResampleActive,
                        summarySnapshot.PassiveAvSyncShouldRebuildAnchor);
                var summaryPlaybackContract =
                    MediaNativeInteropCommon.CreateObservedPlaybackTimingAuditStringsExtended(
                        summarySnapshot.HasPlaybackTimingContract,
                        summarySnapshot.PlaybackContractMasterTimeSec,
                        summarySnapshot.PlaybackContractMasterTimeUs,
                        summarySnapshot.PlaybackContractExternalTimeSec,
                        summarySnapshot.PlaybackContractExternalTimeUs,
                        summarySnapshot.PlaybackContractHasAudioTimeSec,
                        summarySnapshot.PlaybackContractAudioTimeSec,
                        summarySnapshot.PlaybackContractHasAudioTimeUs,
                        summarySnapshot.PlaybackContractAudioTimeUs,
                        summarySnapshot.PlaybackContractHasAudioPresentedTimeSec,
                        summarySnapshot.PlaybackContractAudioPresentedTimeSec,
                        summarySnapshot.PlaybackContractHasAudioPresentedTimeUs,
                        summarySnapshot.PlaybackContractAudioPresentedTimeUs,
                        summarySnapshot.PlaybackContractAudioSinkDelaySec,
                        summarySnapshot.PlaybackContractAudioSinkDelayUs,
                        summarySnapshot.PlaybackContractHasMicrosecondMirror,
                        summarySnapshot.PlaybackContractHasAudioClock);
                var summaryPlayerSession =
                    MediaNativeInteropCommon.CreateValidationSummaryPlayerSessionExtended(
                        summarySnapshot.HasPlayerSessionContract,
                        summarySnapshot.PlayerSessionLifecycleState,
                        summarySnapshot.PlayerSessionPublicState,
                        summarySnapshot.PlayerSessionRuntimeState,
                        summarySnapshot.PlayerSessionPlaybackIntent,
                        summarySnapshot.PlayerSessionStopReason,
                        summarySnapshot.PlayerSessionSourceState,
                        summarySnapshot.PlayerSessionCanSeek,
                        summarySnapshot.PlayerSessionIsRealtime,
                        summarySnapshot.PlayerSessionIsBuffering,
                        summarySnapshot.PlayerSessionIsSyncing,
                        summarySnapshot.PlayerSessionAudioStartStateReported,
                        summarySnapshot.PlayerSessionShouldStartAudio,
                        summarySnapshot.PlayerSessionAudioStartBlockReason,
                        summarySnapshot.PlayerSessionRequiredBufferedSamples,
                        summarySnapshot.PlayerSessionReportedBufferedSamples,
                        summarySnapshot.PlayerSessionRequiresPresentedVideoFrame,
                        summarySnapshot.PlayerSessionHasPresentedVideoFrame,
                        summarySnapshot.PlayerSessionAndroidFileRateBridgeActive);
                var summaryAvSyncEnterprise =
                    MediaNativeInteropCommon.CreateObservedAvSyncEnterpriseAuditStringsExtended(
                        summarySnapshot.HasAvSyncEnterpriseMetrics,
                        summarySnapshot.AvSyncEnterpriseSampleCount,
                        summarySnapshot.AvSyncEnterpriseWindowSpanUs,
                        summarySnapshot.AvSyncEnterpriseLatestRawOffsetUs,
                        summarySnapshot.AvSyncEnterpriseLatestSmoothOffsetUs,
                        summarySnapshot.AvSyncEnterpriseDriftSlopePpm,
                        summarySnapshot.AvSyncEnterpriseDriftProjected2hMs,
                        summarySnapshot.AvSyncEnterpriseOffsetAbsP95Us,
                        summarySnapshot.AvSyncEnterpriseOffsetAbsP99Us,
                        summarySnapshot.AvSyncEnterpriseOffsetAbsMaxUs);
                var summaryFrameContract =
                    MediaNativeInteropCommon.CreateValidationSummaryFrameContract(
                        summarySnapshot.HasFrameContract,
                        summarySnapshot.FrameContractMemoryKind,
                        summarySnapshot.FrameContractDynamicRange,
                        summarySnapshot.FrameContractNominalFps);
                var summaryAvSyncContract =
                    MediaNativeInteropCommon.CreateValidationSummaryAvSyncContract(
                        summarySnapshot.HasAvSyncContract,
                        summarySnapshot.AvSyncContractMasterClock,
                        summarySnapshot.AvSyncContractDriftMs,
                        summarySnapshot.AvSyncContractClockDeltaMs,
                        summarySnapshot.AvSyncContractDropTotal,
                        summarySnapshot.AvSyncContractDuplicateTotal);
                var summaryEnterpriseMetrics =
                    MediaNativeInteropCommon.CreateValidationSummaryEnterpriseMetrics(
                        sessionId: summarySnapshot.SessionId,
                        sourceTimelineModel: summarySnapshot.SourceTimelineModel,
                        uri: summarySnapshot.Uri,
                        hasAvSyncContract: summarySnapshot.HasAvSyncContract,
                        hasAvSyncAudioClockSec: summarySnapshot.AvSyncContractHasAudioClockSec,
                        avSyncAudioClockSec: summarySnapshot.AvSyncContractAudioClockSec,
                        hasAvSyncVideoClockSec: summarySnapshot.AvSyncContractHasVideoClockSec,
                        avSyncVideoClockSec: summarySnapshot.AvSyncContractVideoClockSec,
                        avSyncDropTotal: summarySnapshot.AvSyncContractDropTotal,
                        avSyncDuplicateTotal: summarySnapshot.AvSyncContractDuplicateTotal,
                        hasPlaybackTimingContract: summarySnapshot.HasPlaybackTimingContract,
                        hasPlaybackAudioPresentedTimeUs: summarySnapshot.PlaybackContractHasAudioPresentedTimeUs,
                        playbackAudioPresentedTimeUs: summarySnapshot.PlaybackContractAudioPresentedTimeUs,
                        hasPlaybackAudioTimeUs: summarySnapshot.PlaybackContractHasAudioTimeUs,
                        playbackAudioTimeUs: summarySnapshot.PlaybackContractAudioTimeUs,
                        hasPresentedVideoTime: summarySnapshot.HasPresentedVideoTime,
                        presentedVideoTimeSec: summarySnapshot.PresentedVideoTimeSec,
                        hasPassiveAvSyncSnapshot: summarySnapshot.HasPassiveAvSyncSnapshot,
                        passiveRawOffsetUs: summarySnapshot.PassiveAvSyncRawOffsetUs,
                        passiveSmoothOffsetUs: summarySnapshot.PassiveAvSyncSmoothOffsetUs,
                        passiveDriftPpm: summarySnapshot.PassiveAvSyncDriftPpm,
                        passiveAudioResampleRatio: summarySnapshot.PassiveAvSyncAudioResampleRatio,
                        hasAvSyncEnterpriseMetrics: summarySnapshot.HasAvSyncEnterpriseMetrics,
                        avSyncEnterpriseDriftSlopePpm: summarySnapshot.AvSyncEnterpriseDriftSlopePpm,
                        avSyncEnterpriseOffsetAbsP95Us: summarySnapshot.AvSyncEnterpriseOffsetAbsP95Us,
                        avSyncEnterpriseOffsetAbsP99Us: summarySnapshot.AvSyncEnterpriseOffsetAbsP99Us,
                        reportedBufferedSamples: summarySnapshot.PlayerSessionReportedBufferedSamples,
                        audioSampleRate: summarySnapshot.AudioSampleRate,
                        audioChannels: summarySnapshot.AudioChannels,
                        platform: Application.platform,
                        deviceModel: SystemInfo.deviceModel);
                MediaNativeInteropCommon.AppendPullValidationSummarySections(
                    builder,
                    summaryHeader,
                    summaryWindow,
                    summaryRuntimeHealth,
                    summarySourceRuntime,
                    summaryPathSelection,
                    summaryFrameContract,
                    summaryPlaybackContract,
                    summaryAvSyncContract,
                    summarySourceTimeline,
                    summaryPlayerSession,
                    summaryAudioOutputPolicy,
                    summaryAvSyncEnterprise,
                    summaryPassiveAvSync,
                    summaryEnterpriseMetrics);
                builder.AppendLine("summary_path=" + summaryPath);
                File.WriteAllText(summaryPath, builder.ToString(), Encoding.UTF8);
                Debug.Log(
                    MediaNativeInteropCommon.CreateSummaryWrittenLogLine(
                        ValidationLogPrefix,
                        summaryPath));
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    MediaNativeInteropCommon.CreateSummaryWriteFailedLogLine(
                        ValidationLogPrefix,
                        ex.Message));
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
                Debug.LogWarning(
                    MediaNativeInteropCommon.CreateTimeReadFailedLogLine(
                        ValidationLogPrefix,
                        ex.Message));
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
                MediaNativeInteropCommon.ConfigureValidationView(
                    VideoSurface,
                    ValidationCamera,
                    width,
                    height);

                if (MediaSourceResolver.HasExplicitWindowOverride(
                        _hasExplicitWindowWidth,
                        _hasExplicitWindowHeight))
                {
                    _sourceSizedWindowApplied = true;
                    return;
                }

                if (!_sourceSizedWindowApplied || Screen.width != width || Screen.height != height)
                {
                    MediaNativeInteropCommon.ConfigureValidationWindowAndView(
                        ValidationLogPrefix,
                        ForceWindowedMode,
                        VideoSurface,
                        ValidationCamera,
                        width,
                        height,
                        "source");
                    _windowConfigured = true;
                    _sourceSizedWindowApplied = true;
                }
                return;
            }

            if (_windowConfigured
                || Player.TargetMaterial == null
                || MediaSourceResolver.HasExplicitWindowOverride(
                    _hasExplicitWindowWidth,
                    _hasExplicitWindowHeight))
            {
                return;
            }

            var texture = MediaNativeInteropCommon.ResolveMainTexture(
                Player.TargetMaterial);
            if (texture == null || texture.width <= 0 || texture.height <= 0)
            {
                return;
            }

            if (Time.realtimeSinceStartup - _startTime < 1.0f)
            {
                return;
            }

            MediaNativeInteropCommon.ConfigureValidationWindowAndView(
                ValidationLogPrefix,
                ForceWindowedMode,
                VideoSurface,
                ValidationCamera,
                texture.width,
                texture.height,
                "texture-fallback");
            _windowConfigured = true;
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

            var targetTexture = MediaNativeInteropCommon.ResolveMainTexture(
                Player != null ? Player.TargetMaterial : null);
            if (!ReferenceEquals(_videoSurfaceRuntimeMaterial.mainTexture, targetTexture))
            {
                _videoSurfaceRuntimeMaterial.mainTexture = targetTexture;
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
                    Debug.LogWarning(
                        MediaNativeInteropCommon.CreateAndroidStopAfterSummaryFailedLogLine(
                            ValidationLogPrefix,
                            ex.Message));
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
                            Debug.LogWarning(
                                MediaNativeInteropCommon.CreateAndroidMoveTaskToBackSkippedLogLine(
                                    ValidationLogPrefix,
                                    "currentActivity_unavailable"));
                            return;
                        }

                        var moved = AndroidJavaCallBool(activity, "moveTaskToBack", true);
                        Debug.Log(
                            MediaNativeInteropCommon.CreateAndroidMoveTaskToBackLogLine(
                                ValidationLogPrefix,
                                moved));
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    MediaNativeInteropCommon.CreateAndroidMoveTaskToBackFailedLogLine(
                        ValidationLogPrefix,
                        ex.Message));
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
            public string Uri;
            public string RequestedBackend;
            public string ActualBackend;
            public string RequestedVideoRenderer;
            public string ActualVideoRenderer;
            public float StartupElapsedSeconds;
            public int SessionId;
            public int AudioSampleRate;
            public int AudioChannels;
            public double PlaybackTime;
            public double ValidationGatePlaybackTimeSec;
            public MediaNativeInteropCommon.AudioStartRuntimeCommandView AudioStartRuntimeCommand;
            public MediaNativeInteropCommon.PullValidationAudioGatePolicyView AudioGatePolicy;
            public bool HasTexture;
            public bool AudioPlaying;
            public bool Started;
            public bool ObservedTextureDuringWindow;
            public bool ObservedAudioDuringWindow;
            public bool ObservedStartedDuringWindow;
            public bool ObservedNativeFrameDuringWindow;
            public string ValidationWindowStartReason;
            public double ValidationWindowInitialPlaybackTimeSec;
            public double MaxObservedPlaybackTimeSec;
            public bool HasPresentedNativeVideoFrame;
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

            public static ValidationResultInfo PassedWithAdvance(
                double playbackAdvanceSeconds,
                string reason)
            {
                return new ValidationResultInfo
                {
                    Passed = true,
                    Reason = reason,
                    PlaybackAdvanceSeconds = playbackAdvanceSeconds
                };
            }
        }
    }
}
