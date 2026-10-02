using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using MentionedMe.Models;

namespace MentionedMe.Services
{
    public interface IAudioCaptureService : IDisposable
    {
        bool IsCapturing { get; }
        string DeviceFriendlyName { get; }
        event EventHandler<AudioLevelEventArgs>? AudioLevelChanged;
        event EventHandler<byte[]>? SpeechSegmentAvailable;
        event EventHandler<string>? CaptureError;

        void StartCapture(float vadThreshold = 0.012f);
        void StopCapture();
    }

    public class WasapiLoopbackService : IAudioCaptureService
    {
        private WasapiLoopbackCapture? _capture;
        private AudioResampler? _resampler;
        private MMDevice? _renderDevice;
        private float _vadThreshold = 0.001f;
        private readonly object _lock = new object();

        // 16kHz 16-bit mono = 32,000 bytes per second
        private const int BytesPerSecond16k = 32000;

        // 2.6-second rolling window with 0.35-second stride (2.25-second overlap)
        // Eliminates syllable cutoffs and phrase truncations while maintaining ~0.45s alert latency
        private const int RollingWindowBytes = (int)(BytesPerSecond16k * 2.6);  // 2.6s window
        private const int StrideBytes = (int)(BytesPerSecond16k * 0.35);        // 0.35s stride (ultra-fast emission)
        private const int MinSpeechBytes = (int)(BytesPerSecond16k * 0.25);     // 0.25s minimum speech to emit
        private const int PreRollBytes = (int)(BytesPerSecond16k * 0.35);       // 0.35s pre-roll for leading consonants
        private const int SilenceFrameThreshold = 20;                           // ~1.2s of silence before ending utterance

        private readonly MemoryStream _rollingPcm = new MemoryStream(RollingWindowBytes * 2);
        private int _speechBytesSinceLastEmission = 0;
        private int _consecutiveSilentFrames = 0;
        private bool _isSpeechActive = false;
        private DateTime _lastSpeechDetectedTime = DateTime.MinValue;

        // Diagnostics
        private static int _segmentIdCounter = 0;

        public bool IsCapturing { get; private set; }
        public string DeviceFriendlyName { get; private set; } = "Windows Default Output";

        public event EventHandler<AudioLevelEventArgs>? AudioLevelChanged;
        public event EventHandler<byte[]>? SpeechSegmentAvailable;
        public event EventHandler<string>? CaptureError;

        public void StartCapture(float vadThreshold = 0.012f)
        {
            lock (_lock)
            {
                // Always clean up previous capture instances if any
                CleanupCaptureResources();

                _vadThreshold = vadThreshold;
                _rollingPcm.SetLength(0);
                _speechBytesSinceLastEmission = 0;
                _consecutiveSilentFrames = 0;
                _isSpeechActive = false;

                try
                {
                    using var enumerator = new MMDeviceEnumerator();
                    _renderDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                    DeviceFriendlyName = _renderDevice.FriendlyName;

                    _capture = new WasapiLoopbackCapture(_renderDevice);
                    _resampler = new AudioResampler(_capture.WaveFormat);

                    _capture.DataAvailable += OnDataAvailable;
                    _capture.RecordingStopped += OnRecordingStopped;

                    _capture.StartRecording();
                    IsCapturing = true;
                }
                catch (Exception ex)
                {
                    CleanupCaptureResources();
                    CaptureError?.Invoke(this, $"WASAPI Loopback Capture initialization failed: {ex.Message}");
                }
            }
        }

        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (!IsCapturing || _resampler == null || e.BytesRecorded <= 0)
            {
                // If completely silent (no audio playback), dispatch zero level to keep UI responsive
                AudioLevelChanged?.Invoke(this, new AudioLevelEventArgs(0f, 0f, false));
                return;
            }

            try
            {
                var (pcm16k, peak, rms) = _resampler.ProcessBuffer(e.Buffer, e.BytesRecorded);
                bool hasSpeech = rms >= _vadThreshold;

                AudioLevelChanged?.Invoke(this, new AudioLevelEventArgs(peak, rms, hasSpeech));

                if (pcm16k.Length == 0) return;

                lock (_rollingPcm)
                {
                    if (hasSpeech)
                    {
                        _isSpeechActive = true;
                        _consecutiveSilentFrames = 0;
                        _lastSpeechDetectedTime = DateTime.UtcNow;

                        AppendToRollingBuffer(pcm16k);
                        _speechBytesSinceLastEmission += pcm16k.Length;

                        // Emit rolling window every ~0.8s while speech is continuous
                        if (_speechBytesSinceLastEmission >= StrideBytes && _rollingPcm.Length >= MinSpeechBytes)
                        {
                            EmitCurrentRollingWindow();
                            _speechBytesSinceLastEmission = 0;
                        }
                    }
                    else if (_isSpeechActive)
                    {
                        // Trailing speech / pause
                        _consecutiveSilentFrames++;
                        AppendToRollingBuffer(pcm16k);
                        _speechBytesSinceLastEmission += pcm16k.Length;

                        if (_speechBytesSinceLastEmission >= StrideBytes && _rollingPcm.Length >= MinSpeechBytes)
                        {
                            EmitCurrentRollingWindow();
                            _speechBytesSinceLastEmission = 0;
                        }
                        else if (_consecutiveSilentFrames >= SilenceFrameThreshold)
                        {
                            // Speech paused: emit final window if there is pending audio
                            if (_speechBytesSinceLastEmission >= (BytesPerSecond16k * 0.15) && _rollingPcm.Length >= MinSpeechBytes)
                            {
                                EmitCurrentRollingWindow();
                            }
                            _isSpeechActive = false;
                            _speechBytesSinceLastEmission = 0;
                            _consecutiveSilentFrames = 0;
                        }
                    }
                    else
                    {
                        // Idle silence: maintain pre-roll so leading consonants are intact
                        AppendToRollingBuffer(pcm16k);
                        if (_rollingPcm.Length > PreRollBytes)
                        {
                            TrimRollingBufferTo(PreRollBytes);
                        }
                        _speechBytesSinceLastEmission = 0;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Audio processing error: {ex.Message}");
            }
        }

        private void AppendToRollingBuffer(byte[] pcm16k)
        {
            _rollingPcm.Write(pcm16k, 0, pcm16k.Length);
            if (_rollingPcm.Length > RollingWindowBytes)
            {
                TrimRollingBufferTo(RollingWindowBytes);
            }
        }

        private void TrimRollingBufferTo(int targetLength)
        {
            if (_rollingPcm.Length <= targetLength) return;

            var allBytes = _rollingPcm.ToArray();
            _rollingPcm.SetLength(0);
            int startOffset = allBytes.Length - targetLength;
            _rollingPcm.Write(allBytes, startOffset, targetLength);
        }

        private void EmitCurrentRollingWindow()
        {
            if (_rollingPcm.Length < MinSpeechBytes) return;

            var pcmData = _rollingPcm.ToArray();
            var wavData = AudioResampler.CreateWavBytes(pcmData);

            int segId = Interlocked.Increment(ref _segmentIdCounter);
            Debug.WriteLine($"[DIAG] 🔊 Rolling window #{segId} EMITTED: {pcmData.Length} bytes ({pcmData.Length / 32000.0:F2}s audio, overlap: ~1.6s)");

            SpeechSegmentAvailable?.Invoke(this, wavData);
        }

        private void OnRecordingStopped(object? sender, StoppedEventArgs e)
        {
            lock (_lock)
            {
                CleanupCaptureResources();
            }

            if (e.Exception != null)
            {
                CaptureError?.Invoke(this, $"WASAPI Loopback stopped unexpectedly: {e.Exception.Message}");
            }
        }

        public void StopCapture()
        {
            lock (_lock)
            {
                CleanupCaptureResources();
            }
        }

        private void CleanupCaptureResources()
        {
            IsCapturing = false;
            _isSpeechActive = false;
            _consecutiveSilentFrames = 0;

            if (_capture != null)
            {
                try
                {
                    _capture.DataAvailable -= OnDataAvailable;
                    _capture.RecordingStopped -= OnRecordingStopped;
                    _capture.StopRecording();
                }
                catch { }

                try
                {
                    _capture.Dispose();
                }
                catch { }

                _capture = null;
            }

            if (_renderDevice != null)
            {
                try
                {
                    _renderDevice.Dispose();
                }
                catch { }

                _renderDevice = null;
            }

            _resampler = null;
            _rollingPcm.SetLength(0);
            _speechBytesSinceLastEmission = 0;
            AudioLevelChanged?.Invoke(this, new AudioLevelEventArgs(0f, 0f, false));
        }

        public void Dispose()
        {
            StopCapture();
            _rollingPcm.Dispose();
        }
    }
}
