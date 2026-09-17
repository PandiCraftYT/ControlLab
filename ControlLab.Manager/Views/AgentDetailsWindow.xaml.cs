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

        ScreenButton.IsEnabled =
            online &&
            _authorized;

        LockSessionButton.IsEnabled =
            online &&
            _authorized;

        RestartButton.IsEnabled =
            online &&
            _authorized;

        ShutdownButton.IsEnabled =
            online &&
            _authorized;

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
        // NOMBRE VISIBLE
        // ==========================================

        DisplayNameTextBox.Text =
            !string.IsNullOrWhiteSpace(
                _agent?.DisplayName
            )
                ? _agent!.DisplayName
                : _machineId;

        DisplayNameInfoText.Text =
            !string.IsNullOrWhiteSpace(
                _agent?.DisplayName
            )
                ? _agent!.DisplayName
                : _machineId;

        // ==========================================
        // AUTORIZACIÓN
        // ==========================================

        UpdateAuthorizationUI();

        // ==========================================
        // BOTONES
        // ==========================================

        ScreenButton.IsEnabled =
            online &&
            _authorized;

        LockSessionButton.IsEnabled =
            online &&
            _authorized;

        RestartButton.IsEnabled =
            online &&
            _authorized;

        ShutdownButton.IsEnabled =
            online &&
            _authorized;
    }

    // ==========================================
    // ACTUALIZAR AUTORIZACIÓN
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
    // CAMBIAR NOMBRE
    // ==========================================

    private async void RenameButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        string displayName =
            DisplayNameTextBox.Text.Trim();

        // ==========================================
        // VALIDAR NOMBRE
        // ==========================================

        if (string.IsNullOrWhiteSpace(displayName))
        {
            MessageBox.Show(
                "Escribe un nombre para el equipo.",
                "ControlLab",
                MessageBoxButton.OK,
                MessageBoxImage.Warning
            );

            DisplayNameTextBox.Focus();

            return;
        }

        if (displayName.Length > 50)
        {
            MessageBox.Show(
                "El nombre no puede superar los 50 caracteres.",
                "ControlLab",
                MessageBoxButton.OK,
                MessageBoxImage.Warning
            );

            DisplayNameTextBox.Focus();

            return;
        }

        try
        {
            RenameButton.IsEnabled =
                false;

            RenameButton.Content =
                "Guardando...";

            // ==========================================
            // GUARDAR
            // ==========================================

            bool success =
                await _api.RenameAgentAsync(
                    _machineId,
                    displayName
                );

            if (!success)
            {
                MessageBox.Show(
                    "No se pudo cambiar el nombre.\n\n" +
                    "Comprueba que el nombre no esté siendo utilizado por otro equipo.",
                    "ControlLab",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );

                return;
            }

            // ==========================================
            // ACTUALIZAR INTERFAZ
            // ==========================================

            DisplayNameTextBox.Text =
                displayName;

            DisplayNameInfoText.Text =
                displayName;

            MessageBox.Show(
                $"El equipo ahora se llama:\n\n{displayName}",
                "ControlLab",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Error cambiando el nombre del equipo:\n\n{ex.Message}",
                "ControlLab",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
        finally
        {
            RenameButton.IsEnabled =
                true;

            RenameButton.Content =
                "Cambiar";
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

                _authorized =
                    true;

                UpdateAuthorizationUI();

                bool online =
                    _agent != null &&
                    _agent.Status.Equals(
                        "online",
                        StringComparison.OrdinalIgnoreCase
                    );

                ScreenButton.IsEnabled =
                    online &&
                    _authorized;

                LockSessionButton.IsEnabled =
                    online &&
                    _authorized;

                RestartButton.IsEnabled =
                    online &&
                    _authorized;

                ShutdownButton.IsEnabled =
                    online &&
                    _authorized;

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

            _authorized =
                false;

            UpdateAuthorizationUI();

            ScreenButton.IsEnabled =
                false;

            LockSessionButton.IsEnabled =
                false;

            RestartButton.IsEnabled =
                false;

            ShutdownButton.IsEnabled =
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
    // BLOQUEAR SESIÓN
    // ==========================================

    private async void LockSessionButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var result =
            MessageBox.Show(
                $"¿Deseas bloquear la sesión de {_machineId}?\n\n" +
                "La computadora mostrará la pantalla de bloqueo de Windows.",
                "Bloquear sesión",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning
            );

        if (result != MessageBoxResult.Yes)
            return;

        try
        {
            LockSessionButton.IsEnabled =
                false;

            LockSessionButton.Content =
                "Bloqueando...";

            bool success =
                await _api.LockSessionAsync(
                    _machineId
                );

            if (!success)
            {
                MessageBox.Show(
                    $"No se pudo bloquear la sesión de {_machineId}.\n\n" +
                    "Comprueba que el equipo esté conectado y autorizado.",
                    "ControlLab",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );

                return;
            }

            MessageBox.Show(
                $"La sesión de {_machineId} ha sido bloqueada correctamente.",
                "ControlLab",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Error bloqueando la sesión de {_machineId}:\n\n" +
                ex.Message,
                "ControlLab",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
        finally
        {
            LockSessionButton.IsEnabled =
                _agent != null &&
                _agent.Status.Equals(
                    "online",
                    StringComparison.OrdinalIgnoreCase
                ) &&
                _authorized;

            LockSessionButton.Content =
                "🔒 Bloquear sesión";
        }
    }

    // ==========================================
    // REINICIAR PC
    // ==========================================

    private async void RestartButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var result =
            MessageBox.Show(
                $"¿Deseas reiniciar {_machineId}?\n\n" +
                "La computadora se reiniciará inmediatamente.",
                "Reiniciar PC",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning
            );

        if (result != MessageBoxResult.Yes)
            return;

        try
        {
            RestartButton.IsEnabled =
                false;

            RestartButton.Content =
                "Reiniciando...";

            bool success =
                await _api.RestartAgentAsync(
                    _machineId
                );

            if (!success)
            {
                MessageBox.Show(
                    $"No se pudo reiniciar {_machineId}.\n\n" +
                    "Comprueba que el equipo esté conectado y autorizado.",
                    "ControlLab",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );

                return;
            }

            MessageBox.Show(
                $"Se ha enviado la orden de reinicio a {_machineId}.",
                "ControlLab",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Error reiniciando {_machineId}:\n\n{ex.Message}",
                "ControlLab",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
        finally
        {
            RestartButton.IsEnabled =
                _agent != null &&
                _agent.Status.Equals(
                    "online",
                    StringComparison.OrdinalIgnoreCase
                ) &&
                _authorized;

            RestartButton.Content =
                "Reiniciar PC";
        }
    }

    // ==========================================
    // APAGAR PC
    // ==========================================

    private async void ShutdownButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var result =
            MessageBox.Show(
                $"¿Deseas apagar {_machineId}?\n\n" +
                "La computadora se apagará inmediatamente.",
                "Apagar PC",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning
            );

        if (result != MessageBoxResult.Yes)
            return;

        try
        {
            ShutdownButton.IsEnabled =
                false;

            ShutdownButton.Content =
                "Apagando...";

            bool success =
                await _api.ShutdownAgentAsync(
                    _machineId
                );

            if (!success)
            {
                MessageBox.Show(
                    $"No se pudo apagar {_machineId}.\n\n" +
                    "Comprueba que el equipo esté conectado y autorizado.",
                    "ControlLab",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );

                return;
            }

            MessageBox.Show(
                $"Se ha enviado la orden de apagado a {_machineId}.",
                "ControlLab",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Error apagando {_machineId}:\n\n{ex.Message}",
                "ControlLab",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
        finally
        {
            ShutdownButton.IsEnabled =
                _agent != null &&
                _agent.Status.Equals(
                    "online",
                    StringComparison.OrdinalIgnoreCase
                ) &&
                _authorized;

            ShutdownButton.Content =
                "Apagar PC";
        }
    }

    // ==========================================
    // VER PANTALLA EN VIVO
    // ==========================================

    private async void ScreenButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        bool streamStarted =
            false;

        try
        {
            // ==========================================
            // VALIDAR ESTADO
            // ==========================================

            if (!_authorized)
            {
                MessageBox.Show(
                    $"El equipo {_machineId} no está autorizado.",
                    "ControlLab",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );

                return;
            }

            bool online =
                _agent != null &&
                _agent.Status.Equals(
                    "online",
                    StringComparison.OrdinalIgnoreCase
                );

            if (!online)
            {
                MessageBox.Show(
                    $"El equipo {_machineId} no está conectado.",
                    "ControlLab",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );

                return;
            }

            // ==========================================
            // DESHABILITAR BOTÓN
            // ==========================================

            ScreenButton.IsEnabled =
                false;

            ScreenButton.Content =
                "Iniciando transmisión...";

            // ==========================================
            // INICIAR STREAM EN EL AGENT
            // ==========================================

            streamStarted =
                await _api.StartScreenStreamAsync(
                    _machineId
                );

            if (!streamStarted)
            {
                MessageBox.Show(
                    $"No se pudo iniciar la transmisión de pantalla de {_machineId}.\n\n" +
                    "Comprueba que el equipo esté conectado y autorizado.",
                    "ControlLab",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );

                return;
            }

            // ==========================================
            // ABRIR VISOR
            // ==========================================

            ScreenButton.Content =
                "Abriendo visor...";

            var remoteScreen =
                new RemoteScreenWindow(
                    _machineId,
                    Array.Empty<byte>(),
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
                $"Error iniciando la pantalla en vivo de {_machineId}:\n\n" +
                ex.Message,
                "ControlLab",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
        finally
        {
            // ==========================================
            // DETENER STREAM
            // ==========================================

            if (streamStarted)
            {
                try
                {
                    await _api.StopScreenStreamAsync(
                        _machineId
                    );
                }
                catch
                {
                    // El Agent puede haberse desconectado.
                }
            }

            // ==========================================
            // RESTAURAR BOTÓN
            // ==========================================

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