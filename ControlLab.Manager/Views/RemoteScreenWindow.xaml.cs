using System;
using System.IO;
using System.Net.WebSockets;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ControlLab.Manager.Views;

public partial class RemoteScreenWindow : Window
{
    private readonly string _machineId;

    private CancellationTokenSource? _cancellation;

    private ClientWebSocket? _socket;

    private Task? _receiveTask;

    private bool _closing;

    // ==========================================
    // ESTADÍSTICAS DEL STREAM
    // ==========================================

    private int _framesReceived;

    private DateTime _fpsStartTime;

    // ==========================================
    // CONSTRUCTOR
    // ==========================================

    public RemoteScreenWindow(
        string machineId,
        byte[] initialImage,
        ControlLab.Manager.Services.ControlLabApi api)
    {
        InitializeComponent();

        _machineId =
            machineId;

        Title =
            $"ControlLab — Pantalla de {_machineId}";

        MachineNameText.Text =
            _machineId;

        // ==========================================
        // MOSTRAR CAPTURA INICIAL SI EXISTE
        // ==========================================

        if (IsValidJpeg(initialImage))
        {
            try
            {
                SetImageSource(initialImage);

                LoadingPanel.Visibility =
                    Visibility.Collapsed;
            }
            catch
            {
                // La transmisión proporcionará
                // el siguiente frame.
            }
        }

        // ==========================================
        // ESTADO INICIAL
        // ==========================================

        SetConnectingState();

        _fpsStartTime =
            DateTime.UtcNow;

        // ==========================================
        // CERRAR
        // ==========================================

        Closed +=
            async (_, _) =>
            {
                await StopAsync();
            };
    }

    // =========================================================
    // INICIAR TRANSMISIÓN
    // =========================================================

    public async Task StartAsync()
    {
        Show();

        _cancellation =
            new CancellationTokenSource();

        try
        {
            await StartStreamAsync(
                _cancellation.Token
            );
        }
        catch (OperationCanceledException)
        {
            // Ventana cerrada.
        }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(
                () =>
                {
                    SetErrorState();

                    LoadingPanel.Visibility =
                        Visibility.Collapsed;
                }
            );

            Console.WriteLine(
                $"❌ Error iniciando transmisión de {_machineId}: " +
                $"{ex.Message}"
            );
        }
    }

    // =========================================================
    // CONECTAR STREAM
    // =========================================================

    private async Task StartStreamAsync(
        CancellationToken cancellationToken)
    {
        _socket =
            new ClientWebSocket();

        Uri streamUri =
            new Uri(
                "ws://localhost:8080/ws/screen/" +
                Uri.EscapeDataString(
                    _machineId
                )
            );

        Console.WriteLine(
            $"📡 Conectando transmisión: {streamUri}"
        );

        await _socket.ConnectAsync(
            streamUri,
            cancellationToken
        );

        if (_closing)
            return;

        await Dispatcher.InvokeAsync(
            () =>
            {
                SetLiveState();

                LoadingPanel.Visibility =
                    Visibility.Visible;
            }
        );

        Console.WriteLine(
            $"🟢 Transmisión conectada: {_machineId}"
        );

        _framesReceived =
            0;

        _fpsStartTime =
            DateTime.UtcNow;

        _receiveTask =
            ReceiveFramesAsync(
                _socket,
                cancellationToken
            );

        await _receiveTask;
    }

    // =========================================================
    // RECIBIR FRAMES
    // =========================================================

    private async Task ReceiveFramesAsync(
        ClientWebSocket socket,
        CancellationToken cancellationToken)
    {
        byte[] buffer =
            new byte[
                1024 * 1024
            ];

        while (
            socket.State ==
            WebSocketState.Open
        )
        {
            WebSocketReceiveResult result;

            using var memory =
                new MemoryStream();

            try
            {
                do
                {
                    result =
                        await socket.ReceiveAsync(
                            new ArraySegment<byte>(
                                buffer
                            ),
                            cancellationToken
                        );

                    if (
                        result.MessageType ==
                        WebSocketMessageType.Close
                    )
                    {
                        return;
                    }

                    if (
                        result.MessageType !=
                        WebSocketMessageType.Binary
                    )
                    {
                        continue;
                    }

                    await memory.WriteAsync(
                        buffer.AsMemory(
                            0,
                            result.Count
                        ),
                        cancellationToken
                    );

                    // ==========================================
                    // LÍMITE DE SEGURIDAD DEL FRAME
                    // ==========================================

                    if (
                        memory.Length >
                        8 * 1024 * 1024
                    )
                    {
                        throw new InvalidOperationException(
                            "El frame recibido supera el tamaño permitido."
                        );
                    }

                }
                while (
                    !result.EndOfMessage
                );

                byte[] imageBytes =
                    memory.ToArray();

                if (
                    !IsValidJpeg(
                        imageBytes
                    )
                )
                {
                    Console.WriteLine(
                        $"⚠️ Frame inválido recibido de {_machineId}"
                    );

                    continue;
                }

                _framesReceived++;

                await Dispatcher.InvokeAsync(
                    () =>
                    {
                        try
                        {
                            SetImageSource(
                                imageBytes
                            );

                            LoadingPanel.Visibility =
                                Visibility.Collapsed;

                            UpdateLiveStats();

                            SetLiveState();
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(
                                $"❌ Error mostrando frame: " +
                                $"{ex.Message}"
                            );
                        }
                    }
                );
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (WebSocketException ex)
            {
                Console.WriteLine(
                    $"⚠️ WebSocket de {_machineId}: " +
                    $"{ex.Message}"
                );

                await Dispatcher.InvokeAsync(
                    () =>
                    {
                        if (!_closing)
                        {
                            SetDisconnectedState();

                            LoadingPanel.Visibility =
                                Visibility.Collapsed;
                        }
                    }
                );

                return;
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"❌ Error recibiendo frame: " +
                    $"{ex.Message}"
                );

                await Dispatcher.InvokeAsync(
                    () =>
                    {
                        if (!_closing)
                        {
                            SetErrorState();

                            LoadingPanel.Visibility =
                                Visibility.Collapsed;
                        }
                    }
                );

                return;
            }
        }
    }

    // =========================================================
    // ACTUALIZAR FPS Y ESTADÍSTICAS
    // =========================================================

    private void UpdateLiveStats()
    {
        TimeSpan elapsed =
            DateTime.UtcNow -
            _fpsStartTime;

        if (elapsed.TotalSeconds <= 0)
            return;

        double fps =
            _framesReceived /
            elapsed.TotalSeconds;

        FpsText.Text =
            $"{fps:0.0} FPS";

        ConnectionText.Text =
            "Conectado • transmisión activa";
    }

    // =========================================================
    // ESTADO: CONECTANDO
    // =========================================================

    private void SetConnectingState()
    {
        StatusText.Text =
            "CONECTANDO...";

        StatusText.Foreground =
            new SolidColorBrush(
                Color.FromRgb(
                    255,
                    180,
                    70
                )
            );

        LiveIndicator.Text =
            "●";

        LiveIndicator.Foreground =
            new SolidColorBrush(
                Color.FromRgb(
                    255,
                    180,
                    70
                )
            );

        ConnectionText.Text =
            "Conectando con el equipo...";

        FpsText.Text =
            "— FPS";

        ResolutionText.Text =
            "—";
    }

    // =========================================================
    // ESTADO: EN VIVO
    // =========================================================

    private void SetLiveState()
    {
        StatusText.Text =
            "EN VIVO";

        StatusText.Foreground =
            new SolidColorBrush(
                Color.FromRgb(
                    50,
                    213,
                    131
                )
            );

        LiveIndicator.Text =
            "●";

        LiveIndicator.Foreground =
            new SolidColorBrush(
                Color.FromRgb(
                    50,
                    213,
                    131
                )
            );

        ConnectionText.Text =
            "Conectado • transmisión activa";
    }

    // =========================================================
    // ESTADO: DESCONECTADO
    // =========================================================

    private void SetDisconnectedState()
    {
        StatusText.Text =
            "DESCONECTADO";

        StatusText.Foreground =
            new SolidColorBrush(
                Color.FromRgb(
                    249,
                    112,
                    102
                )
            );

        LiveIndicator.Text =
            "●";

        LiveIndicator.Foreground =
            new SolidColorBrush(
                Color.FromRgb(
                    249,
                    112,
                    102
                )
            );

        ConnectionText.Text =
            "Se perdió la conexión";
    }

    // =========================================================
    // ESTADO: ERROR
    // =========================================================

    private void SetErrorState()
    {
        StatusText.Text =
            "ERROR";

        StatusText.Foreground =
            new SolidColorBrush(
                Color.FromRgb(
                    249,
                    112,
                    102
                )
            );

        LiveIndicator.Text =
            "●";

        LiveIndicator.Foreground =
            new SolidColorBrush(
                Color.FromRgb(
                    249,
                    112,
                    102
                )
            );

        ConnectionText.Text =
            "Error en la transmisión";
    }

    // =========================================================
    // BOTÓN DETENER
    // =========================================================

    private void StopButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        Close();
    }

    // =========================================================
    // DETENER TRANSMISIÓN
    // =========================================================

    private async Task StopAsync()
    {
        if (_closing)
            return;

        _closing =
            true;

        try
        {
            _cancellation?.Cancel();

            if (
                _socket != null &&
                (
                    _socket.State ==
                    WebSocketState.Open ||

                    _socket.State ==
                    WebSocketState.CloseReceived
                )
            )
            {
                try
                {
                    await _socket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Visor cerrado",
                        CancellationToken.None
                    );
                }
                catch
                {
                }
            }

            _socket?.Dispose();
        }
        catch
        {
        }
        finally
        {
            _socket =
                null;

            _cancellation?.Dispose();

            _cancellation =
                null;

            Console.WriteLine(
                $"🔴 Transmisión detenida: {_machineId}"
            );
        }
    }

    // =========================================================
    // VALIDAR JPEG
    // =========================================================

    private static bool IsValidJpeg(
        byte[]? imageBytes)
    {
        if (
            imageBytes == null ||
            imageBytes.Length < 3
        )
        {
            return false;
        }

        return
            imageBytes[0] == 0xFF &&
            imageBytes[1] == 0xD8 &&
            imageBytes[2] == 0xFF;
    }

    // =========================================================
    // MOSTRAR JPEG
    // =========================================================

    private void SetImageSource(
        byte[] imageBytes)
    {
        if (
            !IsValidJpeg(
                imageBytes
            )
        )
        {
            throw new InvalidOperationException(
                "Los datos recibidos no corresponden a un JPEG válido."
            );
        }

        using var stream =
            new MemoryStream(
                imageBytes,
                writable: false
            );

        var decoder =
            BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad
            );

        if (
            decoder.Frames.Count == 0
        )
        {
            throw new InvalidOperationException(
                "No se pudo decodificar el frame."
            );
        }

        BitmapFrame frame =
            decoder.Frames[0];

        var bitmap =
            new WriteableBitmap(
                frame
            );

        bitmap.Freeze();

        RemoteImage.Source =
            bitmap;

        // ==========================================
        // RESOLUCIÓN REAL DEL FRAME
        // ==========================================

        ResolutionText.Text =
            $"{frame.PixelWidth} × {frame.PixelHeight}";
    }
}