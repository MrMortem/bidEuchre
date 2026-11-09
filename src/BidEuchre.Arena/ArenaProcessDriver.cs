using System.Diagnostics;
using System.Text;
using BidEuchre.App;
using BidEuchre.Core;
using BidEuchre.Protocol;

namespace BidEuchre.Arena;

// A process belongs to one seat for one match. Every failed operation permanently poisons it.
public sealed class ArenaProcessDriver : IBotDriver
{
    public const int MaximumStdoutLineBytes = 1024 * 1024;
    public const int MaximumStderrTailBytes = 64 * 1024;
    private readonly EngineSpec spec;
    private readonly BudgetSpec budget;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private readonly byte[] stderrTail = new byte[MaximumStderrTailBytes];
    private readonly byte[] stdoutBuffer = new byte[8192];
    private int stdoutOffset, stdoutCount, stderrOffset, stderrCount;
    private Process? process;
    private Task? stderrDrain;
    private bool poisoned, started;
    private int disposed;

    public ArenaProcessDriver(EngineSpec spec, BudgetSpec budget)
    {
        if (budget.DecisionMs <= 0 || budget.StopMs < budget.DecisionMs || budget.ProcessMs < budget.StopMs || budget.StartupMs <= 0)
            throw new ArgumentException("Budgets must be positive and decision <= stop <= process.");
        this.spec = spec;
        this.budget = budget;
    }

    public string StderrTail
    {
        get
        {
            lock (stderrTail)
            {
                var bytes = new byte[stderrCount];
                for (var index = 0; index < bytes.Length; index++)
                    bytes[index] = stderrTail[(stderrOffset - stderrCount + index + stderrTail.Length) % stderrTail.Length];
                return Encoding.UTF8.GetString(bytes);
            }
        }
    }

    public Task StartAsync(CancellationToken cancellationToken = default) => OperationAsync(budget.StartupMs, async token =>
    {
        if (started) return;
        var info = new ProcessStartInfo(spec.Executable)
        {
            WorkingDirectory = spec.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in spec.Arguments) info.ArgumentList.Add(argument);
        process = Process.Start(info) ?? throw new ProtocolException("Failed to start engine.");
        stderrDrain = DrainStderrAsync(process.StandardError.BaseStream);
        await SendAsync("beuci", token);
        await ReadUntilAsync("beuciok", token);
        await SendAsync("isready", token);
        await ReadUntilAsync("readyok", token);
        started = true;
    }, cancellationToken);

    public Task NewGameAsync(CancellationToken cancellationToken = default) => OperationAsync(budget.ProcessMs,
        async token => { await SendAsync("newgame", token); await ReadyAsync(token); }, cancellationToken);

    public Task ObserveAsync(GameView view, int seat, CancellationToken cancellationToken = default) => OperationAsync(budget.ProcessMs,
        async token =>
        {
            await SendAsync("position " + PositionCodec.Encode(new BotPosition(seat, view)), token);
            await ReadyAsync(token);
        }, cancellationToken);

    public async Task<BotAction> ChooseActionAsync(GameView view, int seat, CancellationToken cancellationToken = default)
    {
        BotAction? result = null;
        await OperationAsync(budget.ProcessMs, async token =>
        {
            await SendAsync("position " + PositionCodec.Encode(new BotPosition(seat, view)), token);
            await SendAsync("go", token);
            while (true)
            {
                var line = await ReadLineAsync(token);
                if (line.Length == 0 || line.StartsWith("info ", StringComparison.OrdinalIgnoreCase)) continue;
                result = BotActionNotation.Parse(line);
                // Validate against the private legal view before handing it to the authority.
                if (!FixtureExporter.LegalActionIds(view).Contains(FixtureExporter.ActionId(result)))
                    throw new ProtocolException("Engine returned an illegal action.");
                return;
            }
        }, cancellationToken);
        return result!;
    }

    private async Task OperationAsync(int milliseconds, Func<CancellationToken, Task> operation, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
        deadline.CancelAfter(milliseconds);
        var entered = false;
        try
        {
            await gate.WaitAsync(deadline.Token);
            entered = true;
            if (poisoned || Volatile.Read(ref disposed) != 0) throw new ObjectDisposedException(nameof(ArenaProcessDriver));
            await operation(deadline.Token);
        }
        catch (Exception exception)
        {
            var timedOut = exception is OperationCanceledException && !cancellationToken.IsCancellationRequested && !lifetime.IsCancellationRequested;
            poisoned = true;
            await KillAsync();
            lifetime.Cancel();
            process?.Dispose();
            process = null;
            if (stderrDrain is not null) await stderrDrain;
            if (timedOut)
                throw new ProtocolException($"Engine operation exceeded {milliseconds} ms.");
            throw;
        }
        finally { if (entered) gate.Release(); }
    }

    private async Task ReadyAsync(CancellationToken token)
    {
        await SendAsync("isready", token);
        await ReadUntilAsync("readyok", token);
    }

    private async Task SendAsync(string line, CancellationToken token)
    {
        if (process is null) throw new ProtocolException("Engine is not started.");
        await process.StandardInput.WriteLineAsync(line.AsMemory(), token);
        await process.StandardInput.FlushAsync(token);
    }

    private async Task ReadUntilAsync(string expected, CancellationToken token)
    {
        while (true)
        {
            var line = await ReadLineAsync(token);
            if (line.Equals(expected, StringComparison.OrdinalIgnoreCase)) return;
            if (line.StartsWith("error ", StringComparison.OrdinalIgnoreCase)) throw new ProtocolException(line);
        }
    }

    private async Task<string> ReadLineAsync(CancellationToken token)
    {
        using var line = new MemoryStream();
        while (true)
        {
            token.ThrowIfCancellationRequested();
            if (stdoutOffset == stdoutCount)
            {
                stdoutCount = await process!.StandardOutput.BaseStream.ReadAsync(stdoutBuffer, token);
                stdoutOffset = 0;
                if (stdoutCount == 0) throw new ProtocolException("Engine closed stdout midresponse.");
            }
            var value = stdoutBuffer[stdoutOffset++];
            if (value == '\n') return Encoding.UTF8.GetString(line.GetBuffer(), 0, (int)line.Length).TrimEnd('\r');
            if (line.Length == MaximumStdoutLineBytes) throw new ProtocolException("Engine stdout line exceeds 1 MiB.");
            line.WriteByte(value);
        }
    }

    private async Task DrainStderrAsync(Stream stream)
    {
        var buffer = new byte[8192];
        try
        {
            int count;
            while ((count = await stream.ReadAsync(buffer, lifetime.Token)) > 0)
            {
                lock (stderrTail)
                {
                    for (var index = 0; index < count; index++)
                    {
                        stderrTail[stderrOffset] = buffer[index];
                        stderrOffset = (stderrOffset + 1) % stderrTail.Length;
                    }
                    stderrCount = Math.Min(stderrTail.Length, stderrCount + count);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException or ObjectDisposedException) { }
    }

    private async Task KillAsync()
    {
        var child = process;
        if (child is null) return;
        try
        {
            if (!child.HasExited) child.Kill(entireProcessTree: true);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            await child.WaitForExitAsync(timeout.Token);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or OperationCanceledException) { }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        lifetime.Cancel();
        await gate.WaitAsync();
        try
        {
            poisoned = true;
            await KillAsync();
            process?.Dispose();
            if (stderrDrain is not null) await stderrDrain;
        }
        finally { gate.Release(); }
    }
}
