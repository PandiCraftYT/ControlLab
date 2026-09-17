using System.Net.WebSockets;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
namespace ControlLab.Manager.Views;

public partial class RemoteScreenWindow : Window
{
    private readonly string _machineId;

    private CancellationTokenSource? _cancellation;

    private ClientWebSocket? _socket;

    private Task? _receiveTask;

    private bool _closing;

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

        // ---------------------------------------------------------
        // Mostrar la captura inicial mientras conecta el stream
        // ---------------------------------------------------------

        if (
            IsValidJpeg(
                initialImage
            )
        )
        {
            SetImageSource(
                initialImage
            );
        }

        StatusText.Text =
            $"● {_machineId}  •  Conectando transmisión...";

        Closed +=
            (_, _) =>
            {
                _ = StopAsync();
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
        catch (
            OperationCanceledException
        )
        {
            // Ventana cerrada.
        }
        catch (Exception ex)
        {
            StatusText.Text =
                $"● {_machineId}  •  Error de transmisión";

            StatusText.Foreground =
                new SolidColorBrush(
                    Color.FromRgb(
                        249,
                        112,
                        102
                    )
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

        StatusText.Text =
            $"● {_machineId}  •  EN VIVO";

        StatusText.Foreground =
            new SolidColorBrush(
                Color.FromRgb(
                    50,
                    213,
                    131
                )
            );

        Console.WriteLine(
            $"🟢 Transmisión conectada: {_machineId}"
        );

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

                await Dispatcher.InvokeAsync(
                    () =>
                    {
                        try
                        {
                            SetImageSource(
                                imageBytes
                            );

                            StatusText.Text =
                                $"● {_machineId}  •  EN VIVO  •  {DateTime.Now:HH:mm:ss}";

                            StatusText.Foreground =
                                new SolidColorBrush(
                                    Color.FromRgb(
                                        50,
                                        213,
                                        131
                                    )
                                );
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
            catch (
                OperationCanceledException
            )
            {
                return;
            }
            catch (
                WebSocketException ex
            )
            {
                Console.WriteLine(
                    $"⚠️ WebSocket de {_machineId}: " +
                    $"{ex.Message}"
                );

                return;
            }
        }
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
            _socket = null;

            _cancellation?.Dispose();

            _cancellation = null;

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

        var bitmap =
            new WriteableBitmap(
                decoder.Frames[0]
            );

        bitmap.Freeze();

        RemoteImage.Source =
            bitmap;
    }
}