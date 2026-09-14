using ControlLab.Agent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

const string AGENT_VERSION = "1.0.0";

string configPath = Path.Combine(
    AppContext.BaseDirectory,
    "config.json"
);

// ==========================================
// LEER / CREAR IDENTIFICADOR DEL EQUIPO
// ==========================================

string machineId;
string authToken;

try
{
    machineId =
        await GetOrCreateMachineIdAsync(
            configPath
        );
}
catch (Exception ex)
{
    Console.WriteLine(
        $"❌ Error obteniendo MachineId: {ex.Message}"
    );

    return;
}

// ==========================================
// OBTENER AUTH TOKEN
// ==========================================

try
{
    authToken =
        await GetAuthTokenAsync(
            configPath
        );
}
catch (Exception ex)
{
    Console.WriteLine(
        $"❌ Error obteniendo AuthToken: {ex.Message}"
    );

    return;
}

if (string.IsNullOrWhiteSpace(machineId))
{
    Console.WriteLine(
        "❌ No se pudo obtener un MachineId válido."
    );

    return;
}

// ==========================================
// INFORMACIÓN DEL AGENTE
// ==========================================

Console.WriteLine("=================================");
Console.WriteLine("       ControlLab Agent");
Console.WriteLine("=================================");
Console.WriteLine($"Equipo:   {machineId}");
Console.WriteLine($"Versión:  {AGENT_VERSION}");
Console.WriteLine();

// ==========================================
// CONEXIÓN PRINCIPAL
// ==========================================

while (true)
{
    // ==========================================
    // BUSCAR MANAGER AUTOMÁTICAMENTE
    // ==========================================

    string? managerAddress =
        await ManagerDiscovery.FindManagerAsync();

    if (string.IsNullOrWhiteSpace(managerAddress))
    {
        Console.WriteLine(
            "❌ No se encontró ControlLab Manager."
        );

        Console.WriteLine(
            "🔄 Reintentando en 5 segundos..."
        );

        await Task.Delay(5000);

        continue;
    }

    // ==========================================
    // CREAR DIRECCIÓN DEL SERVIDOR
    // ==========================================

    string serverUrl =
        $"ws://{managerAddress}:8080";

    Console.WriteLine(
        $"🟢 Manager encontrado: {managerAddress}"
    );

    using var socket = new ClientWebSocket();

    try
    {
        // ==========================================
        // CONECTAR AL SERVIDOR
        // ==========================================

        Console.WriteLine(
            "🔌 Conectando al servidor..."
        );

        await socket.ConnectAsync(
            new Uri(serverUrl),
            CancellationToken.None
        );

        Console.WriteLine(
            "🟢 Conectado al servidor"
        );

        // ==========================================
        // REGISTRO
        // ==========================================

        var registration = new
        {
            type = "AGENT_REGISTER",
            machineId = machineId,
            hostname = Environment.MachineName,
            platform = Environment.OSVersion.Platform.ToString(),
            agentVersion = AGENT_VERSION,
            authToken = authToken
        };

        await SendMessageAsync(
            socket,
            registration
        );

        Console.WriteLine(
            "📡 Registro enviado"
        );

        // ==========================================
        // PRUEBA LOCAL DE CAPTURA
        // ==========================================

        try
        {
            string captura =
                ScreenCapture.CaptureScreen();

            Console.WriteLine(
                $"🖥️ Captura creada: {captura}"
            );
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"❌ Error capturando pantalla: {ex.Message}"
            );
        }

        // ==========================================
        // TAREAS DEL AGENTE
        // ==========================================

        var receiveTask =
            ReceiveMessagesAsync(socket);

        var heartbeatTask =
            SendHeartbeatAsync(
                socket,
                machineId
            );

        await Task.WhenAny(
            receiveTask,
            heartbeatTask
        );

        Console.WriteLine(
            "🔴 Conexión finalizada"
        );
    }
    catch (Exception ex)
    {
        Console.WriteLine(
            $"❌ Error: {ex.Message}"
        );
    }

    // ==========================================
    // RECONEXIÓN
    // ==========================================

    Console.WriteLine(
        "🔄 Intentando reconectar en 5 segundos..."
    );

    await Task.Delay(5000);
}

// ==========================================
// OBTENER / CREAR MACHINE ID
// ==========================================

static async Task<string> GetOrCreateMachineIdAsync(
    string configPath)
{
    AgentConfig? config = null;

    // ==========================================
    // LEER CONFIGURACIÓN EXISTENTE
    // ==========================================

    if (File.Exists(configPath))
    {
        try
        {
            string configJson =
                await File.ReadAllTextAsync(
                    configPath
                );

            config =
                JsonSerializer.Deserialize<AgentConfig>(
                    configJson,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    }
                );
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"⚠️ Error leyendo config.json: {ex.Message}"
            );
        }
    }

    // ==========================================
    // SI YA EXISTE UN ID, CONSERVARLO
    // ==========================================

    if (
        config != null &&
        !string.IsNullOrWhiteSpace(
            config.MachineId
        )
    )
    {
        return config.MachineId.Trim();
    }

    // ==========================================
    // GENERAR NUEVO ID
    // ==========================================

    string newMachineId =
        $"PC-{Guid.NewGuid():N}".ToUpperInvariant();

    var newConfig = new AgentConfig
    {
        MachineId = newMachineId
    };

    string json =
        JsonSerializer.Serialize(
            newConfig,
            new JsonSerializerOptions
            {
                WriteIndented = true
            }
        );

    await File.WriteAllTextAsync(
        configPath,
        json
    );

    Console.WriteLine(
        $"🆔 Nuevo MachineId generado: {newMachineId}"
    );

    Console.WriteLine(
        $"💾 MachineId guardado en: {configPath}"
    );

    return newMachineId;
}
// ==========================================
// OBTENER AUTH TOKEN
// ==========================================

static async Task<string> GetAuthTokenAsync(
    string configPath)
{
    if (!File.Exists(configPath))
    {
        return "";
    }

    try
    {
        string json =
            await File.ReadAllTextAsync(
                configPath
            );

        var config =
            JsonSerializer.Deserialize<AgentConfig>(
                json,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }
            );

        return config?.AuthToken ?? "";
    }
    catch (Exception ex)
    {
        Console.WriteLine(
            $"⚠️ Error leyendo AuthToken: {ex.Message}"
        );

        return "";
    }
}
// ==========================================
// ENVIAR MENSAJE
// ==========================================

static async Task SendMessageAsync(
    ClientWebSocket socket,
    object message)
{
    string json =
        JsonSerializer.Serialize(message);

    byte[] bytes =
        Encoding.UTF8.GetBytes(json);

    await socket.SendAsync(
        new ArraySegment<byte>(bytes),
        WebSocketMessageType.Text,
        true,
        CancellationToken.None
    );
}

// ==========================================
// HEARTBEAT
// ==========================================

static async Task SendHeartbeatAsync(
    ClientWebSocket socket,
    string machineId)
{
    while (
        socket.State ==
        WebSocketState.Open
    )
    {
        await Task.Delay(10000);

        if (
            socket.State !=
            WebSocketState.Open
        )
        {
            break;
        }

        var heartbeat = new
        {
            type = "HEARTBEAT",
            machineId = machineId
        };

        await SendMessageAsync(
            socket,
            heartbeat
        );

        Console.WriteLine(
            "❤️ Heartbeat enviado"
        );
    }
}

// ==========================================
// RECIBIR MENSAJES DEL SERVIDOR
// ==========================================

static async Task ReceiveMessagesAsync(
    ClientWebSocket socket)
{
    byte[] buffer = new byte[4096];

    while (
        socket.State ==
        WebSocketState.Open
    )
    {
        try
        {
            var result =
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
                break;
            }

            string message =
                Encoding.UTF8.GetString(
                    buffer,
                    0,
                    result.Count
                );

            Console.WriteLine(
                $"📨 Servidor: {message}"
            );

            // ==========================================
            // PROCESAR COMANDOS
            // ==========================================

            try
            {
                var command =
                    JsonSerializer.Deserialize<ServerCommand>(
                        message,
                        new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        }
                    );

                if (
                    command?.Type == "COMMAND" &&
                    command.MachineId.Equals(
                        GetMachineId(),
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    // ==========================================
                    // PING
                    // ==========================================

                    if (command.Command == "PING")
                    {
                        Console.WriteLine(
                            "🏓 Comando PING recibido"
                        );

                        var response = new
                        {
                            type = "COMMAND_RESULT",
                            machineId =
                                command.MachineId,
                            command = "PING",
                            success = true,
                            message = "PONG",
                            timestamp =
                                DateTime.UtcNow.ToString("O")
                        };

                        await SendMessageAsync(
                            socket,
                            response
                        );

                        Console.WriteLine(
                            "🏓 PONG enviado"
                        );
                    }

                    // ==========================================
                    // CAPTURA DE PANTALLA
                    // ==========================================

                    else if (
                        command.Command ==
                        "SCREEN_CAPTURE"
                    )
                    {
                        Console.WriteLine(
                            "🖥️ Comando SCREEN_CAPTURE recibido"
                        );

                        try
                        {
                            string filePath =
                                ScreenCapture.CaptureScreen();

                            byte[] imageBytes =
                                await File.ReadAllBytesAsync(
                                    filePath
                                );

                            string base64Image =
                                Convert.ToBase64String(
                                    imageBytes
                                );

                            var response = new
                            {
                                type =
                                    "SCREEN_CAPTURE_RESULT",

                                machineId =
                                    command.MachineId,

                                command =
                                    "SCREEN_CAPTURE",

                                success = true,

                                image =
                                    base64Image,

                                timestamp =
                                    DateTime.UtcNow.ToString("O")
                            };

                            await SendMessageAsync(
                                socket,
                                response
                            );

                            Console.WriteLine(
                                "📸 Captura enviada al servidor"
                            );
                        }
                        catch (Exception ex)
                        {
                            var response = new
                            {
                                type =
                                    "SCREEN_CAPTURE_RESULT",

                                machineId =
                                    command.MachineId,

                                command =
                                    "SCREEN_CAPTURE",

                                success = false,

                                message =
                                    ex.Message,

                                timestamp =
                                    DateTime.UtcNow.ToString("O")
                            };

                            await SendMessageAsync(
                                socket,
                                response
                            );

                            Console.WriteLine(
                                $"❌ Error capturando pantalla: {ex.Message}"
                            );
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"❌ Error procesando comando: {ex.Message}"
                );
            }
        }
        catch (WebSocketException ex)
        {
            Console.WriteLine(
                $"❌ WebSocket: {ex.Message}"
            );

            break;
        }
    }
}

// ==========================================
// OBTENER MACHINE ID
// ==========================================

static string GetMachineId()
{
    string configPath =
        Path.Combine(
            AppContext.BaseDirectory,
            "config.json"
        );

    if (!File.Exists(configPath))
        return "";

    try
    {
        string json =
            File.ReadAllText(configPath);

        var config =
            JsonSerializer.Deserialize<AgentConfig>(
                json,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }
            );

        return config?.MachineId ?? "";
    }
    catch
    {
        return "";
    }
}

// ==========================================
// CONFIGURACIÓN
// ==========================================

public class AgentConfig
{
    public string MachineId { get; set; } = "";

    public string AuthToken { get; set; } = "";
}

// ==========================================
// COMANDO DEL SERVIDOR
// ==========================================

public class ServerCommand
{
    public string Type { get; set; } = "";

    public string Command { get; set; } = "";

    public string MachineId { get; set; } = "";
}