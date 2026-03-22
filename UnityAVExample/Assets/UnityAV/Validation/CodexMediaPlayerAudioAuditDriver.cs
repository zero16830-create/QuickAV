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
        private const float MinimumPlaybackAdvanceSeconds = 1.0f;

        public MediaPlayer Player;
        public float ValidationSeconds = 12f;
        public float StartupTimeoutSeconds = 10f;
        public float LogIntervalSeconds = 1f;
        public string UriArgumentName = "-uri=";
        public string BackendArgumentName = "-backend=";
        public string ValidationSecondsArgumentName = "-validationSeconds=";
        public string StartupTimeoutSecondsArgumentName = "-startupTimeoutSeconds=";
        public string WindowWidthArgumentName = "-windowWidth=";
        public string WindowHeightArgumentName = "-windowHeight=";
        public string AndroidUriExtraName = "rustavUri";
        public string AndroidBackendExtraName = "rustavBackend";
        public string AndroidValidationSecondsExtraName = "rustavValidationSeconds";
        public string AndroidStartupTimeoutSecondsExtraName = "rustavStartupTimeoutSeconds";
        public string AndroidWindowWidthExtraName = "rustavWindowWidth";
        public string AndroidWindowHeightExtraName = "rustavWindowHeight";
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
        private bool _observedTextureDuringWindow;
        private bool _observedAudioDuringWindow;
        private bool _observedStartedDuringWindow;
        private bool _observedNativeFrameDuringWindow;
        private bool _hasAudioListener;
        private bool _observedPlaybackContractAvailable;
        private string _observedPlaybackContractMasterTimeSec = "n/a";
        private string _observedPlaybackContractMasterTimeUs = "n/a";
        private string _observedPlaybackContractExternalTimeSec = "n/a";
        private string _observedPlaybackContractExternalTimeUs = "n/a";
        private string _observedPlaybackContractHasUsMirror = "False";
        private bool _observedSourceTimelineAvailable;
        private string _observedSourceTimelineModel = "n/a";
        private string _observedSourceTimelineAnchorKind = "n/a";
        private string _observedSourceTimelineIsRealtime = "False";
        private string _observedSourceTimelineHasCurrentSourceTimeUs = "False";
        private string _observedSourceTimelineCurrentSourceTimeUs = "n/a";
        private string _observedSourceTimelineHasTimelineOriginUs = "False";
        private string _observedSourceTimelineTimelineOriginUs = "n/a";
        private string _observedSourceTimelineHasAnchorValueUs = "False";
        private string _observedSourceTimelineAnchorValueUs = "n/a";
        private string _observedSourceTimelineHasAnchorMonoUs = "False";
        private string _observedSourceTimelineAnchorMonoUs = "n/a";
        private bool _observedPlayerSessionAvailable;
        private string _observedPlayerSessionLifecycleState = "n/a";
        private string _observedPlayerSessionPublicState = "n/a";
        private string _observedPlayerSessionRuntimeState = "n/a";
        private string _observedPlayerSessionPlaybackIntent = "n/a";
        private string _observedPlayerSessionStopReason = "n/a";
        private string _observedPlayerSessionSourceState = "n/a";
        private string _observedPlayerSessionCanSeek = "False";
        private string _observedPlayerSessionIsRealtime = "False";
        private string _observedPlayerSessionIsBuffering = "False";
        private string _observedPlayerSessionIsSyncing = "False";
        private bool _observedAudioOutputPolicyAvailable;
        private string _observedAudioOutputPolicyFileStartMs = "n/a";
        private string _observedAudioOutputPolicyAndroidFileStartMs = "n/a";
        private string _observedAudioOutputPolicyRealtimeStartMs = "n/a";
        private string _observedAudioOutputPolicyRealtimeStartupGraceMs = "n/a";
        private string _observedAudioOutputPolicyRealtimeStartupMinimumThresholdMs = "n/a";
        private string _observedAudioOutputPolicyFileRingCapacityMs = "n/a";
        private string _observedAudioOutputPolicyAndroidFileRingCapacityMs = "n/a";
        private string _observedAudioOutputPolicyRealtimeRingCapacityMs = "n/a";
        private string _observedAudioOutputPolicyFileBufferedCeilingMs = "n/a";
        private string _observedAudioOutputPolicyAndroidFileBufferedCeilingMs = "n/a";
        private string _observedAudioOutputPolicyRealtimeBufferedCeilingMs = "n/a";
        private string _observedAudioOutputPolicyRealtimeStartupAdditionalSinkDelayMs = "n/a";
        private string _observedAudioOutputPolicyRealtimeSteadyAdditionalSinkDelayMs = "n/a";
        private string _observedAudioOutputPolicyRealtimeBackendAdditionalSinkDelayMs = "n/a";
        private string _observedAudioOutputPolicyRealtimeStartRequiresVideoFrame = "False";
        private string _observedAudioOutputPolicyAllowAndroidFileOutputRateBridge = "False";
        private bool _observedAvSyncEnterpriseAvailable;
        private string _observedAvSyncEnterpriseSampleCount = "n/a";
        private string _observedAvSyncEnterpriseDriftProjected2hMs = "n/a";
        private bool _observedPassiveAvSyncAvailable;
        private string _observedPassiveAvSyncRawOffsetUs = "n/a";
        private string _observedPassiveAvSyncSmoothOffsetUs = "n/a";
        private string _observedPassiveAvSyncDriftPpm = "n/a";
        private string _observedPassiveAvSyncDriftInterceptUs = "n/a";
        private string _observedPassiveAvSyncDriftSampleCount = "n/a";
        private string _observedPassiveAvSyncVideoSchedule = "n/a";
        private string _observedPassiveAvSyncAudioResampleRatio = "n/a";
        private string _observedPassiveAvSyncAudioResampleActive = "False";
        private string _observedPassiveAvSyncShouldRebuildAnchor = "False";

        private void ApplyObservedPlaybackTiming(
            MediaNativeInteropCommon.PlaybackTimingAuditStringsView observation)
        {
            _observedPlaybackContractAvailable = observation.Available;
            _observedPlaybackContractMasterTimeSec = observation.MasterTimeSec;
            _observedPlaybackContractMasterTimeUs = observation.MasterTimeUs;
            _observedPlaybackContractExternalTimeSec = observation.ExternalTimeSec;
            _observedPlaybackContractExternalTimeUs = observation.ExternalTimeUs;
            _observedPlaybackContractHasUsMirror = observation.HasMicrosecondMirror;
        }

        private void ApplyObservedPlayerSession(
            MediaNativeInteropCommon.PlayerSessionAuditStringsView observation)
        {
            _observedPlayerSessionAvailable = observation.Available;
            _observedPlayerSessionLifecycleState = observation.LifecycleState;
            _observedPlayerSessionPublicState = observation.PublicState;
            _observedPlayerSessionRuntimeState = observation.RuntimeState;
            _observedPlayerSessionPlaybackIntent = observation.PlaybackIntent;
            _observedPlayerSessionStopReason = observation.StopReason;
            _observedPlayerSessionSourceState = observation.SourceState;
            _observedPlayerSessionCanSeek = observation.CanSeek;
            _observedPlayerSessionIsRealtime = observation.IsRealtime;
            _observedPlayerSessionIsBuffering = observation.IsBuffering;
            _observedPlayerSessionIsSyncing = observation.IsSyncing;
        }

        private void ApplyObservedSourceTimeline(
            MediaNativeInteropCommon.SourceTimelineAuditStringsView observation)
        {
            _observedSourceTimelineAvailable = observation.Available;
            _observedSourceTimelineModel = observation.Model;
            _observedSourceTimelineAnchorKind = observation.AnchorKind;
            _observedSourceTimelineIsRealtime = observation.IsRealtime;
            _observedSourceTimelineHasCurrentSourceTimeUs = observation.HasCurrentSourceTimeUs;
            _observedSourceTimelineCurrentSourceTimeUs = observation.CurrentSourceTimeUs;
            _observedSourceTimelineHasTimelineOriginUs = observation.HasTimelineOriginUs;
            _observedSourceTimelineTimelineOriginUs = observation.TimelineOriginUs;
            _observedSourceTimelineHasAnchorValueUs = observation.HasAnchorValueUs;
            _observedSourceTimelineAnchorValueUs = observation.AnchorValueUs;
            _observedSourceTimelineHasAnchorMonoUs = observation.HasAnchorMonoUs;
            _observedSourceTimelineAnchorMonoUs = observation.AnchorMonoUs;
        }

        private void ApplyObservedAudioOutputPolicy(
            MediaNativeInteropCommon.AudioOutputPolicyAuditStringsView observation)
        {
            _observedAudioOutputPolicyAvailable = observation.Available;
            _observedAudioOutputPolicyFileStartMs = observation.FileStartThresholdMilliseconds;
            _observedAudioOutputPolicyAndroidFileStartMs =
                observation.AndroidFileStartThresholdMilliseconds;
            _observedAudioOutputPolicyRealtimeStartMs =
                observation.RealtimeStartThresholdMilliseconds;
            _observedAudioOutputPolicyRealtimeStartupGraceMs =
                observation.RealtimeStartupGraceMilliseconds;
            _observedAudioOutputPolicyRealtimeStartupMinimumThresholdMs =
                observation.RealtimeStartupMinimumThresholdMilliseconds;
            _observedAudioOutputPolicyFileRingCapacityMs =
                observation.FileRingCapacityMilliseconds;
            _observedAudioOutputPolicyAndroidFileRingCapacityMs =
                observation.AndroidFileRingCapacityMilliseconds;
            _observedAudioOutputPolicyRealtimeRingCapacityMs =
                observation.RealtimeRingCapacityMilliseconds;
            _observedAudioOutputPolicyFileBufferedCeilingMs =
                observation.FileBufferedCeilingMilliseconds;
            _observedAudioOutputPolicyAndroidFileBufferedCeilingMs =
                observation.AndroidFileBufferedCeilingMilliseconds;
            _observedAudioOutputPolicyRealtimeBufferedCeilingMs =
                observation.RealtimeBufferedCeilingMilliseconds;
            _observedAudioOutputPolicyRealtimeStartupAdditionalSinkDelayMs =
                observation.RealtimeStartupAdditionalSinkDelayMilliseconds;
            _observedAudioOutputPolicyRealtimeSteadyAdditionalSinkDelayMs =
                observation.RealtimeSteadyAdditionalSinkDelayMilliseconds;
            _observedAudioOutputPolicyRealtimeBackendAdditionalSinkDelayMs =
                observation.RealtimeBackendAdditionalSinkDelayMilliseconds;
            _observedAudioOutputPolicyRealtimeStartRequiresVideoFrame =
                observation.RealtimeStartRequiresVideoFrame;
            _observedAudioOutputPolicyAllowAndroidFileOutputRateBridge =
                observation.AllowAndroidFileOutputRateBridge;
        }

        private void ApplyObservedAvSyncEnterprise(
            MediaNativeInteropCommon.AvSyncEnterpriseAuditStringsView observation)
        {
            _observedAvSyncEnterpriseAvailable = observation.Available;
            _observedAvSyncEnterpriseSampleCount = observation.SampleCount;
            _observedAvSyncEnterpriseDriftProjected2hMs = observation.DriftProjected2hMs;
        }

        private void ApplyObservedPassiveAvSync(
            MediaNativeInteropCommon.PassiveAvSyncAuditStringsView observation)
        {
            _observedPassiveAvSyncAvailable = observation.Available;
            _observedPassiveAvSyncRawOffsetUs = observation.RawOffsetUs;
            _observedPassiveAvSyncSmoothOffsetUs = observation.SmoothOffsetUs;
            _observedPassiveAvSyncDriftPpm = observation.DriftPpm;
            _observedPassiveAvSyncDriftInterceptUs = observation.DriftInterceptUs;
            _observedPassiveAvSyncDriftSampleCount = observation.DriftSampleCount;
            _observedPassiveAvSyncVideoSchedule = observation.VideoSchedule;
            _observedPassiveAvSyncAudioResampleRatio = observation.AudioResampleRatio;
            _observedPassiveAvSyncAudioResampleActive = observation.AudioResampleActive;
            _observedPassiveAvSyncShouldRebuildAnchor = observation.ShouldRebuildAnchor;
        }

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

            ValidationSeconds = TryReadFloatArgument(
                ValidationSecondsArgumentName,
                AndroidValidationSecondsExtraName,
                ValidationSeconds);
            StartupTimeoutSeconds = TryReadFloatArgument(
                StartupTimeoutSecondsArgumentName,
                AndroidStartupTimeoutSecondsExtraName,
                StartupTimeoutSeconds);

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
                Debug.LogError("[CodexValidation] missing MediaPlayer");
                StartCoroutine(QuitAfterDelay(1f, 2));
                return;
            }

            Application.runInBackground = true;
            Debug.Log("[CodexValidation] runInBackground=True");

            _hasAudioListener = FindObjectsOfType<AudioListener>().Length > 0;
            _lastLogTime = Time.realtimeSinceStartup;
            _startTime = _lastLogTime;
            Debug.Log(
                string.Format(
                    "[CodexValidation] start validation seconds={0:F1} requestedWindow={1}x{2} explicitWindow={3} media_player_audit=True",
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
                    var outputsReady = snapshot.Started
                        || snapshot.PlaybackTime >= 0.1
                        || snapshot.HasPresentedNativeVideoFrame;
                    if (outputsReady)
                    {
                        StartValidationWindow(
                            now,
                            startupElapsed,
                            "playback-start",
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
            var validationResult = EvaluateValidationResult(finalSnapshot);
            WriteValidationSummary(validationResult, finalSnapshot);
            var exitCode = validationResult.Passed ? 0 : 2;
            yield return QuitAfterDelay(0.5f, exitCode);
        }

        private ValidationSnapshot EmitStatus()
        {
            var snapshot = CaptureSnapshot();
            UpdateObservedContractState();
            var actualRenderer = snapshot.NativeVideoActive
                ? Player.NativeVideoPresentationPath.ToString()
                : "TextureFallback";

            Debug.Log(
                string.Format(
                    "[CodexValidation] time={0:F3}s texture={1} audioPlaying={2} started={3} startupElapsed={4:F3}s sourceState={5} sourcePackets={6} sourceTimeouts={7} sourceReconnects={8} window={9}x{10} textureSize={11}x{12} fullscreen={13} mode={14} backend={15} requested_renderer={16} actual_renderer={17} playback_contract_available={18} playback_contract_master_sec={19} playback_contract_has_us_mirror={20} source_timeline_available={21} source_timeline_model={22} source_timeline_anchor_kind={23} player_session_available={24} player_session_lifecycle={25} player_session_is_realtime={26} audio_output_policy_available={27} av_sync_enterprise_available={28} av_sync_enterprise_sample_count={29} av_sync_enterprise_drift_projected_2h_ms={30}",
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
                    "NativeVideoPreferred",
                    actualRenderer,
                    _observedPlaybackContractAvailable,
                    _observedPlaybackContractMasterTimeSec,
                    _observedPlaybackContractHasUsMirror,
                    _observedSourceTimelineAvailable,
                    _observedSourceTimelineModel,
                    _observedSourceTimelineAnchorKind,
                    _observedPlayerSessionAvailable,
                    _observedPlayerSessionLifecycleState,
                    _observedPlayerSessionIsRealtime,
                    _observedAudioOutputPolicyAvailable,
                    _observedAvSyncEnterpriseAvailable,
                    _observedAvSyncEnterpriseSampleCount,
                    _observedAvSyncEnterpriseDriftProjected2hMs));
            Debug.Log(
                string.Format(
                    "[CodexValidation] media_player_audit audioSourcePresent={0} hasAudioListener={1} nativeVideoActive={2} nativeActivationDecision={3} nativeFrame={4} actualBackend={5}",
                    snapshot.AudioSourcePresent,
                    snapshot.HasAudioListener,
                    snapshot.NativeVideoActive,
                    snapshot.NativeActivationDecision,
                    snapshot.HasPresentedNativeVideoFrame,
                    Player.ActualBackendKind));

            if (snapshot.PlayerSessionAvailable)
            {
                Debug.Log(
                    string.Format(
                        "[CodexValidation] player_session_detail lifecycle={0} public={1} runtime={2} intent={3} stop_reason={4} source_state={5} can_seek={6} realtime={7} buffering={8} syncing={9}",
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

            return snapshot;
        }

        private void UpdateObservedContractState()
        {
            if (Player == null)
            {
                return;
            }

            MediaNativeInteropCommon.PlaybackTimingContractView playbackContract;
            ApplyObservedPlaybackTiming(
                MediaNativeInteropCommon.CreatePlaybackTimingAuditStrings(
                    Player.TryGetPlaybackTimingContract(out playbackContract),
                    playbackContract));

            MediaNativeInteropCommon.SourceTimelineContractView sourceTimeline;
            ApplyObservedSourceTimeline(
                MediaNativeInteropCommon.CreateSourceTimelineAuditStrings(
                    Player.TryGetSourceTimelineContract(out sourceTimeline),
                    sourceTimeline));

            MediaNativeInteropCommon.PlayerSessionContractView playerSession;
            ApplyObservedPlayerSession(
                MediaNativeInteropCommon.CreatePlayerSessionAuditStrings(
                    Player.TryGetPlayerSessionContract(out playerSession),
                    playerSession));

            MediaNativeInteropCommon.AudioOutputPolicyView audioOutputPolicy;
            ApplyObservedAudioOutputPolicy(
                MediaNativeInteropCommon.CreateAudioOutputPolicyAuditStrings(
                    Player.TryGetAudioOutputPolicy(out audioOutputPolicy),
                    audioOutputPolicy));

            MediaNativeInteropCommon.AvSyncEnterpriseMetricsView enterpriseMetrics;
            ApplyObservedAvSyncEnterprise(
                MediaNativeInteropCommon.CreateAvSyncEnterpriseAuditStrings(
                    Player.TryGetAvSyncEnterpriseMetrics(out enterpriseMetrics),
                    enterpriseMetrics));

            MediaNativeInteropCommon.PassiveAvSyncSnapshotView passiveAvSyncSnapshot;
            ApplyObservedPassiveAvSync(
                MediaNativeInteropCommon.CreatePassiveAvSyncAuditStrings(
                    Player.TryGetPassiveAvSyncSnapshot(out passiveAvSyncSnapshot),
                    passiveAvSyncSnapshot));
        }

        private ValidationSnapshot CaptureSnapshot()
        {
            var playbackTime = SafeReadPlaybackTime();
            var texture = Player.TargetMaterial != null ? Player.TargetMaterial.mainTexture : null;
            var hasTexture = texture != null;
            var audioSource = Player.GetComponent<AudioSource>();
            var audioPlaying = audioSource != null && audioSource.isPlaying;

            MediaPlayer.PlayerRuntimeHealth health;
            var hasHealth = Player.TryGetRuntimeHealth(out health);
            var started = hasHealth ? health.IsPlaying : playbackTime >= 0.0;
            MediaNativeInteropCommon.PlayerSessionContractView playerSessionContract;
            var playerSessionAvailable = Player.TryGetPlayerSessionContract(out playerSessionContract);
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
            MediaNativeInteropCommon.AvSyncEnterpriseMetricsView enterpriseMetrics;
            var avSyncEnterpriseObservation =
                MediaNativeInteropCommon.CreateAvSyncEnterpriseAuditStrings(
                    Player.TryGetAvSyncEnterpriseMetrics(out enterpriseMetrics),
                    enterpriseMetrics);
            MediaNativeInteropCommon.AudioOutputPolicyView audioOutputPolicy;
            var audioOutputPolicyAvailable = Player.TryGetAudioOutputPolicy(out audioOutputPolicy);

            return new ValidationSnapshot
            {
                PlaybackTime = playbackTime,
                HasTexture = hasTexture,
                AudioPlaying = audioPlaying,
                AudioSourcePresent = audioSource != null,
                HasAudioListener = _hasAudioListener,
                Started = started,
                TextureWidth = texture != null ? texture.width : 0,
                TextureHeight = texture != null ? texture.height : 0,
                SourceState = hasHealth ? health.SourceConnectionState.ToString() : "Unavailable",
                SourcePackets = hasHealth ? health.SourcePacketCount.ToString() : "-1",
                SourceTimeouts = hasHealth ? health.SourceTimeoutCount.ToString() : "-1",
                SourceReconnects = hasHealth ? health.SourceReconnectCount.ToString() : "-1",
                NativeVideoActive = Player.IsNativeVideoPathActive,
                NativeActivationDecision = Player.NativeVideoActivationDecision.ToString(),
                HasPresentedNativeVideoFrame = Player.HasPresentedNativeVideoFrame,
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
                SourceTimelineAvailable = sourceTimelineObservation.Available,
                SourceTimelineModel = sourceTimelineObservation.Model,
                SourceTimelineAnchorKind = sourceTimelineObservation.AnchorKind,
                PlaybackContractAvailable = playbackContractObservation.Available,
                PlaybackContractHasUsMirror = playbackContractObservation.HasMicrosecondMirror,
                AudioOutputPolicyAvailable = audioOutputPolicyAvailable,
                AvSyncEnterpriseAvailable = avSyncEnterpriseObservation.Available,
                AvSyncEnterpriseSampleCount = avSyncEnterpriseObservation.SampleCount,
                AvSyncEnterpriseDriftProjected2hMs =
                    avSyncEnterpriseObservation.DriftProjected2hMs,
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
            _observedNativeFrameDuringWindow |= snapshot.HasPresentedNativeVideoFrame;
            if (snapshot.PlaybackTime > _maxObservedPlaybackTime)
            {
                _maxObservedPlaybackTime = snapshot.PlaybackTime;
            }
        }

        private ValidationResultInfo EvaluateValidationResult(ValidationSnapshot finalSnapshot)
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
                return ValidationResultInfo.Failed(
                    "startup-timeout-no-playback",
                    playbackAdvance);
            }

            if (!_observedStartedDuringWindow)
            {
                Debug.LogError(
                    "[CodexValidation] result=failed reason=playback-not-started");
                return ValidationResultInfo.Failed(
                    "playback-not-started",
                    playbackAdvance);
            }

            if (playbackAdvance < MinimumPlaybackAdvanceSeconds)
            {
                Debug.LogError(
                    string.Format(
                        "[CodexValidation] result=failed reason=playback-stalled advance={0:F3}s",
                        playbackAdvance));
                return ValidationResultInfo.Failed(
                    "playback-stalled",
                    playbackAdvance);
            }

            var textureObserved = _observedTextureDuringWindow || _observedNativeFrameDuringWindow;
            if (!textureObserved)
            {
                Debug.LogError(
                    "[CodexValidation] result=failed reason=missing-video-signal");
                return ValidationResultInfo.Failed(
                    "missing-video-signal",
                    playbackAdvance);
            }

            var reason = _observedAudioDuringWindow
                ? "steady-playback-with-audio"
                : "steady-playback-no-audio";
            Debug.Log(
                string.Format(
                    "[CodexValidation] result=passed reason={0} advance={1:F3}s sourceState={2} sourceTimeouts={3} sourceReconnects={4}",
                    reason,
                    playbackAdvance,
                    finalSnapshot.SourceState,
                    finalSnapshot.SourceTimeouts,
                    finalSnapshot.SourceReconnects));
            Debug.Log("[CodexValidation] complete");
            return ValidationResultInfo.PassedWithAdvance(playbackAdvance, reason);
        }

        private void WriteValidationSummary(
            ValidationResultInfo result,
            ValidationSnapshot finalSnapshot)
        {
            try
            {
                UpdateObservedContractState();
                var summaryPath = Path.Combine(
                    Application.persistentDataPath,
                    SummaryFileName);
                var builder = new StringBuilder();
                var playerSessionLifecycleState = ResolveSummaryString(
                    _observedPlayerSessionLifecycleState,
                    finalSnapshot.PlayerSessionLifecycleState);
                var playerSessionPublicState = ResolveSummaryString(
                    _observedPlayerSessionPublicState,
                    finalSnapshot.PlayerSessionPublicState);
                var playerSessionRuntimeState = ResolveSummaryString(
                    _observedPlayerSessionRuntimeState,
                    finalSnapshot.PlayerSessionRuntimeState);
                var playerSessionPlaybackIntent = ResolveSummaryString(
                    _observedPlayerSessionPlaybackIntent,
                    finalSnapshot.PlayerSessionPlaybackIntent);
                var playerSessionStopReason = ResolveSummaryString(
                    _observedPlayerSessionStopReason,
                    finalSnapshot.PlayerSessionStopReason);
                var playerSessionSourceState = ResolveSummaryString(
                    _observedPlayerSessionSourceState,
                    finalSnapshot.PlayerSessionSourceState);
                var playerSessionCanSeek = ResolveSummaryString(
                    _observedPlayerSessionCanSeek,
                    finalSnapshot.PlayerSessionCanSeek);
                var playerSessionIsRealtime = ResolveSummaryString(
                    _observedPlayerSessionIsRealtime,
                    finalSnapshot.PlayerSessionIsRealtime);
                var playerSessionIsBuffering = ResolveSummaryString(
                    _observedPlayerSessionIsBuffering,
                    finalSnapshot.PlayerSessionIsBuffering);
                var playerSessionIsSyncing = ResolveSummaryString(
                    _observedPlayerSessionIsSyncing,
                    finalSnapshot.PlayerSessionIsSyncing);
                builder.AppendLine("validation_result=" + (result.Passed ? "passed" : "failed"));
                builder.AppendLine("reason=" + result.Reason);
                builder.AppendLine("uri=" + (Player != null ? Player.Uri : string.Empty));
                builder.AppendLine("requested_backend=" + (Player != null ? Player.PreferredBackend.ToString() : "Unavailable"));
                builder.AppendLine("actual_backend=" + (Player != null ? Player.ActualBackendKind.ToString() : "Unavailable"));
                builder.AppendLine("playback_advance_sec=" + result.PlaybackAdvanceSeconds.ToString("F3"));
                builder.AppendLine("has_texture=" + finalSnapshot.HasTexture);
                builder.AppendLine("audio_playing=" + finalSnapshot.AudioPlaying);
                builder.AppendLine("started=" + finalSnapshot.Started);
                builder.AppendLine("observed_texture_during_window=" + _observedTextureDuringWindow);
                builder.AppendLine("observed_audio_during_window=" + _observedAudioDuringWindow);
                builder.AppendLine("observed_started_during_window=" + _observedStartedDuringWindow);
                builder.AppendLine("validation_window_start_reason=" + _validationWindowStartReason);
                builder.AppendLine("source_state=" + finalSnapshot.SourceState);
                builder.AppendLine("source_packets=" + finalSnapshot.SourcePackets);
                builder.AppendLine("source_timeouts=" + finalSnapshot.SourceTimeouts);
                builder.AppendLine("source_reconnects=" + finalSnapshot.SourceReconnects);
                builder.AppendLine("native_video_active=" + finalSnapshot.NativeVideoActive);
                builder.AppendLine("native_activation_decision=" + finalSnapshot.NativeActivationDecision);
                builder.AppendLine("has_presented_native_video_frame=" + finalSnapshot.HasPresentedNativeVideoFrame);
                builder.AppendLine("player_session_available=" + _observedPlayerSessionAvailable);
                builder.AppendLine("player_session_lifecycle_state=" + playerSessionLifecycleState);
                builder.AppendLine("player_session_public_state=" + playerSessionPublicState);
                builder.AppendLine("player_session_runtime_state=" + playerSessionRuntimeState);
                builder.AppendLine("player_session_playback_intent=" + playerSessionPlaybackIntent);
                builder.AppendLine("player_session_stop_reason=" + playerSessionStopReason);
                builder.AppendLine("player_session_source_state=" + playerSessionSourceState);
                builder.AppendLine("player_session_can_seek=" + playerSessionCanSeek);
                builder.AppendLine("player_session_is_realtime=" + playerSessionIsRealtime);
                builder.AppendLine("player_session_is_buffering=" + playerSessionIsBuffering);
                builder.AppendLine("player_session_is_syncing=" + playerSessionIsSyncing);
                builder.AppendLine("source_timeline_available=" + _observedSourceTimelineAvailable);
                builder.AppendLine("source_timeline_model=" + _observedSourceTimelineModel);
                builder.AppendLine("source_timeline_anchor_kind=" + _observedSourceTimelineAnchorKind);
                builder.AppendLine("source_timeline_is_realtime=" + _observedSourceTimelineIsRealtime);
                builder.AppendLine("source_timeline_has_current_source_time_us=" + _observedSourceTimelineHasCurrentSourceTimeUs);
                builder.AppendLine("source_timeline_current_source_time_us=" + _observedSourceTimelineCurrentSourceTimeUs);
                builder.AppendLine("source_timeline_has_timeline_origin_us=" + _observedSourceTimelineHasTimelineOriginUs);
                builder.AppendLine("source_timeline_timeline_origin_us=" + _observedSourceTimelineTimelineOriginUs);
                builder.AppendLine("source_timeline_has_anchor_value_us=" + _observedSourceTimelineHasAnchorValueUs);
                builder.AppendLine("source_timeline_anchor_value_us=" + _observedSourceTimelineAnchorValueUs);
                builder.AppendLine("source_timeline_has_anchor_mono_us=" + _observedSourceTimelineHasAnchorMonoUs);
                builder.AppendLine("source_timeline_anchor_mono_us=" + _observedSourceTimelineAnchorMonoUs);
                builder.AppendLine("playback_contract_available=" + _observedPlaybackContractAvailable);
                builder.AppendLine("playback_contract_master_sec=" + _observedPlaybackContractMasterTimeSec);
                builder.AppendLine("playback_contract_master_us=" + _observedPlaybackContractMasterTimeUs);
                builder.AppendLine("playback_contract_external_sec=" + _observedPlaybackContractExternalTimeSec);
                builder.AppendLine("playback_contract_external_us=" + _observedPlaybackContractExternalTimeUs);
                builder.AppendLine("playback_contract_has_us_mirror=" + _observedPlaybackContractHasUsMirror);
                builder.AppendLine("audio_output_policy_available=" + _observedAudioOutputPolicyAvailable);
                builder.AppendLine("audio_output_policy_file_start_ms=" + _observedAudioOutputPolicyFileStartMs);
                builder.AppendLine("audio_output_policy_android_file_start_ms=" + _observedAudioOutputPolicyAndroidFileStartMs);
                builder.AppendLine("audio_output_policy_realtime_start_ms=" + _observedAudioOutputPolicyRealtimeStartMs);
                builder.AppendLine("audio_output_policy_realtime_startup_grace_ms=" + _observedAudioOutputPolicyRealtimeStartupGraceMs);
                builder.AppendLine("audio_output_policy_realtime_startup_minimum_threshold_ms=" + _observedAudioOutputPolicyRealtimeStartupMinimumThresholdMs);
                builder.AppendLine("audio_output_policy_file_ring_capacity_ms=" + _observedAudioOutputPolicyFileRingCapacityMs);
                builder.AppendLine("audio_output_policy_android_file_ring_capacity_ms=" + _observedAudioOutputPolicyAndroidFileRingCapacityMs);
                builder.AppendLine("audio_output_policy_realtime_ring_capacity_ms=" + _observedAudioOutputPolicyRealtimeRingCapacityMs);
                builder.AppendLine("audio_output_policy_file_buffered_ceiling_ms=" + _observedAudioOutputPolicyFileBufferedCeilingMs);
                builder.AppendLine("audio_output_policy_android_file_buffered_ceiling_ms=" + _observedAudioOutputPolicyAndroidFileBufferedCeilingMs);
                builder.AppendLine("audio_output_policy_realtime_buffered_ceiling_ms=" + _observedAudioOutputPolicyRealtimeBufferedCeilingMs);
                builder.AppendLine("audio_output_policy_realtime_startup_additional_sink_delay_ms=" + _observedAudioOutputPolicyRealtimeStartupAdditionalSinkDelayMs);
                builder.AppendLine("audio_output_policy_realtime_steady_additional_sink_delay_ms=" + _observedAudioOutputPolicyRealtimeSteadyAdditionalSinkDelayMs);
                builder.AppendLine("audio_output_policy_realtime_backend_additional_sink_delay_ms=" + _observedAudioOutputPolicyRealtimeBackendAdditionalSinkDelayMs);
                builder.AppendLine("audio_output_policy_realtime_start_requires_video_frame=" + _observedAudioOutputPolicyRealtimeStartRequiresVideoFrame);
                builder.AppendLine("audio_output_policy_allow_android_file_output_rate_bridge=" + _observedAudioOutputPolicyAllowAndroidFileOutputRateBridge);
                builder.AppendLine("av_sync_enterprise_available=" + _observedAvSyncEnterpriseAvailable);
                builder.AppendLine("av_sync_enterprise_sample_count=" + _observedAvSyncEnterpriseSampleCount);
                builder.AppendLine("av_sync_enterprise_drift_projected_2h_ms=" + _observedAvSyncEnterpriseDriftProjected2hMs);
                builder.AppendLine("passive_av_sync_available=" + _observedPassiveAvSyncAvailable);
                builder.AppendLine("passive_av_sync_raw_offset_us=" + _observedPassiveAvSyncRawOffsetUs);
                builder.AppendLine("passive_av_sync_smooth_offset_us=" + _observedPassiveAvSyncSmoothOffsetUs);
                builder.AppendLine("passive_av_sync_drift_ppm=" + _observedPassiveAvSyncDriftPpm);
                builder.AppendLine("passive_av_sync_drift_intercept_us=" + _observedPassiveAvSyncDriftInterceptUs);
                builder.AppendLine("passive_av_sync_drift_sample_count=" + _observedPassiveAvSyncDriftSampleCount);
                builder.AppendLine("passive_av_sync_video_schedule=" + _observedPassiveAvSyncVideoSchedule);
                builder.AppendLine("passive_av_sync_audio_resample_ratio=" + _observedPassiveAvSyncAudioResampleRatio);
                builder.AppendLine("passive_av_sync_audio_resample_active=" + _observedPassiveAvSyncAudioResampleActive);
                builder.AppendLine("passive_av_sync_should_rebuild_anchor=" + _observedPassiveAvSyncShouldRebuildAnchor);
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

        private static string ResolveSummaryString(string observed, string fallback)
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
            public bool AudioSourcePresent;
            public bool HasAudioListener;
            public bool Started;
            public int TextureWidth;
            public int TextureHeight;
            public string SourceState;
            public string SourcePackets;
            public string SourceTimeouts;
            public string SourceReconnects;
            public bool NativeVideoActive;
            public string NativeActivationDecision;
            public bool HasPresentedNativeVideoFrame;
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
            public bool SourceTimelineAvailable;
            public string SourceTimelineModel;
            public string SourceTimelineAnchorKind;
            public bool PlaybackContractAvailable;
            public string PlaybackContractHasUsMirror;
            public bool AudioOutputPolicyAvailable;
            public bool AvSyncEnterpriseAvailable;
            public string AvSyncEnterpriseSampleCount;
            public string AvSyncEnterpriseDriftProjected2hMs;
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
