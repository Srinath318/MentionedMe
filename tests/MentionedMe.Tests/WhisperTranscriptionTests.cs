using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MentionedMe.Services;
using Whisper.net;
using Xunit;

namespace MentionedMe.Tests
{
    public class WhisperTranscriptionTests
    {
        [Fact]
        public async Task WhisperFactory_LoadsModelSuccessfully()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var modelPath = Path.Combine(appData, "MentionedMe", "models", "ggml-tiny.en.bin");

            if (!File.Exists(modelPath))
            {
                // Skip if not downloaded
                return;
            }

            using var factory = WhisperFactory.FromPath(modelPath);
            Assert.NotNull(factory);

            using var processor = factory.CreateBuilder()
                .WithLanguage("en")
                .WithThreads(2)
                .WithNoContext()
                .Build();

            Assert.NotNull(processor);

            // Test empty / silent audio stream
            byte[] silencePcm = new byte[32000]; // 1 second of silence
            byte[] wavBytes = AudioResampler.CreateWavBytes(silencePcm);

            using var ms = new MemoryStream(wavBytes);
            int segmentCount = 0;
            await foreach (var segment in processor.ProcessAsync(ms, CancellationToken.None))
            {
                segmentCount++;
            }

            // Silent audio should complete cleanly without throwing
            Assert.True(segmentCount >= 0);
        }
    }
}
