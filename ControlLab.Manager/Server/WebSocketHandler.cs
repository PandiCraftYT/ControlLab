using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace ControlLab.Manager.Server;

public sealed class WebSocketHandler
{
    private const int BufferSize = 8192;

    private const int MaxMessageSize =
        8 * 1024 * 1024;

    // =========================================================
    // AUTENTICACIÓN
    // =========================================================

    private readonly string _expectedAuthToken;

    private readonly Func<string, bool>
        _isAgentAuthorized;

    // =========================================================
    // AGENTES
    // =========================================================

    private readonly AgentRegistry _agents;

    // =========================================================
    // ALMACENAMIENTO DE CAPTURAS
    // =========================================================

    private readonly Action<
        string,
        ScreenCaptureData
    > _saveScreen;

    private readonly Action<
        string,
        ScreenCaptureData
    > _savePreview;

    // =========================================================
    // STREAMING
    // =========================================================

    private readonly ConcurrentDictionary<
        string,
        ScreenCaptureData
    > _streamFrames =
        new();

    // =========================================================
    // BASE DE DATOS
    // =========================================================

    private readonly Action<AgentConnection>
        _saveAgent;

    // =========================================================
    // CONSTRUCTOR
    // =========================================================

    public WebSocketHandler(
        AgentRegistry agents,
        Action<string, ScreenCaptureData> saveScreen,
        Action<string, ScreenCaptureData> savePreview,
        Action<AgentConnection> saveAgent,
        string expectedAuthToken,
        Func<string, bool> isAgentAuthorized)
    {
        _agents =
            agents;

        _saveScreen =
            saveScreen;

        _savePreview =
            savePreview;

        _saveAgent =
            saveAgent;

        _expectedAuthToken =
            expectedAuthToken;

        _isAgentAuthorized =
            isAgentAuthorized;
    }

    // =========================================================
    // OBTENER ÚLTIMO FRAME DEL STREAM
    // =========================================================

    public ScreenCaptureData? GetStreamFrame(
        string machineId)
    {
        return _streamFrames.TryGetValue(
            machineId,
            out var frame
        )
            ? frame
            : null;
    }

    // =========================================================
    // ELIMINAR FRAME DEL STREAM
    // =========================================================

    public void RemoveStreamFrame(
        string machineId)
    {
        _streamFrames.TryRemove(
            machineId,
            out _
        );
    }

    // =========================================================
    // CONEXIÓN WEBSOCKET
    // =========================================================

    public async Task HandleAsync(
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

            context.Response.StatusCode = 500;
            context.Response.Close();

            return;
        }

        WebSocket socket =
            wsContext.WebSocket;

        AgentConnection? registeredAgent =
            null;

        Console.WriteLine(
            "🔌 Nueva conexión WebSocket"
        );

        try
        {
            while (
                socket.State ==
                WebSocketState.Open
            )
            {
                string? message =
                    await ReceiveMessageAsync(
                        socket
                    );

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
        catch (WebSocketException ex)
        {
            Console.WriteLine(
                $"⚠️ WebSocket cerrado: {ex.Message}"
            );
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

                if (
                    _agents.TryGetValue(
                        registeredAgent.MachineId,
                        out var currentAgent
                    ) &&
                    ReferenceEquals(
                        currentAgent,
                        registeredAgent
                    )
                )
                {
                    _agents.TryRemove(
                        registeredAgent.MachineId,
                        out _
                    );
                }

                RemoveStreamFrame(
                    registeredAgent.MachineId
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

    // =========================================================
    // PROCESAR MENSAJES
    // =========================================================

    private async Task<AgentConnection?>
        ProcessMessageAsync(
            WebSocket socket,
            string message,
            AgentConnection? registeredAgent)
    {
        try
        {
            using JsonDocument document =
                JsonDocument.Parse(
                    message
                );

            JsonElement root =
                document.RootElement;

            string type =
                GetString(
                    root,
                    "type"
                );

            switch (type)
            {
                // =================================================
                // REGISTRO
                // =================================================

                case "AGENT_REGISTER":

                    return await RegisterAsync(
                        socket,
                        root
                    );

                // =================================================
                // HEARTBEAT
                // =================================================

                case "HEARTBEAT":

                    if (registeredAgent == null)
                    {
                        await RejectAgentAsync(
                            socket,
                            "Agente no registrado"
                        );

                        return null;
                    }

                    await HeartbeatAsync(
                        socket,
                        root
                    );

                    return registeredAgent;

                // =================================================
                // RESULTADO DE COMANDO
                // =================================================

                case "COMMAND_RESULT":

                    if (registeredAgent == null)
                        return null;

                    LogCommandResult(
                        root
                    );

                    return registeredAgent;

                // =================================================
                // CAPTURA / PREVIEW
                // =================================================

                case "SCREEN_CAPTURE_RESULT":

                    if (registeredAgent == null)
                        return null;

                    string captureCommand =
                        GetString(
                            root,
                            "command"
                        );

                    // ---------------------------------------------
                    // PREVIEW
                    // ---------------------------------------------

                    if (
                        captureCommand.Equals(
                            "PREVIEW_CAPTURE",
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        SavePreview(
                            socket,
                            root
                        );
                    }

                    // ---------------------------------------------
                    // CAPTURA COMPLETA
                    // ---------------------------------------------

                    else
                    {
                        SaveScreen(
                            socket,
                            root
                        );
                    }

                    return registeredAgent;

                // =================================================
                // FRAME DE TRANSMISIÓN
                // =================================================

                case "SCREEN_STREAM_FRAME":

                    if (registeredAgent == null)
                        return null;

                    SaveStreamFrame(
                        socket,
                        root
                    );

                    return registeredAgent;

                // =================================================
                // DESCONOCIDO
                // =================================================

                default:

                    Console.WriteLine(
                        $"⚠️ Mensaje desconocido: {type}"
                    );

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

    // =========================================================
    // REGISTRO DEL AGENTE
    // =========================================================

    private async Task<AgentConnection?>
        RegisterAsync(
            WebSocket socket,
            JsonElement root)
    {
        string machineId =
            GetString(
                root,
                "machineId"
            );

        // =====================================================
        // VALIDAR MACHINE ID
        // =====================================================

        if (
            string.IsNullOrWhiteSpace(
                machineId
            )
        )
        {
            Console.WriteLine(
                "⚠️ Intento de registro sin MachineId."
            );

            await RejectAgentAsync(
                socket,
                "MachineId requerido"
            );

            return null;
        }

        // =====================================================
        // VALIDAR TOKEN
        // =====================================================

        string authToken =
            GetString(
                root,
                "authToken"
            );

        if (
            !string.Equals(
                authToken,
                _expectedAuthToken,
                StringComparison.Ordinal
            )
        )
        {
            Console.WriteLine(
                $"🔐 Agent rechazado por token inválido: " +
                $"{machineId}"
            );

            await RejectAgentAsync(
                socket,
                "Token de autenticación inválido"
            );

            return null;
        }

        // =====================================================
        // CREAR INFORMACIÓN DEL AGENT
        // =====================================================

        string now =
            DateTime.UtcNow.ToString(
                "O"
            );

        var agent =
            new AgentConnection
            {
                MachineId =
                    machineId,

                Hostname =
                    GetString(
                        root,
                        "hostname"
                    ),

                Platform =
                    GetString(
                        root,
                        "platform"
                    ),

                AgentVersion =
                    GetString(
                        root,
                        "agentVersion"
                    ),

                ConnectedAt =
                    now,

                LastHeartbeat =
                    now,

                Socket =
                    socket
            };

        // =====================================================
        // GUARDAR / ACTUALIZAR SQLITE
        // =====================================================

        _saveAgent(
            agent
        );

        // =====================================================
        // COMPROBAR AUTORIZACIÓN
        // =====================================================

        bool authorized =
            _isAgentAuthorized(
                machineId
            );

        if (!authorized)
        {
            Console.WriteLine(
                $"🔒 Agent pendiente de autorización: " +
                $"{machineId} ({agent.Hostname})"
            );

            await SendJsonAsync(
                socket,
                new
                {
                    type =
                        "REGISTRATION_PENDING",

                    machineId =
                        machineId,

                    message =
                        "El equipo está pendiente de autorización por el administrador."
                }
            );

            await RejectAgentAsync(
                socket,
                "Agente pendiente de autorización"
            );

            return null;
        }

        // =====================================================
        // CERRAR CONEXIÓN ANTERIOR
        // =====================================================

        if (
            _agents.TryGetValue(
                machineId,
                out var previous
            )
        )
        {
            try
            {
                if (
                    previous.Socket.State ==
                    WebSocketState.Open
                )
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

        // =====================================================
        // REGISTRAR EN MEMORIA
        // =====================================================

        _agents.Register(
            agent
        );

        Console.WriteLine(
            $"🟢 Agente autenticado y autorizado: " +
            $"{machineId} ({agent.Hostname})"
        );

        // =====================================================
        // CONFIRMACIÓN
        // =====================================================

        await SendJsonAsync(
            socket,
            new
            {
                type =
                    "REGISTER_ACCEPTED",

                machineId =
                    machineId,

                message =
                    "Agente autenticado, autorizado y registrado correctamente"
            }
        );

        return agent;
    }

    // =========================================================
    // RECHAZAR AGENTE
    // =========================================================

    private static async Task RejectAgentAsync(
        WebSocket socket,
        string reason)
    {
        try
        {
            if (
                socket.State ==
                WebSocketState.Open
            )
            {
                await socket.CloseAsync(
                    WebSocketCloseStatus.PolicyViolation,
                    reason,
                    CancellationToken.None
                );
            }
        }
        catch
        {
        }
    }

    // =========================================================
    // HEARTBEAT
    // =========================================================

    private async Task HeartbeatAsync(
        WebSocket socket,
        JsonElement root)
    {
        string machineId =
            GetString(
                root,
                "machineId"
            );

        // =====================================================
        // COMPROBAR AGENTE
        // =====================================================

        if (
            !_agents.TryGetValue(
                machineId,
                out var agent
            )
        )
        {
            return;
        }

        // =====================================================
        // COMPROBAR SOCKET
        // =====================================================

        if (agent.Socket != socket)
        {
            Console.WriteLine(
                $"⚠️ Heartbeat rechazado: " +
                $"socket no autorizado para {machineId}"
            );

            return;
        }

        // =====================================================
        // VOLVER A COMPROBAR AUTORIZACIÓN
        // =====================================================

        if (
            !_isAgentAuthorized(
                machineId
            )
        )
        {
            Console.WriteLine(
                $"🔒 Autorización revocada: " +
                $"{machineId}"
            );

            await RejectAgentAsync(
                socket,
                "Autorización revocada"
            );

            return;
        }

        // =====================================================
        // ACTUALIZAR HEARTBEAT
        // =====================================================

        agent.LastHeartbeat =
            DateTime.UtcNow.ToString(
                "O"
            );

        await SendJsonAsync(
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

    // =========================================================
    // RESULTADO DE COMANDO
    // =========================================================

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

    // =========================================================
    // GUARDAR CAPTURA COMPLETA
    // =========================================================

    private void SaveScreen(
        WebSocket socket,
        JsonElement root)
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

        // =====================================================
        // COMPROBAR SOCKET
        // =====================================================

        if (
            !_agents.TryGetValue(
                machineId,
                out var agent
            ) ||
            agent.Socket != socket
        )
        {
            Console.WriteLine(
                $"⚠️ Captura rechazada: " +
                $"conexión no autorizada para {machineId}"
            );

            return;
        }

        // =====================================================
        // COMPROBAR AUTORIZACIÓN
        // =====================================================

        if (
            !_isAgentAuthorized(
                machineId
            )
        )
        {
            Console.WriteLine(
                $"🔒 Captura rechazada: " +
                $"Agent no autorizado {machineId}"
            );

            return;
        }

        try
        {
            byte[] imageBytes =
                Convert.FromBase64String(
                    image
                );

            if (imageBytes.Length == 0)
                return;

            // =================================================
            // LÍMITE DE TAMAÑO
            // =================================================

            if (
                imageBytes.Length >
                MaxMessageSize
            )
            {
                Console.WriteLine(
                    $"⚠️ Captura demasiado grande: " +
                    $"{machineId}"
                );

                return;
            }

            // =================================================
            // GUARDAR
            // =================================================

            _saveScreen(
                machineId,
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
                }
            );

            Console.WriteLine(
                $"📸 Captura recibida: " +
                $"{machineId} " +
                $"({imageBytes.Length / 1024} KB)"
            );
        }
        catch (FormatException)
        {
            Console.WriteLine(
                $"⚠️ Captura Base64 inválida: " +
                $"{machineId}"
            );
        }
    }

    // =========================================================
    // GUARDAR PREVIEW
    // =========================================================

    private void SavePreview(
        WebSocket socket,
        JsonElement root)
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

        // =====================================================
        // COMPROBAR SOCKET
        // =====================================================

        if (
            !_agents.TryGetValue(
                machineId,
                out var agent
            ) ||
            agent.Socket != socket
        )
        {
            Console.WriteLine(
                $"⚠️ Preview rechazado: " +
                $"conexión no autorizada para {machineId}"
            );

            return;
        }

        // =====================================================
        // COMPROBAR AUTORIZACIÓN
        // =====================================================

        if (
            !_isAgentAuthorized(
                machineId
            )
        )
        {
            Console.WriteLine(
                $"🔒 Preview rechazado: " +
                $"Agent no autorizado {machineId}"
            );

            return;
        }

        try
        {
            byte[] imageBytes =
                Convert.FromBase64String(
                    image
                );

            if (imageBytes.Length == 0)
                return;

            // =================================================
            // LÍMITE DE TAMAÑO
            // =================================================

            if (
                imageBytes.Length >
                MaxMessageSize
            )
            {
                Console.WriteLine(
                    $"⚠️ Preview demasiado grande: " +
                    $"{machineId}"
                );

                return;
            }

            // =================================================
            // GUARDAR PREVIEW
            // =================================================

            _savePreview(
                machineId,
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
                }
            );

            Console.WriteLine(
                $"🖼️ Preview recibido: " +
                $"{machineId} " +
                $"({imageBytes.Length / 1024} KB)"
            );
        }
        catch (FormatException)
        {
            Console.WriteLine(
                $"⚠️ Preview Base64 inválido: " +
                $"{machineId}"
            );
        }
    }

    // =========================================================
    // GUARDAR FRAME DE TRANSMISIÓN
    // =========================================================

    private void SaveStreamFrame(
        WebSocket socket,
        JsonElement root)
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

        // =====================================================
        // COMPROBAR SOCKET
        // =====================================================

        if (
            !_agents.TryGetValue(
                machineId,
                out var agent
            ) ||
            agent.Socket != socket
        )
        {
            Console.WriteLine(
                $"⚠️ Frame rechazado: " +
                $"conexión no autorizada para {machineId}"
            );

            return;
        }

        // =====================================================
        // COMPROBAR AUTORIZACIÓN
        // =====================================================

        if (
            !_isAgentAuthorized(
                machineId
            )
        )
        {
            Console.WriteLine(
                $"🔒 Frame rechazado: " +
                $"Agent no autorizado {machineId}"
            );

            return;
        }

        try
        {
            byte[] imageBytes =
                Convert.FromBase64String(
                    image
                );

            if (imageBytes.Length == 0)
                return;

            // =================================================
            // LÍMITE DE TAMAÑO
            // =================================================

            if (
                imageBytes.Length >
                MaxMessageSize
            )
            {
                Console.WriteLine(
                    $"⚠️ Frame demasiado grande: " +
                    $"{machineId}"
                );

                return;
            }

            // =================================================
            // GUARDAR ÚLTIMO FRAME
            // =================================================

            _streamFrames[machineId] =
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

            Console.WriteLine(
                $"📡 Frame recibido: " +
                $"{machineId} " +
                $"({imageBytes.Length / 1024} KB)"
            );
        }
        catch (FormatException)
        {
            Console.WriteLine(
                $"⚠️ Frame Base64 inválido: " +
                $"{machineId}"
            );
        }
    }

    // =========================================================
    // RECIBIR MENSAJE
    // =========================================================

    private static async Task<string?>
        ReceiveMessageAsync(
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
                    new ArraySegment<byte>(
                        buffer
                    ),
                    CancellationToken.None
                );

            // =================================================
            // CIERRE
            // =================================================

            if (
                result.MessageType ==
                WebSocketMessageType.Close
            )
            {
                return null;
            }

            // =================================================
            // SOLO TEXTO
            // =================================================

            if (
                result.MessageType !=
                WebSocketMessageType.Text
            )
            {
                continue;
            }

            totalBytes +=
                result.Count;

            // =================================================
            // PROTECCIÓN DE TAMAÑO
            // =================================================

            if (
                totalBytes >
                MaxMessageSize
            )
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

            // =================================================
            // GUARDAR FRAGMENTO
            // =================================================

            await memory.WriteAsync(
                buffer.AsMemory(
                    0,
                    result.Count
                )
            );

            // =================================================
            // MENSAJE COMPLETO
            // =================================================

            if (
                result.EndOfMessage
            )
            {
                return Encoding.UTF8.GetString(
                    memory.ToArray()
                );
            }
        }
    }

    // =========================================================
    // ENVIAR JSON
    // =========================================================

    private static Task SendJsonAsync(
        WebSocket socket,
        object data)
    {
        byte[] bytes =
            Encoding.UTF8.GetBytes(
                JsonSerializer.Serialize(
                    data
                )
            );

        return socket.SendAsync(
            new ArraySegment<byte>(
                bytes
            ),
            WebSocketMessageType.Text,
            true,
            CancellationToken.None
        );
    }

    // =========================================================
    // OBTENER STRING
    // =========================================================

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

    // =========================================================
    // OBTENER BOOL
    // =========================================================

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