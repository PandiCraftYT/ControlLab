using System.IO;
using Microsoft.Data.Sqlite;

namespace ControlLab.Manager.Data;

public sealed class ControlLabDatabase

{
    private readonly string _connectionString;

    public ControlLabDatabase()
    {
        string dataDirectory =
            Path.Combine(
                AppContext.BaseDirectory,
                "data"
            );

        Directory.CreateDirectory(
            dataDirectory
        );

        string databasePath =
            Path.Combine(
                dataDirectory,
                "controllab.db"
            );

        _connectionString =
            $"Data Source={databasePath}";

        Initialize();
    }
    public int CountAgents()
    {
        using var connection =
            new SqliteConnection(_connectionString);

        connection.Open();

        using var command =
            connection.CreateCommand();

        command.CommandText =
            "SELECT COUNT(*) FROM Agents;";

        return Convert.ToInt32(
            command.ExecuteScalar()
        );
    }
    // ==========================================
    // INICIALIZAR BASE DE DATOS
    // ==========================================

    private void Initialize()
    {
        using var connection =
            new SqliteConnection(
                _connectionString
            );

        connection.Open();

        using var command =
            connection.CreateCommand();

        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS Agents
            (
                MachineId TEXT PRIMARY KEY,
                Hostname TEXT NOT NULL,
                Platform TEXT NOT NULL,
                AgentVersion TEXT NOT NULL,
                FirstSeen TEXT NOT NULL,
                LastSeen TEXT NOT NULL
            );
            """;

        command.ExecuteNonQuery();
    }

    // ==========================================
    // REGISTRAR / ACTUALIZAR AGENT
    // ==========================================

    public void RegisterAgent(
        string machineId,
        string hostname,
        string platform,
        string agentVersion)
    {
        using var connection =
            new SqliteConnection(
                _connectionString
            );

        connection.Open();

        using var command =
            connection.CreateCommand();

        command.CommandText =
            """
            INSERT INTO Agents
            (
                MachineId,
                Hostname,
                Platform,
                AgentVersion,
                FirstSeen,
                LastSeen
            )
            VALUES
            (
                $machineId,
                $hostname,
                $platform,
                $agentVersion,
                $firstSeen,
                $lastSeen
            )
            ON CONFLICT(MachineId)
            DO UPDATE SET
                Hostname = excluded.Hostname,
                Platform = excluded.Platform,
                AgentVersion = excluded.AgentVersion,
                LastSeen = excluded.LastSeen;
            """;

        string now =
            DateTime.UtcNow.ToString("O");

        command.Parameters.AddWithValue(
            "$machineId",
            machineId
        );

        command.Parameters.AddWithValue(
            "$hostname",
            hostname
        );

        command.Parameters.AddWithValue(
            "$platform",
            platform
        );

        command.Parameters.AddWithValue(
            "$agentVersion",
            agentVersion
        );

        command.Parameters.AddWithValue(
            "$firstSeen",
            now
        );

        command.Parameters.AddWithValue(
            "$lastSeen",
            now
        );

        command.ExecuteNonQuery();
    }

    // ==========================================
    // OBTENER TODOS LOS AGENTS REGISTRADOS
    // ==========================================

    public List<RegisteredAgent> GetAgents()
    {
        var agents =
            new List<RegisteredAgent>();

        using var connection =
            new SqliteConnection(
                _connectionString
            );

        connection.Open();

        using var command =
            connection.CreateCommand();

        command.CommandText =
            """
            SELECT
                MachineId,
                Hostname,
                Platform,
                AgentVersion,
                FirstSeen,
                LastSeen
            FROM Agents
            ORDER BY MachineId;
            """;

        using var reader =
            command.ExecuteReader();

        while (reader.Read())
        {
            agents.Add(
                new RegisteredAgent
                {
                    MachineId =
                        reader.GetString(0),

                    Hostname =
                        reader.GetString(1),

                    Platform =
                        reader.GetString(2),

                    AgentVersion =
                        reader.GetString(3),

                    FirstSeen =
                        reader.GetString(4),

                    LastSeen =
                        reader.GetString(5)
                }
            );
        }

        return agents;
    }
}

// ==========================================
// AGENT REGISTRADO
// ==========================================

public sealed class RegisteredAgent
{
    public string MachineId { get; set; } =
        "";

    public string Hostname { get; set; } =
        "";

    public string Platform { get; set; } =
        "";

    public string AgentVersion { get; set; } =
        "";

    public string FirstSeen { get; set; } =
        "";

    public string LastSeen { get; set; } =
        "";
}