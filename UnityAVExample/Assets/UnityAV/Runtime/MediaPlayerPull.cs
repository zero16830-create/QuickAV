using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

namespace UnityAV
{
    /// <summary>
    /// 使用拉帧/拉音频模式的播放器，适合 Windows/iOS/Android 通用接入。
    /// </summary>
    public class MediaPlayerPull : MonoBehaviour
    {
        private const string RTSPPrefix = "rtsp://";
        private const string RTMPPrefix = "rtmp://";

        private const int DefaultWidth = 1024;
        private const int DefaultHeight = 1024;
        private const int InvalidPlayerId = -1;
        private const int AudioStartThresholdMilliseconds = 120;
        private const int MaxAudioCopyBytes = 256 * 1024;
        private const int MaxAudioCopyIterations = 8;

        private enum RustAVAudioSampleFormat
        {
            Unknown = 0,
            Float32 = 1
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

        /// <summary>
        /// 媒体地址，支持本地文件、RTSP、RTMP。
        /// </summary>
        [Header("Media Properties:")]
        public string Uri;

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
        public int RealtimeAdditionalAudioSinkDelayMilliseconds = 60;

        private Texture2D _targetTexture;
        private int _id = InvalidPlayerId;
        private long _lastFrameIndex = -1;
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
        private int _audioSampleRate;
        private int _audioBytesPerSample;
        private bool _playRequested;
        private bool _resumeAfterPause;
        private readonly object _audioLock = new object();

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerCreatePullRGBA")]
        private static extern int CreatePlayerPullRGBA(string uri, int targetWidth, int targetHeight);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerRelease")]
        private static extern int ReleasePlayer(int id);

        [DllImport(NativePlugin.Name, EntryPoint = "RustAV_PlayerUpdate")]
        private static extern int UpdatePlayer(int id);

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

        /// <summary>
        /// 开始或恢复播放。
        /// </summary>
        public void Play()
        {
            if (!ValidatePlayerId(_id))
            {
                throw new InvalidOperationException(nameof(MediaPlayerPull) +
                    " has no underlying valid native player.");
            }

            var result = Play(_id);
            if (result < 0)
            {
                throw new Exception("Failed to play with error " + result);
            }

            _playRequested = true;
            TryStartAudioSource();
        }

        /// <summary>
        /// 停止播放。
        /// </summary>
        public void Stop()
        {
            if (!ValidatePlayerId(_id))
            {
                throw new InvalidOperationException(nameof(MediaPlayerPull) +
                    " has no underlying valid native player.");
            }

            var result = Stop(_id);
            if (result < 0)
            {
                throw new Exception("Failed to stop with error " + result);
            }

            _playRequested = false;
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
            if (!ValidatePlayerId(_id))
            {
                throw new InvalidOperationException(nameof(MediaPlayerPull) +
                    " has no underlying valid native player.");
            }

            var result = Seek(_id, time);
            if (result < 0)
            {
                throw new Exception("Failed to seek with error " + result);
            }

            ClearAudioBuffer();
            if (_audioSource != null)
            {
                _audioSource.Stop();
            }
            UpdateNativeAudioSinkDelay();
            TryStartAudioSource();
        }

        private void Start()
        {
            NativeInitializer.InitializePullOnly(this);

            var uri = ResolveUri(Uri);
            _isRealtimeSource = IsRemoteUri(uri);
            EnsureAudioSource();

            _targetTexture = new Texture2D(Width, Height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                name = Uri
            };

            _id = CreatePlayerPullRGBA(uri, Width, Height);
            if (!ValidatePlayerId(_id))
            {
                throw new Exception("Failed to create pull player with error: " + _id);
            }

            if (TargetMaterial != null)
            {
                TargetMaterial.mainTexture = _targetTexture;
            }

            SetLoop(_id, Loop);

            if (AutoPlay)
            {
                Play();
            }
        }

        private void Update()
        {
            if (!ValidatePlayerId(_id))
            {
                return;
            }

            UpdatePlayer(_id);
            UpdateVideoFrame();
            UpdateAudioBuffer();
            UpdateNativeAudioSinkDelay();
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
                    Stop();
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
            _lastFrameIndex = meta.FrameIndex;
        }

        private void UpdateAudioBuffer()
        {
            if (!EnableAudio || !ValidatePlayerId(_id))
            {
                return;
            }

            for (var iteration = 0; iteration < MaxAudioCopyIterations; iteration++)
            {
                RustAVAudioMeta meta;
                var status = GetAudioMetaPCM(_id, out meta);
                if (status <= 0 || meta.BufferedBytes <= 0)
                {
                    break;
                }

                if (!EnsureAudioFormat(meta))
                {
                    break;
                }

                var bytesPerInterleavedSample = meta.BytesPerSample * meta.Channels;
                if (bytesPerInterleavedSample <= 0)
                {
                    break;
                }

                var bytesToCopy = Math.Min(meta.BufferedBytes, MaxAudioCopyBytes);
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

                var sampleCount = copied / meta.BytesPerSample;
                if (_audioFloats.Length != sampleCount)
                {
                    _audioFloats = new float[sampleCount];
                }

                Buffer.BlockCopy(_audioBytes, 0, _audioFloats, 0, copied);
                WriteAudioSamples(_audioFloats, sampleCount);

                if (copied < bytesToCopy)
                {
                    break;
                }
            }

            TryStartAudioSource();
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

            if (_audioClip != null
                && _audioSampleRate == meta.SampleRate
                && _audioChannels == meta.Channels
                && _audioBytesPerSample == meta.BytesPerSample)
            {
                return true;
            }

            _audioSampleRate = meta.SampleRate;
            _audioChannels = meta.Channels;
            _audioBytesPerSample = meta.BytesPerSample;

            var ringCapacity = Math.Max(_audioSampleRate * _audioChannels * 4, 4096);
            lock (_audioLock)
            {
                _audioRing = new float[ringCapacity];
                _audioReadIndex = 0;
                _audioWriteIndex = 0;
                _audioBufferedSamples = 0;
            }

            if (_audioClip != null)
            {
                Destroy(_audioClip);
            }

            _audioClip = AudioClip.Create(
                Uri + "_PullAudio",
                _audioSampleRate,
                _audioChannels,
                _audioSampleRate,
                true,
                OnAudioRead,
                OnAudioSetPosition);

            _audioSource.clip = _audioClip;
            _audioSource.loop = true;
            return true;
        }

        private void WriteAudioSamples(float[] samples, int sampleCount)
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
                    return;
                }

                var freeSamples = _audioRing.Length - _audioBufferedSamples;
                if (sampleCount > freeSamples)
                {
                    var dropSamples = sampleCount - freeSamples;
                    _audioReadIndex = (_audioReadIndex + dropSamples) % _audioRing.Length;
                    _audioBufferedSamples -= dropSamples;
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
            }
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
                if (_audioBufferedSamples <= 0 || _audioRing == null || _audioRing.Length == 0)
                {
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
            }
        }

        private void OnAudioSetPosition(int position)
        {
        }

        private void ClearAudioBuffer()
        {
            lock (_audioLock)
            {
                _audioReadIndex = 0;
                _audioWriteIndex = 0;
                _audioBufferedSamples = 0;
                if (_audioRing != null && _audioRing.Length > 0)
                {
                    Array.Clear(_audioRing, 0, _audioRing.Length);
                }
            }

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

            lock (_audioLock)
            {
                if (_audioSampleRate <= 0 || _audioChannels <= 0)
                {
                    return;
                }

                var thresholdSamples = (_audioSampleRate * _audioChannels
                    * AudioStartThresholdMilliseconds) / 1000;
                if (_audioBufferedSamples < thresholdSamples)
                {
                    return;
                }
            }

            _audioSource.Play();
            UpdateNativeAudioSinkDelay();
        }

        private void UpdateNativeAudioSinkDelay()
        {
            if (!ValidatePlayerId(_id))
            {
                return;
            }

            var delaySec = 0.0;
            if (EnableAudio && _audioSampleRate > 0 && _audioChannels > 0)
            {
                lock (_audioLock)
                {
                    delaySec += (double)_audioBufferedSamples / (_audioSampleRate * _audioChannels);
                }

                int dspBufferLength;
                int dspBufferCount;
                AudioSettings.GetDSPBufferSize(out dspBufferLength, out dspBufferCount);
                if (dspBufferLength > 0 && dspBufferCount > 0)
                {
                    delaySec += (double)(dspBufferLength * dspBufferCount) / _audioSampleRate;
                }
            }

            if (_isRealtimeSource && RealtimeAdditionalAudioSinkDelayMilliseconds > 0)
            {
                delaySec += (double)RealtimeAdditionalAudioSinkDelayMilliseconds / 1000.0;
            }

            SetAudioSinkDelaySeconds(_id, delaySec);
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

            SetAudioSinkDelaySeconds(_id, 0.0);
            ReleasePlayer(_id);
            _id = InvalidPlayerId;
            _playRequested = false;
            _resumeAfterPause = false;
        }

        private void ReleaseManagedResources()
        {
            if (_audioSource != null)
            {
                _audioSource.Stop();
            }

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
            _audioBytes = new byte[0];
            _audioFloats = new float[0];
            lock (_audioLock)
            {
                _audioRing = new float[0];
                _audioReadIndex = 0;
                _audioWriteIndex = 0;
                _audioBufferedSamples = 0;
            }
        }
    }
}
