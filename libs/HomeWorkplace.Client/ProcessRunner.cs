using System.Diagnostics;
using System.Text;

namespace HomeWorkplace.Client;

/// <summary>The real <see cref="IProcessRunner"/>: System.Diagnostics.Process with captured output.</summary>
public sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(string command, IReadOnlyList<string> args, TimeSpan timeout, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = command,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        process.Start();   // throws Win32Exception when the executable is not found
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
            return new ProcessResult(process.ExitCode, stdout.ToString(), stderr.ToString(), false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return new ProcessResult(-1, stdout.ToString(), stderr.ToString(), true);
        }
    }

    public IProcessHandle Start(string command, IReadOnlyList<string> args, string? workingDirectory, IReadOnlyDictionary<string, string?> environment)
    {
        var psi = new ProcessStartInfo
        {
            FileName = command,
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.Environment.Clear();
        foreach (var (k, v) in environment) psi.Environment[k] = v;

        var process = new Process { StartInfo = psi };
        var handle = new Handle(process);
        process.OutputDataReceived += (_, e) => handle.Append(e.Data);
        process.ErrorDataReceived += (_, e) => handle.Append(e.Data);
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return handle;
    }

    private sealed class Handle : IProcessHandle
    {
        private const int Keep = 50;
        private readonly Process _process;
        private readonly LinkedList<string> _lines = new();
        private readonly object _gate = new();

        public Handle(Process process) => _process = process;

        public int Id => _process.Id;
        public bool HasExited { get { try { return _process.HasExited; } catch { return true; } } }

        public IReadOnlyList<string> RecentOutput
        {
            get { lock (_gate) return _lines.ToArray(); }
        }

        public void Append(string? line)
        {
            if (line is null) return;
            lock (_gate)
            {
                _lines.AddLast(line);
                while (_lines.Count > Keep) _lines.RemoveFirst();
            }
        }

        public void Kill()
        {
            try { if (!_process.HasExited) _process.Kill(entireProcessTree: true); } catch { }
        }

        public void Dispose() => _process.Dispose();
    }
}
