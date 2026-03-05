#![allow(non_snake_case)]
use std::sync::Arc;
use crate::Live555Packet::Live555Packet;
use crate::FixedSizeQueue::FixedSizeQueue;

pub struct Live555PacketRecycler {
    _readyPackets: Arc<FixedSizeQueue<Live555Packet>>,
    _bufferSize: usize,
}

impl Live555PacketRecycler {
    pub fn new(maxCount: usize, bufferSize: usize) -> Self {
        Self {
            _readyPackets: Arc::new(FixedSizeQueue::new(maxCount)),
            _bufferSize: bufferSize,
        }
    }

    pub fn Recycle(&mut self, packet: Live555Packet) {
        self._readyPackets.Push(packet);
    }

    pub fn GetPacket(&mut self) -> Live555Packet {
        match self._readyPackets.TryPop() {
            Some(p) => p,
            None => Live555Packet::new(self._bufferSize),
        }
    }
}
