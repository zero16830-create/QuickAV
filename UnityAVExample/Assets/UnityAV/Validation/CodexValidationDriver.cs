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
        private MediaNativeInteropCommon.PullValidationAudioGatePolicyView
            _validationWindowAudioGatePolicy;
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

            var overrideUri = TryReadOverrideValue(UriArgumentName, AndroidUriExtraName);
            if (!string.IsNullOrEmpty(overrideUri))
            {
                Player.Uri = overrideUri;
                Debug.Log(
                    MediaNativeInteropCommon.CreateOverrideUriLogLine(
                        ValidationLogPrefix,
                        overrideUri));
            }

            var overrideBackend = TryReadOverrideValue(BackendArgumentName, AndroidBackendExtraName);
            MediaBackendKind parsedBackend;
            if (TryParseBackend(overrideBackend, out parsedBackend))
            {
                Player.PreferredBackend = parsedBackend;
                Player.StrictBackend = parsedBackend != MediaBackendKind.Auto;
                Debug.Log(
                    MediaNativeInteropCommon.CreateOverrideBackendLogLine(
                        ValidationLogPrefix,
                        parsedBackend.ToString(),
                        Player.StrictBackend));
            }

            var overrideVideoRenderer = TryReadOverrideValue(
                VideoRendererArgumentName,
                AndroidVideoRendererExtraName);
            MediaPlayerPull.PullVideoRendererKind parsedVideoRenderer;
            if (TryParseVideoRenderer(overrideVideoRenderer, out parsedVideoRenderer))
            {
                Player.VideoRenderer = parsedVideoRenderer;
                Debug.Log(
                    MediaNativeInteropCommon.CreateOverrideVideoRendererLogLine(
                        ValidationLogPrefix,
                        parsedVideoRenderer.ToString()));
            }

            bool hasExplicitLoopValue;
            Player.Loop = TryReadBoolArgument(
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
                    HasExplicitWindowOverride(),
                    Player.VideoRenderer.ToString(),
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
                    var audioGatePolicy = ResolveValidationAudioGatePolicy(snapshot);
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
                            ResolveValidationGatePlaybackTime(snapshot),
                            audioGatePolicy);
                        RecordValidationObservation(snapshot);
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

            Debug.Log(
                MediaNativeInteropCommon.CreateValidationStatusLogLine(
                    logPrefix: ValidationLogPrefix,
                    playbackTime: snapshot.PlaybackTime,
                    hasTexture: snapshot.HasTexture,
                    audioPlaying: snapshot.AudioPlaying,
                    started: snapshot.Started,
                    startupElapsedSeconds: Player.StartupElapsedSeconds,
                    sourceState: snapshot.SourceState,
                    sourcePackets: snapshot.SourcePackets,
                    sourceTimeouts: snapshot.SourceTimeouts,
                    sourceReconnects: snapshot.SourceReconnects,
                    windowWidth: Screen.width,
                    windowHeight: Screen.height,
                    textureWidth: snapshot.TextureWidth,
                    textureHeight: snapshot.TextureHeight,
                    fullscreen: Screen.fullScreen,
                    fullscreenMode: Screen.fullScreenMode,
                    actualBackend: backendRuntimeObservation.ActualBackend,
                    requestedVideoRenderer: backendRuntimeObservation.RequestedVideoRenderer,
                    actualVideoRenderer: backendRuntimeObservation.ActualVideoRenderer,
                    hasFrameContract: snapshot.HasFrameContract,
                    frameContractMemoryKind: snapshot.FrameContractMemoryKind,
                    frameContractDynamicRange: snapshot.FrameContractDynamicRange,
                    frameContractNominalFps: snapshot.FrameContractNominalFps,
                    hasPlaybackTimingContract: snapshot.HasPlaybackTimingContract,
                    playbackContractMasterTimeSec: snapshot.PlaybackContractMasterTimeSec,
                    hasAvSyncContract: snapshot.HasAvSyncContract,
                    avSyncContractMasterClock: snapshot.AvSyncContractMasterClock,
                    avSyncContractDriftMs: snapshot.AvSyncContractDriftMs,
                    hasBridgeDescriptor: snapshot.HasBridgeDescriptor,
                    bridgeDescriptorState: snapshot.BridgeDescriptorState,
                    bridgeDescriptorRuntimeKind: snapshot.BridgeDescriptorRuntimeKind,
                    bridgeDescriptorZeroCopySupported: snapshot.BridgeDescriptorZeroCopySupported,
                    bridgeDescriptorDirectBindable: snapshot.BridgeDescriptorDirectBindable,
                    bridgeDescriptorSourcePlaneTexturesSupported: snapshot.BridgeDescriptorSourcePlaneTexturesSupported,
                    bridgeDescriptorFallbackCopyPath: snapshot.BridgeDescriptorFallbackCopyPath,
                    hasPathSelection: snapshot.HasPathSelection,
                    pathSelectionKind: snapshot.PathSelectionKind,
                    pathSelectionSourceMemoryKind: snapshot.PathSelectionSourceMemoryKind,
                    pathSelectionPresentedMemoryKind: snapshot.PathSelectionPresentedMemoryKind,
                    pathSelectionTargetZeroCopy: snapshot.PathSelectionTargetZeroCopy,
                    pathSelectionSourcePlaneTexturesSupported: snapshot.PathSelectionSourcePlaneTexturesSupported,
                    pathSelectionCpuFallback: snapshot.PathSelectionCpuFallback,
                    hasSourceTimelineContract: snapshot.HasSourceTimelineContract,
                    sourceTimelineModel: snapshot.SourceTimelineModel,
                    hasPlayerSessionContract: snapshot.HasPlayerSessionContract,
                    playerSessionLifecycleState: snapshot.PlayerSessionLifecycleState,
                    hasAvSyncEnterpriseMetrics: snapshot.HasAvSyncEnterpriseMetrics,
                    avSyncEnterpriseSampleCount: snapshot.AvSyncEnterpriseSampleCount));
            if (snapshot.HasRuntimeHealth)
            {
                Debug.Log(
                    MediaNativeInteropCommon.CreateRuntimeHealthLogLine(
                        logPrefix: ValidationLogPrefix,
                        runtimeStatePublic: snapshot.RuntimeStatePublic,
                        runtimeStateInternal: snapshot.RuntimeStateInternal,
                        playbackIntent: snapshot.PlaybackIntent,
                        streamCount: snapshot.StreamCount,
                        videoDecoderCount: snapshot.VideoDecoderCount,
                        hasAudioDecoder: snapshot.HasAudioDecoder,
                        sourceLastActivityAgeSec: snapshot.SourceLastActivityAgeSec));
            }
            if (snapshot.HasWgpuRenderDescriptor)
            {
                Debug.Log(
                    MediaNativeInteropCommon.CreateWgpuDescriptorLogLine(
                        logPrefix: ValidationLogPrefix,
                        wgpuRuntimeReady: snapshot.WgpuRuntimeReady,
                        wgpuOutputWidth: snapshot.WgpuOutputWidth,
                        wgpuOutputHeight: snapshot.WgpuOutputHeight,
                        wgpuSupportsYuv420p: snapshot.WgpuSupportsYuv420p,
                        wgpuSupportsNv12: snapshot.WgpuSupportsNv12,
                        wgpuSupportsP010: snapshot.WgpuSupportsP010,
                        wgpuSupportsRgba32: snapshot.WgpuSupportsRgba32,
                        wgpuSupportsExternalTextureRgba: snapshot.WgpuSupportsExternalTextureRgba,
                        wgpuSupportsExternalTextureYu12: snapshot.WgpuSupportsExternalTextureYu12,
                        wgpuReadbackExportSupported: snapshot.WgpuReadbackExportSupported));
            }
            if (snapshot.HasWgpuRenderState)
            {
                Debug.Log(
                    MediaNativeInteropCommon.CreateWgpuStateLogLine(
                        logPrefix: ValidationLogPrefix,
                        wgpuRenderPath: snapshot.WgpuRenderPath,
                        wgpuSourceMemoryKind: snapshot.WgpuSourceMemoryKind,
                        wgpuPresentedMemoryKind: snapshot.WgpuPresentedMemoryKind,
                        wgpuSourcePixelFormat: snapshot.WgpuSourcePixelFormat,
                        wgpuPresentedPixelFormat: snapshot.WgpuPresentedPixelFormat,
                        wgpuExternalTextureFormat: snapshot.WgpuExternalTextureFormat,
                        wgpuHasRenderedFrame: snapshot.WgpuHasRenderedFrame,
                        wgpuRenderedFrameIndex: snapshot.WgpuRenderedFrameIndex,
                        wgpuRenderedTimeSec: snapshot.WgpuRenderedTimeSec,
                        wgpuHasRenderError: snapshot.WgpuHasRenderError,
                        wgpuRenderErrorKind: snapshot.WgpuRenderErrorKind,
                        wgpuUploadPlaneCount: snapshot.WgpuUploadPlaneCount,
                        wgpuSourceZeroCopy: snapshot.WgpuSourceZeroCopy,
                        wgpuCpuFallback: snapshot.WgpuCpuFallback));
            }
            if (snapshot.HasPlaybackTimingContract)
            {
                Debug.Log(
                    MediaNativeInteropCommon.CreatePlaybackContractLogLine(
                        logPrefix: ValidationLogPrefix,
                        masterTimeSec: snapshot.PlaybackContractMasterTimeSec,
                        masterTimeUs: snapshot.PlaybackContractMasterTimeUs,
                        externalTimeSec: snapshot.PlaybackContractExternalTimeSec,
                        externalTimeUs: snapshot.PlaybackContractExternalTimeUs,
                        hasAudioTimeSec: snapshot.PlaybackContractHasAudioTimeSec,
                        audioTimeSec: snapshot.PlaybackContractAudioTimeSec,
                        hasAudioTimeUs: snapshot.PlaybackContractHasAudioTimeUs,
                        audioTimeUs: snapshot.PlaybackContractAudioTimeUs,
                        hasAudioPresentedTimeSec: snapshot.PlaybackContractHasAudioPresentedTimeSec,
                        audioPresentedTimeSec: snapshot.PlaybackContractAudioPresentedTimeSec,
                        hasAudioPresentedTimeUs: snapshot.PlaybackContractHasAudioPresentedTimeUs,
                        audioPresentedTimeUs: snapshot.PlaybackContractAudioPresentedTimeUs,
                        audioSinkDelayMs: snapshot.PlaybackContractAudioSinkDelaySec * 1000.0,
                        audioSinkDelayUs: snapshot.PlaybackContractAudioSinkDelayUs,
                        hasAudioClock: snapshot.PlaybackContractHasAudioClock,
                        hasMicrosecondMirror: snapshot.PlaybackContractHasMicrosecondMirror));
            }
            if (snapshot.HasAvSyncContract)
            {
                Debug.Log(
                    MediaNativeInteropCommon.CreateAvSyncContractLogLine(
                        logPrefix: ValidationLogPrefix,
                        masterClock: snapshot.AvSyncContractMasterClock,
                        hasAudioClockSec: snapshot.AvSyncContractHasAudioClockSec,
                        audioClockSec: snapshot.AvSyncContractAudioClockSec,
                        hasVideoClockSec: snapshot.AvSyncContractHasVideoClockSec,
                        videoClockSec: snapshot.AvSyncContractVideoClockSec,
                        clockDeltaMs: snapshot.AvSyncContractClockDeltaMs,
                        driftMs: snapshot.AvSyncContractDriftMs,
                        startupWarmupComplete: snapshot.AvSyncContractStartupWarmupComplete,
                        dropTotal: snapshot.AvSyncContractDropTotal,
                        duplicateTotal: snapshot.AvSyncContractDuplicateTotal));
                if (snapshot.AvSyncContractHasAudioClockSec
                    && snapshot.AvSyncContractHasVideoClockSec)
                {
                    Debug.Log(
                        MediaNativeInteropCommon.CreateAvSyncContractSampleLogLine(
                            logPrefix: ValidationLogPrefix,
                            clockDeltaMs: snapshot.AvSyncContractClockDeltaMs,
                            audioClockSec: snapshot.AvSyncContractAudioClockSec,
                            videoClockSec: snapshot.AvSyncContractVideoClockSec));
                }
            }
            if (snapshot.HasSourceTimelineContract)
            {
                Debug.Log(
                    MediaNativeInteropCommon.CreateSourceTimelineLogLine(
                        logPrefix: ValidationLogPrefix,
                        model: snapshot.SourceTimelineModel,
                        anchorKind: snapshot.SourceTimelineAnchorKind,
                        hasCurrentSourceTimeUs: snapshot.SourceTimelineHasCurrentSourceTimeUs,
                        currentSourceTimeUs: snapshot.SourceTimelineCurrentSourceTimeUs,
                        hasTimelineOriginUs: snapshot.SourceTimelineHasTimelineOriginUs,
                        timelineOriginUs: snapshot.SourceTimelineTimelineOriginUs,
                        hasAnchorValueUs: snapshot.SourceTimelineHasAnchorValueUs,
                        anchorValueUs: snapshot.SourceTimelineAnchorValueUs,
                        hasAnchorMonoUs: snapshot.SourceTimelineHasAnchorMonoUs,
                        anchorMonoUs: snapshot.SourceTimelineAnchorMonoUs,
                        isRealtime: snapshot.SourceTimelineIsRealtime));
            }
            if (snapshot.HasPlayerSessionContract)
            {
                Debug.Log(
                    MediaNativeInteropCommon.CreateValidationPlayerSessionLogLine(
                        logPrefix: ValidationLogPrefix,
                        lifecycleState: snapshot.PlayerSessionLifecycleState,
                        publicState: snapshot.PlayerSessionPublicState,
                        runtimeState: snapshot.PlayerSessionRuntimeState,
                        playbackIntent: snapshot.PlayerSessionPlaybackIntent,
                        stopReason: snapshot.PlayerSessionStopReason,
                        sourceState: snapshot.PlayerSessionSourceState,
                        canSeek: snapshot.PlayerSessionCanSeek,
                        isRealtime: snapshot.PlayerSessionIsRealtime,
                        isBuffering: snapshot.PlayerSessionIsBuffering,
                        isSyncing: snapshot.PlayerSessionIsSyncing));
            }
            if (snapshot.HasAudioOutputPolicy)
            {
                Debug.Log(
                    MediaNativeInteropCommon.CreateAudioOutputPolicyLogLine(
                        logPrefix: ValidationLogPrefix,
                        fileStartThresholdMs: snapshot.AudioOutputPolicyFileStartThresholdMs,
                        androidFileStartThresholdMs: snapshot.AudioOutputPolicyAndroidFileStartThresholdMs,
                        realtimeStartThresholdMs: snapshot.AudioOutputPolicyRealtimeStartThresholdMs,
                        realtimeStartupGraceMs: snapshot.AudioOutputPolicyRealtimeStartupGraceMs,
                        realtimeStartupMinimumThresholdMs: snapshot.AudioOutputPolicyRealtimeStartupMinimumThresholdMs,
                        fileRingCapacityMs: snapshot.AudioOutputPolicyFileRingCapacityMs,
                        androidFileRingCapacityMs: snapshot.AudioOutputPolicyAndroidFileRingCapacityMs,
                        realtimeRingCapacityMs: snapshot.AudioOutputPolicyRealtimeRingCapacityMs,
                        fileBufferedCeilingMs: snapshot.AudioOutputPolicyFileBufferedCeilingMs,
                        androidFileBufferedCeilingMs: snapshot.AudioOutputPolicyAndroidFileBufferedCeilingMs,
                        realtimeBufferedCeilingMs: snapshot.AudioOutputPolicyRealtimeBufferedCeilingMs,
                        realtimeStartupAdditionalSinkDelayMs: snapshot.AudioOutputPolicyRealtimeStartupAdditionalSinkDelayMs,
                        realtimeSteadyAdditionalSinkDelayMs: snapshot.AudioOutputPolicyRealtimeSteadyAdditionalSinkDelayMs,
                        realtimeBackendAdditionalSinkDelayMs: snapshot.AudioOutputPolicyRealtimeBackendAdditionalSinkDelayMs,
                        realtimeStartRequiresVideoFrame: snapshot.AudioOutputPolicyRealtimeStartRequiresVideoFrame,
                        allowAndroidFileOutputRateBridge: snapshot.AudioOutputPolicyAllowAndroidFileOutputRateBridge));
            }
            if (snapshot.HasAvSyncEnterpriseMetrics)
            {
                Debug.Log(
                    MediaNativeInteropCommon.CreateAvSyncEnterpriseLogLine(
                        logPrefix: ValidationLogPrefix,
                        sampleCount: snapshot.AvSyncEnterpriseSampleCount,
                        windowSpanUs: snapshot.AvSyncEnterpriseWindowSpanUs,
                        latestRawOffsetUs: snapshot.AvSyncEnterpriseLatestRawOffsetUs,
                        latestSmoothOffsetUs: snapshot.AvSyncEnterpriseLatestSmoothOffsetUs,
                        driftSlopePpm: snapshot.AvSyncEnterpriseDriftSlopePpm,
                        driftProjected2hMs: snapshot.AvSyncEnterpriseDriftProjected2hMs,
                        offsetAbsP95Us: snapshot.AvSyncEnterpriseOffsetAbsP95Us,
                        offsetAbsP99Us: snapshot.AvSyncEnterpriseOffsetAbsP99Us,
                        offsetAbsMaxUs: snapshot.AvSyncEnterpriseOffsetAbsMaxUs));
            }
            if (snapshot.HasPassiveAvSyncSnapshot)
            {
                Debug.Log(
                    MediaNativeInteropCommon.CreatePassiveAvSyncLogLine(
                        logPrefix: ValidationLogPrefix,
                        rawOffsetUs: snapshot.PassiveAvSyncRawOffsetUs,
                        smoothOffsetUs: snapshot.PassiveAvSyncSmoothOffsetUs,
                        driftPpm: snapshot.PassiveAvSyncDriftPpm,
                        driftInterceptUs: snapshot.PassiveAvSyncDriftInterceptUs,
                        driftSampleCount: snapshot.PassiveAvSyncDriftSampleCount,
                        videoSchedule: snapshot.PassiveAvSyncVideoSchedule,
                        audioResampleRatio: snapshot.PassiveAvSyncAudioResampleRatio,
                        audioResampleActive: snapshot.PassiveAvSyncAudioResampleActive,
                        shouldRebuildAnchor: snapshot.PassiveAvSyncShouldRebuildAnchor));
            }
            if (snapshot.HasAvSyncSample)
            {
                Debug.Log(
                    MediaNativeInteropCommon.CreateAvSyncLogLine(
                        logPrefix: ValidationLogPrefix,
                        deltaMilliseconds: snapshot.AvSyncDeltaMilliseconds,
                        audioPresentedTimeSec: snapshot.AudioPresentedTimeSec,
                        referencePlaybackTimeSec: snapshot.ReferencePlaybackTimeSec,
                        referencePlaybackKind: snapshot.ReferencePlaybackKind,
                        playbackTime: snapshot.PlaybackTime,
                        presentedVideoTimeSec: snapshot.PresentedVideoTimeSec,
                        playbackContractAudioTimeSec: snapshot.PlaybackContractAudioTimeSec,
                        playbackContractAudioPresentedTimeSec: snapshot.PlaybackContractAudioPresentedTimeSec,
                        playbackContractAudioSinkDelayMs: snapshot.PlaybackContractAudioSinkDelaySec * 1000.0,
                        audioPipelineDelayMs: snapshot.AudioPipelineDelaySec * 1000.0));
            }
            if (snapshot.HasRealtimeLatencySample)
            {
                Debug.Log(
                    MediaNativeInteropCommon.CreateRealtimeLatencyLogLine(
                        logPrefix: ValidationLogPrefix,
                        realtimeLatencyMilliseconds: snapshot.RealtimeLatencyMilliseconds,
                        publisherElapsedTimeSec: snapshot.PublisherElapsedTimeSec,
                        realtimeReferenceTimeSec: snapshot.RealtimeReferenceTimeSec));
            }
            if (snapshot.HasRealtimeProbeSample)
            {
                Debug.Log(
                    MediaNativeInteropCommon.CreateRealtimeProbeLogLine(
                        logPrefix: ValidationLogPrefix,
                        realtimeProbeUnixMs: snapshot.RealtimeProbeUnixMs,
                        realtimeReferenceTimeSec: snapshot.RealtimeReferenceTimeSec));
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
            var audioPlaybackObservation =
                MediaNativeInteropCommon.CreateValidationAudioPlaybackObservation(
                    audioSource);
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
                AudioPlaying = audioPlaybackObservation.Playing,
                Started = playbackStartObservation.Started,
                HasPresentedNativeVideoFrame = Player.HasPresentedVideoFrame,
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

        private static double ResolveValidationGatePlaybackTime(ValidationSnapshot snapshot)
        {
            return MediaNativeInteropCommon.ResolveValidationGatePlaybackTime(
                snapshot.PlaybackTime,
                snapshot.ReferencePlaybackTimeSec);
        }

        private ValidationSnapshot ResolveValidationWindowSnapshot(ValidationSnapshot fallbackSnapshot)
        {
            return _hasValidationWindowSnapshot ? _lastValidationWindowSnapshot : fallbackSnapshot;
        }

        private void StartValidationWindow(
            float now,
            float startupElapsed,
            string reason,
            double playbackTime,
            MediaNativeInteropCommon.PullValidationAudioGatePolicyView audioGatePolicy)
        {
            _validationWindowStarted = true;
            _validationWindowStartTime = now;
            _validationWindowStartReason = reason;
            _validationWindowInitialPlaybackTime = playbackTime;
            _maxObservedPlaybackTime = playbackTime;
            _validationWindowAudioGatePolicy = audioGatePolicy;
            Debug.Log(
                MediaNativeInteropCommon.CreateValidationWindowStartedLogLine(
                    ValidationLogPrefix,
                    reason,
                    startupElapsed));
        }

        private MediaNativeInteropCommon.PullValidationAudioGatePolicyView
            ResolveValidationAudioGatePolicy(ValidationSnapshot snapshot)
        {
            if (_validationWindowStarted)
            {
                return _validationWindowAudioGatePolicy;
            }

            var runtimeCommand =
                MediaNativeInteropCommon.ResolveAudioStartRuntimeCommand(
                    snapshot.HasPlayerSessionContract,
                    new MediaNativeInteropCommon.PlayerSessionContractView
                    {
                        AudioStartStateReported =
                            snapshot.PlayerSessionAudioStartStateReported,
                        ShouldStartAudio =
                            snapshot.PlayerSessionShouldStartAudio,
                        AudioStartBlockReason =
                            snapshot.PlayerSessionAudioStartBlockReason,
                        RequiredBufferedSamples =
                            snapshot.PlayerSessionRequiredBufferedSamples,
                        ReportedBufferedSamples =
                            snapshot.PlayerSessionReportedBufferedSamples,
                        RequiresPresentedVideoFrame =
                            snapshot.PlayerSessionRequiresPresentedVideoFrame,
                        HasPresentedVideoFrame =
                            snapshot.PlayerSessionHasPresentedVideoFrame,
                        AndroidFileRateBridgeActive =
                            snapshot.PlayerSessionAndroidFileRateBridgeActive,
                    });
            return MediaNativeInteropCommon.CreatePullValidationAudioGatePolicy(
                RequireAudioOutput,
                Player != null && Player.EnableAudio,
                runtimeCommand);
        }

        private MediaNativeInteropCommon.PullValidationGateInputsView
            CreateValidationGateInputs(ValidationSnapshot snapshot)
        {
            return CreateValidationGateInputs(
                snapshot,
                ResolveValidationAudioGatePolicy(snapshot));
        }

        private MediaNativeInteropCommon.PullValidationGateInputsView
            CreateValidationGateInputs(
                ValidationSnapshot snapshot,
                MediaNativeInteropCommon.PullValidationAudioGatePolicyView audioGatePolicy)
        {
            return new MediaNativeInteropCommon.PullValidationGateInputsView
            {
                HasTexture = snapshot.HasTexture,
                RequireAudioOutput = audioGatePolicy.RequireAudioOutput,
                AudioEnabled = audioGatePolicy.AudioEnabled,
                AudioPlaying = snapshot.AudioPlaying,
                Started = snapshot.Started,
                HasPresentedNativeVideoFrame = snapshot.HasPresentedNativeVideoFrame,
                PlaybackTimeSec = ResolveValidationGatePlaybackTime(snapshot),
            };
        }

        private MediaNativeInteropCommon.ValidationWindowEvidenceObservationView
            CreateValidationEvidenceObservation()
        {
            return new MediaNativeInteropCommon.ValidationWindowEvidenceObservationView
            {
                ObservedTextureDuringWindow = _observedTextureDuringWindow,
                ObservedAudioDuringWindow = _observedAudioDuringWindow,
                ObservedStartedDuringWindow = _observedStartedDuringWindow,
                ObservedNativeFrameDuringWindow = _observedNativeFrameDuringWindow,
                MaxObservedPlaybackTime = _maxObservedPlaybackTime,
            };
        }

        private void RecordValidationObservation(ValidationSnapshot snapshot)
        {
            var validationGateInputs = CreateValidationGateInputs(snapshot);
            var evidenceObservation =
                MediaNativeInteropCommon.AccumulatePullValidationWindowEvidenceObservation(
                    CreateValidationEvidenceObservation(),
                    validationGateInputs);
            _observedTextureDuringWindow =
                evidenceObservation.ObservedTextureDuringWindow;
            _observedAudioDuringWindow =
                evidenceObservation.ObservedAudioDuringWindow;
            _observedStartedDuringWindow =
                evidenceObservation.ObservedStartedDuringWindow;
            _observedNativeFrameDuringWindow =
                evidenceObservation.ObservedNativeFrameDuringWindow;
            _maxObservedPlaybackTime =
                evidenceObservation.MaxObservedPlaybackTime;
            _lastValidationWindowSnapshot = snapshot;
            _hasValidationWindowSnapshot = true;
        }

        private ValidationResultInfo EvaluateValidationResult(ValidationSnapshot finalSnapshot)
        {
            var summarySnapshot = ResolveValidationWindowSnapshot(finalSnapshot);
            var validationGateInputs = CreateValidationGateInputs(summarySnapshot);
            var resultObservation =
                MediaNativeInteropCommon.CreatePullValidationResultObservation(
                    _validationWindowStartReason,
                    CreateValidationEvidenceObservation(),
                    validationGateInputs.RequireAudioOutput,
                    MediaNativeInteropCommon.MinimumValidationPlaybackAdvanceSeconds,
                    _validationWindowInitialPlaybackTime);
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
                var backendRuntimeObservation =
                    MediaNativeInteropCommon.CreatePullBackendRuntimeObservation(
                        Player != null,
                        Player != null ? Player.PreferredBackend : default(MediaBackendKind),
                        Player != null ? Player.ActualBackendKind : default(MediaBackendKind),
                        Player != null ? Player.VideoRenderer : default(MediaPlayerPull.PullVideoRendererKind),
                        Player != null ? Player.ActualVideoRenderer : default(MediaPlayerPull.PullVideoRendererKind));
                var summarySnapshot = ResolveValidationWindowSnapshot(finalSnapshot);
                var summaryAudioGatePolicy =
                    ResolveValidationAudioGatePolicy(summarySnapshot);
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
                        RequireAudioOutput = summaryAudioGatePolicy.RequireAudioOutput,
                        IncludeConfiguredRequireAudioOutput = true,
                        ConfiguredRequireAudioOutput =
                            summaryAudioGatePolicy.ConfiguredRequireAudioOutput,
                        IncludeAudioGateConfiguredAudioEnabled = true,
                        AudioGateConfiguredAudioEnabled =
                            summaryAudioGatePolicy.AudioEnabled,
                        IncludeAudioGateSource = true,
                        AudioGateSource = summaryAudioGatePolicy.Source,
                        IncludeAudioGateRuntimeContractAvailable = true,
                        AudioGateRuntimeContractAvailable =
                            summaryAudioGatePolicy.RuntimeContractAvailable,
                        IncludeAudioGateRuntimeStateReported = true,
                        AudioGateRuntimeStateReported =
                            summaryAudioGatePolicy.RuntimeStateReported,
                        IncludeAudioGateRuntimeShouldPlay = true,
                        AudioGateRuntimeShouldPlay =
                            summaryAudioGatePolicy.RuntimeShouldPlay,
                        IncludeAudioGateRuntimeBlockReason = true,
                        AudioGateRuntimeBlockReason =
                            summaryAudioGatePolicy.RuntimeBlockReason.ToString(),
                        PlaybackAdvanceSeconds = result.PlaybackAdvanceSeconds,
                    });
                MediaNativeInteropCommon.AppendValidationSummaryWindow(
                    builder,
                    new MediaNativeInteropCommon.ValidationSummaryWindowView
                    {
                        HasTexture = summarySnapshot.HasTexture,
                        AudioPlaying = summarySnapshot.AudioPlaying,
                        Started = summarySnapshot.Started,
                        ObservedTextureDuringWindow = _observedTextureDuringWindow,
                        ObservedAudioDuringWindow = _observedAudioDuringWindow,
                        ObservedStartedDuringWindow = _observedStartedDuringWindow,
                        IncludeObservedNativeFrameDuringWindow = true,
                        ObservedNativeFrameDuringWindow = _observedNativeFrameDuringWindow,
                        IncludeValidationWindowStartReason =
                            !string.IsNullOrEmpty(_validationWindowStartReason),
                        ValidationWindowStartReason = _validationWindowStartReason,
                    });
                MediaNativeInteropCommon.AppendValidationSummaryRuntimeHealth(
                    builder,
                    MediaNativeInteropCommon.CreateValidationSummaryRuntimeHealth(
                        finalSnapshot.HasRuntimeHealth,
                        finalSnapshot.RuntimeStatePublic,
                        finalSnapshot.RuntimeStateInternal,
                        finalSnapshot.PlaybackIntent,
                        finalSnapshot.StreamCount,
                        finalSnapshot.VideoDecoderCount,
                        finalSnapshot.HasAudioDecoder));
                MediaNativeInteropCommon.AppendValidationSummarySourceRuntime(
                    builder,
                    MediaNativeInteropCommon.CreateValidationSummarySourceRuntime(
                        finalSnapshot.SourceState,
                        finalSnapshot.SourcePackets,
                        finalSnapshot.SourceTimeouts,
                        finalSnapshot.SourceReconnects,
                        finalSnapshot.SourceLastActivityAgeSec));
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
                        finalSnapshot.SourceTimelineHasCurrentSourceTimeUs,
                        finalSnapshot.SourceTimelineCurrentSourceTimeUs,
                        finalSnapshot.SourceTimelineHasTimelineOriginUs,
                        finalSnapshot.SourceTimelineTimelineOriginUs,
                        finalSnapshot.SourceTimelineHasAnchorValueUs,
                        finalSnapshot.SourceTimelineAnchorValueUs,
                        finalSnapshot.SourceTimelineHasAnchorMonoUs,
                        finalSnapshot.SourceTimelineAnchorMonoUs,
                        finalSnapshot.SourceTimelineIsRealtime);
                var summaryAudioOutputPolicy =
                    MediaNativeInteropCommon.CreateObservedAudioOutputPolicyAuditStrings(
                        finalSnapshot.HasAudioOutputPolicy,
                        finalSnapshot.AudioOutputPolicyFileStartThresholdMs,
                        finalSnapshot.AudioOutputPolicyAndroidFileStartThresholdMs,
                        finalSnapshot.AudioOutputPolicyRealtimeStartThresholdMs,
                        finalSnapshot.AudioOutputPolicyRealtimeStartupGraceMs,
                        finalSnapshot.AudioOutputPolicyRealtimeStartupMinimumThresholdMs,
                        finalSnapshot.AudioOutputPolicyFileRingCapacityMs,
                        finalSnapshot.AudioOutputPolicyAndroidFileRingCapacityMs,
                        finalSnapshot.AudioOutputPolicyRealtimeRingCapacityMs,
                        finalSnapshot.AudioOutputPolicyFileBufferedCeilingMs,
                        finalSnapshot.AudioOutputPolicyAndroidFileBufferedCeilingMs,
                        finalSnapshot.AudioOutputPolicyRealtimeBufferedCeilingMs,
                        finalSnapshot.AudioOutputPolicyRealtimeStartupAdditionalSinkDelayMs,
                        finalSnapshot.AudioOutputPolicyRealtimeSteadyAdditionalSinkDelayMs,
                        finalSnapshot.AudioOutputPolicyRealtimeBackendAdditionalSinkDelayMs,
                        finalSnapshot.AudioOutputPolicyRealtimeStartRequiresVideoFrame,
                        finalSnapshot.AudioOutputPolicyAllowAndroidFileOutputRateBridge);
                var summaryPassiveAvSync =
                    MediaNativeInteropCommon.CreateObservedPassiveAvSyncAuditStrings(
                        finalSnapshot.HasPassiveAvSyncSnapshot,
                        finalSnapshot.PassiveAvSyncRawOffsetUs,
                        finalSnapshot.PassiveAvSyncSmoothOffsetUs,
                        finalSnapshot.PassiveAvSyncDriftPpm,
                        finalSnapshot.PassiveAvSyncDriftInterceptUs,
                        finalSnapshot.PassiveAvSyncDriftSampleCount,
                        finalSnapshot.PassiveAvSyncVideoSchedule,
                        finalSnapshot.PassiveAvSyncAudioResampleRatio,
                        finalSnapshot.PassiveAvSyncAudioResampleActive,
                        finalSnapshot.PassiveAvSyncShouldRebuildAnchor);
                var summaryPlaybackContract =
                    MediaNativeInteropCommon.CreateObservedPlaybackTimingAuditStringsExtended(
                        finalSnapshot.HasPlaybackTimingContract,
                        finalSnapshot.PlaybackContractMasterTimeSec,
                        finalSnapshot.PlaybackContractMasterTimeUs,
                        finalSnapshot.PlaybackContractExternalTimeSec,
                        finalSnapshot.PlaybackContractExternalTimeUs,
                        finalSnapshot.PlaybackContractHasAudioTimeSec,
                        finalSnapshot.PlaybackContractAudioTimeSec,
                        finalSnapshot.PlaybackContractHasAudioTimeUs,
                        finalSnapshot.PlaybackContractAudioTimeUs,
                        finalSnapshot.PlaybackContractHasAudioPresentedTimeSec,
                        finalSnapshot.PlaybackContractAudioPresentedTimeSec,
                        finalSnapshot.PlaybackContractHasAudioPresentedTimeUs,
                        finalSnapshot.PlaybackContractAudioPresentedTimeUs,
                        finalSnapshot.PlaybackContractAudioSinkDelaySec,
                        finalSnapshot.PlaybackContractAudioSinkDelayUs,
                        finalSnapshot.PlaybackContractHasMicrosecondMirror,
                        finalSnapshot.PlaybackContractHasAudioClock);
                var summaryPlayerSession =
                    MediaNativeInteropCommon.CreateValidationSummaryPlayerSessionExtended(
                        finalSnapshot.HasPlayerSessionContract,
                        finalSnapshot.PlayerSessionLifecycleState,
                        finalSnapshot.PlayerSessionPublicState,
                        finalSnapshot.PlayerSessionRuntimeState,
                        finalSnapshot.PlayerSessionPlaybackIntent,
                        finalSnapshot.PlayerSessionStopReason,
                        finalSnapshot.PlayerSessionSourceState,
                        finalSnapshot.PlayerSessionCanSeek,
                        finalSnapshot.PlayerSessionIsRealtime,
                        finalSnapshot.PlayerSessionIsBuffering,
                        finalSnapshot.PlayerSessionIsSyncing,
                        finalSnapshot.PlayerSessionAudioStartStateReported,
                        finalSnapshot.PlayerSessionShouldStartAudio,
                        finalSnapshot.PlayerSessionAudioStartBlockReason,
                        finalSnapshot.PlayerSessionRequiredBufferedSamples,
                        finalSnapshot.PlayerSessionReportedBufferedSamples,
                        finalSnapshot.PlayerSessionRequiresPresentedVideoFrame,
                        finalSnapshot.PlayerSessionHasPresentedVideoFrame,
                        finalSnapshot.PlayerSessionAndroidFileRateBridgeActive);
                var summaryAvSyncEnterprise =
                    MediaNativeInteropCommon.CreateObservedAvSyncEnterpriseAuditStringsExtended(
                        finalSnapshot.HasAvSyncEnterpriseMetrics,
                        finalSnapshot.AvSyncEnterpriseSampleCount,
                        finalSnapshot.AvSyncEnterpriseWindowSpanUs,
                        finalSnapshot.AvSyncEnterpriseLatestRawOffsetUs,
                        finalSnapshot.AvSyncEnterpriseLatestSmoothOffsetUs,
                        finalSnapshot.AvSyncEnterpriseDriftSlopePpm,
                        finalSnapshot.AvSyncEnterpriseDriftProjected2hMs,
                        finalSnapshot.AvSyncEnterpriseOffsetAbsP95Us,
                        finalSnapshot.AvSyncEnterpriseOffsetAbsP99Us,
                        finalSnapshot.AvSyncEnterpriseOffsetAbsMaxUs);
                var summaryFrameContract =
                    MediaNativeInteropCommon.CreateValidationSummaryFrameContract(
                        finalSnapshot.HasFrameContract,
                        finalSnapshot.FrameContractMemoryKind,
                        finalSnapshot.FrameContractDynamicRange,
                        finalSnapshot.FrameContractNominalFps);
                var summaryAvSyncContract =
                    MediaNativeInteropCommon.CreateValidationSummaryAvSyncContract(
                        finalSnapshot.HasAvSyncContract,
                        finalSnapshot.AvSyncContractMasterClock,
                        finalSnapshot.AvSyncContractDriftMs,
                        finalSnapshot.AvSyncContractClockDeltaMs,
                        finalSnapshot.AvSyncContractDropTotal,
                        finalSnapshot.AvSyncContractDuplicateTotal);
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
                MediaNativeInteropCommon.AppendValidationSummaryEnterpriseMetrics(
                    builder,
                    MediaNativeInteropCommon.CreateValidationSummaryEnterpriseMetrics(
                        sessionId: Player != null ? Player.SessionId : -1,
                        sourceTimelineModel: finalSnapshot.SourceTimelineModel,
                        uri: Player != null ? Player.Uri : string.Empty,
                        hasAvSyncContract: finalSnapshot.HasAvSyncContract,
                        hasAvSyncAudioClockSec: finalSnapshot.AvSyncContractHasAudioClockSec,
                        avSyncAudioClockSec: finalSnapshot.AvSyncContractAudioClockSec,
                        hasAvSyncVideoClockSec: finalSnapshot.AvSyncContractHasVideoClockSec,
                        avSyncVideoClockSec: finalSnapshot.AvSyncContractVideoClockSec,
                        avSyncDropTotal: finalSnapshot.AvSyncContractDropTotal,
                        avSyncDuplicateTotal: finalSnapshot.AvSyncContractDuplicateTotal,
                        hasPlaybackTimingContract: finalSnapshot.HasPlaybackTimingContract,
                        hasPlaybackAudioPresentedTimeUs: finalSnapshot.PlaybackContractHasAudioPresentedTimeUs,
                        playbackAudioPresentedTimeUs: finalSnapshot.PlaybackContractAudioPresentedTimeUs,
                        hasPlaybackAudioTimeUs: finalSnapshot.PlaybackContractHasAudioTimeUs,
                        playbackAudioTimeUs: finalSnapshot.PlaybackContractAudioTimeUs,
                        hasPresentedVideoTime: finalSnapshot.HasPresentedVideoTime,
                        presentedVideoTimeSec: finalSnapshot.PresentedVideoTimeSec,
                        hasPassiveAvSyncSnapshot: finalSnapshot.HasPassiveAvSyncSnapshot,
                        passiveRawOffsetUs: finalSnapshot.PassiveAvSyncRawOffsetUs,
                        passiveSmoothOffsetUs: finalSnapshot.PassiveAvSyncSmoothOffsetUs,
                        passiveDriftPpm: finalSnapshot.PassiveAvSyncDriftPpm,
                        passiveAudioResampleRatio: finalSnapshot.PassiveAvSyncAudioResampleRatio,
                        hasAvSyncEnterpriseMetrics: finalSnapshot.HasAvSyncEnterpriseMetrics,
                        avSyncEnterpriseDriftSlopePpm: finalSnapshot.AvSyncEnterpriseDriftSlopePpm,
                        avSyncEnterpriseOffsetAbsP95Us: finalSnapshot.AvSyncEnterpriseOffsetAbsP95Us,
                        avSyncEnterpriseOffsetAbsP99Us: finalSnapshot.AvSyncEnterpriseOffsetAbsP99Us,
                        reportedBufferedSamples: finalSnapshot.PlayerSessionReportedBufferedSamples,
                        audioSampleRate: Player != null ? Player.AudioSampleRate : 0,
                        audioChannels: Player != null ? Player.AudioChannels : 0,
                        platform: Application.platform,
                        deviceModel: SystemInfo.deviceModel));
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
                MediaNativeInteropCommon.CreateWindowConfiguredLogLine(
                    ValidationLogPrefix,
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
                    Debug.LogWarning(
                        MediaNativeInteropCommon.CreateIgnoreUnknownBackendLogLine(
                            ValidationLogPrefix,
                            rawValue));
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
                    Debug.LogWarning(
                        MediaNativeInteropCommon.CreateIgnoreUnknownVideoRendererLogLine(
                            ValidationLogPrefix,
                            rawValue));
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
            public double PlaybackTime;
            public bool HasTexture;
            public bool AudioPlaying;
            public bool Started;
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
