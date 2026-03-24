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

            ValidationSeconds = TryReadFloatArgument(
                ValidationSecondsArgumentName,
                AndroidValidationSecondsExtraName,
                ValidationSeconds);
            StartupTimeoutSeconds = TryReadFloatArgument(
                StartupTimeoutSecondsArgumentName,
                AndroidStartupTimeoutSecondsExtraName,
                StartupTimeoutSeconds);
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

            _hasAudioListener = FindObjectsOfType<AudioListener>().Length > 0;
            _lastLogTime = Time.realtimeSinceStartup;
            _startTime = _lastLogTime;
            Debug.Log(
                MediaNativeInteropCommon.CreateMediaPlayerAuditStartLogLine(
                    ValidationLogPrefix,
                    ValidationSeconds,
                    Player.Width,
                    Player.Height,
                    HasExplicitWindowOverride()));

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

            Debug.Log(
                MediaNativeInteropCommon.CreateMediaPlayerAuditStatusLogLine(
                    ValidationLogPrefix,
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
                    snapshot.ActualBackend,
                    snapshot.RequestedVideoRenderer,
                    snapshot.ActualVideoRenderer,
                    snapshot.PlaybackContractAudit,
                    snapshot.SourceTimelineAudit,
                    snapshot.PlayerSessionAudit,
                    snapshot.AudioOutputPolicyAudit,
                    snapshot.AvSyncEnterpriseAudit));
            Debug.Log(
                MediaNativeInteropCommon.CreateMediaPlayerAuditDetailLogLine(
                    ValidationLogPrefix,
                    snapshot.AudioSourcePresent,
                    snapshot.HasAudioListener,
                    snapshot.NativeVideoActive,
                    snapshot.NativeActivationDecision,
                    snapshot.HasPresentedNativeVideoFrame,
                    snapshot.ActualBackend));

            if (snapshot.PlayerSessionAvailable)
            {
                Debug.Log(
                    MediaNativeInteropCommon.CreatePlayerSessionDetailLogLine(
                        ValidationLogPrefix,
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
                        playbackContractAudioPresentedTimeSec:
                            snapshot.PlaybackContractAudioPresentedTimeSec,
                        playbackContractAudioSinkDelayMs:
                            snapshot.PlaybackContractAudioSinkDelaySec * 1000.0,
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
            var playbackTime = SafeReadPlaybackTime();
            var textureObservation =
                MediaNativeInteropCommon.CreateMediaPlayerValidationVideoTextureObservation(
                    Player.TargetMaterial != null ? Player.TargetMaterial.mainTexture : null);
            var audioSource = Player.GetComponent<AudioSource>();
            var audioPlaybackObservation =
                MediaNativeInteropCommon.CreateValidationAudioPlaybackObservation(
                    audioSource);
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
                    hasHealth,
                    hasHealth ? health.CurrentTimeSec : -1.0,
                    hasHealth && health.IsRealtime,
                    RealtimeReferenceLagToleranceSeconds);
            var referencePlaybackTime = referencePlaybackObservation.ReferenceTimeSec;
            var referencePlaybackKind = referencePlaybackObservation.ReferenceKind;
            var validationGatePlaybackTimeSec =
                MediaNativeInteropCommon.ResolveValidationGatePlaybackTime(
                    playbackTime,
                    referencePlaybackTime);
            MediaNativeInteropCommon.PlayerSessionContractView playerSessionContract;
            var playerSessionAvailable = Player.TryGetPlayerSessionContract(out playerSessionContract);
            var runtimePlaybackConfirmed =
                MediaNativeInteropCommon.IsPlayerSessionRuntimePlaybackConfirmed(
                    playerSessionAvailable,
                    playerSessionContract);
            var playbackStartObservation =
                MediaNativeInteropCommon.CreatePlaybackStartObservation(
                    runtimePlaybackConfirmed,
                    runtimePlaybackConfirmed,
                    runtimeHealthObservation.Available,
                    runtimeHealthObservation.IsPlaying,
                    validationGatePlaybackTimeSec,
                    true);
            var playerSessionObservation =
                MediaNativeInteropCommon.CreatePlayerSessionAuditStrings(
                    playerSessionAvailable,
                    playerSessionContract);
            MediaNativeInteropCommon.SourceTimelineContractView sourceTimelineContract;
            var sourceTimelineAvailable = Player.TryGetSourceTimelineContract(out sourceTimelineContract);
            var sourceTimelineObservation =
                MediaNativeInteropCommon.CreateSourceTimelineAuditStrings(
                    sourceTimelineAvailable,
                    sourceTimelineContract);
            MediaNativeInteropCommon.PlaybackTimingContractView playbackTimingContract;
            var playbackContractObservation =
                MediaNativeInteropCommon.CreatePlaybackTimingAuditStrings(
                    Player.TryGetPlaybackTimingContract(out playbackTimingContract),
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
            var nativeVideoRuntimeObservation =
                MediaNativeInteropCommon.CreateNativeVideoRuntimeObservation(
                    Player != null,
                    Player != null && Player.IsNativeVideoPathActive,
                    Player != null
                        ? Player.NativeVideoPresentationPath
                        : default(MediaPlayer.NativeVideoPresentationPathKind),
                    Player != null
                        ? Player.NativeVideoActivationDecision
                        : default(MediaPlayer.NativeVideoActivationDecisionKind));
            var backendRuntimeObservation =
                MediaNativeInteropCommon.CreateMediaPlayerBackendRuntimeObservation(
                    Player != null,
                    Player != null ? Player.PreferredBackend : default(MediaBackendKind),
                    Player != null ? Player.ActualBackendKind : default(MediaBackendKind));
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
                Uri = Player != null ? Player.Uri : string.Empty,
                RequestedBackend = backendRuntimeObservation.RequestedBackend,
                ActualBackend = backendRuntimeObservation.ActualBackend,
                RequestedVideoRenderer = backendRuntimeObservation.RequestedVideoRenderer,
                ActualVideoRenderer = nativeVideoRuntimeObservation.ActualRenderer,
                PlaybackTime = playbackTime,
                ValidationGatePlaybackTimeSec = validationGatePlaybackTimeSec,
                HasTexture = textureObservation.HasTexture,
                AudioPlaying = audioPlaybackObservation.Playing,
                AudioSourcePresent = audioPlaybackObservation.SourcePresent,
                HasAudioListener = _hasAudioListener,
                Started = playbackStartObservation.Started,
                RuntimePlaybackConfirmed = runtimePlaybackConfirmed,
                TextureWidth = textureObservation.TextureWidth,
                TextureHeight = textureObservation.TextureHeight,
                SourceState = runtimeHealthObservation.SourceState,
                SourcePackets = runtimeHealthObservation.SourcePackets,
                SourceTimeouts = runtimeHealthObservation.SourceTimeouts,
                SourceReconnects = runtimeHealthObservation.SourceReconnects,
                NativeVideoActive = Player.IsNativeVideoPathActive,
                NativeActivationDecision = nativeVideoRuntimeObservation.ActivationDecision,
                HasPresentedNativeVideoFrame = Player.HasPresentedNativeVideoFrame,
                HasPresentedVideoTime = hasPresentedVideoTime,
                PresentedVideoTimeSec = hasPresentedVideoTime ? presentedVideoTimeSec : -1.0,
                HasAvSyncSample = hasAvSyncSample,
                AudioPresentedTimeSec = audioPresentedTimeSec,
                AudioPipelineDelaySec = audioPipelineDelaySec,
                AvSyncDeltaMilliseconds = avSyncDeltaMilliseconds,
                ReferencePlaybackTimeSec = referencePlaybackTime,
                ReferencePlaybackKind = referencePlaybackKind,
                PlaybackContractAudioTimeSec = playbackTimingContract.AudioTimeSec,
                PlaybackContractAudioPresentedTimeSec =
                    playbackTimingContract.AudioPresentedTimeSec,
                PlaybackContractAudioSinkDelaySec = playbackTimingContract.AudioSinkDelaySec,
                HasRealtimeLatencySample = hasRealtimeLatencySample,
                RealtimeLatencyMilliseconds = realtimeLatencyMilliseconds,
                PublisherElapsedTimeSec = publisherElapsedTimeSec,
                HasRealtimeProbeSample = hasRealtimeProbeSample,
                RealtimeProbeUnixMs = realtimeProbeUnixMs,
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
                PlaybackContractAvailable = playbackContractObservation.Available,
                PlaybackContractHasUsMirror = playbackContractObservation.HasMicrosecondMirror,
                PlaybackContractAudit = playbackContractObservation,
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
                AudioOutputPolicyAvailable = audioOutputPolicyAvailable,
                AudioOutputPolicyAudit = audioOutputPolicyObservation,
                AvSyncEnterpriseAvailable = avSyncEnterpriseObservation.Available,
                AvSyncEnterpriseSampleCount = avSyncEnterpriseObservation.SampleCount,
                AvSyncEnterpriseDriftProjected2hMs =
                    avSyncEnterpriseObservation.DriftProjected2hMs,
                AvSyncEnterpriseAudit = avSyncEnterpriseObservation,
                PassiveAvSyncAudit = passiveAvSyncObservation,
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
                MediaNativeInteropCommon.CreateValidationWindowStartedLogLine(
                    ValidationLogPrefix,
                    reason,
                    startupElapsed));
        }

        private ValidationSnapshot ResolveValidationWindowSnapshot(ValidationSnapshot fallbackSnapshot)
        {
            return _hasValidationWindowSnapshot ? _lastValidationWindowSnapshot : fallbackSnapshot;
        }

        private MediaNativeInteropCommon.MediaPlayerValidationGateInputsView
            CreateValidationGateInputs(ValidationSnapshot snapshot)
        {
            return new MediaNativeInteropCommon.MediaPlayerValidationGateInputsView
            {
                HasTexture = snapshot.HasTexture,
                AudioPlaying = snapshot.AudioPlaying,
                Started = snapshot.Started,
                RuntimePlaybackConfirmed = snapshot.RuntimePlaybackConfirmed,
                HasPresentedNativeVideoFrame = snapshot.HasPresentedNativeVideoFrame,
                PlaybackTimeSec = snapshot.ValidationGatePlaybackTimeSec,
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
                MediaNativeInteropCommon.AccumulateMediaPlayerValidationWindowEvidenceObservation(
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
            var resultObservation =
                MediaNativeInteropCommon.CreateMediaPlayerValidationResultObservation(
                    _validationWindowStartReason,
                    CreateValidationEvidenceObservation(),
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
                MediaNativeInteropCommon.AppendValidationSummaryHeader(
                    builder,
                    new MediaNativeInteropCommon.ValidationSummaryHeaderView
                    {
                        Passed = result.Passed,
                        Reason = result.Reason,
                        Uri = summarySnapshot.Uri,
                        RequestedBackend = summarySnapshot.RequestedBackend,
                        ActualBackend = summarySnapshot.ActualBackend,
                        PlaybackAdvanceSeconds = result.PlaybackAdvanceSeconds,
                    });
                MediaNativeInteropCommon.AppendValidationSummaryWindow(
                    builder,
                    new MediaNativeInteropCommon.ValidationSummaryWindowView
                    {
                        HasTexture = summarySnapshot.HasTexture,
                        AudioPlaying = summarySnapshot.AudioPlaying,
                        Started = summarySnapshot.Started,
                        IncludeAudioSourcePresent = true,
                        AudioSourcePresent = summarySnapshot.AudioSourcePresent,
                        IncludeHasAudioListener = true,
                        HasAudioListener = summarySnapshot.HasAudioListener,
                        ObservedTextureDuringWindow = _observedTextureDuringWindow,
                        ObservedAudioDuringWindow = _observedAudioDuringWindow,
                        ObservedStartedDuringWindow = _observedStartedDuringWindow,
                        IncludeObservedNativeFrameDuringWindow = true,
                        ObservedNativeFrameDuringWindow = _observedNativeFrameDuringWindow,
                        IncludeValidationWindowStartReason = true,
                        ValidationWindowStartReason = _validationWindowStartReason,
                    });
                MediaNativeInteropCommon.AppendValidationSummarySourceRuntime(
                    builder,
                    MediaNativeInteropCommon.CreateValidationSummarySourceRuntime(
                        summarySnapshot.SourceState,
                        summarySnapshot.SourcePackets,
                        summarySnapshot.SourceTimeouts,
                        summarySnapshot.SourceReconnects));
                MediaNativeInteropCommon.AppendValidationSummaryNativeVideoRuntime(
                    builder,
                    MediaNativeInteropCommon.CreateValidationSummaryNativeVideoRuntime(
                        summarySnapshot.NativeVideoActive,
                        summarySnapshot.NativeActivationDecision,
                        summarySnapshot.HasPresentedNativeVideoFrame));
                MediaNativeInteropCommon.AppendValidationSummaryPlayerSession(
                    builder,
                    summaryPlayerSession);
                MediaNativeInteropCommon.AppendValidationSummarySourceTimeline(
                    builder,
                    summarySourceTimeline);
                MediaNativeInteropCommon.AppendValidationSummaryPlaybackContract(
                    builder,
                    summaryPlaybackContract);
                MediaNativeInteropCommon.AppendValidationSummaryAvSyncContract(
                    builder,
                    summaryAvSyncContract);
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

            if (HasExplicitWindowOverride())
            {
                ConfigureWindow(Player.Width, Player.Height, "explicit-override");
                ConfigureView(Player.Width, Player.Height);
                _windowConfigured = true;
                return;
            }

            var texture = Player.TargetMaterial != null ? Player.TargetMaterial.mainTexture : null;
            if (texture == null)
            {
                return;
            }

            if (_sourceSizedWindowApplied)
            {
                return;
            }

            ConfigureWindow(texture.width, texture.height, "texture-fallback");
            ConfigureView(texture.width, texture.height);
            _windowConfigured = true;
            _sourceSizedWindowApplied = true;
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
            public double PlaybackTime;
            public double ValidationGatePlaybackTimeSec;
            public bool HasTexture;
            public bool AudioPlaying;
            public bool AudioSourcePresent;
            public bool HasAudioListener;
            public bool Started;
            public bool RuntimePlaybackConfirmed;
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

