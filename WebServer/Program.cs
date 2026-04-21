using System.Net;
using System.Net.Sockets;
using System.Text;
using MySql.Data.MySqlClient;

const int defaultPort = 8080;
int port = args.Length > 0 && int.TryParse(args[0], out int parsedPort) ? parsedPort : defaultPort;

ScoreDataAccess.EnsureTablesExist();

TcpListener listener = new(IPAddress.Any, port);
listener.Start();
Console.WriteLine($"WebServer listening on http://localhost:{port}");

while (true)
{
	TcpClient client = listener.AcceptTcpClient();
	_ = Task.Run(() => HandleClient(client));
}

static void HandleClient(TcpClient client)
{
	using (client)
	using (NetworkStream stream = client.GetStream())
	using (StreamReader reader = new(stream, new UTF8Encoding(false), leaveOpen: true))
	{
		string? requestLine = reader.ReadLine();
		if (string.IsNullOrWhiteSpace(requestLine))
		{
			return;
		}

		while (!string.IsNullOrEmpty(reader.ReadLine()))
		{
			// Consume headers.
		}

		string[] parts = requestLine.Split(' ');
		if (parts.Length < 2)
		{
			WriteResponse(stream, "400 Bad Request", "<html><h3>Bad Request</h3></html>");
			return;
		}

		string method = parts[0];
		string target = parts[1];
		if (!string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
		{
			WriteResponse(stream, "405 Method Not Allowed", "<html><h3>Method Not Allowed</h3></html>");
			return;
		}

		try
		{
			RouteRequest(stream, target);
		}
		catch
		{
			WriteResponse(stream, "500 Internal Server Error", "<html><h3>Internal Server Error</h3></html>");
		}
	}
}

static void RouteRequest(NetworkStream stream, string target)
{
	Uri uri = new($"http://localhost{target}");

	if (uri.AbsolutePath == "/")
	{
		string body = "<html><h3>Welcome to the Snake Games Database!</h3><a href=\"/games\">View Games</a></html>";
		WriteResponse(stream, "200 OK", body);
		return;
	}

	if (uri.AbsolutePath == "/games")
	{
		int? gameId = GetGameId(uri.Query);
		string body = gameId.HasValue ? BuildSingleGamePage(gameId.Value) : BuildAllGamesPage();
		WriteResponse(stream, "200 OK", body);
		return;
	}

	WriteResponse(stream, "404 Not Found", "<html><h3>Page Not Found</h3></html>");
}

static int? GetGameId(string query)
{
	if (string.IsNullOrWhiteSpace(query))
	{
		return null;
	}

	string trimmed = query.TrimStart('?');
	string[] pairs = trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries);
	foreach (string pair in pairs)
	{
		string[] kv = pair.Split('=', 2);
		if (kv.Length == 2 && kv[0] == "gid" && int.TryParse(kv[1], out int gameId))
		{
			return gameId;
		}
	}

	return null;
}

static string BuildAllGamesPage()
{
	List<GameRow> games = ScoreDataAccess.GetGames();
	StringBuilder html = new();
	html.Append("<html><table border=\"1\"><thead><tr><td>ID</td><td>Start</td><td>End</td></tr></thead><tbody>");
	foreach (GameRow game in games)
	{
		html.Append("<tr>");
		html.Append($"<td><a href=\"/games?gid={game.GameId}\">{game.GameId}</a></td>");
		html.Append($"<td>{WebUtility.HtmlEncode(game.StartTime.ToString("G"))}</td>");
		html.Append($"<td>{WebUtility.HtmlEncode(game.EndTime?.ToString("G") ?? "")}</td>");
		html.Append("</tr>");
	}

	html.Append("</tbody></table></html>");
	return html.ToString();
}

static string BuildSingleGamePage(int gameId)
{
	List<PlayerRow> players = ScoreDataAccess.GetPlayersForGame(gameId);
	StringBuilder html = new();
	html.Append($"<html><h3>Stats for Game {gameId}</h3>");
	html.Append("<table border=\"1\"><thead><tr><td>Player ID</td><td>Player Name</td><td>Max Score</td><td>Enter Time</td><td>Leave Time</td></tr></thead><tbody>");

	foreach (PlayerRow player in players)
	{
		html.Append("<tr>");
		html.Append($"<td>{player.PlayerId}</td>");
		html.Append($"<td>{WebUtility.HtmlEncode(player.PlayerName)}</td>");
		html.Append($"<td>{player.MaxScore}</td>");
		html.Append($"<td>{WebUtility.HtmlEncode(player.EnterTime.ToString("G"))}</td>");
		html.Append($"<td>{WebUtility.HtmlEncode(player.LeaveTime?.ToString("G") ?? "")}</td>");
		html.Append("</tr>");
	}

	html.Append("</tbody></table></html>");
	return html.ToString();
}

static void WriteResponse(NetworkStream stream, string status, string body)
{
	int contentLength = Encoding.UTF8.GetByteCount(body);
	string header =
		$"HTTP/1.1 {status}\r\n" +
		"Connection: close\r\n" +
		"Content-Type: text/html; charset=UTF-8\r\n" +
		$"Content-Length: {contentLength}\r\n\r\n";

	byte[] headerBytes = Encoding.UTF8.GetBytes(header);
	byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
	stream.Write(headerBytes, 0, headerBytes.Length);
	stream.Write(bodyBytes, 0, bodyBytes.Length);
}

internal sealed record GameRow(int GameId, DateTime StartTime, DateTime? EndTime);

internal sealed record PlayerRow(int PlayerId, string PlayerName, int MaxScore, DateTime EnterTime, DateTime? LeaveTime);

internal static class ScoreDataAccess
{
	private const string ConnectionString = "server=atr.eng.utah.edu;" +
											"database=u1512040;" +
											"uid=u1512040;" +
											"password=f";

	public static void EnsureTablesExist()
	{
		using MySqlConnection conn = new(ConnectionString);
		conn.Open();

		using MySqlCommand gamesCmd = conn.CreateCommand();
		gamesCmd.CommandText = @"
			CREATE TABLE IF NOT EXISTS Games (
				GameId INT NOT NULL AUTO_INCREMENT,
				StartTime DATETIME NOT NULL,
				EndTime DATETIME NULL,
				PRIMARY KEY (GameId)
			);";
		gamesCmd.ExecuteNonQuery();

		using MySqlCommand playersCmd = conn.CreateCommand();
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
	}

	public static List<GameRow> GetGames()
	{
		List<GameRow> games = [];
		using MySqlConnection conn = new(ConnectionString);
		conn.Open();

		using MySqlCommand cmd = conn.CreateCommand();
		cmd.CommandText = "SELECT GameId, StartTime, EndTime FROM Games ORDER BY GameId;";
		using MySqlDataReader reader = cmd.ExecuteReader();
		while (reader.Read())
		{
			int gameId = reader.GetInt32("GameId");
			DateTime startTime = reader.GetDateTime("StartTime");
			DateTime? endTime = reader.IsDBNull(reader.GetOrdinal("EndTime")) ? null : reader.GetDateTime("EndTime");
			games.Add(new GameRow(gameId, startTime, endTime));
		}

		return games;
	}

	public static List<PlayerRow> GetPlayersForGame(int gameId)
	{
		List<PlayerRow> players = [];
		using MySqlConnection conn = new(ConnectionString);
		conn.Open();

		using MySqlCommand cmd = conn.CreateCommand();
		cmd.CommandText = @"
			SELECT PlayerId, PlayerName, MaxScore, EnterTime, LeaveTime
			FROM Players
			WHERE GameId = @gameId
			ORDER BY MaxScore DESC, PlayerId;";
		cmd.Parameters.AddWithValue("@gameId", gameId);

		using MySqlDataReader reader = cmd.ExecuteReader();
		while (reader.Read())
		{
			int playerId = reader.GetInt32("PlayerId");
			string playerName = reader.GetString("PlayerName");
			int maxScore = reader.GetInt32("MaxScore");
			DateTime enterTime = reader.GetDateTime("EnterTime");
			DateTime? leaveTime = reader.IsDBNull(reader.GetOrdinal("LeaveTime")) ? null : reader.GetDateTime("LeaveTime");
			players.Add(new PlayerRow(playerId, playerName, maxScore, enterTime, leaveTime));
		}

		return players;
	}
}
