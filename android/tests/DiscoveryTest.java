package com.rightpad.capture;

import java.net.*;
import java.nio.*;
import java.util.*;
import java.util.concurrent.*;
import java.util.concurrent.atomic.AtomicBoolean;

public final class DiscoveryTest {
    private static int checks;
    private static void check(boolean value, String message) { checks++; if (!value) throw new AssertionError(message); }
    private static byte[] offer(long nonce, int identity) {
        byte[] data = new byte[40];
        ByteBuffer.wrap(data).order(ByteOrder.LITTLE_ENDIAN)
                .put(new byte[] {'R','P','A','D',1,2,0,0}).putLong(nonce)
                .putLong(identity).putLong(0).putShort((short)50000).put((byte)2);
        return data;
    }
    public static void main(String[] args) throws Exception {
        codec(); selection(); broadcasts(); schedule(); display(); transition();
        System.out.println("RESULT discovery checks=" + checks + " PASS");
    }
    private static void codec() {
        long nonce = 0xfedcba9876543210L;
        check(HexFormat.of().formatHex(DiscoveryProtocol.discover(nonce)).equals("52504144010100001032547698badcfe"), "golden DISCOVER LE bytes");
        byte[] good = offer(nonce, 42);
        DiscoveryProtocol.Offer decoded = DiscoveryProtocol.offer(good, good.length, nonce);
        check(decoded != null && decoded.touchPort == 50000, "OFFER port");
        check(decoded.identity().equals("2a000000000000000000000000000000"), "opaque receiverId order");
        for (int length = 0; length < 81; length++) {
            if (length == 40) continue;
            check(DiscoveryProtocol.offer(Arrays.copyOf(good, length), length, nonce) == null, "invalid length " + length);
        }
        for (int offset : new int[] {0,1,2,3,4,5,6,7,32,33,34,35}) {
            byte[] data = good.clone(); data[offset] ^= (byte)128;
            check(DiscoveryProtocol.offer(data, 40, nonce) == null, "invalid field " + offset);
        }
        check(DiscoveryProtocol.offer(good, 40, nonce + 1) == null, "nonce mismatch");
        good[39] = (byte)255;
        check(DiscoveryProtocol.offer(good, 40, nonce) != null, "unknown capabilities ignored");
    }
    private static void selection() throws Exception {
        DiscoverySelection s = new DiscoverySelection();
        DiscoveryProtocol.Offer a = DiscoveryProtocol.offer(offer(1, 1), 40, 1);
        DiscoveryProtocol.Offer b = DiscoveryProtocol.offer(offer(1, 2), 40, 1);
        InetAddress ipA = InetAddress.getByName("192.0.2.10"), ipB = InetAddress.getByName("198.51.100.20");
        check(s.accept(a, ipA, 0), "first valid OFFER wins");
        check(s.target.getAddress().equals(ipA), "datagram source address is authoritative");
        check(!s.accept(b, ipB, 1000), "other identity cannot preempt");
        check(!s.expire(2499), "not stale early");
        check(s.accept(a, ipA, 2499), "current OFFER refresh");
        check(!s.expire(4998), "refresh extends deadline");
        check(s.expire(4999) && s.target == null, "exact stale boundary clears target");
        check(s.accept(b, ipB, 5000), "B wins after stale");
        check(s.target.getAddress().equals(ipB), "source changes for second location");
        check(!Arrays.equals(a.receiverId, b.receiverId), "two distinct receiver identities");
        s.clear();
        check(!s.accept(a, InetAddress.getLoopbackAddress(), 0), "loopback rejected");
        check(!s.accept(a, InetAddress.getByName("::1"), 0), "IPv6 rejected");
        check(s.accept(a, ipA, 0), "network available -> A");
        s.clear(); check(s.target == null, "network lost clears selection");
        check(s.accept(b, ipB, 1), "next Wi-Fi permits B without saved host");
        s.awaitResumeConfirmation(100000);
        check(!s.expire(102499), "long pause gives bounded fresh confirmation window");
        check(s.accept(b, ipB, 102499), "same target confirmed after pause retains selection");
        check(s.expire(104999), "no offer after resumed window still expires");
    }
    private static void broadcasts() throws Exception {
        InetAddress ip = InetAddress.getByName("192.168.19.8");
        check(DiscoveryBroadcasts.directed(ip, 24).getHostAddress().equals("192.168.19.255"), "/24");
        check(DiscoveryBroadcasts.directed(ip, 16).getHostAddress().equals("192.168.255.255"), "/16");
        check(DiscoveryBroadcasts.directed(ip, 23).getHostAddress().equals("192.168.19.255"), "/23");
        check(DiscoveryBroadcasts.directed(ip, 30).getHostAddress().equals("192.168.19.11"), "/30");
        check(DiscoveryBroadcasts.directed(ip, 1).getHostAddress().equals("255.255.255.255"), "/1");
        for (int prefix : new int[] {-1, 0, 31, 32, 33}) check(DiscoveryBroadcasts.directed(ip, prefix) == null, "skip prefix " + prefix);
        check(DiscoveryBroadcasts.directed(InetAddress.getByName("2001:db8::1"), 24) == null, "non IPv4");
        Set<InetAddress> destinations = DiscoveryBroadcasts.destinations(new InetAddress[] {ip, ip, ip}, new int[] {24,24,1});
        check(destinations.size() == 2 && destinations.contains(InetAddress.getByName("255.255.255.255")), "dedup with limited broadcast");
        Set<String> links = DiscoveryBroadcasts.ipv4Links(new InetAddress[] {ip}, new int[] {24});
        check(links.equals(DiscoveryBroadcasts.ipv4Links(new InetAddress[] {InetAddress.getByName("2001:db8::1"), ip, ip},
                new int[] {64,24,24})), "IPv6 update / ordering / duplicates preserve route");
        check(!links.equals(DiscoveryBroadcasts.ipv4Links(new InetAddress[] {ip}, new int[] {16})), "IPv4 prefix change invalidates route");
        check(DiscoveryBroadcasts.ipv4Links(new InetAddress[] {InetAddress.getByName("2001:db8::1")}, new int[] {64}).isEmpty(), "IPv6-only network has no usable IPv4 route");
    }
    private static void schedule() {
        DiscoverySchedule s = new DiscoverySchedule(); s.reset(0);
        for (long now : new long[] {0,250,500,1000,2000}) {
            check(!s.due(now - 1, false), "not early " + now);
            check(s.due(now, false), "probe " + now);
        }
        s.reset(123); check(s.due(123, true), "immediate after transition");
        check(!s.due(1122, true) && s.due(1123, true), "connected cadence 1 second");
        check(s.due(10000, true) && !s.due(10000, true), "no catch-up burst");
    }
    private static void display() {
        check(ConnectionDisplay.title(null).equals("搜索中"), "searching title");
        check(ConnectionDisplay.address(null).equals("—"), "searching address");
        check(ConnectionDisplay.title("192.0.2.10").equals("已连接"), "connected title");
        check(ConnectionDisplay.address("192.0.2.10").equals("192.0.2.10"), "source address displayed");
    }
    private static byte[] receive(DatagramSocket socket) throws Exception { return UdpTouchSenderTest.receive(socket); }
    private static void noPacket(DatagramSocket socket) throws Exception {
        socket.setSoTimeout(150);
        try { receive(socket); throw new AssertionError("unexpected packet"); }
        catch (SocketTimeoutException expected) { checks++; }
        finally { socket.setSoTimeout(2000); }
    }
    private static void transition() throws Exception {
        try (DatagramSocket a = new DatagramSocket(0, InetAddress.getByName("127.0.0.1"));
             DatagramSocket b = new DatagramSocket(0, InetAddress.getByName("127.0.0.2"))) {
            a.setSoTimeout(2000); b.setSoTimeout(2000);
            CountDownLatch dequeued = new CountDownLatch(1), release = new CountDownLatch(1);
            AtomicBoolean block = new AtomicBoolean(false);
            Runnable barrier = () -> {
                if (!block.getAndSet(false)) return;
                dequeued.countDown();
                boolean done = false;
                while (!done) try { done = release.await(3, TimeUnit.SECONDS); if (!done) throw new AssertionError("barrier timeout"); }
                catch (InterruptedException ignored) { }
            };
            try (UdpTouchSender sender = new UdpTouchSender((InetSocketAddress)null, barrier)) {
                sender.setForeground(true);
                sender.submit(UdpTouchSenderTest.touch(TouchSample.Action.DOWN));
                check(sender.queuedForTest() == 0 && !sender.hasTarget(), "no target no queue");
                noPacket(a); noPacket(b);
                long none = sender.runIdForTest();
                sender.setTarget(new InetSocketAddress(a.getLocalAddress(), a.getLocalPort()));
                byte[] hbA = receive(a);
                check(hbA.length == 10 && hbA[1] == 4, "A first heartbeat");
                long runA = UdpTouchSenderTest.run(hbA);
                check(runA != none, "none -> A rotates identity");
                sender.submit(UdpTouchSenderTest.touch(TouchSample.Action.MOVE)); noPacket(a);
                sender.submit(UdpTouchSenderTest.touch(TouchSample.Action.DOWN));
                byte[] down = receive(a);
                check(down[1] == 1 && UdpTouchSenderTest.sequence(down) == 0, "A first DOWN sequence zero");
                receive(a); receive(a);
                block.set(true);
                sender.submit(UdpTouchSenderTest.touch(TouchSample.Action.MOVE));
                check(dequeued.await(2, TimeUnit.SECONDS), "old MOVE dequeued before transition");
                sender.submit(UdpTouchSenderTest.touch(TouchSample.Action.MOVE));
                check(sender.queuedForTest() == 1, "old MOVE also pending");
                sender.setTarget(new InetSocketAddress(b.getLocalAddress(), b.getLocalPort()));
                check(sender.queuedForTest() == 0, "A -> B queue clear");
                release.countDown();
                byte[] hbB = receive(b);
                check(hbB.length == 10 && hbB[1] == 4, "B first heartbeat / schedule reset");
                long runB = UdpTouchSenderTest.run(hbB);
                check(runB != runA, "A -> B runId rotates");
                sender.submit(UdpTouchSenderTest.touch(TouchSample.Action.MOVE));
                sender.submit(UdpTouchSenderTest.touch(TouchSample.Action.UP));
                noPacket(b); noPacket(a);
                sender.submit(UdpTouchSenderTest.touch(TouchSample.Action.DOWN));
                down = receive(b);
                check(down[1] == 1 && UdpTouchSenderTest.sequence(down) == 0 && UdpTouchSenderTest.run(down) == runB,
                        "B starts only with fresh DOWN sequence zero");
                receive(b); receive(b);
                sender.setTarget(null);
                check(sender.runIdForTest() != runB && sender.queuedForTest() == 0, "B -> none rotates / clears");
                sender.submit(UdpTouchSenderTest.touch(TouchSample.Action.DOWN));
                noPacket(a); noPacket(b);
                check(sender.queuedForTest() == 0, "none never accumulates stale input");
            } finally { release.countDown(); }
        }
    }
}
