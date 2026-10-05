using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MovaCore.Models;

namespace MovaCore.Services
{
    /// <summary>
    /// Runs at most one model download in the background and reports its progress. It outlives the settings window,
    /// so a download keeps going when the window is closed.
    /// </summary>
    public sealed class ModelDownloadManager : IDisposable
    {
        private readonly ModelDownloader _downloader;
        private readonly object _lock = new();
        private CancellationTokenSource? _cts; // the current download's; a finished one's stays until the next Start
        private Task _current = Task.CompletedTask;
        private ModelDownloadState _state = ModelDownloadState.Idle;
        private readonly HashSet<string> _deleted = new(); // guarded by _lock

        public ModelDownloadManager(ModelDownloader downloader, string modelsDirectory)
        {
            _downloader = downloader;
            ModelsDirectory = modelsDirectory;
        }

        public string ModelsDirectory { get; }

        public ModelDownloadState State
        {
            get
            {
                lock (_lock) return _state;
            }
        }

        /// <summary>Raised on a worker thread whenever <see cref="State"/> changes, including progress.</summary>
        public event EventHandler<ModelDownloadState>? StateChanged;

        /// <summary>Raised on the calling thread after <see cref="Delete"/> removed a model.</summary>
        public event EventHandler<SpeechModelInfo>? ModelDeleted;

        public string PathOf(SpeechModelInfo model) => Path.Combine(ModelsDirectory, model.FileName);

        public bool IsDownloaded(SpeechModelInfo model) => File.Exists(PathOf(model));

        /// <summary>The size of the downloaded model, or null if it is not on disk.</summary>
        public long? SizeOnDisk(SpeechModelInfo model)
        {
            var file = new FileInfo(PathOf(model));
            return file.Exists ? file.Length : null;
        }

        /// <summary>
        /// Whether the user deleted this model since the app started: it is then not downloaded again on its own, only
        /// when asked to (<see cref="Start"/>).
        /// </summary>
        public bool WasDeleted(SpeechModelInfo model)
        {
            lock (_lock) return _deleted.Contains(model.Id);
        }

        /// <summary>
        /// Deletes the model and any unfinished download of it. Not while it downloads (cancel that first). File system
        /// errors (the file is in use, access denied) are thrown for the caller to report.
        /// </summary>
        public void Delete(SpeechModelInfo model)
        {
            lock (_lock)
            {
                if (_state.Status == ModelDownloadStatus.Downloading && _state.ModelId == model.Id)
                    throw new InvalidOperationException($"{model.FileName} is downloading");

                if (Directory.Exists(ModelsDirectory))
                {
                    string path = PathOf(model);
                    File.Delete(path);
                    File.Delete(ModelDownloader.PartialPathOf(path));
                }
                _deleted.Add(model.Id);
            }
            AppLog.Info($"Speech model {model.FileName} deleted");
            ModelDeleted?.Invoke(this, model);
        }

        /// <summary>
        /// Starts downloading the model unless it is already on disk or already downloading. A download of another
        /// model is cancelled; its partial file stays for later.
        /// </summary>
        public void Start(SpeechModelInfo model)
        {
            ModelDownloadState state;
            lock (_lock)
            {
                _deleted.Remove(model.Id);
                if (_state.Status == ModelDownloadStatus.Downloading && _state.ModelId == model.Id) return;
                if (IsDownloaded(model)) return;

                _cts?.Cancel();
                var cts = new CancellationTokenSource();
                _cts = cts;
                _state = state = new ModelDownloadState(ModelDownloadStatus.Downloading, model.Id, 0, null, null);
                Task previous = _current;
                _current = Task.Run(() => RunAsync(model, previous, cts));
            }
            StateChanged?.Invoke(this, state);
        }

        public void Cancel()
        {
            lock (_lock) _cts?.Cancel();
        }

        /// <summary>Completes when the current download has ended (for tests and shutdown).</summary>
        internal Task WhenIdleAsync()
        {
            lock (_lock) return _current;
        }

        private async Task RunAsync(SpeechModelInfo model, Task previous, CancellationTokenSource cts)
        {
            // A cancelled download of the same file may still be closing its partial file
            await previous;

            try
            {
                cts.Token.ThrowIfCancellationRequested();
                AppLog.Info($"Downloading the speech model {model.FileName}");
                var progress = new InlineProgress(p => SetState(cts, new ModelDownloadState(
                    ModelDownloadStatus.Downloading, model.Id, p.BytesReceived, p.TotalBytes, null)));
                await _downloader.DownloadAsync(model, PathOf(model), progress, cts.Token);

                AppLog.Info($"Speech model {model.FileName} downloaded");
                ModelDownloadState last = State;
                SetState(cts, new ModelDownloadState(ModelDownloadStatus.Completed, model.Id, last.BytesReceived, last.TotalBytes, null));
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                AppLog.Info($"Download of {model.FileName} cancelled");
                SetState(cts, State with { Status = ModelDownloadStatus.Cancelled });
            }
            catch (ModelDownloadException ex)
            {
                // Network and disk conditions, not bugs
                AppLog.Info($"Download of {model.FileName} failed ({ex.Error}): {ex.Message}");
                SetState(cts, State with { Status = ModelDownloadStatus.Failed, Error = ex.Error });
            }
            catch (Exception ex)
            {
                AppLog.Error($"Download of {model.FileName} failed", ex);
                SetState(cts, State with { Status = ModelDownloadStatus.Failed, Error = ModelDownloadError.Network });
            }
        }

        // Only the current download reports: a cancelled one must not overwrite the state of its successor
        private void SetState(CancellationTokenSource owner, ModelDownloadState state)
        {
            lock (_lock)
            {
                if (_cts != owner) return;
                _state = state;
            }
            StateChanged?.Invoke(this, state);
        }

        public void Dispose()
        {
            Task current;
            lock (_lock)
            {
                _cts?.Cancel();
                current = _current;
            }
            try
            {
                current.Wait(TimeSpan.FromSeconds(2));
            }
            catch (AggregateException)
            {
                // Already logged by RunAsync
            }
        }

        // Progress<T> would post to the thread pool, so reports could arrive after the final state
        private sealed class InlineProgress : IProgress<ModelDownloadProgress>
        {
            private readonly Action<ModelDownloadProgress> _report;

            public InlineProgress(Action<ModelDownloadProgress> report) => _report = report;

            public void Report(ModelDownloadProgress value) => _report(value);
        }
    }
}
