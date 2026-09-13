using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.IO;

namespace ControlLab.Manager;

public partial class MainWindow : Window
{
    private readonly HttpClient _httpClient = new();

    private readonly DispatcherTimer _refreshTimer;

    private const int TotalEquipos = 34;

    private const string ServerUrl =
        "http://localhost:8080";

    public MainWindow()
    {
        InitializeComponent();

        _refreshTimer =
            new DispatcherTimer
            {
                Interval =
                    TimeSpan.FromSeconds(3)
            };

        _refreshTimer.Tick +=
            async (_, _) =>
                await LoadAgentsAsync();

        Loaded +=
            async (_, _) =>
                await LoadAgentsAsync();

        _refreshTimer.Start();
    }

    // ==========================================
    // CARGAR EQUIPOS
    // ==========================================

    private async Task LoadAgentsAsync()
    {
        try
        {
            var response =
                await _httpClient.GetAsync(
                    $"{ServerUrl}/api/agents"
                );

            response.EnsureSuccessStatusCode();

            var json =
                await response.Content
                    .ReadAsStringAsync();

            var data =
                JsonSerializer.Deserialize<AgentsResponse>(
                    json,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    }
                );

            if (data == null)
                return;

            AgentsPanel.Children.Clear();

            for (
                int i = 1;
                i <= TotalEquipos;
                i++
            )
            {
                string machineId =
                    $"PC-{i:00}";

                var agent =
                    data.Agents.FirstOrDefault(
                        x =>
                            x.MachineId.Equals(
                                machineId,
                                StringComparison
                                    .OrdinalIgnoreCase
                            )
                    );

                AgentsPanel.Children.Add(
                    CreateAgentCard(
                        machineId,
                        agent
                    )
                );
            }

            int equiposOnline =
                data.Agents.Count;

            int equiposOffline =
                Math.Max(
                    0,
                    TotalEquipos -
                    equiposOnline
                );

            TotalEquiposText.Text =
                TotalEquipos.ToString();

            EquiposOnlineText.Text =
                equiposOnline.ToString();

            EquiposOfflineText.Text =
                equiposOffline.ToString();
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"Error consultando servidor: {ex.Message}"
            );
        }
    }

    // ==========================================
    // CREAR TARJETA
    // ==========================================

    private Border CreateAgentCard(
        string machineId,
        AgentInfo? agent)
    {
        bool online =
            agent != null &&
            agent.Status.Equals(
                "online",
                StringComparison.OrdinalIgnoreCase
            );

        var statusColor =
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

        var card =
            new Border
            {
                Width = 210,
                Height = 125,

                Background =
                    new SolidColorBrush(
                        Color.FromRgb(
                            23,
                            26,
                            33
                        )
                    ),

                CornerRadius =
                    new CornerRadius(10),

                Margin =
                    new Thickness(
                        0,
                        0,
                        15,
                        15
                    ),

                Padding =
                    new Thickness(16),

                Cursor =
                    Cursors.Hand
            };

        var content =
            new StackPanel();

        var header =
            new StackPanel
            {
                Orientation =
                    Orientation.Horizontal
            };

        var indicator =
            new Ellipse
            {
                Width = 10,
                Height = 10,

                Fill =
                    statusColor,

                Margin =
                    new Thickness(
                        0,
                        0,
                        8,
                        0
                    )
            };

        var machineName =
            new TextBlock
            {
                Text = machineId,

                FontSize = 17,

                FontWeight =
                    FontWeights.SemiBold,

                Foreground =
                    Brushes.White
            };

        header.Children.Add(
            indicator
        );

        header.Children.Add(
            machineName
        );

        var status =
            new TextBlock
            {
                Text =
                    online
                        ? "En línea"
                        : "Desconectada",

                Foreground =
                    statusColor,

                FontSize = 12,

                Margin =
                    new Thickness(
                        18,
                        4,
                        0,
                        0
                    )
            };

        var hostname =
            new TextBlock
            {
                Text =
                    online
                        ? agent!.Hostname
                        : "Sin conexión",

                Foreground =
                    new SolidColorBrush(
                        Color.FromRgb(
                            139,
                            147,
                            161
                        )
                    ),

                FontSize = 11,

                Margin =
                    new Thickness(
                        18,
                        10,
                        0,
                        0
                    )
            };

        content.Children.Add(
            header
        );

        content.Children.Add(
            status
        );

        content.Children.Add(
            hostname
        );

        card.Child = content;

        card.MouseLeftButtonUp +=
            (_, _) =>
            {
                ShowAgentDetails(
                    machineId,
                    agent
                );
            };

        return card;
    }

    // ==========================================
    // DETALLES DEL EQUIPO
    // ==========================================

    private void ShowAgentDetails(
        string machineId,
        AgentInfo? agent)
    {
        bool online =
            agent != null &&
            agent.Status.Equals(
                "online",
                StringComparison.OrdinalIgnoreCase
            );

        string hostname =
            online
                ? agent!.Hostname
                : "Sin conexión";

        string platform =
            online
                ? agent!.Platform
                : "No disponible";

        string version =
            online
                ? agent!.AgentVersion
                : "No disponible";

        string lastHeartbeat =
            online
                ? agent!.LastHeartbeat
                : "Sin conexión";

        var window =
            new Window
            {
                Title =
                    $"ControlLab - {machineId}",

                Width = 500,

                Height = 560,

                WindowStartupLocation =
                    WindowStartupLocation.CenterOwner,

                Owner = this,

                Background =
                    new SolidColorBrush(
                        Color.FromRgb(
                            15,
                            17,
                            23
                        )
                    ),

                ResizeMode =
                    ResizeMode.NoResize
            };

        var mainPanel =
            new StackPanel
            {
                Margin =
                    new Thickness(30)
            };

        mainPanel.Children.Add(
            new TextBlock
            {
                Text = machineId,

                FontSize = 30,

                FontWeight =
                    FontWeights.Bold,

                Foreground =
                    Brushes.White
            }
        );

        mainPanel.Children.Add(
            new TextBlock
            {
                Text =
                    online
                        ? "●  En línea"
                        : "●  Desconectada",

                FontSize = 14,

                Foreground =
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
                        ),

                Margin =
                    new Thickness(
                        0,
                        5,
                        0,
                        25
                    )
            }
        );

        AddDetail(
            mainPanel,
            "NOMBRE DEL EQUIPO",
            hostname
        );

        AddDetail(
            mainPanel,
            "SISTEMA",
            platform
        );

        AddDetail(
            mainPanel,
            "VERSIÓN DEL AGENTE",
            version
        );

        AddDetail(
            mainPanel,
            "ÚLTIMO HEARTBEAT",
            lastHeartbeat
        );

        mainPanel.Children.Add(
            new Border
            {
                Height = 1,

                Background =
                    new SolidColorBrush(
                        Color.FromRgb(
                            41,
                            46,
                            57
                        )
                    ),

                Margin =
                    new Thickness(
                        0,
                        20,
                        0,
                        20
                    )
            }
        );

        // ==========================================
        // BOTÓN VER PANTALLA
        // ==========================================

        var screenButton =
            new Button
            {
                Content =
                    "Ver pantalla",

                Height = 45,

                FontSize = 14,

                Margin =
                    new Thickness(
                        0,
                        0,
                        0,
                        10
                    ),

                IsEnabled = online
            };

        screenButton.Click +=
            async (_, _) =>
            {
                await OpenRemoteScreenAsync(
                    machineId,
                    screenButton
                );
            };

        mainPanel.Children.Add(
            screenButton
        );

        // ==========================================
        // BOTÓN CERRAR
        // ==========================================

        var closeButton =
            new Button
            {
                Content = "Cerrar",

                Height = 40,

                FontSize = 13
            };

        closeButton.Click +=
            (_, _) =>
            {
                window.Close();
            };

        mainPanel.Children.Add(
            closeButton
        );

        window.Content =
            mainPanel;

        window.ShowDialog();
    }

    // ==========================================
    // ABRIR PANTALLA REMOTA
    // ==========================================

    private async Task OpenRemoteScreenAsync(
        string machineId,
        Button screenButton)
    {
        try
        {
            screenButton.IsEnabled =
                false;

            screenButton.Content =
                "Solicitando captura...";

            // ==========================================
            // 1. SOLICITAR CAPTURA
            // ==========================================

            var requestResponse =
                await _httpClient.PostAsync(
                    $"{ServerUrl}/api/agents/" +
                    $"{Uri.EscapeDataString(machineId)}" +
                    "/screen",
                    null
                );

            string requestJson =
                await requestResponse.Content
                    .ReadAsStringAsync();

            if (
                !requestResponse
                    .IsSuccessStatusCode
            )
            {
                MessageBox.Show(
                    $"No se pudo solicitar la pantalla de {machineId}.\n\n" +
                    requestJson,

                    "ControlLab",

                    MessageBoxButton.OK,

                    MessageBoxImage.Warning
                );

                return;
            }

            // ==========================================
            // 2. ESPERAR A QUE LLEGUE LA CAPTURA
            // ==========================================

            screenButton.Content =
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
                        await _httpClient.GetByteArrayAsync(
                            $"{ServerUrl}/api/agents/" +
                            $"{Uri.EscapeDataString(machineId)}" +
                            "/screen"
                        );

                    if (
                        IsValidJpeg(
                            imageBytes
                        )
                    )
                    {
                        break;
                    }

                    imageBytes = null;
                }
                catch
                {
                    // La captura todavía no está lista.
                }
            }

            if (
                imageBytes == null ||
                !IsValidJpeg(imageBytes)
            )
            {
                MessageBox.Show(
                    $"No se recibió una captura JPEG válida de {machineId}.",

                    "ControlLab",

                    MessageBoxButton.OK,

                    MessageBoxImage.Warning
                );

                return;
            }

            // ==========================================
            // 3. MOSTRAR PANTALLA
            // ==========================================

            await ShowRemoteScreenAsync(
                machineId,
                imageBytes
            );
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Error obteniendo la pantalla de {machineId}:\n\n" +
                ex.Message,

                "ControlLab",

                MessageBoxButton.OK,

                MessageBoxImage.Error
            );
        }
        finally
        {
            screenButton.IsEnabled =
                true;

            screenButton.Content =
                "Ver pantalla";
        }
    }

    // ==========================================
    // VENTANA DE PANTALLA REMOTA
    // ==========================================

    private async Task ShowRemoteScreenAsync(
        string machineId,
        byte[] imageBytes)
    {
        var image =
            new Image
            {
                Stretch =
                    Stretch.Uniform,

                HorizontalAlignment =
                    HorizontalAlignment.Center,

                VerticalAlignment =
                    VerticalAlignment.Center
            };

        try
        {
            SetImageSource(
                image,
                imageBytes
            );
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"No se pudo mostrar la captura inicial de {machineId}.\n\n" +
                ex.Message,

                "ControlLab",

                MessageBoxButton.OK,

                MessageBoxImage.Warning
            );

            return;
        }

        var statusText =
            new TextBlock
            {
                Text =
                    $"● {machineId}  •  Conectada",

                Foreground =
                    new SolidColorBrush(
                        Color.FromRgb(
                            50,
                            213,
                            131
                        )
                    ),

                FontSize = 12,

                Margin =
                    new Thickness(
                        15,
                        10,
                        15,
                        10
                    )
            };

        var window =
            new Window
            {
                Title =
                    $"ControlLab — Pantalla de {machineId}",

                Width = 1100,

                Height = 700,

                MinWidth = 800,

                MinHeight = 500,

                WindowStartupLocation =
                    WindowStartupLocation.CenterOwner,

                Owner = this,

                Background =
                    new SolidColorBrush(
                        Color.FromRgb(
                            10,
                            12,
                            16
                        )
                    )
            };

        var layout =
            new Grid();

        layout.RowDefinitions.Add(
            new RowDefinition
            {
                Height =
                    new GridLength(
                        1,
                        GridUnitType.Star
                    )
            }
        );

        layout.RowDefinitions.Add(
            new RowDefinition
            {
                Height =
                    GridLength.Auto
            }
        );

        Grid.SetRow(
            image,
            0
        );

        Grid.SetRow(
            statusText,
            1
        );

        layout.Children.Add(
            image
        );

        layout.Children.Add(
            statusText
        );

        window.Content =
            layout;

        window.Show();

        // ==========================================
        // ACTUALIZACIÓN DE PANTALLA
        // ==========================================

        using var cancellation =
            new CancellationTokenSource();

        window.Closed +=
            (_, _) =>
            {
                cancellation.Cancel();
            };

        while (
            !cancellation.Token.IsCancellationRequested
        )
        {
            try
            {
                await Task.Delay(
                    1000,
                    cancellation.Token
                );

                if (
                    cancellation.Token
                        .IsCancellationRequested
                )
                {
                    break;
                }

                // ==========================================
                // SOLICITAR NUEVA CAPTURA
                // ==========================================

                var requestResponse =
                    await _httpClient.PostAsync(
                        $"{ServerUrl}/api/agents/" +
                        $"{Uri.EscapeDataString(machineId)}" +
                        "/screen",
                        null,
                        cancellation.Token
                    );

                if (
                    !requestResponse
                        .IsSuccessStatusCode
                )
                {
                    statusText.Text =
                        $"● {machineId}  •  Sin conexión";

                    continue;
                }

                // ==========================================
                // ESPERAR RESPUESTA DEL AGENT
                // ==========================================

                byte[]? newImage = null;

                for (
                    int attempt = 0;
                    attempt < 10;
                    attempt++
                )
                {
                    await Task.Delay(
                        100,
                        cancellation.Token
                    );

                    try
                    {
                        newImage =
                            await _httpClient
                                .GetByteArrayAsync(
                                    $"{ServerUrl}/api/agents/" +
                                    $"{Uri.EscapeDataString(machineId)}" +
                                    "/screen",
                                    cancellation.Token
                                );

                        if (
                            IsValidJpeg(
                                newImage
                            )
                        )
                        {
                            break;
                        }

                        newImage = null;
                    }
                    catch
                    {
                        // La captura todavía no está disponible.
                    }
                }

                if (
                    newImage == null ||
                    !IsValidJpeg(newImage)
                )
                {
                    statusText.Text =
                        $"● {machineId}  •  Esperando captura";

                    continue;
                }

                // ==========================================
                // ACTUALIZAR IMAGEN
                // ==========================================

                try
                {
                    SetImageSource(
                        image,
                        newImage
                    );

                    statusText.Text =
                        $"● {machineId}  •  Actualizada {DateTime.Now:HH:mm:ss}";
                }
                catch (Exception ex)
                {
                    statusText.Text =
                        $"● {machineId}  •  Error de imagen";

                    Console.WriteLine(
                        $"Error procesando captura de {machineId}: {ex.Message}"
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
                statusText.Text =
                    $"● {machineId}  •  Error: {ex.Message}";
            }
        }
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
    // CONVERTIR BYTES A IMAGEN WPF
    // ==========================================

    private static void SetImageSource(
        Image image,
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

        image.Source =
            bitmap;
    }

    // ==========================================
    // DETALLES
    // ==========================================

    private static void AddDetail(
        StackPanel panel,
        string title,
        string value)
    {
        panel.Children.Add(
            new TextBlock
            {
                Text = title,

                FontSize = 10,

                Foreground =
                    new SolidColorBrush(
                        Color.FromRgb(
                            139,
                            147,
                            161
                        )
                    ),

                Margin =
                    new Thickness(
                        0,
                        0,
                        0,
                        4
                    )
            }
        );

        panel.Children.Add(
            new TextBlock
            {
                Text = value,

                FontSize = 14,

                Foreground =
                    Brushes.White,

                Margin =
                    new Thickness(
                        0,
                        0,
                        0,
                        15
                    )
            }
        );
    }

    // ==========================================
    // CERRAR
    // ==========================================

    protected override void OnClosed(
        EventArgs e)
    {
        _refreshTimer.Stop();

        _httpClient.Dispose();

        base.OnClosed(e);
    }
}

// ==========================================
// RESPUESTA DE AGENTES
// ==========================================

public class AgentsResponse
{
    public int Total { get; set; }

    public List<AgentInfo> Agents { get; set; } =
        new();
}