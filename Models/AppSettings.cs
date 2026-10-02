using System;
using System.Collections.Generic;
using System.Linq;

namespace MentionedMe.Models
{
    public class AppSettings
    {
        public List<string> TargetNames { get; set; } = new List<string>();
        public string UserName { get; set; } = string.Empty;
        public string Aliases { get; set; } = string.Empty;
        public string SelectedModel { get; set; } = "ggml-base.en.bin"; // Base English whisper model - 100ms ultra-fast real-time on CPU
        public float VadSensitivity { get; set; } = 0.001f; // Voice activity detection RMS threshold
        public bool PlayNotificationSound { get; set; } = true;
        public bool AutoStartListening { get; set; } = false;
        public bool KeepWindowAlwaysOnTop { get; set; } = false;

        // Mobile Companion Sync Settings
        public bool MobileSyncEnabled { get; set; } = true;
        public string MobileSyncToken { get; set; } = string.Empty;
        public string PairedDeviceName { get; set; } = string.Empty;
        public bool IsPro { get; set; } = true;
        public string ProLicenseKey { get; set; } = "MM-DEV-MASTER-SRINATH";
        public string RelayServerUrl { get; set; } = "http://localhost:3000";

        public List<string> GetAllTargetNames()
        {
            var list = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (TargetNames != null)
            {
                foreach (var name in TargetNames)
                {
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        list.Add(name.Trim());
                    }
                }
            }

            void AddSplits(string raw)
            {
                if (string.IsNullOrWhiteSpace(raw)) return;
                var splits = raw.Split(new[] { ',', ';', '/', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var s in splits)
                {
                    var trimmed = s.Trim();
                    if (!string.IsNullOrWhiteSpace(trimmed))
                    {
                        list.Add(trimmed);
                    }
                }
            }

            AddSplits(UserName);
            AddSplits(Aliases);

            return list.Take(5).ToList();
        }
    }
}
