using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using MovaCore.Models;
using MovaCore.Services;
using Xunit;

namespace MovaCore.Tests
{
    public sealed class ModelDownloaderTests : IDisposable
    {
        private const string FileName = "ggml-test.bin";
        private const string CdnUrl = "https://cdn.example/ggml-test.bin";
        private static readonly string HuggingFaceUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/" + FileName;

        private readonly string _directory = Path.Combine(Path.GetTempPath(), $"movacore-dl-{Guid.NewGuid():N}");
        private readonly FakeHttpHandler _http = new();
        private readonly byte[] _content = ModelBytes(300_000);
        private readonly List<ModelDownloadProgress> _progress = new();

        private string Destination => Path.Combine(_directory, FileName);
        private string Partial => Destination + ".partial";

        public void Dispose()
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }

        /// <summary>A file with the ggml magic, as whisper.cpp writes it.</summary>
        private static byte[] ModelBytes(int length)
        {
            var bytes = new byte[length];
            new Random(42).NextBytes(bytes);
            new byte[] { 0x6C, 0x6D, 0x67, 0x67 }.CopyTo(bytes, 0);
            return bytes;
        }

        private static string Sha1(byte[] data) => Convert.ToHexStringLower(SHA1.HashData(data));
        private static string Sha256(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));

        private SpeechModelInfo Model(string? sha1 = null) => new("test", FileName, 300_000, sha1);

        private ModelDownloader Downloader(Func<string, long?>? freeSpace = null) => new(_http)
        {
            StallTimeout = TimeSpan.FromMilliseconds(300),
            GetFreeSpace = freeSpace ?? (_ => long.MaxValue),
        };

        private Task DownloadAsync(SpeechModelInfo model, ModelDownloader? downloader = null, CancellationToken token = default) =>
            (downloader ?? Downloader()).DownloadAsync(model, Destination, new SyncProgress(_progress), token);

        // Hugging Face redirects to its CDN; the redirect carries the file's SHA-256 and size
        private void ServeLikeHuggingFace(byte[] served, string? sha256 = null, long? size = null, int? cutAfter = null)
        {
            _http.Redirect(HuggingFaceUrl, CdnUrl, response =>
            {
                response.Headers.TryAddWithoutValidation("X-Linked-Etag", $"\"{sha256 ?? Sha256(served)}\"");
                response.Headers.TryAddWithoutValidation("X-Linked-Size", (size ?? served.Length).ToString());
            });
            _http.File(CdnUrl, served, cutAfter: cutAfter);
        }

        [Fact]
        public async Task Download_WithKnownSha1_IsVerifiedAndRenamed()
        {
            _http.File(HuggingFaceUrl, _content);

            await DownloadAsync(Model(Sha1(_content)));

            Assert.Equal(_content, File.ReadAllBytes(Destination));
            Assert.False(File.Exists(Partial));
            Assert.Equal(new ModelDownloadProgress(_content.Length, _content.Length), _progress[^1]);
        }

        [Fact]
        public async Task Download_ThroughRedirect_IsVerifiedAgainstTheLinkedSha256()
        {
            ServeLikeHuggingFace(_content);

            await DownloadAsync(Model());

            Assert.Equal(_content, File.ReadAllBytes(Destination));
            Assert.Equal(2, _http.Requests.Count);
        }

        [Fact]
        public async Task LinkedSha256Mismatch_DeletesTheFile()
        {
            ServeLikeHuggingFace(_content, sha256: new string('a', 64));

            var error = await Assert.ThrowsAsync<ModelDownloadException>(() => DownloadAsync(Model()));

            Assert.Equal(ModelDownloadError.Corrupt, error.Error);
            Assert.False(File.Exists(Partial));
            Assert.False(File.Exists(Destination));
        }

        [Fact]
        public async Task KnownSha1Mismatch_DeletesTheFile()
        {
            _http.File(HuggingFaceUrl, _content);

            var error = await Assert.ThrowsAsync<ModelDownloadException>(() => DownloadAsync(Model(new string('0', 40))));

            Assert.Equal(ModelDownloadError.Corrupt, error.Error);
            Assert.False(File.Exists(Partial));
        }

        [Fact]
        public async Task PartialFile_IsResumedWithARangeRequest()
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllBytes(Partial, _content[..100_000]);
            ServeLikeHuggingFace(_content);

            await DownloadAsync(Model(Sha1(_content)));

            Assert.Equal(_content, File.ReadAllBytes(Destination));
            Assert.All(_http.Requests, r => Assert.Equal(100_000, r.Headers.Range?.Ranges.Single().From));
            Assert.Equal(100_000, _progress[0].BytesReceived);
        }

        [Fact]
        public async Task ServerIgnoringTheRange_StartsOver()
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllBytes(Partial, new byte[] { 1, 2, 3 });
            _http.File(HuggingFaceUrl, _content, honorRange: false);

            await DownloadAsync(Model(Sha1(_content)));

            Assert.Equal(_content, File.ReadAllBytes(Destination));
        }

        // The partial file is longer than the remote one: it cannot be resumed
        [Fact]
        public async Task UnsatisfiableRange_StartsOver()
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllBytes(Partial, new byte[_content.Length + 10]);
            _http.File(HuggingFaceUrl, _content);

            await DownloadAsync(Model(Sha1(_content)));

            Assert.Equal(_content, File.ReadAllBytes(Destination));
        }

        [Fact]
        public async Task BrokenConnection_KeepsThePartialFileForResuming()
        {
            ServeLikeHuggingFace(_content, cutAfter: 120_000);

            var error = await Assert.ThrowsAsync<ModelDownloadException>(() => DownloadAsync(Model()));

            Assert.Equal(ModelDownloadError.Network, error.Error);
            Assert.Equal(120_000, new FileInfo(Partial).Length);

            ServeLikeHuggingFace(_content);
            await DownloadAsync(Model());
            Assert.Equal(_content, File.ReadAllBytes(Destination));
        }

        [Fact]
        public async Task NotAWhisperModel_IsDeleted()
        {
            byte[] html = System.Text.Encoding.UTF8.GetBytes("<html>Sign in to continue</html>");
            _http.File(HuggingFaceUrl, html);

            var error = await Assert.ThrowsAsync<ModelDownloadException>(() => DownloadAsync(Model()));

            Assert.Equal(ModelDownloadError.Corrupt, error.Error);
            Assert.False(File.Exists(Partial));
            Assert.False(File.Exists(Destination));
        }

        [Fact]
        public async Task NotEnoughSpace_IsReportedBeforeWriting()
        {
            ServeLikeHuggingFace(_content);

            var error = await Assert.ThrowsAsync<ModelDownloadException>(() => DownloadAsync(Model(), Downloader(_ => 1000)));

            Assert.Equal(ModelDownloadError.NotEnoughSpace, error.Error);
            Assert.False(File.Exists(Partial));
        }

        [Fact]
        public async Task HttpError_IsAServerError()
        {
            _http.Status(HuggingFaceUrl, HttpStatusCode.NotFound);

            var error = await Assert.ThrowsAsync<ModelDownloadException>(() => DownloadAsync(Model()));

            Assert.Equal(ModelDownloadError.Server, error.Error);
            Assert.Contains("404", error.Message);
        }

        [Fact]
        public async Task NoConnection_IsANetworkError()
        {
            _http.Throw(HuggingFaceUrl, new HttpRequestException("No such host is known"));

            var error = await Assert.ThrowsAsync<ModelDownloadException>(() => DownloadAsync(Model()));

            Assert.Equal(ModelDownloadError.Network, error.Error);
        }

        [Fact]
        public async Task SilentConnection_IsStalled()
        {
            _http.Route(HuggingFaceUrl, _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StallingStream()) });

            var error = await Assert.ThrowsAsync<ModelDownloadException>(() => DownloadAsync(Model()));

            Assert.Equal(ModelDownloadError.Stalled, error.Error);
        }

        [Fact]
        public async Task Cancellation_IsNotAnError()
        {
            _http.Route(HuggingFaceUrl, _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StallingStream()) });
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            var downloader = new ModelDownloader(_http) { StallTimeout = TimeSpan.FromSeconds(30), GetFreeSpace = _ => null };

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DownloadAsync(Model(), downloader, cts.Token));
            Assert.False(File.Exists(Destination));
        }

        [Fact]
        public async Task TooManyRedirects_IsAServerError()
        {
            _http.Redirect(HuggingFaceUrl, HuggingFaceUrl);

            var error = await Assert.ThrowsAsync<ModelDownloadException>(() => DownloadAsync(Model()));

            Assert.Equal(ModelDownloadError.Server, error.Error);
        }

        [Fact]
        public async Task RelativeRedirect_IsResolved()
        {
            _http.Redirect(HuggingFaceUrl, "/mirror/" + FileName);
            _http.File("https://huggingface.co/mirror/" + FileName, _content);

            await DownloadAsync(Model(Sha1(_content)));

            Assert.Equal(_content, File.ReadAllBytes(Destination));
        }

        [Fact]
        public async Task Probe_ReadsThePrefixAndTheLinkedHeaders()
        {
            ServeLikeHuggingFace(_content);

            RemoteFileProbe probe = await Downloader().ProbeAsync(new Uri(HuggingFaceUrl), 4096, CancellationToken.None);

            Assert.Equal(_content[..4096], probe.Prefix);
            Assert.Equal(Sha256(_content), probe.Sha256);
            Assert.Equal(_content.Length, probe.Size);
            Assert.Equal(4095, _http.Requests[^1].Headers.Range?.Ranges.Single().To);
        }

        [Theory]
        [InlineData("\"ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789\"", "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789")]
        [InlineData("W/\"abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789\"", "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789")]
        [InlineData("\"5d41402abc4b2a76b9719d911017c592\"", null)] // an MD5-style ETag is not a SHA-256
        public void LinkedEtag_IsParsedAsSha256(string header, string? expected)
        {
            using var response = new HttpResponseMessage(HttpStatusCode.Found);
            response.Headers.TryAddWithoutValidation("X-Linked-Etag", header);

            Assert.Equal(expected, ModelDownloader.ReadLinkedSha256(response));
        }

        private sealed class SyncProgress : IProgress<ModelDownloadProgress>
        {
            private readonly List<ModelDownloadProgress> _reports;

            public SyncProgress(List<ModelDownloadProgress> reports) => _reports = reports;

            public void Report(ModelDownloadProgress value) => _reports.Add(value);
        }
    }
}
