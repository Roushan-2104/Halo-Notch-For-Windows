using System.IO.Pipes;
using System.Windows.Threading;

namespace Halo.Infrastructure;

/// <summary>
/// A local named pipe (current user only) that accepts one-line commands such as "expand" or
/// "search what is a notch". Used for scripted end-to-end tests and future integrations.
/// </summary>
internal sealed class CommandServer : IDisposable
{
    public const string PipeName = "Halo.Commands";

    readonly Dispatcher _ui;
    readonly Func<string, string> _handler;
    readonly CancellationTokenSource _cts = new();

    public CommandServer(Dispatcher ui, Func<string, string> handler)
    {
        _ui = ui;
        _handler = handler;
        _ = Task.Run(LoopAsync);
    }

    async Task LoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_cts.Token);
                using var reader = new StreamReader(pipe, leaveOpen: true);
                await using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
                var line = await reader.ReadLineAsync(_cts.Token);
                if (line is null) continue;
                string reply;
                try { reply = await _ui.InvokeAsync(() => _handler(line.Trim())); }
                catch (Exception ex) { reply = "error: " + ex.Message; }
                await writer.WriteLineAsync(reply);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                Log.Warn("Command pipe error", ex);
                await Task.Delay(500);
            }
        }
    }

    public void Dispose() => _cts.Cancel();
}
