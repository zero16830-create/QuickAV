use crate::av_lib_file_source::AvLibFileSource;
use crate::av_lib_rtmp_source::AvLibRtmpSource;
use crate::av_lib_rtsp_source::AvLibRtspSource;
use crate::av_lib_source::AvLibSource;

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

    pub fn detect_kind(uri: &str) -> SourceKind {
        if Self::starts_with_ignore_ascii_case(uri, Self::RTSP_PREFIX) {
            SourceKind::Rtsp
        } else if Self::starts_with_ignore_ascii_case(uri, Self::RTMP_PREFIX) {
            SourceKind::Rtmp
        } else {
            SourceKind::File
        }
    }

    pub fn is_remote_uri(uri: &str) -> bool {
        matches!(Self::detect_kind(uri), SourceKind::Rtsp | SourceKind::Rtmp)
    }

    pub fn create(uri: String) -> Box<dyn AvLibSource + Send> {
        match Self::detect_kind(&uri) {
            SourceKind::Rtsp => Box::new(AvLibRtspSource::new(uri)),
            SourceKind::Rtmp => Box::new(AvLibRtmpSource::new(uri)),
            SourceKind::File => Box::new(AvLibFileSource::new(uri)),
        }
    }

    fn starts_with_ignore_ascii_case(value: &str, prefix: &str) -> bool {
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
    fn detect_kind_supports_case_insensitive_protocols() {
        assert_eq!(
            SourceFactory::detect_kind("rtsp://127.0.0.1/live"),
            SourceKind::Rtsp
        );
        assert_eq!(
            SourceFactory::detect_kind("RTMP://127.0.0.1/live"),
            SourceKind::Rtmp
        );
        assert_eq!(SourceFactory::detect_kind("Video.mp4"), SourceKind::File);
    }

    #[test]
    fn is_remote_uri_only_matches_known_realtime_protocols() {
        assert!(SourceFactory::is_remote_uri("RtSp://camera/live"));
        assert!(SourceFactory::is_remote_uri("rtmp://server/app"));
        assert!(!SourceFactory::is_remote_uri(
            "http://example.com/video.mp4"
        ));
        assert!(!SourceFactory::is_remote_uri("D:/videos/sample.mp4"));
    }
}
