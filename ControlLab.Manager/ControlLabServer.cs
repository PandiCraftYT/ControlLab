using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.IO;
using System.Net;
using System.Net.Sockets;
namespace ControlLab.Manager;

public sealed class ControlLabServer
{
    private readonly ConcurrentDictionary<string, AgentConnection> _agents = new();
    private readonly ConcurrentDictionary<string, ScreenCaptureData> _screens = new();

    private readonly HttpListener _listener = new();

    private CancellationTokenSource? _cancellation;

    private const int Port = 8080;
    private const int DiscoveryPort = 45678;
    public async Task StartAsync()
    {
        if (_listener.IsListening)
            return;

        _cancellation = new CancellationTokenSource();

        _listener.Prefixes.Add(
            $"http://+:{Port}/"
        );

        _listener.Start();
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
            "WebSocket: ws://0.0.0.0:8080"
        );

        Console.WriteLine();

        while (
            !_cancellation.Token.IsCancellationRequested
        )
        {
            try
            {
                var context =
                    await _listener.GetContextAsync();

                _ = Task.Run(
                    () => HandleRequestAsync(context)
                );
            }
            catch (
                HttpListenerException
            )
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
        }
    }

    // ==========================================
    // PROCESAR PETICIÓN
    // ==========================================

    private async Task HandleRequestAsync(
        HttpListenerContext context)
    {
        try
        {
            // ==========================================
            // WEBSOCKET
            // ==========================================

            if (
                context.Request.IsWebSocketRequest
            )
            {
                await HandleWebSocketAsync(
                    context
                );

                return;
            }

            string path =
                context.Request.Url?.AbsolutePath
                ?? "/";

            string method =
                context.Request.HttpMethod;

            // ==========================================
            // GET /
            // ==========================================

            if (
                method == "GET" &&
                path == "/"
            )
            {
                await SendTextAsync(
                    context,
                    "ControlLab Server funcionando correctamente."
                );

                return;
            }

            // ==========================================
            // GET /api/agents
            // ==========================================

            if (
                method == "GET" &&
                path == "/api/agents"
            )
            {
                var agents =
                    _agents.Values
                        .Select(agent => new
                        {
                            machineId =
                                agent.MachineId,

                            hostname =
                                agent.Hostname,

                            platform =
                                agent.Platform,

                            agentVersion =
                                agent.AgentVersion,

                            connectedAt =
                                agent.ConnectedAt,

                            lastHeartbeat =
                                agent.LastHeartbeat,

                            status =
                                agent.Socket.State ==
                                WebSocketState.Open
                                    ? "online"
                                    : "offline"
                        })
                        .ToList();

                await SendJsonAsync(
                    context,
                    new
                    {
                        total =
                            agents.Count,

                        agents
                    }
                );

                return;
            }

            // ==========================================
            // RUTAS DE EQUIPOS
            // ==========================================

            if (
                path.StartsWith(
                    "/api/agents/",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                string[] parts =
                    path.Split(
                        '/',
                        StringSplitOptions.RemoveEmptyEntries
                    );

                if (parts.Length >= 4)
                {
                    string machineId =
                        Uri.UnescapeDataString(
                            parts[2]
                        );

                    string action =
                        parts[3].ToLowerInvariant();

                    // ==========================================
                    // PING
                    // ==========================================

                    if (
                        method == "POST" &&
                        action == "ping"
                    )
                    {
                        await SendCommandAsync(
                            context,
                            machineId,
                            "PING"
                        );

                        return;
                    }

                    // ==========================================
                    // SOLICITAR CAPTURA
                    // ==========================================

                    if (
                        method == "POST" &&
                        action == "screen"
                    )
                    {
                        await SendCommandAsync(
                            context,
                            machineId,
                            "SCREEN_CAPTURE"
                        );

                        return;
                    }

                    // ==========================================
                    // DEVOLVER CAPTURA
                    // ==========================================

                    if (
                        method == "GET" &&
                        action == "screen"
                    )
                    {
                        await SendScreenAsync(
                            context,
                            machineId
                        );

                        return;
                    }
                }
            }

            context.Response.StatusCode =
                404;

            await SendTextAsync(
                context,
                "Not Found"
            );
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"❌ Error HTTP: {ex.Message}"
            );

            try
            {
                context.Response.StatusCode =
                    500;

                await SendJsonAsync(
                    context,
                    new
                    {
                        success = false,
                        message = ex.Message
                    }
                );
            }
            catch
            {
                // La conexión pudo haberse cerrado.
            }
        }
    }

    // ==========================================
    // ENVIAR COMANDO
    // ==========================================

    private async Task SendCommandAsync(
        HttpListenerContext context,
        string machineId,
        string commandName)
    {
        if (
            !_agents.TryGetValue(
                machineId,
                out var agent
            )
        )
        {
            context.Response.StatusCode =
                404;

            await SendJsonAsync(
                context,
                new
                {
                    success = false,
                    message =
                        $"El equipo {machineId} no está conectado"
                }
            );

            return;
        }

        if (
            agent.Socket.State !=
            WebSocketState.Open
        )
        {
            context.Response.StatusCode =
                409;

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

        if (
            commandName ==
            "SCREEN_CAPTURE"
        )
        {
            _screens.TryRemove(
                machineId,
                out _
            );
        }

        string commandId =
            $"{machineId}-{commandName}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";

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

        Console.WriteLine();
        Console.WriteLine(
            $"📤 COMANDO ENVIADO: {commandName}"
        );
        Console.WriteLine(
            $"   Equipo: {machineId}"
        );
        Console.WriteLine();

        await SendJsonAsync(
            context,
            new
            {
                success = true,

                message =
                    commandName ==
                    "PING"
                        ? $"PING enviado a {machineId}"
                        : $"Captura solicitada a {machineId}",

                commandId
            }
        );
    }

    // ==========================================
    // DEVOLVER IMAGEN
    // ==========================================

    private async Task SendScreenAsync(
        HttpListenerContext context,
        string machineId)
    {
        if (
            !_screens.TryGetValue(
                machineId,
                out var screen
            )
        )
        {
            context.Response.StatusCode =
                404;

            await SendJsonAsync(
                context,
                new
                {
                    success = false,
                    message =
                        $"No hay una captura disponible para {machineId}"
                }
            );

            return;
        }

        byte[] imageBytes =
            Convert.FromBase64String(
                screen.Image
            );

        context.Response.StatusCode =
            200;

        context.Response.ContentType =
            "image/jpeg";

        context.Response.ContentLength64 =
            imageBytes.Length;

        context.Response.Headers[
            "Cache-Control"
        ] =
            "no-store";

        await context.Response.OutputStream
            .WriteAsync(
                imageBytes
            );

        context.Response.Close();
    }

    // ==========================================
    // WEBSOCKET
    // ==========================================

    private async Task HandleWebSocketAsync(
        HttpListenerContext context)
    {
        HttpListenerWebSocketContext wsContext;

        try
        {
            wsContext =
                await context.AcceptWebSocketAsync(
                    null
                );
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"❌ Error WebSocket: {ex.Message}"
            );

            context.Response.StatusCode =
                500;

            context.Response.Close();

            return;
        }

        WebSocket socket =
            wsContext.WebSocket;

        Console.WriteLine(
            "🔌 Nueva conexión WebSocket"
        );

        AgentConnection? registeredAgent =
            null;

        try
        {
            while (
                socket.State ==
                WebSocketState.Open
            )
            {
                string? message =
                    await ReceiveWebSocketMessageAsync(
                        socket
                    );

                if (message == null)
                    break;

                await ProcessAgentMessageAsync(
                    socket,
                    message,
                    agent =>
                    {
                        registeredAgent =
                            agent;
                    }
                );
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"❌ WebSocket: {ex.Message}"
            );
        }
        finally
        {
            if (
                registeredAgent != null
            )
            {
                _agents.TryRemove(
                    registeredAgent.MachineId,
                    out _
                );

                Console.WriteLine(
                    $"🔴 Agente desconectado: {registeredAgent.MachineId}"
                );
            }

            try
            {
                socket.Dispose();
            }
            catch
            {
            }
        }
    }

    // ==========================================
    // PROCESAR MENSAJE DEL AGENTE
    // ==========================================

    private async Task ProcessAgentMessageAsync(
        WebSocket socket,
        string message,
        Action<AgentConnection> onRegistered)
    {
        try
        {
            using JsonDocument document =
                JsonDocument.Parse(message);

            JsonElement root =
                document.RootElement;

            string type =
                root.TryGetProperty(
                    "type",
                    out var typeProperty
                )
                    ? typeProperty.GetString()
                        ?? ""
                    : "";

            // ==========================================
            // REGISTRO
            // ==========================================

            if (
                type ==
                "AGENT_REGISTER"
            )
            {
                string machineId =
                    GetString(
                        root,
                        "machineId"
                    );

                string hostname =
                    GetString(
                        root,
                        "hostname"
                    );

                string platform =
                    GetString(
                        root,
                        "platform"
                    );

                string agentVersion =
                    GetString(
                        root,
                        "agentVersion"
                    );

                if (
                    string.IsNullOrWhiteSpace(
                        machineId
                    )
                )
                {
                    return;
                }

                if (
                    _agents.TryGetValue(
                        machineId,
                        out var previous
                    )
                )
                {
                    try
                    {
                        await previous.Socket.CloseAsync(
                            WebSocketCloseStatus
                                .NormalClosure,
                            "Nueva conexión",
                            CancellationToken.None
                        );
                    }
                    catch
                    {
                    }
                }

                var agent =
                    new AgentConnection
                    {
                        MachineId =
                            machineId,

                        Hostname =
                            hostname,

                        Platform =
                            platform,

                        AgentVersion =
                            agentVersion,

                        ConnectedAt =
                            DateTime.UtcNow
                                .ToString("O"),

                        LastHeartbeat =
                            DateTime.UtcNow
                                .ToString("O"),

                        Socket =
                            socket
                    };

                _agents[
                    machineId
                ] = agent;

                onRegistered(agent);

                Console.WriteLine();
                Console.WriteLine(
                    "🖥️ AGENTE REGISTRADO"
                );
                Console.WriteLine(
                    $"   ID:       {machineId}"
                );
                Console.WriteLine(
                    $"   Hostname: {hostname}"
                );
                Console.WriteLine(
                    $"   Sistema:  {platform}"
                );
                Console.WriteLine(
                    $"   Versión:  {agentVersion}"
                );
                Console.WriteLine(
                    $"   Total:    {_agents.Count}"
                );
                Console.WriteLine();

                await SendWebSocketJsonAsync(
                    socket,
                    new
                    {
                        type =
                            "REGISTER_ACCEPTED",

                        machineId,

                        message =
                            "Agente registrado correctamente"
                    }
                );

                return;
            }

            // ==========================================
            // HEARTBEAT
            // ==========================================

            if (
                type ==
                "HEARTBEAT"
            )
            {
                string machineId =
                    GetString(
                        root,
                        "machineId"
                    );

                if (
                    _agents.TryGetValue(
                        machineId,
                        out var agent
                    ) &&
                    agent.Socket == socket
                )
                {
                    agent.LastHeartbeat =
                        DateTime.UtcNow
                            .ToString("O");

                    await SendWebSocketJsonAsync(
                        socket,
                        new
                        {
                            type =
                                "HEARTBEAT_ACK",

                            timestamp =
                                agent.LastHeartbeat
                        }
                    );
                }

                return;
            }

            // ==========================================
            // RESULTADO DE PING
            // ==========================================

            if (
                type ==
                "COMMAND_RESULT"
            )
            {
                Console.WriteLine();
                Console.WriteLine(
                    "📥 RESULTADO DE COMANDO"
                );
                Console.WriteLine(
                    $"   Equipo:  {GetString(root, "machineId")}"
                );
                Console.WriteLine(
                    $"   Comando: {GetString(root, "command")}"
                );
                Console.WriteLine(
                    $"   Éxito:   {GetBool(root, "success")}"
                );
                Console.WriteLine(
                    $"   Mensaje: {GetString(root, "message")}"
                );
                Console.WriteLine();

                return;
            }

            // ==========================================
            // CAPTURA
            // ==========================================

            if (
                type ==
                "SCREEN_CAPTURE_RESULT"
            )
            {
                string machineId =
                    GetString(
                        root,
                        "machineId"
                    );

                string image =
                    GetString(
                        root,
                        "image"
                    );

                if (
                    string.IsNullOrWhiteSpace(
                        machineId
                    ) ||
                    string.IsNullOrWhiteSpace(
                        image
                    )
                )
                {
                    return;
                }

                if (
                    !_agents.TryGetValue(
                        machineId,
                        out var agent
                    ) ||
                    agent.Socket != socket
                )
                {
                    return;
                }

                _screens[
                    machineId
                ] =
                    new ScreenCaptureData
                    {
                        MachineId =
                            machineId,

                        Image =
                            image,

                        Timestamp =
                            GetString(
                                root,
                                "timestamp"
                            )
                    };

                Console.WriteLine();
                Console.WriteLine(
                    "📸 CAPTURA RECIBIDA"
                );
                Console.WriteLine(
                    $"   Equipo: {machineId}"
                );
                Console.WriteLine(
                    $"   Base64: {image.Length} caracteres"
                );
                Console.WriteLine();

                return;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"❌ Error procesando mensaje: {ex.Message}"
            );
        }
    }

    // ==========================================
    // RECIBIR MENSAJE COMPLETO
    // ==========================================

    private static async Task<string?> ReceiveWebSocketMessageAsync(
        WebSocket socket)
    {
        using var memory =
            new MemoryStream();

        byte[] buffer =
            new byte[8192];

        while (true)
        {
            WebSocketReceiveResult result =
                await socket.ReceiveAsync(
                    new ArraySegment<byte>(
                        buffer
                    ),
                    CancellationToken.None
                );

            if (
                result.MessageType ==
                WebSocketMessageType.Close
            )
            {
                return null;
            }

            if (
                result.MessageType !=
                WebSocketMessageType.Text
            )
            {
                continue;
            }

            await memory.WriteAsync(
                buffer.AsMemory(
                    0,
                    result.Count
                )
            );

            if (
                result.EndOfMessage
            )
            {
                break;
            }
        }

        return Encoding.UTF8.GetString(
            memory.ToArray()
        );
    }

    // ==========================================
    // ENVIAR JSON WEBSOCKET
    // ==========================================

    private static async Task SendWebSocketJsonAsync(
        WebSocket socket,
        object data)
    {
        string json =
            JsonSerializer.Serialize(data);

        byte[] bytes =
            Encoding.UTF8.GetBytes(json);

        await socket.SendAsync(
            new ArraySegment<byte>(
                bytes
            ),
            WebSocketMessageType.Text,
            true,
            CancellationToken.None
        );
    }

    // ==========================================
    // HTTP JSON
    // ==========================================

    private static async Task SendJsonAsync(
        HttpListenerContext context,
        object data)
    {
        string json =
            JsonSerializer.Serialize(
                data,
                new JsonSerializerOptions
                {
                    WriteIndented = true
                }
            );

        byte[] bytes =
            Encoding.UTF8.GetBytes(
                json
            );

        context.Response.StatusCode =
            200;

        context.Response.ContentType =
            "application/json; charset=utf-8";

        context.Response.ContentLength64 =
            bytes.Length;

        await context.Response.OutputStream
            .WriteAsync(bytes);

        context.Response.Close();
    }

    // ==========================================
    // HTTP TEXTO
    // ==========================================

    private static async Task SendTextAsync(
        HttpListenerContext context,
        string text)
    {
        byte[] bytes =
            Encoding.UTF8.GetBytes(
                text
            );

        context.Response.StatusCode =
            200;

        context.Response.ContentType =
            "text/plain; charset=utf-8";

        context.Response.ContentLength64 =
            bytes.Length;

        await context.Response.OutputStream
            .WriteAsync(bytes);

        context.Response.Close();
    }

    // ==========================================
    // HELPERS JSON
    // ==========================================

    private static string GetString(
        JsonElement root,
        string property)
    {
        return root.TryGetProperty(
            property,
            out var value
        )
            ? value.GetString() ?? ""
            : "";
    }

    private static bool GetBool(
        JsonElement root,
        string property)
    {
        return root.TryGetProperty(
            property,
            out var value
        ) &&
        value.ValueKind ==
            JsonValueKind.True;
    }
// ==========================================
// DESCUBRIMIENTO AUTOMÁTICO DEL MANAGER
// ==========================================

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

            if (
                !message.Equals(
                    "CONTROLLAB_DISCOVER",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                continue;
            }

            string? localIp =
                GetLocalIPv4();

            if (
                string.IsNullOrWhiteSpace(
                    localIp
                )
            )
            {
                Console.WriteLine(
                    "⚠️ No se pudo determinar la IP del Manager."
                );

                continue;
            }

            string response =
                $"CONTROLLAB_MANAGER|{localIp}";

            byte[] responseBytes =
                Encoding.UTF8.GetBytes(
                    response
                );

            await udp.SendAsync(
                responseBytes,
                responseBytes.Length,
                result.RemoteEndPoint
            );

            Console.WriteLine(
                $"📡 Agent detectado desde {result.RemoteEndPoint.Address}"
            );

            Console.WriteLine(
                $"   Manager anunciado: {localIp}:{Port}"
            );
        }
    }
    catch (OperationCanceledException)
    {
        // Servidor detenido.
    }
    catch (Exception ex)
    {
        Console.WriteLine(
            $"❌ Error en descubrimiento: {ex.Message}"
        );
    }
}

// ==========================================
// OBTENER IP LOCAL
// ==========================================

private static string? GetLocalIPv4()
{
    try
    {
        var interfaces =
            System.Net.NetworkInformation
                .NetworkInterface
                .GetAllNetworkInterfaces();

        foreach (
            var networkInterface
            in interfaces
        )
        {
            if (
                networkInterface.OperationalStatus !=
                System.Net.NetworkInformation
                    .OperationalStatus.Up
            )
            {
                continue;
            }

            if (
                networkInterface.NetworkInterfaceType ==
                System.Net.NetworkInformation
                    .NetworkInterfaceType.Loopback
            )
            {
                continue;
            }

            var properties =
                networkInterface.GetIPProperties();

            // ==========================================
            // BUSCAR INTERFAZ CON PUERTA DE ENLACE
            // ==========================================

            bool hasGateway =
                properties.GatewayAddresses
                    .Any(
                        gateway =>
                            gateway.Address.AddressFamily ==
                            AddressFamily.InterNetwork &&
                            !gateway.Address.Equals(
                                IPAddress.Any
                            )
                    );

            if (!hasGateway)
            {
                continue;
            }

            foreach (
                var address
                in properties.UnicastAddresses
            )
            {
                if (
                    address.Address.AddressFamily ==
                    AddressFamily.InterNetwork
                )
                {
                    string ip =
                        address.Address.ToString();

                    // Evitar interfaces virtuales
                    if (
                        ip.StartsWith("169.254.")
                    )
                    {
                        continue;
                    }

                    return ip;
                }
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
    // ==========================================
    // DETENER SERVIDOR
    // ==========================================

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

        foreach (
            var agent
            in _agents.Values
        )
        {
            try
            {
                agent.Socket.Dispose();
            }
            catch
            {
            }
        }

        _agents.Clear();
        _screens.Clear();
    }
}

// ==========================================
// AGENTE
// ==========================================

public sealed class AgentConnection
{
    public string MachineId { get; set; } =
        "";

    public string Hostname { get; set; } =
        "";

    public string Platform { get; set; } =
        "";

    public string AgentVersion { get; set; } =
        "";

    public string ConnectedAt { get; set; } =
        "";

    public string LastHeartbeat { get; set; } =
        "";

    public WebSocket Socket { get; set; } =
        null!;
}

// ==========================================
// CAPTURA
// ==========================================

public sealed class ScreenCaptureData
{
    public string MachineId { get; set; } =
        "";

    public string Image { get; set; } =
        "";

    public string Timestamp { get; set; } =
        "";
}