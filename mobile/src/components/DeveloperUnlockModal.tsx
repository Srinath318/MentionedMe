import React, { useState } from 'react';
import { View, Text, TextInput, StyleSheet, Modal, TouchableOpacity, Alert } from 'react-native';
import * as Haptics from 'expo-haptics';
import { StorageService } from '../services/StorageService';

interface Props {
  visible: boolean;
  onClose: () => void;
  onUnlocked: () => void;
}

export const DeveloperUnlockModal: React.FC<Props> = ({ visible, onClose, onUnlocked }) => {
  const [keyInput, setKeyInput] = useState('');

  const handleUnlock = async () => {
    const trimmed = keyInput.trim().toUpperCase();
    if (
      trimmed === 'MM-DEV-MASTER-SRINATH' ||
      trimmed === 'MM-DEV-LIFETIME-OWNER' ||
      trimmed === 'MM-FOUNDER-UNLIMITED' ||
      trimmed === '778899'
    ) {
      await StorageService.setDevUnlocked(true);
      const current = await StorageService.getPairingState();
      await StorageService.savePairingState({
        ...current,
        isPro: true,
        plan: 'lifetime_developer',
        isDevMaster: true,
      });

      try {
        await Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
      } catch {}

      Alert.alert('👑 Developer Access Granted', 'Lifetime Pro unlocked for this device with zero paywalls.');
      onUnlocked();
      onClose();
    } else {
      try {
        await Haptics.notificationAsync(Haptics.NotificationFeedbackType.Error);
      } catch {}
      Alert.alert('Invalid Key', 'The developer master key entered is not recognized.');
    }
  };

  return (
    <Modal visible={visible} transparent animationType="fade" onRequestClose={onClose}>
      <View style={styles.overlay}>
        <View style={styles.modalCard}>
          <Text style={styles.title}>👑 Developer Secret Unlock</Text>
          <Text style={styles.subtitle}>
            Enter the Master Developer License Key or Admin PIN to permanently bypass all paywalls.
          </Text>

          <TextInput
            style={styles.input}
            value={keyInput}
            onChangeText={setKeyInput}
            placeholder="e.g. MM-DEV-MASTER-SRINATH"
            placeholderTextColor="#64748B"
            autoCapitalize="characters"
            autoCorrect={false}
          />

          <View style={styles.actions}>
            <TouchableOpacity style={styles.cancelBtn} onPress={onClose}>
              <Text style={styles.cancelBtnText}>Cancel</Text>
            </TouchableOpacity>
            <TouchableOpacity style={styles.unlockBtn} onPress={handleUnlock}>
              <Text style={styles.unlockBtnText}>Unlock Pro</Text>
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
    backgroundColor: 'rgba(0, 0, 0, 0.8)',
    justifyContent: 'center',
    alignItems: 'center',
    padding: 20,
  },
  modalCard: {
    backgroundColor: '#161B22',
    borderColor: '#38BDF8',
    borderWidth: 1.5,
    borderRadius: 16,
    padding: 22,
    width: '100%',
    maxWidth: 380,
  },
  title: {
    color: '#38BDF8',
    fontSize: 18,
    fontWeight: '800',
    marginBottom: 8,
  },
  subtitle: {
    color: '#94A3B8',
    fontSize: 12.5,
    lineHeight: 18,
    marginBottom: 16,
  },
  input: {
    backgroundColor: '#0D1117',
    borderColor: '#334155',
    borderWidth: 1,
    borderRadius: 8,
    color: '#FFFFFF',
    fontSize: 13,
    paddingHorizontal: 12,
    paddingVertical: 10,
    fontFamily: 'monospace',
    marginBottom: 16,
  },
  actions: {
    flexDirection: 'row',
    justifyContent: 'flex-end',
    gap: 10,
  },
  cancelBtn: {
    backgroundColor: '#1E293B',
    paddingHorizontal: 14,
    paddingVertical: 9,
    borderRadius: 6,
  },
  cancelBtnText: {
    color: '#94A3B8',
    fontSize: 12.5,
    fontWeight: '600',
  },
  unlockBtn: {
    backgroundColor: '#2563EB',
    paddingHorizontal: 16,
    paddingVertical: 9,
    borderRadius: 6,
  },
  unlockBtnText: {
    color: '#FFFFFF',
    fontSize: 12.5,
    fontWeight: '700',
  },
});
