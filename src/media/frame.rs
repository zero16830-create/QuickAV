pub struct Frame {
    pub _eof: bool,
}

impl Frame {
    pub fn new() -> Self {
        Self { _eof: false }
    }

    pub fn set_eof(&mut self) {
        self._eof = true;
    }

    pub fn is_eof(&self) -> bool {
        self._eof
    }

    pub fn on_recycle(&mut self) {
        self._eof = false;
    }
}
