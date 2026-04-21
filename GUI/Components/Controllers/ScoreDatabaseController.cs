// <copyright file="ScoreDatabaseController.cs" company="Snake PS10">
// Copyright (c) 2026 Alex Waldmann & George Higbie. All rights reserved.
// </copyright>
// Authors: Alex Waldmann, George Higbie
// Date: 2026-04-21

using System;
using System.Collections.Generic;
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
    /// Persists live game/session data into the PS10 score database.
    /// </summary>
    public class ScoreDatabaseController
    {
        private readonly ILogger<ScoreDatabaseController> _logger;

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
            EnsureTablesExist();
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
            try
            {
                using var conn = new MySqlConnection(ConnectionString);
                conn.Open();

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

        /// <summary>
        /// Updates the game end time when the client disconnects.
        /// </summary>
        /// <param name="gameId">The game row ID to update.</param>
        /// <param name="endTime">Client-observed disconnect time.</param>
        public void TrySetGameEndTime(int gameId, DateTime endTime)
        {
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
            var sessions = new List<LiveGameSession>();
            try
            {
                using var conn = new MySqlConnection(ConnectionString);
                conn.Open();

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

            return sessions;
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
                        Host VARCHAR(128) NULL,
                        Port INT NULL,
                        IsActive TINYINT(1) NOT NULL DEFAULT 1,
                        PRIMARY KEY (GameId)
                    );";
                gamesCmd.ExecuteNonQuery();

                using var hostIndexCmd = conn.CreateCommand();
                hostIndexCmd.CommandText = @"
                    CREATE INDEX IF NOT EXISTS idx_games_active_host_port
                    ON Games (IsActive, Host, Port);";
                hostIndexCmd.ExecuteNonQuery();

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
    }
}
