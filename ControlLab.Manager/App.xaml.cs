using System.Windows;

namespace ControlLab.Manager;

public partial class App : Application
{
    private ControlLabServer? _server;

    protected override void OnStartup(
        StartupEventArgs e)
    {
        base.OnStartup(e);

        // ==========================================
        // INICIAR SERVIDOR CONTROL LAB
        // ==========================================

        _server =
            new ControlLabServer();

        _ = _server.StartAsync();

        // ==========================================
        // ABRIR MANAGER
        // ==========================================

        var mainWindow =
            new MainWindow();

        MainWindow = mainWindow;

        mainWindow.Show();
    }

    protected override void OnExit(
        ExitEventArgs e)
    {
        // ==========================================
        // DETENER SERVIDOR
        // ==========================================

        _server?.Stop();

        base.OnExit(e);
    }
}