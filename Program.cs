using RightMgr.Services;
using RightMgr.Views;

namespace RightMgr;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        Environment.SetEnvironmentVariable("MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY", AppContext.BaseDirectory);

        if (args.Any(x => x.Equals("--print-all", StringComparison.OrdinalIgnoreCase)))
        {
            PrintAll();
            return;
        }

        var app = new App();
        app.InitializeComponent();
        app.Run(new MainWindow(ParseThemeMode(args) ?? AppConfigService.LoadThemeMode() ?? AppThemeMode.System));
    }

    private static AppThemeMode? ParseThemeMode(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.Equals("--light", StringComparison.OrdinalIgnoreCase))
                return AppThemeMode.Light;
            if (arg.Equals("--dark", StringComparison.OrdinalIgnoreCase))
                return AppThemeMode.Dark;
            if (arg.Equals("--system", StringComparison.OrdinalIgnoreCase))
                return AppThemeMode.System;

            string? value = null;
            if (arg.StartsWith("--theme=", StringComparison.OrdinalIgnoreCase))
                value = arg["--theme=".Length..];
            else if (arg.Equals("--theme", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                value = args[++i];

            if (value == null)
                continue;

            return AppConfigService.ParseThemeMode(value);
        }

        return null;
    }

    private static void PrintAll()
    {
        var items = ContextMenuRegistryScanner.ScanAll();
        var text = ContextMenuRegistryScanner.FormatAll(items);
        var output = Path.Combine(Environment.CurrentDirectory, "RightMgr-print-all.txt");
        File.WriteAllText(output, text);

        Console.WriteLine($"RightMgr scanned {items.Count} context menu entries.");
        Console.WriteLine($"Output: {output}");
        Console.WriteLine(text);
    }
}
