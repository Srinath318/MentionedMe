import * as Notifications from 'expo-notifications';
import * as Device from 'expo-device';
import { Platform } from 'react-native';
import { StorageService } from './StorageService';

// Configure foreground notification behavior
Notifications.setNotificationHandler({
  handleNotification: async () => ({
    shouldShowAlert: true,
    shouldPlaySound: true,
    shouldSetBadge: true,
  }),
});

export const PushService = {
  async registerForPushNotificationsAsync(): Promise<string | null> {
    if (Platform.OS === 'android') {
      await Notifications.setNotificationChannelAsync('mention-alerts', {
        name: 'Mention Alerts',
        importance: Notifications.AndroidImportance.MAX,
        vibrationPattern: [0, 250, 250, 250],
        lightColor: '#2563EB',
        sound: 'default',
        enableVibrate: true,
        showBadge: true,
      });
    }

    if (!Device.isDevice) {
      console.log('[PushService] Must use physical device for push notifications (or mock for simulator).');
      // Return a simulated mock token for emulator testing
      return 'ExponentPushToken[mock_simulator_' + Math.random().toString(36).substring(2, 8) + ']';
    }

    const { status: existingStatus } = await Notifications.getPermissionsAsync();
    let finalStatus = existingStatus;

    if (existingStatus !== 'granted') {
      const { status } = await Notifications.requestPermissionsAsync();
      finalStatus = status;
    }

    if (finalStatus !== 'granted') {
      console.warn('[PushService] Failed to get push token permission!');
      return null;
    }

    // In Expo Go SDK 53+, remote push tokens are disabled in Expo Go.
    // We use a clean unique device sync token for local Wi-Fi pairing & notifications.
    const token = 'MentionDevice_' + (Device.osBuildId || Device.modelName || 'Android').replace(/\s+/g, '_') + '_' + Device.brand;
    return token;
  },

  async generatePairCode(pushToken: string): Promise<{ pairCode: string; expiresAt: number } | null> {
    const relayUrl = await StorageService.getRelayUrl();
    const deviceName = Device.modelName || (Platform.OS === 'android' ? 'Android Phone' : 'Mobile Device');

    try {
      const response = await fetch(`${relayUrl.replace(/\/+$/, '')}/api/pair/create`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          pushToken,
          deviceName,
        }),
      });

      if (!response.ok) {
        throw new Error(`Relay returned status ${response.status}`);
      }

      const data = await response.json();
      return {
        pairCode: data.pairCode,
        expiresAt: data.expiresAt,
      };
    } catch (err: any) {
      console.error('[PushService] Failed to generate pair code from relay:', err.message);
      return null;
    }
  },

  async checkPairStatus(pairCode: string): Promise<any | null> {
    const relayUrl = await StorageService.getRelayUrl();
    try {
      const response = await fetch(`${relayUrl.replace(/\/+$/, '')}/api/pair/status/${pairCode}`);
      if (response.ok) {
        const data = await response.json();
        if (data.status === 'claimed') {
          return data.claimedData;
        }
      }
      return null;
    } catch {
      return null;
    }
  },
};
