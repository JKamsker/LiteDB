using System.Diagnostics;

namespace WriteControllerExperiments;

internal sealed class ChildProcess : IDisposable
{
    internal Process Process { get; }
    private readonly Task<string> _error;
    internal ChildProcess(params string[] args)
    {
        var info = new ProcessStartInfo("dotnet")
        {
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        info.ArgumentList.Add(typeof(ChildProcess).Assembly.Location);
        foreach (var arg in args) info.ArgumentList.Add(arg);
        Process = Process.Start(info)!;
        _error = Process.StandardError.ReadToEndAsync();
    }

    internal string ReadLine() => Process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(30)).GetAwaiter().GetResult()
        ?? throw new Exception("Child exited: " + _error.GetAwaiter().GetResult());

    internal string Finish()
    {
        Process.StandardInput.WriteLine("stop");
        var output = Process.StandardOutput.ReadToEndAsync();
        if (!Process.WaitForExit(30000)) throw new TimeoutException("Child did not drain.");
        if (Process.ExitCode != 0) throw new Exception(_error.GetAwaiter().GetResult());
        return output.GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        if (!Process.HasExited) { Process.Kill(true); Process.WaitForExit(); }
        Process.Dispose();
    }
}
