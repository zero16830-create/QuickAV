#![allow(non_snake_case)]
pub struct Frame {
    pub _eof: bool,
}

impl Frame {
    pub fn new() -> Self {
        Self { _eof: false }
    }

    pub fn SetAsEOF(&mut self) {
        self._eof = true;
    }

    pub fn IsEOF(&self) -> bool {
        self._eof
    }

    pub fn OnRecycle(&mut self) {
        self._eof = false;
    }
}
