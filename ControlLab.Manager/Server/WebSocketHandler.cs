using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace ControlLab.Manager.Server;

public sealed class WebSocketHandler
{
    private const int BufferSize = 8192;
    private const int MaxMessageSize = 8 * 1024 * 1024;

    private readonly AgentRegistry _agents;
    private readonly Action<string, ScreenCaptureData> _saveScreen;
    private readonly Action<AgentConnection> _saveAgent;
    
    public WebSocketHandler(
        AgentRegistry agents,
        Action<string, ScreenCaptureData> saveScreen,
        Action<AgentConnection> saveAgent)
    {
        _agents = agents;
        _saveScreen = saveScreen;
        _saveAgent = saveAgent;
    }

    public async Task HandleAsync(
        HttpListenerContext context)
    {
        HttpListenerWebSocketContext wsContext;

        try
        {
            wsContext =
                await context.AcceptWebSocketAsync(null);
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"❌ Error WebSocket: {ex.Message}"
            );

            context.Response.StatusCode = 500;
            context.Response.Close();
            return;
        }

        WebSocket socket = wsContext.WebSocket;
        AgentConnection? registeredAgent = null;

        Console.WriteLine("🔌 Nueva conexión WebSocket");

        try
        {
            while (socket.State == WebSocketState.Open)
            {
                string? message =
                    await ReceiveMessageAsync(socket);

                if (message == null)
                    break;

                registeredAgent =
                    await ProcessMessageAsync(
                        socket,
                        message,
                        registeredAgent
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
            if (registeredAgent != null)
            {
                Console.WriteLine(
                    $"🔴 Agente desconectado: " +
                    $"{registeredAgent.MachineId}"
                );

                Console.WriteLine(
                    "   El equipo permanecerá registrado como OFFLINE."
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

    private async Task<AgentConnection?> ProcessMessageAsync(
        WebSocket socket,
        string message,
        AgentConnection? registeredAgent)
    {
        try
        {
            using JsonDocument document =
                JsonDocument.Parse(message);

            JsonElement root =
                document.RootElement;

            string type =
                GetString(root, "type");

            switch (type)
            {
                case "AGENT_REGISTER":
                    return await RegisterAsync(
                        socket,
                        root
                    );

                case "HEARTBEAT":
                    await HeartbeatAsync(
                        socket,
                        root
                    );
                    return registeredAgent;

                case "COMMAND_RESULT":
                    LogCommandResult(root);
                    return registeredAgent;

                case "SCREEN_CAPTURE_RESULT":
                    SaveScreen(
                        socket,
                        root
                    );
                    return registeredAgent;

                default:
                    return registeredAgent;
            }
        }
        catch (JsonException)
        {
            Console.WriteLine(
                "⚠️ Mensaje WebSocket inválido."
            );

            return registeredAgent;
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"❌ Error procesando mensaje: {ex.Message}"
            );

            return registeredAgent;
        }
    }

    private async Task<AgentConnection?> RegisterAsync(
        WebSocket socket,
        JsonElement root)
    {
        string machineId =
            GetString(root, "machineId");

        if (string.IsNullOrWhiteSpace(machineId))
            return null;

        if (_agents.TryGetValue(
                machineId,
                out var previous))
        {
            try
            {
                if (previous.Socket.State ==
                    WebSocketState.Open)
                {
                    await previous.Socket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Nueva conexión",
                        CancellationToken.None
                    );
                }
            }
            catch
            {
            }
        }

        string now =
            DateTime.UtcNow.ToString("O");

        var agent =
            new AgentConnection
            {
                MachineId = machineId,
                Hostname =
                    GetString(root, "hostname"),
                Platform =
                    GetString(root, "platform"),
                AgentVersion =
                    GetString(root, "agentVersion"),
                ConnectedAt = now,
                LastHeartbeat = now,
                Socket = socket
            };

        _agents.Register(agent);
        _saveAgent(agent);
        Console.WriteLine(
            $"🖥️ Agente registrado: {machineId} " +
            $"({agent.Hostname})"
        );

        await SendJsonAsync(
            socket,
            new
            {
                type = "REGISTER_ACCEPTED",
                machineId,
                message =
                    "Agente registrado correctamente"
            }
        );

        return agent;
    }

    private async Task HeartbeatAsync(
        WebSocket socket,
        JsonElement root)
    {
        string machineId =
            GetString(root, "machineId");

        if (!_agents.TryGetValue(
                machineId,
                out var agent) ||
            agent.Socket != socket)
        {
            return;
        }

        agent.LastHeartbeat =
            DateTime.UtcNow.ToString("O");

        await SendJsonAsync(
            socket,
            new
            {
                type = "HEARTBEAT_ACK",
                timestamp =
                    agent.LastHeartbeat
            }
        );
    }

    private static void LogCommandResult(
        JsonElement root)
    {
        Console.WriteLine(
            $"📥 Comando → " +
            $"{GetString(root, "machineId")} | " +
            $"{GetString(root, "command")} | " +
            $"{GetBool(root, "success")}"
        );
    }

    private void SaveScreen(
        WebSocket socket,
        JsonElement root)
    {
        string machineId =
            GetString(root, "machineId");

        string image =
            GetString(root, "image");

        if (string.IsNullOrWhiteSpace(machineId) ||
            string.IsNullOrWhiteSpace(image))
        {
            return;
        }

        if (!_agents.TryGetValue(
                machineId,
                out var agent) ||
            agent.Socket != socket)
        {
            return;
        }

        try
        {
            byte[] imageBytes =
                Convert.FromBase64String(image);

            if (imageBytes.Length == 0)
                return;

            _saveScreen(
                machineId,
                new ScreenCaptureData
                {
                    MachineId = machineId,
                    Image = image,
                    Timestamp =
                        GetString(
                            root,
                            "timestamp"
                        )
                }
            );

            Console.WriteLine(
                $"📸 Captura recibida: {machineId} " +
                $"({imageBytes.Length / 1024} KB)"
            );
        }
        catch (FormatException)
        {
            Console.WriteLine(
                $"⚠️ Captura Base64 inválida: {machineId}"
            );
        }
    }

    private static async Task<string?> ReceiveMessageAsync(
        WebSocket socket)
    {
        using var memory =
            new MemoryStream();

        byte[] buffer =
            new byte[BufferSize];

        int totalBytes = 0;

        while (true)
        {
            WebSocketReceiveResult result =
                await socket.ReceiveAsync(
                    new ArraySegment<byte>(buffer),
                    CancellationToken.None
                );

            if (result.MessageType ==
                WebSocketMessageType.Close)
            {
                return null;
            }

            if (result.MessageType !=
                WebSocketMessageType.Text)
            {
                continue;
            }

            totalBytes += result.Count;

            if (totalBytes > MaxMessageSize)
            {
                Console.WriteLine(
                    "⚠️ Mensaje WebSocket demasiado grande."
                );

                try
                {
                    await socket.CloseAsync(
                        WebSocketCloseStatus.MessageTooBig,
                        "Mensaje demasiado grande",
                        CancellationToken.None
                    );
                }
                catch
                {
                }

                return null;
            }

            await memory.WriteAsync(
                buffer.AsMemory(
                    0,
                    result.Count
                )
            );

            if (result.EndOfMessage)
            {
                return Encoding.UTF8.GetString(
                    memory.ToArray()
                );
            }
        }
    }

    private static Task SendJsonAsync(
        WebSocket socket,
        object data)
    {
        byte[] bytes =
            Encoding.UTF8.GetBytes(
                JsonSerializer.Serialize(data)
            );

        return socket.SendAsync(
            new ArraySegment<byte>(bytes),
            WebSocketMessageType.Text,
            true,
            CancellationToken.None
        );
    }

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
}