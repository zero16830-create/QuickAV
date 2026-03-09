#![allow(non_snake_case)]

use crate::AVLibPacket::AVLibPacket;
use crate::AVLibPacketRecycler::AVLibPacketRecycler;
use crate::FixedSizeQueue::FixedSizeQueue;
use crate::IAVLibSource::{
    AVLibSourceConnectionState, AVLibSourceRuntimeStats, AVLibStreamInfo, IAVLibSource,
};
use crate::Logging::Debug::Debug;
use ffmpeg_next::codec::{self, packet::Flags as PacketFlags};
use ffmpeg_next::ffi::{av_mallocz, AVMediaType, AV_CODEC_FLAG_LOW_DELAY, AV_INPUT_BUFFER_PADDING_SIZE};
use ffmpeg_next::{Packet, Rational};
use futures_util::StreamExt;
use retina::client::{
    InitialTimestampPolicy, PlayOptions, Session, SessionOptions, SetupOptions,
    TcpTransportOptions, Transport, UdpTransportOptions,
};
use retina::codec::{CodecItem, ParametersRef};
use std::ptr;
use std::sync::{
    atomic::{AtomicBool, AtomicU64, Ordering},
    Arc, Mutex,
};
use std::thread;
use std::time::{Duration, Instant};
use tokio::runtime::Builder;
use url::Url;

#[derive(Clone, PartialEq)]
struct RetinaVideoConfig {
    codec_id: codec::Id,
    width: i32,
    height: i32,
    clock_rate_hz: u32,
    time_base: f64,
    frame_rate: f64,
    extra_data: Vec<u8>,
}

#[derive(Clone, PartialEq)]
struct RetinaAudioConfig {
    codec_id: codec::Id,
    sample_rate: i32,
    channels: i32,
    clock_rate_hz: u32,
    time_base: f64,
    extra_data: Vec<u8>,
}

pub struct AVLibRTSPSource {
    _uri: String,
    _packetQueues: Arc<Mutex<Vec<Arc<FixedSizeQueue<AVLibPacket>>>>>,
    _streamTypes: Arc<Mutex<Vec<AVMediaType>>>,
    _streams: Arc<Mutex<Vec<AVLibStreamInfo>>>,
    _videoConfig: Arc<Mutex<Option<RetinaVideoConfig>>>,
    _audioConfig: Arc<Mutex<Option<RetinaAudioConfig>>>,
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
    _recycler: Arc<Mutex<AVLibPacketRecycler>>,
}

impl AVLibRTSPSource {
    const REALTIME_VIDEO_PACKET_QUEUE_SIZE: usize = 3;
    const REALTIME_AUDIO_PACKET_QUEUE_SIZE: usize = 96;
    const BEGIN_TIMEOUT_CHECK_SECONDS: u64 = 3;
    const TIMEOUT_SECONDS: u64 = 2;
    const CONNECT_RETRY_DELAY_MS: u64 = 200;
    const POLL_TIMEOUT_MS: u64 = 200;

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
            _recycler: recycler.clone(),
        };

        source._thread = Some(thread::spawn(move || {
            let runtime = match Builder::new_current_thread().enable_all().build() {
                Ok(r) => r,
                Err(e) => {
                    Debug::LogError(&format!(
                        "AVLibRTSPSource::new - failed to create tokio runtime: {}",
                        e
                    ));
                    return;
                }
            };

            while stay_alive.load(Ordering::SeqCst) {
                if !connect_requested.swap(false, Ordering::SeqCst) {
                    thread::sleep(Duration::from_millis(50));
                    continue;
                }

                let run_result = runtime.block_on(Self::RunRetinaLoop(
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
                    recycler.clone(),
                ));

                if let Err(err) = run_result {
                    reconnect_count.fetch_add(1, Ordering::SeqCst);
                    Debug::LogWarning(&format!("AVLibRTSPSource::RunRetinaLoop - {}", err));
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

    fn SelectVideoStreamIndex(streams: &[retina::client::Stream]) -> Option<usize> {
        let mut fallback_h265: Option<usize> = None;

        for (index, stream) in streams.iter().enumerate() {
            if stream.media() != "video" {
                continue;
            }

            match stream.encoding_name() {
                "h264" => return Some(index),
                "h265" | "hevc" => {
                    if fallback_h265.is_none() {
                        fallback_h265 = Some(index);
                    }
                }
                _ => {}
            }
        }

        fallback_h265
    }

    fn SelectAudioStreamIndex(streams: &[retina::client::Stream]) -> Option<usize> {
        for (index, stream) in streams.iter().enumerate() {
            if stream.media() != "audio" {
                continue;
            }

            if Self::AudioCodecIdFromEncodingName(stream.encoding_name()).is_some() {
                return Some(index);
            }
        }

        None
    }

    fn CodecIdFromEncodingName(encoding_name: &str) -> Option<codec::Id> {
        match encoding_name {
            "h264" => Some(codec::Id::H264),
            "h265" | "hevc" => Some(codec::Id::HEVC),
            _ => None,
        }
    }

    fn AudioCodecIdFromEncodingName(encoding_name: &str) -> Option<codec::Id> {
        match encoding_name {
            "mpeg4-generic" | "aac" => Some(codec::Id::AAC),
            "pcma" => Some(codec::Id::PCM_ALAW),
            "pcmu" => Some(codec::Id::PCM_MULAW),
            "opus" => Some(codec::Id::OPUS),
            _ => None,
        }
    }

    fn BuildVideoConfig(stream: &retina::client::Stream) -> Option<RetinaVideoConfig> {
        let codec_id = Self::CodecIdFromEncodingName(stream.encoding_name())?;
        let clock_rate_hz = stream.clock_rate_hz();
        if clock_rate_hz == 0 {
            return None;
        }

        let ParametersRef::Video(video_params) = stream.parameters()? else {
            return None;
        };

        let (width, height) = video_params.pixel_dimensions();
        if width == 0 || height == 0 {
            return None;
        }

        let frame_rate = stream.framerate().map(f64::from).unwrap_or(-1.0);

        Some(RetinaVideoConfig {
            codec_id,
            width: width as i32,
            height: height as i32,
            clock_rate_hz,
            time_base: 1.0 / clock_rate_hz as f64,
            frame_rate,
            extra_data: video_params.extra_data().to_vec(),
        })
    }

    fn BuildAudioConfig(stream: &retina::client::Stream) -> Option<RetinaAudioConfig> {
        let codec_id = Self::AudioCodecIdFromEncodingName(stream.encoding_name())?;
        let clock_rate_hz = stream.clock_rate_hz();
        if clock_rate_hz == 0 {
            return None;
        }

        let (sample_rate, extra_data) = match stream.parameters()? {
            ParametersRef::Audio(audio_params) => {
                (audio_params.clock_rate() as i32, audio_params.extra_data().to_vec())
            }
            _ => return None,
        };

        let channels = stream.channels().map(|v| v.get() as i32).unwrap_or(0);

        Some(RetinaAudioConfig {
            codec_id,
            sample_rate: if sample_rate > 0 {
                sample_rate
            } else {
                clock_rate_hz as i32
            },
            channels,
            clock_rate_hz,
            time_base: 1.0 / clock_rate_hz as f64,
            extra_data,
        })
    }

    fn PublishVideoConfig(
        config: RetinaVideoConfig,
        stream_types: &Arc<Mutex<Vec<AVMediaType>>>,
        streams: &Arc<Mutex<Vec<AVLibStreamInfo>>>,
        video_config: &Arc<Mutex<Option<RetinaVideoConfig>>>,
        is_connected: &Arc<AtomicBool>,
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
                first.width = config.width;
                first.height = config.height;
            }
        }

        {
            let mut config_guard = video_config
                .lock()
                .unwrap_or_else(|poisoned| poisoned.into_inner());
            *config_guard = Some(config);
        }

        is_connected.store(true, Ordering::SeqCst);
    }

    fn PublishAudioConfig(
        config: RetinaAudioConfig,
        stream_types: &Arc<Mutex<Vec<AVMediaType>>>,
        streams: &Arc<Mutex<Vec<AVLibStreamInfo>>>,
        audio_config: &Arc<Mutex<Option<RetinaAudioConfig>>>,
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
                entry.sample_rate = config.sample_rate;
                entry.channels = config.channels;
            }
        }

        {
            let mut config_guard = audio_config
                .lock()
                .unwrap_or_else(|poisoned| poisoned.into_inner());
            *config_guard = Some(config);
        }
    }

    fn UpdateVideoConfigIfAvailable(
        stream_index: usize,
        demuxed_streams: &[retina::client::Stream],
        stream_types: &Arc<Mutex<Vec<AVMediaType>>>,
        streams: &Arc<Mutex<Vec<AVLibStreamInfo>>>,
        video_config: &Arc<Mutex<Option<RetinaVideoConfig>>>,
        is_connected: &Arc<AtomicBool>,
    ) {
        let Some(stream) = demuxed_streams.get(stream_index) else {
            return;
        };
        let Some(new_config) = Self::BuildVideoConfig(stream) else {
            return;
        };

        let changed = {
            let config_guard = video_config
                .lock()
                .unwrap_or_else(|poisoned| poisoned.into_inner());
            config_guard.as_ref() != Some(&new_config)
        };

        if changed {
            Debug::Log(&format!(
                "AVLibRTSPSource::UpdateVideoConfigIfAvailable - {} {}x{}",
                stream.encoding_name(),
                new_config.width,
                new_config.height
            ));
        }

        Self::PublishVideoConfig(
            new_config,
            stream_types,
            streams,
            video_config,
            is_connected,
        );
    }

    fn UpdateAudioConfigIfAvailable(
        stream_index: usize,
        demuxed_streams: &[retina::client::Stream],
        stream_types: &Arc<Mutex<Vec<AVMediaType>>>,
        streams: &Arc<Mutex<Vec<AVLibStreamInfo>>>,
        audio_config: &Arc<Mutex<Option<RetinaAudioConfig>>>,
    ) {
        let Some(stream) = demuxed_streams.get(stream_index) else {
            return;
        };
        let Some(new_config) = Self::BuildAudioConfig(stream) else {
            return;
        };

        let changed = {
            let config_guard = audio_config
                .lock()
                .unwrap_or_else(|poisoned| poisoned.into_inner());
            config_guard.as_ref() != Some(&new_config)
        };

        if changed {
            Debug::Log(&format!(
                "AVLibRTSPSource::UpdateAudioConfigIfAvailable - {} {}Hz ch={}",
                stream.encoding_name(),
                new_config.sample_rate,
                new_config.channels
            ));
        }

        Self::PublishAudioConfig(new_config, stream_types, streams, audio_config);
    }

    fn BuildPacketFromVideoFrame(
        frame: &retina::codec::VideoFrame,
        normalized_pts: i64,
        recycler: &Arc<Mutex<AVLibPacketRecycler>>,
    ) -> AVLibPacket {
        let mut wrapped_packet = recycler
            .lock()
            .unwrap_or_else(|poisoned| poisoned.into_inner())
            .GetPacket();

        let mut packet = Packet::copy(frame.data());
        packet.set_stream(0);

        packet.set_pts(Some(normalized_pts));
        packet.set_dts(Some(normalized_pts));
        packet.set_duration(0);

        if frame.is_random_access_point() {
            packet.set_flags(PacketFlags::KEY);
        }

        wrapped_packet.Packet = packet;
        wrapped_packet
    }

    fn BuildPacketFromAudioFrame(
        frame: &retina::codec::AudioFrame,
        normalized_pts: i64,
        recycler: &Arc<Mutex<AVLibPacketRecycler>>,
    ) -> AVLibPacket {
        let mut wrapped_packet = recycler
            .lock()
            .unwrap_or_else(|poisoned| poisoned.into_inner())
            .GetPacket();

        let mut packet = Packet::copy(frame.data());
        packet.set_stream(1);

        packet.set_pts(Some(normalized_pts));
        packet.set_dts(Some(normalized_pts));
        packet.set_duration(frame.frame_length().get() as i64);

        wrapped_packet.Packet = packet;
        wrapped_packet
    }

    async fn RunRetinaLoop(
        uri: String,
        stay_alive: Arc<AtomicBool>,
        packet_queues: Arc<Mutex<Vec<Arc<FixedSizeQueue<AVLibPacket>>>>>,
        stream_types: Arc<Mutex<Vec<AVMediaType>>>,
        streams: Arc<Mutex<Vec<AVLibStreamInfo>>>,
        video_config: Arc<Mutex<Option<RetinaVideoConfig>>>,
        audio_config: Arc<Mutex<Option<RetinaAudioConfig>>>,
        is_connected: Arc<AtomicBool>,
        last_activity_ticks: Arc<Mutex<Option<Instant>>>,
        packet_count: Arc<AtomicU64>,
        _timeout_count: Arc<AtomicU64>,
        checking_connection: Arc<AtomicBool>,
        recycler: Arc<Mutex<AVLibPacketRecycler>>,
    ) -> Result<(), String> {
        let url = Url::parse(&uri).map_err(|e| format!("invalid RTSP uri {}: {}", uri, e))?;

        let (described, video_stream_index, audio_stream_index) = {
            let mut udp_described = Session::describe(url.clone(), SessionOptions::default())
                .await
                .map_err(|e| format!("DESCRIBE failed: {}", e))?;

            let udp_video_stream_index = Self::SelectVideoStreamIndex(udp_described.streams())
                .ok_or_else(|| "no supported video stream (expected h264/h265)".to_string())?;
            let udp_audio_stream_index = Self::SelectAudioStreamIndex(udp_described.streams());

            match async {
                udp_described
                    .setup(
                        udp_video_stream_index,
                        SetupOptions::default()
                            .transport(Transport::Udp(UdpTransportOptions::default())),
                    )
                    .await
                    .map_err(|e| format!("SETUP video failed: {}", e))?;

                if let Some(audio_index) = udp_audio_stream_index {
                    udp_described
                        .setup(
                            audio_index,
                            SetupOptions::default()
                                .transport(Transport::Udp(UdpTransportOptions::default())),
                        )
                        .await
                        .map_err(|e| format!("SETUP audio failed: {}", e))?;
                }

                Ok::<(), String>(())
            }
            .await
            {
                Ok(_) => {
                    Debug::Log("AVLibRTSPSource::RunRetinaLoop - transport=udp");
                    (udp_described, udp_video_stream_index, udp_audio_stream_index)
                }
                Err(udp_err) => {
                    Debug::LogWarning(&format!(
                        "AVLibRTSPSource::RunRetinaLoop - UDP setup failed, fallback to TCP: {}",
                        udp_err
                    ));

                    let mut tcp_described =
                        Session::describe(url.clone(), SessionOptions::default())
                            .await
                            .map_err(|e| format!("DESCRIBE failed: {}", e))?;

                    let tcp_video_stream_index = Self::SelectVideoStreamIndex(tcp_described.streams())
                        .ok_or_else(|| {
                            "no supported video stream (expected h264/h265)".to_string()
                        })?;
                    let tcp_audio_stream_index = Self::SelectAudioStreamIndex(tcp_described.streams());

                    tcp_described
                        .setup(
                            tcp_video_stream_index,
                            SetupOptions::default()
                                .transport(Transport::Tcp(TcpTransportOptions::default())),
                        )
                        .await
                        .map_err(|e| format!("SETUP video failed: {}", e))?;

                    if let Some(audio_index) = tcp_audio_stream_index {
                        tcp_described
                            .setup(
                                audio_index,
                                SetupOptions::default()
                                    .transport(Transport::Tcp(TcpTransportOptions::default())),
                            )
                            .await
                            .map_err(|e| format!("SETUP audio failed: {}", e))?;
                    }

                    Debug::Log("AVLibRTSPSource::RunRetinaLoop - transport=tcp");
                    (tcp_described, tcp_video_stream_index, tcp_audio_stream_index)
                }
            }
        };

        let mut demuxed = described
            .play(PlayOptions::default().initial_timestamp(InitialTimestampPolicy::Permissive))
            .await
            .map_err(|e| format!("PLAY failed: {}", e))?
            .demuxed()
            .map_err(|e| format!("DEMUX init failed: {}", e))?;

        Self::UpdateVideoConfigIfAvailable(
            video_stream_index,
            demuxed.streams(),
            &stream_types,
            &streams,
            &video_config,
            &is_connected,
        );
        if let Some(audio_stream_index) = audio_stream_index {
            Self::UpdateAudioConfigIfAvailable(
                audio_stream_index,
                demuxed.streams(),
                &stream_types,
                &streams,
                &audio_config,
            );
        }

        let mut video_timestamp_origin: Option<i64> = None;
        let mut audio_timestamp_origin: Option<i64> = None;

        loop {
            if !stay_alive.load(Ordering::SeqCst) {
                return Ok(());
            }

            let next_result =
                tokio::time::timeout(Duration::from_millis(Self::POLL_TIMEOUT_MS), demuxed.next())
                    .await;

            let item_opt = match next_result {
                Ok(v) => v,
                Err(_) => continue,
            };

            let Some(item_result) = item_opt else {
                return Err("demuxed stream ended".to_string());
            };

            let item = item_result.map_err(|e| format!("demux error: {}", e))?;

            match item {
                CodecItem::VideoFrame(video_frame) => {
                    if video_frame.stream_id() != video_stream_index {
                        continue;
                    }

                    if video_frame.has_new_parameters() || !is_connected.load(Ordering::SeqCst) {
                        Self::UpdateVideoConfigIfAvailable(
                            video_stream_index,
                            demuxed.streams(),
                            &stream_types,
                            &streams,
                            &video_config,
                            &is_connected,
                        );
                    }

                    if !is_connected.load(Ordering::SeqCst) {
                        continue;
                    }

                    let queue = {
                        let queues_guard = packet_queues
                            .lock()
                            .unwrap_or_else(|poisoned| poisoned.into_inner());
                        queues_guard.get(0).cloned()
                    };

                    let raw_pts = video_frame.timestamp().elapsed();
                    let origin = video_timestamp_origin.get_or_insert(raw_pts);
                    let normalized_pts = raw_pts.saturating_sub(*origin);

                    let wrapped =
                        Self::BuildPacketFromVideoFrame(&video_frame, normalized_pts, &recycler);

                    if let Some(q) = queue {
                        q.Push(wrapped);
                    } else {
                        recycler
                            .lock()
                            .unwrap_or_else(|poisoned| poisoned.into_inner())
                            .Recycle(wrapped);
                        continue;
                    }

                    packet_count.fetch_add(1, Ordering::SeqCst);
                    if let Ok(mut last) = last_activity_ticks.lock() {
                        *last = Some(Instant::now());
                    }
                    checking_connection.store(false, Ordering::SeqCst);
                }
                CodecItem::AudioFrame(audio_frame) => {
                    if Some(audio_frame.stream_id()) != audio_stream_index {
                        continue;
                    }

                    if let Some(audio_stream_index) = audio_stream_index {
                        Self::UpdateAudioConfigIfAvailable(
                            audio_stream_index,
                            demuxed.streams(),
                            &stream_types,
                            &streams,
                            &audio_config,
                        );
                    }

                    let queue = {
                        let queues_guard = packet_queues
                            .lock()
                            .unwrap_or_else(|poisoned| poisoned.into_inner());
                        queues_guard.get(1).cloned()
                    };

                    let raw_pts = audio_frame.timestamp().elapsed();
                    let origin = audio_timestamp_origin.get_or_insert(raw_pts);
                    let normalized_pts = raw_pts.saturating_sub(*origin);

                    let wrapped =
                        Self::BuildPacketFromAudioFrame(&audio_frame, normalized_pts, &recycler);

                    if let Some(q) = queue {
                        q.Push(wrapped);
                    } else {
                        recycler
                            .lock()
                            .unwrap_or_else(|poisoned| poisoned.into_inner())
                            .Recycle(wrapped);
                        continue;
                    }

                    packet_count.fetch_add(1, Ordering::SeqCst);
                    if let Ok(mut last) = last_activity_ticks.lock() {
                        *last = Some(Instant::now());
                    }
                    checking_connection.store(false, Ordering::SeqCst);
                }
                _ => continue,
            }
        }
    }

    fn CreateDecoderFromConfig(config: &RetinaVideoConfig) -> Option<ffmpeg_next::decoder::Video> {
        let codec = ffmpeg_next::decoder::find(config.codec_id)?;
        let mut context = ffmpeg_next::codec::context::Context::new_with_codec(codec);

        let time_base = Rational::new(1, config.clock_rate_hz as i32);
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
                        "AVLibRTSPSource::CreateDecoderFromConfig - alloc extradata failed",
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

    fn CreateAudioDecoderFromConfig(
        config: &RetinaAudioConfig,
    ) -> Option<ffmpeg_next::decoder::Audio> {
        let codec = ffmpeg_next::decoder::find(config.codec_id)?;
        let mut context = ffmpeg_next::codec::context::Context::new_with_codec(codec);

        let time_base = Rational::new(1, config.clock_rate_hz as i32);
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
                        "AVLibRTSPSource::CreateAudioDecoderFromConfig - alloc extradata failed",
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

impl IAVLibSource for AVLibRTSPSource {
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
            Debug::LogError("AVLibRTSPSource::StreamType - streamIndex was out of range");
            return AVMediaType::AVMEDIA_TYPE_UNKNOWN;
        }

        if !self.IsConnected() {
            return AVMediaType::AVMEDIA_TYPE_UNKNOWN;
        }

        let types = self._streamTypes.lock().unwrap();
        let idx = streamIndex as usize;
        if idx >= types.len() {
            Debug::LogError("AVLibRTSPSource::StreamType - streamIndex was out of range");
            return AVMediaType::AVMEDIA_TYPE_UNKNOWN;
        }

        types[idx]
    }

    fn Stream(&self, streamIndex: i32) -> AVLibStreamInfo {
        if streamIndex < 0 {
            Debug::LogError("AVLibRTSPSource::Stream - streamIndex was out of range");
            return AVLibStreamInfo::empty();
        }

        if !self.IsConnected() {
            return AVLibStreamInfo::empty();
        }

        let streams = self._streams.lock().unwrap();
        let idx = streamIndex as usize;
        if idx >= streams.len() {
            Debug::LogError("AVLibRTSPSource::Stream - streamIndex was out of range");
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
                Debug::LogError("AVLibRTSPSource::TimeBase - streamIndex was out of range");
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
                Debug::LogError("AVLibRTSPSource::FrameRate - streamIndex was out of range");
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
        Debug::LogWarning("AVLibRTSPSource::Seek - realtime source cannot seek");
    }

    fn TryGetNext(&mut self, streamIndex: i32) -> Option<AVLibPacket> {
        if streamIndex < 0 {
            Debug::LogError("AVLibRTSPSource::TryGetNext - streamIndex was out of range");
            return None;
        }

        if !self.IsConnected() {
            return None;
        }

        let queue = {
            let queues = self._packetQueues.lock().unwrap();
            let idx = streamIndex as usize;
            if idx >= queues.len() {
                Debug::LogError("AVLibRTSPSource::TryGetNext - streamIndex was out of range");
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

impl Drop for AVLibRTSPSource {
    fn drop(&mut self) {
        self._stayAlive.store(false, Ordering::SeqCst);
        if let Some(t) = self._thread.take() {
            let _ = t.join();
        }
    }
}
