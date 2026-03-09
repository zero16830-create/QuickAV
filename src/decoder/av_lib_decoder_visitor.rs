use crate::av_lib_video_decoder::AvLibVideoDecoder;

pub trait AvLibDecoderVisitor {
    fn visit(&mut self, video_decoder: &mut AvLibVideoDecoder);
}
