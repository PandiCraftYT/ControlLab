using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using ControlLab.Manager.Server;
using System.IO;
using ControlLab.Manager.Services;
namespace ControlLab.Manager;

public sealed class ControlLabServer
{
    private const int Port = 8080;
    private const int DiscoveryPort = 45678;

    private readonly AgentRegistry _agents = new();
    private readonly ControlLab.Manager.Data.ControlLabDatabase _database = new();
    private readonly ConcurrentDictionary<string, ScreenCaptureData> _screens = new();
    private readonly ConcurrentDictionary<string, ScreenCaptureData> _previews = new();
    private readonly ConcurrentDictionary<string, ScreenCaptureData> _streamFrames = new();
    private readonly HttpListener _listener = new();

    private readonly WebSocketHandler _webSocket;
    private readonly HttpApiHandler _http;
    private readonly ScreenStreamHandler _screenStream;

    private CancellationTokenSource? _cancellation;
    private readonly string _authToken;
    private readonly string _sessionToken;
    private readonly AuthenticationService _authentication;
    // =========================================================
    // COLA DE ENVÍO WEBSOCKET
    // =========================================================
    // WebSocket no permite múltiples SendAsync simultáneos.
    // Todas las órdenes del Manager pasan por este semáforo.
    private readonly SemaphoreSlim _webSocketSendLock =
        new(1, 1);

public ControlLabServer(
    string sessionToken,
    AuthenticationService authentication)
{
    if (string.IsNullOrWhiteSpace(sessionToken))
        throw new ArgumentException(
            "El token de sesión no puede estar vacío.",
            nameof(sessionToken)
        );

    _sessionToken = sessionToken;

    _authentication = authentication;
    _authToken = LoadAuthToken();
    _webSocket = new WebSocketHandler(
        _agents,

        // ==========================================
        // CAPTURA COMPLETA
        // ==========================================

        (machineId, screen) =>
            _screens[machineId] = screen,

        // ==========================================
        // PREVIEW
        // ==========================================

        (machineId, preview) =>
            _previews[machineId] = preview,

        // ==========================================
        // GUARDAR AGENTE
        // ==========================================

        agent =>
            _database.RegisterAgent(
                agent.MachineId,
                agent.Hostname,
                agent.Platform,
                agent.AgentVersion
            ),

        // ==========================================
        // TOKEN
        // ==========================================

        _authToken,

        // ==========================================
        // AUTORIZACIÓN
        // ==========================================

        machineId =>
            _database.IsAgentAuthorized(
                machineId
            )
    );

    _http = new HttpApiHandler(
        () => _agents.Values,

        IsSessionAuthenticated,

        machineId =>
            _screens.TryGetValue(
                machineId,
                out var screen
            )
                ? screen
                : null,

        machineId =>
            _previews.TryGetValue(
                machineId,
                out var preview
            )
                ? preview
                : null,

        machineId =>
            _webSocket.GetStreamFrame(
                machineId
            ),

        SendCommandAsync,

        SendRemoteInputAsync,

        _database
    );

    _screenStream =
    new ScreenStreamHandler(
        machineId =>
            _webSocket.GetStreamFrame(
                machineId
            ),

        machineId =>
            _database.IsAgentAuthorized(
                machineId
            ),

        machineId =>
            _agents.Values.Any(
                agent =>
                    agent.MachineId.Equals(
                        machineId,
                        StringComparison.OrdinalIgnoreCase
                    ) &&
                    agent.Socket.State ==
                        System.Net.WebSockets.WebSocketState.Open
            ),

        IsSessionAuthenticated
    );
    }

    private bool IsSessionAuthenticated(
        HttpListenerContext context)
    {
        try
        {
            string? authorization =
                context.Request.Headers["Authorization"];

            if (string.IsNullOrWhiteSpace(
                    authorization))
            {
                return false;
            }

            const string prefix =
                "Bearer ";

            if (
                !authorization.StartsWith(
                    prefix,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return false;
            }

            string providedToken =
                authorization[
                    prefix.Length..]
                    .Trim();

            if (string.IsNullOrWhiteSpace(
                    providedToken))
            {
                return false;
            }

            if (!string.Equals(
                    providedToken,
                    _sessionToken,
                    StringComparison.Ordinal))
            {
                return false;
            }

            return _authentication.IsSessionValid(
                providedToken
            );
        }
        catch
        {
            return false;
        }
    }
    public async Task StartAsync()
    {
        if (_listener.IsListening)
            return;

        _cancellation =
            new CancellationTokenSource();

        _listener.Prefixes.Add(
            $"http://+:{Port}/"
        );

        _listener.Start();
        Console.WriteLine(
            $"💾 SQLite: {_database.CountAgents()} equipo(s) registrado(s)"
        );
        _ = Task.Run(
            () => DiscoveryLoopAsync(
                _cancellation.Token
            )
        );

        Console.WriteLine("=================================");
        Console.WriteLine("       ControlLab Server");
        Console.WriteLine("=================================");
        Console.WriteLine(
            $"Servidor iniciado en puerto {Port}"
        );
        Console.WriteLine(
            $"HTTP: http://localhost:{Port}"
        );
        Console.WriteLine(
            $"WebSocket: ws://0.0.0.0:{Port}"
        );
        Console.WriteLine();

        while (!_cancellation.Token.IsCancellationRequested)
        {
            try
            {
                var context =
                    await _listener.GetContextAsync();

                _ = Task.Run(
                    () => HandleRequestAsync(context)
                );
            }
            catch (HttpListenerException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
        }
    }

    private async Task HandleRequestAsync(
        HttpListenerContext context)
    {
        try
        {
            if (context.Request.IsWebSocketRequest)
        {
            string path =
                context.Request.Url?.AbsolutePath
                ?? "";

            const string prefix =
                "/ws/screen/";

            if (
                path.StartsWith(
                    prefix,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                string machineId =
                    Uri.UnescapeDataString(
                        path[prefix.Length..]
                    );

                await _screenStream.HandleAsync(
                    context,
                    machineId
                );

                return;
            }

            await _webSocket.HandleAsync(
                context
            );

            return;
        }

            await _http.HandleAsync(context);
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"❌ Error HTTP: {ex.Message}"
            );

            try
            {
                context.Response.StatusCode = 500;
                context.Response.Close();
            }
            catch
            {
            }
        }
    }

    private async Task SendCommandAsync(
        HttpListenerContext context,
        string machineId,
        string commandName)
    {
        if (!_agents.TryGetValue(
                machineId,
                out var agent))
        {
            context.Response.StatusCode = 404;

            await SendJsonAsync(
                context,
                new
                {
                    success = false,
                    message =
                        $"El equipo {machineId} no está registrado"
                }
            );

            return;
        }

        if (agent.Socket.State != WebSocketState.Open)
        {
            context.Response.StatusCode = 409;

            await SendJsonAsync(
                context,
                new
                {
                    success = false,
                    message =
                        $"El equipo {machineId} está desconectado"
                }
            );

            return;
        }

        if (commandName == "SCREEN_CAPTURE")
        {
            _screens.TryRemove(
                machineId,
                out _
            );
        }

        if (commandName == "PREVIEW_CAPTURE")
        {
            _previews.TryRemove(
                machineId,
                out _
            );
        }

        string commandId =
            $"{machineId}-{commandName}-" +
            $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";

        var command = new
        {
            type = "COMMAND",
            command = commandName,
            machineId,
            commandId,
            timestamp =
                DateTime.UtcNow.ToString("O")
        };

        await SendWebSocketJsonAsync(
            agent.Socket,
            command
        );

        Console.WriteLine(
            $"📤 COMANDO ENVIADO: {commandName} → {machineId}"
        );

        await SendJsonAsync(
            context,
            new
            {
                success = true,
                message =
                    commandName == "PING"
                        ? $"PING enviado a {machineId}"
                        : $"Captura solicitada a {machineId}",
                commandId
            }
        );
    }
    // ==========================================
    // ENVIAR CONTROL REMOTO
    // ==========================================

    private async Task SendRemoteInputAsync(
        HttpListenerContext context,
        string machineId,
        string action,
        double x,
        double y,
        string? button,
        int delta,
        string? key)
    {
        // ==========================================
        // COMPROBAR QUE EL EQUIPO EXISTE
        // ==========================================

        var registeredAgent =
            _database.GetAgents()
                .FirstOrDefault(
                    agent =>
                        agent.MachineId.Equals(
                            machineId,
                            StringComparison.OrdinalIgnoreCase
                        )
                );

        if (registeredAgent == null)
        {
            await SendJsonAsync(
                context,
                new
                {
                    success = false,
                    message =
                        "Equipo no encontrado."
                },
                404
            );

            return;
        }

        // ==========================================
        // COMPROBAR AUTORIZACIÓN
        // ==========================================

        if (!registeredAgent.Authorized)
        {
            await SendJsonAsync(
                context,
                new
                {
                    success = false,
                    message =
                        "El equipo no está autorizado."
                },
                403
            );

            return;
        }

        // ==========================================
        // COMPROBAR CONEXIÓN
        // ==========================================

        if (
            !_agents.TryGetValue(
                machineId,
                out var agent
            )
        )
        {
            await SendJsonAsync(
                context,
                new
                {
                    success = false,
                    message =
                        "El equipo no está conectado."
                },
                409
            );

            return;
        }

        if (
            agent.Socket.State !=
            WebSocketState.Open
        )
        {
            await SendJsonAsync(
                context,
                new
                {
                    success = false,
                    message =
                        "El equipo no está conectado."
                },
                409
            );

            return;
        }

        // ==========================================
        // VALIDAR ACCIÓN
        // ==========================================

        string[] allowedActions =
        {
            "MOUSE_MOVE",
            "MOUSE_DOWN",
            "MOUSE_UP",
            "MOUSE_CLICK",
            "MOUSE_DOUBLE_CLICK",
            "MOUSE_WHEEL",
            "KEY_DOWN",
            "KEY_UP"
        };

        if (
            !allowedActions.Contains(
                action,
                StringComparer.OrdinalIgnoreCase
            )
        )
        {
            await SendJsonAsync(
                context,
                new
                {
                    success = false,
                    message =
                        "Acción de control remoto no válida."
                },
                400
            );

            return;
        }

        // ==========================================
        // VALIDAR COORDENADAS
        // ==========================================

        if (
            action.StartsWith(
                "MOUSE_",
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            if (
                double.IsNaN(x) ||
                double.IsInfinity(x) ||
                double.IsNaN(y) ||
                double.IsInfinity(y)
            )
            {
                await SendJsonAsync(
                    context,
                    new
                    {
                        success = false,
                        message =
                            "Las coordenadas no son válidas."
                    },
                    400
                );

                return;
            }

            if (
                x < 0 ||
                y < 0
            )
            {
                await SendJsonAsync(
                    context,
                    new
                    {
                        success = false,
                        message =
                            "Las coordenadas no pueden ser negativas."
                    },
                    400
                );

                return;
            }
        }

        // ==========================================
        // CREAR EVENTO
        // ==========================================

        var remoteInput = new
        {
            type = "REMOTE_INPUT",

            machineId,

            action,

            x,

            y,

            button,

            delta,

            key,

            timestamp =
                DateTime.UtcNow.ToString("O")
        };

        // ==========================================
        // ENVIAR AL AGENT
        // ==========================================

        await SendWebSocketJsonAsync(
            agent.Socket,
            remoteInput
        );

        Console.WriteLine(
            $"🖱️ CONTROL REMOTO: " +
            $"{action} → {machineId}"
        );

        // ==========================================
        // RESPUESTA
        // ==========================================

        await SendJsonAsync(
            context,
            new
            {
                success = true,
                machineId,
                action
            }
        );
    }

    private async Task SendWebSocketJsonAsync(
        WebSocket socket,
        object data)
    {
        byte[] bytes =
            Encoding.UTF8.GetBytes(
                System.Text.Json.JsonSerializer.Serialize(data)
            );

        await _webSocketSendLock.WaitAsync();

        try
        {
            if (socket.State != WebSocketState.Open)
            {
                throw new WebSocketException(
                    "El WebSocket del Agent no está abierto."
                );
            }

            await socket.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                true,
                CancellationToken.None
            );
        }
        finally
        {
            _webSocketSendLock.Release();
        }
    }

    private static async Task SendJsonAsync(
        HttpListenerContext context,
        object data,
        int statusCode = 200)
    {
        string json =
            System.Text.Json.JsonSerializer.Serialize(
                data,
                new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true
                }
            );

        byte[] bytes =
            Encoding.UTF8.GetBytes(json);

        context.Response.StatusCode = statusCode;

        context.Response.ContentType =
            "application/json; charset=utf-8";

        context.Response.ContentLength64 =
            bytes.Length;

        await context.Response.OutputStream.WriteAsync(
            bytes
        );

        context.Response.Close();
    }

    private async Task DiscoveryLoopAsync(
        CancellationToken cancellationToken)
    {
        using UdpClient udp = new();

        try
        {
            udp.EnableBroadcast = true;

            udp.Client.SetSocketOption(
                SocketOptionLevel.Socket,
                SocketOptionName.ReuseAddress,
                true
            );

            udp.Client.Bind(
                new IPEndPoint(
                    IPAddress.Any,
                    DiscoveryPort
                )
            );

            Console.WriteLine(
                $"📡 Descubrimiento activo en UDP {DiscoveryPort}"
            );

            while (!cancellationToken.IsCancellationRequested)
            {
                UdpReceiveResult result =
                    await udp.ReceiveAsync(
                        cancellationToken
                    );

                string message =
                    Encoding.UTF8.GetString(
                        result.Buffer
                    );

                if (!message.Equals(
                        "CONTROLLAB_DISCOVER",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string? localIp =
                    GetLocalIPv4();

                if (string.IsNullOrWhiteSpace(localIp))
                {
                    Console.WriteLine(
                        "⚠️ No se pudo determinar la IP del Manager."
                    );

                    continue;
                }

                string response =
                    $"CONTROLLAB_MANAGER|{localIp}";

                byte[] responseBytes =
                    Encoding.UTF8.GetBytes(response);

                await udp.SendAsync(
                    responseBytes,
                    responseBytes.Length,
                    result.RemoteEndPoint
                );

                Console.WriteLine(
                    $"📡 Agent detectado desde " +
                    $"{result.RemoteEndPoint.Address}"
                );

                Console.WriteLine(
                    $"   Manager anunciado: {localIp}:{Port}"
                );
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"❌ Error en descubrimiento: {ex.Message}"
            );
        }
    }

    private static string? GetLocalIPv4()
    {
        try
        {
            var interfaces =
                System.Net.NetworkInformation
                    .NetworkInterface
                    .GetAllNetworkInterfaces();

            foreach (var networkInterface in interfaces)
            {
                if (networkInterface.OperationalStatus !=
                    System.Net.NetworkInformation
                        .OperationalStatus.Up)
                {
                    continue;
                }

                if (networkInterface.NetworkInterfaceType ==
                    System.Net.NetworkInformation
                        .NetworkInterfaceType.Loopback)
                {
                    continue;
                }

                var properties =
                    networkInterface.GetIPProperties();

                bool hasGateway =
                    properties.GatewayAddresses.Any(
                        gateway =>
                            gateway.Address.AddressFamily ==
                            AddressFamily.InterNetwork &&
                            !gateway.Address.Equals(
                                IPAddress.Any
                            )
                    );

                if (!hasGateway)
                    continue;

                foreach (
                    var address
                    in properties.UnicastAddresses)
                {
                    if (address.Address.AddressFamily !=
                        AddressFamily.InterNetwork)
                    {
                        continue;
                    }

                    string ip =
                        address.Address.ToString();

                    if (ip.StartsWith("169.254."))
                        continue;

                    return ip;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"❌ Error obteniendo IP del Manager: {ex.Message}"
            );
        }

        return null;
    }
private static string LoadAuthToken()
{
    string configPath =
        Path.Combine(
            AppContext.BaseDirectory,
            "config.json"
        );

    if (!File.Exists(configPath))
    {
        throw new FileNotFoundException(
            "No se encontró config.json del Manager.",
            configPath
        );
    }

    try
    {
        string json =
            File.ReadAllText(
                configPath
            );

        using JsonDocument document =
            JsonDocument.Parse(json);

        if (!document.RootElement.TryGetProperty(
                "AuthToken",
                out JsonElement tokenElement))
        {
            throw new InvalidOperationException(
                "AuthToken no está configurado en config.json."
            );
        }

        string token =
            tokenElement.GetString() ?? "";

        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException(
                "AuthToken está vacío en config.json."
            );
        }

        return token.Trim();
    }
    catch (JsonException ex)
    {
        throw new InvalidOperationException(
            "El config.json del Manager no contiene JSON válido.",
            ex
        );
    }
}
    public void Stop()
    {
        try
        {
            _cancellation?.Cancel();
        }
        catch
        {
        }

        try
        {
            if (_listener.IsListening)
                _listener.Stop();
        }
        catch
        {
        }

        try
        {
            _listener.Close();
        }
        catch
        {
        }

        _agents.DisposeConnections();
        _screens.Clear();

        try
        {
            _webSocketSendLock.Dispose();
        }
        catch
        {
        }
    }
}