namespace ControlLab.Manager.Models;

public class AgentsResponse
{
    public int Total { get; set; }

    public List<AgentInfo> Agents { get; set; } =
        new();
}