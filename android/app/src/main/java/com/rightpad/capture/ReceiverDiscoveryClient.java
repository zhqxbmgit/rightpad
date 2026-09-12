package com.rightpad.capture;

import android.content.Context;
import android.net.ConnectivityManager;
import android.net.LinkAddress;
import android.net.LinkProperties;
import android.net.Network;
import android.net.NetworkCapabilities;
import android.net.NetworkRequest;
import android.os.Handler;
import android.os.Looper;
import android.os.SystemClock;
import android.util.Log;
import java.io.Closeable;
import java.io.IOException;
import java.net.DatagramPacket;
import java.net.DatagramSocket;
import java.net.InetAddress;
import java.net.InetSocketAddress;
import java.net.SocketTimeoutException;
import java.util.LinkedHashMap;
import java.util.Map;
import java.util.Set;
import java.util.concurrent.ThreadLocalRandom;

final class ReceiverDiscoveryClient implements Closeable {
    interface Listener { void changed(InetSocketAddress target, String receiverId); }
    private static final String TAG = "RightpadDiscovery";
    private static final class Wifi {
        final Network network;
        final Set<String> links;
        final Set<InetAddress> broadcasts;
        Wifi(Network network, LinkProperties properties) {
            this.network = network;
            InetAddress[] addresses = new InetAddress[properties.getLinkAddresses().size()];
            int[] prefixes = new int[addresses.length];
            int i = 0;
            for (LinkAddress link : properties.getLinkAddresses()) {
                addresses[i] = link.getAddress();
                prefixes[i++] = link.getPrefixLength();
            }
            links = DiscoveryBroadcasts.ipv4Links(addresses, prefixes);
            broadcasts = DiscoveryBroadcasts.destinations(addresses, prefixes);
        }
        boolean same(Wifi other) {
            return other != null && network.equals(other.network) && links.equals(other.links);
        }
    }
    private final ConnectivityManager connectivity;
    private final Handler ui = new Handler(Looper.getMainLooper());
    private final Listener listener;
    private final Map<Network, Wifi> networks = new LinkedHashMap<>(); // UI callback ownership.
    private final Map<Network, LinkProperties> linkProperties = new LinkedHashMap<>();
    private final Map<Network, NetworkCapabilities> capabilities = new LinkedHashMap<>();
    private final Thread worker;
    private volatile Wifi wifi;
    private volatile boolean closed, enabled;
    private volatile long generation;
    private volatile DatagramSocket socket;
    private boolean registered;
    private final ConnectivityManager.NetworkCallback callback = new ConnectivityManager.NetworkCallback() {
        @Override public void onAvailable(Network network) {
            Log.i(TAG, "network_available network=" + network);
        }
        @Override public void onLost(Network network) {
            Log.i(TAG, "network_lost network=" + network);
            networks.remove(network);
            linkProperties.remove(network);
            capabilities.remove(network);
            chooseWifi();
        }
        @Override public void onLinkPropertiesChanged(Network network, LinkProperties properties) {
            linkProperties.put(network, properties);
            update(network);
        }
        @Override public void onCapabilitiesChanged(Network network, NetworkCapabilities value) {
            capabilities.put(network, value);
            update(network);
        }
    };
    ReceiverDiscoveryClient(Context context, Listener listener) {
        this.listener = listener;
        connectivity = context.getSystemService(ConnectivityManager.class);
        worker = new Thread(this::run, "RightpadDiscovery");
        worker.start();
        try {
            connectivity.registerNetworkCallback(new NetworkRequest.Builder()
                    .addTransportType(NetworkCapabilities.TRANSPORT_WIFI)
                    .addCapability(NetworkCapabilities.NET_CAPABILITY_NOT_VPN).build(), callback, ui);
            registered = true;
        } catch (SecurityException exception) { Log.e(TAG, "network_permission_denied ACCESS_NETWORK_STATE", exception); }
    }
    private void update(Network network) {
        if (closed) return;
        // Callback payloads are ordered; synchronous framework queries here can race their events.
        NetworkCapabilities caps = capabilities.get(network);
        LinkProperties properties = linkProperties.get(network);
        if (caps != null && properties != null && caps.hasTransport(NetworkCapabilities.TRANSPORT_WIFI)
                && !caps.hasTransport(NetworkCapabilities.TRANSPORT_VPN)
                && caps.hasCapability(NetworkCapabilities.NET_CAPABILITY_NOT_VPN)) {
            Wifi candidate = new Wifi(network, properties);
            if (!candidate.links.isEmpty()) networks.put(network, candidate);
            else networks.remove(network);
        } else networks.remove(network);
        chooseWifi();
    }
    private void chooseWifi() {
        Wifi next = wifi == null ? null : networks.get(wifi.network);
        if (next == null && !networks.isEmpty()) next = networks.values().iterator().next();
        if (next == null ? wifi != null : !next.same(wifi)) wifi = next;
    }
    void setForeground(boolean value) {
        generation++;
        enabled = value;
        if (!value) {
            DatagramSocket current = socket;
            if (current != null) current.close();
        }
        worker.interrupt();
    }
    private void publish(DiscoverySelection selection, long epoch, Wifi selected) {
        InetSocketAddress target = selection.target;
        String id = selection.current == null ? null : selection.current.identity();
        ui.post(() -> {
            if (!closed && enabled && generation == epoch && wifi == selected) listener.changed(target, id);
        });
    }
    private void run() {
        DiscoverySelection selection = new DiscoverySelection();
        DiscoverySchedule schedule = new DiscoverySchedule();
        Wifi selected = null;
        long epoch = -1, nonce = 0, started = 0, invalid = 0, lastError = -10000;
        boolean probeOutstanding = false;
        byte[] buffer = new byte[DiscoveryProtocol.OFFER_SIZE + 1]; // Oversized datagrams cannot masquerade as 40 bytes.
        try {
            while (!closed) {
                if (!enabled) { closeSocket(); nap(); continue; }
                long now = SystemClock.elapsedRealtime();
                Wifi next = wifi;
                long nextEpoch = generation;
                boolean changedNetwork = next != selected;
                boolean resumed = nextEpoch != epoch;
                if (changedNetwork || resumed) {
                    closeSocket();
                    if (changedNetwork) selection.clear();
                    else if (resumed) selection.awaitResumeConfirmation(now);
                    selected = next;
                    epoch = nextEpoch;
                    probeOutstanding = false;
                    schedule.reset(now);
                    started = now;
                    Log.i(TAG, "Searching foregroundReadyMs=" + now + " confirmationRequired=" + (selection.current != null));
                    if (selection.current == null) {
                        publish(selection, epoch, selected);
                    }
                }
                if (selection.expire(now)) {
                    Log.i(TAG, "timeout -> Searching");
                    started = now;
                    schedule.reset(now);
                    publish(selection, epoch, selected);
                }
                if (selected == null) { nap(); continue; }
                try {
                    if (socket == null) {
                        DatagramSocket opened = new DatagramSocket(null);
                        try {
                            selected.network.bindSocket(opened);
                            opened.setBroadcast(true);
                            opened.bind(new InetSocketAddress(0));
                            opened.setSoTimeout(100);
                            socket = opened;
                            Log.i(TAG, "wifi_socket_bound network=" + selected.network + " destinations=" + selected.broadcasts);
                        } catch (IOException | SecurityException exception) { opened.close(); throw exception; }
                    }
                    DatagramSocket current = socket;
                    if (schedule.due(now, selection.current != null)) {
                        nonce = ThreadLocalRandom.current().nextLong();
                        probeOutstanding = true;
                        byte[] probe = DiscoveryProtocol.discover(nonce);
                        // A failure of one destination must not suppress the other destination.
                        IOException failure = null;
                        int sent = 0;
                        for (InetAddress destination : selected.broadcasts) {
                            try {
                                current.send(new DatagramPacket(probe, probe.length, destination, DiscoveryProtocol.PORT));
                                sent++;
                            }
                            catch (IOException exception) { failure = exception; }
                        }
                        if (failure != null) {
                            if (sent == 0) throw failure;
                            if (now - lastError >= 5000) {
                                lastError = now;
                                logSocketError(failure);
                            }
                        }
                    }
                    DatagramPacket packet = new DatagramPacket(buffer, buffer.length);
                    current.receive(packet);
                    if (!enabled || wifi != selected || generation != epoch) continue;
                    DiscoveryProtocol.Offer offer = probeOutstanding && packet.getPort() == DiscoveryProtocol.PORT
                            ? DiscoveryProtocol.offer(buffer, packet.getLength(), nonce) : null;
                    if (offer == null) {
                        invalid++;
                        if (invalid == 1 || invalid % 1024 == 0) Log.w(TAG, "invalid_discovery_packet count=" + invalid);
                        continue;
                    }
                    long receivedAt = SystemClock.elapsedRealtime();
                    if (selection.expire(receivedAt)) {
                        Log.i(TAG, "timeout -> Searching");
                        started = receivedAt;
                        publish(selection, epoch, selected);
                    }
                    InetSocketAddress previous = selection.target;
                    if (selection.accept(offer, packet.getAddress(), receivedAt)) {
                        if (!selection.target.equals(previous) || started != 0) Log.i(TAG, "Connected receiverId=" + offer.identity()
                                + " source=" + packet.getAddress().getHostAddress() + " touchPort=" + offer.touchPort
                                + " timeToDiscoveryMs=" + (receivedAt - started));
                        started = 0;
                        publish(selection, epoch, selected);
                    }
                } catch (SocketTimeoutException expected) {
                    // Recheck Wi-Fi, lifecycle and presence on the independent worker.
                } catch (IOException | SecurityException exception) {
                    closeSocket();
                    if (enabled && !closed && generation == epoch && wifi == selected && now - lastError >= 5000) {
                        lastError = now;
                        logSocketError(exception);
                    }
                    nap();
                }
            }
        } finally { closeSocket(); Log.i(TAG, "discovery_stopped"); }
    }
    private void closeSocket() {
        DatagramSocket current = socket;
        socket = null;
        if (current != null) current.close();
    }
    private void nap() { try { Thread.sleep(100); } catch (InterruptedException ignored) { } }
    private void logSocketError(Exception exception) {
        boolean permission = exception instanceof SecurityException;
        for (Throwable cause = exception; cause != null; cause = cause.getCause()) {
            String message = String.valueOf(cause.getMessage()).toLowerCase(java.util.Locale.ROOT);
            permission |= message.contains("eperm") || message.contains("eacces")
                    || message.contains("permission denied") || message.contains("operation not permitted");
        }
        Log.e(TAG, permission ? "discovery_permission_denied: local-network access blocked; Android 16 opt-in requires Nearby Devices grant"
                : "discovery_socket_error", exception);
    }
    @Override public void close() {
        closed = true;
        enabled = false;
        generation++;
        if (registered) { connectivity.unregisterNetworkCallback(callback); registered = false; }
        DatagramSocket current = socket;
        if (current != null) current.close();
        worker.interrupt();
    }
}
