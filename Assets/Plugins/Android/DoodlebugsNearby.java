// DoodlebugsNearby.java
// Native Android local peer-to-peer backend for Doodlebugs Revival, used as the
// mobile-data fallback for local co-op. Built on Google Nearby Connections, which
// links nearby devices over Bluetooth / Wi-Fi Direct / hotspot WITHOUT a shared
// router or internet connection.
//
// Consumed from NativeLocalCoop_Android.cs. Callbacks are delivered back to Unity
// via UnityPlayer.UnitySendMessage on the named receiver GameObject. Binary data
// is base64-encoded as "endpointId|base64".

package com.doodlebugs.nearby;

import android.app.Activity;
import android.util.Base64;

import androidx.annotation.NonNull;

import com.google.android.gms.nearby.Nearby;
import com.google.android.gms.nearby.connection.AdvertisingOptions;
import com.google.android.gms.nearby.connection.ConnectionInfo;
import com.google.android.gms.nearby.connection.ConnectionLifecycleCallback;
import com.google.android.gms.nearby.connection.ConnectionResolution;
import com.google.android.gms.nearby.connection.ConnectionsClient;
import com.google.android.gms.nearby.connection.DiscoveredEndpointInfo;
import com.google.android.gms.nearby.connection.DiscoveryOptions;
import com.google.android.gms.nearby.connection.EndpointDiscoveryCallback;
import com.google.android.gms.nearby.connection.Payload;
import com.google.android.gms.nearby.connection.PayloadCallback;
import com.google.android.gms.nearby.connection.PayloadTransferUpdate;
import com.google.android.gms.nearby.connection.Strategy;

import com.unity3d.player.UnityPlayer;

public class DoodlebugsNearby {

    private static final Strategy STRATEGY = Strategy.P2P_STAR;

    private final ConnectionsClient connectionsClient;
    private final String serviceId;
    private final String displayName;
    private final String callbackObject;

    public DoodlebugsNearby(Activity activity, String serviceId, String displayName, String callbackObject) {
        this.connectionsClient = Nearby.getConnectionsClient(activity);
        this.serviceId = serviceId;
        this.displayName = displayName;
        this.callbackObject = callbackObject;
    }

    // maxPlayers is enforced by the game (NGO ApproveConnection); kept for parity.
    public void startAdvertising(int maxPlayers) {
        AdvertisingOptions options = new AdvertisingOptions.Builder().setStrategy(STRATEGY).build();
        connectionsClient.startAdvertising(displayName, serviceId, lifecycleCallback, options);
    }

    public void startDiscovery() {
        DiscoveryOptions options = new DiscoveryOptions.Builder().setStrategy(STRATEGY).build();
        connectionsClient.startDiscovery(serviceId, discoveryCallback, options);
    }

    public void stopAdvertising() { connectionsClient.stopAdvertising(); }
    public void stopDiscovery() { connectionsClient.stopDiscovery(); }

    public void stopAll() {
        connectionsClient.stopAdvertising();
        connectionsClient.stopDiscovery();
        connectionsClient.stopAllEndpoints();
    }

    public void connect(String endpointId) {
        connectionsClient.requestConnection(displayName, endpointId, lifecycleCallback);
    }

    public void disconnect(String endpointId) {
        connectionsClient.disconnectFromEndpoint(endpointId);
    }

    public void send(String endpointId, byte[] data, boolean reliable) {
        // Nearby BYTES payloads are reliable; the reliable flag is advisory.
        connectionsClient.sendPayload(endpointId, Payload.fromBytes(data));
    }

    private void send(String method, String arg) {
        UnityPlayer.UnitySendMessage(callbackObject, method, arg);
    }

    private final EndpointDiscoveryCallback discoveryCallback = new EndpointDiscoveryCallback() {
        @Override
        public void onEndpointFound(@NonNull String endpointId, @NonNull DiscoveredEndpointInfo info) {
            send("OnPeerFound", endpointId);
        }

        @Override
        public void onEndpointLost(@NonNull String endpointId) {
            // Ignore transient losses; an in-flight connection request stays valid.
        }
    };

    private final ConnectionLifecycleCallback lifecycleCallback = new ConnectionLifecycleCallback() {
        @Override
        public void onConnectionInitiated(@NonNull String endpointId, @NonNull ConnectionInfo info) {
            // Open lobby: auto-accept and start listening for payloads.
            connectionsClient.acceptConnection(endpointId, payloadCallback);
        }

        @Override
        public void onConnectionResult(@NonNull String endpointId, @NonNull ConnectionResolution result) {
            if (result.getStatus().isSuccess()) {
                send("OnPeerConnected", endpointId);
            } else {
                send("OnPeerDisconnected", endpointId);
            }
        }

        @Override
        public void onDisconnected(@NonNull String endpointId) {
            send("OnPeerDisconnected", endpointId);
        }
    };

    private final PayloadCallback payloadCallback = new PayloadCallback() {
        @Override
        public void onPayloadReceived(@NonNull String endpointId, @NonNull Payload payload) {
            if (payload.getType() == Payload.Type.BYTES && payload.asBytes() != null) {
                String b64 = Base64.encodeToString(payload.asBytes(), Base64.NO_WRAP);
                send("OnDataReceived", endpointId + "|" + b64);
            }
        }

        @Override
        public void onPayloadTransferUpdate(@NonNull String endpointId, @NonNull PayloadTransferUpdate update) {
            // No-op for BYTES payloads.
        }
    };
}
