using System.Windows;
using Velopack;

namespace SsmsExtensionManager.App;

public partial class App : Application
{
    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build().Run();

        App app = new();
        app.InitializeComponent();
        app.Run();
    }
}
