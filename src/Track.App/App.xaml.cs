using System.Diagnostics.CodeAnalysis;
using System.Windows;
using Track.App.Shell;
using Track.Core.Settings;

namespace Track.App;

/// <summary>Starts Track once per user session: no main window, just the tray icon.</summary>
[ExcludeFromCodeCoverage(Justification = "Application entry point.")]
public partial class App : Application
{
    private const string MutexName = @"Local\Track.TrayApp.SingleInstance";

    private Mutex? _single;
    private TrayController? _controller;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var options = AppOptions.From(e.Args);

        if (options.RenderIconsTo is { } dir)
        {
            _controller = new TrayController(options, Dispatcher);
            _controller.RenderIcons(dir);
            Shutdown();
            return;
        }

        _single = new Mutex(initiallyOwned: true, MutexName, out var first);
        if (!first)
        {
            // Track is already running for this user.
            _single.Dispose();
            _single = null;
            Shutdown();
            return;
        }

        // A tray app should survive a bad moment: log unexpected UI errors and keep running.
        DispatcherUnhandledException += (_, args) =>
        {
            try
            {
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(options.DataDirectory, "errors.log"),
                    $"{DateTimeOffset.Now:O} {args.Exception}{Environment.NewLine}");
            }
            catch (System.IO.IOException)
            {
                // Nowhere to write; still keep running.
            }

            args.Handled = true;
        };

        _controller = new TrayController(options, Dispatcher);
        _controller.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _controller?.Dispose();
        if (_single is not null)
        {
            _single.ReleaseMutex();
            _single.Dispose();
        }

        base.OnExit(e);
    }
}
