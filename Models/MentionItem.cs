using System;

namespace MentionedMe.Models
{
    public class MentionItem
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string FormattedTime => Timestamp.ToString("hh:mm:ss tt");
        public string RelativeTime
        {
            get
            {
                var span = DateTime.Now - Timestamp;
                if (span.TotalSeconds < 60) return "Just now";
                if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m ago";
                if (span.TotalHours < 24) return $"{(int)span.TotalHours}h ago";
                return Timestamp.ToString("MMM d, hh:mm tt");
            }
        }
        public string Sentence { get; set; } = string.Empty;
        public string MatchedName { get; set; } = string.Empty;
        public string AudioSource { get; set; } = "System Audio (Loopback)";
        public bool IsNew { get; set; } = true;
    }
}
