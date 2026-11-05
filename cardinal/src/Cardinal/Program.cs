using System.Runtime.InteropServices;
using System.Windows;

namespace Cardinal;

static partial class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length > 0)
            return Cli.Run(args);

        HideOwnConsole();
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        return app.Run(new MainWindow());
    }

    // Double-clicking the exe opens a console just for it; hide that, but never a terminal it was started from.
    static void HideOwnConsole()
    {
        var processes = new uint[2];
        if (GetConsoleProcessList(processes, 2) == 1)
        {
            ShowWindow(GetConsoleWindow(), 0);
            FreeConsole();
        }
    }

    [LibraryImport("kernel32.dll")]
    private static partial uint GetConsoleProcessList([Out] uint[] processList, uint count);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr GetConsoleWindow();

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FreeConsole();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(IntPtr window, int command);
}
