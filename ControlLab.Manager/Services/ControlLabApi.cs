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
    // CERRAR
    // ==========================================

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}