using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MentionedMe.Models;

namespace MentionedMe.Services
{
    public interface IMobileNotificationService
    {
        bool IsPaired { get; }
        string PairedDeviceName { get; }
        bool IsPro { get; }
        string RelayUrl { get; set; }
        string ProLicenseKey { get; set; }
        bool MobileSyncEnabled { get; set; }

        event EventHandler<string>? StatusChanged;

        Task<(bool success, string message, string deviceName, bool isPro)> PairDeviceAsync(string pairCode, string proLicenseKey);
        Task<bool> SendMentionNotificationAsync(MentionItem item);
        Task<bool> UnlinkDeviceAsync();
        Task<bool> ClearRemoteMentionsAsync();
    }

    public class MobileNotificationService : IMobileNotificationService
    {
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
        private readonly IUserSettingsService _settingsService;

        public bool IsPaired => !string.IsNullOrWhiteSpace(_settingsService.CurrentSettings.MobileSyncToken);
        public string PairedDeviceName => _settingsService.CurrentSettings.PairedDeviceName;
        public bool IsPro => _settingsService.CurrentSettings.IsPro;
        
        public string RelayUrl
        {
            get => _settingsService.CurrentSettings.RelayServerUrl;
            set
            {
                _settingsService.CurrentSettings.RelayServerUrl = value;
                _settingsService.SaveSettings();
            }
        }

        public string ProLicenseKey
        {
            get => _settingsService.CurrentSettings.ProLicenseKey;
            set
            {
                _settingsService.CurrentSettings.ProLicenseKey = value;
                _settingsService.SaveSettings();
            }
        }

        public bool MobileSyncEnabled
        {
            get => _settingsService.CurrentSettings.MobileSyncEnabled;
            set
            {
                _settingsService.CurrentSettings.MobileSyncEnabled = value;
                _settingsService.SaveSettings();
            }
        }

        public event EventHandler<string>? StatusChanged;

        public MobileNotificationService(IUserSettingsService settingsService)
        {
            _settingsService = settingsService;
        }

        public async Task<(bool success, string message, string deviceName, bool isPro)> PairDeviceAsync(string pairCode, string proLicenseKey)
        {
            if (string.IsNullOrWhiteSpace(pairCode))
            {
                return (false, "Please enter a valid pairing code from your mobile app.", string.Empty, false);
            }

            var baseUrl = (RelayUrl ?? "http://localhost:3000").TrimEnd('/');
            var endpoint = $"{baseUrl}/api/pair/claim";

            try
            {
                var payload = new
                {
                    pairCode = pairCode.Trim().ToUpper(),
                    proLicenseKey = (proLicenseKey ?? ProLicenseKey)?.Trim()
                };

                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(endpoint, content);

                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    string errorMsg = "Pairing failed.";
                    try
                    {
                        using var doc = JsonDocument.Parse(responseBody);
                        if (doc.RootElement.TryGetProperty("error", out var errProp))
                        {
                            errorMsg = errProp.GetString() ?? errorMsg;
                        }
                    }
                    catch { }

                    return (false, errorMsg, string.Empty, false);
                }

                using var jsonDoc = JsonDocument.Parse(responseBody);
                var root = jsonDoc.RootElement;

                var syncToken = root.GetProperty("syncToken").GetString() ?? string.Empty;
                var deviceName = root.GetProperty("deviceName").GetString() ?? "Mobile Device";
                var isPro = root.TryGetProperty("isPro", out var proProp) && proProp.GetBoolean();

                _settingsService.CurrentSettings.MobileSyncToken = syncToken;
                _settingsService.CurrentSettings.PairedDeviceName = deviceName;
                _settingsService.CurrentSettings.IsPro = isPro;
                _settingsService.CurrentSettings.ProLicenseKey = proLicenseKey ?? ProLicenseKey;
                _settingsService.SaveSettings();

                Debug.WriteLine($"[DIAG] 📱 Mobile Paired Successfully: {deviceName} (Pro: {isPro})");
                StatusChanged?.Invoke(this, $"Linked to {deviceName}");

                return (true, $"Successfully linked to {deviceName}!", deviceName, isPro);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DIAG] ❌ Mobile pairing error: {ex.Message}");
                return (false, $"Could not connect to relay server: {ex.Message}", string.Empty, false);
            }
        }

        public async Task<bool> SendMentionNotificationAsync(MentionItem item)
        {
            if (!MobileSyncEnabled || !IsPaired || item == null)
            {
                return false;
            }

            var syncToken = _settingsService.CurrentSettings.MobileSyncToken;
            if (string.IsNullOrWhiteSpace(syncToken)) return false;

            var baseUrl = (RelayUrl ?? "http://localhost:3000").TrimEnd('/');
            var endpoint = $"{baseUrl}/api/notify";

            try
            {
                var payload = new
                {
                    syncToken = syncToken,
                    matchedName = item.MatchedName,
                    sentence = item.Sentence,
                    timestamp = item.FormattedTime,
                    audioSource = item.AudioSource
                };

                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                // Asynchronous fire-and-forget style dispatch
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var response = await _httpClient.PostAsync(endpoint, content);
                        if (response.IsSuccessStatusCode)
                        {
                            Debug.WriteLine($"[DIAG] 📱 Push notification sent to {PairedDeviceName} for '{item.MatchedName}'");
                        }
                        else
                        {
                            Debug.WriteLine($"[DIAG] ⚠️ Push relay returned status: {response.StatusCode}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[DIAG] ⚠️ Push dispatch exception: {ex.Message}");
                    }
                });

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DIAG] ⚠️ Failed to initiate push notification: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> UnlinkDeviceAsync()
        {
            var syncToken = _settingsService.CurrentSettings.MobileSyncToken;
            _settingsService.CurrentSettings.MobileSyncToken = string.Empty;
            _settingsService.CurrentSettings.PairedDeviceName = string.Empty;
            _settingsService.SaveSettings();

            if (!string.IsNullOrWhiteSpace(syncToken))
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var baseUrl = (RelayUrl ?? "http://localhost:3000").TrimEnd('/');
                        var endpoint = $"{baseUrl}/api/pair/unlink";
                        var content = new StringContent(JsonSerializer.Serialize(new { syncToken }), Encoding.UTF8, "application/json");
                        await _httpClient.PostAsync(endpoint, content);
                    }
                    catch { }
                });
            }

            Debug.WriteLine("[DIAG] 📱 Mobile Device Unlinked.");
            StatusChanged?.Invoke(this, "Device unlinked.");

            return true;
        }

        public async Task<bool> ClearRemoteMentionsAsync()
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var baseUrl = (RelayUrl ?? "http://localhost:3000").TrimEnd('/');
                    var endpoint = $"{baseUrl}/api/mentions/clear";
                    var content = new StringContent("{}", Encoding.UTF8, "application/json");
                    await _httpClient.PostAsync(endpoint, content);
                }
                catch { }
            });

            return true;
        }
    }
}
