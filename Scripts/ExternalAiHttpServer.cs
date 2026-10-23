#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace RedAlert;

public sealed record ExternalAiHttpRequest(string Method, string Path, byte[] Body, string Query = "");

public sealed record ExternalAiHttpResponse(int StatusCode, byte[] Body)
{
    public static ExternalAiHttpResponse Json(int statusCode, object payload)
        => new(statusCode, JsonSerializer.SerializeToUtf8Bytes(payload, ExternalAiJson.Options));
}

public static class ExternalAiJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            MaxDepth = 32
        };
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}

/// <summary>
/// Loopback HTTP transport. Only Pump invokes application code, on its caller's thread.
/// </summary>
public sealed class ExternalAiHttpServer : IDisposable
{
    private const int MaxActiveRequests = 64;
    private const int MaxBodyBytes = 65_536;
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(5);

    private static readonly ExternalAiHttpResponse BusyResponse = Error(
        503, "server_busy", "The server has reached its request limit.");
    private static readonly ExternalAiHttpResponse QueueTimeoutResponse = Error(
        503, "queue_timeout", "The request timed out waiting to be handled.");
    private static readonly ExternalAiHttpResponse ReadTimeoutResponse = Error(
        408, "request_timeout", "The request body was not received in time.");
    private static readonly ExternalAiHttpResponse BodyTooLargeResponse = Error(
        413, "body_too_large", "The request body exceeds 65536 bytes.");
    private static readonly ExternalAiHttpResponse OriginResponse = Error(
        403, "origin_not_allowed", "Requests with an Origin header are not accepted.");
    private static readonly ExternalAiHttpResponse LoopbackResponse = Error(
        403, "loopback_only", "Only local loopback clients are accepted.");
    private static readonly ExternalAiHttpResponse MethodResponse = Error(
        405, "method_not_allowed", "Only GET and POST are accepted.");
    private static readonly ExternalAiHttpResponse MediaTypeResponse = Error(
        415, "unsupported_media_type", "POST requires application/json.");
    private static readonly ExternalAiHttpResponse InternalErrorResponse = Error(
        500, "internal_error", "The request could not be handled.");

    private readonly object _pumpGate = new();
    private readonly object _gate = new();
    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly HashSet<ActiveRequest> _active = new();
    private readonly LinkedList<ActiveRequest> _pending = new();
    private readonly Task _acceptTask;
    private HttpListenerContext? _rejectingContext;
    private bool _stopping;
    private bool _disposed;

    public int Port { get; }
    public string BaseUrl { get; }

    public ExternalAiHttpServer(int port)
    {
        if (port is < 1 or > 65_535)
            throw new ArgumentOutOfRangeException(nameof(port), "The port must be between 1 and 65535.");

        Port = port;
        BaseUrl = "http://127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture) + "/";
        _listener = new HttpListener();
        try
        {
            _listener.Prefixes.Add(BaseUrl);
            _listener.Start();
            _acceptTask = Task.Run(AcceptLoopAsync);
        }
        catch
        {
            _listener.Close();
            _shutdown.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Invokes at most limit handlers on this thread and returns the number invoked.
    /// Expired requests are skipped. After shutdown this returns zero.
    /// </summary>
    public int Pump(Func<ExternalAiHttpRequest, ExternalAiHttpResponse> handler, int limit = 16)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentOutOfRangeException.ThrowIfNegative(limit);

        // Keep Dispose from completing between claiming a request and actually
        // entering its handler. This monitor is reentrant if a handler disposes us.
        lock (_pumpGate)
            return PumpCore(handler, limit);
    }

    private int PumpCore(Func<ExternalAiHttpRequest, ExternalAiHttpResponse> handler, int limit)
    {
        var handled = 0;
        while (handled < limit)
        {
            ActiveRequest? next = null;
            lock (_gate)
            {
                if (_stopping)
                    break;

                while (_pending.First is { } node)
                {
                    var candidate = node.Value;
                    RemoveFromQueue(candidate);
                    if (candidate.State != RequestState.Queued)
                        continue;

                    // Check the clock as well as the timer: a delayed timer callback
                    // must never let an expired request reach application code.
                    if (Stopwatch.GetElapsedTime(candidate.QueuedAt) >= OperationTimeout)
                    {
                        candidate.State = RequestState.ResponseReady;
                        candidate.Response.TrySetResult(QueueTimeoutResponse);
                        continue;
                    }

                    // Claim before releasing the queue lock. Its queue timer is
                    // removed, and public Dispose is serialized with this Pump.
                    candidate.State = RequestState.Executing;
                    next = candidate;
                    break;
                }
            }

            if (next is null)
                break;

            ExternalAiHttpResponse response;
            try
            {
                response = handler(next.Request!);
                if (response is null || response.Body is null || response.StatusCode is < 200 or > 599)
                    response = InternalErrorResponse;
            }
            catch (Exception)
            {
                response = InternalErrorResponse;
            }

            lock (_gate)
            {
                // Disposal may have abandoned the connection while the handler ran.
                if (next.State == RequestState.Executing)
                {
                    next.State = RequestState.ResponseReady;
                    next.Response.TrySetResult(response);
                }
            }
            handled++;
        }
        return handled;
    }

    public void Dispose()
    {
        // A concurrent Pump must finish before shutdown is committed. Background
        // cleanup never takes _pumpGate, so a handler may safely call Dispose
        // itself. Concurrent Dispose callers also wait for completed cleanup.
        lock (_pumpGate)
        {
            if (_disposed)
                return;
            _disposed = true;
            StopTransport();
            ActiveRequest[] remaining;
            lock (_gate)
            {
                remaining = new ActiveRequest[_active.Count];
                _active.CopyTo(remaining);
            }

            // No request can be admitted after StopTransport. Every asynchronous
            // I/O wait is cancellation bounded, and context aborts unblock I/O.
            ObserveCompletion(_acceptTask);
            foreach (var request in remaining)
                ObserveCompletion(request.Finished.Task);
            _shutdown.Dispose();
        }
    }

    private async Task AcceptLoopAsync()
    {
        try
        {
            while (true)
            {
                var context = await _listener.GetContextAsync().ConfigureAwait(false);
                ActiveRequest? work = null;
                bool stopping;
                lock (_gate)
                {
                    stopping = _stopping;
                    if (!stopping && _active.Count < MaxActiveRequests)
                    {
                        work = new ActiveRequest(this, context);
                        _active.Add(work);
                    }
                    else if (!stopping)
                    {
                        _rejectingContext = context;
                    }
                }

                if (stopping)
                {
                    Abort(context);
                    break;
                }
                if (work is not null)
                {
                    // Its lifetime is tracked by _active and Finished; all failures
                    // are contained inside ProcessRequestAsync.
                    _ = ProcessRequestAsync(work);
                    continue;
                }

                // Reject overflow serially so a flood cannot create unbounded
                // rejection tasks. The response write has its own timeout.
                await TryWriteResponseAsync(context, BusyResponse).ConfigureAwait(false);
                Abort(context);
                lock (_gate)
                {
                    if (ReferenceEquals(_rejectingContext, context))
                        _rejectingContext = null;
                }
            }
        }
        catch (Exception)
        {
            // Listener shutdown, platform/network failures, and disconnects must
            // never escape into the application thread.
        }
        finally
        {
            StopTransport();
        }
    }

    private async Task ProcessRequestAsync(ActiveRequest work)
    {
        try
        {
            var response = Validate(work.Context.Request);
            if (response is null)
            {
                byte[] body;
                using (var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token))
                {
                    readTimeout.CancelAfter(OperationTimeout);
                    body = await ReadBodyAsync(work.Context.Request, readTimeout.Token).ConfigureAwait(false);
                }

                var request = work.Context.Request;
                work.Request = new ExternalAiHttpRequest(
                    request.HttpMethod, request.Url?.AbsolutePath ?? "/", body, request.Url?.Query ?? "");
                if (!Enqueue(work))
                    return;
                response = await work.Response.Task.WaitAsync(_shutdown.Token).ConfigureAwait(false);
            }
            await TryWriteResponseAsync(work.Context, response).ConfigureAwait(false);
        }
        catch (BodyTooLargeException)
        {
            await TryWriteResponseAsync(work.Context, BodyTooLargeResponse).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!_shutdown.IsCancellationRequested)
        {
            await TryWriteResponseAsync(work.Context, ReadTimeoutResponse).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        catch (HttpListenerException) { }
        catch (ObjectDisposedException) { }
        catch (Exception)
        {
            await TryWriteResponseAsync(work.Context, InternalErrorResponse).ConfigureAwait(false);
        }
        finally
        {
            Abort(work.Context);
            lock (_gate)
            {
                RemoveFromQueue(work);
                work.State = RequestState.Canceled;
                work.Response.TrySetCanceled();
                _active.Remove(work);
                work.Finished.TrySetResult(true);
            }
        }
    }

    private static ExternalAiHttpResponse? Validate(HttpListenerRequest request)
    {
        if (request.RemoteEndPoint is not { } peer || !IPAddress.IsLoopback(peer.Address))
            return LoopbackResponse;
        if (!string.IsNullOrEmpty(request.Headers["Origin"]))
            return OriginResponse;
        if (request.HttpMethod is not ("GET" or "POST"))
            return MethodResponse;
        if (request.HttpMethod == "POST"
            && (!MediaTypeHeaderValue.TryParse(request.ContentType, out var contentType)
                || !string.Equals(contentType.MediaType, "application/json", StringComparison.OrdinalIgnoreCase)))
            return MediaTypeResponse;
        if (request.ContentLength64 > MaxBodyBytes)
            return BodyTooLargeResponse;
        return null;
    }

    private static async Task<byte[]> ReadBodyAsync(HttpListenerRequest request, CancellationToken cancellationToken)
    {
        if (!request.HasEntityBody)
            return Array.Empty<byte>();

        // The extra byte detects chunked/unknown-length bodies over the limit.
        // Never grow a buffer based on untrusted Content-Length.
        var buffer = new byte[MaxBodyBytes + 1];
        var total = 0;
        while (total <= MaxBodyBytes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = request.InputStream.ReadAsync(buffer.AsMemory(total), cancellationToken).AsTask();
            int count;
            try
            {
                count = await read.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                ObserveAbandonedIo(read);
                throw;
            }
            if (count == 0)
                return buffer.AsSpan(0, total).ToArray();
            total += count;
            if (total > MaxBodyBytes)
                throw new BodyTooLargeException();
        }
        throw new BodyTooLargeException();
    }

    private bool Enqueue(ActiveRequest work)
    {
        lock (_gate)
        {
            if (_stopping)
                return false;
            work.State = RequestState.Queued;
            work.QueuedAt = Stopwatch.GetTimestamp();
            work.QueueNode = _pending.AddLast(work);
            work.QueueTimer = new Timer(static state =>
            {
                var request = (ActiveRequest)state!;
                request.Owner.ExpireQueuedRequest(request);
            }, work, OperationTimeout, Timeout.InfiniteTimeSpan);
            return true;
        }
    }

    private void ExpireQueuedRequest(ActiveRequest work)
    {
        lock (_gate)
        {
            if (work.State != RequestState.Queued)
                return;
            RemoveFromQueue(work);
            work.State = RequestState.ResponseReady;
            work.Response.TrySetResult(QueueTimeoutResponse);
        }
    }

    // All callers hold _gate. Physically remove canceled entries rather than
    // leaving tombstones which could grow without bound when Pump is idle.
    private void RemoveFromQueue(ActiveRequest work)
    {
        if (work.QueueNode is { } node)
        {
            _pending.Remove(node);
            work.QueueNode = null;
        }
        work.QueueTimer?.Dispose();
        work.QueueTimer = null;
    }

    private async Task TryWriteResponseAsync(HttpListenerContext context, ExternalAiHttpResponse response)
    {
        try
        {
            _shutdown.Token.ThrowIfCancellationRequested();
            var output = context.Response;
            output.StatusCode = response.StatusCode;
            output.ContentType = "application/json; charset=utf-8";
            output.ContentEncoding = Encoding.UTF8;
            output.Headers[HttpResponseHeader.CacheControl] = "no-store";
            if (response.StatusCode == 405)
                output.Headers[HttpResponseHeader.Allow] = "GET, POST";
            output.ContentLength64 = response.Body.LongLength;
            output.KeepAlive = false;

            using var writeTimeout = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
            writeTimeout.CancelAfter(OperationTimeout);
            var write = output.OutputStream.WriteAsync(response.Body.AsMemory(), writeTimeout.Token).AsTask();
            try
            {
                await write.WaitAsync(writeTimeout.Token).ConfigureAwait(false);
            }
            catch
            {
                ObserveAbandonedIo(write);
                throw;
            }
            output.Close();
        }
        catch (Exception)
        {
            // A timeout or lost client is transport failure, never a Pump failure.
            Abort(context);
        }
    }

    private void StopTransport()
    {
        ActiveRequest[] active;
        HttpListenerContext? rejecting;
        lock (_gate)
        {
            if (_stopping)
                return;
            _stopping = true;
            active = new ActiveRequest[_active.Count];
            _active.CopyTo(active);
            foreach (var request in active)
            {
                RemoveFromQueue(request);
                request.State = RequestState.Canceled;
                request.Response.TrySetCanceled();
            }
            rejecting = _rejectingContext;
            _rejectingContext = null;
        }

        try { _shutdown.Cancel(); }
        catch (Exception) { }
        foreach (var request in active)
            Abort(request.Context);
        if (rejecting is not null)
            Abort(rejecting);
        try { _listener.Close(); }
        catch (Exception) { }
    }

    private static void Abort(HttpListenerContext context)
    {
        try { context.Response.Abort(); }
        catch (Exception) { }
        try { context.Request.InputStream.Dispose(); }
        catch (Exception) { }
    }

    private static void ObserveAbandonedIo(Task task)
    {
        // Some HttpListener implementations do not promptly honor I/O cancellation.
        // The context is aborted by the owner; observe any later I/O exception too.
        _ = task.ContinueWith(static completed => { _ = completed.Exception; },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static void ObserveCompletion(Task task)
    {
        try { task.GetAwaiter().GetResult(); }
        catch (Exception) { }
    }

    private static ExternalAiHttpResponse Error(int statusCode, string code, string message)
        => ExternalAiHttpResponse.Json(statusCode, new { error = new { code, message } });

    private enum RequestState { Reading, Queued, Executing, ResponseReady, Canceled }

    private sealed class ActiveRequest
    {
        public ExternalAiHttpServer Owner { get; }
        public HttpListenerContext Context { get; }
        public ExternalAiHttpRequest? Request { get; set; }
        public RequestState State { get; set; } = RequestState.Reading;
        public long QueuedAt { get; set; }
        public LinkedListNode<ActiveRequest>? QueueNode { get; set; }
        public Timer? QueueTimer { get; set; }
        public TaskCompletionSource<ExternalAiHttpResponse> Response { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Finished { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ActiveRequest(ExternalAiHttpServer owner, HttpListenerContext context)
        {
            Owner = owner;
            Context = context;
        }
    }

    private sealed class BodyTooLargeException : Exception { }
}
