using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using ControlLab.Manager.Services;

namespace ControlLab.Manager.Views;

public partial class RemoteScreenWindow : Window
{
    private readonly ControlLabApi _api;

    private readonly string _machineId;

    private CancellationTokenSource? _cancellation;

    public RemoteScreenWindow(
        string machineId,
        byte[] initialImage,
        ControlLabApi api)
    {
        InitializeComponent();

        _machineId =
            machineId;

        _api =
            api;

        Title =
            $"ControlLab — Pantalla de {_machineId}";

        SetImageSource(
            initialImage
        );

        StatusText.Text =
            $"● {_machineId}  •  Conectada";

        Closed +=
            (_, _) =>
            {
                _cancellation?.Cancel();
            };
    }

    // ==========================================
    // MOSTRAR Y ACTUALIZAR
    // ==========================================

    public async Task StartAsync()
    {
        Show();

        _cancellation =
            new CancellationTokenSource();

        try
        {
            await UpdateLoopAsync(
                _cancellation.Token
            );
        }
        catch (OperationCanceledException)
        {
            // Ventana cerrada.
        }
        finally
        {
            _cancellation.Dispose();

            _cancellation = null;
        }
    }

    // ==========================================
    // ACTUALIZAR PANTALLA
    // ==========================================

    private async Task UpdateLoopAsync(
        CancellationToken cancellationToken)
    {
        while (
            !cancellationToken
                .IsCancellationRequested
        )
        {
            try
            {
                await Task.Delay(
                    1000,
                    cancellationToken
                );

                if (
                    cancellationToken
                        .IsCancellationRequested
                )
                {
                    break;
                }

                // ------------------------------------------
                // SOLICITAR NUEVA CAPTURA
                // ------------------------------------------

                var response =
                    await _api.RequestScreenAsync(
                        _machineId
                    );

                if (
                    !response.IsSuccessStatusCode
                )
                {
                    SetOfflineStatus();

                    continue;
                }

                // ------------------------------------------
                // ESPERAR CAPTURA
                // ------------------------------------------

                byte[]? image =
                    await WaitForImageAsync(
                        cancellationToken
                    );

                if (
                    image == null
                )
                {
                    SetWaitingStatus();

                    continue;
                }

                // ------------------------------------------
                // ACTUALIZAR IMAGEN
                // ------------------------------------------

                try
                {
                    SetImageSource(
                        image
                    );

                    StatusText.Text =
                        $"● {_machineId}  •  Actualizada {DateTime.Now:HH:mm:ss}";
                }
                catch (Exception ex)
                {
                    StatusText.Text =
                        $"● {_machineId}  •  Error de imagen";

                    Console.WriteLine(
                        $"Error procesando captura de {_machineId}: {ex.Message}"
                    );
                }
            }
            catch (
                OperationCanceledException
            )
            {
                break;
            }
            catch (Exception ex)
            {
                StatusText.Text =
                    $"● {_machineId}  •  Error: {ex.Message}";

                Console.WriteLine(
                    $"Error actualizando pantalla de {_machineId}: {ex.Message}"
                );
            }
        }
    }

    // ==========================================
    // ESPERAR IMAGEN
    // ==========================================

    private async Task<byte[]?> WaitForImageAsync(
        CancellationToken cancellationToken)
    {
        for (
            int attempt = 0;
            attempt < 10;
            attempt++
        )
        {
            await Task.Delay(
                100,
                cancellationToken
            );

            try
            {
                byte[] image =
                    await _api.GetScreenAsync(
                        _machineId,
                        cancellationToken
                    );

                if (
                    IsValidJpeg(
                        image
                    )
                )
                {
                    return image;
                }
            }
            catch
            {
                // La captura todavía no está disponible.
            }
        }

        return null;
    }

    // ==========================================
    // ESTADOS
    // ==========================================

    private void SetOfflineStatus()
    {
        StatusText.Text =
            $"● {_machineId}  •  Sin conexión";

        StatusText.Foreground =
            new SolidColorBrush(
                Color.FromRgb(
                    249,
                    112,
                    102
                )
            );
    }

    private void SetWaitingStatus()
    {
        StatusText.Text =
            $"● {_machineId}  •  Esperando captura";

        StatusText.Foreground =
            new SolidColorBrush(
                Color.FromRgb(
                    255,
                    184,
                    77
                )
            );
    }

    // ==========================================
    // VALIDAR JPEG
    // ==========================================

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

    // ==========================================
    // CONVERTIR JPEG A IMAGEN WPF
    // ==========================================

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
                "Los datos recibidos no corresponden a una imagen JPEG válida."
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
                "No se pudo decodificar la imagen recibida."
            );
        }

        var frame =
            decoder.Frames[0];

        var bitmap =
            new WriteableBitmap(
                frame
            );

        bitmap.Freeze();

        RemoteImage.Source =
            bitmap;
    }
}