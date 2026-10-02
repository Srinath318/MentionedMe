const express = require('express');
const cors = require('cors');
const crypto = require('crypto');
const fs = require('fs');
const path = require('path');

const app = express();
const PORT = process.env.PORT || 3000;

app.use(cors());
app.use(express.json());

// Master Developer Keys that permanently bypass payment
const MASTER_DEV_KEYS = new Set([
  'MM-DEV-MASTER-SRINATH',
  'MM-DEV-LIFETIME-OWNER',
  'MM-FOUNDER-UNLIMITED'
]);

// Persistence storage path
const DATA_DIR = path.join(__dirname, 'data');
if (!fs.existsSync(DATA_DIR)) {
  fs.mkdirSync(DATA_DIR, { recursive: true });
}
const DB_FILE = path.join(DATA_DIR, 'db.json');

// In-memory state
let db = {
  activePairCodes: {}, // pairCode -> { pushToken, deviceName, createdAt, expiresAt }
  pairedDevices: {},   // syncToken -> { pushToken, deviceName, proLicenseKey, isPro, plan, pairedAt }
  proLicenses: {}      // licenseKey -> { plan, active, createdAt }
};

// Load existing DB if present
if (fs.existsSync(DB_FILE)) {
  try {
    const raw = fs.readFileSync(DB_FILE, 'utf8');
    db = JSON.parse(raw);
    console.log('[Relay DB] Loaded existing database.');
  } catch (e) {
    console.warn('[Relay DB] Could not parse DB file, starting fresh.');
  }
}

function saveDb() {
  try {
    fs.writeFileSync(DB_FILE, JSON.stringify(db, null, 2), 'utf8');
  } catch (e) {
    console.error('[Relay DB] Failed to save DB:', e.message);
  }
}

// Clean expired pair codes every minute
setInterval(() => {
  const now = Date.now();
  let changed = false;
  for (const [code, info] of Object.entries(db.activePairCodes)) {
    if (now > info.expiresAt) {
      delete db.activePairCodes[code];
      changed = true;
    }
  }
  if (changed) saveDb();
}, 60000);

// Helper: Check if license key is valid Pro or Master Dev Key
function validateLicense(key) {
  if (!key) return { isValid: false, isDevMaster: false, plan: 'free' };
  const trimmed = key.trim().toUpperCase();
  if (MASTER_DEV_KEYS.has(trimmed)) {
    return { isValid: true, isDevMaster: true, plan: 'lifetime_developer' };
  }
  if (db.proLicenses[trimmed] && db.proLicenses[trimmed].active) {
    return { isValid: true, isDevMaster: false, plan: db.proLicenses[trimmed].plan || 'pro_yearly' };
  }
  return { isValid: false, isDevMaster: false, plan: 'free' };
}

// --- API Endpoints ---

// 1. Health check
app.get('/health', (req, res) => {
  res.json({
    status: 'ok',
    service: 'MentionedMe Push Relay',
    timestamp: new Date().toISOString(),
    pairedDevicesCount: Object.keys(db.pairedDevices).length
  });
});

// 2. Verify License Key
app.post('/api/auth/verify-license', (req, res) => {
  const { licenseKey } = req.body || {};
  const status = validateLicense(licenseKey);
  res.json(status);
});

// 3. Mobile creates a temporary 6-character Pairing Code (e.g. MM-4829)
app.post('/api/pair/create', (req, res) => {
  const { pushToken, deviceName } = req.body || {};

  if (!pushToken) {
    return res.status(400).json({ error: 'pushToken is required' });
  }

  // Generate clean 4-digit code with prefix: MM-XXXX
  const randomDigits = Math.floor(1000 + Math.random() * 9000);
  const pairCode = `MM-${randomDigits}`;
  const now = Date.now();
  const expiresAt = now + 10 * 60 * 1000; // 10 minutes validity

  db.activePairCodes[pairCode] = {
    pushToken,
    deviceName: deviceName || 'Android Device',
    status: 'pending',
    createdAt: now,
    expiresAt
  };

  saveDb();

  console.log(`[Relay Pair] Generated pair code ${pairCode} for device: ${deviceName || 'Android Device'}`);

  res.json({
    pairCode,
    expiresAt,
    validSeconds: 600
  });
});

// 4. Mobile polls pairing status while code is displayed
app.get('/api/pair/status/:pairCode', (req, res) => {
  const code = (req.params.pairCode || '').trim().toUpperCase();
  const pairInfo = db.activePairCodes[code];

  if (!pairInfo) {
    return res.json({ status: 'not_found' });
  }

  if (pairInfo.status === 'claimed') {
    return res.json({
      status: 'claimed',
      claimedData: pairInfo.claimedData
    });
  }

  return res.json({ status: 'pending' });
});

// 5. Windows Desktop claims the pairing code with optional Pro License Key
app.post('/api/pair/claim', (req, res) => {
  const { pairCode, proLicenseKey } = req.body || {};

  if (!pairCode) {
    return res.status(400).json({ error: 'pairCode is required' });
  }

  const normalizedCode = pairCode.trim().toUpperCase();
  const pairInfo = db.activePairCodes[normalizedCode];

  if (!pairInfo) {
    return res.status(404).json({ error: 'Invalid or expired pairing code. Please generate a new code on your phone.' });
  }

  if (Date.now() > pairInfo.expiresAt) {
    delete db.activePairCodes[normalizedCode];
    saveDb();
    return res.status(410).json({ error: 'Pairing code expired. Please generate a new code on your phone.' });
  }

  // Validate Pro license / Master Developer Key
  const licenseStatus = validateLicense(proLicenseKey);

  // Generate permanent SyncToken for this desktop <-> phone connection
  const syncToken = 'st_' + crypto.randomBytes(24).toString('hex');

  const claimResult = {
    syncToken,
    deviceName: pairInfo.deviceName,
    isPro: licenseStatus.isValid,
    plan: licenseStatus.plan,
    isDevMaster: licenseStatus.isDevMaster
  };

  db.pairedDevices[syncToken] = {
    pushToken: pairInfo.pushToken,
    deviceName: pairInfo.deviceName,
    proLicenseKey: proLicenseKey || '',
    isPro: licenseStatus.isValid,
    plan: licenseStatus.plan,
    isDevMaster: licenseStatus.isDevMaster,
    pairedAt: Date.now()
  };

  // Mark pair code as claimed so phone detects it instantly
  db.activePairCodes[normalizedCode] = {
    ...pairInfo,
    status: 'claimed',
    claimedData: claimResult
  };
  saveDb();

  console.log(`[Relay Pair] Successfully paired desktop with ${pairInfo.deviceName} (SyncToken: ${syncToken.substring(0, 10)}..., Pro: ${licenseStatus.isValid})`);

  res.json({
    success: true,
    ...claimResult
  });
});

// Recent mentions queue for live polling
const recentMentions = [];

app.get('/api/mentions/poll', (req, res) => {
  const since = parseInt(req.query.since || '0', 10);
  const newMentions = recentMentions.filter(m => m.receivedAt > since);
  res.json({ mentions: newMentions });
});

// SSE Clients map for live streaming
const sseClients = new Map();

app.get('/api/stream', (req, res) => {
  const syncToken = req.query.syncToken || '';
  const clientId = 'c_' + Date.now() + '_' + Math.random().toString(36).substring(2, 6);

  res.setHeader('Content-Type', 'text/event-stream');
  res.setHeader('Cache-Control', 'no-cache');
  res.setHeader('Connection', 'keep-alive');
  res.flushHeaders();

  sseClients.set(clientId, { res, syncToken });
  res.write(`data: ${JSON.stringify({ type: 'connected', clientId })}\n\n`);

  req.on('close', () => {
    sseClients.delete(clientId);
  });
});

// Unlink device across all connected apps
app.post('/api/pair/unlink', (req, res) => {
  const { syncToken } = req.body || {};
  if (syncToken && db.pairedDevices[syncToken]) {
    const name = db.pairedDevices[syncToken].deviceName;
    delete db.pairedDevices[syncToken];
    saveDb();
    console.log(`[Relay] Device ${name} unlinked.`);
  }
  const event = { type: 'unlinked', receivedAt: Date.now() };
  recentMentions.push(event);
  for (const [id, client] of sseClients.entries()) {
    try { client.res.write(`data: ${JSON.stringify(event)}\n\n`); } catch (e) { sseClients.delete(id); }
  }
  res.json({ success: true });
});

// Clear mentions across PC and phone
app.post('/api/mentions/clear', (req, res) => {
  recentMentions.length = 0;
  const event = { type: 'clear', receivedAt: Date.now() };
  recentMentions.push(event);
  for (const [id, client] of sseClients.entries()) {
    try { client.res.write(`data: ${JSON.stringify(event)}\n\n`); } catch (e) { sseClients.delete(id); }
  }
  res.json({ success: true });
});

// 5. Windows Desktop dispatches a real-time mention notification
app.post('/api/notify', async (req, res) => {
  const { syncToken, matchedName, sentence, timestamp, audioSource } = req.body || {};

  if (!syncToken) {
    return res.status(400).json({ error: 'syncToken is required' });
  }

  const device = db.pairedDevices[syncToken];
  if (!device) {
    return res.status(401).json({ error: 'Invalid or unlinked syncToken. Please re-pair your mobile device in Settings.' });
  }

  // Verify Pro status (or Master Dev Key)
  if (!device.isPro && !device.isDevMaster) {
    return res.status(402).json({
      error: 'Pro subscription required for mobile sync.',
      code: 'PAYMENT_REQUIRED'
    });
  }

  const formattedTime = timestamp || new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit' });
  const mentionEvent = {
    type: 'mention',
    matchedName,
    sentence,
    timestamp: formattedTime,
    audioSource: audioSource || 'Windows Audio',
    receivedAt: Date.now()
  };

  recentMentions.push(mentionEvent);
  if (recentMentions.length > 50) recentMentions.shift();

  // Broadcast to all active local Wi-Fi SSE stream clients immediately
  for (const [id, client] of sseClients.entries()) {
    try {
      client.res.write(`data: ${JSON.stringify(mentionEvent)}\n\n`);
    } catch (e) {
      sseClients.delete(id);
    }
  }

  const pushToken = device.pushToken;
  const title = `🎯 Mentioned: ${matchedName || 'You'}`;
  const body = sentence || 'Your name was spoken in system audio.';

  console.log(`[Relay Notify] Dispatching mention to ${device.deviceName}: "${body}"`);

  try {
    // Send to Expo Push Notification service
    const pushPayload = {
      to: pushToken,
      title,
      body,
      sound: 'default',
      priority: 'high',
      channelId: 'mention-alerts',
      data: mentionEvent
    };

    const pushResponse = await fetch('https://exp.host/--/api/v2/push/send', {
      method: 'POST',
      headers: {
        'Accept': 'application/json',
        'Accept-encoding': 'gzip, deflate',
        'Content-Type': 'application/json',
      },
      body: JSON.stringify(pushPayload)
    });

    const pushResult = await pushResponse.json();

    res.json({
      success: true,
      deliveredTo: device.deviceName,
      timestamp: formattedTime,
      pushResult
    });
  } catch (err) {
    console.error('[Relay Notify] Push dispatch error:', err.message);
    res.json({
      success: true,
      deliveredTo: device.deviceName,
      timestamp: formattedTime,
      note: 'Delivered via local LAN stream'
    });
  }
});

// Start relay server
app.listen(PORT, () => {
  console.log(`=========================================`);
  console.log(`🚀 MentionedMe Push Relay Running on port ${PORT}`);
  console.log(`🔑 Master Developer Keys Active:`);
  MASTER_DEV_KEYS.forEach(k => console.log(`   - ${k}`));
  console.log(`=========================================`);
});
