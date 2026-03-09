use std::collections::VecDeque;
use std::sync::{
    atomic::{AtomicU64, Ordering},
    Mutex,
};

pub struct FixedSizeQueue<T> {
    queue: Mutex<VecDeque<T>>,
    capacity: usize,
    dropped_count: AtomicU64,
}

impl<T> FixedSizeQueue<T> {
    pub fn new(capacity: usize) -> Self {
        Self {
            queue: Mutex::new(VecDeque::with_capacity(capacity)),
            capacity,
            dropped_count: AtomicU64::new(0),
        }
    }

    pub fn push(&self, item: T) {
        let mut q = self.queue.lock().unwrap();
        if q.len() >= self.capacity {
            let _ = q.pop_front();
            self.dropped_count.fetch_add(1, Ordering::SeqCst);
        }
        q.push_back(item);
    }

    pub fn pop(&self) -> Option<T> {
        self.queue.lock().unwrap().pop_front()
    }

    pub fn try_pop(&self) -> Option<T> {
        self.queue.lock().unwrap().pop_front()
    }

    pub fn drain(&self) -> Vec<T> {
        self.queue.lock().unwrap().drain(..).collect()
    }

    pub fn flush(&self) {
        self.queue.lock().unwrap().clear();
    }

    pub fn is_empty(&self) -> bool {
        self.queue.lock().unwrap().is_empty()
    }

    pub fn is_full(&self) -> bool {
        self.queue.lock().unwrap().len() >= self.capacity
    }

    pub fn len(&self) -> usize {
        self.queue.lock().unwrap().len()
    }

    pub fn dropped_count(&self) -> u64 {
        self.dropped_count.load(Ordering::SeqCst)
    }
}
