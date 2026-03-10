using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace UnityAV
{
    /// <summary>
    /// Responsible for playing streamed media
    /// </summary>
    public class MediaPlayer : MonoBehaviour
    {
        private const string RTSPPrefix = "rtsp://";
        private const string RTMPPrefix = "rtmp://";

        private const int DefaultWidth = 1024;
        private const int DefaultHeight = 1024;
        private const int InvalidPlayerId = -1;
        private const uint RustAVPlayerOpenOptionsVersion = 1u;
        private const uint RustAVPlayerHealthSnapshotV2Version = 2u;
        private const int BackendDiagnosticBufferLength = 512;

        [StructLayout(LayoutKind.Sequential)]
        private struct RustAVPlayerOpenOptions
        {
            public uint StructSize;
            public uint StructVersion;
            public int BackendKind;
            public int StrictBackend;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RustAVPlayerHealthSnapshotV2
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
            public double ExternalTimeSec;
            public double AudioTimeSec;
            public double AudioPresentedTimeSec;
            public double AudioSinkDelaySec;
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

        public struct PlayerRuntimeHealth
        {
            public MediaSourceConnectionState SourceConnectionState;
            public bool IsConnected;
            public bool IsPlaying;
            public bool IsRealtime;
            public long SourcePacketCount;
            public long SourceTimeoutCount;
            public long SourceReconnectCount;
            public double SourceLastActivityAgeSec;
            public double CurrentTimeSec;
        }

        /// <summary>
        /// The uri of the media to stream
        /// </summary>
        [Header("Media Properties:")]
        public string Uri;

        /// <summary>
        /// Preferred native backend.
        /// </summary>
        public MediaBackendKind PreferredBackend = MediaBackendKind.Auto;

        /// <summary>
        /// Whether fallback is forbidden when PreferredBackend is specified.
        /// </summary>
        public bool StrictBackend;

        /// <summary>
        /// Should the media be looped?
        /// </summary>
        public bool Loop;

        /// <summary>
        /// Should the media play as soon as it's loaded?
        /// </summary>
        public bool AutoPlay;

        /// <summary>
        /// The width of the texture in pixels
        /// </summary>
        [Header("Video Target Properties:")]
        [Range(2, 4096)]
        public int Width = DefaultWidth;

        /// <summary>
        /// The height of the texture in pixels
        /// </summary>
        [Range(2, 4096)]
        public int Height = DefaultHeight;

        /// <summary>
        /// The material to apply any streaming video to
        /// </summary>
        public Material TargetMaterial;

        private Texture2D _targetTexture;
        private int _id = InvalidPlayerId;
        private bool _playRequested;
        private bool _resumeAfterPause;
        private MediaBackendKind _actualBackendKind = MediaBackendKind.Auto;

        public MediaBackendKind ActualBackendKind
        {
            get { return _actualBackendKind; }
        }

        private static bool IsRemoteUri(string uri)
        {
            if (string.IsNullOrEmpty(uri))
            {
                return false;
            }

            return uri.StartsWith(RTSPPrefix, StringComparison.OrdinalIgnoreCase)
                || uri.StartsWith(RTMPPrefix, StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveUri(string uri)
        {
            if (IsRemoteUri(uri))
            {
                return string.Copy(uri);
            }

            var path = Application.streamingAssetsPath + Path.DirectorySeparatorChar + uri;
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(path + " not found.");
            }

            return path;
        }

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerCreateTexture")]
        private static extern int GetPlayer(string uri, IntPtr texturePointer);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerCreateTextureEx")]
        private static extern int GetPlayerEx(
            string uri,
            IntPtr texturePointer,
            ref RustAVPlayerOpenOptions options);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerRelease")]
        private static extern int ReleasePlayer(int id);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerGetDuration")]
        private static extern double Duration(int id);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerGetTime")]
        private static extern double Time(int id);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerPlay")]
        private static extern int Play(int id);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerStop")]
        private static extern int Stop(int id);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerSeek")]
        private static extern int Seek(int id, double time);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerSetLoop")]
        private static extern int SetLoop(int id, bool loop);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerGetBackendKind")]
        private static extern int GetPlayerBackendKind(int id);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerGetHealthSnapshotV2")]
        private static extern int GetPlayerHealthSnapshotV2(
            int id,
            ref RustAVPlayerHealthSnapshotV2 snapshot);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_GetBackendRuntimeDiagnostic")]
        private static extern int GetBackendRuntimeDiagnostic(
            int backendKind,
            string path,
            bool requireAudioExport,
            StringBuilder destination,
            int destinationLength);

        /// <summary>
        /// Begins or resumes playback
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        public void Play()
        {
            if (!ValidatePlayerId(_id))
            {
                throw new InvalidOperationException($"{nameof(MediaPlayer)} has no " +
                    "underlying valid native player.");
            }

            Play(_id);
            _playRequested = true;
        }

        /// <summary>
        /// Stops playback
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown if MediaPlayer failed to 
        /// obtain a native player</exception>
        public void Stop()
        {
            if (!ValidatePlayerId(_id))
            {
                throw new InvalidOperationException($"{nameof(MediaPlayer)} has no " +
                    "underlying valid native player.");
            }

            var result = Stop(_id);

            if (result < 0)
            {
                throw new Exception($"Failed to stop with error {result}");
            }

            _playRequested = false;
        }

        /// <summary>
        /// Evaluates the duration
        /// </summary>
        /// <returns>The duration in seconds</returns>
        /// <exception cref="InvalidOperationException">Thrown if MediaPlayer failed to 
        /// obtain a native player</exception>
        public double Duration()
        {
            if (!ValidatePlayerId(_id))
            {
                throw new InvalidOperationException($"{nameof(MediaPlayer)} has no " +
                    "underlying valid native player.");
            }

            var result = Duration(_id);

            if (result < 0)
            {
                throw new Exception("Failed to get duration");
            }

            return result;
        }

        /// <summary>
        /// Evaluates the current time
        /// </summary>
        /// <returns>The time in seconds since playback start</returns>
        /// <exception cref="InvalidOperationException">Thrown if MediaPlayer failed to 
        /// obtain a native player</exception>
        public double Time()
        {
            if (!ValidatePlayerId(_id))
            {
                throw new InvalidOperationException($"{nameof(MediaPlayer)} has no " +
                    "underlying valid native player.");
            }

            var result = Time(_id);

            if (result < 0)
            {
                throw new Exception("Failed to get time");
            }

            return result;
        }

        /// <summary>
        /// Seeks the playback
        /// </summary>
        /// <param name="time">The time to seek to in seconds</param>
        /// <exception cref="InvalidOperationException">Thrown if MediaPlayer failed to 
        /// obtain a native player</exception>
        public void Seek(double time)
        {
            if (!ValidatePlayerId(_id))
            {
                throw new InvalidOperationException($"{nameof(MediaPlayer)} has no " +
                    "underlying valid native player.");
            }

            var result = Seek(_id, time);

            if (result < 0)
            {
                throw new Exception($"Failed to seek with error {result}");
            }
        }

        public bool TryGetRuntimeHealth(out PlayerRuntimeHealth health)
        {
            health = default(PlayerRuntimeHealth);
            if (!ValidatePlayerId(_id))
            {
                return false;
            }

            try
            {
                var snapshot = new RustAVPlayerHealthSnapshotV2
                {
                    StructSize = (uint)Marshal.SizeOf(typeof(RustAVPlayerHealthSnapshotV2)),
                    StructVersion = RustAVPlayerHealthSnapshotV2Version,
                };
                var result = GetPlayerHealthSnapshotV2(_id, ref snapshot);
                if (result < 0)
                {
                    return false;
                }

                health = new PlayerRuntimeHealth
                {
                    SourceConnectionState = NormalizeSourceConnectionState(snapshot.SourceConnectionState),
                    IsConnected = snapshot.IsConnected != 0,
                    IsPlaying = snapshot.IsPlaying != 0,
                    IsRealtime = snapshot.IsRealtime != 0,
                    SourcePacketCount = snapshot.SourcePacketCount,
                    SourceTimeoutCount = snapshot.SourceTimeoutCount,
                    SourceReconnectCount = snapshot.SourceReconnectCount,
                    SourceLastActivityAgeSec = snapshot.SourceLastActivityAgeSec,
                    CurrentTimeSec = snapshot.CurrentTimeSec,
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

        private void Start()
        {
            NativeInitializer.Initialize(this);

            var uri = ResolveUri(Uri);

            _targetTexture = new Texture2D(Width, Height, TextureFormat.ARGB32, false)
            {
                filterMode = FilterMode.Bilinear,
                name = Uri
            };

            var openOptions = new RustAVPlayerOpenOptions
            {
                StructSize = (uint)Marshal.SizeOf(typeof(RustAVPlayerOpenOptions)),
                StructVersion = RustAVPlayerOpenOptionsVersion,
                BackendKind = (int)PreferredBackend,
                StrictBackend = StrictBackend ? 1 : 0,
            };

            try
            {
                _id = GetPlayerEx(uri, _targetTexture.GetNativeTexturePtr(), ref openOptions);
            }
            catch (EntryPointNotFoundException)
            {
                _id = GetPlayer(uri, _targetTexture.GetNativeTexturePtr());
            }

            if (ValidatePlayerId(_id))
            {
                _actualBackendKind = ReadActualBackendKind();
                if (TargetMaterial != null)
                {
                    TargetMaterial.mainTexture = _targetTexture;
                }
                SetLoop(_id, Loop);
            }
            else
            {
                var diagnostic = ReadBackendRuntimeDiagnostic(uri);
                if (string.IsNullOrEmpty(diagnostic))
                {
                    throw new Exception($"Failed to create player with error: {_id}");
                }

                throw new Exception($"Failed to create player with error: {_id}; {diagnostic}");
            }

            if (AutoPlay)
            {
                Play();
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

        private void OnApplicationPause(bool pauseStatus)
        {
            if (ValidatePlayerId(_id))
            {
                if (pauseStatus)
                {
                    _resumeAfterPause = _playRequested;
                    if (_resumeAfterPause)
                    {
                        Stop();
                    }
                }
                else if (_resumeAfterPause)
                {
                    Play();
                    _resumeAfterPause = false;
                }
            }
        }

        private static bool ValidatePlayerId(int id)
        {
            return id >= 0;
        }

        private void ReleaseNativePlayer()
        {
            if (!ValidatePlayerId(_id))
            {
                return;
            }

            var result = ReleasePlayer(_id);
            _id = InvalidPlayerId;
            _playRequested = false;
            _resumeAfterPause = false;
            _actualBackendKind = MediaBackendKind.Auto;

            if (result < 0)
            {
                throw new Exception($"Failed to release player with error: {result}");
            }
        }

        private void ReleaseManagedResources()
        {
            if (TargetMaterial != null && ReferenceEquals(TargetMaterial.mainTexture, _targetTexture))
            {
                TargetMaterial.mainTexture = null;
            }

            if (_targetTexture != null)
            {
                Destroy(_targetTexture);
                _targetTexture = null;
            }
        }

        private MediaBackendKind ReadActualBackendKind()
        {
            if (!ValidatePlayerId(_id))
            {
                return MediaBackendKind.Auto;
            }

            try
            {
                var backendKind = GetPlayerBackendKind(_id);
                switch (backendKind)
                {
                    case 1:
                        return MediaBackendKind.Ffmpeg;
                    case 2:
                        return MediaBackendKind.Gstreamer;
                    default:
                        return MediaBackendKind.Auto;
                }
            }
            catch (EntryPointNotFoundException)
            {
                return MediaBackendKind.Auto;
            }
        }

        private string ReadBackendRuntimeDiagnostic(string uri)
        {
            if (string.IsNullOrEmpty(uri))
            {
                return string.Empty;
            }

            try
            {
                var buffer = new StringBuilder(BackendDiagnosticBufferLength);
                var result = GetBackendRuntimeDiagnostic(
                    (int)PreferredBackend,
                    uri,
                    false,
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

        private static MediaSourceConnectionState NormalizeSourceConnectionState(int value)
        {
            switch (value)
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
    }
}
