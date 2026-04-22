using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using MySql.Data.MySqlClient;

const int defaultPort = 8080;
int port = args.Length > 0 && int.TryParse(args[0], out int parsedPort) ? parsedPort : defaultPort;

ScoreDataAccess.EnsureTablesExist();

TcpListener listener = new(IPAddress.Any, port);
listener.Start();
Console.WriteLine($"WebServer listening on http://0.0.0.0:{port}");

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

		string[] parts = requestLine.Split(' ');
		if (parts.Length < 2)
		{
			WriteResponse(stream, "400 Bad Request", "<html><h3>Bad Request</h3></html>");
			return;
		}

		string method = parts[0];
		string target = parts[1];

		Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);
		string? headerLine;
		while (!string.IsNullOrEmpty(headerLine = reader.ReadLine()))
		{
			int sep = headerLine.IndexOf(':');
			if (sep > 0)
			{
				string key = headerLine[..sep].Trim();
				string value = headerLine[(sep + 1)..].Trim();
				headers[key] = value;
			}
		}

		string body = string.Empty;
		if (headers.TryGetValue("Content-Length", out string? contentLengthValue)
			&& int.TryParse(contentLengthValue, out int contentLength)
			&& contentLength > 0)
		{
			char[] buffer = new char[contentLength];
			int read = 0;
			while (read < contentLength)
			{
				int chunk = reader.Read(buffer, read, contentLength - read);
				if (chunk <= 0)
				{
					break;
				}

				read += chunk;
			}

			body = new string(buffer, 0, read);
		}

		try
		{
			RouteRequest(stream, method, target, body);
		}
		catch
		{
			WriteResponse(stream, "500 Internal Server Error", "<html><h3>Internal Server Error</h3></html>");
		}
	}
}

static void RouteRequest(NetworkStream stream, string method, string target, string body)
{
	Uri uri = new($"http://localhost{target}");

	if (string.Equals(uri.AbsolutePath, "/api/health", StringComparison.OrdinalIgnoreCase)
		&& string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
	{
		WriteJsonResponse(stream, "200 OK", new { ok = true });
		return;
	}

	if (string.Equals(uri.AbsolutePath, "/api/games/open", StringComparison.OrdinalIgnoreCase)
		&& string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
	{
		WriteJsonResponse(stream, "200 OK", ScoreDataAccess.GetOpenGameSessions());
		return;
	}

	if (string.Equals(uri.AbsolutePath, "/api/scores/top", StringComparison.OrdinalIgnoreCase)
		&& string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
	{
		int limit = GetPositiveQueryInt(uri.Query, "limit") ?? 10;
		limit = Math.Clamp(limit, 1, 100);
		WriteJsonResponse(stream, "200 OK", ScoreDataAccess.GetGlobalTopScores(limit));
		return;
	}

	if (string.Equals(uri.AbsolutePath, "/api/games/start", StringComparison.OrdinalIgnoreCase)
		&& string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase))
	{
		var payload = JsonSerializer.Deserialize<CreateGameRequest>(body);
		if (payload == null)
		{
			WriteJsonResponse(stream, "400 Bad Request", new { error = "Invalid JSON payload." });
			return;
		}

		int? gameId = ScoreDataAccess.CreateGame(payload.StartTime, payload.Host, payload.Port);
		WriteJsonResponse(stream, "200 OK", new { gameId });
		return;
	}

	if (string.Equals(uri.AbsolutePath, "/api/games/end", StringComparison.OrdinalIgnoreCase)
		&& string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase))
	{
		var payload = JsonSerializer.Deserialize<SetGameEndRequest>(body);
		if (payload == null)
		{
			WriteJsonResponse(stream, "400 Bad Request", new { error = "Invalid JSON payload." });
			return;
		}

		ScoreDataAccess.SetGameEndTime(payload.GameId, payload.EndTime);
		WriteJsonResponse(stream, "200 OK", new { ok = true });
		return;
	}

	if (string.Equals(uri.AbsolutePath, "/api/players/upsert", StringComparison.OrdinalIgnoreCase)
		&& string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase))
	{
		var payload = JsonSerializer.Deserialize<UpsertPlayerRequest>(body);
		if (payload == null)
		{
			WriteJsonResponse(stream, "400 Bad Request", new { error = "Invalid JSON payload." });
			return;
		}

		ScoreDataAccess.UpsertPlayer(payload.GameId, payload.PlayerId, payload.PlayerName, payload.MaxScore, payload.EnterTime);
		WriteJsonResponse(stream, "200 OK", new { ok = true });
		return;
	}

	if (string.Equals(uri.AbsolutePath, "/api/players/score", StringComparison.OrdinalIgnoreCase)
		&& string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase))
	{
		var payload = JsonSerializer.Deserialize<UpdatePlayerScoreRequest>(body);
		if (payload == null)
		{
			WriteJsonResponse(stream, "400 Bad Request", new { error = "Invalid JSON payload." });
			return;
		}

		ScoreDataAccess.UpdatePlayerMaxScore(payload.GameId, payload.PlayerId, payload.MaxScore);
		WriteJsonResponse(stream, "200 OK", new { ok = true });
		return;
	}

	if (string.Equals(uri.AbsolutePath, "/api/players/leave", StringComparison.OrdinalIgnoreCase)
		&& string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase))
	{
		var payload = JsonSerializer.Deserialize<SetPlayerLeaveRequest>(body);
		if (payload == null)
		{
			WriteJsonResponse(stream, "400 Bad Request", new { error = "Invalid JSON payload." });
			return;
		}

		ScoreDataAccess.SetPlayerLeaveTime(payload.GameId, payload.PlayerId, payload.LeaveTime);
		WriteJsonResponse(stream, "200 OK", new { ok = true });
		return;
	}

	if (string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
	{
		if (uri.AbsolutePath == "/")
		{
			string home = "<html><h3>Welcome to the Snake Games Database!</h3><a href=\"/games\">View Games</a></html>";
			WriteResponse(stream, "200 OK", home);
			return;
		}

		if (uri.AbsolutePath == "/games")
		{
			int? gameId = GetGameId(uri.Query);
			string html = gameId.HasValue ? BuildSingleGamePage(gameId.Value) : BuildAllGamesPage();
			WriteResponse(stream, "200 OK", html);
			return;
		}
	}

	if (!string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase)
		&& !string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase))
	{
		WriteResponse(stream, "405 Method Not Allowed", "<html><h3>Method Not Allowed</h3></html>");
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

static int? GetPositiveQueryInt(string query, string key)
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
		if (kv.Length == 2
			&& string.Equals(kv[0], key, StringComparison.OrdinalIgnoreCase)
			&& int.TryParse(kv[1], out int value)
			&& value > 0)
		{
			return value;
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

static void WriteJsonResponse(NetworkStream stream, string status, object payload)
{
	string json = JsonSerializer.Serialize(payload);
	int contentLength = Encoding.UTF8.GetByteCount(json);
	string header =
		$"HTTP/1.1 {status}\r\n" +
		"Connection: close\r\n" +
		"Content-Type: application/json; charset=UTF-8\r\n" +
		$"Content-Length: {contentLength}\r\n\r\n";

	byte[] headerBytes = Encoding.UTF8.GetBytes(header);
	byte[] bodyBytes = Encoding.UTF8.GetBytes(json);
	stream.Write(headerBytes, 0, headerBytes.Length);
	stream.Write(bodyBytes, 0, bodyBytes.Length);
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

internal sealed record TopScoreRow(string PlayerName, int MaxScore, DateTime EnterTime, int GameId, int PlayerId);

internal sealed record LiveGameSession(int GameId, string Host, int Port, DateTime StartTime);
internal sealed record CreateGameRequest(DateTime StartTime, string? Host, int? Port);
internal sealed record SetGameEndRequest(int GameId, DateTime EndTime);
internal sealed record UpsertPlayerRequest(int GameId, int PlayerId, string PlayerName, int MaxScore, DateTime EnterTime);
internal sealed record UpdatePlayerScoreRequest(int GameId, int PlayerId, int MaxScore);
internal sealed record SetPlayerLeaveRequest(int GameId, int PlayerId, DateTime LeaveTime);

internal static class ScoreDataAccess
{
	private const string ConnectionString = "server=atr.eng.utah.edu;" +
											"database=u1512040;" +
											"uid=u1512040;" +
											"password=f;" +
											"Connection Timeout=3;" +
											"Default Command Timeout=3";

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

		EnsureColumnExists(conn, "Games", "Host", "ALTER TABLE Games ADD COLUMN Host VARCHAR(128) NULL;");
		EnsureColumnExists(conn, "Games", "Port", "ALTER TABLE Games ADD COLUMN Port INT NULL;");
		EnsureColumnExists(conn, "Games", "IsActive", "ALTER TABLE Games ADD COLUMN IsActive TINYINT(1) NOT NULL DEFAULT 1;");

		using MySqlCommand backfillCmd = conn.CreateCommand();
		backfillCmd.CommandText = "UPDATE Games SET IsActive = CASE WHEN EndTime IS NULL THEN 1 ELSE 0 END;";
		backfillCmd.ExecuteNonQuery();

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

	public static int? CreateGame(DateTime startTime, string? host, int? port)
	{
		using MySqlConnection conn = new(ConnectionString);
		conn.Open();

		CloseActiveGamesForEndpoint(conn, host, port, startTime);

		using MySqlCommand cmd = conn.CreateCommand();
		cmd.CommandText = @"
			INSERT INTO Games (StartTime, EndTime, Host, Port, IsActive)
			VALUES (@startTime, NULL, @host, @port, 1);
			SELECT LAST_INSERT_ID();";
		cmd.Parameters.AddWithValue("@startTime", startTime.ToString("yyyy-MM-dd H:mm:ss"));
		cmd.Parameters.AddWithValue("@host", host);
		cmd.Parameters.AddWithValue("@port", port);

		object? result = cmd.ExecuteScalar();
		return result == null ? null : Convert.ToInt32(result);
	}

	private static void CloseActiveGamesForEndpoint(MySqlConnection conn, string? host, int? port, DateTime closeTime)
	{
		if (string.IsNullOrWhiteSpace(host) || !port.HasValue)
		{
			return;
		}

		using MySqlCommand cmd = conn.CreateCommand();
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

	public static void SetGameEndTime(int gameId, DateTime endTime)
	{
		using MySqlConnection conn = new(ConnectionString);
		conn.Open();

		using MySqlCommand cmd = conn.CreateCommand();
		cmd.CommandText = @"
			UPDATE Games
			SET EndTime = @endTime,
				IsActive = 0
			WHERE GameId = @gameId;";
		cmd.Parameters.AddWithValue("@endTime", endTime.ToString("yyyy-MM-dd H:mm:ss"));
		cmd.Parameters.AddWithValue("@gameId", gameId);
		cmd.ExecuteNonQuery();
	}

	public static void UpsertPlayer(int gameId, int playerId, string playerName, int maxScore, DateTime enterTime)
	{
		using MySqlConnection conn = new(ConnectionString);
		conn.Open();

		using MySqlCommand cmd = conn.CreateCommand();
		cmd.CommandText = @"
			INSERT INTO Players (GameId, PlayerId, PlayerName, MaxScore, EnterTime, LeaveTime)
			VALUES (@gameId, @playerId, @name, @maxScore, @enterTime, NULL)
			ON DUPLICATE KEY UPDATE
				PlayerName = VALUES(PlayerName),
				MaxScore = GREATEST(MaxScore, VALUES(MaxScore));";
		cmd.Parameters.AddWithValue("@gameId", gameId);
		cmd.Parameters.AddWithValue("@playerId", playerId);
		cmd.Parameters.AddWithValue("@name", playerName);
		cmd.Parameters.AddWithValue("@maxScore", maxScore);
		cmd.Parameters.AddWithValue("@enterTime", enterTime.ToString("yyyy-MM-dd H:mm:ss"));
		cmd.ExecuteNonQuery();
	}

	public static void UpdatePlayerMaxScore(int gameId, int playerId, int maxScore)
	{
		using MySqlConnection conn = new(ConnectionString);
		conn.Open();

		using MySqlCommand cmd = conn.CreateCommand();
		cmd.CommandText = @"
			UPDATE Players
			SET MaxScore = GREATEST(MaxScore, @maxScore)
			WHERE GameId = @gameId AND PlayerId = @playerId;";
		cmd.Parameters.AddWithValue("@gameId", gameId);
		cmd.Parameters.AddWithValue("@playerId", playerId);
		cmd.Parameters.AddWithValue("@maxScore", maxScore);
		cmd.ExecuteNonQuery();
	}

	public static void SetPlayerLeaveTime(int gameId, int playerId, DateTime leaveTime)
	{
		using MySqlConnection conn = new(ConnectionString);
		conn.Open();

		using MySqlCommand cmd = conn.CreateCommand();
		cmd.CommandText = @"
			UPDATE Players
			SET LeaveTime = @leaveTime
			WHERE GameId = @gameId AND PlayerId = @playerId;";
		cmd.Parameters.AddWithValue("@leaveTime", leaveTime.ToString("yyyy-MM-dd H:mm:ss"));
		cmd.Parameters.AddWithValue("@gameId", gameId);
		cmd.Parameters.AddWithValue("@playerId", playerId);
		cmd.ExecuteNonQuery();

		TryCloseGameIfNoActivePlayers(conn, gameId, leaveTime);
	}

	public static List<LiveGameSession> GetOpenGameSessions()
	{
		List<LiveGameSession> sessions = [];
		using MySqlConnection conn = new(ConnectionString);
		conn.Open();

		CloseAllEmptyActiveGames(conn, DateTime.Now);
		CloseDuplicateActiveEndpointGames(conn, DateTime.Now);

		using MySqlCommand cmd = conn.CreateCommand();
		cmd.CommandText = @"
			SELECT GameId, Host, Port, StartTime
			FROM Games
			WHERE IsActive = 1
			  AND Host IS NOT NULL
			  AND Host <> ''
			  AND Port IS NOT NULL
			ORDER BY StartTime DESC
			LIMIT 200;";

		using MySqlDataReader reader = cmd.ExecuteReader();
		while (reader.Read())
		{
			sessions.Add(new LiveGameSession(
				reader.GetInt32("GameId"),
				reader.GetString("Host"),
				reader.GetInt32("Port"),
				reader.GetDateTime("StartTime")));
		}

		return PruneUnreachableLoopbackSessions(sessions);
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

	private static void TryCloseGameIfNoActivePlayers(MySqlConnection conn, int gameId, DateTime closeTime)
	{
		using MySqlCommand cmd = conn.CreateCommand();
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
		using MySqlCommand cmd = conn.CreateCommand();
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
		using MySqlCommand cmd = conn.CreateCommand();
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

	private static List<LiveGameSession> PruneUnreachableLoopbackSessions(List<LiveGameSession> sessions)
	{
		if (sessions.Count == 0)
		{
			return sessions;
		}

		DateTime now = DateTime.Now;
		List<LiveGameSession> reachable = [];
		foreach (LiveGameSession session in sessions)
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

			SetGameEndTime(session.GameId, now);
		}

		return reachable;
	}

	private static bool IsTcpEndpointReachable(string host, int port)
	{
		try
		{
			using TcpClient client = new();
			Task connectTask = client.ConnectAsync(host, port);
			bool completed = connectTask.Wait(TimeSpan.FromMilliseconds(150));
			return completed && client.Connected;
		}
		catch
		{
			return false;
		}
	}

	public static List<TopScoreRow> GetGlobalTopScores(int limit)
	{
		List<TopScoreRow> topScores = [];
		using MySqlConnection conn = new(ConnectionString);
		conn.Open();

		using MySqlCommand cmd = conn.CreateCommand();
		cmd.CommandText = $@"
			SELECT PlayerName, MaxScore, EnterTime, GameId, PlayerId
			FROM Players
			ORDER BY MaxScore DESC, EnterTime ASC, EntryId ASC
			LIMIT {Math.Clamp(limit, 1, 100)};";

		using MySqlDataReader reader = cmd.ExecuteReader();
		while (reader.Read())
		{
			string playerName = reader.GetString("PlayerName");
			int maxScore = reader.GetInt32("MaxScore");
			DateTime enterTime = reader.GetDateTime("EnterTime");
			int gameId = reader.GetInt32("GameId");
			int playerId = reader.GetInt32("PlayerId");
			topScores.Add(new TopScoreRow(playerName, maxScore, enterTime, gameId, playerId));
		}

		return topScores;
	}

	private static void EnsureColumnExists(MySqlConnection conn, string tableName, string columnName, string alterSql)
	{
		if (ColumnExists(conn, tableName, columnName))
		{
			return;
		}

		using MySqlCommand cmd = conn.CreateCommand();
		cmd.CommandText = alterSql;
		cmd.ExecuteNonQuery();
	}

	private static bool ColumnExists(MySqlConnection conn, string tableName, string columnName)
	{
		using MySqlCommand cmd = conn.CreateCommand();
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
}
