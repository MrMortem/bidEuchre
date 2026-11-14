using System.Diagnostics;
using System.ComponentModel;
using System.Text;
using BidEuchre.Core;

namespace BidEuchre.Protocol;

public sealed record EngineIdentity(string Name, string Author, string ProtocolVersion);

public sealed class EngineProcessClient : IAsyncDisposable
{
    private readonly string _executable;
    private readonly string _arguments;
    private readonly TimeSpan _timeout;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _disposeLock = new();
    private Process? _process;
    private StreamWriter? _input;
    private StreamReader? _output;
    private Task? _disposeTask;
    private bool _channelFailed;
    private readonly StringBuilder _standardError = new();
    private readonly object _errorLock = new();
    private StreamReader? _errorOutput;
    private Task? _errorTask;

    public EngineProcessClient(string executable, string? arguments = null, TimeSpan? timeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        _executable = executable;
        _arguments = arguments ?? string.Empty;
        _timeout = timeout ?? TimeSpan.FromSeconds(5);
    }

    public EngineIdentity? Identity { get; private set; }
    public bool IsRunning => !_channelFailed && ProcessIsRunning(_process);

    public async Task<EngineIdentity> StartAsync(CancellationToken cancellationToken = default)
    {
        using var operation = CreateOperationCancellation(cancellationToken);
        await _gate.WaitAsync(operation.Token);
        try
        {
            ThrowIfClosing();
            EnsureChannelUsable();
            if (IsRunning)
            {
                return Identity ?? throw new ProtocolException("The engine handshake did not complete.");
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = _executable,
                Arguments = _arguments,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            _process = Process.Start(startInfo)
                ?? throw new ProtocolException($"Could not start engine '{_executable}'.");
            _input = _process.StandardInput;
            _input.AutoFlush = true;
            _output = _process.StandardOutput;
            _errorOutput = _process.StandardError;
            lock (_errorLock) _standardError.Clear();
            _errorTask = DrainStandardErrorAsync(_errorOutput);

            await SendAsync("beuci", operation.Token);
            string? name = null;
            string? author = null;
            var version = "1";
            while (true)
            {
                var line = await ReadLineAsync(operation.Token);
                if (line.Equals("beuciok", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                var tokens = CommandTokenizer.Tokenize(line);
                if (tokens.Count >= 3 && tokens[0].Equals("id", StringComparison.OrdinalIgnoreCase))
                {
                    if (tokens[1].Equals("name", StringComparison.OrdinalIgnoreCase))
                    {
                        name = string.Join(' ', tokens.Skip(2));
                    }
                    else if (tokens[1].Equals("author", StringComparison.OrdinalIgnoreCase))
                    {
                        author = string.Join(' ', tokens.Skip(2));
                    }
                }
                else if (tokens.Count == 3 && tokens[0].Equals("protocol", StringComparison.OrdinalIgnoreCase))
                {
                    version = tokens[2];
                }
            }

            Identity = new EngineIdentity(name ?? Path.GetFileNameWithoutExtension(_executable), author ?? "Unknown", version);
            await SendAsync("isready", operation.Token);
            var ready = await ReadUntilAsync(line => line.Equals("readyok", StringComparison.OrdinalIgnoreCase), operation.Token);
            if (!ready.Equals("readyok", StringComparison.OrdinalIgnoreCase))
            {
                throw new ProtocolException("Engine did not become ready.");
            }

            return Identity;
        }
        catch (Exception exception) when (exception is ProtocolException or IOException or Win32Exception)
        {
            // Let a failing launcher's final diagnostics arrive, but do not wait
            // indefinitely for a process that leaves its error stream open.
            if (_errorTask is not null)
            {
                try { await _errorTask.WaitAsync(TimeSpan.FromMilliseconds(200), operation.Token); }
                catch (TimeoutException) { }
            }

            string diagnostic;
            lock (_errorLock) diagnostic = _standardError.ToString().Trim();
            throw new ProtocolException($"Could not load engine '{_executable}': {exception.Message}" +
                (diagnostic.Length == 0 ? string.Empty : $"\n{diagnostic}"));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task NewGameAsync(CancellationToken cancellationToken = default)
    {
        using var operation = CreateOperationCancellation(cancellationToken);
        await _gate.WaitAsync(operation.Token);
        try
        {
            ThrowIfClosing();
            EnsureRunning();
            await SendAsync("newgame", operation.Token);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<BotAction> ChooseActionAsync(GameView view, int seat, CancellationToken cancellationToken = default)
    {
        using var operation = CreateOperationCancellation(cancellationToken);
        await _gate.WaitAsync(operation.Token);
        try
        {
            ThrowIfClosing();
            EnsureRunning();
            await SendPositionAsync(view, seat, operation.Token);
            await SendAsync("go", operation.Token);

            while (true)
            {
                var line = await ReadLineAsync(operation.Token);
                if (line.StartsWith("info ", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (line.StartsWith("error ", StringComparison.OrdinalIgnoreCase))
                {
                    throw new ProtocolException($"Engine error: {line[6..]}");
                }

                return BotActionNotation.Parse(line);
            }
        }
        catch (OperationCanceledException)
        {
            // A cancelled write may already have delivered go. There is no request
            // identifier in BEUCI, so this stream cannot safely serve another turn.
            FailChannel();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ObserveAsync(GameView view, int seat, CancellationToken cancellationToken = default)
    {
        using var operation = CreateOperationCancellation(cancellationToken);
        await _gate.WaitAsync(operation.Token);
        try
        {
            ThrowIfClosing();
            EnsureRunning();
            await SendPositionAsync(view, seat, operation.Token);
        }
        finally
        {
            _gate.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        Task disposeTask;
        lock (_disposeLock)
        {
            _disposeTask ??= DisposeCoreAsync();
            disposeTask = _disposeTask;
        }

        return new ValueTask(disposeTask);
    }

    private async Task DisposeCoreAsync()
    {
        _lifetime.Cancel();
        await _gate.WaitAsync();

        try
        {
            var process = _process;
            var input = _input;
            var output = _output;
            _process = null;
            _input = null;
            _output = null;

            if (process is not null)
            {
                try
                {
                    if (ProcessIsRunning(process) && input is not null)
                    {
                        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                        await input.WriteLineAsync("quit".AsMemory(), timeout.Token);
                        await input.FlushAsync(timeout.Token);
                        await process.WaitForExitAsync(timeout.Token);
                    }
                }
                catch (Exception)
                {
                    // The process is forcefully terminated below if graceful shutdown fails.
                }

                try
                {
                    if (ProcessIsRunning(process))
                    {
                        process.Kill(true);
                        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                        await process.WaitForExitAsync(timeout.Token);
                    }
                }
                catch (Exception)
                {
                    // Cleanup is best effort; all local resources are still released.
                }
            }

            DisposeQuietly(input);
            DisposeQuietly(output);
            DisposeQuietly(_errorOutput);
            DisposeQuietly(process);
        }
        finally
        {
            _gate.Release();
            _lifetime.Dispose();
        }
    }

    private CancellationTokenSource CreateOperationCancellation(CancellationToken cancellationToken)
    {
        lock (_disposeLock)
        {
            ThrowIfClosingLocked();
            return CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        }
    }

    private void ThrowIfClosing()
    {
        lock (_disposeLock)
        {
            ThrowIfClosingLocked();
        }
    }

    private void ThrowIfClosingLocked()
    {
        if (_disposeTask is not null)
        {
            throw new ObjectDisposedException(nameof(EngineProcessClient));
        }
    }

    private static bool ProcessIsRunning(Process? process)
    {
        if (process is null)
        {
            return false;
        }

        try
        {
            return !process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static void DisposeQuietly(IDisposable? resource)
    {
        try
        {
            resource?.Dispose();
        }
        catch (Exception)
        {
            // One broken pipe must not prevent the rest of teardown.
        }
    }

    private async Task SendAsync(string command, CancellationToken cancellationToken)
    {
        EnsureRunning();
        await _input!.WriteLineAsync(command.AsMemory(), cancellationToken);
        await _input.FlushAsync(cancellationToken);
    }

    private async Task DrainStandardErrorAsync(StreamReader reader)
    {
        var buffer = new char[1024];
        try
        {
            int count;
            while ((count = await reader.ReadAsync(buffer.AsMemory())) > 0)
            {
                lock (_errorLock)
                {
                    _standardError.Append(buffer, 0, count);
                    // Keep the most recent diagnostics without retaining unbounded logs.
                    if (_standardError.Length > 4096)
                        _standardError.Remove(0, _standardError.Length - 4096);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            // Shutdown may close the pipe while a read is pending.
        }
    }

    private Task SendPositionAsync(GameView view, int seat, CancellationToken cancellationToken)
    {
        var payload = PositionCodec.Encode(new BotPosition(seat, view));
        return SendAsync($"position {payload}", cancellationToken);
    }

    private async Task<string> ReadLineAsync(CancellationToken cancellationToken)
    {
        EnsureRunning();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        try
        {
            return await _output!.ReadLineAsync(timeout.Token)
                ?? throw new ProtocolException("Engine closed its output stream.");
        }
        catch (OperationCanceledException)
        {
            FailChannel();
            if (cancellationToken.IsCancellationRequested)
                throw;
            throw new ProtocolException($"Engine did not respond within {_timeout.TotalSeconds:0.#} seconds.");
        }
    }

    private async Task<string> ReadUntilAsync(Func<string, bool> predicate, CancellationToken cancellationToken)
    {
        while (true)
        {
            var line = await ReadLineAsync(cancellationToken);
            if (predicate(line))
            {
                return line;
            }

            if (line.StartsWith("error ", StringComparison.OrdinalIgnoreCase))
            {
                throw new ProtocolException(line);
            }
        }
    }

    private void FailChannel()
    {
        _channelFailed = true;
        try
        {
            if (ProcessIsRunning(_process)) _process!.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            // The failed channel remains unusable even if the OS cannot kill it.
            // DisposeAsync still owns the resources and attempts final cleanup.
        }
    }

    private void EnsureChannelUsable()
    {
        if (_channelFailed)
            throw new ProtocolException("Engine channel failed after an interrupted response; replace the engine client.");
    }

    private void EnsureRunning()
    {
        EnsureChannelUsable();
        if (!IsRunning || _input is null || _output is null)
        {
            throw new ProtocolException("Engine process is not running.");
        }
    }
}
