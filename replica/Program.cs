namespace KeyMouseSyncReplica;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        System.Windows.Forms.Application.EnableVisualStyles();
        System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);

        var app = new App();
        app.InitializeComponent();

        app.Run();
    }
}
