using System.Net;
using System.Net.Http;
using MovaCore.Models;
using MovaCore.Services;

namespace MovaCore.Tests
{
    public class FakeAudioRecorder : IAudioRecorder
    {
        /// <summary>What the next Stop returns.</summary>
        public float[] Recording { get; set; } = Speech(1.0);

        public Exception? StartError { get; set; }
        public List<string?> StartedDevices { get; } = new();
        public int StopCalls { get; private set; }
        public bool IsRecording { get; private set; }
        public float CurrentLevel => IsRecording ? 0.5f : 0;

        public IReadOnlyList<AudioInputDevice> GetInputDevices() => new[] { new AudioInputDevice("{mic-1}", "Microphone") };

        public void Start(string? deviceId)
        {
            if (StartError != null) throw StartError;
            StartedDevices.Add(deviceId);
            IsRecording = true;
        }

        public float[] Stop()
        {
            StopCalls++;
            IsRecording = false;
            return (float[])Recording.Clone(); // the orchestrator clears the buffer it gets
        }

        public void Dispose() { }

        /// <summary>A 220 Hz tone, loud enough to count as speech.</summary>
        public static float[] Speech(double seconds)
        {
            var samples = new float[(int)(seconds * AudioSamples.SampleRate)];
            for (int i = 0; i < samples.Length; i++)
                samples[i] = 0.2f * MathF.Sin(2 * MathF.PI * 220 * i / AudioSamples.SampleRate);
            return samples;
        }
    }

    public class FakeSpeechRecognizer : ISpeechRecognizer
    {
        public string[] Segments { get; set; } = { " Привіт, світе." };
        public Exception? Error { get; set; }
        public Exception? PreloadError { get; set; }

        /// <summary>When set, transcription waits for it (or for cancellation).</summary>
        public TaskCompletionSource? Gate { get; set; }

        public List<SpeechOptions> Preloads { get; } = new();
        public List<SpeechOptions> Transcriptions { get; } = new();
        public int SampleCount { get; private set; }
        public int UnloadCalls { get; private set; }

        public Task PreloadAsync(SpeechOptions options, CancellationToken cancellationToken)
        {
            lock (Preloads) Preloads.Add(options);
            return PreloadError == null ? Task.CompletedTask : Task.FromException(PreloadError);
        }

        public async Task<IReadOnlyList<string>> TranscribeAsync(float[] samples, SpeechOptions options, CancellationToken cancellationToken)
        {
            lock (Transcriptions) Transcriptions.Add(options);
            SampleCount = samples.Length;
            if (Gate != null) await Gate.Task.WaitAsync(cancellationToken);
            if (Error != null) throw Error;
            return Segments;
        }

        public void Unload() => UnloadCalls++;

        public void Dispose() { }
    }

    /// <summary>Serves files over fake HTTP, with redirects, range requests and Hugging Face's X-Linked-* headers.</summary>
    public class FakeHttpHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> _routes = new();

        public List<HttpRequestMessage> Requests { get; } = new();

        public void Redirect(string from, string to, Action<HttpResponseMessage>? headers = null)
        {
            _routes[from] = _ =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.Found);
                response.Headers.Location = new Uri(to, UriKind.RelativeOrAbsolute);
                headers?.Invoke(response);
                return response;
            };
        }

        /// <summary>Serves <paramref name="content"/>; with <paramref name="honorRange"/> false a range is ignored (200).</summary>
        public void File(string url, byte[] content, bool honorRange = true, int? cutAfter = null)
        {
            _routes[url] = request =>
            {
                long from = 0;
                long to = content.Length - 1;
                bool partial = false;
                if (honorRange && request.Headers.Range?.Ranges.FirstOrDefault() is { } range)
                {
                    from = range.From ?? 0;
                    to = Math.Min(range.To ?? to, content.Length - 1);
                    partial = true;
                    if (from >= content.Length)
                        return new HttpResponseMessage(HttpStatusCode.RequestedRangeNotSatisfiable);
                }

                byte[] body = content[(int)from..(int)(to + 1)];
                if (cutAfter is int cut && body.Length > cut) body = body[..cut];
                var response = new HttpResponseMessage(partial ? HttpStatusCode.PartialContent : HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(body),
                };
                if (partial)
                    response.Content.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(from, to, content.Length);
                return response;
            };
        }

        public void Status(string url, HttpStatusCode status) => _routes[url] = _ => new HttpResponseMessage(status);

        public void Route(string url, Func<HttpRequestMessage, HttpResponseMessage> route) => _routes[url] = route;

        public void Throw(string url, Exception exception) => _routes[url] = _ => throw exception;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (Requests) Requests.Add(request);
            string url = request.RequestUri!.ToString();
            HttpResponseMessage response = _routes.TryGetValue(url, out var route)
                ? route(request)
                : new HttpResponseMessage(HttpStatusCode.NotFound);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }

    /// <summary>A response body that never delivers anything until cancelled.</summary>
    public class StallingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
