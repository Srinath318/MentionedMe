using System;
using System.IO;
using System.Text.Json;
using MentionedMe.Models;

namespace MentionedMe.Services
{
    public interface IUserSettingsService
    {
        AppSettings CurrentSettings { get; }
        void LoadSettings();
        void SaveSettings();
        void UpdateSettings(AppSettings settings);
    }

    public class UserSettingsService : IUserSettingsService
    {
        private readonly string _settingsFilePath;
        private AppSettings _currentSettings;

        public AppSettings CurrentSettings => _currentSettings;

        public UserSettingsService()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var appFolder = Path.Combine(appData, "MentionedMe");
            Directory.CreateDirectory(appFolder);
            _settingsFilePath = Path.Combine(appFolder, "settings.json");
            _currentSettings = new AppSettings();
            LoadSettings();
        }

        public void LoadSettings()
        {
            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    var json = File.ReadAllText(_settingsFilePath);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json);
                    if (settings != null)
                    {
                        _currentSettings = settings;
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load settings: {ex.Message}");
            }

            _currentSettings = new AppSettings();
        }

        public void SaveSettings()
        {
            try
            {
                var json = JsonSerializer.Serialize(_currentSettings, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(_settingsFilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
            }
        }

        public void UpdateSettings(AppSettings settings)
        {
            _currentSettings = settings ?? new AppSettings();
            SaveSettings();
        }
    }
}
