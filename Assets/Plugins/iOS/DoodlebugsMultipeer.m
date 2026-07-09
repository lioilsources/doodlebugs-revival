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

@implementation DoodlebugsMultipeer

- (instancetype)initWithService:(NSString *)serviceType displayName:(NSString *)displayName {
    self = [super init];
    if (self) {
        self.serviceType = serviceType;
        self.knownPeers = [NSMutableDictionary dictionary];
        self.localPeerID = [[MCPeerID alloc] initWithDisplayName:displayName];
        self.session = [[MCSession alloc] initWithPeer:self.localPeerID
                                      securityIdentity:nil
                                  encryptionPreference:MCEncryptionOptional];
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
    self.knownPeers[peerID.displayName] = peerID;
    invitationHandler(YES, self.session); // auto-accept (lobby is open)
}

#pragma mark - Session delegate

- (void)session:(MCSession *)session
           peer:(MCPeerID *)peerID
 didChangeState:(MCSessionState)state {
    const char *name = peerID.displayName.UTF8String;
    if (state == MCSessionStateConnected) {
        self.knownPeers[peerID.displayName] = peerID;
        if (self.onConnected) self.onConnected(name);
    } else if (state == MCSessionStateNotConnected) {
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
}

void _DBMP_SetCallbacks(DBMPPeerCallback found, DBMPPeerCallback connected,
                        DBMPPeerCallback disconnected, DBMPDataCallback data) {
    if (!gInstance) return;
    gInstance.onFound = found;
    gInstance.onConnected = connected;
    gInstance.onDisconnected = disconnected;
    gInstance.onData = data;
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
