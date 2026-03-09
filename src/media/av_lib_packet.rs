use ffmpeg_next::Packet;

pub struct AvLibPacket {
    pub packet: Packet,
    pub is_eof: bool,
    pub is_seek_request: bool,
    pub seek_request_time: f64,
}

impl AvLibPacket {
    pub fn new() -> Self {
        Self {
            packet: Packet::empty(),
            is_eof: false,
            is_seek_request: false,
            seek_request_time: 0.0,
        }
    }

    pub fn is_eof(&self) -> bool {
        self.is_eof
    }

    pub fn is_seek_request(&self) -> bool {
        self.is_seek_request
    }

    pub fn set_eof(&mut self) {
        self.is_eof = true;
    }

    pub fn set_seek_request(&mut self, time: f64) {
        self.is_seek_request = true;
        self.seek_request_time = time;
    }

    pub fn seek_time(&self) -> f64 {
        self.seek_request_time
    }

    pub fn on_recycle(&mut self) {
        self.clean();
        self.is_eof = false;
        self.is_seek_request = false;
        self.seek_request_time = 0.0;
    }

    pub fn clean(&mut self) {
        self.packet = Packet::empty();
    }
}
