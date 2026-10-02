using System;

namespace MentionedMe.Models
{
    public class AudioLevelEventArgs : EventArgs
    {
        public float PeakLevel { get; }
        public float RmsLevel { get; }
        public bool IsSpeechDetected { get; }

        public AudioLevelEventArgs(float peakLevel, float rmsLevel, bool isSpeechDetected)
        {
            PeakLevel = Math.Clamp(peakLevel, 0.0f, 1.0f);
            RmsLevel = Math.Clamp(rmsLevel, 0.0f, 1.0f);
            IsSpeechDetected = isSpeechDetected;
        }
    }
}
