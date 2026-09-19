using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using System.IO;
namespace ControlLab.Manager.Services;

public sealed class AuthenticationService
{
    // =========================================================
    // CONFIGURACIÓN DE SEGURIDAD
    // =========================================================

    private const int SaltSize = 32;

    private const int HashSize = 32;

    // PBKDF2-HMAC-SHA256
    private const int Iterations = 600_000;

    // Sesión absoluta máxima
    private static readonly TimeSpan SessionLifetime =
        TimeSpan.FromHours(8);

    // La sesión expira si permanece inactiva este tiempo
    private static readonly TimeSpan IdleTimeout =
        TimeSpan.FromMinutes(30);

    private readonly string _connectionString;

    private readonly object _sessionLock = new();

    private readonly Dictionary<string, SessionInfo> _sessions =
        new(StringComparer.Ordinal);

    public AuthenticationService()
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

    // =========================================================
    // INICIALIZAR BASE DE DATOS
    // =========================================================

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
            CREATE TABLE IF NOT EXISTS AdminUsers
            (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,

                Username TEXT NOT NULL UNIQUE,

                PasswordHash TEXT NOT NULL,

                PasswordSalt TEXT NOT NULL,

                CreatedAt TEXT NOT NULL
            );
            """;

        command.ExecuteNonQuery();
    }

    // =========================================================
    // ¿EXISTE ADMINISTRADOR?
    // =========================================================

    public bool HasAdministrator()
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
            SELECT COUNT(*)
            FROM AdminUsers;
            """;

        long count =
            Convert.ToInt64(
                command.ExecuteScalar()
            );

        return count > 0;
    }

    // =========================================================
    // CREAR ADMINISTRADOR
    // =========================================================

    public bool CreateAdministrator(
        string username,
        string password,
        out string error)
    {
        error = "";

        username =
            username.Trim();

        // -----------------------------------------------------
        // VALIDAR USUARIO
        // -----------------------------------------------------

        if (string.IsNullOrWhiteSpace(username))
        {
            error =
                "El usuario es obligatorio.";

            return false;
        }

        if (username.Length < 3)
        {
            error =
                "El usuario debe tener al menos 3 caracteres.";

            return false;
        }

        if (username.Length > 50)
        {
            error =
                "El usuario no puede superar los 50 caracteres.";

            return false;
        }

        // -----------------------------------------------------
        // VALIDAR CONTRASEÑA
        // -----------------------------------------------------

        if (string.IsNullOrEmpty(password))
        {
            error =
                "La contraseña es obligatoria.";

            return false;
        }

        if (password.Length < 10)
        {
            error =
                "La contraseña debe tener al menos 10 caracteres.";

            return false;
        }

        if (password.Length > 200)
        {
            error =
                "La contraseña es demasiado larga.";

            return false;
        }

        // -----------------------------------------------------
        // EVITAR CREAR MÁS DE UN ADMINISTRADOR
        // -----------------------------------------------------

        using var connection =
            new SqliteConnection(
                _connectionString
            );

        connection.Open();

        using var countCommand =
            connection.CreateCommand();

        countCommand.CommandText =
            """
            SELECT COUNT(*)
            FROM AdminUsers;
            """;

        long existingUsers =
            Convert.ToInt64(
                countCommand.ExecuteScalar()
            );

        if (existingUsers > 0)
        {
            error =
                "Ya existe un administrador.";

            return false;
        }

        // -----------------------------------------------------
        // GENERAR SALT CRIPTOGRÁFICO
        // -----------------------------------------------------

        byte[] salt =
            RandomNumberGenerator.GetBytes(
                SaltSize
            );

        // -----------------------------------------------------
        // GENERAR HASH
        // -----------------------------------------------------

        byte[] hash =
            DerivePasswordHash(
                password,
                salt
            );

        string saltBase64 =
            Convert.ToBase64String(
                salt
            );

        string hashBase64 =
            Convert.ToBase64String(
                hash
            );

        // -----------------------------------------------------
        // GUARDAR
        // -----------------------------------------------------

        using var insertCommand =
            connection.CreateCommand();

        insertCommand.CommandText =
            """
            INSERT INTO AdminUsers
            (
                Username,
                PasswordHash,
                PasswordSalt,
                CreatedAt
            )
            VALUES
            (
                $username,
                $passwordHash,
                $passwordSalt,
                $createdAt
            );
            """;

        insertCommand.Parameters.AddWithValue(
            "$username",
            username
        );

        insertCommand.Parameters.AddWithValue(
            "$passwordHash",
            hashBase64
        );

        insertCommand.Parameters.AddWithValue(
            "$passwordSalt",
            saltBase64
        );

        insertCommand.Parameters.AddWithValue(
            "$createdAt",
            DateTime.UtcNow.ToString("O")
        );

        try
        {
            insertCommand.ExecuteNonQuery();

            Console.WriteLine(
                $"🔐 Administrador creado: {username}"
            );

            return true;
        }
        catch (SqliteException)
        {
            error =
                "No fue posible crear el administrador.";

            return false;
        }
    }

    // =========================================================
    // AUTENTICAR
    // =========================================================

    public AuthenticationResult Authenticate(
        string username,
        string password)
    {
        username =
            username.Trim();

        if (
            string.IsNullOrWhiteSpace(username) ||
            string.IsNullOrEmpty(password)
        )
        {
            return AuthenticationResult.Failed(
                "Usuario o contraseña incorrectos."
            );
        }

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
                Username,
                PasswordHash,
                PasswordSalt
            FROM AdminUsers
            WHERE Username = $username
            LIMIT 1;
            """;

        command.Parameters.AddWithValue(
            "$username",
            username
        );

        using var reader =
            command.ExecuteReader();

        if (!reader.Read())
        {
            return AuthenticationResult.Failed(
                "Usuario o contraseña incorrectos."
            );
        }

        string storedUsername =
            reader.GetString(0);

        string storedHash =
            reader.GetString(1);

        string storedSalt =
            reader.GetString(2);

        byte[] salt;

        byte[] expectedHash;

        try
        {
            salt =
                Convert.FromBase64String(
                    storedSalt
                );

            expectedHash =
                Convert.FromBase64String(
                    storedHash
                );
        }
        catch
        {
            return AuthenticationResult.Failed(
                "La información de autenticación está dañada."
            );
        }

        byte[] actualHash =
            DerivePasswordHash(
                password,
                salt
            );

        bool passwordValid =
            CryptographicOperations.FixedTimeEquals(
                actualHash,
                expectedHash
            );

        if (!passwordValid)
        {
            return AuthenticationResult.Failed(
                "Usuario o contraseña incorrectos."
            );
        }

        // =====================================================
        // CREAR SESIÓN
        // =====================================================

        string sessionToken =
            Convert.ToBase64String(
                RandomNumberGenerator.GetBytes(32)
            );

        DateTime createdAt =
            DateTime.UtcNow;

        DateTime expiresAt =
            createdAt.Add(
                SessionLifetime
            );

        var session =
            new SessionInfo
            {
                Token = sessionToken,

                Username = storedUsername,

                CreatedAt = createdAt,

                LastActivity = createdAt,

                ExpiresAt = expiresAt
            };

        lock (_sessionLock)
        {
            CleanupExpiredSessionsInternal();

            _sessions[sessionToken] =
                session;
        }

        Console.WriteLine(
            $"🟢 Inicio de sesión: {storedUsername}"
        );

        return AuthenticationResult.Success(
            sessionToken,
            storedUsername,
            expiresAt
        );
    }

    // =========================================================
    // VALIDAR SESIÓN
    // =========================================================

    public bool IsSessionValid(
        string? sessionToken)
    {
        if (
            string.IsNullOrWhiteSpace(
                sessionToken
            )
        )
        {
            return false;
        }

        lock (_sessionLock)
        {
            if (
                !_sessions.TryGetValue(
                    sessionToken,
                    out SessionInfo? session
                )
            )
            {
                return false;
            }

            DateTime now =
                DateTime.UtcNow;

            // -------------------------------------------------
            // SESIÓN EXPIRADA ABSOLUTAMENTE
            // -------------------------------------------------

            if (
                now >=
                session.ExpiresAt
            )
            {
                _sessions.Remove(
                    sessionToken
                );

                return false;
            }

            // -------------------------------------------------
            // SESIÓN INACTIVA
            // -------------------------------------------------

            if (
                now - session.LastActivity >
                IdleTimeout
            )
            {
                _sessions.Remove(
                    sessionToken
                );

                return false;
            }

            // -------------------------------------------------
            // ACTUALIZAR ACTIVIDAD
            // -------------------------------------------------

            session.LastActivity =
                now;

            return true;
        }
    }

    // =========================================================
    // OBTENER USUARIO DE LA SESIÓN
    // =========================================================

    public string? GetUsername(
        string? sessionToken)
    {
        if (
            string.IsNullOrWhiteSpace(
                sessionToken
            )
        )
        {
            return null;
        }

        lock (_sessionLock)
        {
            if (
                !_sessions.TryGetValue(
                    sessionToken,
                    out SessionInfo? session
                )
            )
            {
                return null;
            }

            DateTime now =
                DateTime.UtcNow;

            if (
                now >=
                session.ExpiresAt
            )
            {
                _sessions.Remove(
                    sessionToken
                );

                return null;
            }

            if (
                now - session.LastActivity >
                IdleTimeout
            )
            {
                _sessions.Remove(
                    sessionToken
                );

                return null;
            }

            session.LastActivity =
                now;

            return session.Username;
        }
    }

    // =========================================================
    // CERRAR SESIÓN
    // =========================================================

    public bool Logout(
        string? sessionToken)
    {
        if (
            string.IsNullOrWhiteSpace(
                sessionToken
            )
        )
        {
            return false;
        }

        lock (_sessionLock)
        {
            bool removed =
                _sessions.Remove(
                    sessionToken
                );

            if (removed)
            {
                Console.WriteLine(
                    "🔴 Sesión cerrada."
                );
            }

            return removed;
        }
    }

    // =========================================================
    // LIMPIAR SESIONES EXPIRADAS
    // =========================================================

    public void CleanupExpiredSessions()
    {
        lock (_sessionLock)
        {
            CleanupExpiredSessionsInternal();
        }
    }

    private void CleanupExpiredSessionsInternal()
    {
        DateTime now =
            DateTime.UtcNow;

        var expiredTokens =
            _sessions
                .Where(
                    pair =>
                        now >=
                            pair.Value.ExpiresAt
                        ||
                        now -
                            pair.Value.LastActivity >
                            IdleTimeout
                )
                .Select(
                    pair =>
                        pair.Key
                )
                .ToList();

        foreach (
            string token
            in expiredTokens
        )
        {
            _sessions.Remove(
                token
            );
        }
    }

    // =========================================================
    // PBKDF2
    // =========================================================

    private static byte[] DerivePasswordHash(
        string password,
        byte[] salt)
    {
        return Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            Iterations,
            HashAlgorithmName.SHA256,
            HashSize
        );
    }
}

// =============================================================
// INFORMACIÓN DE SESIÓN
// =============================================================

internal sealed class SessionInfo
{
    public string Token { get; set; } =
        "";

    public string Username { get; set; } =
        "";

    public DateTime CreatedAt { get; set; }

    public DateTime LastActivity { get; set; }

    public DateTime ExpiresAt { get; set; }
}

// =============================================================
// RESULTADO DE AUTENTICACIÓN
// =============================================================

public sealed class AuthenticationResult
{
    public bool Succeeded { get; }

    public string Message { get; }

    public string? SessionToken { get; }

    public string? Username { get; }

    public DateTime? ExpiresAt { get; }

    private AuthenticationResult(
        bool succeeded,
        string message,
        string? sessionToken,
        string? username,
        DateTime? expiresAt)
    {
        Succeeded =
            succeeded;

        Message =
            message;

        SessionToken =
            sessionToken;

        Username =
            username;

        ExpiresAt =
            expiresAt;
    }

    public static AuthenticationResult Success(
        string sessionToken,
        string username,
        DateTime expiresAt)
    {
        return new AuthenticationResult(
            true,
            "Autenticación correcta.",
            sessionToken,
            username,
            expiresAt
        );
    }

    public static AuthenticationResult Failed(
        string message)
    {
        return new AuthenticationResult(
            false,
            message,
            null,
            null,
            null
        );
    }
}