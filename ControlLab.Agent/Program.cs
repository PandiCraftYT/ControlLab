using ControlLab.Agent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices;

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

public static class WindowsNativeMethods
{
    [DllImport(
        "user32.dll",
        SetLastError = true
    )]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool LockWorkStation();
}