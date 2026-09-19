using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
using ControlLab.Manager.Models;
using System.Threading;
using System.Net.Http.Headers;
using ControlLab.Manager.Services;
namespace ControlLab.Manager.Controls;

public static class AgentCard
{
    private static readonly HttpClient HttpClient = new();

    private const string ServerUrl =
        "http://localhost:8080";

    public static Border Create(
        string machineId,
        AgentInfo? agent,
        Action onClick)
    {
        bool online =
            agent != null &&
            agent.Status.Equals(
                "online",
                StringComparison.OrdinalIgnoreCase
            );

        bool authorized =
            agent != null &&
            agent.Authorized;

        string displayName =
            !string.IsNullOrWhiteSpace(agent?.DisplayName)
                ? agent.DisplayName
                : machineId;

        // ==========================================
        // COLORES
        // ==========================================

        var statusColor =
            online
                ? Color.FromRgb(50, 213, 131)
                : Color.FromRgb(249, 112, 102);

        var authorizationColor =
            authorized
                ? Color.FromRgb(50, 213, 131)
                : Color.FromRgb(255, 180, 70);

        var cardBackground =
            new SolidColorBrush(
                Color.FromRgb(17, 24, 33)
            );

        var borderBrush =
            new SolidColorBrush(
                Color.FromRgb(32, 43, 55)
            );

        // ==========================================
        // TARJETA
        // ==========================================

        var card =
            new Border
            {
                Width = 250,
                Height = 230,

                Background =
                    cardBackground,

                BorderBrush =
                    borderBrush,

                BorderThickness =
                    new Thickness(1),

                CornerRadius =
                    new CornerRadius(12),

                Margin =
                    new Thickness(0, 0, 14, 14),

                Cursor =
                    Cursors.Hand
            };

        // ==========================================
        // CONTENIDO PRINCIPAL
        // ==========================================

        var content =
            new Grid
            {
                Margin =
                    new Thickness(12)
            };

        content.RowDefinitions.Add(
            new RowDefinition
            {
                Height =
                    new GridLength(
                        110
                    )
            });

        content.RowDefinitions.Add(
            new RowDefinition
            {
                Height =
                    GridLength.Auto
            });

        content.RowDefinitions.Add(
            new RowDefinition
            {
                Height =
                    GridLength.Auto
            });

        content.RowDefinitions.Add(
            new RowDefinition
            {
                Height =
                    new GridLength(
                        1,
                        GridUnitType.Star
                    )
            });

        // ==========================================
        // PREVIEW
        // ==========================================

        var previewBorder =
            new Border
            {
                Height = 110,

                Background =
                    new SolidColorBrush(
                        Color.FromRgb(
                            10,
                            14,
                            20
                        )
                    ),

                BorderBrush =
                    new SolidColorBrush(
                        Color.FromRgb(
                            35,
                            48,
                            62
                        )
                    ),

                BorderThickness =
                    new Thickness(1),

                CornerRadius =
                    new CornerRadius(9),

                ClipToBounds = true
            };

        var previewGrid =
            new Grid();

        var previewImage =
            new Image
            {
                Stretch =
                    Stretch.UniformToFill,

                HorizontalAlignment =
                    HorizontalAlignment.Center,

                VerticalAlignment =
                    VerticalAlignment.Center
            };

        previewGrid.Children.Add(
            previewImage
        );

        // ==========================================
        // ESTADO DE PREVIEW
        // ==========================================

        var previewText =
            new TextBlock
            {
                Text =
                    online
                        ? "Cargando preview..."
                        : "Sin conexión",

                Foreground =
                    new SolidColorBrush(
                        Color.FromRgb(
                            101,
                            120,
                            141
                        )
                    ),

                FontSize = 10,

                HorizontalAlignment =
                    HorizontalAlignment.Center,

                VerticalAlignment =
                    VerticalAlignment.Center
            };

        previewGrid.Children.Add(
            previewText
        );

        previewBorder.Child =
            previewGrid;

        Grid.SetRow(
            previewBorder,
            0
        );

        content.Children.Add(
            previewBorder
        );

        // ==========================================
        // ENCABEZADO
        // ==========================================

        var header =
            new Grid
            {
                Margin =
                    new Thickness(
                        0,
                        9,
                        0,
                        0
                    )
            };

        header.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(
                        1,
                        GridUnitType.Star
                    )
            });

        header.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    GridLength.Auto
            });

        var name =
            new TextBlock
            {
                Text =
                    displayName,

                FontSize = 14,

                FontWeight =
                    FontWeights.SemiBold,

                Foreground =
                    Brushes.White,

                TextTrimming =
                    TextTrimming.CharacterEllipsis,

                VerticalAlignment =
                    VerticalAlignment.Center
            };

        Grid.SetColumn(
            name,
            0
        );

        header.Children.Add(
            name
        );

        var status =
            new TextBlock
            {
                Text =
                    online
                        ? "ONLINE"
                        : "OFFLINE",

                Foreground =
                    new SolidColorBrush(
                        statusColor
                    ),

                FontSize = 9,

                FontWeight =
                    FontWeights.Bold,

                VerticalAlignment =
                    VerticalAlignment.Center
            };

        Grid.SetColumn(
            status,
            1
        );

        header.Children.Add(
            status
        );

        Grid.SetRow(
            header,
            1
        );

        content.Children.Add(
            header
        );

        // ==========================================
        // AUTORIZACIÓN
        // ==========================================

        var authorization =
            new TextBlock
            {
                Text =
                    authorized
                        ? "✓ AUTORIZADO"
                        : "⚠ PENDIENTE",

                Foreground =
                    new SolidColorBrush(
                        authorizationColor
                    ),

                FontSize = 9,

                FontWeight =
                    FontWeights.Bold,

                Margin =
                    new Thickness(
                        0,
                        4,
                        0,
                        0
                    )
            };

        Grid.SetRow(
            authorization,
            2
        );

        content.Children.Add(
            authorization
        );

        // ==========================================
        // INFORMACIÓN INFERIOR
        // ==========================================

        var info =
            new Grid
            {
                Margin =
                    new Thickness(
                        0,
                        6,
                        0,
                        0
                    )
            };

        info.RowDefinitions.Add(
            new RowDefinition
            {
                Height =
                    GridLength.Auto
            });

        info.RowDefinitions.Add(
            new RowDefinition
            {
                Height =
                    GridLength.Auto
            });

        var machineIdText =
            new TextBlock
            {
                Text =
                    machineId,

                FontSize = 9,

                Foreground =
                    new SolidColorBrush(
                        Color.FromRgb(
                            101,
                            120,
                            141
                        )
                    ),

                TextTrimming =
                    TextTrimming.CharacterEllipsis
            };

        Grid.SetRow(
            machineIdText,
            0
        );

        info.Children.Add(
            machineIdText
        );

        var hostname =
            new TextBlock
            {
                Text =
                    online
                        ? agent!.Hostname
                        : "Sin conexión",

                FontSize = 9,

                Foreground =
                    online
                        ? new SolidColorBrush(
                            Color.FromRgb(
                                147,
                                164,
                                184
                            )
                        )
                        : new SolidColorBrush(
                            Color.FromRgb(
                                102,
                                119,
                                138
                            )
                        ),

                TextTrimming =
                    TextTrimming.CharacterEllipsis,

                Margin =
                    new Thickness(
                        0,
                        3,
                        0,
                        0
                    )
            };

        Grid.SetRow(
            hostname,
            1
        );

        info.Children.Add(
            hostname
        );

        Grid.SetRow(
            info,
            3
        );

        content.Children.Add(
            info
        );

        card.Child =
            content;
        
        var previewCancellation =
            new CancellationTokenSource();

        card.Unloaded += (_, _) =>
        {
            previewCancellation.Cancel();
            previewCancellation.Dispose();
        };
        // ==========================================
        // CARGAR PREVIEW
        // ==========================================

        if (online && authorized)
        {
            _ = StartPreviewLoopAsync(
                machineId,
                previewImage,
                previewText,
                previewCancellation.Token
            );
        }
        else if (!authorized)
        {
            previewText.Text =
                "Equipo no autorizado";
        }

        // ==========================================
        // EFECTO HOVER
        // ==========================================

        card.MouseEnter +=
            (_, _) =>
            {
                card.Background =
                    new SolidColorBrush(
                        Color.FromRgb(
                            23,
                            34,
                            46
                        )
                    );

                card.BorderBrush =
                    new SolidColorBrush(
                        Color.FromRgb(
                            53,
                            82,
                            108
                        )
                    );
            };

        card.MouseLeave +=
            (_, _) =>
            {
                card.Background =
                    cardBackground;

                card.BorderBrush =
                    borderBrush;
            };

        // ==========================================
        // CLICK
        // ==========================================

        card.MouseLeftButtonUp +=
            (_, _) =>
            {
                onClick();
            };

        return card;
    }
    // ==========================================
    // PreviewAsync
    // ==========================================
    private static async Task StartPreviewLoopAsync(
        string machineId,
        Image image,
        TextBlock statusText,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await LoadPreviewAsync(
                    machineId,
                    image,
                    statusText
                );

                await Task.Delay(
                    TimeSpan.FromSeconds(5),
                    cancellationToken
                );
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"⚠️ Error en loop de preview de {machineId}: {ex.Message}"
                );

                await Task.Delay(
                    TimeSpan.FromSeconds(5),
                    cancellationToken
                );
            }
        }
    }

    // ==========================================
    // OBTENER ÚLTIMA CAPTURA
    // ==========================================

    private static async Task LoadPreviewAsync(
        string machineId,
        Image image,
        TextBlock statusText)
    {
        try
        {
            // ==========================================
            // OBTENER SESIÓN ACTUAL
            // ==========================================

            string? sessionToken =
                ControlLabApi.GetSessionToken();

            if (string.IsNullOrWhiteSpace(sessionToken))
            {
                statusText.Text =
                    "Sesión no válida";

                return;
            }

            // ==========================================
            // 1. SOLICITAR NUEVO PREVIEW
            // ==========================================

            string previewUrl =
                $"{ServerUrl}/api/agents/" +
                $"{Uri.EscapeDataString(machineId)}" +
                "/preview";

            using var previewRequest =
                new HttpRequestMessage(
                    HttpMethod.Post,
                    previewUrl
                );

            previewRequest.Headers.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    sessionToken
                );

            using var previewResponse =
                await HttpClient.SendAsync(
                    previewRequest
                );

            if (!previewResponse.IsSuccessStatusCode)
            {
                Console.WriteLine(
                    $"⚠️ Preview POST rechazado para {machineId}: " +
                    $"{(int)previewResponse.StatusCode} " +
                    $"{previewResponse.StatusCode}"
                );

                statusText.Text =
                    "Preview no disponible";

                return;
            }

            // ==========================================
            // 2. ESPERAR A QUE EL AGENT GENERE EL PREVIEW
            // ==========================================

            string url =
                $"{ServerUrl}/api/agents/" +
                $"{Uri.EscapeDataString(machineId)}" +
                "/preview";

            byte[]? bytes = null;

            for (int attempt = 0; attempt < 12; attempt++)
            {
                try
                {
                    using var getRequest =
                        new HttpRequestMessage(
                            HttpMethod.Get,
                            $"{url}?t={DateTime.UtcNow.Ticks}"
                        );

                    getRequest.Headers.Authorization =
                        new AuthenticationHeaderValue(
                            "Bearer",
                            sessionToken
                        );

                    using var getResponse =
                        await HttpClient.SendAsync(
                            getRequest
                        );

                    if (getResponse.IsSuccessStatusCode)
                    {
                        byte[] result =
                            await getResponse.Content.ReadAsByteArrayAsync();

                        if (result.Length > 0)
                        {
                            bytes = result;
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(
                        $"⚠️ Intento de preview {attempt + 1} " +
                        $"para {machineId}: {ex.Message}"
                    );
                }

                await Task.Delay(250);
            }

            // ==========================================
            // 3. VALIDAR IMAGEN
            // ==========================================

            if (bytes == null || bytes.Length == 0)
            {
                statusText.Text =
                    "Sin preview";

                return;
            }

            // ==========================================
            // 4. MOSTRAR IMAGEN
            // ==========================================

            await Application.Current.Dispatcher.InvokeAsync(
                () =>
                {
                    try
                    {
                        using var stream =
                            new MemoryStream(bytes);

                        var bitmap =
                            new BitmapImage();

                        bitmap.BeginInit();

                        bitmap.CacheOption =
                            BitmapCacheOption.OnLoad;

                        bitmap.StreamSource =
                            stream;

                        bitmap.EndInit();

                        bitmap.Freeze();

                        image.Source =
                            bitmap;

                        statusText.Visibility =
                            Visibility.Collapsed;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(
                            $"❌ Error mostrando preview de " +
                            $"{machineId}: {ex.Message}"
                        );

                        statusText.Text =
                            "Error de imagen";
                    }
                }
            );
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"⚠️ Preview no disponible para " +
                $"{machineId}: {ex.Message}"
            );

            await Application.Current.Dispatcher.InvokeAsync(
                () =>
                {
                    statusText.Text =
                        "Sin preview";
                }
            );
        }
    }
}