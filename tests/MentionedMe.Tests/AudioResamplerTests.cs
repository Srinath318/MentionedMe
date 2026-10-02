using System;
using System.IO;
using NAudio.Wave;
using MentionedMe.Models;
using MentionedMe.Services;
using Xunit;

namespace MentionedMe.Tests
{
    public class AudioResamplerTests
    {
        [Fact]
        public void Resamples48kHzStereoFloat_To16kHzMonoPcm()
        {
            // 48kHz Stereo 32-bit float
            var waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
            var resampler = new AudioResampler(waveFormat);

            // Generate 480 frames (10ms) of 48kHz stereo float
            int frameCount = 480;
            byte[] rawBuffer = new byte[frameCount * 2 * 4]; // 2 channels * 4 bytes
            
            // Fill with a sine wave test signal
            for (int i = 0; i < frameCount; i++)
            {
                float sample = (float)Math.Sin(2.0 * Math.PI * 440.0 * i / 48000.0) * 0.5f;
                byte[] bytes = BitConverter.GetBytes(sample);
                Buffer.BlockCopy(bytes, 0, rawBuffer, i * 8, 4);     // Left
                Buffer.BlockCopy(bytes, 0, rawBuffer, i * 8 + 4, 4); // Right
            }

            var (pcmBytes, peak, rms) = resampler.ProcessBuffer(rawBuffer, rawBuffer.Length);

            Assert.NotEmpty(pcmBytes);
            // 480 frames at 48kHz -> ~160 frames at 16kHz (2 bytes per sample = ~320 bytes)
            Assert.InRange(pcmBytes.Length, 300, 340);
            Assert.InRange(peak, 0.3f, 0.7f);
            Assert.True(rms > 0.05f);
        }

        [Fact]
        public void CreateWavBytes_GeneratesValidRiffHeader()
        {
            byte[] dummyPcm = new byte[3200]; // 100ms of 16kHz 16-bit mono
            var wavBytes = AudioResampler.CreateWavBytes(dummyPcm);

            Assert.Equal(44 + 3200, wavBytes.Length);
            // Check RIFF and WAVE magic headers
            Assert.Equal((byte)'R', wavBytes[0]);
            Assert.Equal((byte)'I', wavBytes[1]);
            Assert.Equal((byte)'F', wavBytes[2]);
            Assert.Equal((byte)'F', wavBytes[3]);

            Assert.Equal((byte)'W', wavBytes[8]);
            Assert.Equal((byte)'A', wavBytes[9]);
            Assert.Equal((byte)'V', wavBytes[10]);
            Assert.Equal((byte)'E', wavBytes[11]);
        }
    }

    public class AppSettingsTests
    {
        [Fact]
        public void GetAllTargetNames_SplitsAliasesCorrectly()
        {
            var settings = new AppSettings
            {
                UserName = " Srinath ",
                Aliases = "Sri, Srinath, Nath; Srini / Sree"
            };

            var names = settings.GetAllTargetNames();

            Assert.Contains("Srinath", names);
            Assert.Contains("Sri", names);
            Assert.Contains("Nath", names);
            Assert.Contains("Srini", names);
            Assert.Contains("Sree", names);
            // Check deduplication
            Assert.Equal(5, names.Count);
        }
    }
}
