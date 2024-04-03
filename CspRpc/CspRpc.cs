using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.IO;

using CspRpc.HttpClients.TempFileHostClient;
using CspRpc.Util;

using DiscordRPC;
using DiscordRPC.Message;

using LaquaiLib.Util;

using Microsoft.Extensions.DependencyInjection;

using Button = DiscordRPC.Button;

namespace CspRpc;

public static class CspRpc
{
    private static readonly CancellationTokenSource _cts = new CancellationTokenSource();
    private static readonly TimeSpan _interval = TimeSpan.FromSeconds(10);

    private static DiscordRpcClient rpcClient;
    private static string? applicationId = "928158606313000961";

    private const string targetExecutableArgPrefix = "--targetProcess=";
    private static string targetExecutableName = "CLIPStudioPaint.exe";
    private static Process targetProcess;

    private static bool doNotLaunch;
    private static bool doNotFallbackToExistingProcess;

    private static ITempFileHostClient tempFileHostClient;

    private const bool fakeRunningCsp = false;

    public static async Task Main(string[] args)
    {
        // Service setup
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton<ITempFileHostClient>(_ => TempFileHostClient.Instance);
        var serviceProvider = serviceCollection.BuildServiceProvider();
        tempFileHostClient = serviceProvider.GetRequiredService<ITempFileHostClient>();

        // Parse and use args
        {
            if (Array.Exists(args, a => a.Equals("--help", StringComparison.OrdinalIgnoreCase)))
            {
                Console.WriteLine($"""
                    Usage: CspRpc [options]

                    Options:
                        --help
                            Show this help message.

                        --targetProcess=<processName>
                            Instead of monitoring {targetExecutableName}, monitor the specified process.
                        --do-not-launch
                            Disables the default behavior of launching the target executable if it's not running.
                            CspRpc will exit if the target executable isn't running.
                        --do-not-fallback-to-existing
                            Disables the default behavior of falling back to an existing process instead of launching the configured target executable.
                    """);
                Environment.Exit(0);
            }
        }
        {
            if (Array.Find(args, a => a.StartsWith(targetExecutableArgPrefix, StringComparison.OrdinalIgnoreCase)) is string targetExecutableArg
                && targetExecutableArg[targetExecutableArgPrefix.Length..] is var targetExecutableParsed
                && !string.IsNullOrWhiteSpace(targetExecutableParsed))
            {
                targetExecutableName = targetExecutableParsed;
            }
        }
        {
            doNotLaunch = Array.Exists(args, a => a.Equals("--do-not-launch", StringComparison.OrdinalIgnoreCase));
        }
        {
            doNotFallbackToExistingProcess = Array.Exists(args, a => a.Equals("--do-not-fallback-to-existing", StringComparison.OrdinalIgnoreCase));
        }

        AppDomain.CurrentDomain.ProcessExit += (_, _) => Dispose();

        Console.WriteLine("This is a tool for running a CSP Discord Presence!");
        Console.WriteLine($"Run this instead of {targetExecutableName}.");
        var started = TryStartCsp();

        if (!started && !fakeRunningCsp)
        {
            Console.WriteLine("Something went wrong. Exiting...");
            return;
        }

        if (string.IsNullOrWhiteSpace(applicationId))
        {
            Console.WriteLine("There's no Discord Application ID preconfigured.");
            Console.WriteLine("Please specify it now:");
            Console.Write(">>");
            applicationId = Console.ReadLine();
        }

        rpcClient = new DiscordRpcClient(applicationId);
        rpcClient.OnReady += RpcClient_OnReady;
        rpcClient.OnPresenceUpdate += RpcClient_OnPresenceUpdate;

        // Connect to the RPC
        rpcClient.Initialize();

        await Task.Run(async () =>
        {
            while (true)
            {
                CheckProcess();
                await HandleState();
                try
                {
                    await Task.Delay(_interval, _cts.Token);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        });
    }

    private static readonly RichPresence _richPresence = new RichPresence()
    {
        Details = "Drawing",
        State = "",
        Timestamps = Timestamps.Now,
        Buttons =
        [
            new Button()
            {
                Label = "My Art",
                Url = "https://patreon.com/KorOwOne"
            }
        ],
        Assets = new Assets()
        {
            LargeImageKey = "",
            LargeImageText = "KorOwOne",
            SmallImageKey = "paint-new",
            SmallImageText = "CLIP STUDIO PAINT"
        }
    };

    private static string lastOpenFile = "";
    private static async Task HandleState()
    {
        // newOpenFile should come from Path.GetFileName and not be the full path
        var newOpenFile = "";

        if (lastOpenFile?.Equals(newOpenFile, StringComparison.OrdinalIgnoreCase) == false)
        {
            // Either no rich presence was set or the file changed
            _richPresence.Timestamps = Timestamps.Now;
            _richPresence.State = newOpenFile;
        }

        using (var bmp = ScreenCapture.Capture(targetProcess.MainWindowHandle))
        await using (var ms = new MemoryStream())
        {
            bmp.Save(ms, ImageFormat.Png);
            ms.Seek(0, SeekOrigin.Begin);
            // TODO: Change this to use https://temp.sh/
            var url = await tempFileHostClient.Upload(ms);
            _richPresence.Assets.LargeImageKey = url;
        }

        rpcClient.SetPresence(_richPresence);

        lastOpenFile = _richPresence.State;
    }

    private static async void RpcClient_OnReady(object sender, ReadyMessage e)
    {
        Console.WriteLine($"Received ready from user '{e.User.Username}'.");
    }
    private static async void RpcClient_OnPresenceUpdate(object sender, PresenceMessage e)
    {
        Console.WriteLine($"Presence updated: '{e.Presence}'.");
    }

    private static readonly EnumerationOptions _enumerationOptions = new EnumerationOptions()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true
    };
    private static bool TryStartCsp()
    {
        if (fakeRunningCsp)
        {
            return true;
        }

        if (doNotLaunch)
        {
            #region Finding an existing instance
            var processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(targetExecutableName)).ToFrozenDictionary(p => p.Id, p => (Process: p, CommandLine: p.GetCommandLine()));

            switch (processes.Count)
            {
                case 0:
                {
                    Console.WriteLine($"{targetExecutableName} isn't running.");
                    return false;
                }
                case 1:
                {
                    targetProcess = processes.First().Value.Process;
                    Console.WriteLine($"Found one instance of {targetExecutableName}.");
                    break;
                }
                default:
                {
                    Console.WriteLine($"Multiple {targetExecutableName} instances found.");
                    Console.WriteLine("Please choose the one you want by its PID now (check the process's command-line to make sure you get the right one, it's usually the one with the shortest and least amount argument junk in it):");
                    var pidPaddingWidth = processes.Keys.Max().ToString().Length;
                    foreach (var (pid, (process, commandLine)) in processes.OrderBy(kv => kv.Value.CommandLine.Length))
                    {
                        Console.WriteLine($"[{pid.ToString().PadLeft(pidPaddingWidth)}] {targetExecutableName} '{commandLine}'");
                    }

                    Console.Write(">>");
                    var target = Console.ReadLine();
                    if (int.TryParse(target, out var targetPid) && targetPid >= 0)
                    {
                        targetProcess = processes[targetPid].Process;
                    }
                    else
                    {
                        Console.WriteLine("Invalid input.");
                        return false;
                    }
                    break;
                }
            }
            #endregion
        }
        else
        {
            #region Finding the executable
            Console.WriteLine($"Searching for {targetExecutableName}...");

            HashSet<string> locations = [];
            var drives = Environment.GetLogicalDrives();
            string[] programFiles =
            [
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
            ];
            foreach (var drive in drives)
            {
                foreach (var pf in programFiles)
                {
                    locations.Add(Path.Combine(drive, pf));
                }
            }

            ConcurrentBag<string> paths = [];
            Parallel.ForEach(locations, new ParallelOptions() { MaxDegreeOfParallelism = 1 /*Environment.ProcessorCount*/ }, loc =>
            {
                var executables = Directory.GetFiles(loc, targetExecutableName, _enumerationOptions);
                foreach (var exe in executables)
                {
                    paths.Add(exe);
                }
            });

            var target = paths.FirstOrDefault();
            switch (paths.Count)
            {
                case 0:
                {
                    Console.WriteLine($"No {targetExecutableName} installation found.");
                    if (doNotFallbackToExistingProcess)
                    {
                        Console.WriteLine("Please specify it manually now:");
                        Console.Write(">>");
                        target = Console.ReadLine();
                        if (!File.Exists(target))
                        {
                            Console.WriteLine("Invalid path.");
                            return false;
                        }
                    }
                    else
                    {
                        Console.WriteLine("Trying to use an existing instance of it instead...");
                        doNotLaunch = true;
                        return TryStartCsp();
                    }
                    break;
                }
                case 1:
                {
                    Console.WriteLine($"Found {targetExecutableName} at '{target}'.");
                    break;
                }
                default:
                {
                    Console.WriteLine($"Multiple {targetExecutableName} installations found.");
                    Console.WriteLine("Please choose the one you want:");
                    var enumerated = paths.ToArray();
                    for (var i = 0; i < enumerated.Length; i++)
                    {
                        var path = enumerated[i];
                        Console.WriteLine($"[{i}] {path}");
                    }
                    target = Console.ReadLine();
                    if (int.TryParse(target, out var value) && value >= 0)
                    {
                        target = enumerated[value];
                    }
                    else
                    {
                        Console.WriteLine("Invalid input.");
                        return false;
                    }
                    break;
                }
            }

            targetProcess = Process.Start(target);
            #endregion
        }

        return targetProcess is not null;
    }

    private static void CheckProcess()
    {
        if (fakeRunningCsp)
        {
            Console.WriteLine("Running CSP is being faked to test Rich Presence...", ConsoleColor.DarkYellow);
            return;
        }

        var pname = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(targetExecutableName));
        if (pname.FirstOrDefault() is Process p)
        {
            targetProcess = p;
            Console.WriteLine($"{targetExecutableName} is still running.");
        }
        else
        {
            Console.WriteLine($"Couldn't find {targetExecutableName}, exiting...");
            Environment.Exit(0);
        }
    }

    private static void Dispose()
    {
        if (!_cts.IsCancellationRequested)
        {
            _cts.Cancel();
            Console.WriteLine($"Stopped {targetExecutableName} process checker.");
        }
        if (rpcClient is not null)
        {
            rpcClient.Dispose();
            Console.WriteLine("Stopped DiscordPresence.");
        }
    }
}

internal static class Console
{
    internal static void WriteLine<T>(T value) => System.Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] " + value);
    internal static void WriteLine<T>(T value, ConsoleColor color)
    {
        System.Console.ForegroundColor = color;
        WriteLine(value);
        System.Console.ResetColor();
    }
    internal static void WriteLine() => System.Console.WriteLine();
    internal static void Write<T>(T value) => System.Console.Write(value);
    internal static string ReadLine() => System.Console.ReadLine();
}
