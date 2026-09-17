using System.Net.Http;
using System.Text.Json;
using ControlLab.Manager.Models;

namespace ControlLab.Manager.Services;

public class ControlLabApi
{
    private readonly HttpClient _httpClient;

    private const string ServerUrl =
        "http://localhost:8080";

    public ControlLabApi()
    {
        _httpClient = new HttpClient();
    }

    // ==========================================
    // OBTENER EQUIPOS
    // ==========================================

    public async Task<AgentsResponse?> GetAgentsAsync()
    {
        var response =
            await _httpClient.GetAsync(
                $"{ServerUrl}/api/agents"
            );

        response.EnsureSuccessStatusCode();

        var json =
            await response.Content
                .ReadAsStringAsync();

        return JsonSerializer.Deserialize<AgentsResponse>(
            json,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }
        );
    }

    // ==========================================
    // SOLICITAR CAPTURA
    // ==========================================

    public async Task<HttpResponseMessage>
        RequestScreenAsync(
            string machineId)
    {
        return await _httpClient.PostAsync(
            $"{ServerUrl}/api/agents/" +
            $"{Uri.EscapeDataString(machineId)}" +
            "/screen",
            null
        );
    }

    // ==========================================
    // OBTENER CAPTURA
    // ==========================================

    public async Task<byte[]> GetScreenAsync(
        string machineId,
        CancellationToken cancellationToken = default)
    {
        return await _httpClient.GetByteArrayAsync(
            $"{ServerUrl}/api/agents/" +
            $"{Uri.EscapeDataString(machineId)}" +
            "/screen",
            cancellationToken
        );
    }

    // ==========================================
    // AUTORIZAR EQUIPO
    // ==========================================

    public async Task<bool> AuthorizeAgentAsync(
        string machineId,
        CancellationToken cancellationToken = default)
    {
        using var response =
            await _httpClient.PostAsync(
                $"{ServerUrl}/api/agents/" +
                $"{Uri.EscapeDataString(machineId)}" +
                "/authorize",
                null,
                cancellationToken
            );

        return response.IsSuccessStatusCode;
    }

    // ==========================================
    // REVOCAR AUTORIZACIÓN
    // ==========================================

    public async Task<bool> RevokeAgentAsync(
        string machineId,
        CancellationToken cancellationToken = default)
    {
        using var response =
            await _httpClient.PostAsync(
                $"{ServerUrl}/api/agents/" +
                $"{Uri.EscapeDataString(machineId)}" +
                "/revoke",
                null,
                cancellationToken
            );

        return response.IsSuccessStatusCode;
    }

    // ==========================================
    // CAMBIAR NOMBRE DEL EQUIPO
    // ==========================================

    public async Task<bool> RenameAgentAsync(
        string machineId,
        string displayName,
        CancellationToken cancellationToken = default)
    {
        var payload =
            JsonSerializer.Serialize(
                new
                {
                    displayName
                }
            );

        using var content =
            new StringContent(
                payload,
                System.Text.Encoding.UTF8,
                "application/json"
            );

        using var response =
            await _httpClient.PostAsync(
                $"{ServerUrl}/api/agents/" +
                $"{Uri.EscapeDataString(machineId)}" +
                "/rename",
                content,
                cancellationToken
            );

        return response.IsSuccessStatusCode;
    }

    // ==========================================
    // BLOQUEAR SESIÓN
    // ==========================================

    public async Task<bool> LockSessionAsync(
        string machineId,
        CancellationToken cancellationToken = default)
    {
        using var response =
            await _httpClient.PostAsync(
                $"{ServerUrl}/api/agents/" +
                $"{Uri.EscapeDataString(machineId)}" +
                "/lock",
                null,
                cancellationToken
            );

        return response.IsSuccessStatusCode;
    }

    // ==========================================
    // PREVIEW
    // ==========================================

    public async Task<bool> RequestPreviewAsync(
        string machineId,
        CancellationToken cancellationToken = default)
    {
        using var response =
            await _httpClient.PostAsync(
                $"{ServerUrl}/api/agents/" +
                $"{Uri.EscapeDataString(machineId)}" +
                "/preview",
                null,
                cancellationToken
            );

        return response.IsSuccessStatusCode;
    }

    // ==========================================
    // INICIAR STREAM DE PANTALLA
    // ==========================================

    public async Task<bool> StartScreenStreamAsync(
        string machineId,
        CancellationToken cancellationToken = default)
    {
        using var response =
            await _httpClient.PostAsync(
                $"{ServerUrl}/api/agents/" +
                $"{Uri.EscapeDataString(machineId)}" +
                "/stream?mode=start",
                null,
                cancellationToken
            );

        return response.IsSuccessStatusCode;
    }

    // ==========================================
    // DETENER STREAM DE PANTALLA
    // ==========================================

    public async Task<bool> StopScreenStreamAsync(
        string machineId,
        CancellationToken cancellationToken = default)
    {
        using var response =
            await _httpClient.PostAsync(
                $"{ServerUrl}/api/agents/" +
                $"{Uri.EscapeDataString(machineId)}" +
                "/stream?mode=stop",
                null,
                cancellationToken
            );

        return response.IsSuccessStatusCode;
    }

    // ==========================================
    // REINICIAR PC
    // ==========================================

    public async Task<bool> RestartAgentAsync(
        string machineId,
        CancellationToken cancellationToken = default)
    {
        using var response =
            await _httpClient.PostAsync(
                $"{ServerUrl}/api/agents/" +
                $"{Uri.EscapeDataString(machineId)}" +
                "/restart",
                null,
                cancellationToken
            );

        return response.IsSuccessStatusCode;
    }

    // ==========================================
    // APAGAR PC
    // ==========================================

    public async Task<bool> ShutdownAgentAsync(
        string machineId,
        CancellationToken cancellationToken = default)
    {
        using var response =
            await _httpClient.PostAsync(
                $"{ServerUrl}/api/agents/" +
                $"{Uri.EscapeDataString(machineId)}" +
                "/shutdown",
                null,
                cancellationToken
            );

        return response.IsSuccessStatusCode;
    }

    // ==========================================
    // CERRAR
    // ==========================================

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}