using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

namespace UnityAV
{
    /// <summary>
    /// 用于审计 MediaPlayer 默认路径的最小运行时驱动。
    /// 它不把“无音频”判为失败，而是把真实音频状态记录到日志里。
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public class CodexMediaPlayerAudioAuditDriver : MonoBehaviour
    {
        private const string ValidationLogPrefix = "CodexValidation";

        public MediaPlayer Player;
        public float ValidationSeconds = 12f;
        public float StartupTimeoutSeconds = 10f;
        public float LogIntervalSeconds = 1f;
        public float RealtimeReferenceLagToleranceSeconds = 0.10f;
        public string UriArgumentName = "-uri=";
        public string BackendArgumentName = "-backend=";
        public string ValidationSecondsArgumentName = "-validationSeconds=";
        public string StartupTimeoutSecondsArgumentName = "-startupTimeoutSeconds=";
        public string WindowWidthArgumentName = "-windowWidth=";
        public string WindowHeightArgumentName = "-windowHeight=";
        public string PublisherStartUnixMsArgumentName = "-publisherStartUnixMs=";
        public string AndroidUriExtraName = "rustavUri";
        public string AndroidBackendExtraName = "rustavBackend";
        public string AndroidValidationSecondsExtraName = "rustavValidationSeconds";
        public string AndroidStartupTimeoutSecondsExtraName = "rustavStartupTimeoutSeconds";
        public string AndroidWindowWidthExtraName = "rustavWindowWidth";
        public string AndroidWindowHeightExtraName = "rustavWindowHeight";
        public string AndroidPublisherStartUnixMsExtraName = "rustavPublisherStartUnixMs";
        public string SummaryFileName = "codex-validation-summary.txt";
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
        private bool _hasAudioListener;
        private bool _hasValidationWindowSnapshot;
        private ValidationSnapshot _lastValidationWindowSnapshot;
        private void Awake()
        {
            if (Player == null)
            {
                Player = GetComponent<MediaPlayer>();
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

            ValidationSeconds = MediaSourceResolver.TryReadPositiveFloatOverride(
                ValidationSecondsArgumentName,
                AndroidValidationSecondsExtraName,
                ValidationSeconds);
            StartupTimeoutSeconds = MediaSourceResolver.TryReadPositiveFloatOverride(
                StartupTimeoutSecondsArgumentName,
                AndroidStartupTimeoutSecondsExtraName,
                StartupTimeoutSeconds);
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
            if (Player == null)
            {
                Player = GetComponent<MediaPlayer>();
            }

            if (Player == null)
            {
                Debug.LogError(
                    MediaNativeInteropCommon.CreateMissingComponentLogLine(
                        ValidationLogPrefix,
                        "MediaPlayer"));
                StartCoroutine(QuitAfterDelay(1f, 2));
                return;
            }

            Application.runInBackground = true;
            Debug.Log(
                MediaNativeInteropCommon.CreateRunInBackgroundEnabledLogLine(
                    ValidationLogPrefix));

            _hasAudioListener = MediaNativeInteropCommon.HasAnyAudioListener();
            _lastLogTime = Time.realtimeSinceStartup;
            _startTime = _lastLogTime;
            Debug.Log(
                MediaNativeInteropCommon.CreateMediaPlayerAuditStartLogLine(
                    ValidationLogPrefix,
                    ValidationSeconds,
                    Player.Width,
                    Player.Height,
                    MediaSourceResolver.HasExplicitWindowOverride(
                        _hasExplicitWindowWidth,
                        _hasExplicitWindowHeight)));

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
                    var validationGateInputs = CreateValidationGateInputs(snapshot);
                    var validationWindowStartObservation =
                        MediaNativeInteropCommon.CreateMediaPlayerValidationWindowStartObservation(
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
            MediaNativeInteropCommon.EmitMediaPlayerAuditStatusLogs(
                Debug.Log,
                ValidationLogPrefix,
                CreateEmitStatusView(snapshot));

            return snapshot;
        }

        private MediaNativeInteropCommon.MediaPlayerAuditEmitStatusView
            CreateEmitStatusView(ValidationSnapshot snapshot)
        {
            return new MediaNativeInteropCommon.MediaPlayerAuditEmitStatusView
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
                PlaybackContractAudit = snapshot.PlaybackContractAudit,
                SourceTimelineAudit = snapshot.SourceTimelineAudit,
                PlayerSessionAudit = snapshot.PlayerSessionAudit,
                AudioOutputPolicyAudit = snapshot.AudioOutputPolicyAudit,
                AvSyncEnterpriseAudit = snapshot.AvSyncEnterpriseAudit,
                AudioSourcePresent = snapshot.AudioSourcePresent,
                HasAudioListener = snapshot.HasAudioListener,
                NativeVideoActive = snapshot.NativeVideoActive,
                NativeActivationDecision = snapshot.NativeActivationDecision,
                HasPresentedNativeVideoFrame = snapshot.HasPresentedNativeVideoFrame,
                PlayerSessionAvailable = snapshot.PlayerSessionAvailable,
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
                HasAvSyncSample = snapshot.HasAvSyncSample,
                AvSyncDeltaMilliseconds = snapshot.AvSyncDeltaMilliseconds,
                AudioPresentedTimeSec = snapshot.AudioPresentedTimeSec,
                ReferencePlaybackTimeSec = snapshot.ReferencePlaybackTimeSec,
                ReferencePlaybackKind = snapshot.ReferencePlaybackKind,
                PresentedVideoTimeSec = snapshot.PresentedVideoTimeSec,
                PlaybackContractAudioTimeSec = snapshot.PlaybackContractAudioTimeSec,
                PlaybackContractAudioPresentedTimeSec =
                    snapshot.PlaybackContractAudioPresentedTimeSec,
                PlaybackContractAudioSinkDelaySec =
                    snapshot.PlaybackContractAudioSinkDelaySec,
                AudioPipelineDelaySec = snapshot.AudioPipelineDelaySec,
                HasRealtimeLatencySample = snapshot.HasRealtimeLatencySample,
                RealtimeLatencyMilliseconds = snapshot.RealtimeLatencyMilliseconds,
                PublisherElapsedTimeSec = snapshot.PublisherElapsedTimeSec,
                RealtimeReferenceTimeSec = snapshot.RealtimeReferenceTimeSec,
                HasRealtimeProbeSample = snapshot.HasRealtimeProbeSample,
                RealtimeProbeUnixMs = snapshot.RealtimeProbeUnixMs,
            };
        }

        private ValidationSnapshot CaptureSnapshot()
        {
            MediaNativeInteropCommon.PlaybackTimingContractView playbackTimingContract;
            var hasPlaybackTimingContract = Player.TryGetPlaybackTimingContract(out playbackTimingContract);
            double playbackTime;
            if (!MediaNativeInteropCommon.TryResolvePlaybackTimingContractTime(
                    hasPlaybackTimingContract,
                    playbackTimingContract,
                    MediaNativeInteropCommon.PlaybackTimingPreference.ExternalThenMaster,
                    out playbackTime))
            {
                playbackTime = SafeReadPlaybackTime();
            }
            var textureObservation =
                MediaNativeInteropCommon.CreateMediaPlayerValidationVideoTextureObservation(
                    Player != null ? Player.TargetMaterial : null);
            var audioPlaybackObservation =
                MediaNativeInteropCommon.CreateValidationAudioPlaybackObservation(
                    Player);
            double presentedVideoTimeSec;
            var hasPresentedVideoTime = Player.TryGetPresentedNativeVideoTimeSec(
                out presentedVideoTimeSec);

            MediaPlayer.PlayerRuntimeHealth health;
            var hasHealth = Player.TryGetRuntimeHealth(out health);
            var runtimeHealthObservation =
                MediaNativeInteropCommon.CreateRuntimeHealthObservation(
                    hasHealth,
                    health);
            var referencePlaybackObservation =
                MediaNativeInteropCommon.CreateReferencePlaybackObservation(
                    playbackTime,
                    hasPresentedVideoTime,
                    presentedVideoTimeSec,
                    runtimeHealthObservation,
                    RealtimeReferenceLagToleranceSeconds);
            var referencePlaybackTime = referencePlaybackObservation.ReferenceTimeSec;
            var referencePlaybackKind = referencePlaybackObservation.ReferenceKind;
            var validationGatePlaybackTimeSec =
                MediaNativeInteropCommon.ResolveValidationGatePlaybackTime(
                    playbackTime,
                    referencePlaybackTime);
            MediaNativeInteropCommon.PlayerSessionContractView playerSessionContract;
            var playerSessionAvailable = Player.TryGetPlayerSessionContract(out playerSessionContract);
            var playerSessionContractObservation =
                MediaNativeInteropCommon.CreatePlayerSessionObservation(
                    playerSessionAvailable,
                    playerSessionContract);
            var playerSessionPlaybackConfirmed =
                playerSessionContractObservation.PlaybackConfirmed;
            var playbackStartObservation =
                MediaNativeInteropCommon.CreateMediaPlayerPlaybackStartObservation(
                    playerSessionContractObservation,
                    runtimeHealthObservation,
                    validationGatePlaybackTimeSec);
            var playerSessionObservation =
                MediaNativeInteropCommon.CreatePlayerSessionAuditStrings(
                    playerSessionContractObservation);
            MediaNativeInteropCommon.SourceTimelineContractView sourceTimelineContract;
            var sourceTimelineAvailable = Player.TryGetSourceTimelineContract(out sourceTimelineContract);
            var sourceTimelineContractObservation =
                MediaNativeInteropCommon.CreateSourceTimelineObservation(
                    sourceTimelineAvailable,
                    sourceTimelineContract);
            var sourceTimelineObservation =
                MediaNativeInteropCommon.CreateSourceTimelineAuditStrings(
                    sourceTimelineContractObservation);
            var playbackContractObservation =
                MediaNativeInteropCommon.CreatePlaybackTimingAuditStrings(
                    hasPlaybackTimingContract,
                    playbackTimingContract);
            MediaNativeInteropCommon.AvSyncContractView avSyncContract;
            var hasAvSyncContract = Player.TryGetAvSyncContract(out avSyncContract);
            var avSyncContractObservation =
                MediaNativeInteropCommon.CreateAvSyncContractObservation(
                    hasAvSyncContract,
                    avSyncContract);
            var hasAvSyncSample =
                playbackTimingContract.HasAudioPresentedTimeSec
                && referencePlaybackObservation.HasSample;
            var audioPresentedTimeSec = hasAvSyncSample
                ? playbackTimingContract.AudioPresentedTimeSec
                : 0.0;
            var audioPipelineDelaySec = playbackTimingContract.AudioSinkDelaySec;
            var avSyncDeltaMilliseconds = hasAvSyncSample
                ? (audioPresentedTimeSec - referencePlaybackTime) * 1000.0
                : 0.0;
            MediaNativeInteropCommon.AvSyncEnterpriseMetricsView enterpriseMetrics;
            var avSyncEnterpriseObservation =
                MediaNativeInteropCommon.CreateAvSyncEnterpriseAuditStrings(
                    Player.TryGetAvSyncEnterpriseMetrics(out enterpriseMetrics),
                    enterpriseMetrics);
            MediaNativeInteropCommon.AudioOutputPolicyView audioOutputPolicy;
            var audioOutputPolicyAvailable = Player.TryGetAudioOutputPolicy(out audioOutputPolicy);
            var audioOutputPolicyObservation =
                MediaNativeInteropCommon.CreateAudioOutputPolicyAuditStrings(
                    audioOutputPolicyAvailable,
                    audioOutputPolicy);
            MediaNativeInteropCommon.PassiveAvSyncSnapshotView passiveAvSyncSnapshot;
            var passiveAvSyncObservation =
                MediaNativeInteropCommon.CreatePassiveAvSyncAuditStrings(
                    Player.TryGetPassiveAvSyncSnapshot(out passiveAvSyncSnapshot),
                    passiveAvSyncSnapshot);
            MediaNativeInteropCommon.NativeVideoBridgeDescriptorView bridgeDescriptor;
            var hasBridgeDescriptor = Player.TryGetNativeVideoBridgeDescriptor(out bridgeDescriptor);
            MediaNativeInteropCommon.NativeVideoPathSelectionView pathSelection;
            var hasPathSelection = Player.TryGetNativeVideoPathSelection(out pathSelection);
            var bridgeDescriptorAudit =
                MediaNativeInteropCommon.CreateNativeVideoBridgeDescriptorAudit(
                    hasBridgeDescriptor,
                    bridgeDescriptor);
            var pathSelectionAudit =
                MediaNativeInteropCommon.CreateNativeVideoPathSelectionAudit(
                    hasPathSelection,
                    pathSelection);
            var hasPresentedNativeVideoFrame =
                MediaNativeInteropCommon.ResolveHasPresentedNativeVideoFrame(
                    playerSessionAvailable,
                    playerSessionContract,
                    hasPathSelection,
                    pathSelection);
            var nativeVideoRuntimeObservation =
                MediaNativeInteropCommon.CreateNativeVideoRuntimeObservation(Player);
            var backendRuntimeObservation =
                MediaNativeInteropCommon.CreateMediaPlayerBackendRuntimeObservation(Player);
            var realtimeProbeObservation =
                MediaNativeInteropCommon.CreateRealtimeProbeObservation(
                    runtimeHealthObservation,
                    referencePlaybackTime,
                    _hasPublisherStartUnixMs,
                    _publisherStartUnixMs);
            var playbackContractProjection =
                MediaNativeInteropCommon.CreateValidationPlaybackContractProjection(
                    hasPlaybackTimingContract,
                    playbackTimingContract);
            var avSyncContractProjection =
                MediaNativeInteropCommon.CreateValidationAvSyncContractProjection(
                    avSyncContractObservation);
            var nativeVideoRuntimeProjection =
                MediaNativeInteropCommon.CreateValidationSummaryNativeVideoRuntime(
                    nativeVideoRuntimeObservation,
                    hasPresentedNativeVideoFrame);
            var bridgeDescriptorProjection =
                MediaNativeInteropCommon.CreateValidationSummaryBridgeDescriptor(
                    bridgeDescriptorAudit);
            var pathSelectionProjection =
                MediaNativeInteropCommon.CreateValidationSummaryPathSelectionExtended(
                    pathSelectionAudit);
            return new ValidationSnapshot
            {
                Uri = Player != null ? Player.Uri : string.Empty,
                RequestedBackend = backendRuntimeObservation.RequestedBackend,
                ActualBackend = backendRuntimeObservation.ActualBackend,
                RequestedVideoRenderer = backendRuntimeObservation.RequestedVideoRenderer,
                ActualVideoRenderer = nativeVideoRuntimeObservation.ActualRenderer,
                StartupElapsedSeconds = Player.StartupElapsedSeconds,
                PlaybackTime = playbackTime,
                ValidationGatePlaybackTimeSec = validationGatePlaybackTimeSec,
                HasTexture = textureObservation.HasTexture,
                AudioPlaying = audioPlaybackObservation.Playing,
                AudioSourcePresent = audioPlaybackObservation.SourcePresent,
                HasAudioListener = _hasAudioListener,
                Started = playbackStartObservation.Started,
                PlayerSessionPlaybackConfirmed = playerSessionPlaybackConfirmed,
                TextureWidth = textureObservation.TextureWidth,
                TextureHeight = textureObservation.TextureHeight,
                SourceState = runtimeHealthObservation.SourceState,
                SourcePackets = runtimeHealthObservation.SourcePackets,
                SourceTimeouts = runtimeHealthObservation.SourceTimeouts,
                SourceReconnects = runtimeHealthObservation.SourceReconnects,
                NativeVideoActive = nativeVideoRuntimeProjection.Active,
                NativeActivationDecision = nativeVideoRuntimeProjection.ActivationDecision,
                HasPresentedNativeVideoFrame = nativeVideoRuntimeProjection.HasPresentedFrame,
                HasPresentedVideoTime = hasPresentedVideoTime,
                PresentedVideoTimeSec = hasPresentedVideoTime ? presentedVideoTimeSec : -1.0,
                HasAvSyncSample = hasAvSyncSample,
                AudioPresentedTimeSec = audioPresentedTimeSec,
                AudioPipelineDelaySec = audioPipelineDelaySec,
                AvSyncDeltaMilliseconds = avSyncDeltaMilliseconds,
                ReferencePlaybackTimeSec = referencePlaybackTime,
                ReferencePlaybackKind = referencePlaybackKind,
                PlaybackContractAudioTimeSec = playbackContractProjection.AudioTimeSec,
                PlaybackContractAudioPresentedTimeSec =
                    playbackContractProjection.AudioPresentedTimeSec,
                PlaybackContractAudioSinkDelaySec = playbackContractProjection.AudioSinkDelaySec,
                HasRealtimeLatencySample = realtimeProbeObservation.HasRealtimeLatencySample,
                RealtimeLatencyMilliseconds =
                    realtimeProbeObservation.RealtimeLatencyMilliseconds,
                PublisherElapsedTimeSec =
                    realtimeProbeObservation.PublisherElapsedTimeSec,
                HasRealtimeProbeSample = realtimeProbeObservation.HasRealtimeProbeSample,
                RealtimeProbeUnixMs = realtimeProbeObservation.RealtimeProbeUnixMs,
                RealtimeReferenceTimeSec = referencePlaybackTime,
                PlayerSessionAvailable = playerSessionObservation.Available,
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
                PlayerSessionAudit = playerSessionObservation,
                SourceTimelineAvailable = sourceTimelineObservation.Available,
                SourceTimelineModel = sourceTimelineObservation.Model,
                SourceTimelineAnchorKind = sourceTimelineObservation.AnchorKind,
                SourceTimelineAudit = sourceTimelineObservation,
                PlaybackContractAvailable = playbackContractProjection.Available,
                PlaybackContractHasUsMirror =
                    playbackContractProjection.HasMicrosecondMirror.ToString(),
                PlaybackContractAudit = playbackContractObservation,
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
                AudioOutputPolicyAvailable = audioOutputPolicyAvailable,
                AudioOutputPolicyAudit = audioOutputPolicyObservation,
                AvSyncEnterpriseAvailable = avSyncEnterpriseObservation.Available,
                AvSyncEnterpriseSampleCount = avSyncEnterpriseObservation.SampleCount,
                AvSyncEnterpriseDriftProjected2hMs =
                    avSyncEnterpriseObservation.DriftProjected2hMs,
                AvSyncEnterpriseAudit = avSyncEnterpriseObservation,
                PassiveAvSyncAudit = passiveAvSyncObservation,
                HasBridgeDescriptor = bridgeDescriptorProjection.Available,
                BridgeDescriptorState = bridgeDescriptorProjection.State,
                BridgeDescriptorRuntimeKind = bridgeDescriptorProjection.RuntimeKind,
                BridgeDescriptorZeroCopySupported = bridgeDescriptorProjection.ZeroCopySupported,
                BridgeDescriptorDirectBindable =
                    bridgeDescriptorProjection.DirectBindable,
                BridgeDescriptorSourcePlaneTexturesSupported =
                    bridgeDescriptorProjection.SourcePlaneTexturesSupported,
                BridgeDescriptorFallbackCopyPath =
                    bridgeDescriptorProjection.FallbackCopyPath,
                BridgeDescriptorBackendKind = bridgeDescriptorProjection.BackendKind,
                BridgeDescriptorTargetPlatformKind =
                    bridgeDescriptorProjection.TargetPlatformKind,
                BridgeDescriptorTargetSurfaceKind =
                    bridgeDescriptorProjection.TargetSurfaceKind,
                BridgeDescriptorTargetWidth = bridgeDescriptorProjection.TargetWidth,
                BridgeDescriptorTargetHeight = bridgeDescriptorProjection.TargetHeight,
                BridgeDescriptorTargetPixelFormat =
                    bridgeDescriptorProjection.TargetPixelFormat,
                BridgeDescriptorTargetFlags = bridgeDescriptorProjection.TargetFlags,
                BridgeDescriptorPlatformKind = bridgeDescriptorProjection.PlatformKind,
                BridgeDescriptorSurfaceKind = bridgeDescriptorProjection.SurfaceKind,
                BridgeDescriptorSupported = bridgeDescriptorProjection.Supported,
                BridgeDescriptorHardwareDecodeSupported =
                    bridgeDescriptorProjection.HardwareDecodeSupported,
                BridgeDescriptorAcquireReleaseSupported =
                    bridgeDescriptorProjection.AcquireReleaseSupported,
                BridgeDescriptorCapsFlags = bridgeDescriptorProjection.CapsFlags,
                BridgeDescriptorTargetValid = bridgeDescriptorProjection.TargetValid,
                BridgeDescriptorRequestedExternalTextureTarget =
                    bridgeDescriptorProjection.RequestedExternalTextureTarget,
                BridgeDescriptorDirectTargetPresentAllowed =
                    bridgeDescriptorProjection.DirectTargetPresentAllowed,
                BridgeDescriptorTargetBindingSupported =
                    bridgeDescriptorProjection.TargetBindingSupported,
                BridgeDescriptorExternalTextureTargetSupported =
                    bridgeDescriptorProjection.ExternalTextureTargetSupported,
                BridgeDescriptorFrameAcquireSupported =
                    bridgeDescriptorProjection.FrameAcquireSupported,
                BridgeDescriptorFrameReleaseSupported =
                    bridgeDescriptorProjection.FrameReleaseSupported,
                BridgeDescriptorSourceSurfaceZeroCopy =
                    bridgeDescriptorProjection.SourceSurfaceZeroCopy,
                BridgeDescriptorPresentedFrameStrictZeroCopy =
                    bridgeDescriptorProjection.PresentedFrameStrictZeroCopy,
                BridgeDescriptorSourcePlaneViewsSupported =
                    bridgeDescriptorProjection.SourcePlaneViewsSupported,
                HasPathSelection = pathSelectionProjection.Available,
                PathSelectionKind = pathSelectionProjection.Kind,
                PathSelectionSourceMemoryKind = pathSelectionProjection.SourceMemoryKind,
                PathSelectionPresentedMemoryKind =
                    pathSelectionProjection.PresentedMemoryKind,
                PathSelectionTargetZeroCopy = pathSelectionProjection.TargetZeroCopy,
                PathSelectionSourcePlaneTexturesSupported =
                    pathSelectionProjection.SourcePlaneTexturesSupported,
                PathSelectionCpuFallback = pathSelectionProjection.CpuFallback,
                PathSelectionHasSourceFrame = pathSelectionProjection.HasSourceFrame,
                PathSelectionHasPresentedFrame = pathSelectionProjection.HasPresentedFrame,
                PathSelectionBridgeState = pathSelectionProjection.BridgeState,
                PathSelectionSourceSurfaceZeroCopy =
                    pathSelectionProjection.SourceSurfaceZeroCopy,
            };
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

        private ValidationSnapshot ResolveValidationWindowSnapshot(ValidationSnapshot fallbackSnapshot)
        {
            return MediaNativeInteropCommon.ResolveValidationWindowSnapshot(
                _hasValidationWindowSnapshot,
                _lastValidationWindowSnapshot,
                fallbackSnapshot);
        }

        private MediaNativeInteropCommon.MediaPlayerValidationGateInputsView
            CreateValidationGateInputs(ValidationSnapshot snapshot)
        {
            return MediaNativeInteropCommon.CreateMediaPlayerValidationGateInputs(
                snapshot.HasTexture,
                snapshot.AudioPlaying,
                snapshot.Started,
                snapshot.PlayerSessionPlaybackConfirmed,
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
                MediaNativeInteropCommon.AccumulateMediaPlayerValidationWindowEvidenceObservation(
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
            var resultObservation =
                MediaNativeInteropCommon.CreateMediaPlayerValidationResultObservation(
                    summarySnapshot.ValidationWindowStartReason,
                    CreateValidationEvidenceObservation(summarySnapshot),
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
                var builder = new StringBuilder();
                var summaryPlayerSession =
                    MediaNativeInteropCommon.CreateValidationSummaryPlayerSession(
                        summarySnapshot.PlayerSessionAudit.Available,
                        summarySnapshot.PlayerSessionAudit,
                        summarySnapshot.PlayerSessionAudit);
                var summaryAvSyncContract =
                    MediaNativeInteropCommon.CreateValidationSummaryAvSyncContract(
                        summarySnapshot.HasAvSyncContract,
                        summarySnapshot.AvSyncContractMasterClock,
                        summarySnapshot.AvSyncContractDriftMs,
                        summarySnapshot.AvSyncContractClockDeltaMs,
                        summarySnapshot.AvSyncContractDropTotal,
                        summarySnapshot.AvSyncContractDuplicateTotal);
                var summaryPlaybackContract = summarySnapshot.PlaybackContractAudit;
                var summarySourceTimeline = summarySnapshot.SourceTimelineAudit;
                var summaryPassiveAvSync = summarySnapshot.PassiveAvSyncAudit;
                var summaryAvSyncEnterprise = summarySnapshot.AvSyncEnterpriseAudit;
                var summaryAudioOutputPolicy = summarySnapshot.AudioOutputPolicyAudit;
                var summaryHeader =
                    MediaNativeInteropCommon.CreateMediaPlayerValidationSummaryHeader(
                        result.Passed,
                        result.Reason,
                        summarySnapshot.Uri,
                        summarySnapshot.RequestedBackend,
                        summarySnapshot.ActualBackend,
                        result.PlaybackAdvanceSeconds);
                var summaryWindow =
                    MediaNativeInteropCommon.CreateMediaPlayerValidationSummaryWindow(
                        summarySnapshot.HasTexture,
                        summarySnapshot.AudioPlaying,
                        summarySnapshot.AudioSourcePresent,
                        summarySnapshot.HasAudioListener,
                        summarySnapshot.Started,
                        summarySnapshot.ObservedTextureDuringWindow,
                        summarySnapshot.ObservedAudioDuringWindow,
                        summarySnapshot.ObservedStartedDuringWindow,
                        summarySnapshot.ObservedNativeFrameDuringWindow,
                        summarySnapshot.ValidationWindowStartReason);
                var summarySourceRuntime =
                    MediaNativeInteropCommon.CreateValidationSummarySourceRuntime(
                        summarySnapshot.SourceState,
                        summarySnapshot.SourcePackets,
                        summarySnapshot.SourceTimeouts,
                        summarySnapshot.SourceReconnects);
                var summaryBridgeDescriptor =
                    MediaNativeInteropCommon.CreateValidationSummaryBridgeDescriptor(
                        summarySnapshot.HasBridgeDescriptor,
                        summarySnapshot.BridgeDescriptorState,
                        summarySnapshot.BridgeDescriptorRuntimeKind,
                        summarySnapshot.BridgeDescriptorZeroCopySupported,
                        summarySnapshot.BridgeDescriptorDirectBindable,
                        summarySnapshot.BridgeDescriptorSourcePlaneTexturesSupported,
                        summarySnapshot.BridgeDescriptorFallbackCopyPath,
                        summarySnapshot.BridgeDescriptorBackendKind,
                        summarySnapshot.BridgeDescriptorTargetPlatformKind,
                        summarySnapshot.BridgeDescriptorTargetSurfaceKind,
                        summarySnapshot.BridgeDescriptorTargetWidth,
                        summarySnapshot.BridgeDescriptorTargetHeight,
                        summarySnapshot.BridgeDescriptorTargetPixelFormat,
                        summarySnapshot.BridgeDescriptorTargetFlags,
                        summarySnapshot.BridgeDescriptorPlatformKind,
                        summarySnapshot.BridgeDescriptorSurfaceKind,
                        summarySnapshot.BridgeDescriptorSupported,
                        summarySnapshot.BridgeDescriptorHardwareDecodeSupported,
                        summarySnapshot.BridgeDescriptorAcquireReleaseSupported,
                        summarySnapshot.BridgeDescriptorCapsFlags,
                        summarySnapshot.BridgeDescriptorTargetValid,
                        summarySnapshot.BridgeDescriptorRequestedExternalTextureTarget,
                        summarySnapshot.BridgeDescriptorDirectTargetPresentAllowed,
                        summarySnapshot.BridgeDescriptorTargetBindingSupported,
                        summarySnapshot.BridgeDescriptorExternalTextureTargetSupported,
                        summarySnapshot.BridgeDescriptorFrameAcquireSupported,
                        summarySnapshot.BridgeDescriptorFrameReleaseSupported,
                        summarySnapshot.BridgeDescriptorSourceSurfaceZeroCopy,
                        summarySnapshot.BridgeDescriptorPresentedFrameStrictZeroCopy,
                        summarySnapshot.BridgeDescriptorSourcePlaneViewsSupported);
                var summaryPathSelection =
                    MediaNativeInteropCommon.CreateValidationSummaryPathSelectionExtended(
                        summarySnapshot.HasPathSelection,
                        summarySnapshot.PathSelectionKind,
                        summarySnapshot.PathSelectionSourceMemoryKind,
                        summarySnapshot.PathSelectionPresentedMemoryKind,
                        summarySnapshot.PathSelectionTargetZeroCopy,
                        summarySnapshot.PathSelectionSourcePlaneTexturesSupported,
                        summarySnapshot.PathSelectionCpuFallback,
                        summarySnapshot.PathSelectionHasSourceFrame,
                        summarySnapshot.PathSelectionHasPresentedFrame,
                        summarySnapshot.PathSelectionBridgeState,
                        summarySnapshot.PathSelectionSourceSurfaceZeroCopy);
                var summaryNativeVideoRuntime =
                    MediaNativeInteropCommon.CreateValidationSummaryNativeVideoRuntime(
                        summarySnapshot.NativeVideoActive,
                        summarySnapshot.NativeActivationDecision,
                        summarySnapshot.HasPresentedNativeVideoFrame);
                MediaNativeInteropCommon.AppendMediaPlayerValidationSummarySections(
                    builder,
                    summaryHeader,
                    summaryWindow,
                    summarySourceRuntime,
                    summaryBridgeDescriptor,
                    summaryPathSelection,
                    summaryNativeVideoRuntime,
                    summaryPlayerSession,
                    summarySourceTimeline,
                    summaryPlaybackContract,
                    summaryAvSyncContract,
                    summaryAudioOutputPolicy,
                    summaryAvSyncEnterprise,
                    summaryPassiveAvSync);
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
        }

        private void TryConfigureWindow()
        {
            if (Player == null)
            {
                return;
            }

            if (_windowConfigured)
            {
                return;
            }

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
                return;
            }

            var texture = MediaNativeInteropCommon.ResolveMainTexture(
                Player != null ? Player.TargetMaterial : null);
            if (texture == null)
            {
                return;
            }

            if (_sourceSizedWindowApplied)
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
            _sourceSizedWindowApplied = true;
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
            public string Uri;
            public string RequestedBackend;
            public string ActualBackend;
            public string RequestedVideoRenderer;
            public string ActualVideoRenderer;
            public float StartupElapsedSeconds;
            public double PlaybackTime;
            public double ValidationGatePlaybackTimeSec;
            public bool HasTexture;
            public bool AudioPlaying;
            public bool AudioSourcePresent;
            public bool HasAudioListener;
            public bool Started;
            public bool PlayerSessionPlaybackConfirmed;
            public bool ObservedTextureDuringWindow;
            public bool ObservedAudioDuringWindow;
            public bool ObservedStartedDuringWindow;
            public bool ObservedNativeFrameDuringWindow;
            public string ValidationWindowStartReason;
            public double ValidationWindowInitialPlaybackTimeSec;
            public double MaxObservedPlaybackTimeSec;
            public int TextureWidth;
            public int TextureHeight;
            public string SourceState;
            public string SourcePackets;
            public string SourceTimeouts;
            public string SourceReconnects;
            public bool NativeVideoActive;
            public string NativeActivationDecision;
            public bool HasPresentedNativeVideoFrame;
            public bool HasPresentedVideoTime;
            public double PresentedVideoTimeSec;
            public bool HasAvSyncSample;
            public double AudioPresentedTimeSec;
            public double AudioPipelineDelaySec;
            public double AvSyncDeltaMilliseconds;
            public double ReferencePlaybackTimeSec;
            public string ReferencePlaybackKind;
            public double PlaybackContractAudioTimeSec;
            public double PlaybackContractAudioPresentedTimeSec;
            public double PlaybackContractAudioSinkDelaySec;
            public bool HasRealtimeLatencySample;
            public double RealtimeLatencyMilliseconds;
            public double PublisherElapsedTimeSec;
            public bool HasRealtimeProbeSample;
            public long RealtimeProbeUnixMs;
            public double RealtimeReferenceTimeSec;
            public bool PlayerSessionAvailable;
            public string PlayerSessionLifecycleState;
            public string PlayerSessionPublicState;
            public string PlayerSessionRuntimeState;
            public string PlayerSessionPlaybackIntent;
            public string PlayerSessionStopReason;
            public string PlayerSessionSourceState;
            public string PlayerSessionCanSeek;
            public string PlayerSessionIsRealtime;
            public string PlayerSessionIsBuffering;
            public string PlayerSessionIsSyncing;
            public MediaNativeInteropCommon.PlayerSessionAuditStringsView PlayerSessionAudit;
            public bool SourceTimelineAvailable;
            public string SourceTimelineModel;
            public string SourceTimelineAnchorKind;
            public MediaNativeInteropCommon.SourceTimelineAuditStringsView SourceTimelineAudit;
            public bool PlaybackContractAvailable;
            public string PlaybackContractHasUsMirror;
            public MediaNativeInteropCommon.PlaybackTimingAuditStringsView PlaybackContractAudit;
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
            public bool AudioOutputPolicyAvailable;
            public MediaNativeInteropCommon.AudioOutputPolicyAuditStringsView AudioOutputPolicyAudit;
            public bool AvSyncEnterpriseAvailable;
            public string AvSyncEnterpriseSampleCount;
            public string AvSyncEnterpriseDriftProjected2hMs;
            public MediaNativeInteropCommon.AvSyncEnterpriseAuditStringsView AvSyncEnterpriseAudit;
            public MediaNativeInteropCommon.PassiveAvSyncAuditStringsView PassiveAvSyncAudit;
            public bool HasBridgeDescriptor;
            public string BridgeDescriptorState;
            public string BridgeDescriptorRuntimeKind;
            public bool BridgeDescriptorZeroCopySupported;
            public bool BridgeDescriptorDirectBindable;
            public bool BridgeDescriptorSourcePlaneTexturesSupported;
            public bool BridgeDescriptorFallbackCopyPath;
            public string BridgeDescriptorBackendKind;
            public string BridgeDescriptorTargetPlatformKind;
            public string BridgeDescriptorTargetSurfaceKind;
            public int BridgeDescriptorTargetWidth;
            public int BridgeDescriptorTargetHeight;
            public string BridgeDescriptorTargetPixelFormat;
            public string BridgeDescriptorTargetFlags;
            public string BridgeDescriptorPlatformKind;
            public string BridgeDescriptorSurfaceKind;
            public bool BridgeDescriptorSupported;
            public bool BridgeDescriptorHardwareDecodeSupported;
            public bool BridgeDescriptorAcquireReleaseSupported;
            public string BridgeDescriptorCapsFlags;
            public bool BridgeDescriptorTargetValid;
            public bool BridgeDescriptorRequestedExternalTextureTarget;
            public bool BridgeDescriptorDirectTargetPresentAllowed;
            public bool BridgeDescriptorTargetBindingSupported;
            public bool BridgeDescriptorExternalTextureTargetSupported;
            public bool BridgeDescriptorFrameAcquireSupported;
            public bool BridgeDescriptorFrameReleaseSupported;
            public bool BridgeDescriptorSourceSurfaceZeroCopy;
            public bool BridgeDescriptorPresentedFrameStrictZeroCopy;
            public bool BridgeDescriptorSourcePlaneViewsSupported;
            public bool HasPathSelection;
            public string PathSelectionKind;
            public string PathSelectionSourceMemoryKind;
            public string PathSelectionPresentedMemoryKind;
            public bool PathSelectionTargetZeroCopy;
            public bool PathSelectionSourcePlaneTexturesSupported;
            public bool PathSelectionCpuFallback;
            public bool PathSelectionHasSourceFrame;
            public bool PathSelectionHasPresentedFrame;
            public string PathSelectionBridgeState;
            public bool PathSelectionSourceSurfaceZeroCopy;
        }

        private struct ValidationResultInfo
        {
            public bool Passed;
            public string Reason;
            public double PlaybackAdvanceSeconds;

            public static ValidationResultInfo PassedWithAdvance(double playbackAdvanceSeconds, string reason)
            {
                return new ValidationResultInfo
                {
                    Passed = true,
                    Reason = reason,
                    PlaybackAdvanceSeconds = playbackAdvanceSeconds,
                };
            }

            public static ValidationResultInfo Failed(string reason, double playbackAdvanceSeconds)
            {
                return new ValidationResultInfo
                {
                    Passed = false,
                    Reason = reason,
                    PlaybackAdvanceSeconds = playbackAdvanceSeconds,
                };
            }
        }
    }
}

