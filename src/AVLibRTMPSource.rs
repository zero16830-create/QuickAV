#![allow(non_snake_case)]

use crate::AVLibPacket::AVLibPacket;
use crate::AVLibPacketRecycler::AVLibPacketRecycler;
use crate::FixedSizeQueue::FixedSizeQueue;
use crate::IAVLibSource::{
    AVLibSourceConnectionState, AVLibSourceRuntimeStats, AVLibStreamInfo, IAVLibSource,
};
use crate::Logging::Debug::Debug;
use bytes::Bytes;
use ffmpeg_next::codec::{self, packet::Flags as PacketFlags};
use ffmpeg_next::ffi::{av_mallocz, AVMediaType, AV_CODEC_FLAG_LOW_DELAY, AV_INPUT_BUFFER_PADDING_SIZE};
use ffmpeg_next::{Packet, Rational};
use rml_rtmp::chunk_io::Packet as RtmpPacket;
use rml_rtmp::handshake::{Handshake, HandshakeProcessResult, PeerType};
use rml_rtmp::sessions::{
    ClientSession, ClientSessionConfig, ClientSessionEvent, ClientSessionResult, StreamMetadata,
};
use std::collections::VecDeque;
use std::io::{ErrorKind, Read, Write};
use std::net::{TcpStream, ToSocketAddrs};
use std::ptr;
use std::sync::{
    atomic::{AtomicBool, AtomicU64, Ordering},
    Arc, Mutex,
};
use std::thread;
use std::time::{Duration, Instant};
use url::Url;

#[derive(Clone, PartialEq)]
struct RtmpVideoConfig {
    codec_id: codec::Id,
    width: i32,
    height: i32,
    time_base: f64,
    frame_rate: f64,
    extra_data: Vec<u8>,
}

#[derive(Clone, PartialEq)]
struct RtmpAudioConfig {
    codec_id: codec::Id,
    sample_rate: i32,
    channels: i32,
    time_base: f64,
    extra_data: Vec<u8>,
}

#[derive(Clone)]
struct RtmpTarget {
    host: String,
    port: u16,
    app: String,
    stream_key: String,
    tc_url: String,
}

pub struct AVLibRTMPSource {
    _uri: String,
    _packetQueues: Arc<Mutex<Vec<Arc<FixedSizeQueue<AVLibPacket>>>>>,
    _streamTypes: Arc<Mutex<Vec<AVMediaType>>>,
    _streams: Arc<Mutex<Vec<AVLibStreamInfo>>>,
    _videoConfig: Arc<Mutex<Option<RtmpVideoConfig>>>,
    _audioConfig: Arc<Mutex<Option<RtmpAudioConfig>>>,
    _stayAlive: Arc<AtomicBool>,
    _thread: Option<thread::JoinHandle<()>>,
    _isConnected: Arc<AtomicBool>,
    _lastActivityTicks: Arc<Mutex<Option<Instant>>>,
    _checkingConnection: Arc<AtomicBool>,
    _checkStartTicks: Arc<Mutex<Option<Instant>>>,
    _packetCount: Arc<AtomicU64>,
    _timeoutCount: Arc<AtomicU64>,
    _reconnectCount: Arc<AtomicU64>,
    _connectRequested: Arc<AtomicBool>,
    _videoTimestampOrigin: Arc<Mutex<Option<i64>>>,
    _audioTimestampOrigin: Arc<Mutex<Option<i64>>>,
    _recycler: Arc<Mutex<AVLibPacketRecycler>>,
}

impl AVLibRTMPSource {
    const REALTIME_VIDEO_PACKET_QUEUE_SIZE: usize = 3;
    const REALTIME_AUDIO_PACKET_QUEUE_SIZE: usize = 96;
    const BEGIN_TIMEOUT_CHECK_SECONDS: u64 = 3;
    const TIMEOUT_SECONDS: u64 = 2;
    const CONNECT_RETRY_DELAY_MS: u64 = 200;
    const SOCKET_CONNECT_TIMEOUT_MS: u64 = 3000;
    const SOCKET_POLL_TIMEOUT_MS: u64 = 200;
    const SOCKET_WRITE_TIMEOUT_MS: u64 = 3000;
    const SOCKET_READ_BUFFER_BYTES: usize = 8192;

    pub fn new(uri: String) -> Self {
        let _ = ffmpeg_next::init();

        let packet_queues = Arc::new(Mutex::new(vec![
            Arc::new(FixedSizeQueue::new(Self::REALTIME_VIDEO_PACKET_QUEUE_SIZE)),
            Arc::new(FixedSizeQueue::new(Self::REALTIME_AUDIO_PACKET_QUEUE_SIZE)),
        ]));
        let stream_types = Arc::new(Mutex::new(vec![
            AVMediaType::AVMEDIA_TYPE_VIDEO,
            AVMediaType::AVMEDIA_TYPE_UNKNOWN,
        ]));
        let streams = Arc::new(Mutex::new(vec![
            AVLibStreamInfo {
                index: 0,
                codec_type: AVMediaType::AVMEDIA_TYPE_VIDEO,
                width: 0,
                height: 0,
                sample_rate: 0,
                channels: 0,
            },
            AVLibStreamInfo {
                index: 1,
                codec_type: AVMediaType::AVMEDIA_TYPE_UNKNOWN,
                width: 0,
                height: 0,
                sample_rate: 0,
                channels: 0,
            },
        ]));
        let video_config = Arc::new(Mutex::new(None));
        let audio_config = Arc::new(Mutex::new(None));
        let stay_alive = Arc::new(AtomicBool::new(true));
        let is_connected = Arc::new(AtomicBool::new(false));
        let last_activity_ticks = Arc::new(Mutex::new(None));
        let checking_connection = Arc::new(AtomicBool::new(false));
        let check_start_ticks = Arc::new(Mutex::new(None));
        let packet_count = Arc::new(AtomicU64::new(0));
        let timeout_count = Arc::new(AtomicU64::new(0));
        let reconnect_count = Arc::new(AtomicU64::new(0));
        let connect_requested = Arc::new(AtomicBool::new(true));
        let video_timestamp_origin = Arc::new(Mutex::new(None));
        let audio_timestamp_origin = video_timestamp_origin.clone();
        let recycler = Arc::new(Mutex::new(AVLibPacketRecycler::new(30)));

        let mut source = Self {
            _uri: uri.clone(),
            _packetQueues: packet_queues.clone(),
            _streamTypes: stream_types.clone(),
            _streams: streams.clone(),
            _videoConfig: video_config.clone(),
            _audioConfig: audio_config.clone(),
            _stayAlive: stay_alive.clone(),
            _thread: None,
            _isConnected: is_connected.clone(),
            _lastActivityTicks: last_activity_ticks.clone(),
            _checkingConnection: checking_connection.clone(),
            _checkStartTicks: check_start_ticks.clone(),
            _packetCount: packet_count.clone(),
            _timeoutCount: timeout_count.clone(),
            _reconnectCount: reconnect_count.clone(),
            _connectRequested: connect_requested.clone(),
            _videoTimestampOrigin: video_timestamp_origin.clone(),
            _audioTimestampOrigin: audio_timestamp_origin.clone(),
            _recycler: recycler.clone(),
        };

        source._thread = Some(thread::spawn(move || {
            while stay_alive.load(Ordering::SeqCst) {
                if !connect_requested.swap(false, Ordering::SeqCst) {
                    thread::sleep(Duration::from_millis(50));
                    continue;
                }

                let run_result = Self::RunRtmpLoop(
                    uri.clone(),
                    stay_alive.clone(),
                    packet_queues.clone(),
                    stream_types.clone(),
                    streams.clone(),
                    video_config.clone(),
                    audio_config.clone(),
                    is_connected.clone(),
                    last_activity_ticks.clone(),
                    packet_count.clone(),
                    timeout_count.clone(),
                    checking_connection.clone(),
                    video_timestamp_origin.clone(),
                    audio_timestamp_origin.clone(),
                    recycler.clone(),
                );

                if let Err(err) = run_result {
                    reconnect_count.fetch_add(1, Ordering::SeqCst);
                    Debug::LogWarning(&format!("AVLibRTMPSource::RunRtmpLoop - {}", err));
                }

                is_connected.store(false, Ordering::SeqCst);
                checking_connection.store(false, Ordering::SeqCst);
                if let Ok(mut check_start) = check_start_ticks.lock() {
                    *check_start = None;
                }

                if stay_alive.load(Ordering::SeqCst) {
                    thread::sleep(Duration::from_millis(Self::CONNECT_RETRY_DELAY_MS));
                }
            }
        }));

        source
    }

    fn DefaultVideoConfig() -> RtmpVideoConfig {
        RtmpVideoConfig {
            codec_id: codec::Id::H264,
            width: 0,
            height: 0,
            time_base: 1.0 / 1000.0,
            frame_rate: -1.0,
            extra_data: Vec::new(),
        }
    }

    fn DefaultAudioConfig() -> RtmpAudioConfig {
        RtmpAudioConfig {
            codec_id: codec::Id::AAC,
            sample_rate: 44_100,
            channels: 2,
            time_base: 1.0 / 1000.0,
            extra_data: Vec::new(),
        }
    }

    fn FlvSoundRateToHz(sound_rate: u8) -> i32 {
        match sound_rate {
            0 => 5_500,
            1 => 11_025,
            2 => 22_050,
            3 => 44_100,
            _ => 0,
        }
    }

    fn ParseRtmpTarget(uri: &str) -> Result<RtmpTarget, String> {
        let url = Url::parse(uri).map_err(|e| format!("invalid RTMP uri {}: {}", uri, e))?;
        if url.scheme() != "rtmp" {
            return Err(format!("unsupported scheme: {}", url.scheme()));
        }

        let host = url
            .host_str()
            .map(str::to_string)
            .ok_or_else(|| "missing RTMP host".to_string())?;
        let port = url.port().unwrap_or(1935);

        let mut app_query: Option<String> = None;
        let mut stream_query: Option<String> = None;
        for (k, v) in url.query_pairs() {
            if k == "app" {
                app_query = Some(v.to_string());
            } else if k == "stream" {
                stream_query = Some(v.to_string());
            }
        }

        let segments: Vec<String> = url
            .path_segments()
            .map(|parts| {
                parts
                    .filter(|s| !s.is_empty())
                    .map(str::to_string)
                    .collect::<Vec<_>>()
            })
            .unwrap_or_default();

        let mut app = app_query.unwrap_or_default();
        let stream_key = if let Some(v) = stream_query {
            if app.is_empty() && !segments.is_empty() {
                app = segments[0].clone();
            }
            v
        } else if segments.len() >= 2 {
            if app.is_empty() {
                app = segments[0].clone();
            }
            segments[1..].join("/")
        } else if segments.len() == 1 {
            segments[0].clone()
        } else {
            return Err("RTMP uri missing stream key".to_string());
        };

        if stream_key.is_empty() {
            return Err("RTMP stream key was empty".to_string());
        }

        let tc_url = if app.is_empty() {
            format!("rtmp://{}:{}/", host, port)
        } else {
            format!("rtmp://{}:{}/{}", host, port, app)
        };

        Ok(RtmpTarget {
            host,
            port,
            app,
            stream_key,
            tc_url,
        })
    }

    fn ConnectSocket(target: &RtmpTarget) -> Result<TcpStream, String> {
        let addrs: Vec<_> = (target.host.as_str(), target.port)
            .to_socket_addrs()
            .map_err(|e| format!("resolve {}:{} failed: {}", target.host, target.port, e))?
            .collect();

        if addrs.is_empty() {
            return Err(format!("no resolved address for {}:{}", target.host, target.port));
        }

        let mut last_err = String::new();
        for addr in addrs {
            match TcpStream::connect_timeout(
                &addr,
                Duration::from_millis(Self::SOCKET_CONNECT_TIMEOUT_MS),
            ) {
                Ok(stream) => {
                    let _ = stream.set_nodelay(true);
                    let _ = stream.set_read_timeout(Some(Duration::from_millis(
                        Self::SOCKET_POLL_TIMEOUT_MS,
                    )));
                    let _ = stream.set_write_timeout(Some(Duration::from_millis(
                        Self::SOCKET_WRITE_TIMEOUT_MS,
                    )));
                    return Ok(stream);
                }
                Err(e) => {
                    last_err = e.to_string();
                }
            }
        }

        Err(format!(
            "connect {}:{} failed: {}",
            target.host, target.port, last_err
        ))
    }

    fn PublishVideoStreamShape(
        width: i32,
        height: i32,
        stream_types: &Arc<Mutex<Vec<AVMediaType>>>,
        streams: &Arc<Mutex<Vec<AVLibStreamInfo>>>,
    ) {
        {
            let mut types_guard = stream_types
                .lock()
                .unwrap_or_else(|poisoned| poisoned.into_inner());
            if let Some(first) = types_guard.get_mut(0) {
                *first = AVMediaType::AVMEDIA_TYPE_VIDEO;
            }
        }

        {
            let mut stream_guard = streams
                .lock()
                .unwrap_or_else(|poisoned| poisoned.into_inner());
            if let Some(first) = stream_guard.get_mut(0) {
                first.index = 0;
                first.codec_type = AVMediaType::AVMEDIA_TYPE_VIDEO;
                first.width = width;
                first.height = height;
            }
        }
    }

    fn PublishAudioStreamShape(
        sample_rate: i32,
        channels: i32,
        stream_types: &Arc<Mutex<Vec<AVMediaType>>>,
        streams: &Arc<Mutex<Vec<AVLibStreamInfo>>>,
    ) {
        {
            let mut types_guard = stream_types
                .lock()
                .unwrap_or_else(|poisoned| poisoned.into_inner());
            if let Some(entry) = types_guard.get_mut(1) {
                *entry = AVMediaType::AVMEDIA_TYPE_AUDIO;
            }
        }

        {
            let mut stream_guard = streams
                .lock()
                .unwrap_or_else(|poisoned| poisoned.into_inner());
            if let Some(entry) = stream_guard.get_mut(1) {
                entry.index = 1;
                entry.codec_type = AVMediaType::AVMEDIA_TYPE_AUDIO;
                entry.width = 0;
                entry.height = 0;
                entry.sample_rate = sample_rate;
                entry.channels = channels;
            }
        }
    }

    fn UpdateVideoConfigFromMetadata(
        metadata: &StreamMetadata,
        stream_types: &Arc<Mutex<Vec<AVMediaType>>>,
        streams: &Arc<Mutex<Vec<AVLibStreamInfo>>>,
        video_config: &Arc<Mutex<Option<RtmpVideoConfig>>>,
        is_connected: &Arc<AtomicBool>,
    ) {
        let (width, height, has_extra) = {
            let mut cfg_guard = video_config
                .lock()
                .unwrap_or_else(|poisoned| poisoned.into_inner());
            let mut cfg = cfg_guard.clone().unwrap_or_else(Self::DefaultVideoConfig);

            if let Some(v) = metadata.video_width {
                cfg.width = v as i32;
            }
            if let Some(v) = metadata.video_height {
                cfg.height = v as i32;
            }
            if let Some(v) = metadata.video_frame_rate {
                cfg.frame_rate = v as f64;
            }

            let has_extra = !cfg.extra_data.is_empty();
            let width = cfg.width;
            let height = cfg.height;

            *cfg_guard = Some(cfg);
            (width, height, has_extra)
        };

        Self::PublishVideoStreamShape(width, height, stream_types, streams);
        if has_extra {
            is_connected.store(true, Ordering::SeqCst);
        }
    }

    fn UpdateAudioConfigFromMetadata(
        metadata: &StreamMetadata,
        stream_types: &Arc<Mutex<Vec<AVMediaType>>>,
        streams: &Arc<Mutex<Vec<AVLibStreamInfo>>>,
        audio_config: &Arc<Mutex<Option<RtmpAudioConfig>>>,
    ) {
        let (sample_rate, channels) = {
            let mut cfg_guard = audio_config
                .lock()
                .unwrap_or_else(|poisoned| poisoned.into_inner());
            let mut cfg = cfg_guard.clone().unwrap_or_else(Self::DefaultAudioConfig);

            if let Some(v) = metadata.audio_sample_rate {
                cfg.sample_rate = v as i32;
            }

            if let Some(v) = metadata.audio_channels {
                cfg.channels = v as i32;
            } else if let Some(is_stereo) = metadata.audio_is_stereo {
                cfg.channels = if is_stereo { 2 } else { 1 };
            }

            if let Some(v) = metadata.audio_codec_id {
                cfg.codec_id = match v {
                    2 => codec::Id::MP3,
                    10 => codec::Id::AAC,
                    _ => cfg.codec_id,
                };
            }

            let sample_rate = cfg.sample_rate;
            let channels = cfg.channels;
            *cfg_guard = Some(cfg);
            (sample_rate, channels)
        };

        Self::PublishAudioStreamShape(sample_rate, channels, stream_types, streams);
    }

    fn UpdateVideoConfigFromSequenceHeader(
        extra_data: Vec<u8>,
        stream_types: &Arc<Mutex<Vec<AVMediaType>>>,
        streams: &Arc<Mutex<Vec<AVLibStreamInfo>>>,
        video_config: &Arc<Mutex<Option<RtmpVideoConfig>>>,
        is_connected: &Arc<AtomicBool>,
    ) {
        let (width, height) = {
            let mut cfg_guard = video_config
                .lock()
                .unwrap_or_else(|poisoned| poisoned.into_inner());
            let mut cfg = cfg_guard.clone().unwrap_or_else(Self::DefaultVideoConfig);
            cfg.extra_data = extra_data;

            if (cfg.width <= 0 || cfg.height <= 0)
                && !cfg.extra_data.is_empty()
            {
                if let Some((detected_width, detected_height)) =
                    Self::DetectVideoShapeFromConfig(&cfg)
                {
                    cfg.width = detected_width;
                    cfg.height = detected_height;
                }
            }

            let width = cfg.width;
            let height = cfg.height;
            *cfg_guard = Some(cfg);
            (width, height)
        };

        Self::PublishVideoStreamShape(width, height, stream_types, streams);
        is_connected.store(true, Ordering::SeqCst);
    }

    fn DetectVideoShapeFromConfig(config: &RtmpVideoConfig) -> Option<(i32, i32)> {
        let decoder = Self::CreateDecoderFromConfig(config)?;
        let width = decoder.width() as i32;
        let height = decoder.height() as i32;
        if width > 0 && height > 0 {
            Some((width, height))
        } else {
            None
        }
    }

    fn HasDecoderConfig(video_config: &Arc<Mutex<Option<RtmpVideoConfig>>>) -> bool {
        video_config
            .lock()
            .ok()
            .and_then(|g| g.as_ref().map(|cfg| !cfg.extra_data.is_empty()))
            .unwrap_or(false)
    }

    fn HasAudioDecoderConfig(audio_config: &Arc<Mutex<Option<RtmpAudioConfig>>>) -> bool {
        audio_config
            .lock()
            .ok()
            .and_then(|g| {
                g.as_ref().map(|cfg| {
                    cfg.codec_id != codec::Id::AAC || !cfg.extra_data.is_empty()
                })
            })
            .unwrap_or(false)
    }

    fn ParseSigned24(value: &[u8]) -> i32 {
        let raw = ((value[0] as i32) << 16) | ((value[1] as i32) << 8) | (value[2] as i32);
        if (raw & 0x80_0000) != 0 {
            raw | !0x00FF_FFFF
        } else {
            raw
        }
    }

    fn BuildPacketFromAvcPayload(
        payload: &[u8],
        dts_ms: i64,
        composition_time_ms: i64,
        frame_type: u8,
        recycler: &Arc<Mutex<AVLibPacketRecycler>>,
    ) -> AVLibPacket {
        let mut wrapped_packet = recycler
            .lock()
            .unwrap_or_else(|poisoned| poisoned.into_inner())
            .GetPacket();

        let mut packet = Packet::copy(payload);
        packet.set_stream(0);
        packet.set_dts(Some(dts_ms));
        packet.set_pts(Some(dts_ms.saturating_add(composition_time_ms)));
        packet.set_duration(0);

        if frame_type == 1 {
            packet.set_flags(PacketFlags::KEY);
        }

        wrapped_packet.Packet = packet;
        wrapped_packet
    }

    fn BuildPacketFromAudioPayload(
        payload: &[u8],
        dts_ms: i64,
        recycler: &Arc<Mutex<AVLibPacketRecycler>>,
    ) -> AVLibPacket {
        let mut wrapped_packet = recycler
            .lock()
            .unwrap_or_else(|poisoned| poisoned.into_inner())
            .GetPacket();

        let mut packet = Packet::copy(payload);
        packet.set_stream(1);
        packet.set_dts(Some(dts_ms));
        packet.set_pts(Some(dts_ms));
        packet.set_duration(0);
        wrapped_packet.Packet = packet;
        wrapped_packet
    }

    fn ProcessVideoDataEvent(
        timestamp_ms: i64,
        data: Bytes,
        packet_queues: &Arc<Mutex<Vec<Arc<FixedSizeQueue<AVLibPacket>>>>>,
        stream_types: &Arc<Mutex<Vec<AVMediaType>>>,
        streams: &Arc<Mutex<Vec<AVLibStreamInfo>>>,
        video_config: &Arc<Mutex<Option<RtmpVideoConfig>>>,
        is_connected: &Arc<AtomicBool>,
        last_activity_ticks: &Arc<Mutex<Option<Instant>>>,
        packet_count: &Arc<AtomicU64>,
        checking_connection: &Arc<AtomicBool>,
        video_timestamp_origin: &Arc<Mutex<Option<i64>>>,
        recycler: &Arc<Mutex<AVLibPacketRecycler>>,
    ) {
        if data.len() < 5 {
            return;
        }

        let frame_type = data[0] >> 4;
        let codec_id = data[0] & 0x0F;
        if codec_id != 7 {
            return;
        }

        let avc_packet_type = data[1];
        let composition_time = Self::ParseSigned24(&data[2..5]) as i64;
        let payload = &data[5..];

        match avc_packet_type {
            0 => {
                if payload.is_empty() {
                    return;
                }

                Self::UpdateVideoConfigFromSequenceHeader(
                    payload.to_vec(),
                    stream_types,
                    streams,
                    video_config,
                    is_connected,
                );
            }
            1 => {
                if payload.is_empty() || !Self::HasDecoderConfig(video_config) {
                    return;
                }

                let queue = {
                    let queues_guard = packet_queues
                        .lock()
                        .unwrap_or_else(|poisoned| poisoned.into_inner());
                    queues_guard.get(0).cloned()
                };

                let normalized_timestamp_ms = {
                    let mut origin = video_timestamp_origin
                        .lock()
                        .unwrap_or_else(|poisoned| poisoned.into_inner());
                    let base = origin.get_or_insert(timestamp_ms);
                    timestamp_ms.saturating_sub(*base)
                };

                let wrapped_packet = Self::BuildPacketFromAvcPayload(
                    payload,
                    normalized_timestamp_ms,
                    composition_time,
                    frame_type,
                    recycler,
                );

                if let Some(q) = queue {
                    q.Push(wrapped_packet);
                } else {
                    recycler
                        .lock()
                        .unwrap_or_else(|poisoned| poisoned.into_inner())
                        .Recycle(wrapped_packet);
                    return;
                }

                packet_count.fetch_add(1, Ordering::SeqCst);
                if let Ok(mut last) = last_activity_ticks.lock() {
                    *last = Some(Instant::now());
                }
                checking_connection.store(false, Ordering::SeqCst);
                is_connected.store(true, Ordering::SeqCst);
            }
            _ => {}
        }
    }

    fn ProcessAudioDataEvent(
        timestamp_ms: i64,
        data: Bytes,
        packet_queues: &Arc<Mutex<Vec<Arc<FixedSizeQueue<AVLibPacket>>>>>,
        stream_types: &Arc<Mutex<Vec<AVMediaType>>>,
        streams: &Arc<Mutex<Vec<AVLibStreamInfo>>>,
        audio_config: &Arc<Mutex<Option<RtmpAudioConfig>>>,
        last_activity_ticks: &Arc<Mutex<Option<Instant>>>,
        packet_count: &Arc<AtomicU64>,
        checking_connection: &Arc<AtomicBool>,
        audio_timestamp_origin: &Arc<Mutex<Option<i64>>>,
        recycler: &Arc<Mutex<AVLibPacketRecycler>>,
    ) {
        if data.len() < 2 {
            return;
        }

        let sound_format = data[0] >> 4;
        let sound_rate = (data[0] >> 2) & 0x03;
        let sound_type = data[0] & 0x01;
        let channels = if sound_type == 1 { 2 } else { 1 };
        let sample_rate_from_tag = Self::FlvSoundRateToHz(sound_rate);

        match sound_format {
            10 => {
                let aac_packet_type = data[1];
                let payload = &data[2..];
                match aac_packet_type {
                    0 => {
                        if payload.is_empty() {
                            return;
                        }

                        let (sample_rate, channels) = {
                            let mut cfg_guard = audio_config
                                .lock()
                                .unwrap_or_else(|poisoned| poisoned.into_inner());
                            let mut cfg =
                                cfg_guard.clone().unwrap_or_else(Self::DefaultAudioConfig);
                            cfg.codec_id = codec::Id::AAC;
                            if sample_rate_from_tag > 0 {
                                cfg.sample_rate = sample_rate_from_tag;
                            }
                            cfg.channels = channels;
                            cfg.extra_data = payload.to_vec();
                            let sample_rate = cfg.sample_rate;
                            let channels = cfg.channels;
                            *cfg_guard = Some(cfg);
                            (sample_rate, channels)
                        };

                        Self::PublishAudioStreamShape(sample_rate, channels, stream_types, streams);
                    }
                    1 => {
                        if payload.is_empty() || !Self::HasAudioDecoderConfig(audio_config) {
                            return;
                        }

                        let queue = {
                            let queues_guard = packet_queues
                                .lock()
                                .unwrap_or_else(|poisoned| poisoned.into_inner());
                            queues_guard.get(1).cloned()
                        };

                        let normalized_timestamp_ms = {
                            let mut origin = audio_timestamp_origin
                                .lock()
                                .unwrap_or_else(|poisoned| poisoned.into_inner());
                            let base = origin.get_or_insert(timestamp_ms);
                            timestamp_ms.saturating_sub(*base)
                        };

                        let wrapped_packet = Self::BuildPacketFromAudioPayload(
                            payload,
                            normalized_timestamp_ms,
                            recycler,
                        );

                        if let Some(q) = queue {
                            q.Push(wrapped_packet);
                        } else {
                            recycler
                                .lock()
                                .unwrap_or_else(|poisoned| poisoned.into_inner())
                                .Recycle(wrapped_packet);
                            return;
                        }

                        packet_count.fetch_add(1, Ordering::SeqCst);
                        if let Ok(mut last) = last_activity_ticks.lock() {
                            *last = Some(Instant::now());
                        }
                        checking_connection.store(false, Ordering::SeqCst);
                    }
                    _ => {}
                }
            }
            2 => {
                let payload = &data[1..];
                if payload.is_empty() {
                    return;
                }

                let queue = {
                    let queues_guard = packet_queues
                        .lock()
                        .unwrap_or_else(|poisoned| poisoned.into_inner());
                    queues_guard.get(1).cloned()
                };

                {
                    let mut cfg_guard = audio_config
                        .lock()
                        .unwrap_or_else(|poisoned| poisoned.into_inner());
                    let mut cfg = cfg_guard.clone().unwrap_or_else(Self::DefaultAudioConfig);
                    cfg.codec_id = codec::Id::MP3;
                    if sample_rate_from_tag > 0 {
                        cfg.sample_rate = sample_rate_from_tag;
                    }
                    cfg.channels = channels;
                    cfg.extra_data.clear();
                    let sample_rate = cfg.sample_rate;
                    let channel_count = cfg.channels;
                    *cfg_guard = Some(cfg);
                    Self::PublishAudioStreamShape(
                        sample_rate,
                        channel_count,
                        stream_types,
                        streams,
                    );
                }

                let normalized_timestamp_ms = {
                    let mut origin = audio_timestamp_origin
                        .lock()
                        .unwrap_or_else(|poisoned| poisoned.into_inner());
                    let base = origin.get_or_insert(timestamp_ms);
                    timestamp_ms.saturating_sub(*base)
                };

                let wrapped_packet = Self::BuildPacketFromAudioPayload(
                    payload,
                    normalized_timestamp_ms,
                    recycler,
                );

                if let Some(q) = queue {
                    q.Push(wrapped_packet);
                } else {
                    recycler
                        .lock()
                        .unwrap_or_else(|poisoned| poisoned.into_inner())
                        .Recycle(wrapped_packet);
                    return;
                }

                packet_count.fetch_add(1, Ordering::SeqCst);
                if let Ok(mut last) = last_activity_ticks.lock() {
                    *last = Some(Instant::now());
                }
                checking_connection.store(false, Ordering::SeqCst);
            }
            _ => {}
        }
    }

    fn WriteAll(stream: &mut TcpStream, bytes: &[u8]) -> Result<(), String> {
        stream
            .write_all(bytes)
            .map_err(|e| format!("socket write failed: {}", e))
    }

    fn SendOutboundPacket(stream: &mut TcpStream, packet: &RtmpPacket) -> Result<(), String> {
        Self::WriteAll(stream, &packet.bytes)
    }

    fn HandleClientResults(
        results: Vec<ClientSessionResult>,
        session: &mut ClientSession,
        stream: &mut TcpStream,
        target: &RtmpTarget,
        packet_queues: &Arc<Mutex<Vec<Arc<FixedSizeQueue<AVLibPacket>>>>>,
        stream_types: &Arc<Mutex<Vec<AVMediaType>>>,
        streams: &Arc<Mutex<Vec<AVLibStreamInfo>>>,
        video_config: &Arc<Mutex<Option<RtmpVideoConfig>>>,
        audio_config: &Arc<Mutex<Option<RtmpAudioConfig>>>,
        is_connected: &Arc<AtomicBool>,
        last_activity_ticks: &Arc<Mutex<Option<Instant>>>,
        packet_count: &Arc<AtomicU64>,
        checking_connection: &Arc<AtomicBool>,
        video_timestamp_origin: &Arc<Mutex<Option<i64>>>,
        audio_timestamp_origin: &Arc<Mutex<Option<i64>>>,
        recycler: &Arc<Mutex<AVLibPacketRecycler>>,
    ) -> Result<(), String> {
        let mut pending: VecDeque<ClientSessionResult> = results.into_iter().collect();

        while let Some(result) = pending.pop_front() {
            match result {
                ClientSessionResult::OutboundResponse(packet) => {
                    Self::SendOutboundPacket(stream, &packet)?;
                }
                ClientSessionResult::RaisedEvent(event) => match event {
                    ClientSessionEvent::ConnectionRequestAccepted => {
                        let playback_result = session
                            .request_playback(target.stream_key.clone())
                            .map_err(|e| format!("request_playback failed: {}", e))?;
                        pending.push_back(playback_result);
                    }
                    ClientSessionEvent::ConnectionRequestRejected { description } => {
                        return Err(format!("RTMP connection rejected: {}", description));
                    }
                    ClientSessionEvent::PlaybackRequestAccepted => {
                        Debug::Log(&format!(
                            "AVLibRTMPSource::PlaybackRequestAccepted app='{}' stream='{}'",
                            target.app, target.stream_key
                        ));
                    }
                    ClientSessionEvent::StreamMetadataReceived { metadata } => {
                        Self::UpdateVideoConfigFromMetadata(
                            &metadata,
                            stream_types,
                            streams,
                            video_config,
                            is_connected,
                        );
                        Self::UpdateAudioConfigFromMetadata(
                            &metadata,
                            stream_types,
                            streams,
                            audio_config,
                        );
                    }
                    ClientSessionEvent::VideoDataReceived { timestamp, data } => {
                        Self::ProcessVideoDataEvent(
                            timestamp.value as i64,
                            data,
                            packet_queues,
                            stream_types,
                            streams,
                            video_config,
                            is_connected,
                            last_activity_ticks,
                            packet_count,
                            checking_connection,
                            video_timestamp_origin,
                            recycler,
                        );
                    }
                    ClientSessionEvent::AudioDataReceived { timestamp, data } => {
                        Self::ProcessAudioDataEvent(
                            timestamp.value as i64,
                            data,
                            packet_queues,
                            stream_types,
                            streams,
                            audio_config,
                            last_activity_ticks,
                            packet_count,
                            checking_connection,
                            audio_timestamp_origin,
                            recycler,
                        );
                    }
                    ClientSessionEvent::PublishRequestAccepted => {}
                    ClientSessionEvent::UnhandleableAmf0Command { command_name, .. } => {
                        Debug::LogWarning(&format!(
                            "AVLibRTMPSource::UnhandleableAmf0Command - {}",
                            command_name
                        ));
                    }
                    ClientSessionEvent::UnknownTransactionResultReceived {
                        transaction_id, ..
                    } => {
                        Debug::LogWarning(&format!(
                            "AVLibRTMPSource::UnknownTransactionResult - {}",
                            transaction_id
                        ));
                    }
                    ClientSessionEvent::UnhandleableOnStatusCode { code } => {
                        let known_play_codes = matches!(
                            code.as_str(),
                            "NetStream.Play.Reset"
                                | "NetStream.Data.Start"
                                | "NetStream.Play.PublishNotify"
                        );
                        if !known_play_codes {
                            Debug::LogWarning(&format!(
                                "AVLibRTMPSource::UnhandleableOnStatusCode - {}",
                                code
                            ));
                        }
                    }
                    ClientSessionEvent::AcknowledgementReceived { .. } => {}
                    ClientSessionEvent::PingResponseReceived { .. } => {}
                },
                ClientSessionResult::UnhandleableMessageReceived(_) => {}
            }
        }

        Ok(())
    }

    fn RunRtmpLoop(
        uri: String,
        stay_alive: Arc<AtomicBool>,
        packet_queues: Arc<Mutex<Vec<Arc<FixedSizeQueue<AVLibPacket>>>>>,
        stream_types: Arc<Mutex<Vec<AVMediaType>>>,
        streams: Arc<Mutex<Vec<AVLibStreamInfo>>>,
        video_config: Arc<Mutex<Option<RtmpVideoConfig>>>,
        audio_config: Arc<Mutex<Option<RtmpAudioConfig>>>,
        is_connected: Arc<AtomicBool>,
        last_activity_ticks: Arc<Mutex<Option<Instant>>>,
        packet_count: Arc<AtomicU64>,
        _timeout_count: Arc<AtomicU64>,
        checking_connection: Arc<AtomicBool>,
        video_timestamp_origin: Arc<Mutex<Option<i64>>>,
        audio_timestamp_origin: Arc<Mutex<Option<i64>>>,
        recycler: Arc<Mutex<AVLibPacketRecycler>>,
    ) -> Result<(), String> {
        if let Ok(mut origin) = video_timestamp_origin.lock() {
            *origin = None;
        }
        if let Ok(mut origin) = audio_timestamp_origin.lock() {
            *origin = None;
        }

        let target = Self::ParseRtmpTarget(&uri)?;
        let mut socket = Self::ConnectSocket(&target)?;

        let mut handshake = Handshake::new(PeerType::Client);
        let outbound_c0_c1 = handshake
            .generate_outbound_p0_and_p1()
            .map_err(|e| format!("handshake init failed: {}", e))?;
        Self::WriteAll(&mut socket, &outbound_c0_c1)?;

        let mut session_config = ClientSessionConfig::new();
        session_config.tc_url = Some(target.tc_url.clone());
        let (mut session, initial_results) = ClientSession::new(session_config)
            .map_err(|e| format!("create RTMP session failed: {}", e))?;

        if !initial_results.is_empty() {
            Self::HandleClientResults(
                initial_results,
                &mut session,
                &mut socket,
                &target,
                &packet_queues,
                &stream_types,
                &streams,
                &video_config,
                &audio_config,
                &is_connected,
                &last_activity_ticks,
                &packet_count,
                &checking_connection,
                &video_timestamp_origin,
                &audio_timestamp_origin,
                &recycler,
            )?;
        }

        let mut handshake_completed = false;
        let mut connect_sent = false;
        let mut read_buffer = [0_u8; Self::SOCKET_READ_BUFFER_BYTES];

        while stay_alive.load(Ordering::SeqCst) {
            let read_size = match socket.read(&mut read_buffer) {
                Ok(0) => {
                    return Err("rtmp socket closed by peer".to_string());
                }
                Ok(n) => n,
                Err(e) if e.kind() == ErrorKind::TimedOut || e.kind() == ErrorKind::WouldBlock => {
                    continue;
                }
                Err(e) => {
                    return Err(format!("socket read failed: {}", e));
                }
            };

            let mut inbound_bytes = read_buffer[..read_size].to_vec();

            if !handshake_completed {
                let handshake_result = handshake
                    .process_bytes(&inbound_bytes)
                    .map_err(|e| format!("handshake process failed: {}", e))?;

                match handshake_result {
                    HandshakeProcessResult::InProgress { response_bytes } => {
                        if !response_bytes.is_empty() {
                            Self::WriteAll(&mut socket, &response_bytes)?;
                        }
                        continue;
                    }
                    HandshakeProcessResult::Completed {
                        response_bytes,
                        remaining_bytes,
                    } => {
                        if !response_bytes.is_empty() {
                            Self::WriteAll(&mut socket, &response_bytes)?;
                        }
                        handshake_completed = true;
                        inbound_bytes = remaining_bytes;
                    }
                }
            }

            if !connect_sent {
                let connect_result = session
                    .request_connection(target.app.clone())
                    .map_err(|e| format!("request_connection failed: {}", e))?;

                Self::HandleClientResults(
                    vec![connect_result],
                    &mut session,
                    &mut socket,
                    &target,
                    &packet_queues,
                    &stream_types,
                    &streams,
                    &video_config,
                    &audio_config,
                    &is_connected,
                    &last_activity_ticks,
                    &packet_count,
                    &checking_connection,
                    &video_timestamp_origin,
                    &audio_timestamp_origin,
                    &recycler,
                )?;
                connect_sent = true;
            }

            if inbound_bytes.is_empty() {
                continue;
            }

            let input_results = session
                .handle_input(&inbound_bytes)
                .map_err(|e| format!("handle_input failed: {}", e))?;

            Self::HandleClientResults(
                input_results,
                &mut session,
                &mut socket,
                &target,
                &packet_queues,
                &stream_types,
                &streams,
                &video_config,
                &audio_config,
                &is_connected,
                &last_activity_ticks,
                &packet_count,
                &checking_connection,
                &video_timestamp_origin,
                &audio_timestamp_origin,
                &recycler,
            )?;
        }

        Ok(())
    }

    fn CreateDecoderFromConfig(config: &RtmpVideoConfig) -> Option<ffmpeg_next::decoder::Video> {
        let codec = ffmpeg_next::decoder::find(config.codec_id)?;
        let mut context = ffmpeg_next::codec::context::Context::new_with_codec(codec);

        let time_base = Rational::new(1, 1000);
        context.set_time_base(time_base);
        if config.frame_rate > 0.0 {
            context.set_frame_rate(Some(Rational::from(config.frame_rate)));
        }

        unsafe {
            let ctx = context.as_mut_ptr();
            (*ctx).codec_type = AVMediaType::AVMEDIA_TYPE_VIDEO;
            (*ctx).codec_id = config.codec_id.into();
            (*ctx).width = config.width;
            (*ctx).height = config.height;
            (*ctx).thread_count = 1;
            (*ctx).flags |= AV_CODEC_FLAG_LOW_DELAY as i32;

            if !config.extra_data.is_empty() {
                let extra_data_size = config.extra_data.len();
                let padded_size = extra_data_size + AV_INPUT_BUFFER_PADDING_SIZE as usize;
                let extra_ptr = av_mallocz(padded_size) as *mut u8;

                if extra_ptr.is_null() {
                    Debug::LogError(
                        "AVLibRTMPSource::CreateDecoderFromConfig - alloc extradata failed",
                    );
                    return None;
                }

                ptr::copy_nonoverlapping(config.extra_data.as_ptr(), extra_ptr, extra_data_size);
                (*ctx).extradata = extra_ptr;
                (*ctx).extradata_size = extra_data_size as i32;
            }
        }

        let mut decoder = context.decoder();
        decoder.set_packet_time_base(time_base);
        decoder.video().ok()
    }

    fn CreateAudioDecoderFromConfig(config: &RtmpAudioConfig) -> Option<ffmpeg_next::decoder::Audio> {
        let codec = ffmpeg_next::decoder::find(config.codec_id)?;
        let mut context = ffmpeg_next::codec::context::Context::new_with_codec(codec);

        let time_base = Rational::new(1, 1000);
        context.set_time_base(time_base);

        unsafe {
            let ctx = context.as_mut_ptr();
            (*ctx).codec_type = AVMediaType::AVMEDIA_TYPE_AUDIO;
            (*ctx).codec_id = config.codec_id.into();
            (*ctx).sample_rate = config.sample_rate;
            (*ctx).thread_count = 1;
            (*ctx).flags |= AV_CODEC_FLAG_LOW_DELAY as i32;

            if !config.extra_data.is_empty() {
                let extra_data_size = config.extra_data.len();
                let padded_size = extra_data_size + AV_INPUT_BUFFER_PADDING_SIZE as usize;
                let extra_ptr = av_mallocz(padded_size) as *mut u8;

                if extra_ptr.is_null() {
                    Debug::LogError(
                        "AVLibRTMPSource::CreateAudioDecoderFromConfig - alloc extradata failed",
                    );
                    return None;
                }

                ptr::copy_nonoverlapping(config.extra_data.as_ptr(), extra_ptr, extra_data_size);
                (*ctx).extradata = extra_ptr;
                (*ctx).extradata_size = extra_data_size as i32;
            }
        }

        let mut decoder = context.decoder();
        decoder.set_packet_time_base(time_base);
        decoder.audio().ok()
    }

    pub fn VideoDecoder(&self, streamIndex: i32) -> Option<ffmpeg_next::decoder::Video> {
        let stream_info = self.Stream(streamIndex);
        if stream_info.index < 0 {
            return None;
        }

        let config = self
            ._videoConfig
            .lock()
            .ok()
            .and_then(|g| g.as_ref().cloned())?;

        Self::CreateDecoderFromConfig(&config)
    }

    pub fn AudioDecoder(&self, _streamIndex: i32) -> Option<ffmpeg_next::decoder::Audio> {
        let config = self
            ._audioConfig
            .lock()
            .ok()
            .and_then(|g| g.as_ref().cloned())?;

        Self::CreateAudioDecoderFromConfig(&config)
    }
}

impl IAVLibSource for AVLibRTMPSource {
    fn Connect(&mut self) {
        if !self._isConnected.load(Ordering::SeqCst) {
            self._connectRequested.store(true, Ordering::SeqCst);
        }
    }

    fn ConnectionState(&self) -> AVLibSourceConnectionState {
        if self._isConnected.load(Ordering::SeqCst) {
            if self._checkingConnection.load(Ordering::SeqCst) {
                AVLibSourceConnectionState::Checking
            } else {
                AVLibSourceConnectionState::Connected
            }
        } else if self._connectRequested.load(Ordering::SeqCst) {
            if self._reconnectCount.load(Ordering::SeqCst) > 0
                || self._timeoutCount.load(Ordering::SeqCst) > 0
            {
                AVLibSourceConnectionState::Reconnecting
            } else {
                AVLibSourceConnectionState::Connecting
            }
        } else if self._reconnectCount.load(Ordering::SeqCst) > 0
            || self._timeoutCount.load(Ordering::SeqCst) > 0
        {
            AVLibSourceConnectionState::Reconnecting
        } else {
            AVLibSourceConnectionState::Disconnected
        }
    }

    fn Duration(&self) -> f64 {
        -1.0
    }

    fn StreamCount(&self) -> i32 {
        self._streams.lock().unwrap().len() as i32
    }

    fn StreamType(&self, streamIndex: i32) -> AVMediaType {
        if streamIndex < 0 {
            Debug::LogError("AVLibRTMPSource::StreamType - streamIndex was out of range");
            return AVMediaType::AVMEDIA_TYPE_UNKNOWN;
        }

        if !self.IsConnected() {
            return AVMediaType::AVMEDIA_TYPE_UNKNOWN;
        }

        let types = self._streamTypes.lock().unwrap();
        let idx = streamIndex as usize;
        if idx >= types.len() {
            Debug::LogError("AVLibRTMPSource::StreamType - streamIndex was out of range");
            return AVMediaType::AVMEDIA_TYPE_UNKNOWN;
        }

        types[idx]
    }

    fn Stream(&self, streamIndex: i32) -> AVLibStreamInfo {
        if streamIndex < 0 {
            Debug::LogError("AVLibRTMPSource::Stream - streamIndex was out of range");
            return AVLibStreamInfo::empty();
        }

        if !self.IsConnected() {
            return AVLibStreamInfo::empty();
        }

        let streams = self._streams.lock().unwrap();
        let idx = streamIndex as usize;
        if idx >= streams.len() {
            Debug::LogError("AVLibRTMPSource::Stream - streamIndex was out of range");
            return AVLibStreamInfo::empty();
        }

        streams[idx]
    }

    fn TimeBase(&self, streamIndex: i32) -> f64 {
        match streamIndex {
            0 => self
                ._videoConfig
                .lock()
                .ok()
                .and_then(|g| g.as_ref().map(|cfg| cfg.time_base))
                .unwrap_or(-1.0),
            1 => self
                ._audioConfig
                .lock()
                .ok()
                .and_then(|g| g.as_ref().map(|cfg| cfg.time_base))
                .unwrap_or(-1.0),
            _ => {
                Debug::LogError("AVLibRTMPSource::TimeBase - streamIndex was out of range");
                -1.0
            }
        }
    }

    fn FrameRate(&self, streamIndex: i32) -> f64 {
        match streamIndex {
            0 => self
                ._videoConfig
                .lock()
                .ok()
                .and_then(|g| g.as_ref().map(|cfg| cfg.frame_rate))
                .unwrap_or(-1.0),
            1 => 0.0,
            _ => {
                Debug::LogError("AVLibRTMPSource::FrameRate - streamIndex was out of range");
                -1.0
            }
        }
    }

    fn FrameDuration(&self, streamIndex: i32) -> f64 {
        let frame_rate = self.FrameRate(streamIndex);
        if frame_rate > 0.0 {
            1.0 / frame_rate
        } else {
            -1.0
        }
    }

    fn IsRealtime(&self) -> bool {
        true
    }

    fn CanSeek(&self) -> bool {
        false
    }

    fn Seek(&mut self, _from: f64, _to: f64) {
        Debug::LogWarning("AVLibRTMPSource::Seek - realtime source cannot seek");
    }

    fn TryGetNext(&mut self, streamIndex: i32) -> Option<AVLibPacket> {
        if streamIndex < 0 {
            Debug::LogError("AVLibRTMPSource::TryGetNext - streamIndex was out of range");
            return None;
        }

        if !self.IsConnected() {
            return None;
        }

        let queue = {
            let queues = self._packetQueues.lock().unwrap();
            let idx = streamIndex as usize;
            if idx >= queues.len() {
                Debug::LogError("AVLibRTMPSource::TryGetNext - streamIndex was out of range");
                return None;
            }
            queues.get(idx).cloned()
        };

        let packet = queue.and_then(|q| q.TryPop());
        if packet.is_none() {
            let count = self._packetCount.load(Ordering::SeqCst);
            if count > 0 {
                if !self._checkingConnection.load(Ordering::SeqCst) {
                    let last = self._lastActivityTicks.lock().ok().and_then(|v| *v);
                    if let Some(last_time) = last {
                        if last_time.elapsed().as_secs() > Self::BEGIN_TIMEOUT_CHECK_SECONDS {
                            self._checkingConnection.store(true, Ordering::SeqCst);
                            if let Ok(mut check_start) = self._checkStartTicks.lock() {
                                *check_start = Some(Instant::now());
                            }
                        }
                    }
                } else {
                    let started = self._checkStartTicks.lock().ok().and_then(|v| *v);
                    if let Some(start_time) = started {
                        if start_time.elapsed().as_secs() > Self::TIMEOUT_SECONDS {
                            self._timeoutCount.fetch_add(1, Ordering::SeqCst);
                            self._isConnected.store(false, Ordering::SeqCst);
                            self._checkingConnection.store(false, Ordering::SeqCst);
                        }
                    }
                }
            }
        } else {
            self._isConnected.store(true, Ordering::SeqCst);
            self._checkingConnection.store(false, Ordering::SeqCst);
            if let Ok(mut last) = self._lastActivityTicks.lock() {
                *last = Some(Instant::now());
            }
        }

        packet
    }

    fn Recycle(&mut self, packet: AVLibPacket) {
        self._recycler.lock().unwrap().Recycle(packet);
    }

    fn CreateVideoDecoder(&self, streamIndex: i32) -> Option<ffmpeg_next::decoder::Video> {
        self.VideoDecoder(streamIndex)
    }

    fn CreateAudioDecoder(&self, streamIndex: i32) -> Option<ffmpeg_next::decoder::Audio> {
        self.AudioDecoder(streamIndex)
    }

    fn RuntimeStats(&self) -> AVLibSourceRuntimeStats {
        let last_activity_age_sec = self
            ._lastActivityTicks
            .lock()
            .ok()
            .and_then(|last| *last)
            .map(|last| last.elapsed().as_secs_f64())
            .unwrap_or(-1.0);

        AVLibSourceRuntimeStats {
            connection_state: self.ConnectionState(),
            packet_count: self._packetCount.load(Ordering::SeqCst),
            timeout_count: self._timeoutCount.load(Ordering::SeqCst),
            reconnect_count: self._reconnectCount.load(Ordering::SeqCst),
            is_checking_connection: self._checkingConnection.load(Ordering::SeqCst),
            last_activity_age_sec,
        }
    }
}

impl Drop for AVLibRTMPSource {
    fn drop(&mut self) {
        self._stayAlive.store(false, Ordering::SeqCst);
        if let Some(t) = self._thread.take() {
            let _ = t.join();
        }
    }
}
