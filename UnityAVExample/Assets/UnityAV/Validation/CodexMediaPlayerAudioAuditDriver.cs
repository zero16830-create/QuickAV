using System;
using System.Collections;
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

            ValidationSeconds = TryReadFloatArgument(
                ValidationSecondsArgumentName,
                ValidationSeconds);
            StartupTimeoutSeconds = TryReadFloatArgument(
                StartupTimeoutSecondsArgumentName,
                StartupTimeoutSeconds);

            _requestedWindowWidth = TryReadIntArgument(
                WindowWidthArgumentName,
                Player.Width,
                out _hasExplicitWindowWidth);
            _requestedWindowHeight = TryReadIntArgument(
                WindowHeightArgumentName,
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
            var validationPassed = EvaluateValidationResult(finalSnapshot);
            var exitCode = validationPassed ? 0 : 2;
            yield return QuitAfterDelay(0.5f, exitCode);
        }

        private ValidationSnapshot EmitStatus()
        {
            var snapshot = CaptureSnapshot();
            var actualRenderer = snapshot.NativeVideoActive
                ? Player.NativeVideoPresentationPath.ToString()
                : "TextureFallback";

            Debug.Log(
                string.Format(
                    "[CodexValidation] time={0:F3}s texture={1} audioPlaying={2} started={3} startupElapsed={4:F3}s sourceState={5} sourcePackets={6} sourceTimeouts={7} sourceReconnects={8} window={9}x{10} textureSize={11}x{12} fullscreen={13} mode={14} backend={15} requested_renderer={16} actual_renderer={17}",
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
                    actualRenderer));
            Debug.Log(
                string.Format(
                    "[CodexValidation] media_player_audit audioSourcePresent={0} hasAudioListener={1} nativeVideoActive={2} nativeActivationDecision={3} nativeFrame={4} actualBackend={5}",
                    snapshot.AudioSourcePresent,
                    snapshot.HasAudioListener,
                    snapshot.NativeVideoActive,
                    snapshot.NativeActivationDecision,
                    snapshot.HasPresentedNativeVideoFrame,
                    Player.ActualBackendKind));

            return snapshot;
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

            if (playbackAdvance < MinimumPlaybackAdvanceSeconds)
            {
                Debug.LogError(
                    string.Format(
                        "[CodexValidation] result=failed reason=playback-stalled advance={0:F3}s",
                        playbackAdvance));
                return false;
            }

            var textureObserved = _observedTextureDuringWindow || _observedNativeFrameDuringWindow;
            if (!textureObserved)
            {
                Debug.LogError(
                    "[CodexValidation] result=failed reason=missing-video-signal");
                return false;
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
        }
    }
}
