using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;

using ControlLab.Manager.Server;

namespace ControlLab.Manager;

public sealed class ControlLabServer
{
    private const int Port = 8080;
    private const int DiscoveryPort = 45678;

    private readonly AgentRegistry _agents = new();
    private readonly ControlLab.Manager.Data.ControlLabDatabase _database = new();
    private readonly ConcurrentDictionary<string, ScreenCaptureData> _screens = new();
    private readonly HttpListener _listener = new();

    private readonly WebSocketHandler _webSocket;
    private readonly HttpApiHandler _http;

    private CancellationTokenSource? _cancellation;

    public ControlLabServer()
    {
        _webSocket = new WebSocketHandler(
            _agents,
            (machineId, screen) =>
                _screens[machineId] = screen,
            agent =>
                _database.RegisterAgent(
                    agent.MachineId,
                    agent.Hostname,
                    agent.Platform,
                    agent.AgentVersion
                )
        );

        _http = new HttpApiHandler(
            () => _agents.Values,
            machineId =>
                _screens.TryGetValue(
                    machineId,
                    out var screen
                )
                    ? screen
                    : null,
            SendCommandAsync
        );
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
                await _webSocket.HandleAsync(context);
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

    private static async Task SendWebSocketJsonAsync(
        WebSocket socket,
        object data)
    {
        byte[] bytes =
            Encoding.UTF8.GetBytes(
                System.Text.Json.JsonSerializer.Serialize(data)
            );

        await socket.SendAsync(
            new ArraySegment<byte>(bytes),
            WebSocketMessageType.Text,
            true,
            CancellationToken.None
        );
    }

    private static async Task SendJsonAsync(
        HttpListenerContext context,
        object data)
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

        context.Response.StatusCode = 200;
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
    }
}