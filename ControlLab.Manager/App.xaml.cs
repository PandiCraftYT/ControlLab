using System.Windows;
using ControlLab.Manager.Services;
using ControlLab.Manager.Views;

namespace ControlLab.Manager;

public partial class App : Application
{
    private ControlLabServer? _server;

    private AuthenticationService? _authentication;

    private string? _sessionToken;

    private string? _username;

    // =========================================================
    // INICIO DE CONTROL LAB
    // =========================================================

    protected override void OnStartup(
        StartupEventArgs e)
    {
        base.OnStartup(e);

        // Evita que WPF cierre la aplicación cuando
        // LoginWindow se cierre después de iniciar sesión.
        ShutdownMode =
            ShutdownMode.OnExplicitShutdown;

        try
        {
            // =================================================
            // SERVICIO DE AUTENTICACIÓN
            // =================================================

            _authentication =
                new AuthenticationService();

            // =================================================
            // MOSTRAR LOGIN
            // =================================================

            var loginWindow =
                new LoginWindow(
                    _authentication
                );

            bool? loginResult =
                loginWindow.ShowDialog();

            // =================================================
            // LOGIN CANCELADO
            // =================================================

            if (
                loginResult != true ||
                string.IsNullOrWhiteSpace(
                    loginWindow.SessionToken
                )
            )
            {
                Shutdown();
                return;
            }

            // =================================================
            // GUARDAR SESIÓN
            // =================================================

            _sessionToken =
                loginWindow.SessionToken;

            _username =
                loginWindow.Username;

            ControlLabApi.SetSessionToken(
                _sessionToken
            );

            Console.WriteLine(
                $"🔐 Sesión iniciada: {_username}"
            );

            // =================================================
            // INICIAR SERVIDOR
            //
            // IMPORTANTE:
            // Pasamos LA MISMA instancia de
            // AuthenticationService que utilizó LoginWindow.
            // =================================================

            if (_authentication == null)
            {
                throw new InvalidOperationException(
                    "El servicio de autenticación no está disponible."
                );
            }

            _server =
                new ControlLabServer(
                    _sessionToken,
                    _authentication
                );

            _ = StartServerAsync();

            // =================================================
            // ABRIR VENTANA PRINCIPAL
            // =================================================

            var mainWindow =
                new MainWindow();

            MainWindow =
                mainWindow;

            ShutdownMode =
                ShutdownMode.OnMainWindowClose;

            mainWindow.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "ControlLab no pudo iniciarse.\n\n" +
                ex.Message,
                "ControlLab",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );

            Shutdown();
        }
    }

    // =========================================================
    // INICIAR SERVIDOR
    // =========================================================

    private async Task StartServerAsync()
    {
        try
        {
            if (_server == null)
                return;

            await _server.StartAsync();
        }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(
                () =>
                {
                    MessageBox.Show(
                        "El servidor de ControlLab no pudo iniciarse.\n\n" +
                        ex.Message,
                        "ControlLab",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );
                }
            );
        }
    }

    // =========================================================
    // CERRAR CONTROL LAB
    // =========================================================

    protected override void OnExit(
        ExitEventArgs e)
    {
        // =====================================================
        // CERRAR SESIÓN
        // =====================================================

        try
        {
            if (
                _authentication != null &&
                !string.IsNullOrWhiteSpace(
                    _sessionToken
                )
            )
            {
                _authentication.Logout(
                    _sessionToken
                );

                _sessionToken = null;
                _username = null;
            }
        }
        catch
        {
            // No impedir el cierre.
        }

        // =====================================================
        // DETENER SERVIDOR
        // =====================================================

        try
        {
            _server?.Stop();
        }
        catch
        {
            // No impedir el cierre.
        }

        base.OnExit(e);
    }
}