// DoodlebugsMultipeer.m
// Native iOS local peer-to-peer backend for Doodlebugs Revival, used as the
// mobile-data fallback for local co-op. Built on MultipeerConnectivity, which
// links nearby devices over Bluetooth / peer-to-peer Wi-Fi WITHOUT a shared
// router or internet connection.
//
// Exposes a small C ABI consumed by NativeLocalCoop_iOS.cs.

#import <Foundation/Foundation.h>
#import <MultipeerConnectivity/MultipeerConnectivity.h>

typedef void (*DBMPPeerCallback)(const char *peerId);
typedef void (*DBMPDataCallback)(const char *peerId, const unsigned char *data, int length);

@interface DoodlebugsMultipeer : NSObject <MCSessionDelegate,
                                           MCNearbyServiceAdvertiserDelegate,
                                           MCNearbyServiceBrowserDelegate>
@property (nonatomic, strong) MCPeerID *localPeerID;
@property (nonatomic, strong) MCSession *session;
@property (nonatomic, strong) MCNearbyServiceAdvertiser *advertiser;
@property (nonatomic, strong) MCNearbyServiceBrowser *browser;
@property (nonatomic, strong) NSMutableDictionary<NSString *, MCPeerID *> *knownPeers;
@property (nonatomic, copy) NSString *serviceType;

@property (nonatomic, assign) DBMPPeerCallback onFound;
@property (nonatomic, assign) DBMPPeerCallback onConnected;
@property (nonatomic, assign) DBMPPeerCallback onDisconnected;
@property (nonatomic, assign) DBMPDataCallback onData;
@end

static DoodlebugsMultipeer *gInstance = nil;

// Callback registration must survive any C#-side call ordering: store the
// pointers in statics and apply them to the instance on both SetCallbacks
// and Initialize (a guard on gInstance alone used to silently drop them).
static DBMPPeerCallback gFoundCb = NULL;
static DBMPPeerCallback gConnectedCb = NULL;
static DBMPPeerCallback gDisconnectedCb = NULL;
static DBMPDataCallback gDataCb = NULL;

static void DBMPApplyCallbacks(void) {
    if (!gInstance) return;
    gInstance.onFound = gFoundCb;
    gInstance.onConnected = gConnectedCb;
    gInstance.onDisconnected = gDisconnectedCb;
    gInstance.onData = gDataCb;
}

@implementation DoodlebugsMultipeer

- (instancetype)initWithService:(NSString *)serviceType displayName:(NSString *)displayName {
    self = [super init];
    if (self) {
        self.serviceType = serviceType;
        self.knownPeers = [NSMutableDictionary dictionary];
        self.localPeerID = [[MCPeerID alloc] initWithDisplayName:displayName];
        // EncryptionRequired: with MCEncryptionOptional the TLS negotiation
        // between iOS peers frequently fails (Connecting -> NotConnected) and
        // the session never establishes.
        self.session = [[MCSession alloc] initWithPeer:self.localPeerID
                                      securityIdentity:nil
                                  encryptionPreference:MCEncryptionRequired];
        self.session.delegate = self;
    }
    return self;
}

- (void)startAdvertising {
    self.advertiser = [[MCNearbyServiceAdvertiser alloc] initWithPeer:self.localPeerID
                                                        discoveryInfo:nil
                                                          serviceType:self.serviceType];
    self.advertiser.delegate = self;
    [self.advertiser startAdvertisingPeer];
}

- (void)startBrowsing {
    self.browser = [[MCNearbyServiceBrowser alloc] initWithPeer:self.localPeerID
                                                    serviceType:self.serviceType];
    self.browser.delegate = self;
    [self.browser startBrowsingForPeers];
}

- (void)stopAdvertising { [self.advertiser stopAdvertisingPeer]; self.advertiser = nil; }
- (void)stopBrowsing { [self.browser stopBrowsingForPeers]; self.browser = nil; }

- (void)stopAll {
    [self stopAdvertising];
    [self stopBrowsing];
    [self.session disconnect];
    [self.knownPeers removeAllObjects];
}

- (void)connectToPeer:(NSString *)peerId {
    MCPeerID *peer = self.knownPeers[peerId];
    if (peer && self.browser) {
        NSLog(@"[DBMP] inviting peer=%@", peerId);
        [self.browser invitePeer:peer toSession:self.session withContext:nil timeout:30];
    } else {
        // The invite goes through the live browser; if it was stopped/nil'd first
        // the host never receives it and the session never connects.
        NSLog(@"[DBMP] connectToPeer '%@' skipped: peer=%@ browser=%@", peerId, peer, self.browser);
    }
}

- (void)sendData:(NSData *)data toPeer:(NSString *)peerId reliable:(BOOL)reliable {
    MCPeerID *peer = self.knownPeers[peerId];
    if (!peer) return;
    MCSessionSendDataMode mode = reliable ? MCSessionSendDataReliable : MCSessionSendDataUnreliable;
    NSError *err = nil;
    [self.session sendData:data toPeers:@[peer] withMode:mode error:&err];
    if (err) { NSLog(@"[DBMP] send error: %@", err); }
}

- (void)disconnectPeer:(NSString *)peerId {
    // MCSession has no per-peer disconnect; drop the whole session if the host leaves.
    [self.knownPeers removeObjectForKey:peerId];
}

#pragma mark - Browser delegate

- (void)browser:(MCNearbyServiceBrowser *)browser
      foundPeer:(MCPeerID *)peerID
withDiscoveryInfo:(NSDictionary<NSString *,NSString *> *)info {
    self.knownPeers[peerID.displayName] = peerID;
    if (self.onFound) self.onFound(peerID.displayName.UTF8String);
}

- (void)browser:(MCNearbyServiceBrowser *)browser lostPeer:(MCPeerID *)peerID {
    // Keep the mapping; a transient loss should not invalidate an in-flight invite.
}

#pragma mark - Advertiser delegate

- (void)advertiser:(MCNearbyServiceAdvertiser *)advertiser
didReceiveInvitationFromPeer:(MCPeerID *)peerID
       withContext:(NSData *)context
 invitationHandler:(void (^)(BOOL, MCSession *))invitationHandler {
    NSLog(@"[DBMP] invitation from peer=%@ - auto-accepting", peerID.displayName);
    self.knownPeers[peerID.displayName] = peerID;
    invitationHandler(YES, self.session); // auto-accept (lobby is open)
}

#pragma mark - Session delegate

// Both endpoints are our own app; accept the peer's TLS certificate so the
// EncryptionRequired handshake can complete without stalling.
- (void)session:(MCSession *)session
didReceiveCertificate:(NSArray *)certificate
       fromPeer:(MCPeerID *)peerID
certificateHandler:(void (^)(BOOL accept))certificateHandler {
    certificateHandler(YES);
}

- (void)session:(MCSession *)session
           peer:(MCPeerID *)peerID
 didChangeState:(MCSessionState)state {
    const char *name = peerID.displayName.UTF8String;
    if (state == MCSessionStateConnecting) {
        NSLog(@"[DBMP] state Connecting peer=%@", peerID.displayName);
    } else if (state == MCSessionStateConnected) {
        NSLog(@"[DBMP] state Connected peer=%@", peerID.displayName);
        self.knownPeers[peerID.displayName] = peerID;
        if (self.onConnected) self.onConnected(name);
    } else if (state == MCSessionStateNotConnected) {
        NSLog(@"[DBMP] state NotConnected peer=%@", peerID.displayName);
        if (self.onDisconnected) self.onDisconnected(name);
    }
}

- (void)session:(MCSession *)session
 didReceiveData:(NSData *)data
       fromPeer:(MCPeerID *)peerID {
    if (self.onData) {
        self.onData(peerID.displayName.UTF8String, (const unsigned char *)data.bytes, (int)data.length);
    }
}

- (void)session:(MCSession *)session didReceiveStream:(NSInputStream *)stream withName:(NSString *)streamName fromPeer:(MCPeerID *)peerID {}
- (void)session:(MCSession *)session didStartReceivingResourceWithName:(NSString *)resourceName fromPeer:(MCPeerID *)peerID withProgress:(NSProgress *)progress {}
- (void)session:(MCSession *)session didFinishReceivingResourceWithName:(NSString *)resourceName fromPeer:(MCPeerID *)peerID atURL:(NSURL *)localURL withError:(NSError *)error {}

@end

#pragma mark - C ABI

static NSString *CStr(const char *s) { return s ? [NSString stringWithUTF8String:s] : @""; }

void _DBMP_Initialize(const char *serviceType, const char *displayName) {
    gInstance = [[DoodlebugsMultipeer alloc] initWithService:CStr(serviceType) displayName:CStr(displayName)];
    DBMPApplyCallbacks();
}

void _DBMP_SetCallbacks(DBMPPeerCallback found, DBMPPeerCallback connected,
                        DBMPPeerCallback disconnected, DBMPDataCallback data) {
    gFoundCb = found;
    gConnectedCb = connected;
    gDisconnectedCb = disconnected;
    gDataCb = data;
    DBMPApplyCallbacks();
}

void _DBMP_StartAdvertising(void) { [gInstance startAdvertising]; }
void _DBMP_StartBrowsing(void) { [gInstance startBrowsing]; }
void _DBMP_StopAdvertising(void) { [gInstance stopAdvertising]; }
void _DBMP_StopBrowsing(void) { [gInstance stopBrowsing]; }
void _DBMP_StopAll(void) { [gInstance stopAll]; gInstance = nil; }
void _DBMP_Connect(const char *peerId) { [gInstance connectToPeer:CStr(peerId)]; }
void _DBMP_Disconnect(const char *peerId) { [gInstance disconnectPeer:CStr(peerId)]; }

void _DBMP_Send(const char *peerId, const unsigned char *data, int length, int reliable) {
    NSData *payload = [NSData dataWithBytes:data length:length];
    [gInstance sendData:payload toPeer:CStr(peerId) reliable:(reliable != 0)];
}
