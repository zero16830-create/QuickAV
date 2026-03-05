#![allow(non_snake_case)]

use ffmpeg_next::Packet;

pub struct AVLibPacket {
    pub Packet: Packet,
    pub _isEOF: bool,
    pub _isSeekRequest: bool,
    pub _seekRequestTime: f64,
}

impl AVLibPacket {
    pub fn new() -> Self {
        Self {
            Packet: Packet::empty(),
            _isEOF: false,
            _isSeekRequest: false,
            _seekRequestTime: 0.0,
        }
    }

    pub fn IsEOF(&self) -> bool {
        self._isEOF
    }

    pub fn IsSeekRequest(&self) -> bool {
        self._isSeekRequest
    }

    pub fn SetAsEOF(&mut self) {
        self._isEOF = true;
    }

    pub fn SetSeekRequest(&mut self, time: f64) {
        self._isSeekRequest = true;
        self._seekRequestTime = time;
    }

    pub fn SeekTime(&self) -> f64 {
        self._seekRequestTime
    }

    pub fn OnRecycle(&mut self) {
        self.Clean();
        self._isEOF = false;
        self._isSeekRequest = false;
        self._seekRequestTime = 0.0;
    }

    pub fn Clean(&mut self) {
        self.Packet = Packet::empty();
    }
}
