using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Windows.Threading;

namespace Shammy.Infrastructure;

/// <summary>
/// A local named pipe that accepts one-line commands such as "expand" or "search what is a notch",
/// for scripted end-to-end tests. Off unless SHAMMY_TEST_PIPE=1 is set when Shammy starts, because the
/// commands can read files and drive the app. Even then only the current user on this machine can
/// connect: network (SMB) clients are denied by the pipe's ACL.
/// </summary>
internal sealed class CommandServer : IDisposable
{
    public const string PipeName = "Shammy.Commands";
    const int MaxLineChars = 4096;

    readonly Dispatcher _ui;
    readonly Func<string, string> _handler;
    readonly CancellationTokenSource _cts = new();

    public static bool IsEnabled => Environment.GetEnvironmentVariable("SHAMMY_TEST_PIPE") == "1";

    public CommandServer(Dispatcher ui, Func<string, string> handler)
    {
        _ui = ui;
        _handler = handler;
        _ = Task.Run(LoopAsync);
    }

    static PipeSecurity Security()
    {
        var security = new PipeSecurity();
        var me = WindowsIdentity.GetCurrent().User!;
        security.SetOwner(me);
        security.AddAccessRule(new PipeAccessRule(me, PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance, AccessControlType.Allow));
        // Deny wins over allow: same user logging in over the network still can't reach it.
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
        return security;
    }

    async Task LoopAsync()
    {
        int failures = 0;
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                // FirstPipeInstance: if another process grabbed the name first, fail rather than share it.
                await using var pipe = NamedPipeServerStreamAcl.Create(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.FirstPipeInstance, 0, 0, Security());
                await pipe.WaitForConnectionAsync(_cts.Token);
                failures = 0;

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                using var reader = new StreamReader(pipe, leaveOpen: true);
                await using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
                var line = await reader.ReadLineAsync(timeout.Token);
                if (line is null || line.Length > MaxLineChars) continue;
                string reply;
                try { reply = await _ui.InvokeAsync(() => _handler(line.Trim())); }
                catch (Exception ex) { reply = "error: " + ex.Message; }
                await writer.WriteLineAsync(reply);
            }
            catch (OperationCanceledException) when (_cts.IsCancellationRequested)
            {
                return;
            }
            catch (OperationCanceledException)
            {
                // A client that never sent a line; drop it.
            }
            catch (Exception ex)
            {
                if (++failures == 1 || failures % 20 == 0) Log.Warn($"Command pipe error ({failures})", ex);
                if (failures >= 60) return;
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, failures)));
            }
        }
    }

    public void Dispose() => _cts.Cancel();
}
