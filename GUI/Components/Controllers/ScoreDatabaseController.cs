// <copyright file="ScoreDatabaseController.cs" company="Snake PS10">
// Copyright (c) 2026 Alex Waldmann & George Higbie. All rights reserved.
// </copyright>
// Authors: Alex Waldmann, George Higbie
// Date: 2026-04-21

using System;
using MySql.Data.MySqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GUI.Components.Controllers
{
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
        /// <returns>The inserted game ID, or null if the insert failed.</returns>
        public int? TryCreateGame(DateTime startTime)
        {
            try
            {
                using var conn = new MySqlConnection(ConnectionString);
                conn.Open();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO Games (StartTime, EndTime)
                    VALUES (@startTime, NULL);
                    SELECT LAST_INSERT_ID();";
                cmd.Parameters.AddWithValue("@startTime", startTime.ToString("yyyy-MM-dd H:mm:ss"));

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
                    SET EndTime = @endTime
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
