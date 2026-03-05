#![allow(non_snake_case)]
pub struct Live555Packet {
    pub _data: Vec<u8>,
    pub _dataSize: usize,
}

impl Live555Packet {
    pub fn new(bufferSize: usize) -> Self {
        Self {
            _data: vec![0; bufferSize],
            _dataSize: 0,
        }
    }
    pub fn Data(&self) -> &[u8] {
        &self._data[0..self._dataSize]
    }
    pub fn DataMut(&mut self) -> &mut [u8] {
        &mut self._data
    }
    pub fn DataSize(&self) -> usize {
        self._dataSize
    }
    pub fn SetDataSize(&mut self, size: usize) {
        self._dataSize = size;
    }
}
