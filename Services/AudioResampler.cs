using System;
using System.IO;
using NAudio.Wave;

namespace MentionedMe.Services
{
    public class AudioResampler
    {
        public const int TargetSampleRate = 16000;
        public const int TargetChannels = 1;
        public const int TargetBitsPerSample = 16;

        private readonly WaveFormat _sourceFormat;
        private double _resampleRatio; // sourceSampleRate / TargetSampleRate
        private double _resampleFraction = 0.0;

        public AudioResampler(WaveFormat sourceFormat)
        {
            _sourceFormat = sourceFormat ?? throw new ArgumentNullException(nameof(sourceFormat));
            _resampleRatio = (double)_sourceFormat.SampleRate / TargetSampleRate;
        }

        /// <summary>
        /// Resamples incoming WASAPI buffer into 16kHz 16-bit Mono PCM samples,
        /// while calculating peak and RMS levels for live UI visualization.
        /// </summary>
        public (byte[] pcmBytes, float peak, float rms) ProcessBuffer(byte[] rawBuffer, int bytesRecorded)
        {
            if (bytesRecorded <= 0 || rawBuffer == null)
            {
                return (Array.Empty<byte>(), 0f, 0f);
            }

            int channels = _sourceFormat.Channels;
            bool isFloat = _sourceFormat.Encoding == WaveFormatEncoding.IeeeFloat || _sourceFormat.BitsPerSample == 32;
            int bytesPerSample = _sourceFormat.BitsPerSample / 8;
            int frameSize = bytesPerSample * channels;

            if (frameSize <= 0) return (Array.Empty<byte>(), 0f, 0f);

            int sourceFrames = bytesRecorded / frameSize;
            if (sourceFrames <= 0) return (Array.Empty<byte>(), 0f, 0f);

            // Step 1: Extract Mono 32-bit float samples from source
            float[] monoSamples = new float[sourceFrames];
            float maxSample = 0f;
            double sumSquares = 0.0;

            int byteIndex = 0;
            for (int i = 0; i < sourceFrames; i++)
            {
                float channelSum = 0f;

                for (int ch = 0; ch < channels; ch++)
                {
                    float sample = 0f;
                    if (isFloat && _sourceFormat.BitsPerSample == 32)
                    {
                        sample = BitConverter.ToSingle(rawBuffer, byteIndex);
                        byteIndex += 4;
                    }
                    else if (_sourceFormat.BitsPerSample == 16)
                    {
                        short s = BitConverter.ToInt16(rawBuffer, byteIndex);
                        sample = s / 32768.0f;
                        byteIndex += 2;
                    }
                    else if (_sourceFormat.BitsPerSample == 24)
                    {
                        int s = (rawBuffer[byteIndex + 2] << 16) | (rawBuffer[byteIndex + 1] << 8) | rawBuffer[byteIndex];
                        if ((s & 0x800000) != 0) s |= unchecked((int)0xFF000000);
                        sample = s / 8388608.0f;
                        byteIndex += 3;
                    }
                    else
                    {
                        byteIndex += bytesPerSample;
                    }

                    channelSum += sample;
                }

                float mono = channelSum / channels;
                monoSamples[i] = mono;

                float absVal = Math.Abs(mono);
                if (absVal > maxSample) maxSample = absVal;
                sumSquares += mono * mono;
            }

            float peak = Math.Min(1.0f, maxSample);
            float rms = (float)Math.Sqrt(sumSquares / sourceFrames);

            // Step 2: Linear Resample to 16,000 Hz Mono 16-bit PCM
            int estimatedTargetSamples = (int)Math.Ceiling(sourceFrames / _resampleRatio) + 2;
            using var ms = new MemoryStream(estimatedTargetSamples * 2);
            using var writer = new BinaryWriter(ms);

            while (_resampleFraction < sourceFrames - 1)
            {
                int index0 = (int)_resampleFraction;
                int index1 = Math.Min(index0 + 1, sourceFrames - 1);
                double t = _resampleFraction - index0;

                float interpolated = (float)((1.0 - t) * monoSamples[index0] + t * monoSamples[index1]);
                
                // Clamp and convert to 16-bit integer
                interpolated = Math.Clamp(interpolated, -1.0f, 1.0f);
                short pcm16 = (short)(interpolated * 32767.0f);
                writer.Write(pcm16);

                _resampleFraction += _resampleRatio;
            }

            _resampleFraction -= sourceFrames;
            if (_resampleFraction < 0.0) _resampleFraction = 0.0;

            return (ms.ToArray(), peak, rms);
        }

        /// <summary>
        /// Encapsulates raw 16kHz 16-bit mono PCM bytes into a standard WAV stream header.
        /// </summary>
        public static byte[] CreateWavBytes(byte[] pcmData)
        {
            using var ms = new MemoryStream(44 + pcmData.Length);
            using var writer = new BinaryWriter(ms);

            // RIFF header
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + pcmData.Length);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));

            // fmt sub-chunk
            writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16); // subchunk1 size (16 for PCM)
            writer.Write((short)1); // AudioFormat (1 = PCM)
            writer.Write((short)TargetChannels); // 1 channel
            writer.Write(TargetSampleRate); // 16000
            writer.Write(TargetSampleRate * TargetChannels * (TargetBitsPerSample / 8)); // ByteRate: 32000
            writer.Write((short)(TargetChannels * (TargetBitsPerSample / 8))); // BlockAlign: 2
            writer.Write((short)TargetBitsPerSample); // 16 bits

            // data sub-chunk
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            writer.Write(pcmData.Length);
            writer.Write(pcmData);

            return ms.ToArray();
        }
    }
}
