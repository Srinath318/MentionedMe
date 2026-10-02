import AsyncStorage from '@react-native-async-storage/async-storage';

export interface MentionRecord {
  id: string;
  matchedName: string;
  sentence: string;
  timestamp: string;
  audioSource?: string;
  receivedAt: number;
}

export interface PairingState {
  isPaired: boolean;
  syncToken?: string;
  deviceName?: string;
  isPro: boolean;
  plan: string;
  isDevMaster?: boolean;
}

const KEYS = {
  MENTIONS: 'mentionedme_mentions_v1',
  PAIRING: 'mentionedme_pairing_v1',
  DEV_UNLOCKED: 'mentionedme_dev_unlocked_v1',
  RELAY_URL: 'mentionedme_relay_url_v1',
};

const DEFAULT_RELAY = 'http://192.168.0.3:3000'; // Local Wi-Fi IP for direct physical phone connection

export const StorageService = {
  async getMentions(): Promise<MentionRecord[]> {
    try {
      const raw = await AsyncStorage.getItem(KEYS.MENTIONS);
      return raw ? JSON.parse(raw) : [];
    } catch {
      return [];
    }
  },

  async addMention(mention: Omit<MentionRecord, 'id' | 'receivedAt'>): Promise<MentionRecord[]> {
    try {
      const existing = await this.getMentions();
      const newRecord: MentionRecord = {
        ...mention,
        id: 'm_' + Date.now() + '_' + Math.random().toString(36).substring(2, 6),
        receivedAt: Date.now(),
      };
      // Keep last 100 mentions
      const updated = [newRecord, ...existing].slice(0, 100);
      await AsyncStorage.setItem(KEYS.MENTIONS, JSON.stringify(updated));
      return updated;
    } catch {
      return [];
    }
  },

  async clearMentions(): Promise<void> {
    try {
      await AsyncStorage.removeItem(KEYS.MENTIONS);
    } catch {}
  },

  async getPairingState(): Promise<PairingState> {
    try {
      const raw = await AsyncStorage.getItem(KEYS.PAIRING);
      if (raw) return JSON.parse(raw);
    } catch {}
    return { isPaired: false, isPro: false, plan: 'free' };
  },

  async savePairingState(state: PairingState): Promise<void> {
    try {
      await AsyncStorage.setItem(KEYS.PAIRING, JSON.stringify(state));
    } catch {}
  },

  async isDevUnlocked(): Promise<boolean> {
    try {
      const val = await AsyncStorage.getItem(KEYS.DEV_UNLOCKED);
      return val === 'true';
    } catch {
      return false;
    }
  },

  async setDevUnlocked(unlocked: boolean): Promise<void> {
    try {
      await AsyncStorage.setItem(KEYS.DEV_UNLOCKED, unlocked ? 'true' : 'false');
    } catch {}
  },

  async getRelayUrl(): Promise<string> {
    try {
      const url = await AsyncStorage.getItem(KEYS.RELAY_URL);
      return url || DEFAULT_RELAY;
    } catch {
      return DEFAULT_RELAY;
    }
  },

  async setRelayUrl(url: string): Promise<void> {
    try {
      await AsyncStorage.setItem(KEYS.RELAY_URL, url);
    } catch {}
  }
};
