#![allow(non_snake_case)]

use crate::AVLibFileSource::AVLibFileSource;
use crate::AVLibRTMPSource::AVLibRTMPSource;
use crate::AVLibRTSPSource::AVLibRTSPSource;
use crate::IAVLibSource::IAVLibSource;

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum SourceKind {
    File,
    Rtsp,
    Rtmp,
}

pub struct SourceFactory;

impl SourceFactory {
    const RTSP_PREFIX: &'static str = "rtsp://";
    const RTMP_PREFIX: &'static str = "rtmp://";

    pub fn DetectKind(uri: &str) -> SourceKind {
        if Self::StartsWithIgnoreAsciiCase(uri, Self::RTSP_PREFIX) {
            SourceKind::Rtsp
        } else if Self::StartsWithIgnoreAsciiCase(uri, Self::RTMP_PREFIX) {
            SourceKind::Rtmp
        } else {
            SourceKind::File
        }
    }

    pub fn IsRemoteUri(uri: &str) -> bool {
        matches!(Self::DetectKind(uri), SourceKind::Rtsp | SourceKind::Rtmp)
    }

    pub fn Create(uri: String) -> Box<dyn IAVLibSource + Send> {
        match Self::DetectKind(&uri) {
            SourceKind::Rtsp => Box::new(AVLibRTSPSource::new(uri)),
            SourceKind::Rtmp => Box::new(AVLibRTMPSource::new(uri)),
            SourceKind::File => Box::new(AVLibFileSource::new(uri)),
        }
    }

    fn StartsWithIgnoreAsciiCase(value: &str, prefix: &str) -> bool {
        value
            .get(..prefix.len())
            .map(|candidate| candidate.eq_ignore_ascii_case(prefix))
            .unwrap_or(false)
    }
}

#[cfg(test)]
mod tests {
    use super::{SourceFactory, SourceKind};

    #[test]
    fn DetectKind_supports_case_insensitive_protocols() {
        assert_eq!(
            SourceFactory::DetectKind("rtsp://127.0.0.1/live"),
            SourceKind::Rtsp
        );
        assert_eq!(
            SourceFactory::DetectKind("RTMP://127.0.0.1/live"),
            SourceKind::Rtmp
        );
        assert_eq!(
            SourceFactory::DetectKind("Video.mp4"),
            SourceKind::File
        );
    }

    #[test]
    fn IsRemoteUri_only_matches_known_realtime_protocols() {
        assert!(SourceFactory::IsRemoteUri("RtSp://camera/live"));
        assert!(SourceFactory::IsRemoteUri("rtmp://server/app"));
        assert!(!SourceFactory::IsRemoteUri("http://example.com/video.mp4"));
        assert!(!SourceFactory::IsRemoteUri("D:/videos/sample.mp4"));
    }
}
