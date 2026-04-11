using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using MySql.Data.MySqlClient;

namespace SnakeGame;

public static class DatabaseController
{
	private static string password = "";

	private const string connectionBase = "server=atr.eng.utah.edu;database=snake;uid=travis;";

	public static void SaveGame(World theWorld, int durationSeconds)
	{
		MySqlConnection val = new MySqlConnection("server=atr.eng.utah.edu;database=snake;uid=travis;password=" + password);
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
			Console.WriteLine("Error: " + ex.Message);
		}
		finally
		{
			((IDisposable)val)?.Dispose();
		}
	}

	public static Dictionary<uint, GameModel> GetAllGames()
	{
		Dictionary<uint, GameModel> dictionary = new Dictionary<uint, GameModel>();
		string text = "server=atr.eng.utah.edu;database=snake;uid=travis;password=" + password;
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
						dictionary[num] = new GameModel(num, (((DbDataReader)(object)val2)["duration"] as uint?).Value);
					}
					GameModel gameModel = dictionary[num];
					string name = ((DbDataReader)(object)val2)["name"] as string;
					uint value = (((DbDataReader)(object)val2)["Score"] as uint?).Value;
					uint value2 = (((DbDataReader)(object)val2)["Accuracy"] as uint?).Value;
					gameModel.AddPlayer(name, value, value2);
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

	public static List<SessionModel> GetPlayerGames(string name)
	{
		List<SessionModel> list = new List<SessionModel>();
		string text = "server=atr.eng.utah.edu;database=snake;uid=travis;password=" + password;
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

	public static List<List<string>> GetPlayersGames(string player)
	{
		List<List<string>> list = new List<List<string>>();
		string text = "server=atr.eng.utah.edu;database=snake;uid=travis;password=" + password;
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
			((DbCommand)(object)obj).CommandText = "select Games.GameID, Player, Score, Accuracy, Duration from PlayersByGame join Games on Games.GameID = PlayersByGame.GameID where Player = \"" + player + "\";";
			Console.WriteLine("getting games for player " + player);
			MySqlDataReader val2 = obj.ExecuteReader();
			try
			{
				while (((DbDataReader)(object)val2).Read())
				{
					list.Add(new List<string>());
					for (int i = 0; i < ((DbDataReader)(object)val2).FieldCount; i++)
					{
						list[list.Count - 1].Add(((DbDataReader)(object)val2)[i].ToString());
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
			Console.WriteLine("Error: " + ex.Message);
		}
		finally
		{
			((IDisposable)val)?.Dispose();
		}
		return list;
	}

	public static List<List<string>> GetGame(string id)
	{
		List<List<string>> list = new List<List<string>>();
		string text = "server=atr.eng.utah.edu;database=snake;uid=travis;password=" + password;
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
			((DbCommand)(object)obj).CommandText = "select Player, Score, Accuracy from PlayersByGame where GameID = " + id + ";";
			Console.WriteLine("getting game ID " + id);
			MySqlDataReader val2 = obj.ExecuteReader();
			try
			{
				while (((DbDataReader)(object)val2).Read())
				{
					list.Add(new List<string>());
					for (int i = 0; i < ((DbDataReader)(object)val2).FieldCount; i++)
					{
						list[list.Count - 1].Add(((DbDataReader)(object)val2)[i].ToString());
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
			Console.WriteLine("Error: " + ex.Message);
		}
		finally
		{
			((IDisposable)val)?.Dispose();
		}
		return list;
	}

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
		password = text;
	}
}
public class PlayerModel
{
	public readonly string Name;

	public readonly uint Score;

	public readonly uint Accuracy;

	public PlayerModel(string n, uint s, uint a)
	{
		Name = n;
		Score = s;
		Accuracy = a;
	}
}
public class GameModel
{
	public readonly uint ID;

	public readonly uint Duration;

	private List<PlayerModel> players;

	public GameModel(uint id, uint d)
	{
		Duration = d;
		players = new List<PlayerModel>();
	}

	public void AddPlayer(string name, uint score, uint accuracy)
	{
		players.Add(new PlayerModel(name, score, accuracy));
	}

	public List<PlayerModel> GetPlayers()
	{
		return players;
	}
}
public class SessionModel
{
	public readonly uint GameID;

	public readonly uint Duration;

	public readonly uint Score;

	public readonly uint Accuracy;

	public SessionModel(uint gid, uint dur, uint score, uint acc)
	{
		GameID = gid;
		Duration = dur;
		Score = score;
		Accuracy = acc;
	}
}
