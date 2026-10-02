using System;
using System.Windows;
using Microsoft.Toolkit.Uwp.Notifications;
using Wpf.Ui.Appearance;
using MentionedMe.Services;
using MentionedMe.ViewModels;

namespace MentionedMe
{
    public partial class App : Application
    {
        private MainViewModel? _mainViewModel;

        private void OnStartup(object sender, StartupEventArgs e)
        {
            // Apply Dark Theme Fluent Design by default
            ApplicationThemeManager.Apply(ApplicationTheme.Dark);

            // Register Toast Notification activation listener
            ToastNotificationManagerCompat.OnActivated += toastArgs =>
            {
                Current.Dispatcher.Invoke(() =>
                {
                    if (MainWindow != null)
                    {
                        if (MainWindow.WindowState == WindowState.Minimized)
                        {
                            MainWindow.WindowState = WindowState.Normal;
                        }
                        MainWindow.Activate();
                        MainWindow.Focus();
                    }
                });
            };

            // Initialize Services
            var settingsService = new UserSettingsService();
            var modelDownloadService = new ModelDownloadService();
            var audioCaptureService = new WasapiLoopbackService();
            var transcriptionService = new WhisperTranscriptionService();
            var mentionDetectionService = new MentionDetectionService();
            var notificationService = new WindowsNotificationService();
            var mobileNotificationService = new MobileNotificationService(settingsService);

            _mainViewModel = new MainViewModel(
                settingsService,
                modelDownloadService,
                audioCaptureService,
                transcriptionService,
                mentionDetectionService,
                notificationService,
                mobileNotificationService);

            var mainWindow = new MainWindow
            {
                DataContext = _mainViewModel
            };

            MainWindow = mainWindow;
            mainWindow.Show();

            // Auto-start if user configured it
            if (settingsService.CurrentSettings.AutoStartListening && !string.IsNullOrWhiteSpace(settingsService.CurrentSettings.UserName))
            {
                _ = _mainViewModel.StartListeningAsync();
            }
        }

        private async void OnExit(object sender, ExitEventArgs e)
        {
            ToastNotificationManagerCompat.Uninstall();
            if (_mainViewModel != null)
            {
                await _mainViewModel.DisposeAsync();
            }
        }
    }
}
