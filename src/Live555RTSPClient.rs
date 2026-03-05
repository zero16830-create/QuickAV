#![allow(non_snake_case)]
use std::sync::{Arc, atomic::{AtomicBool, Ordering}};
use crate::Live555Packet::Live555Packet;

pub struct Live555RTSPClient {
    _uri: String,
    _isConnected: AtomicBool,
}

impl Live555RTSPClient {
    pub fn new(uri: String) -> Self {
        Self {
            _uri: uri,
            _isConnected: AtomicBool::new(false),
        }
    }

    pub fn Connect(&self) {
        self._isConnected.store(true, Ordering::SeqCst);
    }

    pub fn IsConnected(&self) -> bool {
        self._isConnected.load(Ordering::SeqCst)
    }

    pub fn TryGetNext(&self, streamIndex: i32) -> Option<Live555Packet> {
        None
    }

    pub fn Recycle(&self, packet: Live555Packet, streamIndex: i32) {
    }

    pub fn ConnectionDropped(&self) -> bool {
        false
    }
}
