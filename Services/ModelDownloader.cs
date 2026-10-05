using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using MovaCore.Models;

namespace MovaCore.Services
{
    /// <summary>
    /// Downloads a model file into "&lt;file&gt;.partial", resuming an interrupted download with an HTTP range request,
    /// checks its size, hash and format, and only then gives it its final name. The only network code in MovaCore.
    /// </summary>
    public sealed class ModelDownloader : IDisposable
    {
        private const int MaxRedirects = 10;
        private const int BufferSize = 256 * 1024;
        private const long SpaceMargin = 64L * 1024 * 1024;
        private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(250);

        private readonly HttpClient _http;

        /// <param name="handler">For tests; by default redirects are followed by hand (see <see cref="SendAsync"/>).</param>
        public ModelDownloader(HttpMessageHandler? handler = null)
        {
            _http = new HttpClient(handler ?? new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                ConnectTimeout = TimeSpan.FromSeconds(30),
            })
            {
                // A model takes minutes to download; StallTimeout catches a connection that stops delivering instead
                Timeout = Timeout.InfiniteTimeSpan,
            };
            _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(
                "MovaCore", Strings.FormatVersion(typeof(ModelDownloader).Assembly.GetName().Version)));
        }

        /// <summary>How long a response or a read may deliver nothing before the download counts as stalled.</summary>
        internal TimeSpan StallTimeout { get; init; } = TimeSpan.FromSeconds(30);

        /// <summary>Free bytes on the drive holding a path (null if unknown); replaceable for tests.</summary>
        internal Func<string, long?> GetFreeSpace { get; init; } = DefaultFreeSpace;

        public async Task DownloadAsync(
            SpeechModelInfo model,
            string destinationPath,
            IProgress<ModelDownloadProgress>? progress,
            CancellationToken cancellationToken)
        {
            string partialPath = destinationPath + ".partial";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destinationPath))!);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new ModelDownloadException(ModelDownloadError.Disk, ex.Message, ex);
            }

            for (int attempt = 0; ; attempt++)
            {
                long existing = File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0;
                Uri url = SpeechModelCatalog.DownloadUrl(model);
                (HttpResponseMessage response, string? sha256, long? linkedSize) =
                    await SendAsync(url, existing, null, cancellationToken);
                using (response)
                {
                    long offset;
                    if (response.StatusCode == HttpStatusCode.PartialContent && existing > 0
                        && response.Content.Headers.ContentRange?.From == existing)
                    {
                        offset = existing;
                    }
                    else if (response.StatusCode == HttpStatusCode.OK)
                    {
                        offset = 0; // a fresh download, or the server ignored the range: start over
                    }
                    else if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable && existing > 0 && attempt == 0)
                    {
                        // Nothing left to download: the partial file is complete (the app stopped before renaming it)
                        // or does not belong to the remote file, in which case it starts over once
                        if (linkedSize == existing && await IsCompleteAsync(model, partialPath, sha256, cancellationToken))
                            break;
                        DeleteQuietly(partialPath);
                        continue;
                    }
                    else
                    {
                        throw new ModelDownloadException(ModelDownloadError.Server, $"HTTP {(int)response.StatusCode} from {url.Host}");
                    }

                    long? total = linkedSize ?? response.Content.Headers.ContentRange?.Length
                        ?? (response.Content.Headers.ContentLength is long length ? offset + length : null);
                    long needed = (total ?? model.ApproximateBytes) - offset;
                    if (GetFreeSpace(partialPath) is long free && free < needed + SpaceMargin)
                    {
                        throw new ModelDownloadException(
                            ModelDownloadError.NotEnoughSpace, $"{needed / (1024 * 1024)} MB needed, {free / (1024 * 1024)} MB free");
                    }

                    using IncrementalHash? hash = CreateHash(model, sha256, out string? expectedHash);
                    if (hash != null && offset > 0) await HashFileAsync(partialPath, hash, cancellationToken);

                    long received = await ReceiveAsync(response, partialPath, offset, total, hash, progress, cancellationToken);

                    if (total is long expectedSize && received != expectedSize)
                    {
                        if (received < expectedSize)
                            throw new ModelDownloadException(ModelDownloadError.Network, "The connection closed before the end of the file");

                        DeleteQuietly(partialPath);
                        throw new ModelDownloadException(ModelDownloadError.Corrupt, $"Received {received} bytes, expected {expectedSize}");
                    }
                    if (hash != null && !string.Equals(Convert.ToHexStringLower(hash.GetHashAndReset()), expectedHash, StringComparison.Ordinal))
                    {
                        DeleteQuietly(partialPath);
                        throw new ModelDownloadException(ModelDownloadError.Corrupt, "The file does not match its hash");
                    }
                }
                break;
            }

            if (SpeechModelFile.Check(partialPath) != SpeechModelFormat.Ggml)
            {
                DeleteQuietly(partialPath);
                throw new ModelDownloadException(ModelDownloadError.Corrupt, "The file is not a whisper.cpp model");
            }

            try
            {
                File.Move(partialPath, destinationPath, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new ModelDownloadException(ModelDownloadError.Disk, ex.Message, ex);
            }
        }

        /// <summary>Reads the first bytes of a remote file (the smoke test checks the network path with it).</summary>
        public async Task<RemoteFileProbe> ProbeAsync(Uri url, int length, CancellationToken cancellationToken)
        {
            (HttpResponseMessage response, string? sha256, long? size) = await SendAsync(url, 0, length - 1, cancellationToken);
            using (response)
            {
                if (response.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.PartialContent))
                    throw new ModelDownloadException(ModelDownloadError.Server, $"HTTP {(int)response.StatusCode} from {url.Host}");

                var prefix = new byte[length];
                int read;
                try
                {
                    await using Stream body = await response.Content.ReadAsStreamAsync(cancellationToken);
                    read = await WithStallTimeout(t => body.ReadAtLeastAsync(prefix, length, false, t).AsTask(), cancellationToken);
                }
                catch (Exception ex) when (ex is IOException or HttpRequestException)
                {
                    throw new ModelDownloadException(ModelDownloadError.Network, ex.Message, ex);
                }
                return new RemoteFileProbe(prefix[..read], sha256, size ?? response.Content.Headers.ContentRange?.Length);
            }
        }

        /// <summary>
        /// Follows redirects by hand: Hugging Face answers with a redirect to its CDN, and only that first response
        /// carries the file's SHA-256 and size (X-Linked-Etag, X-Linked-Size).
        /// </summary>
        private async Task<(HttpResponseMessage Response, string? Sha256, long? LinkedSize)> SendAsync(
            Uri url, long rangeFrom, long? rangeTo, CancellationToken cancellationToken)
        {
            string? sha256 = null;
            long? linkedSize = null;
            for (int hop = 0; hop <= MaxRedirects; hop++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                if (rangeFrom > 0 || rangeTo != null) request.Headers.Range = new RangeHeaderValue(rangeFrom, rangeTo);

                HttpResponseMessage response;
                try
                {
                    response = await WithStallTimeout(
                        t => _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, t), cancellationToken);
                }
                catch (HttpRequestException ex)
                {
                    throw new ModelDownloadException(ModelDownloadError.Network, ex.Message, ex);
                }

                sha256 ??= ReadLinkedSha256(response);
                linkedSize ??= ReadLinkedSize(response);

                if (IsRedirect(response.StatusCode) && response.Headers.Location is { } location)
                {
                    url = location.IsAbsoluteUri ? location : new Uri(url, location);
                    response.Dispose();
                    continue;
                }
                return (response, sha256, linkedSize);
            }
            throw new ModelDownloadException(ModelDownloadError.Server, "Too many redirects");
        }

        private async Task<long> ReceiveAsync(
            HttpResponseMessage response,
            string partialPath,
            long offset,
            long? total,
            IncrementalHash? hash,
            IProgress<ModelDownloadProgress>? progress,
            CancellationToken cancellationToken)
        {
            FileStream file;
            try
            {
                file = new FileStream(partialPath, offset > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write,
                    FileShare.Read, BufferSize, useAsync: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new ModelDownloadException(ModelDownloadError.Disk, ex.Message, ex);
            }

            await using (file)
            {
                await using Stream body = await response.Content.ReadAsStreamAsync(cancellationToken);
                var buffer = new byte[BufferSize];
                long received = offset;
                long lastReport = Stopwatch.GetTimestamp();
                progress?.Report(new ModelDownloadProgress(received, total));

                while (true)
                {
                    int read;
                    try
                    {
                        read = await WithStallTimeout(t => body.ReadAsync(buffer, t).AsTask(), cancellationToken);
                    }
                    catch (Exception ex) when (ex is IOException or HttpRequestException)
                    {
                        throw new ModelDownloadException(ModelDownloadError.Network, ex.Message, ex);
                    }
                    if (read == 0) break;

                    try
                    {
                        await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    }
                    catch (IOException ex)
                    {
                        throw new ModelDownloadException(ModelDownloadError.Disk, ex.Message, ex);
                    }
                    hash?.AppendData(buffer, 0, read);
                    received += read;

                    if (Stopwatch.GetElapsedTime(lastReport) >= ProgressInterval)
                    {
                        progress?.Report(new ModelDownloadProgress(received, total));
                        lastReport = Stopwatch.GetTimestamp();
                    }
                }

                progress?.Report(new ModelDownloadProgress(received, total));
                return received;
            }
        }

        private async Task<T> WithStallTimeout<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
        {
            using var stall = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            stall.CancelAfter(StallTimeout);
            try
            {
                return await operation(stall.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new ModelDownloadException(ModelDownloadError.Stalled, $"No data for {StallTimeout.TotalSeconds:0} s");
            }
        }

        /// <summary>The catalog's hash if it has one, otherwise the SHA-256 reported by the server, if any.</summary>
        private static IncrementalHash? CreateHash(SpeechModelInfo model, string? reportedSha256, out string? expected)
        {
            if (model.Sha1 != null)
            {
                expected = model.Sha1.ToLowerInvariant();
                return IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
            }
            if ((model.Sha256 ?? reportedSha256) is { } sha256)
            {
                expected = sha256.ToLowerInvariant();
                return IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            }
            expected = null;
            return null;
        }

        private static async Task<bool> IsCompleteAsync(
            SpeechModelInfo model, string path, string? reportedSha256, CancellationToken cancellationToken)
        {
            using IncrementalHash? hash = CreateHash(model, reportedSha256, out string? expected);
            if (hash == null) return false;

            await HashFileAsync(path, hash, cancellationToken);
            return string.Equals(Convert.ToHexStringLower(hash.GetHashAndReset()), expected, StringComparison.Ordinal);
        }

        private static async Task HashFileAsync(string path, IncrementalHash hash, CancellationToken cancellationToken)
        {
            await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true);
            var buffer = new byte[BufferSize];
            int read;
            while ((read = await file.ReadAsync(buffer, cancellationToken)) > 0) hash.AppendData(buffer, 0, read);
        }

        private static bool IsRedirect(HttpStatusCode status) => status is
            HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther or
            HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

        // X-Linked-Etag: "<64 hex digits>", the SHA-256 of a file stored in Git LFS / Xet
        internal static string? ReadLinkedSha256(HttpResponseMessage response)
        {
            if (!response.Headers.TryGetValues("X-Linked-Etag", out var values)) return null;
            foreach (string value in values)
            {
                string tag = value.Trim();
                if (tag.StartsWith("W/", StringComparison.Ordinal)) tag = tag[2..];
                tag = tag.Trim('"');
                if (tag.Length == 64 && IsHex(tag)) return tag.ToLowerInvariant();
            }
            return null;
        }

        private static long? ReadLinkedSize(HttpResponseMessage response)
        {
            if (!response.Headers.TryGetValues("X-Linked-Size", out var values)) return null;
            foreach (string value in values)
            {
                if (long.TryParse(value, out long size) && size > 0) return size;
            }
            return null;
        }

        private static bool IsHex(string text)
        {
            foreach (char c in text)
            {
                if (!char.IsAsciiHexDigit(c)) return false;
            }
            return true;
        }

        private static long? DefaultFreeSpace(string path)
        {
            try
            {
                string? root = Path.GetPathRoot(Path.GetFullPath(path));
                return root == null ? null : new DriveInfo(root).AvailableFreeSpace;
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        private static void DeleteQuietly(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Info($"Could not delete {Path.GetFileName(path)}: {ex.Message}");
            }
        }

        public void Dispose() => _http.Dispose();
    }
}
