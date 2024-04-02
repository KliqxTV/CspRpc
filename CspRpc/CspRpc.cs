using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using DiscordRPC;
using DiscordRPC.Message;

namespace CspRpc;

public static class CspRpc
{
    private static readonly CancellationTokenSource _cts = new CancellationTokenSource();
    private static readonly TimeSpan _interval = TimeSpan.FromSeconds(10);

    private static DiscordRpcClient rpcClient;
    private static Process cspProcess;
    private static string? applicationId = "928158606313000961";

    public static async Task Main(string[] args)
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Dispose();

        Console.WriteLine("This is a tool for running a CSP Discord Presence!");
        Console.WriteLine("Run this instead of CLIP STUDIO PAINT.");
        var started = TryStartCsp();

        if (!started)
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

        // Set the rich presence
        // Call this as many times as you want and anywhere in your code.
        rpcClient.SetPresence(new RichPresence()
        {
            Details = "Drawing",
            State = "",
            Timestamps = Timestamps.Now,
            Buttons =
            [
                new Button()
                {
                    Label = "CSP",
                    Url = "https://www.clipstudio.net/en/"
                }
            ],
            Assets = new Assets()
            {
                LargeImageKey = "paint-new",
                LargeImageText = "Created by Mia!",
                SmallImageKey = ""
            }
        });

        await Monitor();
    }

    private static async void RpcClient_OnReady(object sender, ReadyMessage e)
    {
        Console.WriteLine($"Received ready from user '{e.User.Username}'.");
    }
    private static async void RpcClient_OnPresenceUpdate(object sender, PresenceMessage e)
    {
        Console.WriteLine($"Received update: '{e.Presence}'.");
    }

    private static readonly EnumerationOptions _enumerationOptions = new EnumerationOptions()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true
    };
    private static bool TryStartCsp()
    {
        Console.WriteLine("Discovering CLIP STUDIO PAINT installation(s)...");
        var drives = Environment.GetLogicalDrives();
        string[] programFiles =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        ];

        ConcurrentBag<string> paths = [];
        var locations = drives.SelectMany(drive => programFiles.Select(pf => Path.Combine(drive, pf)));
        Parallel.ForEach(locations, new ParallelOptions() { MaxDegreeOfParallelism = Environment.ProcessorCount }, loc =>
        {
            var executables = Directory.GetFiles(loc, "CLIPStudioPaint.exe", _enumerationOptions);
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
                Console.WriteLine("No CLIP STUDIO PAINT installation found.");
                Console.WriteLine("Please specify it manually now:");
                Console.Write(">>");
                target = Console.ReadLine();
                if (!File.Exists(target))
                {
                    Console.WriteLine("Invalid path.");
                    return false;
                }
                break;
            }
            case 1:
            {
                Console.WriteLine($"Found CLIP STUDIO PAINT at '{target}'.");
                break;
            }
            default:
            {
                Console.WriteLine("Multiple CLIP STUDIO PAINT installations found.");
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

        cspProcess = Process.Start(target);
        return cspProcess is not null;
    }

    private static Task Monitor()
    {
        return Task.Factory.StartNew(() =>
        {
            while (!_cts.Token.WaitHandle.WaitOne(_interval))
            {
                CheckProcess();
            }
        }, _cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    private static void CheckProcess()
    {
        var pname = Process.GetProcessesByName("CLIPStudioPaint");
        if (pname.Length == 0)
        {
            Console.WriteLine("Couldn't find CLIPStudioPaint.exe, exiting...");
            Environment.Exit(0);
        }
        else
        {
            Console.WriteLine("CLIPStudioPaint.exe is still running.");
        }
    }

    private static void Dispose()
    {
        if (!_cts.IsCancellationRequested)
        {
            _cts.Cancel();
            Console.WriteLine("Stopped CLIPStudioPaint.exe process checker.");
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
