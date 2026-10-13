using System.Runtime.CompilerServices;
using Stagecraft.Interop;
using Stagecraft.Services;

namespace Stagecraft;

internal static class Program
{
    /// <summary>
    /// Entry point. A second launch (a double-clicked deck, the Start menu) hands its command line to the
    /// Stagecraft that is already running and exits before any window code is loaded.
    /// </summary>
    [STAThread]
    public static int Main(string[] args)
    {
        var command = Command.Parse(args);
        if (command.CanHandOver)
        {
            // Let the running Stagecraft bring its window to the front for us.
            Native.AllowAnyForeground();
            if (!InstanceManager.TryBecomePrimary(command.ToArgs())) return 0;
        }
        try
        {
            return RunApp(command);
        }
        finally
        {
            InstanceManager.Stop();
        }
    }

    // Kept out of Main so a process that hands over exits without loading WPF.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int RunApp(Command command)
    {
        var app = new App(command);
        app.InitializeComponent();
        return app.Run();
    }
}
