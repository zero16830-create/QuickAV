#![allow(non_snake_case)]
use crate::AVLibVideoDecoder::AVLibVideoDecoder;

pub trait IAVLibDecoderVisitor {
    fn Visit(&mut self, videoDecoder: &mut AVLibVideoDecoder);
}
