using System;
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

    private bool _authorized;

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

        _authorized =
            agent?.Authorized ?? false;

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

        // ==========================================
        // ESTADO DE CONEXIÓN
        // ==========================================

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

        // ==========================================
        // INFORMACIÓN DEL EQUIPO
        // ==========================================

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

        // ==========================================
        // AUTORIZACIÓN
        // ==========================================

        UpdateAuthorizationUI();

        // ==========================================
        // PANTALLA
        // ==========================================

        ScreenButton.IsEnabled =
            online &&
            _authorized;
    }

    // ==========================================
    // ACTUALIZAR INTERFAZ DE AUTORIZACIÓN
    // ==========================================

    private void UpdateAuthorizationUI()
    {
        if (_authorized)
        {
            AuthorizationText.Text =
                "✓ Equipo autorizado";

            AuthorizationText.Foreground =
                new SolidColorBrush(
                    Color.FromRgb(
                        50,
                        213,
                        131
                    )
                );

            AuthorizationBadge.Background =
                new SolidColorBrush(
                    Color.FromRgb(
                        25,
                        55,
                        43
                    )
                );

            AuthorizationButton.Content =
                "Revocar autorización";
        }
        else
        {
            AuthorizationText.Text =
                "⚠ Pendiente de autorización";

            AuthorizationText.Foreground =
                new SolidColorBrush(
                    Color.FromRgb(
                        255,
                        180,
                        70
                    )
                );

            AuthorizationBadge.Background =
                new SolidColorBrush(
                    Color.FromRgb(
                        55,
                        45,
                        25
                    )
                );

            AuthorizationButton.Content =
                "Autorizar equipo";
        }
    }

    // ==========================================
    // AUTORIZAR / REVOCAR
    // ==========================================

    private async void AuthorizationButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            AuthorizationButton.IsEnabled =
                false;

            // ==========================================
            // AUTORIZAR
            // ==========================================

            if (!_authorized)
            {
                AuthorizationButton.Content =
                    "Autorizando...";

                bool success =
                    await _api.AuthorizeAgentAsync(
                        _machineId
                    );

                if (!success)
                {
                    MessageBox.Show(
                        $"No se pudo autorizar el equipo {_machineId}.",

                        "ControlLab",

                        MessageBoxButton.OK,

                        MessageBoxImage.Warning
                    );

                    UpdateAuthorizationUI();

                    return;
                }

                _authorized = true;

                UpdateAuthorizationUI();

                ScreenButton.IsEnabled =
                    _agent != null &&
                    _agent.Status.Equals(
                        "online",
                        StringComparison.OrdinalIgnoreCase
                    );

                MessageBox.Show(
                    $"El equipo {_machineId} ha sido autorizado correctamente.",

                    "ControlLab",

                    MessageBoxButton.OK,

                    MessageBoxImage.Information
                );

                return;
            }

            // ==========================================
            // CONFIRMAR REVOCACIÓN
            // ==========================================

            var result =
                MessageBox.Show(
                    $"¿Deseas revocar la autorización de {_machineId}?\n\n" +
                    "El equipo dejará de estar autorizado para conectarse al Manager.",

                    "Revocar autorización",

                    MessageBoxButton.YesNo,

                    MessageBoxImage.Warning
                );

            if (result != MessageBoxResult.Yes)
            {
                UpdateAuthorizationUI();
                return;
            }

            // ==========================================
            // REVOCAR
            // ==========================================

            AuthorizationButton.Content =
                "Revocando...";

            bool revoked =
                await _api.RevokeAgentAsync(
                    _machineId
                );

            if (!revoked)
            {
                MessageBox.Show(
                    $"No se pudo revocar la autorización de {_machineId}.",

                    "ControlLab",

                    MessageBoxButton.OK,

                    MessageBoxImage.Warning
                );

                UpdateAuthorizationUI();

                return;
            }

            _authorized = false;

            UpdateAuthorizationUI();

            ScreenButton.IsEnabled =
                false;

            MessageBox.Show(
                $"La autorización de {_machineId} ha sido revocada.",

                "ControlLab",

                MessageBoxButton.OK,

                MessageBoxImage.Information
            );
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Error modificando la autorización de {_machineId}:\n\n" +
                ex.Message,

                "ControlLab",

                MessageBoxButton.OK,

                MessageBoxImage.Error
            );

            UpdateAuthorizationUI();
        }
        finally
        {
            AuthorizationButton.IsEnabled =
                true;

            UpdateAuthorizationUI();
        }
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

            if (!response.IsSuccessStatusCode)
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
                attempt++)
            {
                await Task.Delay(250);

                try
                {
                    imageBytes =
                        await _api.GetScreenAsync(
                            _machineId
                        );

                    if (imageBytes.Length > 0)
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
                ) &&
                _authorized;

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