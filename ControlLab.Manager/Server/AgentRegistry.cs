using System.Collections.Concurrent;
using System.Net.WebSockets;

namespace ControlLab.Manager.Server;

public sealed class AgentRegistry
{
    private readonly ConcurrentDictionary<
        string,
        AgentConnection
    > _agents = new();

    // ==========================================
    // CANTIDAD DE AGENTS
    // ==========================================

    public int Count =>
        _agents.Count;

    // ==========================================
    // TODOS LOS AGENTS
    // ==========================================

    public IEnumerable<AgentConnection> Values =>
        _agents.Values;

    // ==========================================
    // OBTENER AGENT
    // ==========================================

    public bool TryGetValue(
        string machineId,
        out AgentConnection agent)
    {
        return _agents.TryGetValue(
            machineId,
            out agent!
        );
    }

    // ==========================================
    // REGISTRAR / ACTUALIZAR AGENT
    // ==========================================

    public void Register(
        AgentConnection agent)
    {
        _agents[
            agent.MachineId
        ] = agent;
    }

    // ==========================================
    // COMPROBAR EXISTENCIA
    // ==========================================

    public bool Contains(
        string machineId)
    {
        return _agents.ContainsKey(
            machineId
        );
    }

    // ==========================================
    // ELIMINAR AGENT
    // ==========================================

    public bool TryRemove(
        string machineId,
        out AgentConnection? agent)
    {
        return _agents.TryRemove(
            machineId,
            out agent
        );
    }

    // ==========================================
    // LIMPIAR
    // ==========================================

    public void Clear()
    {
        _agents.Clear();
    }

    // ==========================================
    // AGENTS ONLINE
    // ==========================================

    public IEnumerable<AgentConnection> GetOnline()
    {
        return _agents.Values.Where(
            agent =>
                agent.Socket.State ==
                WebSocketState.Open
        );
    }

    // ==========================================
    // AGENTS OFFLINE
    // ==========================================

    public IEnumerable<AgentConnection> GetOffline()
    {
        return _agents.Values.Where(
            agent =>
                agent.Socket.State !=
                WebSocketState.Open
        );
    }

    // ==========================================
    // CERRAR TODAS LAS CONEXIONES
    // ==========================================

    public void DisposeConnections()
    {
        foreach (
            var agent
            in _agents.Values
        )
        {
            try
            {
                agent.Socket.Dispose();
            }
            catch
            {
            }
        }

        _agents.Clear();
    }
}


// ==========================================
// CONEXIÓN DE AGENT
// ==========================================

public sealed class AgentConnection
{
    public string MachineId { get; set; } =
        "";

    public string Hostname { get; set; } =
        "";

    public string Platform { get; set; } =
        "";

    public string AgentVersion { get; set; } =
        "";

    public string ConnectedAt { get; set; } =
        "";

    public string LastHeartbeat { get; set; } =
        "";

    public WebSocket Socket { get; set; } =
        null!;
}


// ==========================================
// CAPTURA DE PANTALLA
// ==========================================

public sealed class ScreenCaptureData
{
    public string MachineId { get; set; } =
        "";

    public string Image { get; set; } =
        "";

    public string Timestamp { get; set; } =
        "";
}