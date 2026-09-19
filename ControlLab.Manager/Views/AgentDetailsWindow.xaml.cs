using System;
using System.IO;
using System.Net.WebSockets;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Diagnostics;
using ControlLab.Manager.Models;
using ControlLab.Manager.Services;

namespace ControlLab.Manager.Views;

public partial class AgentDetailsWindow : Window
{
    private readonly AgentInfo? _agent;

    private readonly string _machineId;

    private readonly ControlLabApi _api;

    private ClientWebSocket? _socket;

    private CancellationTokenSource? _streamCancellation;

    private Task? _receiveTask;

    private bool _closing;

    private bool _streamStarted;

    // =========================================================
    // CONTROL REMOTO
    // =========================================================

    private bool _remoteControlEnabled;

    private bool _keyboardControlEnabled;

    private bool _authorized;

    private bool _leftMouseDown;

    private bool _rightMouseDown;

    private bool _middleMouseDown;

    // Teclas que actualmente consideramos presionadas en la PC remota.
    // Evita enviar KEY_UP duplicados y permite liberar teclas pendientes
    // cuando se desactiva el teclado o se cierra la ventana.
    private readonly HashSet<string> _pressedRemoteKeys =
        new(StringComparer.OrdinalIgnoreCase);

    private DateTime _lastMouseMoveSentUtc =
        DateTime.MinValue;

    private Point _lastMousePoint =
        new(-1, -1);

    private const int RemoteFrameWidth = 640;

    private const int RemoteFrameHeight = 360;

    private const int MouseMoveIntervalMs = 35;

    // =========================================================
    // MEDICIÓN REAL DE FPS
    // =========================================================

    private readonly Stopwatch _fpsTimer =
        new();

    private int _fpsFrameCount;

    private long _lastFpsUpdateTicks;

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

        _authorized =
            agent?.Authorized ?? false;

        _api =
            api;

        Title =
            $"ControlLab · {GetDisplayName()}";

        LoadInterface();

        Loaded +=
            async (_, _) =>
            {
                if (_authorized && IsOnline())
                {
                    await StartStreamAsync();
                }
                else
                {
                    StreamStatusText.Text =
                        "  AUTORIZACIÓN REQUERIDA";

                    StreamStatusText.Foreground =
                        new SolidColorBrush(
                            Color.FromRgb(
                                255,
                                196,
                                77
                            )
                        );
                }
            };

        Closed +=
            (_, _) =>
            {
                _ = StopStreamAsync();
            };
    }

    // =========================================================
    // CARGAR INTERFAZ
    // =========================================================

    private void LoadInterface()
    {
        bool online =
            _agent != null &&
            _agent.Status.Equals(
                "online",
                StringComparison.OrdinalIgnoreCase
            );

        string displayName =
            GetDisplayName();

        MachineIdText.Text =
            displayName;

        HostnameText.Text =
            online
                ? _agent!.Hostname
                : "Sin conexión";

        StatusText.Text =
            online
                ? "● EN LÍNEA"
                : "● DESCONECTADO";

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

        NoConnectionPanel.Visibility =
            online
                ? Visibility.Collapsed
                : Visibility.Visible;

        RemoteControlButton.IsEnabled =
            online && _authorized;

        KeyboardButton.IsEnabled =
            online && _authorized;

        LockSessionButton.IsEnabled =
            online && _authorized;

        RestartButton.IsEnabled =
            online && _authorized;

        ShutdownButton.IsEnabled =
            online && _authorized;

        AuthorizationButton.Content =
            _authorized
                ? "🛡  Autorizado"
                : "🛡  Autorizar";

        if (!online)
        {
            StreamStatusText.Text =
                "  SIN CONEXIÓN";

            StreamStatusText.Foreground =
                new SolidColorBrush(
                    Color.FromRgb(
                        249,
                        112,
                        102
                    )
                );

            ResolutionText.Text =
                "—";

            FpsText.Text =
                "—";
        }
    }

    // =========================================================
    // NOMBRE VISIBLE
    // =========================================================

    private string GetDisplayName()
    {
        if (
            !string.IsNullOrWhiteSpace(
                _agent?.DisplayName
            )
        )
        {
            return _agent!.DisplayName;
        }

        return _machineId;
    }

    // =========================================================
    // INICIAR STREAM
    // =========================================================

    private async Task<bool> StartStreamAsync()
    {
        if (_streamStarted)
        {
            return true;
        }

        try
        {
            StreamStatusText.Text =
                "  CONECTANDO...";

            StreamStatusText.Foreground =
                new SolidColorBrush(
                    Color.FromRgb(
                        255,
                        196,
                        77
                    )
                );

            ResolutionText.Text =
                "Conectando...";

            FpsText.Text =
                "—";

            // =================================================
            // PEDIR AL AGENT QUE INICIE LA TRANSMISIÓN
            // =================================================

            bool commandSent =
                await _api.StartScreenStreamAsync(
                    _machineId
                );

            if (!commandSent)
            {
                StreamStatusText.Text =
                    "  NO DISPONIBLE";

                StreamStatusText.Foreground =
                    new SolidColorBrush(
                        Color.FromRgb(
                            249,
                            112,
                            102
                        )
                    );

                return false;
            }

            // =================================================
            // CREAR SOCKET DEL VISOR
            // =================================================

            _streamCancellation =
                new CancellationTokenSource();

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
                $"📡 Conectando visor: {streamUri}"
            );

            await _socket.ConnectAsync(
                streamUri,
                _streamCancellation.Token
            );

            _streamStarted =
                true;
            // =================================================
            // REINICIAR MEDICIÓN DE FPS
            // =================================================

            _fpsFrameCount = 0;

            _fpsTimer.Restart();

            _lastFpsUpdateTicks =
                Stopwatch.GetTimestamp();

            FpsText.Text =
                "0 FPS";
            StreamStatusText.Text =
                "  EN VIVO";

            StreamStatusText.Foreground =
                new SolidColorBrush(
                    Color.FromRgb(
                        50,
                        213,
                        131
                    )
                );

            ResolutionText.Text =
                "640 × 360";

            FpsText.Text =
                "0 FPS";

            NoConnectionPanel.Visibility =
                Visibility.Collapsed;

            Console.WriteLine(
                $"🟢 Visor conectado: {_machineId}"
            );

            _receiveTask =
                ReceiveFramesAsync(
                    _socket,
                    _streamCancellation.Token
                );

            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"❌ Error iniciando stream de {_machineId}: " +
                $"{ex.Message}"
            );

            StreamStatusText.Text =
                "  ERROR DE TRANSMISIÓN";

            StreamStatusText.Foreground =
                new SolidColorBrush(
                    Color.FromRgb(
                        249,
                        112,
                        102
                    )
                );

            return false;
        }
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

        try
        {
            while (
                socket.State ==
                WebSocketState.Open &&
                !cancellationToken.IsCancellationRequested
            )
            {
                using var memory =
                    new MemoryStream();

                WebSocketReceiveResult result;

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

                            // ==========================================
                            // CONTAR FRAME
                            // ==========================================

                            _fpsFrameCount++;

                            // ==========================================
                            // CALCULAR FPS
                            // ==========================================

                            long nowTicks =
                                Stopwatch.GetTimestamp();

                            double elapsedSeconds =
                                (double)(
                                    nowTicks -
                                    _lastFpsUpdateTicks
                                ) /
                                Stopwatch.Frequency;

                            if (
                                elapsedSeconds >= 1.0
                            )
                            {
                                double fps =
                                    _fpsFrameCount /
                                    elapsedSeconds;

                                FpsText.Text =
                                    $"{fps:0.0} FPS";

                                _fpsFrameCount =
                                    0;

                                _lastFpsUpdateTicks =
                                    nowTicks;
                            }

                            // ==========================================
                            // ESTADO
                            // ==========================================

                            StreamStatusText.Text =
                                "  EN VIVO";

                            StreamStatusText.Foreground =
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
        }
        catch (
            OperationCanceledException
        )
        {
        }
        catch (
            WebSocketException ex
        )
        {
            Console.WriteLine(
                $"⚠️ Stream desconectado de {_machineId}: " +
                $"{ex.Message}"
            );
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"❌ Error recibiendo stream: " +
                $"{ex.Message}"
            );
        }
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
            return;
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
            return;
        }

        var bitmap =
            new WriteableBitmap(
                decoder.Frames[0]
            );

        bitmap.Freeze();

        RemoteImage.Source =
            bitmap;
    }

    // =========================================================
    // VALIDAR JPEG
    // =========================================================

    private static bool IsValidJpeg(
        byte[]? imageBytes)
    {
        if (
            imageBytes == null ||
            imageBytes.Length < 4
        )
        {
            return false;
        }

        return
            imageBytes[0] == 0xFF &&
            imageBytes[1] == 0xD8 &&
            imageBytes[2] == 0xFF &&
            imageBytes[^2] == 0xFF &&
            imageBytes[^1] == 0xD9;
    }

    // =========================================================
    // CONTROL REMOTO
    // =========================================================

    private void RemoteControlButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (!IsOnline())
        {
            return;
        }

        _remoteControlEnabled =
            !_remoteControlEnabled;

        RemoteInputOverlay.Visibility =
            _remoteControlEnabled
                ? Visibility.Visible
                : Visibility.Collapsed;

        RemoteControlActiveBadge.Visibility =
            _remoteControlEnabled
                ? Visibility.Visible
                : Visibility.Collapsed;

        RemoteControlButton.Content =
            _remoteControlEnabled
                ? "🖱  Control activo"
                : "🖱  Control remoto";

        RemoteControlButton.Background =
            _remoteControlEnabled
                ? new SolidColorBrush(
                    Color.FromRgb(18, 67, 52))
                : new SolidColorBrush(
                    Color.FromRgb(18, 42, 64));

        RemoteControlButton.BorderBrush =
            _remoteControlEnabled
                ? new SolidColorBrush(
                    Color.FromRgb(50, 213, 131))
                : new SolidColorBrush(
                    Color.FromRgb(39, 105, 163));

        if (!_remoteControlEnabled)
        {
            ReleasePressedMouseButtons();
        }

        if (_remoteControlEnabled)
        {
            RemoteInputOverlay.Focus();
        }
    }

    // =========================================================
    // TECLADO
    // =========================================================

    private void KeyboardButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (!IsOnline())
        {
            return;
        }

        _keyboardControlEnabled =
            !_keyboardControlEnabled;

        KeyboardButton.Content =
            _keyboardControlEnabled
                ? "⌨  Teclado activo"
                : "⌨  Teclado";

        KeyboardButton.Background =
            _keyboardControlEnabled
                ? new SolidColorBrush(
                    Color.FromRgb(45, 43, 82))
                : new SolidColorBrush(
                    Color.FromRgb(25, 28, 50));

        KeyboardButton.BorderBrush =
            _keyboardControlEnabled
                ? new SolidColorBrush(
                    Color.FromRgb(155, 159, 255))
                : new SolidColorBrush(
                    Color.FromRgb(71, 76, 138));

        if (_keyboardControlEnabled)
        {
            ActivateKeyboardFocus();
        }
        else
        {
            ReleasePressedRemoteKeys();
        }
    }

    // =========================================================
    // MOUSE REMOTO
    // =========================================================

    private async void RemoteInputOverlay_MouseMove(
        object sender,
        System.Windows.Input.MouseEventArgs e)
    {
        if (!_remoteControlEnabled)
        {
            return;
        }

        Point point =
            e.GetPosition(
                RemoteInputOverlay
            );

        if (!TryGetNormalizedCoordinates(
                point,
                out double x,
                out double y))
        {
            return;
        }

        if (_lastMousePoint.X >= 0 &&
            _lastMousePoint.Y >= 0)
        {
            double distance =
                Math.Abs(point.X - _lastMousePoint.X) +
                Math.Abs(point.Y - _lastMousePoint.Y);

            if (distance < 1)
            {
                return;
            }
        }

        DateTime now =
            DateTime.UtcNow;

        if (
            (now - _lastMouseMoveSentUtc)
                .TotalMilliseconds
            < MouseMoveIntervalMs
        )
        {
            return;
        }

        _lastMousePoint = point;
        _lastMouseMoveSentUtc = now;

        await SendMouseAsync(
            "MOUSE_MOVE",
            x,
            y
        );
    }

    private async void RemoteInputOverlay_MouseLeftButtonDown(
        object sender,
        System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_remoteControlEnabled)
        {
            return;
        }

        _leftMouseDown = true;

        Point point =
            e.GetPosition(
                RemoteInputOverlay
            );

        if (TryGetNormalizedCoordinates(
                point,
                out double x,
                out double y))
        {
            await SendMouseAsync(
                "MOUSE_DOWN",
                x,
                y,
                "LEFT"
            );
        }

        RemoteInputOverlay.CaptureMouse();
        e.Handled = true;
    }

    private async void RemoteInputOverlay_MouseLeftButtonUp(
        object sender,
        System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_remoteControlEnabled)
        {
            return;
        }

        _leftMouseDown = false;

        Point point =
            e.GetPosition(
                RemoteInputOverlay
            );

        if (TryGetNormalizedCoordinates(
                point,
                out double x,
                out double y))
        {
            await SendMouseAsync(
                "MOUSE_UP",
                x,
                y,
                "LEFT"
            );
        }

        if (!_leftMouseDown &&
            !_rightMouseDown &&
            !_middleMouseDown)
        {
            RemoteInputOverlay.ReleaseMouseCapture();
        }

        e.Handled = true;
    }

    private async void RemoteInputOverlay_MouseRightButtonDown(
        object sender,
        System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_remoteControlEnabled)
        {
            return;
        }

        _rightMouseDown = true;

        Point point =
            e.GetPosition(
                RemoteInputOverlay
            );

        if (TryGetNormalizedCoordinates(
                point,
                out double x,
                out double y))
        {
            await SendMouseAsync(
                "MOUSE_DOWN",
                x,
                y,
                "RIGHT"
            );
        }

        RemoteInputOverlay.CaptureMouse();
        e.Handled = true;
    }

    private async void RemoteInputOverlay_MouseRightButtonUp(
        object sender,
        System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_remoteControlEnabled)
        {
            return;
        }

        _rightMouseDown = false;

        Point point =
            e.GetPosition(
                RemoteInputOverlay
            );

        if (TryGetNormalizedCoordinates(
                point,
                out double x,
                out double y))
        {
            await SendMouseAsync(
                "MOUSE_UP",
                x,
                y,
                "RIGHT"
            );
        }

        if (!_leftMouseDown &&
            !_rightMouseDown &&
            !_middleMouseDown)
        {
            RemoteInputOverlay.ReleaseMouseCapture();
        }

        e.Handled = true;
    }

    private async void RemoteInputOverlay_MouseDown(
        object sender,
        System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_remoteControlEnabled)
        {
            return;
        }

        if (e.ChangedButton !=
            System.Windows.Input.MouseButton.Middle)
        {
            return;
        }

        _middleMouseDown = true;

        Point point =
            e.GetPosition(
                RemoteInputOverlay
            );

        if (TryGetNormalizedCoordinates(
                point,
                out double x,
                out double y))
        {
            await SendMouseAsync(
                "MOUSE_DOWN",
                x,
                y,
                "MIDDLE"
            );
        }

        RemoteInputOverlay.CaptureMouse();
        e.Handled = true;
    }

    private async void RemoteInputOverlay_MouseUp(
        object sender,
        System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_remoteControlEnabled)
        {
            return;
        }

        if (e.ChangedButton !=
            System.Windows.Input.MouseButton.Middle)
        {
            return;
        }

        _middleMouseDown = false;

        Point point =
            e.GetPosition(
                RemoteInputOverlay
            );

        if (TryGetNormalizedCoordinates(
                point,
                out double x,
                out double y))
        {
            await SendMouseAsync(
                "MOUSE_UP",
                x,
                y,
                "MIDDLE"
            );
        }

        if (!_leftMouseDown &&
            !_rightMouseDown &&
            !_middleMouseDown)
        {
            RemoteInputOverlay.ReleaseMouseCapture();
        }

        e.Handled = true;
    }

    private static bool TryGetNormalizedCoordinates(
        Point point,
        out double x,
        out double y)
    {
        x = 0;
        y = 0;

        if (point.X < 0 ||
            point.Y < 0)
        {
            return false;
        }

        double width =
            Application.Current.Windows
                .OfType<AgentDetailsWindow>()
                .FirstOrDefault()
                ?.RemoteInputOverlay.ActualWidth
            ?? 0;

        double height =
            Application.Current.Windows
                .OfType<AgentDetailsWindow>()
                .FirstOrDefault()
                ?.RemoteInputOverlay.ActualHeight
            ?? 0;

        if (width <= 0 ||
            height <= 0)
        {
            return false;
        }

        // El stream mantiene una relación 16:9.
        // Ajustamos las coordenadas al área real de la imagen,
        // ignorando las franjas laterales/superiores que pueda
        // producir Stretch=Uniform.
        double targetAspect =
            (double)RemoteFrameWidth /
            RemoteFrameHeight;

        double containerAspect =
            width / height;

        double imageWidth;
        double imageHeight;
        double offsetX;
        double offsetY;

        if (containerAspect > targetAspect)
        {
            imageHeight = height;
            imageWidth = height * targetAspect;
            offsetX = (width - imageWidth) / 2;
            offsetY = 0;
        }
        else
        {
            imageWidth = width;
            imageHeight = width / targetAspect;
            offsetX = 0;
            offsetY = (height - imageHeight) / 2;
        }

        double imageX =
            point.X - offsetX;

        double imageY =
            point.Y - offsetY;

        if (imageX < 0 ||
            imageY < 0 ||
            imageX > imageWidth ||
            imageY > imageHeight)
        {
            return false;
        }

        x =
            Math.Clamp(
                imageX / imageWidth,
                0,
                1
            );

        y =
            Math.Clamp(
                imageY / imageHeight,
                0,
                1
            );

        return true;
    }

    private async Task SendMouseAsync(
        string action,
        double x,
        double y,
        string? button = null)
    {
        try
        {
            await _api.SendRemoteInputAsync(
                _machineId,
                action,
                x,
                y,
                button
            );
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"❌ Error enviando mouse: {ex.Message}"
            );
        }
    }

    private void ReleasePressedMouseButtons()
    {
        if (_leftMouseDown)
        {
            _leftMouseDown = false;
            _ = SendMouseAsync(
                "MOUSE_UP",
                0,
                0,
                "LEFT"
            );
        }

        if (_rightMouseDown)
        {
            _rightMouseDown = false;
            _ = SendMouseAsync(
                "MOUSE_UP",
                0,
                0,
                "RIGHT"
            );
        }

        if (_middleMouseDown)
        {
            _middleMouseDown = false;
            _ = SendMouseAsync(
                "MOUSE_UP",
                0,
                0,
                "MIDDLE"
            );
        }

        RemoteInputOverlay.ReleaseMouseCapture();
    }

    private async void RemoteInputOverlay_MouseWheel(
        object sender,
        System.Windows.Input.MouseWheelEventArgs e)
    {
        if (!_remoteControlEnabled)
        {
            return;
        }

        Point point =
            e.GetPosition(
                RemoteInputOverlay
            );

        if (TryGetNormalizedCoordinates(
                point,
                out double x,
                out double y))
        {
            await SendMouseWheelAsync(
                x,
                y,
                e.Delta
            );
        }

        e.Handled = true;
    }

    private async Task SendMouseWheelAsync(
        double x,
        double y,
        int delta)
    {
        try
        {
            await _api.SendRemoteInputAsync(
                _machineId,
                "MOUSE_WHEEL",
                x,
                y,
                delta: delta
            );
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"❌ Error enviando rueda: {ex.Message}"
            );
        }
    }

    // =========================================================
    // TECLADO REMOTO
    // =========================================================

    private void ActivateKeyboardFocus()
    {
        if (!_keyboardControlEnabled)
        {
            return;
        }

        Activate();
        Focus();
        System.Windows.Input.Keyboard.Focus(this);
    }

    private async void Window_PreviewKeyDown(
        object sender,
        System.Windows.Input.KeyEventArgs e)
    {
        if (!_keyboardControlEnabled ||
            e.IsRepeat)
        {
            return;
        }

        string? key =
            ConvertWpfKeyToRemoteKey(
                e.Key == System.Windows.Input.Key.System
                    ? e.SystemKey
                    : e.Key
            );

        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        // Si ya está marcada como presionada, no volvemos a enviar
        // KEY_DOWN aunque Windows haya generado otro evento.
        if (!_pressedRemoteKeys.Add(key))
        {
            e.Handled = true;
            return;
        }

        try
        {
            await SendKeyboardAsync(
                "KEY_DOWN",
                key
            );
        }
        catch
        {
            // Si el envío falla, permitimos reintentar la tecla.
            _pressedRemoteKeys.Remove(key);
        }

        e.Handled = true;
    }

    private async void Window_PreviewKeyUp(
        object sender,
        System.Windows.Input.KeyEventArgs e)
    {
        if (!_keyboardControlEnabled ||
            e.IsRepeat)
        {
            return;
        }

        string? key =
            ConvertWpfKeyToRemoteKey(
                e.Key == System.Windows.Input.Key.System
                    ? e.SystemKey
                    : e.Key
            );

        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        // Solo enviamos KEY_UP si previamente enviamos KEY_DOWN.
        // Esto elimina los KEY_UP fantasma/duplicados.
        if (!_pressedRemoteKeys.Remove(key))
        {
            e.Handled = true;
            return;
        }

        try
        {
            await SendKeyboardAsync(
                "KEY_UP",
                key
            );
        }
        catch
        {
            // No hacemos reintentos automáticos de KEY_UP para evitar
            // duplicados que puedan saturar la conexión.
        }

        e.Handled = true;
    }

    private async Task SendKeyboardAsync(
        string action,
        string key)
    {
        try
        {
            await _api.SendRemoteInputAsync(
                _machineId,
                action,
                key: key
            );
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"❌ Error enviando teclado: {ex.Message}"
            );
        }
    }

    private static string? ConvertWpfKeyToRemoteKey(
        System.Windows.Input.Key key)
    {
        if (key >= System.Windows.Input.Key.A &&
            key <= System.Windows.Input.Key.Z)
        {
            return key.ToString();
        }

        if (key >= System.Windows.Input.Key.D0 &&
            key <= System.Windows.Input.Key.D9)
        {
            return (
                (int)(key -
                System.Windows.Input.Key.D0)
            ).ToString();
        }

        var map =
            new Dictionary<
                System.Windows.Input.Key,
                string>
            {
                [System.Windows.Input.Key.Enter] = "ENTER",
                [System.Windows.Input.Key.Escape] = "ESC",
                [System.Windows.Input.Key.Tab] = "TAB",
                [System.Windows.Input.Key.Space] = "SPACE",
                [System.Windows.Input.Key.Back] = "BACKSPACE",
                [System.Windows.Input.Key.Delete] = "DELETE",
                [System.Windows.Input.Key.Insert] = "INSERT",
                [System.Windows.Input.Key.Home] = "HOME",
                [System.Windows.Input.Key.End] = "END",
                [System.Windows.Input.Key.PageUp] = "PAGEUP",
                [System.Windows.Input.Key.PageDown] = "PAGEDOWN",

                [System.Windows.Input.Key.Left] = "LEFT",
                [System.Windows.Input.Key.Up] = "UP",
                [System.Windows.Input.Key.Right] = "RIGHT",
                [System.Windows.Input.Key.Down] = "DOWN",

                [System.Windows.Input.Key.LeftShift] = "SHIFT",
                [System.Windows.Input.Key.RightShift] = "SHIFT",
                [System.Windows.Input.Key.LeftCtrl] = "CTRL",
                [System.Windows.Input.Key.RightCtrl] = "CTRL",
                [System.Windows.Input.Key.LeftAlt] = "ALT",
                [System.Windows.Input.Key.RightAlt] = "ALT",
                [System.Windows.Input.Key.LWin] = "WIN",
                [System.Windows.Input.Key.RWin] = "WIN",

                [System.Windows.Input.Key.CapsLock] = "CAPSLOCK",
                [System.Windows.Input.Key.NumLock] = "NUMLOCK",
                [System.Windows.Input.Key.Scroll] = "SCROLLLOCK",

                [System.Windows.Input.Key.F1] = "F1",
                [System.Windows.Input.Key.F2] = "F2",
                [System.Windows.Input.Key.F3] = "F3",
                [System.Windows.Input.Key.F4] = "F4",
                [System.Windows.Input.Key.F5] = "F5",
                [System.Windows.Input.Key.F6] = "F6",
                [System.Windows.Input.Key.F7] = "F7",
                [System.Windows.Input.Key.F8] = "F8",
                [System.Windows.Input.Key.F9] = "F9",
                [System.Windows.Input.Key.F10] = "F10",
                [System.Windows.Input.Key.F11] = "F11",
                [System.Windows.Input.Key.F12] = "F12"
            };

        return map.TryGetValue(
            key,
            out string? result)
                ? result
                : null;
    }

    // =========================================================
    // BLOQUEAR SESIÓN
    // =========================================================

    private async void LockSessionButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var result =
            MessageBox.Show(
                $"¿Deseas bloquear la sesión de {GetDisplayName()}?",
                "Bloquear sesión",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning
            );

        if (
            result !=
            MessageBoxResult.Yes
        )
        {
            return;
        }

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
                    "No se pudo bloquear la sesión.",
                    "ControlLab",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );

                return;
            }

            Console.WriteLine(
                $"🔒 Sesión bloqueada: {_machineId}"
            );
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Error bloqueando la sesión:\n\n{ex.Message}",
                "ControlLab",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
        finally
        {
            LockSessionButton.IsEnabled =
                IsOnline();

            LockSessionButton.Content =
                "🔒  Bloquear";
        }
    }

    // =========================================================
    // REINICIAR
    // =========================================================

    private async void RestartButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var result =
            MessageBox.Show(
                $"¿Deseas reiniciar {GetDisplayName()}?",
                "Reiniciar PC",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning
            );

        if (
            result !=
            MessageBoxResult.Yes
        )
        {
            return;
        }

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
                    "No se pudo enviar la orden de reinicio.",
                    "ControlLab",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );

                return;
            }

            Console.WriteLine(
                $"🔄 Reinicio solicitado: {_machineId}"
            );
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Error reiniciando el equipo:\n\n{ex.Message}",
                "ControlLab",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
        finally
        {
            RestartButton.IsEnabled =
                IsOnline();

            RestartButton.Content =
                "↻  Reiniciar";
        }
    }

    // =========================================================
    // APAGAR
    // =========================================================

    private async void ShutdownButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var result =
            MessageBox.Show(
                $"¿Deseas apagar {GetDisplayName()}?",
                "Apagar PC",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning
            );

        if (
            result !=
            MessageBoxResult.Yes
        )
        {
            return;
        }

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
                    "No se pudo enviar la orden de apagado.",
                    "ControlLab",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );

                return;
            }

            Console.WriteLine(
                $"⏻ Apagado solicitado: {_machineId}"
            );
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Error apagando el equipo:\n\n{ex.Message}",
                "ControlLab",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
        finally
        {
            ShutdownButton.IsEnabled =
                IsOnline();

            ShutdownButton.Content =
                "⏻  Apagar";
        }
    }

    private async void AuthorizationButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            AuthorizationButton.IsEnabled =
                false;

            if (_authorized)
            {
                var result =
                    MessageBox.Show(
                        $"¿Deseas revocar la autorización de {GetDisplayName()}?",
                        "Revocar autorización",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning
                    );

                if (result != MessageBoxResult.Yes)
                    return;

                bool success =
                    await _api.RevokeAgentAsync(
                        _machineId
                    );

                if (!success)
                {
                    MessageBox.Show(
                        "No se pudo revocar la autorización.",
                        "ControlLab",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning
                    );

                    return;
                }

                _authorized = false;

                if (_streamStarted)
                {
                    await StopStreamAsync();
                }

                _remoteControlEnabled = false;
                _keyboardControlEnabled = false;
                ReleasePressedMouseButtons();
                ReleasePressedRemoteKeys();

                RemoteInputOverlay.Visibility =
                    Visibility.Collapsed;

                RemoteControlActiveBadge.Visibility =
                    Visibility.Collapsed;

                RemoteControlButton.Content =
                    "🖱  Control remoto";

                KeyboardButton.Content =
                    "⌨  Teclado";

                StreamStatusText.Text =
                    "  AUTORIZACIÓN REQUERIDA";

                StreamStatusText.Foreground =
                    new SolidColorBrush(
                        Color.FromRgb(
                            255,
                            196,
                            77
                        )
                    );
            }
            else
            {
                var result =
                    MessageBox.Show(
                        $"¿Deseas autorizar el equipo {GetDisplayName()}?",
                        "Autorizar equipo",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Information
                    );

                if (result != MessageBoxResult.Yes)
                    return;

                bool success =
                    await _api.AuthorizeAgentAsync(
                        _machineId
                    );

                if (!success)
                {
                    MessageBox.Show(
                        "No se pudo autorizar el equipo.",
                        "ControlLab",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning
                    );

                    return;
                }

                _authorized = true;

                if (IsOnline() && !_streamStarted)
                {
                    await StartStreamAsync();
                }
            }

            AuthorizationButton.Content =
                _authorized
                    ? "🛡  Autorizado"
                    : "🛡  Autorizar";

            bool online =
                IsOnline();

            RemoteControlButton.IsEnabled =
                online && _authorized;

            KeyboardButton.IsEnabled =
                online && _authorized;

            LockSessionButton.IsEnabled =
                online && _authorized;

            RestartButton.IsEnabled =
                online && _authorized;

            ShutdownButton.IsEnabled =
                online && _authorized;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Error cambiando la autorización:\n\n{ex.Message}",
                "ControlLab",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
        finally
        {
            AuthorizationButton.IsEnabled =
                true;
        }
    }

    // =========================================================
    // LIBERAR TECLAS REMOTAS
    // =========================================================

    private void ReleasePressedRemoteKeys()
    {
        if (_pressedRemoteKeys.Count == 0)
        {
            return;
        }

        string[] keys =
            _pressedRemoteKeys.ToArray();

        _pressedRemoteKeys.Clear();

        foreach (string key in keys)
        {
            _ = SendKeyboardAsync(
                "KEY_UP",
                key
            );
        }
    }

    // =========================================================
    // DETENER STREAM
    // =========================================================

    private async Task StopStreamAsync()
    {
        _remoteControlEnabled = false;
        _keyboardControlEnabled = false;

        ReleasePressedMouseButtons();
        ReleasePressedRemoteKeys();

        if (_closing)
        {
            return;
        }

        _closing =
            true;

        try
        {
            _streamCancellation?.Cancel();

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
                        "Ventana cerrada",
                        CancellationToken.None
                    );
                }
                catch
                {
                }
            }

            _socket?.Dispose();

            _socket =
                null;

            // =================================================
            // AVISAR AL AGENT QUE DETENGA LA CAPTURA
            // =================================================

            if (_streamStarted)
            {
                try
                {
                    await _api.StopScreenStreamAsync(
                        _machineId
                    );
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
        finally
        {
            _streamCancellation?.Dispose();

            _streamCancellation =
                null;

            _streamStarted =
                false;

            _receiveTask =
                null;

            Console.WriteLine(
                $"🔴 Stream detenido: {_machineId}"
            );
        }
    }

    // =========================================================
    // ESTADO ONLINE
    // =========================================================

    private bool IsOnline()
    {
        return
            _agent != null &&
            _agent.Status.Equals(
                "online",
                StringComparison.OrdinalIgnoreCase
            );
    }

    // =========================================================
    // CERRAR
    // =========================================================

    private void CloseButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        Close();
    }
}