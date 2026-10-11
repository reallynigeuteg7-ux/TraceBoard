namespace TraceBoard;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Theme.Initialize();
        Application.Run(new MainForm());
        Theme.Release();
    }
}
