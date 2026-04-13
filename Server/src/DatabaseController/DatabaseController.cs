// <copyright file="DatabaseController.cs" company="Snake PS9">
// Copyright (c) 2026 Alex Waldmann & George Higbie. All rights reserved.
// </copyright>
// Authors: Alex Waldmann, George Higbie
// Date: 2026-04-12

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using MySql.Data.MySqlClient;

namespace SnakeGame;

/// <summary>
/// Provides static methods for persisting and retrieving Snake game data
/// from a MySQL database. Uses parameterized queries to prevent SQL injection.
/// </summary>
public static class DatabaseController
{
    // ==================== Private Fields ====================

    private static string _password = "";

    private const string ConnectionBase = "server=atr.eng.utah.edu;database=snake;uid=travis;";

    // ==================== Public Methods ====================

    /// <summary>
    /// Persists a completed game and all participating snakes to the database.
    /// Errors are written to <see cref="Console"/> but do not propagate to the caller.
    /// </summary>
    /// <param name="theWorld">The world whose snakes were active during the game. Must not be null.</param>
    /// <param name="durationSeconds">Total game duration in seconds.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="theWorld"/> is null.</exception>
    public static void SaveGame(World theWorld, int durationSeconds)
    {
        ArgumentNullException.ThrowIfNull(theWorld);
        MySqlConnection val = new MySqlConnection(ConnectionBase + "password=" + _password);
        try
        {
            Console.WriteLine("opening connection");
            ((DbConnection)(object)val).Open();
            MySqlCommand obj = val.CreateCommand();
            ((DbCommand)(object)obj).CommandText = "insert into Games (duration) values (" + durationSeconds + ");";
            Console.WriteLine("adding game");
            ((DbCommand)(object)obj).ExecuteNonQuery();
            MySqlCommand obj2 = val.CreateCommand();
            ((DbCommand)(object)obj2).CommandText = "select LAST_INSERT_ID();";
            int num = -1;
            Console.WriteLine("getting game ID");
            MySqlDataReader val2 = obj2.ExecuteReader();
            try
            {
                ((DbDataReader)(object)val2).Read();
                num = ((DbDataReader)(object)val2).GetInt32(0);
            }
            finally
            {
                ((IDisposable)val2)?.Dispose();
            }
            Console.WriteLine("gameID = " + num);
            foreach (Snake item in new List<Snake>(theWorld.Snakes.Values))
            {
                MySqlCommand obj3 = val.CreateCommand();
                ((DbCommand)(object)obj3).CommandText = "insert ignore into Players(name) values(@name);";
                obj3.Parameters.AddWithValue("@name", (object)item.GetName());
                ((DbCommand)(object)obj3).ExecuteNonQuery();
                MySqlCommand obj4 = val.CreateCommand();
                ((DbCommand)(object)obj4).CommandText = "insert into PlayersGames values (@gID, (select pID from Players where name = @name), @score, @acc);";
                obj4.Parameters.AddWithValue("@gID", (object)num);
                obj4.Parameters.AddWithValue("@name", (object)item.GetName());
                obj4.Parameters.AddWithValue("@score", (object)item.GetScore());
                obj4.Parameters.AddWithValue("@acc", (object)0);
                Console.WriteLine("adding player to game");
                ((DbCommand)(object)obj4).ExecuteNonQuery();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("DB Error: " + ex.Message);
        }
        finally
        {
            ((IDisposable)val)?.Dispose();
        }
    }

    /// <summary>
    /// Retrieves all games from the database, keyed by game ID.
    /// Returns an empty dictionary if a database error occurs.
    /// </summary>
    /// <returns>
    /// A dictionary mapping each game ID to its <see cref="GameModel"/>, which includes
    /// the participating players and their scores.
    /// </returns>
    public static Dictionary<uint, GameModel> GetAllGames()
    {
        Dictionary<uint, GameModel> dictionary = new Dictionary<uint, GameModel>();
        string text = ConnectionBase + "password=" + _password;
        Console.WriteLine("connecting to database");
        MySqlConnection val = new MySqlConnection(text);
        try
        {
            Console.WriteLine("connecting");
            ((DbConnection)(object)val).Open();
            MySqlCommand obj = val.CreateCommand();
            ((DbCommand)(object)obj).CommandText = "select gID, duration, pID, name, Score, Accuracy from PlayersGames natural join Games natural join Players order by Score desc;";
            Console.WriteLine("getting all data");
            MySqlDataReader val2 = obj.ExecuteReader();
            try
            {
                while (((DbDataReader)(object)val2).Read())
                {
                    uint num = (uint)((DbDataReader)(object)val2)["gID"];
                    if (!dictionary.ContainsKey(num))
                    {
                        dictionary[num] = new GameModel(num, (((DbDataReader)(object)val2)["duration"] as uint?) ?? 0u);
                    }
                    GameModel gameModel = dictionary[num];
                    string? name = ((DbDataReader)(object)val2)["name"] as string;
                    uint value = (((DbDataReader)(object)val2)["Score"] as uint?) ?? 0u;
                    uint value2 = (((DbDataReader)(object)val2)["Accuracy"] as uint?) ?? 0u;
                    gameModel.AddPlayer(name ?? string.Empty, value, value2);
                }
            }
            finally
            {
                ((IDisposable)val2)?.Dispose();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("DB Error: " + ex.Message);
        }
        finally
        {
            ((IDisposable)val)?.Dispose();
        }
        return dictionary;
    }

    /// <summary>
    /// Retrieves all game sessions for a specific player, ordered by score descending.
    /// Returns an empty list if a database error occurs.
    /// </summary>
    /// <param name="name">The player's name. Must not be null.</param>
    /// <returns>A list of <see cref="SessionModel"/> records for the player.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="name"/> is null.</exception>
    public static List<SessionModel> GetPlayerGames(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        List<SessionModel> list = new List<SessionModel>();
        string text = ConnectionBase + "password=" + _password;
        Console.WriteLine("connecting to database");
        MySqlConnection val = new MySqlConnection(text);
        try
        {
            Console.WriteLine("connecting");
            ((DbConnection)(object)val).Open();
            MySqlCommand obj = val.CreateCommand();
            ((DbCommand)(object)obj).CommandText = "select gID, duration, Score, Accuracy from PlayersGames natural join Games natural join Players where name = @name order by Score desc;";
            obj.Parameters.AddWithValue("@name", (object)name);
            Console.WriteLine("getting all data");
            MySqlDataReader val2 = obj.ExecuteReader();
            try
            {
                while (((DbDataReader)(object)val2).Read())
                {
                    list.Add(new SessionModel((uint)((DbDataReader)(object)val2)[0], (uint)((DbDataReader)(object)val2)[1], (uint)((DbDataReader)(object)val2)[2], (uint)((DbDataReader)(object)val2)[3]));
                }
            }
            finally
            {
                ((IDisposable)val2)?.Dispose();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("DB Error: " + ex.Message);
        }
        finally
        {
            ((IDisposable)val)?.Dispose();
        }
        return list;
    }

    /// <summary>
    /// Retrieves all game rows for a specific player from the legacy <c>PlayersByGame</c> view.
    /// Returns an empty list if a database error occurs.
    /// </summary>
    /// <param name="player">The player name to filter by. Must not be null.</param>
    /// <returns>
    /// A list of rows; the first row contains column headers and subsequent rows contain data.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="player"/> is null.</exception>
    public static List<List<string>> GetPlayersGames(string player)
    {
        ArgumentNullException.ThrowIfNull(player);
        List<List<string>> list = new List<List<string>>();
        string text = ConnectionBase + "password=" + _password;
        Console.WriteLine("connecting to database");
        MySqlConnection val = new MySqlConnection(text);
        try
        {
            Console.WriteLine("connecting");
            ((DbConnection)(object)val).Open();
            MySqlCommand obj = val.CreateCommand();
            list.Add(new List<string>());
            list[0].Add("Game ID");
            list[0].Add("Player");
            list[0].Add("Score");
            list[0].Add("Accuracy (%)");
            list[0].Add("Game Duration (s)");
            ((DbCommand)(object)obj).CommandText = "select Games.GameID, Player, Score, Accuracy, Duration from PlayersByGame join Games on Games.GameID = PlayersByGame.GameID where Player = @player;";
            obj.Parameters.AddWithValue("@player", (object)player);
            Console.WriteLine("getting games for player " + player);
            MySqlDataReader val2 = obj.ExecuteReader();
            try
            {
                while (((DbDataReader)(object)val2).Read())
                {
                    list.Add(new List<string>());
                    for (int i = 0; i < ((DbDataReader)(object)val2).FieldCount; i++)
                    {
                        list[list.Count - 1].Add(((DbDataReader)(object)val2)[i].ToString() ?? string.Empty);
                    }
                }
            }
            finally
            {
                ((IDisposable)val2)?.Dispose();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("DB Error: " + ex.Message);
        }
        finally
        {
            ((IDisposable)val)?.Dispose();
        }
        return list;
    }

    /// <summary>
    /// Retrieves all player rows for a specific game from the legacy <c>PlayersByGame</c> view.
    /// Returns an empty list if a database error occurs.
    /// </summary>
    /// <param name="id">The game ID string to look up. Must not be null.</param>
    /// <returns>
    /// A list of rows; the first row contains column headers and subsequent rows contain data.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="id"/> is null.</exception>
    public static List<List<string>> GetGame(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        List<List<string>> list = new List<List<string>>();
        string text = ConnectionBase + "password=" + _password;
        Console.WriteLine("connecting to database");
        MySqlConnection val = new MySqlConnection(text);
        try
        {
            Console.WriteLine("connecting");
            ((DbConnection)(object)val).Open();
            MySqlCommand obj = val.CreateCommand();
            list.Add(new List<string>());
            list[0].Add("Player");
            list[0].Add("Score");
            list[0].Add("Accuracy (%)");
            ((DbCommand)(object)obj).CommandText = "select Player, Score, Accuracy from PlayersByGame where GameID = @id;";
            obj.Parameters.AddWithValue("@id", (object)id);
            Console.WriteLine("getting game ID " + id);
            MySqlDataReader val2 = obj.ExecuteReader();
            try
            {
                while (((DbDataReader)(object)val2).Read())
                {
                    list.Add(new List<string>());
                    for (int i = 0; i < ((DbDataReader)(object)val2).FieldCount; i++)
                    {
                        list[list.Count - 1].Add(((DbDataReader)(object)val2)[i].ToString() ?? string.Empty);
                    }
                }
            }
            finally
            {
                ((IDisposable)val2)?.Dispose();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("DB Error: " + ex.Message);
        }
        finally
        {
            ((IDisposable)val)?.Dispose();
        }
        return list;
    }

    /// <summary>
    /// Prompts the console user to enter a database password securely (characters are not echoed).
    /// Stores the password in the private static field for subsequent connection strings.
    /// </summary>
    public static void ReadPassword()
    {
        Console.WriteLine("Enter password:\n");
        string text = "";
        while (true)
        {
            ConsoleKeyInfo consoleKeyInfo = Console.ReadKey(intercept: true);
            if (consoleKeyInfo.Key == ConsoleKey.Enter)
            {
                break;
            }
            text += consoleKeyInfo.KeyChar;
        }
        _password = text;
    }
}

/// <summary>Represents a single player's score within a game record.</summary>
public class PlayerModel
{
    // ==================== Public Properties ====================

    /// <summary>The player's display name.</summary>
    public readonly string Name;

    /// <summary>The player's final score in this game.</summary>
    public readonly uint Score;

    /// <summary>The player's accuracy percentage (0–100).</summary>
    public readonly uint Accuracy;

    // ==================== Constructor ====================

    /// <summary>
    /// Initializes a new <see cref="PlayerModel"/>.
    /// </summary>
    /// <param name="n">Player name. Must not be null.</param>
    /// <param name="s">Score.</param>
    /// <param name="a">Accuracy (0–100).</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="n"/> is null.</exception>
    public PlayerModel(string n, uint s, uint a)
    {
        ArgumentNullException.ThrowIfNull(n);
        Name = n;
        Score = s;
        Accuracy = a;
    }
}

/// <summary>Represents a single game record with its duration and list of participants.</summary>
public class GameModel
{
    // ==================== Private Fields ====================

    private readonly List<PlayerModel> _players;

    // ==================== Public Properties ====================

    /// <summary>The unique database-assigned game ID.</summary>
    public readonly uint ID;

    /// <summary>Total duration of the game in seconds.</summary>
    public readonly uint Duration;

    // ==================== Constructor ====================

    /// <summary>
    /// Initializes a new <see cref="GameModel"/>.
    /// </summary>
    /// <param name="id">Database game ID.</param>
    /// <param name="d">Duration in seconds.</param>
    public GameModel(uint id, uint d)
    {
        ID = id;
        Duration = d;
        _players = new List<PlayerModel>();
    }

    // ==================== Public Methods ====================

    /// <summary>
    /// Adds a player result to this game record.
    /// </summary>
    /// <param name="name">Player display name. Must not be null.</param>
    /// <param name="score">Player's score.</param>
    /// <param name="accuracy">Accuracy percentage (0–100).</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="name"/> is null.</exception>
    public void AddPlayer(string name, uint score, uint accuracy)
    {
        ArgumentNullException.ThrowIfNull(name);
        _players.Add(new PlayerModel(name, score, accuracy));
    }

    /// <summary>Returns the list of player results for this game.</summary>
    /// <returns>A read-only view of the player results.</returns>
    public IReadOnlyList<PlayerModel> GetPlayers()
    {
        return _players.AsReadOnly();
    }
}

/// <summary>Represents summary statistics for a single game session queried per player.</summary>
public class SessionModel
{
    // ==================== Public Properties ====================

    /// <summary>The unique database-assigned game ID.</summary>
    public readonly uint GameID;

    /// <summary>Total game duration in seconds.</summary>
    public readonly uint Duration;

    /// <summary>The player's score in this session.</summary>
    public readonly uint Score;

    /// <summary>The player's accuracy percentage (0–100) in this session.</summary>
    public readonly uint Accuracy;

    // ==================== Constructor ====================

    /// <summary>
    /// Initializes a new <see cref="SessionModel"/>.
    /// </summary>
    /// <param name="gid">Game ID.</param>
    /// <param name="dur">Duration in seconds.</param>
    /// <param name="score">Player score.</param>
    /// <param name="acc">Accuracy percentage.</param>
    public SessionModel(uint gid, uint dur, uint score, uint acc)
    {
        GameID = gid;
        Duration = dur;
        Score = score;
        Accuracy = acc;
    }
}
