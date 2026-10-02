using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace MentionedMe.Services
{
    public interface IModelDownloadService
    {
        string ModelsDirectory { get; }
        string GetModelPath(string modelFileName);
        bool IsModelAvailable(string modelFileName);
        Task EnsureModelAvailableAsync(string modelFileName, IProgress<double>? progress = null, CancellationToken cancellationToken = default);
    }

    public class ModelDownloadService : IModelDownloadService
    {
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        private readonly string _modelsDirectory;

        public string ModelsDirectory => _modelsDirectory;

        public ModelDownloadService()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _modelsDirectory = Path.Combine(appData, "MentionedMe", "models");
            Directory.CreateDirectory(_modelsDirectory);
        }

        public string GetModelPath(string modelFileName)
        {
            if (string.IsNullOrWhiteSpace(modelFileName))
            {
                modelFileName = "ggml-base.en.bin";
            }
            return Path.Combine(_modelsDirectory, modelFileName);
        }

        public bool IsModelAvailable(string modelFileName)
        {
            var path = GetModelPath(modelFileName);
            if (!File.Exists(path)) return false;

            var fi = new FileInfo(path);
            // tiny.en is ~75MB, base.en is ~142MB. If < 10MB, it's incomplete.
            return fi.Length > 10 * 1024 * 1024;
        }

        public async Task EnsureModelAvailableAsync(string modelFileName, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
        {
            if (IsModelAvailable(modelFileName))
            {
                progress?.Report(100.0);
                return;
            }

            var destinationPath = GetModelPath(modelFileName);
            var tempPath = destinationPath + ".tmp";

            var downloadUrls = new[]
            {
                $"https://huggingface.co/ggerganov/whisper.cpp/resolve/main/{modelFileName}",
                $"https://github.com/ggerganov/whisper.cpp/raw/master/models/{modelFileName}"
            };

            Exception? lastEx = null;

            foreach (var url in downloadUrls)
            {
                try
                {
                    using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    response.EnsureSuccessStatusCode();

                    var totalBytes = response.Content.Headers.ContentLength ?? -1L;

                    await using (var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken))
                    await using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                    {
                        var buffer = new byte[81920];
                        long totalRead = 0;
                        int bytesRead;

                        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                            totalRead += bytesRead;

                            if (totalBytes > 0 && progress != null)
                            {
                                var percentage = (double)totalRead / totalBytes * 100.0;
                                progress.Report(percentage);
                            }
                        }
                    }

                    if (File.Exists(destinationPath))
                    {
                        File.Delete(destinationPath);
                    }
                    File.Move(tempPath, destinationPath);
                    progress?.Report(100.0);
                    return;
                }
                catch (Exception ex)
                {
                    lastEx = ex;
                    if (File.Exists(tempPath))
                    {
                        try { File.Delete(tempPath); } catch { }
                    }
                }
            }

            throw new InvalidOperationException($"Failed to download Whisper speech model '{modelFileName}'. Please check internet connection.", lastEx);
        }
    }
}
