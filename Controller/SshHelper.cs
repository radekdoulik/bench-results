using System.Diagnostics;
using System.Text;

namespace Controller;

public class SshHelper : IDisposable
{
    readonly Process sshProcess = new();
    public StringBuilder output = new();
    public StringBuilder errors = new();
    int lineCount;

    public delegate void OutputFilter(string line);
    public delegate void ErrorFilter(string line);

    public OutputFilter? outputFilter;
    public ErrorFilter? errorFilter;

    public static readonly string Command = "ssh";

    private bool disposed = false;

    protected virtual void Dispose(bool disposing)
    {
        if (!this.disposed)
        {
            if (disposing)
            {
                // Dispose managed resources.
                sshProcess?.Dispose();
            }

            // Note disposing has been done.
            disposed = true;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    internal async Task<bool> OpenTunnel()
    {
        return await Run("-L localhost:8123:localhost:8123 192.168.2.173 ls");
    }

    internal async Task<bool> Run(string arguments)
    {
        var si = sshProcess.StartInfo;
        si.FileName = Command;
        si.Arguments = arguments;
        Console.WriteLine($"Running: {ANSIColor.Color(Color.Cyan)}{Command} {arguments}{ANSIColor.Reset}");
        si.RedirectStandardOutput = true;
        si.RedirectStandardError = true;
        si.CreateNoWindow = true;

        sshProcess.OutputDataReceived += new DataReceivedEventHandler((sender, e) =>
        {
            output.AppendLine(e.Data);
            // Prepend line numbers to each line of the output.
            if (!String.IsNullOrEmpty(e.Data))
            {
                if (outputFilter != null)
                    outputFilter(e.Data);
                lineCount++;
                //Console.WriteLine($"ssh[{lineCount,6}]: {e.Data}");
            }
        });

        sshProcess.ErrorDataReceived += new DataReceivedEventHandler((sender, e) =>
        {
            errors.AppendLine(e.Data);
            // Prepend line numbers to each line of the output.
            if (!String.IsNullOrEmpty(e.Data))
            {
                if (errorFilter != null)
                    errorFilter(e.Data);
                Console.WriteLine($"{ANSIColor.Color(Color.Red)}ssh error: {e.Data}{ANSIColor.Reset}");
            }
        });

        var success = sshProcess.Start();
        if (!success)
            return false;

        sshProcess.BeginOutputReadLine();
        sshProcess.BeginErrorReadLine();

        await sshProcess.WaitForExitAsync();

        await sshProcess.WaitForExitAsync();
        var exitCode = sshProcess.ExitCode;
        sshProcess.Close();

        return exitCode == 0;
    }

    public void Stop() => sshProcess.Kill();
}

