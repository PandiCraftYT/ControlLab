namespace ControlLab.Manager.Models;

public class AgentInfo
{
    public string MachineId { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public string Hostname { get; set; } = "";

    public string Platform { get; set; } = "";

    public string AgentVersion { get; set; } = "";

    public string ConnectedAt { get; set; } = "";

    public string LastHeartbeat { get; set; } = "";

    public string Status { get; set; } = "";
    
    public bool Authorized { get; set; }
}