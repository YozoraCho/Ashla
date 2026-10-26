using System.ComponentModel;
using System.Diagnostics;
using Ashla.Host.Settings;

namespace Ashla.Host.Launch;

internal interface IProcessStarter
{
    void Start(ProcessStartInfo startInfo);
    bool IsRunning(string processName);
}

internal sealed class SystemProcessStarter : IProcessStarter
{
    public void Start(ProcessStartInfo startInfo)
    {
        using var _ = Process.Start(startInfo);
    }

    public bool IsRunning(string processName)
    {
        var processes = Process.GetProcessesByName(processName);
        foreach (var process in processes) process.Dispose();
        return processes.Length > 0;
    }
}

internal sealed record LaunchResult(bool Ok, string Message)
{
    public static LaunchResult Success(string message) => new(true, message);
    public static LaunchResult Failure(string message) => new(false, message);
}

internal sealed class GameLauncher(IProcessStarter processes)
{
    public const string GameProcessName = "swtor";

    public bool IsGameRunning => processes.IsRunning(GameProcessName);

    public LaunchResult Launch(GameLaunchMethod method, SwtorInstallation? installation)
    {
        if (IsGameRunning) return LaunchResult.Success("SWTOR is already running.");

        ProcessStartInfo startInfo;
        string started;
        switch (method)
        {
            case GameLaunchMethod.Manual:
                return LaunchResult.Success("Start SWTOR whenever you're ready. Ashla appears over it automatically.");

            case GameLaunchMethod.Steam:
                startInfo = new ProcessStartInfo($"steam://rungameid/{SwtorInstallFinder.SteamAppId}") { UseShellExecute = true };
                started = "Asked Steam to start SWTOR.";
                break;

            case GameLaunchMethod.SwtorLauncher:
                if (installation is null)
                    return LaunchResult.Failure("Ashla doesn't know where SWTOR is installed. Set its folder in Settings.");
                startInfo = new ProcessStartInfo(installation.LauncherPath)
                {
                    UseShellExecute = true,
                    WorkingDirectory = installation.Folder
                };
                started = "Started the SWTOR launcher.";
                break;

            default:
                return LaunchResult.Failure($"Unknown way of starting SWTOR: {method}.");
        }

        try
        {
            processes.Start(startInfo);
            return LaunchResult.Success(started);
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            return LaunchResult.Failure(method == GameLaunchMethod.Steam
                ? $"Steam couldn't be started ({error.Message}). Is Steam installed?"
                : $"The SWTOR launcher couldn't be started ({error.Message}).");
        }
    }

    public static GameLaunchMethod Suggest(SwtorInstallation? installation) => installation switch
    {
        { IsSteam: true } => GameLaunchMethod.Steam,
        not null => GameLaunchMethod.SwtorLauncher,
        null => GameLaunchMethod.Manual
    };
}
