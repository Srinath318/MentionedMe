using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using MentionedMe.Models;
using MentionedMe.Services;

namespace MentionedMe.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged, IAsyncDisposable
    {
        private readonly IUserSettingsService _settingsService;
        private readonly IModelDownloadService _modelDownloadService;
        private readonly IAudioCaptureService _audioCaptureService;
        private readonly ITranscriptionService _transcriptionService;
        private readonly IMentionDetectionService _mentionDetectionService;
        private readonly INotificationService _notificationService;
        private readonly IMobileNotificationService _mobileNotificationService;

        private ListeningState _state = ListeningState.Stopped;
        private string _statusText = "Ready to listen";
        private string _userName = string.Empty;
        private string _aliases = string.Empty;
        private const string ModelFileName = "ggml-base.en.bin";
        private string _selectedModel = ModelFileName;
        private float _vadSensitivity = 0.001f;
        private bool _playNotificationSound = true;
        private bool _keepWindowAlwaysOnTop = false;
        private float _audioLevel = 0f;
        private bool _isSpeechDetected = false;
        private bool _isSettingsOpen = false;
        private bool _isDownloadingModel = false;
        private double _downloadProgress = 0.0;
        private string _downloadStatusText = string.Empty;
        private string _activeDeviceName = "System Audio (Default Output)";
        private string _liveTranscriptSnippet = string.Empty;
        private string _errorMessage = string.Empty;
        private bool _isEditPopupOpen = false;
        private string _editingOriginalName = string.Empty;
        private string _editingNameText = string.Empty;
        private bool _isLiveTranscriptVisible = false;

        // Mobile Companion Properties
        private string _pairingCodeInput = string.Empty;
        private string _proLicenseKeyInput = string.Empty;
        private string _mobilePairingStatusText = string.Empty;
        private bool _isPairingInProgress = false;

        public event PropertyChangedEventHandler? PropertyChanged;

        public ObservableCollection<MentionItem> RecentMentions { get; } = new ObservableCollection<MentionItem>();
        public ObservableCollection<string> LiveTranscriptLog { get; } = new ObservableCollection<string>();

        #region Properties

        public ListeningState State
        {
            get => _state;
            set
            {
                if (_state != value)
                {
                    _state = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsListening));
                    OnPropertyChanged(nameof(IsStopped));
                    OnPropertyChanged(nameof(CanToggleListening));
                }
            }
        }

        public bool IsListening => State == ListeningState.Listening;
        public bool IsStopped => State == ListeningState.Stopped;
        public bool CanToggleListening => State != ListeningState.Initializing && State != ListeningState.DownloadingModel;

        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(); }
        }

        public ObservableCollection<string> TargetNames { get; } = new ObservableCollection<string>();
        public ObservableCollection<string> AdditionalNamesList { get; } = new ObservableCollection<string>();

        private string _newNameInput = string.Empty;
        public string NewNameInput
        {
            get => _newNameInput;
            set
            {
                _newNameInput = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanAddName));
            }
        }

        private string _newAdditionalNameInput = string.Empty;
        public string NewAdditionalNameInput
        {
            get => _newAdditionalNameInput;
            set
            {
                _newAdditionalNameInput = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanAddAdditionalName));
            }
        }

        private string _primaryName = string.Empty;

        public bool CanAddName => !string.IsNullOrWhiteSpace(NewNameInput) && TargetNames.Count < 5;
        public bool CanAddMoreNames => TargetNames.Count < 5;
        public string TargetNamesCountText => $"{TargetNames.Count}/5";

        public bool CanAddAdditionalName => !string.IsNullOrWhiteSpace(NewAdditionalNameInput) && AdditionalNamesList.Count < 4 && TargetNames.Count < 5;
        public bool CanAddMoreAdditionalNames => AdditionalNamesList.Count < 4 && TargetNames.Count < 5;
        public string AdditionalNamesCountText => $"{AdditionalNamesList.Count}/4";

        public string PrimaryName
        {
            get => _primaryName;
            set
            {
                if (_primaryName != value)
                {
                    _primaryName = value;
                    OnPropertyChanged();
                    RebuildTargetNamesFromSettings();
                }
            }
        }

        public string AdditionalNames
        {
            get => string.Join(", ", AdditionalNamesList);
            set
            {
                // Kept for backward compatibility
            }
        }

        public void SyncSettingsFromTargetNames()
        {
            _primaryName = TargetNames.FirstOrDefault() ?? string.Empty;
            AdditionalNamesList.Clear();
            foreach (var n in TargetNames.Skip(1).Take(4))
            {
                AdditionalNamesList.Add(n);
            }
            OnPropertyChanged(nameof(PrimaryName));
            OnPropertyChanged(nameof(AdditionalNames));
            OnPropertyChanged(nameof(CanAddAdditionalName));
            OnPropertyChanged(nameof(CanAddMoreAdditionalNames));
            OnPropertyChanged(nameof(AdditionalNamesCountText));
            OnPropertyChanged(nameof(TargetNamesCountText));
        }

        private void RebuildTargetNamesFromSettings()
        {
            var list = new List<string>();
            var p = _primaryName?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(p))
            {
                list.Add(p);
            }

            foreach (var a in AdditionalNamesList)
            {
                var trimmed = a.Trim();
                if (!string.IsNullOrWhiteSpace(trimmed) && list.Count < 5)
                {
                    if (!list.Any(n => string.Equals(n, trimmed, StringComparison.OrdinalIgnoreCase)))
                    {
                        list.Add(trimmed);
                    }
                }
            }

            TargetNames.Clear();
            foreach (var name in list)
            {
                TargetNames.Add(name);
            }

            SaveCurrentSettings();
            UpdateTargetNames();
            OnPropertyChanged(nameof(CanAddName));
            OnPropertyChanged(nameof(CanAddMoreNames));
            OnPropertyChanged(nameof(TargetNamesCountText));
            OnPropertyChanged(nameof(IsNameValid));
            OnPropertyChanged(nameof(CanAddAdditionalName));
            OnPropertyChanged(nameof(CanAddMoreAdditionalNames));
            OnPropertyChanged(nameof(AdditionalNamesCountText));
        }

        public void AddAdditionalName(string? customName = null)
        {
            var input = customName ?? NewAdditionalNameInput;
            if (string.IsNullOrWhiteSpace(input)) return;
            var trimmed = input.Trim();

            if (AdditionalNamesList.Count < 4 && TargetNames.Count < 5)
            {
                if (!string.Equals(trimmed, PrimaryName, StringComparison.OrdinalIgnoreCase) &&
                    !AdditionalNamesList.Any(n => string.Equals(n, trimmed, StringComparison.OrdinalIgnoreCase)))
                {
                    AdditionalNamesList.Add(trimmed);
                    RebuildTargetNamesFromSettings();
                    OnPropertyChanged(nameof(CanAddAdditionalName));
                    OnPropertyChanged(nameof(CanAddMoreAdditionalNames));
                    OnPropertyChanged(nameof(AdditionalNamesCountText));
                }
            }

            NewAdditionalNameInput = string.Empty;
        }

        public void RemoveAdditionalName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            var existing = AdditionalNamesList.FirstOrDefault(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                AdditionalNamesList.Remove(existing);
                RebuildTargetNamesFromSettings();
                OnPropertyChanged(nameof(CanAddAdditionalName));
                OnPropertyChanged(nameof(CanAddMoreAdditionalNames));
                OnPropertyChanged(nameof(AdditionalNamesCountText));
            }
        }

        public string UserName
        {
            get => PrimaryName;
            set => PrimaryName = value;
        }

        public string Aliases
        {
            get => AdditionalNames;
            set => AdditionalNames = value;
        }

        public bool IsNameValid => TargetNames.Count > 0;

        public string SelectedModel
        {
            get => _selectedModel;
            set
            {
                if (_selectedModel != value)
                {
                    _selectedModel = value;
                    OnPropertyChanged();
                    SaveCurrentSettings();
                }
            }
        }

        public float VadSensitivity
        {
            get => _vadSensitivity;
            set
            {
                if (Math.Abs(_vadSensitivity - value) > 0.0001f)
                {
                    _vadSensitivity = value;
                    OnPropertyChanged();
                    SaveCurrentSettings();
                }
            }
        }

        public bool PlayNotificationSound
        {
            get => _playNotificationSound;
            set
            {
                if (_playNotificationSound != value)
                {
                    _playNotificationSound = value;
                    OnPropertyChanged();
                    SaveCurrentSettings();
                }
            }
        }

        public bool KeepWindowAlwaysOnTop
        {
            get => _keepWindowAlwaysOnTop;
            set
            {
                if (_keepWindowAlwaysOnTop != value)
                {
                    _keepWindowAlwaysOnTop = value;
                    OnPropertyChanged();
                    SaveCurrentSettings();
                }
            }
        }

        public float AudioLevel
        {
            get => _audioLevel;
            set { _audioLevel = value; OnPropertyChanged(); }
        }

        public bool IsSpeechDetected
        {
            get => _isSpeechDetected;
            set { _isSpeechDetected = value; OnPropertyChanged(); }
        }

        public bool IsSettingsOpen
        {
            get => _isSettingsOpen;
            set { _isSettingsOpen = value; OnPropertyChanged(); }
        }

        public bool IsDownloadingModel
        {
            get => _isDownloadingModel;
            set { _isDownloadingModel = value; OnPropertyChanged(); }
        }

        public double DownloadProgress
        {
            get => _downloadProgress;
            set { _downloadProgress = value; OnPropertyChanged(); }
        }

        public string DownloadStatusText
        {
            get => _downloadStatusText;
            set { _downloadStatusText = value; OnPropertyChanged(); }
        }

        public string ActiveDeviceName
        {
            get => _activeDeviceName;
            set { _activeDeviceName = value; OnPropertyChanged(); }
        }

        public string LiveTranscriptSnippet
        {
            get => _liveTranscriptSnippet;
            set { _liveTranscriptSnippet = value; OnPropertyChanged(); }
        }

        public bool IsLiveTranscriptVisible
        {
            get => _isLiveTranscriptVisible;
            set
            {
                if (_isLiveTranscriptVisible != value)
                {
                    _isLiveTranscriptVisible = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(LiveTranscriptButtonText));
                }
            }
        }

        public string LiveTranscriptButtonText => IsLiveTranscriptVisible ? "Hide live transcription" : "Show live transcription";

        public string ErrorMessage
        {
            get => _errorMessage;
            set
            {
                _errorMessage = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasError));
            }
        }

        public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

        public int TotalMentionsCount => RecentMentions.Count;

        public bool IsEditPopupOpen
        {
            get => _isEditPopupOpen;
            set { _isEditPopupOpen = value; OnPropertyChanged(); }
        }

        public string EditingOriginalName
        {
            get => _editingOriginalName;
            set { _editingOriginalName = value; OnPropertyChanged(); }
        }

        public string EditingNameText
        {
            get => _editingNameText;
            set { _editingNameText = value; OnPropertyChanged(); }
        }

        // Mobile Companion Sync
        public bool MobileSyncEnabled
        {
            get => _mobileNotificationService.MobileSyncEnabled;
            set
            {
                if (_mobileNotificationService.MobileSyncEnabled != value)
                {
                    _mobileNotificationService.MobileSyncEnabled = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsMobilePaired => _mobileNotificationService.IsPaired;
        public string PairedDeviceName => _mobileNotificationService.PairedDeviceName;
        public bool IsProActive => _mobileNotificationService.IsPro;

        public string PairingCodeInput
        {
            get => _pairingCodeInput;
            set { _pairingCodeInput = value; OnPropertyChanged(); }
        }

        public string ProLicenseKeyInput
        {
            get => _proLicenseKeyInput;
            set { _proLicenseKeyInput = value; OnPropertyChanged(); }
        }

        public string MobilePairingStatusText
        {
            get => _mobilePairingStatusText;
            set { _mobilePairingStatusText = value; OnPropertyChanged(); }
        }

        public bool IsPairingInProgress
        {
            get => _isPairingInProgress;
            set { _isPairingInProgress = value; OnPropertyChanged(); }
        }

        #endregion

        #region Commands

        public ICommand ToggleListeningCommand { get; }
        public ICommand StartListeningCommand { get; }
        public ICommand StopListeningCommand { get; }
        public ICommand ToggleSettingsCommand { get; }
        public ICommand ClearMentionsCommand { get; }
        public ICommand CopyMentionCommand { get; }
        public ICommand TestNotificationCommand { get; }
        public ICommand DismissErrorCommand { get; }
        public ICommand AddNameCommand { get; }
        public ICommand RemoveNameCommand { get; }
        public ICommand AddAdditionalNameCommand { get; }
        public ICommand RemoveAdditionalNameCommand { get; }
        public ICommand EditNameCommand { get; }
        public ICommand SaveEditedNameCommand { get; }
        public ICommand CancelEditNameCommand { get; }
        public ICommand ToggleLiveTranscriptCommand { get; }
        public ICommand CopyTranscriptCommand { get; }

        public ICommand PairMobileCommand { get; }
        public ICommand UnlinkMobileCommand { get; }
        public ICommand SaveProKeyCommand { get; }

        #endregion

        public MainViewModel(
            IUserSettingsService settingsService,
            IModelDownloadService modelDownloadService,
            IAudioCaptureService audioCaptureService,
            ITranscriptionService transcriptionService,
            IMentionDetectionService mentionDetectionService,
            INotificationService notificationService,
            IMobileNotificationService mobileNotificationService)
        {
            _settingsService = settingsService;
            _modelDownloadService = modelDownloadService;
            _audioCaptureService = audioCaptureService;
            _transcriptionService = transcriptionService;
            _mentionDetectionService = mentionDetectionService;
            _notificationService = notificationService;
            _mobileNotificationService = mobileNotificationService;

            _proLicenseKeyInput = _mobileNotificationService.ProLicenseKey;
            _mobileNotificationService.StatusChanged += (s, msg) =>
            {
                RunOnUI(() =>
                {
                    OnPropertyChanged(nameof(IsMobilePaired));
                    OnPropertyChanged(nameof(PairedDeviceName));
                    OnPropertyChanged(nameof(IsProActive));
                    MobilePairingStatusText = msg;
                });
            };

            // Load saved settings
            var s = _settingsService.CurrentSettings;
            _userName = s.UserName;
            _aliases = s.Aliases;
            _selectedModel = ModelFileName; // Always use small.en
            _vadSensitivity = s.VadSensitivity;
            _playNotificationSound = s.PlayNotificationSound;
            _keepWindowAlwaysOnTop = s.KeepWindowAlwaysOnTop;

            TargetNames.Clear();
            var loadedNames = s.GetAllTargetNames();
            if (loadedNames.Count == 0)
            {
                loadedNames = new List<string>();
            }
            foreach (var n in loadedNames)
            {
                TargetNames.Add(n);
            }

            UpdateTargetNames();

            // Wire pipeline events
            _audioCaptureService.AudioLevelChanged += OnAudioLevelChanged;
            _audioCaptureService.SpeechSegmentAvailable += OnSpeechSegmentAvailable;
            _audioCaptureService.CaptureError += OnAudioCaptureError;

            _transcriptionService.TranscriptSegmentReceived += OnTranscriptSegmentReceived;
            _transcriptionService.TranscriptionError += OnTranscriptionError;

            _mentionDetectionService.MentionDetected += OnMentionDetected;

            // Commands
            ToggleListeningCommand = new RelayCommand(async () => await ToggleListeningAsync());
            StartListeningCommand = new RelayCommand(async () => await StartListeningAsync());
            StopListeningCommand = new RelayCommand(() => StopListening());

            PairMobileCommand = new RelayCommand(async () => await PairMobileAsync());
            UnlinkMobileCommand = new RelayCommand(async () => await UnlinkMobileAsync());
            SaveProKeyCommand = new RelayCommand(() => SaveProKey());
            ToggleSettingsCommand = new RelayCommand(() =>
            {
                if (!IsSettingsOpen)
                {
                    SyncSettingsFromTargetNames();
                }
                IsSettingsOpen = !IsSettingsOpen;
            });
            ClearMentionsCommand = new RelayCommand(ClearMentions);
            CopyMentionCommand = new RelayCommand(CopyMention);
            TestNotificationCommand = new RelayCommand(SendTestNotification);
            DismissErrorCommand = new RelayCommand(() => ErrorMessage = string.Empty);
            AddNameCommand = new RelayCommand(() => AddName());
            RemoveNameCommand = new RelayCommand(p => RemoveName(p as string));
            AddAdditionalNameCommand = new RelayCommand(() => AddAdditionalName());
            RemoveAdditionalNameCommand = new RelayCommand(p => RemoveAdditionalName(p as string));
            EditNameCommand = new RelayCommand(p => OpenEditPopup(p as string));
            SaveEditedNameCommand = new RelayCommand(() => SaveEditedName());
            CancelEditNameCommand = new RelayCommand(() => IsEditPopupOpen = false);
            ToggleLiveTranscriptCommand = new RelayCommand(() => IsLiveTranscriptVisible = !IsLiveTranscriptVisible);
            CopyTranscriptCommand = new RelayCommand(CopyTranscript);

            SyncSettingsFromTargetNames();
            StatusText = string.Empty;
        }

        private void OnAudioLevelChanged(object? sender, AudioLevelEventArgs e)
        {
            // Smooth level decay
            float target = e.PeakLevel;
            float current = AudioLevel;
            float smoothed = target > current ? target : (current * 0.75f + target * 0.25f);

            RunOnUI(() =>
            {
                AudioLevel = smoothed;
                IsSpeechDetected = e.IsSpeechDetected;
            });
        }

        private void OnSpeechSegmentAvailable(object? sender, byte[] wavData)
        {
            _transcriptionService.EnqueueAudioSegment(wavData);
        }

        private void OnTranscriptSegmentReceived(object? sender, string transcript)
        {
            var cleaned = CleanTranscriptSnippet(transcript);
            if (!string.IsNullOrWhiteSpace(cleaned))
            {
                RunOnUI(() =>
                {
                    LiveTranscriptSnippet = $"\"{cleaned}\"";

                    // Append to live transcript log (avoid immediate consecutive duplicate lines)
                    var timestamp = DateTime.Now.ToString("HH:mm:ss");
                    var entry = $"[{timestamp}]  {cleaned}";
                    if (LiveTranscriptLog.Count == 0 || !LiveTranscriptLog.Last().EndsWith(cleaned, StringComparison.OrdinalIgnoreCase))
                    {
                        LiveTranscriptLog.Add(entry);
                        while (LiveTranscriptLog.Count > 200)
                        {
                            LiveTranscriptLog.RemoveAt(0);
                        }
                    }
                });
            }

            _mentionDetectionService.ProcessTranscript(transcript);
        }

        private static string CleanTranscriptSnippet(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

            // Remove non-speech bracketed tags: [Music], (music), [Applause], [Silence], ♪, etc.
            var withoutTags = System.Text.RegularExpressions.Regex.Replace(raw, @"\[.*?\]|\(.*?\)|[*_♪#]+", " ").Trim();
            var normalized = System.Text.RegularExpressions.Regex.Replace(withoutTags, @"\s+", " ").Trim();

            // If only punctuation remains, ignore
            if (System.Text.RegularExpressions.Regex.IsMatch(normalized, @"^[^\w\d]+$"))
            {
                return string.Empty;
            }

            return normalized;
        }

        private void OnMentionDetected(object? sender, MentionItem mention)
        {
            System.Diagnostics.Debug.WriteLine($"[DIAG] 🔔 NOTIFICATION FIRED: '{mention.MatchedName}' at {DateTime.Now:HH:mm:ss.fff}");

            RunOnUI(() =>
            {
                RecentMentions.Insert(0, mention);
                OnPropertyChanged(nameof(TotalMentionsCount));
            });

            _notificationService.ShowMentionNotification(mention, PlayNotificationSound);
            _ = _mobileNotificationService.SendMentionNotificationAsync(mention);
        }

        private void OnAudioCaptureError(object? sender, string error)
        {
            RunOnUI(() =>
            {
                ErrorMessage = error;
                StopListening();
                StatusText = "Audio device disconnected. Click Start Listening to resume.";
            });
        }

        private void OnTranscriptionError(object? sender, string error)
        {
            RunOnUI(() =>
            {
                ErrorMessage = error;
            });
        }

        public async Task ToggleListeningAsync()
        {
            if (IsListening)
            {
                StopListening();
            }
            else
            {
                StopListening();
                await StartListeningAsync();
            }
        }

        public async Task StartListeningAsync()
        {
            if (string.IsNullOrWhiteSpace(UserName))
            {
                ErrorMessage = "Please enter your name first so Mentioned Me? knows what to listen for.";
                return;
            }

            ErrorMessage = string.Empty;
            State = ListeningState.Initializing;
            StatusText = "Preparing speech model...";

            try
            {
                // Ensure model exists
                var modelFile = SelectedModel;
                if (!_modelDownloadService.IsModelAvailable(modelFile))
                {
                    State = ListeningState.DownloadingModel;
                    IsDownloadingModel = true;
                    DownloadProgress = 0.0;
                    DownloadStatusText = $"Downloading Whisper model ({modelFile})...";

                    var progress = new Progress<double>(p =>
                    {
                        RunOnUI(() =>
                        {
                            DownloadProgress = p;
                            DownloadStatusText = $"Downloading Whisper model ({modelFile}) - {p:0}%";
                        });
                    });

                    await _modelDownloadService.EnsureModelAvailableAsync(modelFile, progress);
                    IsDownloadingModel = false;
                }

                StatusText = "Loading model into memory...";
                var modelPath = _modelDownloadService.GetModelPath(modelFile);
                var allNames = _settingsService.CurrentSettings.GetAllTargetNames();
                var prompt = allNames.Count > 0 ? string.Join(", ", allNames) + "." : null;
                await _transcriptionService.LoadModelAsync(modelPath, prompt);

                // Start WASAPI loopback
                StatusText = "Starting system audio loopback...";
                _audioCaptureService.StartCapture(VadSensitivity);
                ActiveDeviceName = _audioCaptureService.DeviceFriendlyName;

                State = ListeningState.Listening;
                StatusText = $"Listening to {ActiveDeviceName}";
                LiveTranscriptSnippet = "Listening for mentions...";
            }
            catch (Exception ex)
            {
                IsDownloadingModel = false;
                State = ListeningState.Error;
                ErrorMessage = $"Failed to start listening: {ex.Message}";
                StatusText = "Error starting listening";
            }
        }

        public void StopListening()
        {
            _audioCaptureService.StopCapture();
            _transcriptionService.StopProcessing();
            State = ListeningState.Stopped;
            StatusText = string.Empty;
            AudioLevel = 0f;
            IsSpeechDetected = false;
            LiveTranscriptSnippet = string.Empty;
            IsLiveTranscriptVisible = false;
            LiveTranscriptLog.Clear();
        }

        private void ClearMentions()
        {
            RecentMentions.Clear();
            _mentionDetectionService.ResetDeduplicationHistory();
            OnPropertyChanged(nameof(TotalMentionsCount));
            _ = _mobileNotificationService.ClearRemoteMentionsAsync();
        }

        private void CopyMention(object? parameter)
        {
            if (parameter is MentionItem item && !string.IsNullOrWhiteSpace(item.Sentence))
            {
                try
                {
                    Clipboard.SetText(item.Sentence);
                }
                catch { }
            }
        }

        private void CopyTranscript()
        {
            if (LiveTranscriptLog.Count == 0) return;
            try
            {
                var fullText = string.Join(Environment.NewLine, LiveTranscriptLog);
                Clipboard.SetText(fullText);
            }
            catch { }
        }

        public void AddName(string? customName = null)
        {
            var input = customName ?? NewNameInput;
            if (string.IsNullOrWhiteSpace(input)) return;

            var splits = input.Split(new[] { ',', ';', '/', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            bool changed = false;

            foreach (var raw in splits)
            {
                var trimmed = raw.Trim();
                if (!string.IsNullOrWhiteSpace(trimmed) && TargetNames.Count < 5)
                {
                    if (!TargetNames.Any(n => string.Equals(n, trimmed, StringComparison.OrdinalIgnoreCase)))
                    {
                        TargetNames.Add(trimmed);
                        changed = true;
                    }
                }
            }

            if (changed)
            {
                SaveCurrentSettings();
                UpdateTargetNames();
                SyncSettingsFromTargetNames();
                OnPropertyChanged(nameof(CanAddName));
                OnPropertyChanged(nameof(CanAddMoreNames));
                OnPropertyChanged(nameof(TargetNamesCountText));
                OnPropertyChanged(nameof(IsNameValid));
            }

            NewNameInput = string.Empty;
        }

        public void RemoveName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            var existing = TargetNames.FirstOrDefault(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                TargetNames.Remove(existing);
                SaveCurrentSettings();
                UpdateTargetNames();
                SyncSettingsFromTargetNames();
                OnPropertyChanged(nameof(CanAddName));
                OnPropertyChanged(nameof(CanAddMoreNames));
                OnPropertyChanged(nameof(TargetNamesCountText));
                OnPropertyChanged(nameof(IsNameValid));
            }
        }

        private void OpenEditPopup(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            EditingOriginalName = name;
            EditingNameText = name;
            IsEditPopupOpen = true;
        }

        private void SaveEditedName()
        {
            var trimmed = EditingNameText?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                // If cleared, just remove it
                RemoveName(EditingOriginalName);
                IsEditPopupOpen = false;
                return;
            }

            // Find the original and replace it
            var idx = -1;
            for (int i = 0; i < TargetNames.Count; i++)
            {
                if (string.Equals(TargetNames[i], EditingOriginalName, StringComparison.OrdinalIgnoreCase))
                {
                    idx = i;
                    break;
                }
            }

            if (idx >= 0)
            {
                // Check for duplicates (don't allow editing to a name that already exists)
                bool isDuplicate = false;
                for (int i = 0; i < TargetNames.Count; i++)
                {
                    if (i != idx && string.Equals(TargetNames[i], trimmed, StringComparison.OrdinalIgnoreCase))
                    {
                        isDuplicate = true;
                        break;
                    }
                }
                if (!isDuplicate)
                {
                    TargetNames[idx] = trimmed;
                    SaveCurrentSettings();
                    UpdateTargetNames();
                    SyncSettingsFromTargetNames();
                    OnPropertyChanged(nameof(IsNameValid));
                }
            }

            IsEditPopupOpen = false;
        }

        private void SendTestNotification()
        {
            var testName = TargetNames.FirstOrDefault() ?? "You";
            var testItem = new MentionItem
            {
                Sentence = $"Hey {testName}, can you share your thoughts on the new release?",
                MatchedName = testName,
                AudioSource = "Test Notification",
                Timestamp = DateTime.Now
            };

            RecentMentions.Insert(0, testItem);
            OnPropertyChanged(nameof(TotalMentionsCount));
            _notificationService.ShowMentionNotification(testItem, PlayNotificationSound);
            _ = _mobileNotificationService.SendMentionNotificationAsync(testItem);
        }

        public async Task PairMobileAsync()
        {
            if (string.IsNullOrWhiteSpace(PairingCodeInput))
            {
                MobilePairingStatusText = "Please enter the pairing code from your phone app.";
                return;
            }

            IsPairingInProgress = true;
            MobilePairingStatusText = "Connecting to mobile device...";

            var result = await _mobileNotificationService.PairDeviceAsync(PairingCodeInput, ProLicenseKeyInput);

            IsPairingInProgress = false;
            MobilePairingStatusText = result.message;

            if (result.success)
            {
                PairingCodeInput = string.Empty;
                OnPropertyChanged(nameof(IsMobilePaired));
                OnPropertyChanged(nameof(PairedDeviceName));
                OnPropertyChanged(nameof(IsProActive));
            }
        }

        public async Task UnlinkMobileAsync()
        {
            await _mobileNotificationService.UnlinkDeviceAsync();
            OnPropertyChanged(nameof(IsMobilePaired));
            OnPropertyChanged(nameof(PairedDeviceName));
            MobilePairingStatusText = "Device unlinked.";
        }

        public void SaveProKey()
        {
            _mobileNotificationService.ProLicenseKey = ProLicenseKeyInput;
            MobilePairingStatusText = "Pro license key updated.";
            OnPropertyChanged(nameof(IsProActive));
        }

        private void SaveCurrentSettings()
        {
            var s = _settingsService.CurrentSettings;
            s.TargetNames = TargetNames.ToList();
            s.UserName = TargetNames.FirstOrDefault() ?? string.Empty;
            s.Aliases = string.Join(", ", TargetNames.Skip(1));
            s.SelectedModel = ModelFileName;
            s.VadSensitivity = VadSensitivity;
            s.PlayNotificationSound = PlayNotificationSound;
            s.KeepWindowAlwaysOnTop = KeepWindowAlwaysOnTop;
            s.MobileSyncEnabled = MobileSyncEnabled;
            s.ProLicenseKey = ProLicenseKeyInput;
            _settingsService.SaveSettings();
        }

        private void UpdateTargetNames()
        {
            _mentionDetectionService.UpdateTargetNames(TargetNames.ToList());
        }

        private void RunOnUI(Action action)
        {
            if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
            {
                Application.Current.Dispatcher.BeginInvoke(action);
            }
            else
            {
                action();
            }
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public async ValueTask DisposeAsync()
        {
            StopListening();
            _audioCaptureService.Dispose();
            await _transcriptionService.DisposeAsync();
        }
    }
}
