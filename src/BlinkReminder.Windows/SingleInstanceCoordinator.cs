using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace BlinkReminder.Windows;

public enum InstanceCommand : byte { OpenSettings = 1, Preview = 2 }

/// <summary>One process per user and session, including coexistence of MSIX and EXE channels.</summary>
public sealed class SingleInstanceCoordinator : IAsyncDisposable
{
    private readonly Mutex? lease;
    private readonly CancellationTokenSource shutdown = new();
    private readonly string pipeName;
    private readonly Task? listener;
    private int disposed;
    public bool IsPrimary { get; }
    public event EventHandler<InstanceCommand>? CommandReceived;
    public event EventHandler<Exception>? ConnectionFailed;

    private SingleInstanceCoordinator(string applicationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationId);
        using var identity = WindowsIdentity.GetCurrent();
        string sid = identity.User?.Value ?? throw new InvalidOperationException("user_sid_missing");
        string scope = CreateScope(applicationId, sid, Process.GetCurrentProcess().SessionId);
        pipeName = scope;
        // The named object's lifetime is the lease. No thread owns the mutex: Windows
        // removes it when the primary exits, including crashes and async shutdown.
        var candidate = new Mutex(false, @"Local\" + scope, out bool createdNew);
        IsPrimary = createdNew;
        if (IsPrimary) { lease = candidate; listener = ListenAsync(); }
        else candidate.Dispose();
    }

    public static SingleInstanceCoordinator TryAcquire(string applicationId = "BlinkReminder") => new(applicationId);

    public static string CreateScope(string applicationId, string sid, int sessionId)
    {
        string key = $"{applicationId}\n{sid}\n{sessionId}";
        return "BlinkReminder." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
    }

    public static bool IsKnownCommand(byte value) => value is (byte)InstanceCommand.OpenSettings or (byte)InstanceCommand.Preview;

    public async Task<bool> SendAsync(InstanceCommand command, CancellationToken cancellationToken = default)
    {
        if (!IsKnownCommand((byte)command)) return false;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await client.ConnectAsync(timeout.Token).ConfigureAwait(false);
            await client.WriteAsync(new byte[] { (byte)command }, timeout.Token).ConfigureAwait(false);
            await client.FlushAsync(timeout.Token).ConfigureAwait(false);
            var response = new byte[1];
            return await client.ReadAsync(response, timeout.Token).ConfigureAwait(false) == 1 && response[0] == 1;
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or UnauthorizedAccessException) { return false; }
    }

    private async Task ListenAsync()
    {
        while (!shutdown.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, 64, 64);
                await server.WaitForConnectionAsync(shutdown.Token).ConfigureAwait(false);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(2));
                var request = new byte[1];
                bool accepted = await server.ReadAsync(request, timeout.Token).ConfigureAwait(false) == 1 && IsKnownCommand(request[0]);
                if (accepted) CommandReceived?.Invoke(this, (InstanceCommand)request[0]);
                await server.WriteAsync(new byte[] { accepted ? (byte)1 : (byte)0 }, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { break; }
            catch (OperationCanceledException) { } // A local client connected without sending a command.
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ConnectionFailed?.Invoke(this, ex);
                try { await Task.Delay(TimeSpan.FromSeconds(1), shutdown.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        await shutdown.CancelAsync().ConfigureAwait(false);
        if (listener is not null) await listener.ConfigureAwait(false);
        lease?.Dispose();
        shutdown.Dispose();
    }
}
