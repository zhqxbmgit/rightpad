package android.os;

import java.util.concurrent.ConcurrentLinkedQueue;

public final class Handler {
    private record Work(Handler owner, Runnable runnable) { }
    private static final ConcurrentLinkedQueue<Work> queue = new ConcurrentLinkedQueue<>();
    public Handler(Looper looper) { }
    public boolean post(Runnable runnable) { queue.add(new Work(this, runnable)); return true; }
    public void removeCallbacksAndMessages(Object token) { queue.removeIf(work -> work.owner == this); }
    public static boolean hasPending() { return !queue.isEmpty(); }
    public static void drain() { Work work; while ((work = queue.poll()) != null) work.runnable.run(); }
}
