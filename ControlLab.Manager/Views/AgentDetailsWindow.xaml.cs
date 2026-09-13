using System.Windows;
using System.Windows.Media;

using ControlLab.Manager.Models;
using ControlLab.Manager.Services;

namespace ControlLab.Manager.Views;

public partial class AgentDetailsWindow : Window
{
    private readonly AgentInfo? _agent;

    private readonly string _machineId;

    private readonly ControlLabApi _api;

    public AgentDetailsWindow(
        string machineId,
        AgentInfo? agent,
        ControlLabApi api)
    {
        InitializeComponent();

        _machineId =
            machineId;

        _agent =
            agent;

        _api =
            api;

        Title =
            $"ControlLab - {_machineId}";

        LoadAgentInformation();
    }

    // ==========================================
    // CARGAR INFORMACIÓN
    // ==========================================

    private void LoadAgentInformation()
    {
        bool online =
            _agent != null &&
            _agent.Status.Equals(
                "online",
                StringComparison.OrdinalIgnoreCase
            );

        MachineIdText.Text =
            _machineId;

        StatusText.Text =
            online
                ? "●  En línea"
                : "●  Desconectada";

        StatusText.Foreground =
            online
                ? new SolidColorBrush(
                    Color.FromRgb(
                        50,
                        213,
                        131
                    )
                )
                : new SolidColorBrush(
                    Color.FromRgb(
                        249,
                        112,
                        102
                    )
                );

        HostnameText.Text =
            online
                ? _agent!.Hostname
                : "Sin conexión";

        PlatformText.Text =
            online
                ? _agent!.Platform
                : "No disponible";

        VersionText.Text =
            online
                ? _agent!.AgentVersion
                : "No disponible";

        HeartbeatText.Text =
            online
                ? _agent!.LastHeartbeat
                : "Sin conexión";

        ScreenButton.IsEnabled =
            online;
    }

    // ==========================================
    // VER PANTALLA
    // ==========================================

    private async void ScreenButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            ScreenButton.IsEnabled =
                false;

            ScreenButton.Content =
                "Solicitando captura...";

            // ==========================================
            // SOLICITAR CAPTURA
            // ==========================================

            var response =
                await _api.RequestScreenAsync(
                    _machineId
                );

            string responseText =
                await response.Content
                    .ReadAsStringAsync();

            if (
                !response.IsSuccessStatusCode
            )
            {
                MessageBox.Show(
                    $"No se pudo solicitar la pantalla de {_machineId}.\n\n" +
                    responseText,

                    "ControlLab",

                    MessageBoxButton.OK,

                    MessageBoxImage.Warning
                );

                return;
            }

            // ==========================================
            // ESPERAR CAPTURA
            // ==========================================

            ScreenButton.Content =
                "Recibiendo pantalla...";

            byte[]? imageBytes = null;

            for (
                int attempt = 0;
                attempt < 20;
                attempt++
            )
            {
                await Task.Delay(250);

                try
                {
                    imageBytes =
                        await _api.GetScreenAsync(
                            _machineId
                        );

                    if (
                        imageBytes.Length > 0
                    )
                    {
                        break;
                    }

                    imageBytes = null;
                }
                catch
                {
                    // La captura todavía no está disponible.
                }
            }

            if (
                imageBytes == null ||
                imageBytes.Length == 0
            )
            {
                MessageBox.Show(
                    $"No se recibió una captura de {_machineId}.",

                    "ControlLab",

                    MessageBoxButton.OK,

                    MessageBoxImage.Warning
                );

                return;
            }

            // ==========================================
            // ABRIR VISOR
            // ==========================================

            var remoteScreen =
                new RemoteScreenWindow(
                    _machineId,
                    imageBytes,
                    _api
                )
                {
                    Owner = this
                };

            await remoteScreen.StartAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Error obteniendo la pantalla de {_machineId}:\n\n" +
                ex.Message,

                "ControlLab",

                MessageBoxButton.OK,

                MessageBoxImage.Error
            );
        }
        finally
        {
            ScreenButton.IsEnabled =
                _agent != null &&
                _agent.Status.Equals(
                    "online",
                    StringComparison.OrdinalIgnoreCase
                );

            ScreenButton.Content =
                "Ver pantalla";
        }
    }

    // ==========================================
    // CERRAR
    // ==========================================

    private void CloseButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        Close();
    }
}