using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Whisper.net;

namespace MentionedMe.Services
{
    public interface ITranscriptionService : IAsyncDisposable
    {
        bool IsModelLoaded { get; }
        string LoadedModelPath { get; }
        event EventHandler<string>? TranscriptSegmentReceived;
        event EventHandler<string>? TranscriptionError;

        Task LoadModelAsync(string modelPath, string? initialPrompt = null, CancellationToken cancellationToken = default);
        void EnqueueAudioSegment(byte[] wavData);
        void StopProcessing();
    }

    public class WhisperTranscriptionService : ITranscriptionService
    {
        private WhisperFactory? _whisperFactory;
        private WhisperProcessor? _whisperProcessor;
        private Channel<byte[]>? _windowChannel;
        private Task? _processingTask;
        private CancellationTokenSource? _cts;
        private readonly SemaphoreSlim _modelLock = new SemaphoreSlim(1, 1);
        private string _lastInitialPrompt = string.Empty;

        // Diagnostics
        private static int _windowIdCounter = 0;

        public bool IsModelLoaded => _whisperProcessor != null;
        public string LoadedModelPath { get; private set; } = string.Empty;

        public event EventHandler<string>? TranscriptSegmentReceived;
        public event EventHandler<string>? TranscriptionError;

        public async Task LoadModelAsync(string modelPath, string? initialPrompt = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath))
            {
                throw new FileNotFoundException($"Whisper model not found at path: {modelPath}");
            }

            await _modelLock.WaitAsync(cancellationToken);
            try
            {
                var prompt = initialPrompt ?? string.Empty;

                // If model is already loaded with same path & prompt, ensure processing loop is active
                if (IsModelLoaded &&
                    string.Equals(LoadedModelPath, modelPath, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(_lastInitialPrompt, prompt, StringComparison.OrdinalIgnoreCase))
                {
                    EnsureProcessingLoopRunning();
                    return;
                }

                // Clean up previous processor
                StopProcessing();
                _whisperProcessor?.Dispose();
                _whisperFactory?.Dispose();

                _whisperFactory = WhisperFactory.FromPath(modelPath);

                // Configure processor for fast real-time transcription
                int threadCount = Math.Clamp(Environment.ProcessorCount, 4, 12);
                var builder = _whisperFactory.CreateBuilder()
                    .WithLanguage("en")
                    .WithThreads(threadCount)
                    .WithNoContext();

                if (!string.IsNullOrWhiteSpace(prompt))
                {
                    builder.WithPrompt(prompt);
                }

                _whisperProcessor = builder.Build();

                LoadedModelPath = modelPath;
                _lastInitialPrompt = prompt;

                Debug.WriteLine($"[DIAG] ✅ Whisper model loaded: {Path.GetFileName(modelPath)}, threads={threadCount}, prompt='{prompt}'");

                // Start processing loop
                EnsureProcessingLoopRunning();
            }
            catch (Exception ex)
            {
                TranscriptionError?.Invoke(this, $"Failed to load Whisper model: {ex.Message}");
                throw;
            }
            finally
            {
                _modelLock.Release();
            }
        }

        private void EnsureProcessingLoopRunning()
        {
            if (_cts != null && !_cts.IsCancellationRequested && _processingTask != null && !_processingTask.IsCompleted)
            {
                return;
            }

            try
            {
                _cts?.Cancel();
                _cts?.Dispose();
            }
            catch { }

            _cts = new CancellationTokenSource();
            _windowChannel = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(12)
            {
                FullMode = BoundedChannelFullMode.DropOldest
            });

            _processingTask = Task.Run(() => ProcessingLoopAsync(_cts.Token));

            Debug.WriteLine("[DIAG] 🔄 Whisper window processing loop started");
        }

        public void EnqueueAudioSegment(byte[] wavData)
        {
            if (!IsModelLoaded || wavData == null || wavData.Length <= 44)
            {
                return;
            }

            if (_windowChannel == null || _cts == null || _cts.IsCancellationRequested)
            {
                EnsureProcessingLoopRunning();
            }

            int winId = Interlocked.Increment(ref _windowIdCounter);
            int pcmBytes = wavData.Length - 44;

            Debug.WriteLine($"[DIAG] 📥 Window #{winId} QUEUED: {pcmBytes} bytes ({pcmBytes / 32000.0:F2}s audio)");
            _windowChannel?.Writer.TryWrite(wavData);
        }

        private async Task ProcessingLoopAsync(CancellationToken ct)
        {
            if (_windowChannel == null) return;

            while (!ct.IsCancellationRequested)
            {
                byte[] wavData;
                try
                {
                    if (!await _windowChannel.Reader.WaitToReadAsync(ct)) break;
                    if (!_windowChannel.Reader.TryRead(out wavData!) || wavData == null) continue;
                }
                catch (OperationCanceledException) { break; }

                if (_whisperProcessor == null || wavData.Length <= 44) continue;

                var sw = Stopwatch.StartNew();
                int pcmBytes = wavData.Length - 44;
                double audioSeconds = pcmBytes / 32000.0;

                Debug.WriteLine($"[DIAG] 🎙️ Whisper PROCESSING START: {audioSeconds:F2}s audio");

                try
                {
                    using var stream = new MemoryStream(wavData);
                    await foreach (var result in _whisperProcessor.ProcessAsync(stream, ct))
                    {
                        var text = result.Text?.Trim();
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            Debug.WriteLine($"[DIAG] 📝 Whisper RESULT: \"{text}\" (at {sw.ElapsedMilliseconds}ms)");
                            TranscriptSegmentReceived?.Invoke(this, text);
                        }
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[DIAG] ❌ Whisper ERROR: {ex.Message}");
                }

                sw.Stop();
                double processingSeconds = sw.ElapsedMilliseconds / 1000.0;
                Debug.WriteLine($"[DIAG] ✅ Whisper PROCESSING END: took {sw.ElapsedMilliseconds}ms for {audioSeconds:F2}s audio");
            }

            Debug.WriteLine("[DIAG] 🔄 Whisper processing loop ended");
        }

        public void StopProcessing()
        {
            try
            {
                _cts?.Cancel();
                _windowChannel?.Writer.TryComplete();
                _cts?.Dispose();
                _cts = null;
                _windowChannel = null;
            }
            catch { }

            Debug.WriteLine("[DIAG] ⏹️ Whisper processing stopped");
        }

        public async ValueTask DisposeAsync()
        {
            StopProcessing();
            if (_processingTask != null)
            {
                try { await _processingTask; } catch { }
            }

            _whisperProcessor?.Dispose();
            _whisperFactory?.Dispose();
            _modelLock.Dispose();
        }
    }
}
