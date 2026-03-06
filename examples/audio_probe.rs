use rustav_native::Logging::Debug::{Initialize, Teardown};
use rustav_native::{
    CopyAudioPCM, CreatePlayerPullRGBA, GetAudioMetaPCM, GetFrameMetaRGBA, Play, ReleasePlayer,
    RustAVAudioMeta, RustAVFrameMeta, UpdatePlayer,
};
use std::ffi::CString;
use std::thread;
use std::time::{Duration, Instant};

fn parse_arg<T: std::str::FromStr>(args: &[String], index: usize, default: T) -> T {
    if let Some(v) = args.get(index) {
        if let Ok(parsed) = v.parse::<T>() {
            return parsed;
        }
    }
    default
}

fn sample_video_uri() -> String {
    let candidates = [
        "../TestFiles/SampleVideo_1280x720_10mb.mp4",
        "TestFiles/SampleVideo_1280x720_10mb.mp4",
        "./TestFiles/SampleVideo_1280x720_10mb.mp4",
    ];

    for candidate in candidates {
        if std::path::Path::new(candidate).exists() {
            return candidate.to_string();
        }
    }

    candidates[0].to_string()
}

fn empty_frame_meta() -> RustAVFrameMeta {
    RustAVFrameMeta {
        width: 0,
        height: 0,
        format: 0,
        stride: 0,
        data_size: 0,
        time_sec: 0.0,
        frame_index: 0,
    }
}

fn empty_audio_meta() -> RustAVAudioMeta {
    RustAVAudioMeta {
        sample_rate: 0,
        channels: 0,
        bytes_per_sample: 0,
        sample_format: 0,
        buffered_bytes: 0,
        time_sec: 0.0,
        frame_index: 0,
    }
}

fn main() {
    let args: Vec<String> = std::env::args().collect();
    let uri = args.get(1).cloned().unwrap_or_else(sample_video_uri);
    let run_seconds: f64 = parse_arg(&args, 2, 6.0);
    let width: i32 = parse_arg(&args, 3, 1280);
    let height: i32 = parse_arg(&args, 4, 720);

    Initialize(false);

    let uri_c = match CString::new(uri.clone()) {
        Ok(v) => v,
        Err(_) => {
            eprintln!("[FAIL] uri contains interior NUL");
            std::process::exit(2);
        }
    };

    let id = CreatePlayerPullRGBA(uri_c.as_ptr(), width, height);
    if id < 0 {
        eprintln!("[FAIL] CreatePlayerPullRGBA failed for uri={}", uri);
        std::process::exit(1);
    }

    if Play(id) != 0 {
        let _ = ReleasePlayer(id);
        eprintln!("[FAIL] Play failed for id={}", id);
        std::process::exit(1);
    }

    let start = Instant::now();
    let mut last_print = Instant::now();
    let mut last_frame_index = -1i64;
    let mut frame_count = 0usize;
    let mut audio_bytes = 0usize;
    let mut audio_chunks = 0usize;
    let mut first_frame_elapsed: Option<f64> = None;
    let mut first_audio_elapsed: Option<f64> = None;

    while start.elapsed().as_secs_f64() < run_seconds {
        let _ = UpdatePlayer(id);
        thread::sleep(Duration::from_millis(20));

        let mut frame_meta = empty_frame_meta();
        if GetFrameMetaRGBA(id, &mut frame_meta as *mut RustAVFrameMeta) > 0
            && frame_meta.frame_index != last_frame_index
        {
            last_frame_index = frame_meta.frame_index;
            frame_count += 1;
            if first_frame_elapsed.is_none() {
                first_frame_elapsed = Some(start.elapsed().as_secs_f64());
                println!(
                    "[audio_probe] first_video={:.3}s frame_index={} size={}x{}",
                    first_frame_elapsed.unwrap(),
                    frame_meta.frame_index,
                    frame_meta.width,
                    frame_meta.height
                );
            }
        }

        let mut audio_meta = empty_audio_meta();
        let audio_ready = GetAudioMetaPCM(id, &mut audio_meta as *mut RustAVAudioMeta);
        if audio_ready > 0 && audio_meta.buffered_bytes > 0 {
            let read_len = (audio_meta.buffered_bytes as usize).min(64 * 1024);
            let mut buffer = vec![0u8; read_len];
            let copied = CopyAudioPCM(id, buffer.as_mut_ptr(), buffer.len() as i32);
            if copied > 0 {
                audio_bytes += copied as usize;
                audio_chunks += 1;
                if first_audio_elapsed.is_none() {
                    first_audio_elapsed = Some(start.elapsed().as_secs_f64());
                    println!(
                        "[audio_probe] first_audio={:.3}s sample_rate={} channels={} copied={} pts={:.3}",
                        first_audio_elapsed.unwrap(),
                        audio_meta.sample_rate,
                        audio_meta.channels,
                        copied,
                        audio_meta.time_sec
                    );
                }
            }
        }

        if last_print.elapsed().as_secs_f64() >= 1.0 {
            println!(
                "[audio_probe] elapsed={:.2}s frames={} audio_bytes={} audio_chunks={}",
                start.elapsed().as_secs_f64(),
                frame_count,
                audio_bytes,
                audio_chunks
            );
            last_print = Instant::now();
        }
    }

    let _ = ReleasePlayer(id);
    Teardown();

    println!(
        "[audio_probe] done uri={} seconds={:.2} frames={} audio_bytes={} audio_chunks={}",
        uri, run_seconds, frame_count, audio_bytes, audio_chunks
    );
    if let Some(first_frame) = first_frame_elapsed {
        println!("[audio_probe] first_video_final={:.3}s", first_frame);
    }
    if let Some(first_audio) = first_audio_elapsed {
        println!("[audio_probe] first_audio_final={:.3}s", first_audio);
    }

    if frame_count == 0 || audio_bytes == 0 {
        eprintln!(
            "[FAIL] insufficient media output frames={} audio_bytes={}",
            frame_count, audio_bytes
        );
        std::process::exit(1);
    }
}
