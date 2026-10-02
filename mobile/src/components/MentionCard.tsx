import React, { useState } from 'react';
import { View, Text, StyleSheet, TouchableOpacity } from 'react-native';
import * as Clipboard from 'expo-clipboard';
import * as Haptics from 'expo-haptics';
import { MentionRecord } from '../services/StorageService';

interface Props {
  mention: MentionRecord;
}

export const MentionCard: React.FC<Props> = ({ mention }) => {
  const [copied, setCopied] = useState(false);

  const handleCopy = async () => {
    await Clipboard.setStringAsync(mention.sentence);
    try {
      await Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
    } catch {}
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  };

  return (
    <View style={styles.card}>
      {/* Header Row */}
      <View style={styles.headerRow}>
        <View style={styles.badge}>
          <Text style={styles.badgeText}>🎯 {mention.matchedName}</Text>
        </View>
        <Text style={styles.timestamp}>{mention.timestamp}</Text>
      </View>

      {/* Sentence Content */}
      <Text style={styles.sentence}>"{mention.sentence}"</Text>

      {/* Footer Row */}
      <View style={styles.footerRow}>
        <Text style={styles.audioSource}>
          {mention.audioSource || 'Windows System Audio'}
        </Text>
        <TouchableOpacity style={styles.copyBtn} onPress={handleCopy} activeOpacity={0.7}>
          <Text style={styles.copyBtnText}>{copied ? '✓ Copied' : '📋 Copy'}</Text>
        </TouchableOpacity>
      </View>
    </View>
  );
};

const styles = StyleSheet.create({
  card: {
    backgroundColor: '#161B22',
    borderColor: '#30363D',
    borderWidth: 1,
    borderRadius: 12,
    padding: 14,
    marginBottom: 10,
  },
  headerRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    marginBottom: 8,
  },
  badge: {
    backgroundColor: '#1E293B',
    borderColor: '#334155',
    borderWidth: 1,
    paddingHorizontal: 8,
    paddingVertical: 3,
    borderRadius: 6,
  },
  badgeText: {
    color: '#38BDF8',
    fontSize: 12,
    fontWeight: '700',
  },
  timestamp: {
    color: '#64748B',
    fontSize: 11.5,
    fontWeight: '500',
  },
  sentence: {
    color: '#F8FAFC',
    fontSize: 14,
    lineHeight: 20,
    marginBottom: 10,
  },
  footerRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
  },
  audioSource: {
    color: '#64748B',
    fontSize: 11,
  },
  copyBtn: {
    backgroundColor: '#0D1117',
    borderColor: '#30363D',
    borderWidth: 1,
    paddingHorizontal: 10,
    paddingVertical: 4,
    borderRadius: 6,
  },
  copyBtnText: {
    color: '#94A3B8',
    fontSize: 11,
    fontWeight: '600',
  },
});
