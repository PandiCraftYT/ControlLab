using System.IO;
using Microsoft.Data.Sqlite;

namespace ControlLab.Manager.Data;

public sealed class ControlLabDatabase
{
    private readonly string _connectionString;

    private readonly object _registrationLock = new();

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

    // ==========================================
    // CONTAR EQUIPOS REGISTRADOS
    // ==========================================

    public int CountAgents()
    {
        using var connection =
            new SqliteConnection(
                _connectionString
            );

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
            "CREATE TABLE IF NOT EXISTS Agents (" +
            "MachineId TEXT PRIMARY KEY, " +
            "DisplayName TEXT NOT NULL UNIQUE, " +
            "Hostname TEXT NOT NULL, " +
            "Platform TEXT NOT NULL, " +
            "AgentVersion TEXT NOT NULL, " +
            "FirstSeen TEXT NOT NULL, " +
            "LastSeen TEXT NOT NULL, " +
            "Authorized INTEGER NOT NULL DEFAULT 0" +
            ");";

        command.ExecuteNonQuery();

        // ==========================================
        // MIGRACIONES
        // ==========================================

        EnsureDisplayNameColumn(connection);

        EnsureAuthorizedColumn(connection);
    }

    // ==========================================
    // ASEGURAR COLUMNA DISPLAY NAME
    // ==========================================

    private void EnsureDisplayNameColumn(
        SqliteConnection connection)
    {
        bool displayNameExists =
            ColumnExists(
                connection,
                "DisplayName"
            );

        if (displayNameExists)
            return;

        using var alterCommand =
            connection.CreateCommand();

        alterCommand.CommandText =
            "ALTER TABLE Agents " +
            "ADD COLUMN DisplayName TEXT;";

        alterCommand.ExecuteNonQuery();

        // ==========================================
        // ASIGNAR NOMBRES A EQUIPOS EXISTENTES
        // ==========================================

        using var agentsCommand =
            connection.CreateCommand();

        agentsCommand.CommandText =
            "SELECT MachineId " +
            "FROM Agents " +
            "WHERE DisplayName IS NULL " +
            "OR DisplayName = '' " +
            "ORDER BY MachineId;";

        var machineIds =
            new List<string>();

        using var reader =
            agentsCommand.ExecuteReader();

        while (reader.Read())
        {
            machineIds.Add(
                reader.GetString(0)
            );
        }

        foreach (string machineId in machineIds)
        {
            string displayName =
                GetNextDisplayName(
                    connection
                );

            using var updateCommand =
                connection.CreateCommand();

            updateCommand.CommandText =
                "UPDATE Agents " +
                "SET DisplayName = $displayName " +
                "WHERE MachineId = $machineId;";

            updateCommand.Parameters.AddWithValue(
                "$displayName",
                displayName
            );

            updateCommand.Parameters.AddWithValue(
                "$machineId",
                machineId
            );

            updateCommand.ExecuteNonQuery();
        }
    }

    // ==========================================
    // ASEGURAR COLUMNA AUTHORIZED
    // ==========================================

    private void EnsureAuthorizedColumn(
        SqliteConnection connection)
    {
        bool authorizedExists =
            ColumnExists(
                connection,
                "Authorized"
            );

        if (authorizedExists)
            return;

        using var alterCommand =
            connection.CreateCommand();

        alterCommand.CommandText =
            "ALTER TABLE Agents " +
            "ADD COLUMN Authorized " +
            "INTEGER NOT NULL DEFAULT 1;";

        alterCommand.ExecuteNonQuery();
    }

    // ==========================================
    // COMPROBAR SI EXISTE UNA COLUMNA
    // ==========================================

    private static bool ColumnExists(
        SqliteConnection connection,
        string columnName)
    {
        using var command =
            connection.CreateCommand();

        command.CommandText =
            "PRAGMA table_info(Agents);";

        using var reader =
            command.ExecuteReader();

        while (reader.Read())
        {
            string currentColumn =
                reader.GetString(1);

            if (
                currentColumn.Equals(
                    columnName,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return true;
            }
        }

        return false;
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
        lock (_registrationLock)
        {
            using var connection =
                new SqliteConnection(
                    _connectionString
                );

            connection.Open();

            // ==========================================
            // ¿YA EXISTE?
            // ==========================================

            using var existingCommand =
                connection.CreateCommand();

            existingCommand.CommandText =
                "SELECT DisplayName " +
                "FROM Agents " +
                "WHERE MachineId = $machineId;";

            existingCommand.Parameters.AddWithValue(
                "$machineId",
                machineId
            );

            object? existingResult =
                existingCommand.ExecuteScalar();

            // ==========================================
            // EQUIPO NUEVO
            // ==========================================

            if (existingResult == null)
            {
                string displayName =
                    GetNextDisplayName(
                        connection
                    );

                string now =
                    DateTime.UtcNow.ToString("O");

                using var insertCommand =
                    connection.CreateCommand();

                insertCommand.CommandText =
                    "INSERT INTO Agents " +
                    "(" +
                    "MachineId, " +
                    "DisplayName, " +
                    "Hostname, " +
                    "Platform, " +
                    "AgentVersion, " +
                    "FirstSeen, " +
                    "LastSeen, " +
                    "Authorized" +
                    ") " +
                    "VALUES " +
                    "(" +
                    "$machineId, " +
                    "$displayName, " +
                    "$hostname, " +
                    "$platform, " +
                    "$agentVersion, " +
                    "$firstSeen, " +
                    "$lastSeen, " +
                    "$authorized" +
                    ");";

                insertCommand.Parameters.AddWithValue(
                    "$machineId",
                    machineId
                );

                insertCommand.Parameters.AddWithValue(
                    "$displayName",
                    displayName
                );

                insertCommand.Parameters.AddWithValue(
                    "$hostname",
                    hostname
                );

                insertCommand.Parameters.AddWithValue(
                    "$platform",
                    platform
                );

                insertCommand.Parameters.AddWithValue(
                    "$agentVersion",
                    agentVersion
                );

                insertCommand.Parameters.AddWithValue(
                    "$firstSeen",
                    now
                );

                insertCommand.Parameters.AddWithValue(
                    "$lastSeen",
                    now
                );

                // Equipo nuevo = pendiente de autorización.
                insertCommand.Parameters.AddWithValue(
                    "$authorized",
                    0
                );

                insertCommand.ExecuteNonQuery();

                Console.WriteLine(
                    $"💾 Equipo registrado en SQLite: " +
                    $"{displayName} ({machineId}) " +
                    "[PENDIENTE DE AUTORIZACIÓN]"
                );

                return;
            }

            // ==========================================
            // EQUIPO EXISTENTE
            // ==========================================

            string lastSeen =
                DateTime.UtcNow.ToString("O");

            using var updateCommand =
                connection.CreateCommand();

            updateCommand.CommandText =
                "UPDATE Agents " +
                "SET " +
                "Hostname = $hostname, " +
                "Platform = $platform, " +
                "AgentVersion = $agentVersion, " +
                "LastSeen = $lastSeen " +
                "WHERE MachineId = $machineId;";

            updateCommand.Parameters.AddWithValue(
                "$hostname",
                hostname
            );

            updateCommand.Parameters.AddWithValue(
                "$platform",
                platform
            );

            updateCommand.Parameters.AddWithValue(
                "$agentVersion",
                agentVersion
            );

            updateCommand.Parameters.AddWithValue(
                "$lastSeen",
                lastSeen
            );

            updateCommand.Parameters.AddWithValue(
                "$machineId",
                machineId
            );

            updateCommand.ExecuteNonQuery();

            Console.WriteLine(
                $"💾 Equipo actualizado en SQLite: " +
                $"{existingResult} ({machineId})"
            );
        }
    }

    // ==========================================
    // GENERAR SIGUIENTE NOMBRE DISPONIBLE
    // ==========================================

    private string GetNextDisplayName(
        SqliteConnection connection)
    {
        using var command =
            connection.CreateCommand();

        command.CommandText =
            "SELECT DisplayName " +
            "FROM Agents " +
            "WHERE DisplayName IS NOT NULL " +
            "AND DisplayName <> '';";

        var usedNames =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase
            );

        using var reader =
            command.ExecuteReader();

        while (reader.Read())
        {
            string name =
                reader.GetString(0);

            usedNames.Add(
                name
            );
        }

        int number = 1;

        while (true)
        {
            string candidate =
                $"PC-{number:00}";

            if (!usedNames.Contains(candidate))
            {
                return candidate;
            }

            number++;
        }
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
            "SELECT " +
            "MachineId, " +
            "DisplayName, " +
            "Hostname, " +
            "Platform, " +
            "AgentVersion, " +
            "FirstSeen, " +
            "LastSeen, " +
            "Authorized " +
            "FROM Agents " +
            "ORDER BY DisplayName;";

        using var reader =
            command.ExecuteReader();

        while (reader.Read())
        {
            agents.Add(
                new RegisteredAgent
                {
                    MachineId =
                        reader.GetString(0),

                    DisplayName =
                        reader.GetString(1),

                    Hostname =
                        reader.GetString(2),

                    Platform =
                        reader.GetString(3),

                    AgentVersion =
                        reader.GetString(4),

                    FirstSeen =
                        reader.GetString(5),

                    LastSeen =
                        reader.GetString(6),

                    Authorized =
                        reader.GetInt32(7) == 1
                }
            );
        }

        return agents;
    }

    // ==========================================
    // AUTORIZAR AGENT
    // ==========================================

    public bool AuthorizeAgent(
        string machineId)
    {
        return SetAuthorization(
            machineId,
            true
        );
    }

    // ==========================================
    // DESAUTORIZAR AGENT
    // ==========================================

    public bool RevokeAgent(
        string machineId)
    {
        return SetAuthorization(
            machineId,
            false
        );
    }

    // ==========================================
    // CAMBIAR AUTORIZACIÓN
    // ==========================================

    private bool SetAuthorization(
        string machineId,
        bool authorized)
    {
        using var connection =
            new SqliteConnection(
                _connectionString
            );

        connection.Open();

        using var command =
            connection.CreateCommand();

        command.CommandText =
            "UPDATE Agents " +
            "SET Authorized = $authorized " +
            "WHERE MachineId = $machineId;";

        command.Parameters.AddWithValue(
            "$authorized",
            authorized ? 1 : 0
        );

        command.Parameters.AddWithValue(
            "$machineId",
            machineId
        );

        int affectedRows =
            command.ExecuteNonQuery();

        if (affectedRows > 0)
        {
            Console.WriteLine(
                authorized
                    ? $"🔓 Equipo autorizado: {machineId}"
                    : $"🔒 Autorización revocada: {machineId}"
            );
        }

        return affectedRows > 0;
    }

    // ==========================================
    // COMPROBAR AUTORIZACIÓN
    // ==========================================

    public bool IsAgentAuthorized(
        string machineId)
    {
        using var connection =
            new SqliteConnection(
                _connectionString
            );

        connection.Open();

        using var command =
            connection.CreateCommand();

        command.CommandText =
            "SELECT Authorized " +
            "FROM Agents " +
            "WHERE MachineId = $machineId;";

        command.Parameters.AddWithValue(
            "$machineId",
            machineId
        );

        object? result =
            command.ExecuteScalar();

        if (result == null)
            return false;

        return Convert.ToInt32(result) == 1;
    }
}

// ==========================================
// AGENT REGISTRADO
// ==========================================

public sealed class RegisteredAgent
{
    public string MachineId { get; set; } =
        "";

    public string DisplayName { get; set; } =
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

    public bool Authorized { get; set; }
}