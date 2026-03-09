use crate::av_lib_packet::AvLibPacket;
use crate::av_lib_packet_recycler::AvLibPacketRecycler;
use crate::av_lib_source::{
    AvLibSource, AvLibSourceConnectionState, AvLibSourceRuntimeStats, AvLibStreamInfo,
};
use crate::fixed_size_queue::FixedSizeQueue;
use crate::logging::debug::Debug;
use bytes::Bytes;
use ffmpeg_next::codec::{self, packet::Flags as PacketFlags};
use ffmpeg_next::ffi::{
    av_mallocz, AVMediaType, AV_CODEC_FLAG_LOW_DELAY, AV_INPUT_BUFFER_PADDING_SIZE,
};
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

pub struct AvLibRtmpSource {
    packet_queues: Arc<Mutex<Vec<Arc<FixedSizeQueue<AvLibPacket>>>>>,
    stream_types: Arc<Mutex<Vec<AVMediaType>>>,
    streams: Arc<Mutex<Vec<AvLibStreamInfo>>>,
    video_config: Arc<Mutex<Option<RtmpVideoConfig>>>,
    audio_config: Arc<Mutex<Option<RtmpAudioConfig>>>,
    stay_alive: Arc<AtomicBool>,
    thread: Option<thread::JoinHandle<()>>,
    _isconnected: Arc<AtomicBool>,
    last_activity_ticks: Arc<Mutex<Option<Instant>>>,
    _checkingconnection: Arc<AtomicBool>,
    check_start_ticks: Arc<Mutex<Option<Instant>>>,
    packet_count: Arc<AtomicU64>,
    timeout_count: Arc<AtomicU64>,
    reconnect_count: Arc<AtomicU64>,
    connect_requested: Arc<AtomicBool>,
    recycler: Arc<Mutex<AvLibPacketRecycler>>,
}

impl AvLibRtmpSource {
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
            AvLibStreamInfo {
                index: 0,
                codec_type: AVMediaType::AVMEDIA_TYPE_VIDEO,
                width: 0,
                height: 0,
                sample_rate: 0,
                channels: 0,
            },
            AvLibStreamInfo {
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
        let audio_timestamp_origin = Arc::new(Mutex::new(None));
        let recycler = Arc::new(Mutex::new(AvLibPacketRecycler::new(30)));

        let mut source = Self {
            packet_queues: packet_queues.clone(),
            stream_types: stream_types.clone(),
            streams: streams.clone(),
            video_config: video_config.clone(),
            audio_config: audio_config.clone(),
            stay_alive: stay_alive.clone(),
            thread: None,
            _isconnected: is_connected.clone(),
            last_activity_ticks: last_activity_ticks.clone(),
            _checkingconnection: checking_connection.clone(),
            check_start_ticks: check_start_ticks.clone(),
            packet_count: packet_count.clone(),
            timeout_count: timeout_count.clone(),
            reconnect_count: reconnect_count.clone(),
            connect_requested: connect_requested.clone(),
            recycler: recycler.clone(),
        };

        source.thread = Some(thread::spawn(move || {
            while stay_alive.load(Ordering::SeqCst) {
                if !connect_requested.swap(false, Ordering::SeqCst) {
                    thread::sleep(Duration::from_millis(50));
                    continue;
                }

                let run_result = Self::run_rtmp_loop(
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
                    Debug::log_warning(&format!("AvLibRtmpSource::run_rtmp_loop - {}", err));
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

    fn default_video_config() -> RtmpVideoConfig {
        RtmpVideoConfig {
            codec_id: codec::Id::H264,
            width: 0,
            height: 0,
            time_base: 1.0 / 1000.0,
            frame_rate: -1.0,
            extra_data: Vec::new(),
        }
    }

    fn default_audio_config() -> RtmpAudioConfig {
        RtmpAudioConfig {
            codec_id: codec::Id::AAC,
            sample_rate: 44_100,
            channels: 2,
            time_base: 1.0 / 1000.0,
            extra_data: Vec::new(),
        }
    }

    fn flv_sound_rate_to_hz(sound_rate: u8) -> i32 {
        match sound_rate {
            0 => 5_500,
            1 => 11_025,
            2 => 22_050,
            3 => 44_100,
            _ => 0,
        }
    }

    fn parse_rtmp_target(uri: &str) -> Result<RtmpTarget, String> {
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

    fn connect_socket(target: &RtmpTarget) -> Result<TcpStream, String> {
        let addrs: Vec<_> = (target.host.as_str(), target.port)
            .to_socket_addrs()
            .map_err(|e| format!("resolve {}:{} failed: {}", target.host, target.port, e))?
            .collect();

        if addrs.is_empty() {
            return Err(format!(
                "no resolved address for {}:{}",
                target.host, target.port
            ));
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

    fn publish_video_stream_shape(
        width: i32,
        height: i32,
        stream_types: &Arc<Mutex<Vec<AVMediaType>>>,
        streams: &Arc<Mutex<Vec<AvLibStreamInfo>>>,
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

    fn publish_audio_stream_shape(
        sample_rate: i32,
        channels: i32,
        stream_types: &Arc<Mutex<Vec<AVMediaType>>>,
        streams: &Arc<Mutex<Vec<AvLibStreamInfo>>>,
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

    fn update_video_config_from_metadata(
        metadata: &StreamMetadata,
        stream_types: &Arc<Mutex<Vec<AVMediaType>>>,
        streams: &Arc<Mutex<Vec<AvLibStreamInfo>>>,
        video_config: &Arc<Mutex<Option<RtmpVideoConfig>>>,
        is_connected: &Arc<AtomicBool>,
    ) {
        let (width, height, has_extra) = {
            let mut cfg_guard = video_config
                .lock()
                .unwrap_or_else(|poisoned| poisoned.into_inner());
            let mut cfg = cfg_guard.clone().unwrap_or_else(Self::default_video_config);

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

        Self::publish_video_stream_shape(width, height, stream_types, streams);
        if has_extra {
            is_connected.store(true, Ordering::SeqCst);
        }
    }

    fn update_audio_config_from_metadata(
        metadata: &StreamMetadata,
        stream_types: &Arc<Mutex<Vec<AVMediaType>>>,
        streams: &Arc<Mutex<Vec<AvLibStreamInfo>>>,
        audio_config: &Arc<Mutex<Option<RtmpAudioConfig>>>,
    ) {
        let (sample_rate, channels) = {
            let mut cfg_guard = audio_config
                .lock()
                .unwrap_or_else(|poisoned| poisoned.into_inner());
            let mut cfg = cfg_guard.clone().unwrap_or_else(Self::default_audio_config);

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

        Self::publish_audio_stream_shape(sample_rate, channels, stream_types, streams);
    }

    fn update_video_config_from_sequence_header(
        extra_data: Vec<u8>,
        stream_types: &Arc<Mutex<Vec<AVMediaType>>>,
        streams: &Arc<Mutex<Vec<AvLibStreamInfo>>>,
        video_config: &Arc<Mutex<Option<RtmpVideoConfig>>>,
        is_connected: &Arc<AtomicBool>,
    ) {
        let (width, height) = {
            let mut cfg_guard = video_config
                .lock()
                .unwrap_or_else(|poisoned| poisoned.into_inner());
            let mut cfg = cfg_guard.clone().unwrap_or_else(Self::default_video_config);
            cfg.extra_data = extra_data;

            if (cfg.width <= 0 || cfg.height <= 0) && !cfg.extra_data.is_empty() {
                if let Some((detected_width, detected_height)) =
                    Self::detect_video_shape_from_config(&cfg)
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

        Self::publish_video_stream_shape(width, height, stream_types, streams);
        is_connected.store(true, Ordering::SeqCst);
    }

    fn detect_video_shape_from_config(config: &RtmpVideoConfig) -> Option<(i32, i32)> {
        let decoder = Self::create_decoder_from_config(config)?;
        let width = decoder.width() as i32;
        let height = decoder.height() as i32;
        if width > 0 && height > 0 {
            Some((width, height))
        } else {
            None
        }
    }

    fn has_decoder_config(video_config: &Arc<Mutex<Option<RtmpVideoConfig>>>) -> bool {
        video_config
            .lock()
            .ok()
            .and_then(|g| g.as_ref().map(|cfg| !cfg.extra_data.is_empty()))
            .unwrap_or(false)
    }

    fn has_audio_decoder_config(audio_config: &Arc<Mutex<Option<RtmpAudioConfig>>>) -> bool {
        audio_config
            .lock()
            .ok()
            .and_then(|g| {
                g.as_ref()
                    .map(|cfg| cfg.codec_id != codec::Id::AAC || !cfg.extra_data.is_empty())
            })
            .unwrap_or(false)
    }

    fn parse_signed24(value: &[u8]) -> i32 {
        let raw = ((value[0] as i32) << 16) | ((value[1] as i32) << 8) | (value[2] as i32);
        if (raw & 0x80_0000) != 0 {
            raw | !0x00FF_FFFF
        } else {
            raw
        }
    }

    fn build_packet_from_avc_payload(
        payload: &[u8],
        dts_ms: i64,
        composition_time_ms: i64,
        frame_type: u8,
        recycler: &Arc<Mutex<AvLibPacketRecycler>>,
    ) -> AvLibPacket {
        let mut wrapped_packet = recycler
            .lock()
            .unwrap_or_else(|poisoned| poisoned.into_inner())
            .get_packet();

        let mut packet = Packet::copy(payload);
        packet.set_stream(0);
        packet.set_dts(Some(dts_ms));
        packet.set_pts(Some(dts_ms.saturating_add(composition_time_ms)));
        packet.set_duration(0);

        if frame_type == 1 {
            packet.set_flags(PacketFlags::KEY);
        }

        wrapped_packet.packet = packet;
        wrapped_packet
    }

    fn build_packet_from_audio_payload(
        payload: &[u8],
        dts_ms: i64,
        recycler: &Arc<Mutex<AvLibPacketRecycler>>,
    ) -> AvLibPacket {
        let mut wrapped_packet = recycler
            .lock()
            .unwrap_or_else(|poisoned| poisoned.into_inner())
            .get_packet();

        let mut packet = Packet::copy(payload);
        packet.set_stream(1);
        packet.set_dts(Some(dts_ms));
        packet.set_pts(Some(dts_ms));
        packet.set_duration(0);
        wrapped_packet.packet = packet;
        wrapped_packet
    }

    fn process_video_data_event(
        timestamp_ms: i64,
        data: Bytes,
        packet_queues: &Arc<Mutex<Vec<Arc<FixedSizeQueue<AvLibPacket>>>>>,
        stream_types: &Arc<Mutex<Vec<AVMediaType>>>,
        streams: &Arc<Mutex<Vec<AvLibStreamInfo>>>,
        video_config: &Arc<Mutex<Option<RtmpVideoConfig>>>,
        is_connected: &Arc<AtomicBool>,
        last_activity_ticks: &Arc<Mutex<Option<Instant>>>,
        packet_count: &Arc<AtomicU64>,
        checking_connection: &Arc<AtomicBool>,
        video_timestamp_origin: &Arc<Mutex<Option<i64>>>,
        recycler: &Arc<Mutex<AvLibPacketRecycler>>,
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
        let composition_time = Self::parse_signed24(&data[2..5]) as i64;
        let payload = &data[5..];

        match avc_packet_type {
            0 => {
                if payload.is_empty() {
                    return;
                }

                Self::update_video_config_from_sequence_header(
                    payload.to_vec(),
                    stream_types,
                    streams,
                    video_config,
                    is_connected,
                );
            }
            1 => {
                if payload.is_empty() || !Self::has_decoder_config(video_config) {
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

                let wrapped_packet = Self::build_packet_from_avc_payload(
                    payload,
                    normalized_timestamp_ms,
                    composition_time,
                    frame_type,
                    recycler,
                );

                if let Some(q) = queue {
                    q.push(wrapped_packet);
                } else {
                    recycler
                        .lock()
                        .unwrap_or_else(|poisoned| poisoned.into_inner())
                        .recycle(wrapped_packet);
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

    fn process_audio_data_event(
        timestamp_ms: i64,
        data: Bytes,
        packet_queues: &Arc<Mutex<Vec<Arc<FixedSizeQueue<AvLibPacket>>>>>,
        stream_types: &Arc<Mutex<Vec<AVMediaType>>>,
        streams: &Arc<Mutex<Vec<AvLibStreamInfo>>>,
        audio_config: &Arc<Mutex<Option<RtmpAudioConfig>>>,
        last_activity_ticks: &Arc<Mutex<Option<Instant>>>,
        packet_count: &Arc<AtomicU64>,
        checking_connection: &Arc<AtomicBool>,
        audio_timestamp_origin: &Arc<Mutex<Option<i64>>>,
        recycler: &Arc<Mutex<AvLibPacketRecycler>>,
    ) {
        if data.len() < 2 {
            return;
        }

        let sound_format = data[0] >> 4;
        let sound_rate = (data[0] >> 2) & 0x03;
        let sound_type = data[0] & 0x01;
        let channels = if sound_type == 1 { 2 } else { 1 };
        let sample_rate_from_tag = Self::flv_sound_rate_to_hz(sound_rate);

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
                                cfg_guard.clone().unwrap_or_else(Self::default_audio_config);
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

                        Self::publish_audio_stream_shape(
                            sample_rate,
                            channels,
                            stream_types,
                            streams,
                        );
                    }
                    1 => {
                        if payload.is_empty() || !Self::has_audio_decoder_config(audio_config) {
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

                        let wrapped_packet = Self::build_packet_from_audio_payload(
                            payload,
                            normalized_timestamp_ms,
                            recycler,
                        );

                        if let Some(q) = queue {
                            q.push(wrapped_packet);
                        } else {
                            recycler
                                .lock()
                                .unwrap_or_else(|poisoned| poisoned.into_inner())
                                .recycle(wrapped_packet);
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
                    let mut cfg = cfg_guard.clone().unwrap_or_else(Self::default_audio_config);
                    cfg.codec_id = codec::Id::MP3;
                    if sample_rate_from_tag > 0 {
                        cfg.sample_rate = sample_rate_from_tag;
                    }
                    cfg.channels = channels;
                    cfg.extra_data.clear();
                    let sample_rate = cfg.sample_rate;
                    let channel_count = cfg.channels;
                    *cfg_guard = Some(cfg);
                    Self::publish_audio_stream_shape(
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

                let wrapped_packet = Self::build_packet_from_audio_payload(
                    payload,
                    normalized_timestamp_ms,
                    recycler,
                );

                if let Some(q) = queue {
                    q.push(wrapped_packet);
                } else {
                    recycler
                        .lock()
                        .unwrap_or_else(|poisoned| poisoned.into_inner())
                        .recycle(wrapped_packet);
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

    fn write_all(stream: &mut TcpStream, bytes: &[u8]) -> Result<(), String> {
        stream
            .write_all(bytes)
            .map_err(|e| format!("socket write failed: {}", e))
    }

    fn send_outbound_packet(stream: &mut TcpStream, packet: &RtmpPacket) -> Result<(), String> {
        Self::write_all(stream, &packet.bytes)
    }

    fn handle_client_results(
        results: Vec<ClientSessionResult>,
        session: &mut ClientSession,
        stream: &mut TcpStream,
        target: &RtmpTarget,
        packet_queues: &Arc<Mutex<Vec<Arc<FixedSizeQueue<AvLibPacket>>>>>,
        stream_types: &Arc<Mutex<Vec<AVMediaType>>>,
        streams: &Arc<Mutex<Vec<AvLibStreamInfo>>>,
        video_config: &Arc<Mutex<Option<RtmpVideoConfig>>>,
        audio_config: &Arc<Mutex<Option<RtmpAudioConfig>>>,
        is_connected: &Arc<AtomicBool>,
        last_activity_ticks: &Arc<Mutex<Option<Instant>>>,
        packet_count: &Arc<AtomicU64>,
        checking_connection: &Arc<AtomicBool>,
        video_timestamp_origin: &Arc<Mutex<Option<i64>>>,
        audio_timestamp_origin: &Arc<Mutex<Option<i64>>>,
        recycler: &Arc<Mutex<AvLibPacketRecycler>>,
    ) -> Result<(), String> {
        let mut pending: VecDeque<ClientSessionResult> = results.into_iter().collect();

        while let Some(result) = pending.pop_front() {
            match result {
                ClientSessionResult::OutboundResponse(packet) => {
                    Self::send_outbound_packet(stream, &packet)?;
                }
                ClientSessionResult::RaisedEvent(event) => match event {
                    ClientSessionEvent::ConnectionRequestAccepted => {
                        let playback_result =
                            session
                                .request_playback(target.stream_key.clone())
                                .map_err(|e| format!("request_playback failed: {}", e))?;
                        pending.push_back(playback_result);
                    }
                    ClientSessionEvent::ConnectionRequestRejected { description } => {
                        return Err(format!("RTMP connection rejected: {}", description));
                    }
                    ClientSessionEvent::PlaybackRequestAccepted => {
                        Debug::log(&format!(
                            "AvLibRtmpSource::PlaybackRequestAccepted app='{}' stream='{}'",
                            target.app, target.stream_key
                        ));
                    }
                    ClientSessionEvent::StreamMetadataReceived { metadata } => {
                        Self::update_video_config_from_metadata(
                            &metadata,
                            stream_types,
                            streams,
                            video_config,
                            is_connected,
                        );
                        Self::update_audio_config_from_metadata(
                            &metadata,
                            stream_types,
                            streams,
                            audio_config,
                        );
                    }
                    ClientSessionEvent::VideoDataReceived { timestamp, data } => {
                        Self::process_video_data_event(
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
                        Self::process_audio_data_event(
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
                        Debug::log_warning(&format!(
                            "AvLibRtmpSource::UnhandleableAmf0Command - {}",
                            command_name
                        ));
                    }
                    ClientSessionEvent::UnknownTransactionResultReceived {
                        transaction_id, ..
                    } => {
                        Debug::log_warning(&format!(
                            "AvLibRtmpSource::UnknownTransactionResult - {}",
                            transaction_id
                        ));
                    }
                    ClientSessionEvent::UnhandleableOnStatusCode { code } => {
                        let known_play_codes = matches!(
                            code.as_str(),
                            "Netstream.play.reset"
                                | "Netstream.Data.Start"
                                | "Netstream.play.PublishNotify"
                        );
                        if !known_play_codes {
                            Debug::log_warning(&format!(
                                "AvLibRtmpSource::UnhandleableOnStatusCode - {}",
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

    fn run_rtmp_loop(
        uri: String,
        stay_alive: Arc<AtomicBool>,
        packet_queues: Arc<Mutex<Vec<Arc<FixedSizeQueue<AvLibPacket>>>>>,
        stream_types: Arc<Mutex<Vec<AVMediaType>>>,
        streams: Arc<Mutex<Vec<AvLibStreamInfo>>>,
        video_config: Arc<Mutex<Option<RtmpVideoConfig>>>,
        audio_config: Arc<Mutex<Option<RtmpAudioConfig>>>,
        is_connected: Arc<AtomicBool>,
        last_activity_ticks: Arc<Mutex<Option<Instant>>>,
        packet_count: Arc<AtomicU64>,
        _timeout_count: Arc<AtomicU64>,
        checking_connection: Arc<AtomicBool>,
        video_timestamp_origin: Arc<Mutex<Option<i64>>>,
        audio_timestamp_origin: Arc<Mutex<Option<i64>>>,
        recycler: Arc<Mutex<AvLibPacketRecycler>>,
    ) -> Result<(), String> {
        if let Ok(mut origin) = video_timestamp_origin.lock() {
            *origin = None;
        }
        if let Ok(mut origin) = audio_timestamp_origin.lock() {
            *origin = None;
        }

        let target = Self::parse_rtmp_target(&uri)?;
        let mut socket = Self::connect_socket(&target)?;

        let mut handshake = Handshake::new(PeerType::Client);
        let outbound_c0_c1 = handshake
            .generate_outbound_p0_and_p1()
            .map_err(|e| format!("handshake init failed: {}", e))?;
        Self::write_all(&mut socket, &outbound_c0_c1)?;

        let mut session_config = ClientSessionConfig::new();
        session_config.tc_url = Some(target.tc_url.clone());
        let (mut session, initial_results) = ClientSession::new(session_config)
            .map_err(|e| format!("create RTMP session failed: {}", e))?;

        if !initial_results.is_empty() {
            Self::handle_client_results(
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
                            Self::write_all(&mut socket, &response_bytes)?;
                        }
                        continue;
                    }
                    HandshakeProcessResult::Completed {
                        response_bytes,
                        remaining_bytes,
                    } => {
                        if !response_bytes.is_empty() {
                            Self::write_all(&mut socket, &response_bytes)?;
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

                Self::handle_client_results(
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

            Self::handle_client_results(
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

    fn create_decoder_from_config(config: &RtmpVideoConfig) -> Option<ffmpeg_next::decoder::Video> {
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
                    Debug::log_error(
                        "AvLibRtmpSource::create_decoder_from_config - alloc extradata failed",
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

    fn create_audio_decoder_from_config(
        config: &RtmpAudioConfig,
    ) -> Option<ffmpeg_next::decoder::Audio> {
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
                    Debug::log_error(
                        "AvLibRtmpSource::create_audio_decoder_from_config - alloc extradata failed",
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

    pub fn video_decoder(&self, stream_index: i32) -> Option<ffmpeg_next::decoder::Video> {
        let stream_info = self.stream(stream_index);
        if stream_info.index < 0 {
            return None;
        }

        let config = self
            .video_config
            .lock()
            .ok()
            .and_then(|g| g.as_ref().cloned())?;

        Self::create_decoder_from_config(&config)
    }

    pub fn audio_decoder(&self, _stream_index: i32) -> Option<ffmpeg_next::decoder::Audio> {
        let config = self
            .audio_config
            .lock()
            .ok()
            .and_then(|g| g.as_ref().cloned())?;

        Self::create_audio_decoder_from_config(&config)
    }
}

impl AvLibSource for AvLibRtmpSource {
    fn connect(&mut self) {
        if !self._isconnected.load(Ordering::SeqCst) {
            self.connect_requested.store(true, Ordering::SeqCst);
        }
    }

    fn connection_state(&self) -> AvLibSourceConnectionState {
        if self._isconnected.load(Ordering::SeqCst) {
            if self._checkingconnection.load(Ordering::SeqCst) {
                AvLibSourceConnectionState::Checking
            } else {
                AvLibSourceConnectionState::Connected
            }
        } else if self.connect_requested.load(Ordering::SeqCst) {
            if self.reconnect_count.load(Ordering::SeqCst) > 0
                || self.timeout_count.load(Ordering::SeqCst) > 0
            {
                AvLibSourceConnectionState::Reconnecting
            } else {
                AvLibSourceConnectionState::Connecting
            }
        } else if self.reconnect_count.load(Ordering::SeqCst) > 0
            || self.timeout_count.load(Ordering::SeqCst) > 0
        {
            AvLibSourceConnectionState::Reconnecting
        } else {
            AvLibSourceConnectionState::Disconnected
        }
    }

    fn duration(&self) -> f64 {
        -1.0
    }

    fn stream_count(&self) -> i32 {
        self.streams.lock().unwrap().len() as i32
    }

    fn stream_type(&self, stream_index: i32) -> AVMediaType {
        if stream_index < 0 {
            Debug::log_error("AvLibRtmpSource::stream_type - stream_index was out of range");
            return AVMediaType::AVMEDIA_TYPE_UNKNOWN;
        }

        if !self.is_connected() {
            return AVMediaType::AVMEDIA_TYPE_UNKNOWN;
        }

        let types = self.stream_types.lock().unwrap();
        let idx = stream_index as usize;
        if idx >= types.len() {
            Debug::log_error("AvLibRtmpSource::stream_type - stream_index was out of range");
            return AVMediaType::AVMEDIA_TYPE_UNKNOWN;
        }

        types[idx]
    }

    fn stream(&self, stream_index: i32) -> AvLibStreamInfo {
        if stream_index < 0 {
            Debug::log_error("AvLibRtmpSource::stream - stream_index was out of range");
            return AvLibStreamInfo::empty();
        }

        if !self.is_connected() {
            return AvLibStreamInfo::empty();
        }

        let streams = self.streams.lock().unwrap();
        let idx = stream_index as usize;
        if idx >= streams.len() {
            Debug::log_error("AvLibRtmpSource::stream - stream_index was out of range");
            return AvLibStreamInfo::empty();
        }

        streams[idx]
    }

    fn time_base(&self, stream_index: i32) -> f64 {
        match stream_index {
            0 => self
                .video_config
                .lock()
                .ok()
                .and_then(|g| g.as_ref().map(|cfg| cfg.time_base))
                .unwrap_or(-1.0),
            1 => self
                .audio_config
                .lock()
                .ok()
                .and_then(|g| g.as_ref().map(|cfg| cfg.time_base))
                .unwrap_or(-1.0),
            _ => {
                Debug::log_error("AvLibRtmpSource::time_base - stream_index was out of range");
                -1.0
            }
        }
    }

    fn frame_rate(&self, stream_index: i32) -> f64 {
        match stream_index {
            0 => self
                .video_config
                .lock()
                .ok()
                .and_then(|g| g.as_ref().map(|cfg| cfg.frame_rate))
                .unwrap_or(-1.0),
            1 => 0.0,
            _ => {
                Debug::log_error("AvLibRtmpSource::frame_rate - stream_index was out of range");
                -1.0
            }
        }
    }

    fn frame_duration(&self, stream_index: i32) -> f64 {
        let frame_rate = self.frame_rate(stream_index);
        if frame_rate > 0.0 {
            1.0 / frame_rate
        } else {
            -1.0
        }
    }

    fn is_realtime(&self) -> bool {
        true
    }

    fn can_seek(&self) -> bool {
        false
    }

    fn seek(&mut self, _from: f64, _to: f64) {
        Debug::log_warning("AvLibRtmpSource::seek - realtime source cannot seek");
    }

    fn try_get_next(&mut self, stream_index: i32) -> Option<AvLibPacket> {
        if stream_index < 0 {
            Debug::log_error("AvLibRtmpSource::try_get_next - stream_index was out of range");
            return None;
        }

        if !self.is_connected() {
            return None;
        }

        let queue = {
            let queues = self.packet_queues.lock().unwrap();
            let idx = stream_index as usize;
            if idx >= queues.len() {
                Debug::log_error("AvLibRtmpSource::try_get_next - stream_index was out of range");
                return None;
            }
            queues.get(idx).cloned()
        };

        let packet = queue.and_then(|q| q.try_pop());
        if packet.is_none() {
            let count = self.packet_count.load(Ordering::SeqCst);
            if count > 0 {
                if !self._checkingconnection.load(Ordering::SeqCst) {
                    let last = self.last_activity_ticks.lock().ok().and_then(|v| *v);
                    if let Some(last_time) = last {
                        if last_time.elapsed().as_secs() > Self::BEGIN_TIMEOUT_CHECK_SECONDS {
                            self._checkingconnection.store(true, Ordering::SeqCst);
                            if let Ok(mut check_start) = self.check_start_ticks.lock() {
                                *check_start = Some(Instant::now());
                            }
                        }
                    }
                } else {
                    let started = self.check_start_ticks.lock().ok().and_then(|v| *v);
                    if let Some(start_time) = started {
                        if start_time.elapsed().as_secs() > Self::TIMEOUT_SECONDS {
                            self.timeout_count.fetch_add(1, Ordering::SeqCst);
                            self._isconnected.store(false, Ordering::SeqCst);
                            self._checkingconnection.store(false, Ordering::SeqCst);
                        }
                    }
                }
            }
        } else {
            self._isconnected.store(true, Ordering::SeqCst);
            self._checkingconnection.store(false, Ordering::SeqCst);
            if let Ok(mut last) = self.last_activity_ticks.lock() {
                *last = Some(Instant::now());
            }
        }

        packet
    }

    fn recycle(&mut self, packet: AvLibPacket) {
        self.recycler.lock().unwrap().recycle(packet);
    }

    fn create_video_decoder(&self, stream_index: i32) -> Option<ffmpeg_next::decoder::Video> {
        self.video_decoder(stream_index)
    }

    fn create_audio_decoder(&self, stream_index: i32) -> Option<ffmpeg_next::decoder::Audio> {
        self.audio_decoder(stream_index)
    }

    fn runtime_stats(&self) -> AvLibSourceRuntimeStats {
        let last_activity_age_sec = self
            .last_activity_ticks
            .lock()
            .ok()
            .and_then(|last| *last)
            .map(|last| last.elapsed().as_secs_f64())
            .unwrap_or(-1.0);

        AvLibSourceRuntimeStats {
            connection_state: self.connection_state(),
            packet_count: self.packet_count.load(Ordering::SeqCst),
            timeout_count: self.timeout_count.load(Ordering::SeqCst),
            reconnect_count: self.reconnect_count.load(Ordering::SeqCst),
            is_checking_connection: self._checkingconnection.load(Ordering::SeqCst),
            last_activity_age_sec,
        }
    }
}

impl Drop for AvLibRtmpSource {
    fn drop(&mut self) {
        self.stay_alive.store(false, Ordering::SeqCst);
        if let Some(t) = self.thread.take() {
            let _ = t.join();
        }
    }
}
