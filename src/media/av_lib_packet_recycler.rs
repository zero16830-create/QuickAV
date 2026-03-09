use crate::av_lib_packet::AvLibPacket;
use crate::fixed_size_queue::FixedSizeQueue;

pub struct AvLibPacketRecycler {
    ready_packets: FixedSizeQueue<AvLibPacket>,
    given_packets: i32,
    returned_packets: i32,
    recycled_packets: i32,
}

impl AvLibPacketRecycler {
    pub fn new(max_count: usize) -> Self {
        Self {
            ready_packets: FixedSizeQueue::new(max_count),
            given_packets: 0,
            returned_packets: 0,
            recycled_packets: 0,
        }
    }

    pub fn recycle(&mut self, mut packet: AvLibPacket) {
        packet.on_recycle();
        self.ready_packets.push(packet);
        self.returned_packets += 1;
    }

    pub fn get_packet(&mut self) -> AvLibPacket {
        let packet = match self.ready_packets.try_pop() {
            Some(p) => {
                self.recycled_packets += 1;
                p
            }
            None => AvLibPacket::new(),
        };

        self.given_packets += 1;
        packet
    }
}
