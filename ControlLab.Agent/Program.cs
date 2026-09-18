using ControlLab.Agent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Windows.Forms;
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

if (string.IsNullOrWhiteSpace(authToken))
{
    Console.WriteLine(
        "❌ No se encontró un AuthToken válido."
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
// RECONEXIÓN
// ==========================================

int reconnectDelaySeconds = 2;

while (true)
{
    ClientWebSocket? socket = null;

    CancellationTokenSource?
        connectionCancellation = null;

    try
    {
        // ==========================================
        // BUSCAR MANAGER AUTOMÁTICAMENTE
        // ==========================================

        Console.WriteLine(
            "📡 Buscando ControlLab Manager..."
        );

        string? managerAddress =
            await ManagerDiscovery.FindManagerAsync();

        if (
            string.IsNullOrWhiteSpace(
                managerAddress
            )
        )
        {
            Console.WriteLine(
                "❌ No se encontró ControlLab Manager."
            );

            reconnectDelaySeconds =
            await WaitBeforeReconnectAsync(
                reconnectDelaySeconds
            );

            continue;
        }

        Console.WriteLine(
            $"🟢 Manager encontrado: {managerAddress}"
        );

        // ==========================================
        // CREAR DIRECCIÓN DEL SERVIDOR
        // ==========================================

        string serverUrl =
            $"ws://{managerAddress}:8080";

        socket =
            new ClientWebSocket();

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
        // REINICIAR BACKOFF
        // ==========================================

        reconnectDelaySeconds = 2;

        // ==========================================
        // TOKEN DE CANCELACIÓN DE LA CONEXIÓN
        // ==========================================

        connectionCancellation =
            new CancellationTokenSource();

        CancellationToken connectionToken =
            connectionCancellation.Token;

        // ==========================================
        // REGISTRO
        // ==========================================

        var registration = new
        {
            type = "AGENT_REGISTER",

            machineId = machineId,

            hostname =
                Environment.MachineName,

            platform =
                Environment.OSVersion.Platform.ToString(),

            agentVersion =
                AGENT_VERSION,

            authToken =
                authToken
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

        Task receiveTask =
            ReceiveMessagesAsync(
                socket,
                connectionToken
            );

        Task heartbeatTask =
            SendHeartbeatAsync(
                socket,
                machineId,
                connectionToken
            );

        Task screenPreviewTask =
            SendScreenPreviewAsync(
                socket,
                machineId,
                connectionToken
            );

        // ==========================================
        // ESPERAR A QUE UNA TAREA TERMINE
        // ==========================================

        await Task.WhenAny(
            receiveTask,
            heartbeatTask,
            screenPreviewTask
        );

        Console.WriteLine(
            "🔴 Una tarea de conexión finalizó."
        );

        // ==========================================
        // CANCELAR TODAS LAS TAREAS
        // ==========================================

        try
        {
            connectionCancellation.Cancel();
        }
        catch
        {
        }

        // ==========================================
        // CERRAR SOCKET
        // ==========================================

        try
        {
            if (
                socket.State ==
                WebSocketState.Open
            )
            {
                await socket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "Reconectando",
                    CancellationToken.None
                );
            }
        }
        catch
        {
        }

        // ==========================================
        // ESPERAR TAREAS
        // ==========================================

        try
        {
            await Task.WhenAll(
                receiveTask,
                heartbeatTask,
                screenPreviewTask
            );
        }
        catch
        {
            // Las tareas pueden finalizar
            // debido a la cancelación.
        }
    }
    catch (OperationCanceledException)
    {
        Console.WriteLine(
            "🛑 Conexión cancelada."
        );
    }
    catch (WebSocketException ex)
    {
        Console.WriteLine(
            $"⚠️ WebSocket desconectado: {ex.Message}"
        );
    }
    catch (Exception ex)
    {
        Console.WriteLine(
            $"❌ Error de conexión: {ex.Message}"
        );
    }
    finally
    {
        // ==========================================
        // CANCELAR CONEXIÓN
        // ==========================================

        try
        {
            connectionCancellation?.Cancel();
        }
        catch
        {
        }

        connectionCancellation?.Dispose();

        // ==========================================
        // CERRAR SOCKET
        // ==========================================

        try
        {
            socket?.Dispose();
        }
        catch
        {
        }
    }

    // ==========================================
    // RECONEXIÓN
    // ==========================================

    Console.WriteLine(
        $"🔄 Reconectando en {reconnectDelaySeconds} segundos..."
    );

    await Task.Delay(
        TimeSpan.FromSeconds(
            reconnectDelaySeconds
        )
    );

    // ==========================================
    // BACKOFF PROGRESIVO
    // ==========================================

    reconnectDelaySeconds =
        Math.Min(
            reconnectDelaySeconds * 2,
            30
        );
}

// =========================================================
// ESPERAR ANTES DE RECONEXIÓN
// =========================================================

static async Task<int> WaitBeforeReconnectAsync(
    int delaySeconds)
{
    Console.WriteLine(
        $"🔄 Reintentando en {delaySeconds} segundos..."
    );

    await Task.Delay(
        TimeSpan.FromSeconds(
            delaySeconds
        )
    );

    return Math.Min(
        delaySeconds * 2,
        30
    );
}

// =========================================================
// OBTENER / CREAR MACHINE ID
// =========================================================

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

    var newConfig =
        new AgentConfig
        {
            MachineId =
                newMachineId,

            AuthToken =
                config?.AuthToken ?? ""
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

// =========================================================
// OBTENER AUTH TOKEN
// =========================================================

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

// =========================================================
// TRANSMISIÓN DE PANTALLA
// =========================================================

static async Task StartScreenStreamAsync(
    ClientWebSocket socket,
    string machineId,
    CancellationToken cancellationToken)
{
    Console.WriteLine(
        $"📡 Iniciando transmisión para {machineId}"
    );

    try
    {
        while (
            !cancellationToken.IsCancellationRequested &&
            socket.State == WebSocketState.Open
        )
        {
            try
            {
                // ==========================================
                // CAPTURAR PANTALLA
                // ==========================================

                byte[] imageBytes =
                    ScreenCapture.CaptureStreamFrame();

                // ==========================================
                // ENVIAR FRAME JPEG COMO BINARIO
                // ==========================================

                await SendBinaryMessageAsync(
                    socket,
                    imageBytes
                );

                // ==========================================
                // 10 FPS APROX.
                // ==========================================

                await Task.Delay(
                    100,
                    cancellationToken
                );
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (WebSocketException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"❌ Error en frame de transmisión: {ex.Message}"
                );

                try
                {
                    await Task.Delay(
                        250,
                        cancellationToken
                    );
                }
                catch
                {
                    break;
                }
            }
        }
    }
    catch (OperationCanceledException)
    {
    }
    catch (WebSocketException)
    {
    }
    catch (Exception ex)
    {
        Console.WriteLine(
            $"❌ Error en transmisión: {ex.Message}"
        );
    }

    Console.WriteLine(
        $"🔴 Transmisión finalizada para {machineId}"
    );
}

// =========================================================
// ENVIAR MENSAJE DE TEXTO
// =========================================================

static async Task SendMessageAsync(
    ClientWebSocket socket,
    object message)
{
    await SendLockHolder.Lock.WaitAsync();

    try
    {
        if (
            socket.State !=
            WebSocketState.Open
        )
        {
            return;
        }

        string json =
            JsonSerializer.Serialize(
                message
            );

        byte[] bytes =
            Encoding.UTF8.GetBytes(
                json
            );

        await socket.SendAsync(
            new ArraySegment<byte>(
                bytes
            ),
            WebSocketMessageType.Text,
            true,
            CancellationToken.None
        );
    }
    finally
    {
        SendLockHolder.Lock.Release();
    }
}

// =========================================================
// ENVIAR FRAME BINARIO
// =========================================================

static async Task SendBinaryMessageAsync(
    ClientWebSocket socket,
    byte[] data)
{
    await SendLockHolder.Lock.WaitAsync();

    try
    {
        if (
            socket.State !=
            WebSocketState.Open
        )
        {
            return;
        }

        if (
            data == null ||
            data.Length == 0
        )
        {
            return;
        }

        await socket.SendAsync(
            new ArraySegment<byte>(
                data
            ),
            WebSocketMessageType.Binary,
            true,
            CancellationToken.None
        );
    }
    finally
    {
        SendLockHolder.Lock.Release();
    }
}

// =========================================================
// HEARTBEAT
// =========================================================

static async Task SendHeartbeatAsync(
    ClientWebSocket socket,
    string machineId,
    CancellationToken cancellationToken)
{
    try
    {
        while (
            !cancellationToken.IsCancellationRequested &&
            socket.State == WebSocketState.Open
        )
        {
            await Task.Delay(
                10000,
                cancellationToken
            );

            if (
                cancellationToken.IsCancellationRequested ||
                socket.State != WebSocketState.Open
            )
            {
                break;
            }

            var heartbeat =
                new
                {
                    type =
                        "HEARTBEAT",

                    machineId =
                        machineId
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
    catch (OperationCanceledException)
    {
    }
    catch (WebSocketException)
    {
    }
    catch (Exception ex)
    {
        Console.WriteLine(
            $"❌ Error en heartbeat: {ex.Message}"
        );
    }
}

// =========================================================
// RECIBIR MENSAJES DEL SERVIDOR
// =========================================================

static async Task ReceiveMessagesAsync(
    ClientWebSocket socket,
    CancellationToken cancellationToken)
{
    byte[] buffer =
        new byte[4096];

    CancellationTokenSource?
        screenStreamCancellation = null;

    Task?
        screenStreamTask = null;

    try
    {
        while (
            !cancellationToken.IsCancellationRequested &&
            socket.State == WebSocketState.Open
        )
        {
            WebSocketReceiveResult result =
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
                break;
            }

            if (
                result.MessageType !=
                WebSocketMessageType.Text
            )
            {
                continue;
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
                    command != null &&
                    command.MachineId.Equals(
                        GetMachineId(),
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    if (command.Type == "COMMAND")
                    {
                        // ==========================================
                        // PING
                    // ==========================================

                    if (
                        command.Command ==
                        "PING"
                    )
                    {
                        Console.WriteLine(
                            "🏓 Comando PING recibido"
                        );

                        var response =
                            new
                            {
                                type =
                                    "COMMAND_RESULT",

                                machineId =
                                    command.MachineId,

                                command =
                                    "PING",

                                success =
                                    true,

                                message =
                                    "PONG",

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

                            var response =
                                new
                                {
                                    type =
                                        "SCREEN_CAPTURE_RESULT",

                                    machineId =
                                        command.MachineId,

                                    command =
                                        "SCREEN_CAPTURE",

                                    success =
                                        true,

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
                            var response =
                                new
                                {
                                    type =
                                        "SCREEN_CAPTURE_RESULT",

                                    machineId =
                                        command.MachineId,

                                    command =
                                        "SCREEN_CAPTURE",

                                    success =
                                        false,

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

                    // ==========================================
                    // INICIAR TRANSMISIÓN
                    // ==========================================

                    else if (
                        command.Command ==
                        "START_SCREEN_STREAM"
                    )
                    {
                        Console.WriteLine(
                            "📡 Comando START_SCREEN_STREAM recibido"
                        );

                        // Detener stream anterior
                        try
                        {
                            screenStreamCancellation?.Cancel();
                        }
                        catch
                        {
                        }

                        screenStreamCancellation =
                            new CancellationTokenSource();

                        CancellationToken streamToken =
                            screenStreamCancellation.Token;

                        screenStreamTask =
                            Task.Run(
                                () =>
                                    StartScreenStreamAsync(
                                        socket,
                                        command.MachineId,
                                        streamToken
                                    ),
                                streamToken
                            );

                        Console.WriteLine(
                            "🟢 Transmisión de pantalla iniciada"
                        );
                    }

                    // ==========================================
                    // DETENER TRANSMISIÓN
                    // ==========================================

                    else if (
                        command.Command ==
                        "STOP_SCREEN_STREAM"
                    )
                    {
                        Console.WriteLine(
                            "🛑 Comando STOP_SCREEN_STREAM recibido"
                        );

                        try
                        {
                            screenStreamCancellation?.Cancel();
                        }
                        catch
                        {
                        }

                        screenStreamCancellation =
                            null;

                        screenStreamTask =
                            null;

                        Console.WriteLine(
                            "🔴 Transmisión de pantalla detenida"
                        );
                    }

                    // ==========================================
                    // PREVIEW
                    // ==========================================

                    else if (
                        command.Command ==
                        "PREVIEW_CAPTURE"
                    )
                    {
                        Console.WriteLine(
                            "🖼️ Comando PREVIEW_CAPTURE recibido"
                        );

                        try
                        {
                            string filePath =
                                ScreenCapture.CapturePreview();

                            byte[] imageBytes =
                                await File.ReadAllBytesAsync(
                                    filePath
                                );

                            string base64Image =
                                Convert.ToBase64String(
                                    imageBytes
                                );

                            var response =
                                new
                                {
                                    type =
                                        "SCREEN_CAPTURE_RESULT",

                                    machineId =
                                        command.MachineId,

                                    command =
                                        "PREVIEW_CAPTURE",

                                    success =
                                        true,

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
                                $"🖼️ Preview enviado: " +
                                $"{imageBytes.Length / 1024} KB"
                            );
                        }
                        catch (Exception ex)
                        {
                            var response =
                                new
                                {
                                    type =
                                        "SCREEN_CAPTURE_RESULT",

                                    machineId =
                                        command.MachineId,

                                    command =
                                        "PREVIEW_CAPTURE",

                                    success =
                                        false,

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
                                $"❌ Error generando preview: " +
                                $"{ex.Message}"
                            );
                        }
                    }
                    // ==========================================
                    // BLOQUEAR SESIÓN
                    // ==========================================

                    else if (
                        command.Command ==
                        "LOCK_SESSION"
                    )
                    {
                        Console.WriteLine(
                            "🔒 Comando LOCK_SESSION recibido"
                        );

                        try
                        {
                            bool success =
                                WindowsNativeMethods
                                    .LockWorkStation();

                            var response =
                                new
                                {
                                    type =
                                        "COMMAND_RESULT",

                                    machineId =
                                        command.MachineId,

                                    command =
                                        "LOCK_SESSION",

                                    success,

                                    message =
                                        success
                                            ? "Sesión bloqueada correctamente."
                                            : "Windows no pudo bloquear la sesión.",

                                    timestamp =
                                        DateTime.UtcNow.ToString("O")
                                };

                            await SendMessageAsync(
                                socket,
                                response
                            );

                            Console.WriteLine(
                                success
                                    ? "🔒 Sesión bloqueada"
                                    : "❌ No se pudo bloquear la sesión"
                            );
                        }
                        catch (Exception ex)
                        {
                            var response =
                                new
                                {
                                    type =
                                        "COMMAND_RESULT",

                                    machineId =
                                        command.MachineId,

                                    command =
                                        "LOCK_SESSION",

                                    success =
                                        false,

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
                                $"❌ Error bloqueando sesión: {ex.Message}"
                            );
                        }
                    }

                    // ==========================================
                    // REINICIAR PC
                    // ==========================================

                    else if (
                        command.Command ==
                        "RESTART_PC"
                    )
                    {
                        Console.WriteLine(
                            "🔄 Comando RESTART_PC recibido"
                        );

                        try
                        {
                            using var process =
                                new System.Diagnostics.Process();

                            process.StartInfo.FileName =
                                "shutdown.exe";

                            process.StartInfo.Arguments =
                                "/r /t 0";

                            process.StartInfo.CreateNoWindow =
                                true;

                            process.StartInfo.UseShellExecute =
                                false;

                            process.Start();

                            var response =
                                new
                                {
                                    type =
                                        "COMMAND_RESULT",

                                    machineId =
                                        command.MachineId,

                                    command =
                                        "RESTART_PC",

                                    success =
                                        true,

                                    message =
                                        "Reinicio solicitado correctamente.",

                                    timestamp =
                                        DateTime.UtcNow.ToString("O")
                                };

                            await SendMessageAsync(
                                socket,
                                response
                            );

                            Console.WriteLine(
                                "🔄 Reinicio solicitado"
                            );
                        }
                        catch (Exception ex)
                        {
                            var response =
                                new
                                {
                                    type =
                                        "COMMAND_RESULT",

                                    machineId =
                                        command.MachineId,

                                    command =
                                        "RESTART_PC",

                                    success =
                                        false,

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
                                $"❌ Error reiniciando PC: {ex.Message}"
                            );
                        }
                    }

                    // ==========================================
                    // APAGAR PC
                    // ==========================================

                    else if (
                        command.Command ==
                        "SHUTDOWN_PC"
                    )
                    {
                        Console.WriteLine(
                            "⏻ Comando SHUTDOWN_PC recibido"
                        );

                        try
                        {
                            using var process =
                                new System.Diagnostics.Process();

                            process.StartInfo.FileName =
                                "shutdown.exe";

                            process.StartInfo.Arguments =
                                "/s /t 0";

                            process.StartInfo.CreateNoWindow =
                                true;

                            process.StartInfo.UseShellExecute =
                                false;

                            process.Start();

                            var response =
                                new
                                {
                                    type =
                                        "COMMAND_RESULT",

                                    machineId =
                                        command.MachineId,

                                    command =
                                        "SHUTDOWN_PC",

                                    success =
                                        true,

                                    message =
                                        "Apagado solicitado correctamente.",

                                    timestamp =
                                        DateTime.UtcNow.ToString("O")
                                };

                            await SendMessageAsync(
                                socket,
                                response
                            );

                            Console.WriteLine(
                                "⏻ Apagado solicitado"
                            );
                        }
                        catch (Exception ex)
                        {
                            var response =
                                new
                                {
                                    type =
                                        "COMMAND_RESULT",

                                    machineId =
                                        command.MachineId,

                                    command =
                                        "SHUTDOWN_PC",

                                    success =
                                        false,

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
                                $"❌ Error apagando PC: {ex.Message}"
                            );
                        }
                    }

                    }

                    // ==========================================
                    // CONTROL REMOTO
                    // ==========================================

                    else if (
                        command.Type == "REMOTE_INPUT"
                    )
                    {
                        try
                        {
                            await ProcessRemoteInputAsync(command);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(
                                $"❌ Error en control remoto: {ex.Message}"
                            );
                        }
                    }
                }
            }
            catch (JsonException)
            {
                Console.WriteLine(
                    "⚠️ Comando JSON inválido."
                );
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"❌ Error procesando comando: {ex.Message}"
                );
            }
        }
    }
    catch (OperationCanceledException)
    {
    }
    catch (WebSocketException ex)
    {
        Console.WriteLine(
            $"❌ WebSocket: {ex.Message}"
        );
    }
    catch (Exception ex)
    {
        Console.WriteLine(
            $"❌ Error recibiendo mensajes: {ex.Message}"
        );
    }
    finally
    {
        try
        {
            screenStreamCancellation?.Cancel();
        }
        catch
        {
        }

        screenStreamCancellation?.Dispose();

        // Evitar warning de variable no utilizada
        _ = screenStreamTask;
    }
}
// =========================================================
// PROCESAR CONTROL REMOTO
// =========================================================

static Task ProcessRemoteInputAsync(
    ServerCommand command)
{
    string action =
        command.Action?.Trim().ToUpperInvariant() ?? "";

    switch (action)
    {
        // ==========================================
        // MOUSE MOVE
        // ==========================================

        case "MOUSE_MOVE":
            WindowsNativeMethods.MoveMouse(
                command.X,
                command.Y
            );
            break;

        // ==========================================
        // MOUSE DOWN
        // ==========================================

        case "MOUSE_DOWN":
            WindowsNativeMethods.MouseDown(
                command.Button
            );
            break;

        // ==========================================
        // MOUSE UP
        // ==========================================

        case "MOUSE_UP":
            WindowsNativeMethods.MouseUp(
                command.Button
            );
            break;

        // ==========================================
        // CLICK
        // ==========================================

        case "MOUSE_CLICK":
            WindowsNativeMethods.MouseClick(
                command.Button
            );
            break;

        // ==========================================
        // DOBLE CLICK
        // ==========================================

        case "MOUSE_DOUBLE_CLICK":
            WindowsNativeMethods.MouseDoubleClick(
                command.Button
            );
            break;

        // ==========================================
        // RUEDA
        // ==========================================

        case "MOUSE_WHEEL":
            WindowsNativeMethods.MouseWheel(
                command.Delta
            );
            break;

        // ==========================================
        // TECLA DOWN
        // ==========================================

        case "KEY_DOWN":
            WindowsNativeMethods.KeyDown(
                command.Key
            );
            break;

        // ==========================================
        // TECLA UP
        // ==========================================

        case "KEY_UP":
            WindowsNativeMethods.KeyUp(
                command.Key
            );
            break;

        default:
            Console.WriteLine(
                $"⚠️ Acción REMOTE_INPUT desconocida: {action}"
            );
            break;
    }

    return Task.CompletedTask;
}
// =========================================================
// PREVIEW AUTOMÁTICA DE PANTALLA
// =========================================================

static async Task SendScreenPreviewAsync(
    ClientWebSocket socket,
    string machineId,
    CancellationToken cancellationToken)
{
    try
    {
        while (
            !cancellationToken.IsCancellationRequested &&
            socket.State == WebSocketState.Open
        )
        {
            try
            {
                // ==========================================
                // ESPERAR ANTES DE GENERAR
                // ==========================================

                await Task.Delay(
                    3000,
                    cancellationToken
                );

                if (
                    cancellationToken.IsCancellationRequested ||
                    socket.State != WebSocketState.Open
                )
                {
                    break;
                }

                Console.WriteLine(
                    "🖥️ Generando preview automática..."
                );

                // ==========================================
                // CAPTURAR
                // ==========================================

                string filePath =
                    ScreenCapture.CaptureScreen();

                byte[] imageBytes =
                    await File.ReadAllBytesAsync(
                        filePath,
                        cancellationToken
                    );

                string base64Image =
                    Convert.ToBase64String(
                        imageBytes
                    );

                var response =
                    new
                    {
                        type =
                            "SCREEN_CAPTURE_RESULT",

                        machineId =
                            machineId,

                        command =
                            "SCREEN_PREVIEW",

                        success =
                            true,

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
                    "📸 Preview automática enviada"
                );
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (WebSocketException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"❌ Error en preview automática: {ex.Message}"
                );

                break;
            }
        }
    }
    catch (OperationCanceledException)
    {
    }
    catch (WebSocketException)
    {
    }
}

// =========================================================
// OBTENER MACHINE ID
// =========================================================

static string GetMachineId()
{
    string configPath =
        Path.Combine(
            AppContext.BaseDirectory,
            "config.json"
        );

    if (!File.Exists(configPath))
    {
        return "";
    }

    try
    {
        string json =
            File.ReadAllText(
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

        return config?.MachineId ?? "";
    }
    catch
    {
        return "";
    }
}

// =========================================================
// CONFIGURACIÓN
// =========================================================

public class AgentConfig
{
    public string MachineId { get; set; } = "";

    public string AuthToken { get; set; } = "";
}

// =========================================================
// COMANDO DEL SERVIDOR
// =========================================================

public class ServerCommand
{
    public string Type { get; set; } = "";
    public string Command { get; set; } = "";
    public string MachineId { get; set; } = "";

    // ==========================================
    // CONTROL REMOTO
    // ==========================================

    public string Action { get; set; } = "";

    public double X { get; set; }

    public double Y { get; set; }

    public string? Button { get; set; }

    public int Delta { get; set; }

    public string? Key { get; set; }
}

// =========================================================
// PROTECCIÓN DE ENVÍOS WEBSOCKET
// =========================================================

public static class SendLockHolder
{
    public static readonly SemaphoreSlim Lock =
        new(1, 1);
}

// =========================================================
// FUNCIONES NATIVAS DE WINDOWS
// =========================================================

// =========================================================
// FUNCIONES NATIVAS DE WINDOWS
// =========================================================

public static class WindowsNativeMethods
{
    private const uint INPUT_MOUSE = 0;
    private const uint INPUT_KEYBOARD = 1;

    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;
    private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;

    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MOUSEINPUT mi;

        [FieldOffset(0)]
        public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport(
        "user32.dll",
        SetLastError = true
    )]
    private static extern uint SendInput(
        uint nInputs,
        INPUT[] pInputs,
        int cbSize
    );

    [DllImport(
        "user32.dll",
        SetLastError = true
    )]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool LockWorkStation();

    // =====================================================
    // MOUSE
    // =====================================================

    public static void MoveMouse(
        double x,
        double y)
    {
        int screenWidth =
            GetSystemMetrics(0);

        int screenHeight =
            GetSystemMetrics(1);

        if (screenWidth <= 1 || screenHeight <= 1)
            return;

        int pixelX =
            Math.Clamp(
                (int)Math.Round(x),
                0,
                screenWidth - 1
            );

        int pixelY =
            Math.Clamp(
                (int)Math.Round(y),
                0,
                screenHeight - 1
            );

        int absoluteX =
            (int)Math.Round(
                pixelX * 65535.0 /
                (screenWidth - 1)
            );

        int absoluteY =
            (int)Math.Round(
                pixelY * 65535.0 /
                (screenHeight - 1)
            );

        SendMouse(
            absoluteX,
            absoluteY,
            MOUSEEVENTF_MOVE |
            MOUSEEVENTF_ABSOLUTE
        );
    }

    public static void MouseDown(
        string? button)
    {
        SendMouseButton(
            button,
            true
        );
    }

    public static void MouseUp(
        string? button)
    {
        SendMouseButton(
            button,
            false
        );
    }

    public static void MouseClick(
        string? button)
    {
        SendMouseButton(
            button,
            true
        );

        SendMouseButton(
            button,
            false
        );
    }

    public static void MouseDoubleClick(
        string? button)
    {
        MouseClick(button);

        Thread.Sleep(50);

        MouseClick(button);
    }

    public static void MouseWheel(
        int delta)
    {
        if (delta == 0)
            return;

        SendMouse(
            0,
            0,
            MOUSEEVENTF_WHEEL,
            unchecked((uint)delta)
        );
    }

    private static void SendMouseButton(
        string? button,
        bool down)
    {
        string normalized =
            button?.Trim().ToUpperInvariant()
            ?? "LEFT";

        uint flags;

        switch (normalized)
        {
            case "RIGHT":
                flags =
                    down
                        ? MOUSEEVENTF_RIGHTDOWN
                        : MOUSEEVENTF_RIGHTUP;
                break;

            case "MIDDLE":
                flags =
                    down
                        ? MOUSEEVENTF_MIDDLEDOWN
                        : MOUSEEVENTF_MIDDLEUP;
                break;

            case "LEFT":
                flags =
                    down
                        ? MOUSEEVENTF_LEFTDOWN
                        : MOUSEEVENTF_LEFTUP;
                break;

            default:
                return;
        }

        SendMouse(
            0,
            0,
            flags
        );
    }

    private static void SendMouse(
        int dx,
        int dy,
        uint flags,
        uint mouseData = 0)
    {
        INPUT[] inputs =
        {
            new INPUT
            {
                type = INPUT_MOUSE,
                U = new InputUnion
                {
                    mi = new MOUSEINPUT
                    {
                        dx = dx,
                        dy = dy,
                        mouseData = mouseData,
                        dwFlags = flags,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            }
        };

        SendInput(
            1,
            inputs,
            Marshal.SizeOf<INPUT>()
        );
    }

    // =====================================================
    // TECLADO
    // =====================================================

    public static void KeyDown(
        string? key)
    {
        if (!TryGetVirtualKey(
                key,
                out ushort virtualKey))
        {
            return;
        }

        SendKeyboard(
            virtualKey,
            false
        );
    }

    public static void KeyUp(
        string? key)
    {
        if (!TryGetVirtualKey(
                key,
                out ushort virtualKey))
        {
            return;
        }

        SendKeyboard(
            virtualKey,
            true
        );
    }

    private static void SendKeyboard(
        ushort virtualKey,
        bool keyUp)
    {
        INPUT[] inputs =
        {
            new INPUT
            {
                type = INPUT_KEYBOARD,
                U = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = virtualKey,
                        wScan = 0,
                        dwFlags =
                            keyUp
                                ? KEYEVENTF_KEYUP
                                : 0,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            }
        };

        SendInput(
            1,
            inputs,
            Marshal.SizeOf<INPUT>()
        );
    }

    // =====================================================
    // CONVERSIÓN DE TECLAS
    // =====================================================

    private static bool TryGetVirtualKey(
        string? key,
        out ushort virtualKey)
    {
        virtualKey = 0;

        if (string.IsNullOrWhiteSpace(key))
            return false;

        string normalized =
            key.Trim().ToUpperInvariant();

        // Letras
        if (
            normalized.Length == 1 &&
            normalized[0] >= 'A' &&
            normalized[0] <= 'Z'
        )
        {
            virtualKey =
                normalized[0];

            return true;
        }

        // Números
        if (
            normalized.Length == 1 &&
            normalized[0] >= '0' &&
            normalized[0] <= '9'
        )
        {
            virtualKey =
                normalized[0];

            return true;
        }

        // Teclas especiales
        var keys =
            new Dictionary<string, ushort>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["ENTER"] = 0x0D,
                ["ESC"] = 0x1B,
                ["ESCAPE"] = 0x1B,
                ["TAB"] = 0x09,
                ["SPACE"] = 0x20,
                ["BACKSPACE"] = 0x08,
                ["DELETE"] = 0x2E,
                ["DEL"] = 0x2E,
                ["INSERT"] = 0x2D,
                ["HOME"] = 0x24,
                ["END"] = 0x23,
                ["PAGEUP"] = 0x21,
                ["PAGEDOWN"] = 0x22,

                ["LEFT"] = 0x25,
                ["UP"] = 0x26,
                ["RIGHT"] = 0x27,
                ["DOWN"] = 0x28,

                ["SHIFT"] = 0x10,
                ["CTRL"] = 0x11,
                ["CONTROL"] = 0x11,
                ["ALT"] = 0x12,
                ["WIN"] = 0x5B,
                ["LWIN"] = 0x5B,
                ["RWIN"] = 0x5C,

                ["CAPSLOCK"] = 0x14,
                ["NUMLOCK"] = 0x90,
                ["SCROLLLOCK"] = 0x91,

                ["F1"] = 0x70,
                ["F2"] = 0x71,
                ["F3"] = 0x72,
                ["F4"] = 0x73,
                ["F5"] = 0x74,
                ["F6"] = 0x75,
                ["F7"] = 0x76,
                ["F8"] = 0x77,
                ["F9"] = 0x78,
                ["F10"] = 0x79,
                ["F11"] = 0x7A,
                ["F12"] = 0x7B
            };

        return keys.TryGetValue(
            normalized,
            out virtualKey
        );
    }

    // =====================================================
    // RESOLUCIÓN DE PANTALLA
    // =====================================================

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(
        int nIndex
    );
}