use rustav_native::FixedSizeQueue::FixedSizeQueue;

#[test]
fn push_drops_oldest_item_when_capacity_is_exceeded() {
    let queue = FixedSizeQueue::new(2);
    queue.Push(1);
    queue.Push(2);
    queue.Push(3);

    assert_eq!(queue.Count(), 2);
    assert_eq!(queue.DroppedCount(), 1);
    assert_eq!(queue.Pop(), Some(2));
    assert_eq!(queue.Pop(), Some(3));
    assert!(queue.Empty());
}

#[test]
fn drain_returns_all_items_and_clears_queue() {
    let queue = FixedSizeQueue::new(3);
    queue.Push("a");
    queue.Push("b");
    queue.Push("c");

    let drained = queue.Drain();
    assert_eq!(drained, vec!["a", "b", "c"]);
    assert!(queue.Empty());
    assert_eq!(queue.Count(), 0);
    assert!(!queue.Full());
    assert_eq!(queue.DroppedCount(), 0);
}
