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
        app.Run(new MainWindow());
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
