using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using MovaCore.Models;
using MovaCore.Services;
using Xunit;

namespace MovaCore.Tests
{
    public sealed class ModelDownloadManagerTests : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), $"movacore-dm-{Guid.NewGuid():N}");
        private readonly FakeHttpHandler _http = new();
        private readonly ModelDownloadManager _manager;
        private readonly List<ModelDownloadState> _states = new();
        private readonly byte[] _content;
        private readonly SpeechModelInfo _model;

        public ModelDownloadManagerTests()
        {
            _content = new byte[50_000];
            new Random(7).NextBytes(_content);
            new byte[] { 0x6C, 0x6D, 0x67, 0x67 }.CopyTo(_content, 0);
            _model = new SpeechModelInfo("test", "ggml-test.bin", _content.Length, Convert.ToHexStringLower(SHA1.HashData(_content)));

            var downloader = new ModelDownloader(_http) { GetFreeSpace = _ => null, StallTimeout = TimeSpan.FromSeconds(30) };
            _manager = new ModelDownloadManager(downloader, _directory);
            _manager.StateChanged += (_, state) =>
            {
                lock (_states) _states.Add(state);
            };
        }

        private string Url => SpeechModelCatalog.DownloadUrl(_model).ToString();

        public void Dispose()
        {
            _manager.Dispose();
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }

        [Fact]
        public async Task Start_DownloadsAndReportsCompletion()
        {
            _http.File(Url, _content);

            _manager.Start(_model);
            await _manager.WhenIdleAsync();

            Assert.True(_manager.IsDownloaded(_model));
            Assert.Equal(ModelDownloadStatus.Completed, _manager.State.Status);
            Assert.Equal(100, _manager.State.Percent);
            Assert.Equal(ModelDownloadStatus.Downloading, _states[0].Status);
            Assert.Equal(ModelDownloadStatus.Completed, _states[^1].Status);
        }

        [Fact]
        public async Task Start_ForAModelOnDisk_DoesNothing()
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllBytes(_manager.PathOf(_model), _content);

            _manager.Start(_model);
            await _manager.WhenIdleAsync();

            Assert.Equal(ModelDownloadStatus.Idle, _manager.State.Status);
            Assert.Empty(_http.Requests);
        }

        [Fact]
        public async Task Failure_IsReportedWithItsReason()
        {
            _http.Status(Url, HttpStatusCode.ServiceUnavailable);

            _manager.Start(_model);
            await _manager.WhenIdleAsync();

            Assert.Equal(ModelDownloadStatus.Failed, _manager.State.Status);
            Assert.Equal(ModelDownloadError.Server, _manager.State.Error);
        }

        [Fact]
        public async Task Cancel_StopsTheDownload()
        {
            _http.Route(Url, _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StallingStream()) });

            _manager.Start(_model);
            await Task.Delay(100);
            _manager.Cancel();
            await _manager.WhenIdleAsync();

            Assert.Equal(ModelDownloadStatus.Cancelled, _manager.State.Status);
            Assert.False(_manager.IsDownloaded(_model));
        }

        [Fact]
        public async Task StartingTheSameModelTwice_DownloadsOnce()
        {
            _http.Route(Url, _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StallingStream()) });

            _manager.Start(_model);
            _manager.Start(_model);
            await Task.Delay(100);
            _manager.Cancel();
            await _manager.WhenIdleAsync();

            Assert.Single(_http.Requests);
        }

        [Fact]
        public async Task AfterAFailure_StartTriesAgain()
        {
            _http.Status(Url, HttpStatusCode.ServiceUnavailable);
            _manager.Start(_model);
            await _manager.WhenIdleAsync();

            _http.File(Url, _content);
            _manager.Start(_model);
            await _manager.WhenIdleAsync();

            Assert.Equal(ModelDownloadStatus.Completed, _manager.State.Status);
        }

        [Fact]
        public void Percent_IsNullWhileTheSizeIsUnknown()
        {
            Assert.Null(new ModelDownloadState(ModelDownloadStatus.Downloading, "x", 10, null, null).Percent);
            Assert.Equal(25, new ModelDownloadState(ModelDownloadStatus.Downloading, "x", 25, 100, null).Percent);
        }

        [Fact]
        public void Delete_RemovesTheModelAndItsUnfinishedDownload()
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllBytes(_manager.PathOf(_model), _content);
            File.WriteAllBytes(ModelDownloader.PartialPathOf(_manager.PathOf(_model)), _content[..100]);
            SpeechModelInfo? deleted = null;
            _manager.ModelDeleted += (_, model) => deleted = model;

            Assert.Equal(_content.Length, _manager.SizeOnDisk(_model));
            _manager.Delete(_model);

            Assert.False(_manager.IsDownloaded(_model));
            Assert.Null(_manager.SizeOnDisk(_model));
            Assert.False(File.Exists(ModelDownloader.PartialPathOf(_manager.PathOf(_model))));
            Assert.Same(_model, deleted);
            Assert.True(_manager.WasDeleted(_model));
        }

        // Without even the models folder there is nothing to delete, and no error
        [Fact]
        public void Delete_WithoutTheModel_DoesNothing()
        {
            _manager.Delete(_model);

            Assert.True(_manager.WasDeleted(_model));
        }

        [Fact]
        public async Task Delete_WhileDownloadingIt_IsRefused()
        {
            _http.Route(Url, _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StallingStream()) });
            _manager.Start(_model);

            Assert.Throws<InvalidOperationException>(() => _manager.Delete(_model));

            _manager.Cancel();
            await _manager.WhenIdleAsync();
        }

        // Asking for the download again is the user's decision: the model is no longer marked as deleted
        [Fact]
        public async Task Start_AfterDelete_ClearsTheMark()
        {
            _manager.Delete(_model);
            _http.File(Url, _content);

            _manager.Start(_model);
            await _manager.WhenIdleAsync();

            Assert.False(_manager.WasDeleted(_model));
            Assert.True(_manager.IsDownloaded(_model));
        }
    }
}
