using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using ControlLab.Manager.Data;

namespace ControlLab.Manager.Server;

public sealed class HttpApiHandler
{
    private readonly Func<IEnumerable<AgentConnection>> _getAgents;
    private readonly ControlLabDatabase _database;
    private readonly Func<string, ScreenCaptureData?> _getScreen;
    private readonly Func<string, ScreenCaptureData?> _getPreview;
    private readonly Func<string, ScreenCaptureData?> _getStreamFrame;
    private readonly Func<HttpListenerContext, string, string, Task> _sendCommand;

    public HttpApiHandler(
        Func<IEnumerable<AgentConnection>> getAgents,
        Func<string, ScreenCaptureData?> getScreen,
        Func<string, ScreenCaptureData?> getPreview,
        Func<string, ScreenCaptureData?> getStreamFrame,
        Func<HttpListenerContext, string, string, Task> sendCommand,
        ControlLabDatabase database)
    {
        _getAgents = getAgents;
        _getScreen = getScreen;
        _getPreview = getPreview;
        _sendCommand = sendCommand;
        _database = database;
        _getStreamFrame = getStreamFrame;
    }

    public async Task HandleAsync(HttpListenerContext context)
    {
        try
        {
            string path =
                context.Request.Url?.AbsolutePath ?? "/";

            string method =
                context.Request.HttpMethod;

            // ==========================================
            // SERVIDOR
            // ==========================================

            if (method == "GET" && path == "/")
            {
                await SendTextAsync(
                    context,
                    "ControlLab Server funcionando correctamente."
                );

                return;
            }

            // ==========================================
            // LISTAR EQUIPOS
            // ==========================================

            if (method == "GET" && path == "/api/agents")
            {
                await SendAgentsAsync(context);

                return;
            }

            // ==========================================
            // VALIDAR RUTA
            // ==========================================

            if (!path.StartsWith(
                    "/api/agents/",
                    StringComparison.OrdinalIgnoreCase))
            {
                await SendErrorAsync(
                    context,
                    404,
                    "Not Found"
                );

                return;
            }

            string[] parts =
                path.Split(
                    '/',
                    StringSplitOptions.RemoveEmptyEntries
                );

            if (parts.Length < 4)
            {
                await SendErrorAsync(
                    context,
                    404,
                    "Ruta no válida."
                );

                return;
            }

            string machineId =
                Uri.UnescapeDataString(parts[2]);

            string action =
                parts[3].ToLowerInvariant();
            
            // ==========================================
            // REINICIAR PC
            // ==========================================

            if (
                method == "POST" &&
                action == "restart"
            )
            {
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
                    await SendErrorAsync(
                        context,
                        404,
                        "Equipo no encontrado."
                    );
                    return;
                }

                if (!registeredAgent.Authorized)
                {
                    await SendErrorAsync(
                        context,
                        403,
                        "El equipo no está autorizado."
                    );
                    return;
                }

                bool online =
                    _getAgents()
                        .Any(
                            agent =>
                                agent.MachineId.Equals(
                                    machineId,
                                    StringComparison.OrdinalIgnoreCase
                                ) &&
                                agent.Socket.State ==
                                    WebSocketState.Open
                        );

                if (!online)
                {
                    await SendErrorAsync(
                        context,
                        409,
                        "El equipo no está conectado."
                    );
                    return;
                }

                await _sendCommand(
                    context,
                    machineId,
                    "RESTART_PC"
                );

                return;
            }
            // ==========================================
            // BLOQUEAR SESIÓN
            // ==========================================
            if (
                method == "POST" &&
                action == "lock"
            )
            {
                // ==========================================
                // COMPROBAR AUTORIZACIÓN
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
                    await SendErrorAsync(
                        context,
                        404,
                        "Equipo no encontrado."
                    );

                    return;
                }

                if (!registeredAgent.Authorized)
                {
                    await SendErrorAsync(
                        context,
                        403,
                        "El equipo no está autorizado."
                    );

                    return;
                }

                // ==========================================
                // COMPROBAR CONEXIÓN
                // ==========================================

                bool online =
                    _getAgents()
                        .Any(
                            agent =>
                                agent.MachineId.Equals(
                                    machineId,
                                    StringComparison.OrdinalIgnoreCase
                                ) &&
                                agent.Socket.State ==
                                    WebSocketState.Open
                        );

                if (!online)
                {
                    await SendErrorAsync(
                        context,
                        409,
                        "El equipo no está conectado."
                    );

                    return;
                }

                // ==========================================
                // ENVIAR COMANDO
                // ==========================================

                await _sendCommand(
                    context,
                    machineId,
                    "LOCK_SESSION"
                );

                return;
            }
            if (
                method == "POST" &&
                action == "shutdown"
            )
            {
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
                    await SendErrorAsync(
                        context,
                        404,
                        "Equipo no encontrado."
                    );
                    return;
                }

                if (!registeredAgent.Authorized)
                {
                    await SendErrorAsync(
                        context,
                        403,
                        "El equipo no está autorizado."
                    );
                    return;
                }

                bool online =
                    _getAgents()
                        .Any(
                            agent =>
                                agent.MachineId.Equals(
                                    machineId,
                                    StringComparison.OrdinalIgnoreCase
                                ) &&
                                agent.Socket.State ==
                                    WebSocketState.Open
                        );

                if (!online)
                {
                    await SendErrorAsync(
                        context,
                        409,
                        "El equipo no está conectado."
                    );
                    return;
                }

                await _sendCommand(
                    context,
                    machineId,
                    "SHUTDOWN_PC"
                );

                return;
            }
            // ==========================================
            // AUTORIZAR EQUIPO
            // ==========================================

            if (
                method == "POST" &&
                action == "authorize"
            )
            {
                bool success =
                    _database.AuthorizeAgent(
                        machineId
                    );

                if (!success)
                {
                    await SendErrorAsync(
                        context,
                        404,
                        "Equipo no encontrado."
                    );

                    return;
                }

                await SendJsonAsync(
                    context,
                    new
                    {
                        success = true,
                        machineId,
                        authorized = true
                    }
                );

                return;
            }

            // ==========================================
            // REVOCAR AUTORIZACIÓN
            // ==========================================

            if (
                method == "POST" &&
                action == "revoke"
            )
            {
                bool success =
                    _database.RevokeAgent(
                        machineId
                    );

                if (!success)
                {
                    await SendErrorAsync(
                        context,
                        404,
                        "Equipo no encontrado."
                    );

                    return;
                }

                await SendJsonAsync(
                    context,
                    new
                    {
                        success = true,
                        machineId,
                        authorized = false
                    }
                );

                return;
            }

            // ==========================================
            // CAMBIAR NOMBRE DEL EQUIPO
            // ==========================================

            if (
                method == "POST" &&
                action == "rename"
            )
            {
                await RenameAgentAsync(
                    context,
                    machineId
                );

                return;
            }

            // ==========================================
            // PING / CAPTURA
            // ==========================================

            if (
                method == "POST" &&
                (action == "ping" || action == "screen")
            )
            {
                string command =
                    action == "ping"
                        ? "PING"
                        : "SCREEN_CAPTURE";

                await _sendCommand(
                    context,
                    machineId,
                    command
                );

                return;
            }

            // ==========================================
            // SOLICITAR PREVIEW
            // ==========================================

            if (
                method == "POST" &&
                action == "preview"
            )
            {
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
                    await SendErrorAsync(
                        context,
                        404,
                        "Equipo no encontrado."
                    );

                    return;
                }

                if (!registeredAgent.Authorized)
                {
                    await SendErrorAsync(
                        context,
                        403,
                        "El equipo no está autorizado."
                    );

                    return;
                }

                bool online =
                    _getAgents()
                        .Any(
                            agent =>
                                agent.MachineId.Equals(
                                    machineId,
                                    StringComparison.OrdinalIgnoreCase
                                ) &&
                                agent.Socket.State ==
                                    WebSocketState.Open
                        );

                if (!online)
                {
                    await SendErrorAsync(
                        context,
                        409,
                        "El equipo no está conectado."
                    );

                    return;
                }

                await _sendCommand(
                    context,
                    machineId,
                    "PREVIEW_CAPTURE"
                );

                return;
            }
            // ==========================================
            // INICIAR TRANSMISIÓN DE PANTALLA
            // ==========================================

            if (
                method == "POST" &&
                action == "stream"
            )
            {
                string? mode =
                    context.Request.QueryString["mode"];

                if (
                    string.Equals(
                        mode,
                        "start",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
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
                        await SendErrorAsync(
                            context,
                            404,
                            "Equipo no encontrado."
                        );

                        return;
                    }

                    if (!registeredAgent.Authorized)
                    {
                        await SendErrorAsync(
                            context,
                            403,
                            "El equipo no está autorizado."
                        );

                        return;
                    }

                    bool online =
                        _getAgents()
                            .Any(
                                agent =>
                                    agent.MachineId.Equals(
                                        machineId,
                                        StringComparison.OrdinalIgnoreCase
                                    ) &&
                                    agent.Socket.State ==
                                        WebSocketState.Open
                            );

                    if (!online)
                    {
                        await SendErrorAsync(
                            context,
                            409,
                            "El equipo no está conectado."
                        );

                        return;
                    }

                    await _sendCommand(
                        context,
                        machineId,
                        "START_SCREEN_STREAM"
                    );

                    return;
                }

                // ==========================================
                // DETENER TRANSMISIÓN
                // ==========================================

                if (
                    string.Equals(
                        mode,
                        "stop",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    await _sendCommand(
                        context,
                        machineId,
                        "STOP_SCREEN_STREAM"
                    );

                    return;
                }
            }
            // ==========================================
            // OBTENER FRAME DE TRANSMISIÓN
            // ==========================================

            if (
                method == "GET" &&
                action == "stream"
            )
            {
                await SendStreamFrameAsync(
                    context,
                    machineId
                );

                return;
            }
            // ==========================================
            // OBTENER CAPTURA
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
            // ==========================================
            // OBTENER PREVIEW
            // ==========================================

            if (
                method == "GET" &&
                action == "preview"
            )
            {
                await SendPreviewAsync(
                    context,
                    machineId
                );

                return;
            }

            // ==========================================
            // ACCIÓN NO ENCONTRADA
            // ==========================================

            await SendErrorAsync(
                context,
                404,
                "Acción no encontrada."
            );
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"❌ Error HTTP: {ex.Message}"
            );

            try
            {
                await SendErrorAsync(
                    context,
                    500,
                    ex.Message
                );
            }
            catch
            {
            }
        }
    }

    // ==========================================
    // CAMBIAR NOMBRE
    // ==========================================

    private async Task RenameAgentAsync(
        HttpListenerContext context,
        string machineId)
    {
        // ==========================================
        // VALIDAR LONGITUD DEL CONTENIDO
        // ==========================================

        if (
            context.Request.ContentLength64 > 4096
        )
        {
            await SendErrorAsync(
                context,
                413,
                "La solicitud es demasiado grande."
            );

            return;
        }

        // ==========================================
        // LEER JSON
        // ==========================================

        RenameAgentRequest? request;

        try
        {
            request =
                await JsonSerializer.DeserializeAsync<RenameAgentRequest>(
                    context.Request.InputStream,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    }
                );
        }
        catch (JsonException)
        {
            await SendErrorAsync(
                context,
                400,
                "El JSON enviado no es válido."
            );

            return;
        }

        if (request == null)
        {
            await SendErrorAsync(
                context,
                400,
                "No se recibió información."
            );

            return;
        }

        // ==========================================
        // VALIDAR NOMBRE
        // ==========================================

        string displayName =
            request.DisplayName?.Trim() ?? "";

        if (displayName.Length == 0)
        {
            await SendErrorAsync(
                context,
                400,
                "El nombre del equipo no puede estar vacío."
            );

            return;
        }

        if (displayName.Length > 50)
        {
            await SendErrorAsync(
                context,
                400,
                "El nombre del equipo no puede superar los 50 caracteres."
            );

            return;
        }

        // ==========================================
        // ACTUALIZAR BASE DE DATOS
        // ==========================================

        bool success =
            _database.RenameAgent(
                machineId,
                displayName
            );

        if (!success)
        {
            await SendErrorAsync(
                context,
                409,
                "No se pudo cambiar el nombre. " +
                "El equipo puede no existir o el nombre ya estar en uso."
            );

            return;
        }

        // ==========================================
        // RESPUESTA
        // ==========================================

        await SendJsonAsync(
            context,
            new
            {
                success = true,
                machineId,
                displayName
            }
        );
    }

    // ==========================================
    // OBTENER EQUIPOS
    // ==========================================

    private async Task SendAgentsAsync(
        HttpListenerContext context)
    {
        // Equipos guardados permanentemente en SQLite

        var registeredAgents =
            _database.GetAgents();

        // Equipos conectados actualmente

        var connectedAgents =
            _getAgents()
                .ToDictionary(
                    agent => agent.MachineId,
                    StringComparer.OrdinalIgnoreCase
                );

        var agents =
            registeredAgents
                .Select(registered =>
                {
                    // ==========================================
                    // EQUIPO CONECTADO
                    // ==========================================

                    if (
                        connectedAgents.TryGetValue(
                            registered.MachineId,
                            out var onlineAgent
                        )
                    )
                    {
                        return new
                        {
                            machineId =
                                onlineAgent.MachineId,

                            displayName =
                                registered.DisplayName,

                            hostname =
                                onlineAgent.Hostname,

                            platform =
                                onlineAgent.Platform,

                            agentVersion =
                                onlineAgent.AgentVersion,

                            connectedAt =
                                onlineAgent.ConnectedAt,

                            lastHeartbeat =
                                onlineAgent.LastHeartbeat,

                            status =
                                "online",

                            authorized =
                                registered.Authorized
                        };
                    }

                    // ==========================================
                    // EQUIPO DESCONECTADO
                    // ==========================================

                    return new
                    {
                        machineId =
                            registered.MachineId,

                        displayName =
                            registered.DisplayName,

                        hostname =
                            registered.Hostname,

                        platform =
                            registered.Platform,

                        agentVersion =
                            registered.AgentVersion,

                        connectedAt =
                            registered.FirstSeen,

                        lastHeartbeat =
                            registered.LastSeen,

                        status =
                            "offline",

                        authorized =
                            registered.Authorized
                    };
                })
                .OrderBy(
                    agent =>
                        agent.displayName,
                    StringComparer.OrdinalIgnoreCase
                )
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
    }

    // ==========================================
    // OBTENER CAPTURA
    // ==========================================

    private async Task SendScreenAsync(
        HttpListenerContext context,
        string machineId)
    {
        ScreenCaptureData? screen =
            _getScreen(machineId);

        if (screen == null)
        {
            await SendErrorAsync(
                context,
                404,
                $"No hay una captura disponible para {machineId}"
            );

            return;
        }

        byte[] imageBytes;

        try
        {
            imageBytes =
                Convert.FromBase64String(
                    screen.Image
                );
        }
        catch
        {
            await SendErrorAsync(
                context,
                500,
                "La captura recibida no es válida."
            );

            return;
        }

        context.Response.StatusCode =
            200;

        context.Response.ContentType =
            "image/jpeg";

        context.Response.ContentLength64 =
            imageBytes.Length;

        context.Response.Headers["Cache-Control"] =
            "no-store";

        await context.Response.OutputStream
            .WriteAsync(imageBytes);

        context.Response.Close();
    }
    // ==========================================
    // ENVIAR FRAME DE TRANSMISIÓN
    // ==========================================

    private async Task SendStreamFrameAsync(
        HttpListenerContext context,
        string machineId)
    {
        var frame =
            _getStreamFrame(
                machineId
            );

        if (frame == null)
        {
            await SendErrorAsync(
                context,
                404,
                "Frame de transmisión no disponible."
            );

            return;
        }

        try
        {
            byte[] imageBytes =
                Convert.FromBase64String(
                    frame.Image
                );

            context.Response.StatusCode = 200;

            context.Response.ContentType =
                "image/jpeg";

            context.Response.ContentLength64 =
                imageBytes.Length;

            context.Response.Headers[
                "Cache-Control"
            ] =
                "no-store, no-cache, must-revalidate";

            context.Response.Headers[
                "Pragma"
            ] =
                "no-cache";

            await context.Response.OutputStream.WriteAsync(
                imageBytes
            );

            context.Response.OutputStream.Close();
        }
        catch (FormatException)
        {
            await SendErrorAsync(
                context,
                500,
                "El frame recibido no es un JPEG válido."
            );
        }
    }
    // ==========================================
    // OBTENER PREVIEW
    // ==========================================

    private async Task SendPreviewAsync(
        HttpListenerContext context,
        string machineId)
    {
        ScreenCaptureData? preview =
            _getPreview(machineId);

        if (preview == null)
        {
            await SendErrorAsync(
                context,
                404,
                $"No hay un preview disponible para {machineId}"
            );

            return;
        }

        byte[] imageBytes;

        try
        {
            imageBytes =
                Convert.FromBase64String(
                    preview.Image
                );
        }
        catch
        {
            await SendErrorAsync(
                context,
                500,
                "El preview recibido no es válido."
            );

            return;
        }

        context.Response.StatusCode =
            200;

        context.Response.ContentType =
            "image/jpeg";

        context.Response.ContentLength64 =
            imageBytes.Length;

        context.Response.Headers["Cache-Control"] =
            "no-store";

        await context.Response.OutputStream
            .WriteAsync(imageBytes);

        context.Response.Close();
    }
    // ==========================================
    // RESPUESTA JSON
    // ==========================================

    internal static async Task SendJsonAsync(
        HttpListenerContext context,
        object data,
        int statusCode = 200)
    {
        byte[] bytes =
            Encoding.UTF8.GetBytes(
                JsonSerializer.Serialize(data)
            );

        context.Response.StatusCode =
            statusCode;

        context.Response.ContentType =
            "application/json; charset=utf-8";

        context.Response.ContentLength64 =
            bytes.Length;

        await context.Response.OutputStream
            .WriteAsync(bytes);

        context.Response.Close();
    }

    // ==========================================
    // RESPUESTA TEXTO
    // ==========================================

    internal static async Task SendTextAsync(
        HttpListenerContext context,
        string text,
        int statusCode = 200)
    {
        byte[] bytes =
            Encoding.UTF8.GetBytes(text);

        context.Response.StatusCode =
            statusCode;

        context.Response.ContentType =
            "text/plain; charset=utf-8";

        context.Response.ContentLength64 =
            bytes.Length;

        await context.Response.OutputStream
            .WriteAsync(bytes);

        context.Response.Close();
    }

    // ==========================================
    // ERROR
    // ==========================================

    private static Task SendErrorAsync(
        HttpListenerContext context,
        int statusCode,
        string message)
    {
        return SendJsonAsync(
            context,
            new
            {
                success = false,
                message
            },
            statusCode
        );
    }
}

// ==========================================
// MODELO PARA CAMBIAR NOMBRE
// ==========================================

public sealed class RenameAgentRequest
{
    public string? DisplayName { get; set; }
}