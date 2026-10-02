import React, { useState } from 'react';
import { View, Text, StyleSheet, Modal, TouchableOpacity, ActivityIndicator } from 'react-native';
import * as Clipboard from 'expo-clipboard';
import * as Haptics from 'expo-haptics';

interface Props {
  visible: boolean;
  pairCode: string | null;
  loading: boolean;
  onClose: () => void;
  onRefresh: () => void;
}

export const PairingModal: React.FC<Props> = ({ visible, pairCode, loading, onClose, onRefresh }) => {
  const [copied, setCopied] = useState(false);

  const handleCopyCode = async () => {
    if (!pairCode) return;
    await Clipboard.setStringAsync(pairCode);
    try {
      await Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
    } catch {}
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  };

  return (
    <Modal visible={visible} transparent animationType="slide" onRequestClose={onClose}>
      <View style={styles.overlay}>
        <View style={styles.modalCard}>
          {/* Header */}
          <View style={styles.header}>
            <Text style={styles.title}>Pair with Windows PC</Text>
            <TouchableOpacity onPress={onClose} hitSlop={{ top: 10, bottom: 10, left: 10, right: 10 }}>
              <Text style={styles.closeBtn}>✕</Text>
            </TouchableOpacity>
          </View>

          <Text style={styles.subtitle}>
            Enter this 6-character pairing code in your Windows MentionedMe app to link this device.
          </Text>

          {/* Pair Code Box */}
          <View style={styles.codeContainer}>
            {loading ? (
              <ActivityIndicator size="large" color="#38BDF8" />
            ) : (
              <TouchableOpacity onPress={handleCopyCode} activeOpacity={0.8} style={styles.codeTouchable}>
                <Text style={styles.codeText}>{pairCode || '----'}</Text>
                <Text style={styles.copyHint}>{copied ? '✓ Copied to clipboard' : 'Tap to copy code'}</Text>
              </TouchableOpacity>
            )}
          </View>

          {/* Step-by-Step Instructions */}
          <View style={styles.stepsContainer}>
            <Text style={styles.stepItem}>1. Open <Text style={styles.boldText}>MentionedMe</Text> on your Windows PC</Text>
            <Text style={styles.stepItem}>2. Click <Text style={styles.boldText}>Settings ⚙️ → Mobile Companion</Text></Text>
            <Text style={styles.stepItem}>3. Enter <Text style={styles.boldText}>{pairCode || 'code'}</Text> and click <Text style={styles.boldText}>Link Phone</Text></Text>
          </View>

          {/* Action Buttons */}
          <View style={styles.actions}>
            <TouchableOpacity style={styles.refreshBtn} onPress={onRefresh} disabled={loading}>
              <Text style={styles.refreshBtnText}>🔄 Generate New Code</Text>
            </TouchableOpacity>
            <TouchableOpacity style={styles.doneBtn} onPress={onClose}>
              <Text style={styles.doneBtnText}>Done</Text>
            </TouchableOpacity>
          </View>
        </View>
      </View>
    </Modal>
  );
};

const styles = StyleSheet.create({
  overlay: {
    flex: 1,
    backgroundColor: 'rgba(0, 0, 0, 0.75)',
    justifyContent: 'center',
    alignItems: 'center',
    padding: 20,
  },
  modalCard: {
    backgroundColor: '#161B22',
    borderColor: '#30363D',
    borderWidth: 1,
    borderRadius: 16,
    padding: 20,
    width: '100%',
    maxWidth: 380,
  },
  header: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    marginBottom: 8,
  },
  title: {
    color: '#FFFFFF',
    fontSize: 18,
    fontWeight: '700',
  },
  closeBtn: {
    color: '#94A3B8',
    fontSize: 18,
    padding: 4,
  },
  subtitle: {
    color: '#94A3B8',
    fontSize: 13,
    lineHeight: 18,
    marginBottom: 16,
  },
  codeContainer: {
    backgroundColor: '#0D1117',
    borderColor: '#2563EB',
    borderWidth: 1.5,
    borderRadius: 12,
    paddingVertical: 18,
    alignItems: 'center',
    marginBottom: 18,
  },
  codeTouchable: {
    alignItems: 'center',
  },
  codeText: {
    color: '#38BDF8',
    fontSize: 32,
    fontWeight: '800',
    letterSpacing: 4,
    fontFamily: 'monospace',
  },
  copyHint: {
    color: '#64748B',
    fontSize: 11,
    marginTop: 6,
    fontWeight: '500',
  },
  stepsContainer: {
    backgroundColor: '#0D1117',
    borderRadius: 10,
    padding: 12,
    marginBottom: 18,
  },
  stepItem: {
    color: '#CBD5E1',
    fontSize: 12.5,
    lineHeight: 22,
  },
  boldText: {
    color: '#FFFFFF',
    fontWeight: '700',
  },
  actions: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    gap: 10,
  },
  refreshBtn: {
    flex: 1,
    backgroundColor: '#1E293B',
    borderColor: '#334155',
    borderWidth: 1,
    paddingVertical: 12,
    borderRadius: 8,
    alignItems: 'center',
  },
  refreshBtnText: {
    color: '#E2E8F0',
    fontSize: 12,
    fontWeight: '600',
  },
  doneBtn: {
    flex: 1,
    backgroundColor: '#2563EB',
    paddingVertical: 12,
    borderRadius: 8,
    alignItems: 'center',
  },
  doneBtnText: {
    color: '#FFFFFF',
    fontSize: 13,
    fontWeight: '700',
  },
});
