using System.Diagnostics;

namespace DeveloperPlatform.E2ETests.Infrastructure;

public sealed class AppProcess : IAsyncDisposable
{
    private readonly Process _process;

    private AppProcess(Process process) => _process = process;

    public static AppProcess Start(string projectPath, IReadOnlyDictionary<string, string> env)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            ArgumentList = { "run", "--project", projectPath, "--no-launch-profile" },
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var (k, v) in env)
        {
            psi.Environment[k] = v;
        }

        var p = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start {projectPath}");
        p.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                Console.WriteLine(e.Data);
            }
        };
        p.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                Console.Error.WriteLine(e.Data);
            }
        };
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        return new AppProcess(p);
    }

    public static async Task WaitUntilReadyAsync(string url, TimeSpan timeout)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var res = await http.GetAsync(url);
                if ((int)res.StatusCode < 500)
                {
                    return;
                }
            }
            catch
            {
                // not up yet
            }

            await Task.Delay(1000);
        }

        throw new TimeoutException($"{url} not ready within {timeout}.");
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // best-effort teardown
        }

        await Task.CompletedTask;
        _process.Dispose();
    }
}
