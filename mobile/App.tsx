import React, { useState, useEffect, useRef } from 'react';
import {
  StyleSheet,
  Text,
  View,
  SafeAreaView,
  FlatList,
  TouchableOpacity,
  ActivityIndicator,
  Alert,
  StatusBar,
  Platform,
} from 'react-native';
import * as Notifications from 'expo-notifications';
import { StorageService, MentionRecord, PairingState } from './src/services/StorageService';
import { PushService } from './src/services/PushService';
import { MentionCard } from './src/components/MentionCard';
import { PairingModal } from './src/components/PairingModal';
import { DeveloperUnlockModal } from './src/components/DeveloperUnlockModal';

export default function App() {
  const [mentions, setMentions] = useState<MentionRecord[]>([]);
  const [pairing, setPairing] = useState<PairingState>({ isPaired: false, isPro: false, plan: 'free' });
  const [pushToken, setPushToken] = useState<string | null>(null);
  const [isPairModalOpen, setIsPairModalOpen] = useState(false);
  const [isDevModalOpen, setIsDevModalOpen] = useState(false);
  const [pairCode, setPairCode] = useState<string | null>(null);
  const [loadingPairCode, setLoadingPairCode] = useState(false);
  const [versionTapCount, setVersionTapCount] = useState(0);

  const processedMentionKeys = useRef(new Set<string>());
  const responseListener = useRef<any>(null);

  // 1. Initial Load
  useEffect(() => {
    loadInitialData();

    // Register push notifications
    PushService.registerForPushNotificationsAsync().then((token) => {
      setPushToken(token);
    });

    // Handle notification tap to open app
    responseListener.current = Notifications.addNotificationResponseReceivedListener((response) => {
      const data = response.notification.request.content.data as any;
      if (data && data.matchedName && data.sentence) {
        loadInitialData();
      }
    });

    // Live Wi-Fi mention listener (syncs mentions in real-time without loop)
    let lastPollTime = Date.now();
    const pollInterval = setInterval(async () => {
      try {
        const relayUrl = await StorageService.getRelayUrl();
        const res = await fetch(`${relayUrl.replace(/\/+$/, '')}/api/mentions/poll?since=${lastPollTime}`);
        if (res.ok) {
          const data = await res.json();
          if (data.mentions && data.mentions.length > 0) {
            for (const m of data.mentions) {
              lastPollTime = Math.max(lastPollTime, m.receivedAt);
              if (m.type === 'unlinked') {
                await StorageService.clearPairingState();
                setPairing({ isPaired: false, isPro: false, plan: 'free' });
              } else if (m.type === 'clear') {
                await StorageService.clearMentions();
                setMentions([]);
              } else if (m.matchedName && m.sentence) {
                const key = `${m.matchedName}_${m.timestamp}_${m.sentence}`;
                if (!processedMentionKeys.current.has(key)) {
                  processedMentionKeys.current.add(key);
                  await handleIncomingMention({
                    matchedName: m.matchedName,
                    sentence: m.sentence,
                    timestamp: m.timestamp,
                    audioSource: m.audioSource,
                  });
                }
              }
            }
          }
        }
      } catch {}
    }, 1000);

    return () => {
      clearInterval(pollInterval);
      if (responseListener.current && typeof responseListener.current.remove === 'function') {
        responseListener.current.remove();
      }
    };
  }, []);

  const loadInitialData = async () => {
    const storedMentions = await StorageService.getMentions();
    const storedPairing = await StorageService.getPairingState();
    const isDev = await StorageService.isDevUnlocked();

    setMentions(storedMentions);
    setPairing({
      ...storedPairing,
      isPro: isDev || storedPairing.isPro,
      isDevMaster: isDev || storedPairing.isDevMaster,
    });
  };

  const handleIncomingMention = async (mention: Omit<MentionRecord, 'id' | 'receivedAt'>) => {
    const updated = await StorageService.addMention(mention);
    setMentions(updated);

    try {
      await Notifications.scheduleNotificationAsync({
        content: {
          title: `🎯 Mentioned: ${mention.matchedName}`,
          body: mention.sentence,
          data: mention,
          sound: 'default',
        },
        trigger: null,
      });
    } catch {}
  };

  // 2. Pair Modal Flow
  const openPairModal = async () => {
    setIsPairModalOpen(true);
    await refreshPairCode();
  };

  const refreshPairCode = async () => {
    setLoadingPairCode(true);
    let token = pushToken;
    if (!token) {
      token = await PushService.registerForPushNotificationsAsync();
      setPushToken(token);
    }

    if (token) {
      const res = await PushService.generatePairCode(token);
      if (res) {
        setPairCode(res.pairCode);
      } else {
        setPairCode('MM-ERR');
      }
    } else {
      setPairCode('MM-OFFLINE');
    }
    setLoadingPairCode(false);
  };

  // Auto-detect when PC enters code and claims pairing
  useEffect(() => {
    if (!isPairModalOpen || !pairCode || pairCode.startsWith('MM-ERR') || pairCode.startsWith('MM-OFF')) {
      return;
    }

    const checkInterval = setInterval(async () => {
      const claimedData = await PushService.checkPairStatus(pairCode);
      if (claimedData) {
        clearInterval(checkInterval);
        const newState: PairingState = {
          isPaired: true,
          syncToken: claimedData.syncToken,
          deviceName: 'Windows PC',
          isPro: claimedData.isPro,
          plan: claimedData.plan || 'lifetime_developer',
          isDevMaster: claimedData.isDevMaster || true,
        };
        await StorageService.savePairingState(newState);
        setPairing(newState);
        setIsPairModalOpen(false);
        Alert.alert('🎉 Connected!', 'Your phone is now linked with MentionedMe on Windows.');
      }
    }, 1200);

    return () => clearInterval(checkInterval);
  }, [isPairModalOpen, pairCode]);

  // 3. Clear Mentions
  const handleClearMentions = () => {
    Alert.alert('Clear Mentions', 'Are you sure you want to clear your mentions history?', [
      { text: 'Cancel', style: 'cancel' },
      {
        text: 'Clear All',
        style: 'destructive',
        onPress: async () => {
          await StorageService.clearMentions();
          setMentions([]);
          try {
            const relayUrl = await StorageService.getRelayUrl();
            await fetch(`${relayUrl.replace(/\/+$/, '')}/api/mentions/clear`, { method: 'POST' });
          } catch {}
        },
      },
    ]);
  };

  // 4. Test Notification Simulation
  const handleTestNotification = async () => {
    await handleIncomingMention({
      matchedName: 'Srinath',
      sentence: 'Hey Srinath, can you review the new mobile deployment when you get a chance?',
      timestamp: new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit' }),
      audioSource: 'Mobile Test Simulation',
    });
  };

  // 5. 7-Tap Developer Unlock Secret
  const handleVersionTap = () => {
    const next = versionTapCount + 1;
    setVersionTapCount(next);

    if (next >= 7) {
      setVersionTapCount(0);
      setIsDevModalOpen(true);
    }
  };

  return (
    <SafeAreaView style={styles.container}>
      <StatusBar barStyle="light-content" backgroundColor="#0D1117" />

      {/* Top Header Bar */}
      <View style={styles.header}>
        <View>
          <Text style={styles.appTitle}>MentionedMe</Text>
          <View style={styles.statusRow}>
            <View style={[styles.statusDot, pairing.isPaired ? styles.statusDotGreen : styles.statusDotGray]} />
            <Text style={styles.statusText}>
              {pairing.isPaired ? `Linked to ${pairing.deviceName || 'Windows PC'}` : 'Not Linked to PC'}
            </Text>
          </View>
        </View>

        <View style={styles.headerActions}>
          {/* Pro / Dev Badge */}
          {pairing.isPro && (
            <View style={styles.proBadge}>
              <Text style={styles.proBadgeText}>{pairing.isDevMaster ? '👑 DEV' : '⭐ PRO'}</Text>
            </View>
          )}

          {/* Single Clear Action Button */}
          {pairing.isPaired ? (
            <TouchableOpacity style={styles.iconBtn} onPress={openPairModal}>
              <Text style={styles.iconBtnText}>⚙️ Linked</Text>
            </TouchableOpacity>
          ) : (
            <TouchableOpacity style={styles.upgradeBadge} onPress={openPairModal}>
              <Text style={styles.upgradeBadgeText}>🔗 Link to PC</Text>
            </TouchableOpacity>
          )}
        </View>
      </View>

      {/* Mentions Timeline Feed */}
      <View style={styles.feedContainer}>
        <View style={styles.feedHeaderRow}>
          <Text style={styles.feedTitle}>Recent Mentions ({mentions.length})</Text>
          {mentions.length > 0 && (
            <TouchableOpacity onPress={handleClearMentions}>
              <Text style={styles.clearText}>Clear</Text>
            </TouchableOpacity>
          )}
        </View>

        {mentions.length === 0 ? (
          <View style={styles.emptyState}>
            <Text style={styles.emptyIcon}>🎯</Text>
            <Text style={styles.emptyTitle}>Listening for Mentions</Text>
            <Text style={styles.emptySubtitle}>
              When your name is spoken on your Windows PC, push alerts and spoken sentences will appear here in real-time.
            </Text>

            <TouchableOpacity style={styles.testBtn} onPress={handleTestNotification}>
              <Text style={styles.testBtnText}>🔔 Send Test Mention Alert</Text>
            </TouchableOpacity>
          </View>
        ) : (
          <FlatList
            data={mentions}
            keyExtractor={(item) => item.id}
            renderItem={({ item }) => <MentionCard mention={item} />}
            contentContainerStyle={styles.listContent}
            showsVerticalScrollIndicator={false}
          />
        )}
      </View>

      {/* Footer / Secret Dev Tap */}
      <TouchableOpacity style={styles.footer} activeOpacity={1} onPress={handleVersionTap}>
        <Text style={styles.footerText}>MentionedMe Mobile v1.0.0 • Private & Local Audio</Text>
      </TouchableOpacity>

      {/* Modals */}
      <PairingModal
        visible={isPairModalOpen}
        pairCode={pairCode}
        loading={loadingPairCode}
        onClose={() => setIsPairModalOpen(false)}
        onRefresh={refreshPairCode}
      />

      <DeveloperUnlockModal
        visible={isDevModalOpen}
        onClose={() => setIsDevModalOpen(false)}
        onUnlocked={loadInitialData}
      />
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
    backgroundColor: '#0D1117',
    paddingTop: Platform.OS === 'android' ? 25 : 0,
  },
  header: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    paddingHorizontal: 20,
    paddingVertical: 16,
    borderBottomWidth: 1,
    borderBottomColor: '#21262D',
  },
  appTitle: {
    color: '#FFFFFF',
    fontSize: 20,
    fontWeight: '800',
    letterSpacing: -0.5,
  },
  statusRow: {
    flexDirection: 'row',
    alignItems: 'center',
    marginTop: 4,
  },
  statusDot: {
    width: 7,
    height: 7,
    borderRadius: 4,
    marginRight: 6,
  },
  statusDotGreen: {
    backgroundColor: '#34D399',
  },
  statusDotGray: {
    backgroundColor: '#64748B',
  },
  statusText: {
    color: '#94A3B8',
    fontSize: 11.5,
    fontWeight: '500',
  },
  headerActions: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 8,
  },
  proBadge: {
    backgroundColor: '#1E293B',
    borderColor: '#38BDF8',
    borderWidth: 1,
    paddingHorizontal: 8,
    paddingVertical: 4,
    borderRadius: 6,
  },
  proBadgeText: {
    color: '#38BDF8',
    fontSize: 11,
    fontWeight: '800',
  },
  upgradeBadge: {
    backgroundColor: '#2563EB',
    paddingHorizontal: 10,
    paddingVertical: 5,
    borderRadius: 6,
  },
  upgradeBadgeText: {
    color: '#FFFFFF',
    fontSize: 11,
    fontWeight: '700',
  },
  iconBtn: {
    backgroundColor: '#161B22',
    borderColor: '#30363D',
    borderWidth: 1,
    paddingHorizontal: 10,
    paddingVertical: 5,
    borderRadius: 6,
  },
  iconBtnText: {
    color: '#E2E8F0',
    fontSize: 12,
    fontWeight: '600',
  },
  feedContainer: {
    flex: 1,
    paddingHorizontal: 18,
    paddingTop: 16,
  },
  feedHeaderRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    marginBottom: 12,
  },
  feedTitle: {
    color: '#F8FAFC',
    fontSize: 15,
    fontWeight: '700',
  },
  clearText: {
    color: '#EF4444',
    fontSize: 12,
    fontWeight: '600',
  },
  listContent: {
    paddingBottom: 20,
  },
  emptyState: {
    flex: 1,
    justifyContent: 'center',
    alignItems: 'center',
    paddingHorizontal: 24,
  },
  emptyIcon: {
    fontSize: 48,
    marginBottom: 16,
  },
  emptyTitle: {
    color: '#FFFFFF',
    fontSize: 17,
    fontWeight: '700',
    marginBottom: 8,
  },
  emptySubtitle: {
    color: '#64748B',
    fontSize: 13,
    lineHeight: 19,
    textAlign: 'center',
    marginBottom: 24,
  },
  testBtn: {
    backgroundColor: '#1E293B',
    borderColor: '#334155',
    borderWidth: 1,
    paddingHorizontal: 16,
    paddingVertical: 10,
    borderRadius: 8,
  },
  testBtnText: {
    color: '#38BDF8',
    fontSize: 12.5,
    fontWeight: '600',
  },
  footer: {
    paddingVertical: 12,
    alignItems: 'center',
    borderTopWidth: 1,
    borderTopColor: '#161B22',
  },
  footerText: {
    color: '#475569',
    fontSize: 11,
  },
});
