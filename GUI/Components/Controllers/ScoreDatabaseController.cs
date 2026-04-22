// <copyright file="ScoreDatabaseController.cs" company="Snake PS10">
// Copyright (c) 2026 Alex Waldmann & George Higbie. All rights reserved.
// </copyright>
// Authors: Alex Waldmann, George Higbie
// Date: 2026-04-21

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using MySql.Data.MySqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GUI.Components.Controllers
{
    /// <summary>
    /// Lightweight descriptor for a currently open game endpoint advertised via SQL.
    /// </summary>
    public sealed record LiveGameSession(int GameId, string Host, int Port, DateTime StartTime);

    /// <summary>
    /// Single all-time score row used for global leaderboards.
    /// </summary>
    public sealed record GlobalHighScoreEntry(string PlayerName, int MaxScore, DateTime EnterTime, int GameId, int PlayerId);

    /// <summary>
    /// Persists live game/session data into the PS10 score database.
    /// </summary>
    public class ScoreDatabaseController
    {
        private readonly ILogger<ScoreDatabaseController> _logger;
        private readonly HttpClient? _relayClient;

        // Assignment requirement: keep the connection string directly in source.
        /// <summary>
        /// MySQL connection string used by both the client logger and score web server.
        /// </summary>
        public const string ConnectionString = "server=atr.eng.utah.edu;" +
                                               "database=u1512040;" +
                                               "uid=u1512040;" +
                                               "password=f;" +
                                               "Connection Timeout=3;" +
                                               "Default Command Timeout=3";

        /// <summary>
        /// Initializes the controller and creates required tables if they do not exist.
        /// </summary>
        public ScoreDatabaseController(ILogger<ScoreDatabaseController>? logger = null)
        {
            _logger = logger ?? NullLogger<ScoreDatabaseController>.Instance;

            string? relayBaseUrl = Environment.GetEnvironmentVariable("SCORE_RELAY_BASE_URL");
            if (!string.IsNullOrWhiteSpace(relayBaseUrl)
                && Uri.TryCreate(relayBaseUrl.Trim(), UriKind.Absolute, out Uri? relayUri))
            {
                _relayClient = new HttpClient
                {
                    BaseAddress = relayUri,
                    Timeout = TimeSpan.FromSeconds(3),
                };
                _logger.LogInformation("Score DB relay enabled via {RelayBaseUrl}.", relayUri);
            }

            if (_relayClient == null)
            {
                EnsureTablesExist();
            }
        }

        /// <summary>
        /// Inserts a new game row and returns the generated game ID.
        /// </summary>
        /// <param name="startTime">Client-observed connection time for the game.</param>
        /// <param name="host">Advertised host for joining this game session.</param>
        /// <param name="port">Advertised port for joining this game session.</param>
        /// <returns>The inserted game ID, or null if the insert failed.</returns>
        public int? TryCreateGame(DateTime startTime, string? host = null, int? port = null)
        {
            if (_relayClient != null)
            {
                return RelayCreateGame(startTime, host, port);
            }

            try
            {
                using var conn = new MySqlConnection(ConnectionString);
                conn.Open();

                CloseActiveGamesForEndpoint(conn, host, port, startTime);

                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO Games (StartTime, EndTime, Host, Port, IsActive)
                    VALUES (@startTime, NULL, @host, @port, 1);
                    SELECT LAST_INSERT_ID();";
                cmd.Parameters.AddWithValue("@startTime", startTime.ToString("yyyy-MM-dd H:mm:ss"));
                cmd.Parameters.AddWithValue("@host", host);
                cmd.Parameters.AddWithValue("@port", port);

                var result = cmd.ExecuteScalar();
                var gameId = result != null ? Convert.ToInt32(result) : (int?)null;
                if (gameId.HasValue)
                {
                    _logger.LogInformation("Created game session row {GameId} at {StartTime}.", gameId.Value, startTime);
                }

                return gameId;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to create game session row.");
                return null;
            }
        }

        private static void CloseActiveGamesForEndpoint(MySqlConnection conn, string? host, int? port, DateTime closeTime)
        {
            if (string.IsNullOrWhiteSpace(host) || !port.HasValue)
            {
                return;
            }

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE Games
                SET EndTime = COALESCE(EndTime, @closeTime),
                    IsActive = 0
                WHERE IsActive = 1
                  AND Host = @host
                  AND Port = @port;";
            cmd.Parameters.AddWithValue("@closeTime", closeTime.ToString("yyyy-MM-dd H:mm:ss"));
            cmd.Parameters.AddWithValue("@host", host);
            cmd.Parameters.AddWithValue("@port", port.Value);
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// Updates the game end time when the client disconnects.
        /// </summary>
        /// <param name="gameId">The game row ID to update.</param>
        /// <param name="endTime">Client-observed disconnect time.</param>
        public void TrySetGameEndTime(int gameId, DateTime endTime)
        {
            if (_relayClient != null)
            {
                RelayPost("/api/games/end", new SetGameEndRequest(gameId, endTime));
                return;
            }

            try
            {
                using var conn = new MySqlConnection(ConnectionString);
                conn.Open();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    UPDATE Games
                    SET EndTime = @endTime,
                        IsActive = 0
                    WHERE GameId = @gameId;";
                cmd.Parameters.AddWithValue("@endTime", endTime.ToString("yyyy-MM-dd H:mm:ss"));
                cmd.Parameters.AddWithValue("@gameId", gameId);
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to set end time for game {GameId}.", gameId);
            }
        }

        /// <summary>
        /// Inserts a player row for first sighting within a game.
        /// </summary>
        /// <param name="gameId">Owning game ID.</param>
        /// <param name="playerId">Snake ID from the game server.</param>
        /// <param name="name">Snake name from the game server.</param>
        /// <param name="maxScore">Current max score at first sighting.</param>
        /// <param name="enterTime">Client-observed first-seen time.</param>
        public void TryInsertPlayer(int gameId, int playerId, string name, int maxScore, DateTime enterTime)
        {
            if (_relayClient != null)
            {
                RelayPost("/api/players/upsert", new UpsertPlayerRequest(gameId, playerId, name, maxScore, enterTime));
                return;
            }

            try
            {
                using var conn = new MySqlConnection(ConnectionString);
                conn.Open();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO Players (GameId, PlayerId, PlayerName, MaxScore, EnterTime, LeaveTime)
                    VALUES (@gameId, @playerId, @name, @maxScore, @enterTime, NULL)
                    ON DUPLICATE KEY UPDATE
                        PlayerName = VALUES(PlayerName),
                        MaxScore = GREATEST(MaxScore, VALUES(MaxScore));";
                cmd.Parameters.AddWithValue("@gameId", gameId);
                cmd.Parameters.AddWithValue("@playerId", playerId);
                cmd.Parameters.AddWithValue("@name", name);
                cmd.Parameters.AddWithValue("@maxScore", maxScore);
                cmd.Parameters.AddWithValue("@enterTime", enterTime.ToString("yyyy-MM-dd H:mm:ss"));
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to insert or update player {PlayerId} for game {GameId}.", playerId, gameId);
            }
        }

        /// <summary>
        /// Raises a player's max score when a higher score is observed.
        /// </summary>
        /// <param name="gameId">Owning game ID.</param>
        /// <param name="playerId">Snake ID from the game server.</param>
        /// <param name="maxScore">New candidate max score.</param>
        public void TryUpdatePlayerMaxScore(int gameId, int playerId, int maxScore)
        {
            if (_relayClient != null)
            {
                RelayPost("/api/players/score", new UpdatePlayerScoreRequest(gameId, playerId, maxScore));
                return;
            }

            try
            {
                using var conn = new MySqlConnection(ConnectionString);
                conn.Open();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    UPDATE Players
                    SET MaxScore = GREATEST(MaxScore, @maxScore)
                    WHERE GameId = @gameId
                      AND PlayerId = @playerId;";
                cmd.Parameters.AddWithValue("@gameId", gameId);
                cmd.Parameters.AddWithValue("@playerId", playerId);
                cmd.Parameters.AddWithValue("@maxScore", maxScore);
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update max score for player {PlayerId} in game {GameId}.", playerId, gameId);
            }
        }

        /// <summary>
        /// Sets the leave time for a player when the client sees dc=true or disconnects.
        /// </summary>
        /// <param name="gameId">Owning game ID.</param>
        /// <param name="playerId">Snake ID from the game server.</param>
        /// <param name="leaveTime">Client-observed leave time.</param>
        public void TrySetPlayerLeaveTime(int gameId, int playerId, DateTime leaveTime)
        {
            if (_relayClient != null)
            {
                RelayPost("/api/players/leave", new SetPlayerLeaveRequest(gameId, playerId, leaveTime));
                return;
            }

            try
            {
                using var conn = new MySqlConnection(ConnectionString);
                conn.Open();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    UPDATE Players
                    SET LeaveTime = @leaveTime
                    WHERE GameId = @gameId
                      AND PlayerId = @playerId;";
                cmd.Parameters.AddWithValue("@leaveTime", leaveTime.ToString("yyyy-MM-dd H:mm:ss"));
                cmd.Parameters.AddWithValue("@gameId", gameId);
                cmd.Parameters.AddWithValue("@playerId", playerId);
                cmd.ExecuteNonQuery();

                TryCloseGameIfNoActivePlayers(conn, gameId, leaveTime);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to set leave time for player {PlayerId} in game {GameId}.", playerId, gameId);
            }
        }

        /// <summary>
        /// Returns active games that advertise a host/port endpoint for joining.
        /// </summary>
        public IReadOnlyList<LiveGameSession> GetOpenGameSessions()
        {
            if (_relayClient != null)
            {
                return RelayGetOpenGameSessions();
            }

            var sessions = new List<LiveGameSession>();
            try
            {
                using var conn = new MySqlConnection(ConnectionString);
                conn.Open();

                CloseAllEmptyActiveGames(conn, DateTime.Now);
                CloseDuplicateActiveEndpointGames(conn, DateTime.Now);

                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT GameId, Host, Port, StartTime
                    FROM Games
                    WHERE IsActive = 1
                      AND Host IS NOT NULL
                      AND Host <> ''
                      AND Port IS NOT NULL
                    ORDER BY StartTime DESC
                    LIMIT 200;";

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var gameId = reader.GetInt32("GameId");
                    var host = reader.GetString("Host");
                    var port = reader.GetInt32("Port");
                    var startTime = reader.GetDateTime("StartTime");
                    sessions.Add(new LiveGameSession(gameId, host, port, startTime));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to query open game sessions.");
            }

            return PruneUnreachableLoopbackSessions(sessions);
        }

        /// <summary>
        /// Returns the highest all-time scores across all games.
        /// </summary>
        /// <param name="limit">Maximum number of rows to return.</param>
        public IReadOnlyList<GlobalHighScoreEntry> GetGlobalTopScores(int limit = 10)
        {
            int safeLimit = Math.Clamp(limit, 1, 100);

            if (_relayClient != null)
            {
                return RelayGetGlobalTopScores(safeLimit);
            }

            var topScores = new List<GlobalHighScoreEntry>();
            try
            {
                using var conn = new MySqlConnection(ConnectionString);
                conn.Open();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = $@"
                    SELECT PlayerName, MaxScore, EnterTime, GameId, PlayerId
                    FROM Players
                    ORDER BY MaxScore DESC, EnterTime ASC, EntryId ASC
                    LIMIT {safeLimit};";

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var playerName = reader.GetString("PlayerName");
                    var maxScore = reader.GetInt32("MaxScore");
                    var enterTime = reader.GetDateTime("EnterTime");
                    var gameId = reader.GetInt32("GameId");
                    var playerId = reader.GetInt32("PlayerId");
                    topScores.Add(new GlobalHighScoreEntry(playerName, maxScore, enterTime, gameId, playerId));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to query global top scores.");
            }

            return topScores;
        }

        private static void TryCloseGameIfNoActivePlayers(MySqlConnection conn, int gameId, DateTime closeTime)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE Games g
                SET EndTime = COALESCE(EndTime, @closeTime),
                    IsActive = 0
                WHERE g.GameId = @gameId
                  AND g.IsActive = 1
                  AND NOT EXISTS (
                      SELECT 1
                      FROM Players p
                      WHERE p.GameId = g.GameId
                        AND p.LeaveTime IS NULL
                  );";
            cmd.Parameters.AddWithValue("@closeTime", closeTime.ToString("yyyy-MM-dd H:mm:ss"));
            cmd.Parameters.AddWithValue("@gameId", gameId);
            cmd.ExecuteNonQuery();
        }

        private static void CloseAllEmptyActiveGames(MySqlConnection conn, DateTime closeTime)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE Games g
                SET EndTime = COALESCE(EndTime, @closeTime),
                    IsActive = 0
                WHERE g.IsActive = 1
                  AND NOT EXISTS (
                      SELECT 1
                      FROM Players p
                      WHERE p.GameId = g.GameId
                        AND p.LeaveTime IS NULL
                  );";
            cmd.Parameters.AddWithValue("@closeTime", closeTime.ToString("yyyy-MM-dd H:mm:ss"));
            cmd.ExecuteNonQuery();
        }

        private static void CloseDuplicateActiveEndpointGames(MySqlConnection conn, DateTime closeTime)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE Games g
                JOIN (
                    SELECT Host, Port, MAX(GameId) AS KeepGameId
                    FROM Games
                    WHERE IsActive = 1
                      AND Host IS NOT NULL
                      AND Host <> ''
                      AND Port IS NOT NULL
                    GROUP BY Host, Port
                ) keepers
                    ON keepers.Host = g.Host
                   AND keepers.Port = g.Port
                SET g.EndTime = COALESCE(g.EndTime, @closeTime),
                    g.IsActive = 0
                WHERE g.IsActive = 1
                  AND g.GameId <> keepers.KeepGameId;";
            cmd.Parameters.AddWithValue("@closeTime", closeTime.ToString("yyyy-MM-dd H:mm:ss"));
            cmd.ExecuteNonQuery();
        }

        private IReadOnlyList<LiveGameSession> PruneUnreachableLoopbackSessions(IReadOnlyList<LiveGameSession> sessions)
        {
            if (sessions.Count == 0)
            {
                return sessions;
            }

            DateTime now = DateTime.Now;
            var reachable = new List<LiveGameSession>(sessions.Count);
            foreach (var session in sessions)
            {
                if (IsTcpEndpointReachable(session.Host, session.Port))
                {
                    reachable.Add(session);
                    continue;
                }

                // Keep very recent sessions during startup races.
                if ((now - session.StartTime) < TimeSpan.FromSeconds(20))
                {
                    reachable.Add(session);
                    continue;
                }

                TrySetGameEndTime(session.GameId, now);
            }

            return reachable;
        }

        private static bool IsTcpEndpointReachable(string host, int port)
        {
            try
            {
                using var client = new TcpClient();
                var connectTask = client.ConnectAsync(host, port);
                bool completed = connectTask.Wait(TimeSpan.FromMilliseconds(150));
                return completed && client.Connected;
            }
            catch
            {
                return false;
            }
        }

        private int? RelayCreateGame(DateTime startTime, string? host, int? port)
        {
            if (_relayClient == null)
            {
                return null;
            }

            try
            {
                string payload = JsonSerializer.Serialize(new CreateGameRequest(startTime, host, port));
                using var content = new StringContent(payload, Encoding.UTF8, "application/json");
                using HttpResponseMessage response = _relayClient.PostAsync("/api/games/start", content).GetAwaiter().GetResult();
                response.EnsureSuccessStatusCode();

                string json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                CreateGameResponse? result = JsonSerializer.Deserialize<CreateGameResponse>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                });
                return result?.GameId;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to create game via score relay.");
                return null;
            }
        }

        private void RelayPost<TPayload>(string path, TPayload payload)
        {
            if (_relayClient == null)
            {
                return;
            }

            try
            {
                string json = JsonSerializer.Serialize(payload);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using HttpResponseMessage response = _relayClient.PostAsync(path, content).GetAwaiter().GetResult();
                response.EnsureSuccessStatusCode();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to post score event to relay endpoint {Path}.", path);
            }
        }

        private IReadOnlyList<LiveGameSession> RelayGetOpenGameSessions()
        {
            if (_relayClient == null)
            {
                return [];
            }

            try
            {
                using HttpResponseMessage response = _relayClient.GetAsync("/api/games/open").GetAwaiter().GetResult();
                response.EnsureSuccessStatusCode();

                string json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                List<LiveGameSession>? sessions = JsonSerializer.Deserialize<List<LiveGameSession>>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                });
                return PruneUnreachableLoopbackSessions(sessions ?? []);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch open game sessions from score relay.");
                return [];
            }
        }

        private IReadOnlyList<GlobalHighScoreEntry> RelayGetGlobalTopScores(int limit)
        {
            if (_relayClient == null)
            {
                return [];
            }

            try
            {
                using HttpResponseMessage response = _relayClient.GetAsync($"/api/scores/top?limit={limit}").GetAwaiter().GetResult();
                response.EnsureSuccessStatusCode();

                string json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                List<GlobalHighScoreEntry>? scores = JsonSerializer.Deserialize<List<GlobalHighScoreEntry>>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                });
                return scores ?? [];
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch global top scores from score relay.");
                return [];
            }
        }

        private void EnsureTablesExist()
        {
            try
            {
                using var conn = new MySqlConnection(ConnectionString);
                conn.Open();

                using var gamesCmd = conn.CreateCommand();
                gamesCmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS Games (
                        GameId INT NOT NULL AUTO_INCREMENT,
                        StartTime DATETIME NOT NULL,
                        EndTime DATETIME NULL,
                        PRIMARY KEY (GameId)
                    );";
                gamesCmd.ExecuteNonQuery();

                EnsureColumnExists(conn, "Games", "Host", "ALTER TABLE Games ADD COLUMN Host VARCHAR(128) NULL;");
                EnsureColumnExists(conn, "Games", "Port", "ALTER TABLE Games ADD COLUMN Port INT NULL;");
                EnsureColumnExists(conn, "Games", "IsActive", "ALTER TABLE Games ADD COLUMN IsActive TINYINT(1) NOT NULL DEFAULT 1;");

                using var gamesBackfillCmd = conn.CreateCommand();
                gamesBackfillCmd.CommandText = @"
                    UPDATE Games
                    SET IsActive = CASE WHEN EndTime IS NULL THEN 1 ELSE 0 END;";
                gamesBackfillCmd.ExecuteNonQuery();

                EnsureIndexExists(
                    conn,
                    "idx_games_active_host_port",
                    "CREATE INDEX idx_games_active_host_port ON Games (IsActive, Host, Port);");

                using var playersCmd = conn.CreateCommand();
                playersCmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS Players (
                        EntryId INT NOT NULL AUTO_INCREMENT,
                        GameId INT NOT NULL,
                        PlayerId INT NOT NULL,
                        PlayerName VARCHAR(64) NOT NULL,
                        MaxScore INT NOT NULL,
                        EnterTime DATETIME NOT NULL,
                        LeaveTime DATETIME NULL,
                        PRIMARY KEY (EntryId),
                        UNIQUE KEY uq_game_player (GameId, PlayerId),
                        INDEX idx_game (GameId)
                    );";
                playersCmd.ExecuteNonQuery();
                _logger.LogInformation("Verified score tables exist (Games, Players).");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to verify or create score tables.");
            }
        }

        private static void EnsureColumnExists(MySqlConnection conn, string tableName, string columnName, string alterSql)
        {
            if (ColumnExists(conn, tableName, columnName))
            {
                return;
            }

            using var cmd = conn.CreateCommand();
            cmd.CommandText = alterSql;
            cmd.ExecuteNonQuery();
        }

        private static bool ColumnExists(MySqlConnection conn, string tableName, string columnName)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT COUNT(*)
                FROM information_schema.columns
                WHERE table_schema = DATABASE()
                  AND table_name = @table
                  AND column_name = @column;";
            cmd.Parameters.AddWithValue("@table", tableName);
            cmd.Parameters.AddWithValue("@column", columnName);

            return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
        }

        private static void EnsureIndexExists(MySqlConnection conn, string indexName, string createSql)
        {
            using var checkCmd = conn.CreateCommand();
            checkCmd.CommandText = @"
                SELECT COUNT(*)
                FROM information_schema.statistics
                WHERE table_schema = DATABASE()
                  AND table_name = 'Games'
                  AND index_name = @indexName;";
            checkCmd.Parameters.AddWithValue("@indexName", indexName);

            if (Convert.ToInt64(checkCmd.ExecuteScalar()) > 0)
            {
                return;
            }

            using var createCmd = conn.CreateCommand();
            createCmd.CommandText = createSql;
            createCmd.ExecuteNonQuery();
        }

        private sealed record CreateGameRequest(DateTime StartTime, string? Host, int? Port);
        private sealed record CreateGameResponse(int? GameId);
        private sealed record SetGameEndRequest(int GameId, DateTime EndTime);
        private sealed record UpsertPlayerRequest(int GameId, int PlayerId, string PlayerName, int MaxScore, DateTime EnterTime);
        private sealed record UpdatePlayerScoreRequest(int GameId, int PlayerId, int MaxScore);
        private sealed record SetPlayerLeaveRequest(int GameId, int PlayerId, DateTime LeaveTime);
    }
}
