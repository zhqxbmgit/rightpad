package com.rightpad.capture;

import java.net.Inet4Address;
import java.net.InetAddress;
import java.net.UnknownHostException;
import java.util.LinkedHashSet;
import java.util.Set;

final class DiscoveryBroadcasts {
    static Set<String> ipv4Links(InetAddress[] addresses, int[] prefixes) {
        Set<String> result = new LinkedHashSet<>();
        for (int i = 0; i < addresses.length; i++) {
            if (addresses[i] instanceof Inet4Address)
                result.add(addresses[i].getHostAddress() + "/" + prefixes[i]);
        }
        return result; // Ignore ordering, IPv6 and Android LinkAddress lifetime/flag metadata.
    }
    static InetAddress directed(InetAddress address, int prefix) {
        // /0 is not a useful attached subnet; /31 and /32 have no broadcast host.
        if (!(address instanceof Inet4Address) || prefix < 1 || prefix > 30
                || address.isAnyLocalAddress() || address.isLoopbackAddress()) return null;
        byte[] bytes = address.getAddress();
        int ip = ipv4Bits(bytes);
        int mask = -1 << (32 - prefix);
        int result = ip | ~mask;
        try { return InetAddress.getByAddress(new byte[] {(byte)(result >>> 24),
                (byte)(result >>> 16), (byte)(result >>> 8), (byte)result}); }
        catch (UnknownHostException impossible) { throw new AssertionError(impossible); }
    }
    static Set<InetAddress> destinations(InetAddress[] addresses, int[] prefixes) {
        Set<InetAddress> result = new LinkedHashSet<>();
        try { result.add(InetAddress.getByAddress(new byte[] {-1, -1, -1, -1})); }
        catch (UnknownHostException impossible) { throw new AssertionError(impossible); }
        for (int i = 0; i < addresses.length; i++) {
            InetAddress broadcast = directed(addresses[i], prefixes[i]);
            if (broadcast != null) result.add(broadcast);
        }
        return result;
    }
    private static int ipv4Bits(byte[] b) {
        return (b[0] & 255) << 24 | (b[1] & 255) << 16 | (b[2] & 255) << 8 | (b[3] & 255);
    }
}
