using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace ControlLab.Manager.Server;

public sealed class HttpApiHandler
{
    private readonly Func<IEnumerable<AgentConnection>> _getAgents;
    private readonly Func<string, ScreenCaptureData?> _getScreen;
    private readonly Func<HttpListenerContext, string, string, Task> _sendCommand;

    public HttpApiHandler(
        Func<IEnumerable<AgentConnection>> getAgents,
        Func<string, ScreenCaptureData?> getScreen,
        Func<HttpListenerContext, string, string, Task> sendCommand)
    {
        _getAgents = getAgents;
        _getScreen = getScreen;
        _sendCommand = sendCommand;
    }

    public async Task HandleAsync(HttpListenerContext context)
    {
        try
        {
            string path = context.Request.Url?.AbsolutePath ?? "/";
            string method = context.Request.HttpMethod;

            if (method == "GET" && path == "/")
            {
                await SendTextAsync(
                    context,
                    "ControlLab Server funcionando correctamente."
                );
                return;
            }

            if (method == "GET" && path == "/api/agents")
            {
                await SendAgentsAsync(context);
                return;
            }

            if (!path.StartsWith(
                    "/api/agents/",
                    StringComparison.OrdinalIgnoreCase))
            {
                await SendErrorAsync(context, 404, "Not Found");
                return;
            }

            string[] parts = path.Split(
                '/',
                StringSplitOptions.RemoveEmptyEntries
            );

            if (parts.Length < 4)
            {
                await SendErrorAsync(context, 404, "Ruta no válida.");
                return;
            }

            string machineId =
                Uri.UnescapeDataString(parts[2]);

            string action =
                parts[3].ToLowerInvariant();

            if (method == "POST" &&
                (action == "ping" || action == "screen"))
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

            if (method == "GET" && action == "screen")
            {
                await SendScreenAsync(
                    context,
                    machineId
                );
                return;
            }

            await SendErrorAsync(
                context,
                404,
                "Acción no encontrada."
            );
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error HTTP: {ex.Message}");

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

    private async Task SendAgentsAsync(
        HttpListenerContext context)
    {
        var agents = _getAgents()
            .Select(agent => new
            {
                machineId = agent.MachineId,
                hostname = agent.Hostname,
                platform = agent.Platform,
                agentVersion = agent.AgentVersion,
                connectedAt = agent.ConnectedAt,
                lastHeartbeat = agent.LastHeartbeat,
                status =
                    agent.Socket.State == WebSocketState.Open
                        ? "online"
                        : "offline"
            })
            .OrderBy(
                agent => agent.machineId,
                StringComparer.OrdinalIgnoreCase
            )
            .ToList();

        await SendJsonAsync(
            context,
            new
            {
                total = agents.Count,
                agents
            }
        );
    }

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
                Convert.FromBase64String(screen.Image);
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

        context.Response.StatusCode = 200;
        context.Response.ContentType = "image/jpeg";
        context.Response.ContentLength64 =
            imageBytes.Length;
        context.Response.Headers["Cache-Control"] =
            "no-store";

        await context.Response.OutputStream
            .WriteAsync(imageBytes);

        context.Response.Close();
    }

    internal static async Task SendJsonAsync(
        HttpListenerContext context,
        object data,
        int statusCode = 200)
    {
        byte[] bytes =
            Encoding.UTF8.GetBytes(
                JsonSerializer.Serialize(data)
            );

        context.Response.StatusCode = statusCode;
        context.Response.ContentType =
            "application/json; charset=utf-8";
        context.Response.ContentLength64 =
            bytes.Length;

        await context.Response.OutputStream
            .WriteAsync(bytes);

        context.Response.Close();
    }

    internal static async Task SendTextAsync(
        HttpListenerContext context,
        string text,
        int statusCode = 200)
    {
        byte[] bytes =
            Encoding.UTF8.GetBytes(text);

        context.Response.StatusCode = statusCode;
        context.Response.ContentType =
            "text/plain; charset=utf-8";
        context.Response.ContentLength64 =
            bytes.Length;

        await context.Response.OutputStream
            .WriteAsync(bytes);

        context.Response.Close();
    }

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