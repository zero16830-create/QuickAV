#![allow(non_snake_case)]

use crate::AVLibPacket::AVLibPacket;
use crate::FixedSizeQueue::FixedSizeQueue;

pub struct AVLibPacketRecycler {
    _readyPackets: FixedSizeQueue<AVLibPacket>,
    _givenPackets: i32,
    _returnedPackets: i32,
    _recycledPackets: i32,
}

impl AVLibPacketRecycler {
    pub fn new(maxCount: usize) -> Self {
        Self {
            _readyPackets: FixedSizeQueue::new(maxCount),
            _givenPackets: 0,
            _returnedPackets: 0,
            _recycledPackets: 0,
        }
    }

    pub fn Recycle(&mut self, mut packet: AVLibPacket) {
        packet.OnRecycle();
        self._readyPackets.Push(packet);
        self._returnedPackets += 1;
    }

    pub fn GetPacket(&mut self) -> AVLibPacket {
        let packet = match self._readyPackets.TryPop() {
            Some(p) => {
                self._recycledPackets += 1;
                p
            }
            None => AVLibPacket::new(),
        };

        self._givenPackets += 1;
        packet
    }
}
