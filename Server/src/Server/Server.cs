using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using NetworkUtil;

namespace SnakeGame;

public class Server
{
	private static double interval = 0.0;

	private static ulong startInterval = 0uL;

	private static ulong totalFrames = 0uL;

	private static ulong FPS = 0uL;

	private static Stopwatch watch = new Stopwatch();

	private static StringBuilder log = new StringBuilder();

	public static World world;

	public static LinkedList<SocketState> connections = new LinkedList<SocketState>();

	private static DateTime start_time;

	private static GameConfig cfg;

	private static bool running = false;

	private static Stopwatch totalDuration = new Stopwatch();

	private static void Main(string[] args)
	{
		string text = "../../../settings.xml";
		string baseDirectory = AppContext.BaseDirectory;
		if (!File.Exists(text))
		{
			text = Path.Combine(baseDirectory, "settings.xml");
		}
		if (!File.Exists(text))
		{
			text = "settings.xml";
		}
		if (args.Length != 0 && File.Exists(args[0]))
		{
			text = args[0];
		}
		cfg = OldGameConfig.ReadXml(text);
		world = new World(cfg.GetSize(), cfg.GetWalls(), cfg.GetRespawnRate(), cfg.GetFramesPerShot());
		watch = new Stopwatch();
		watch.Start();
		running = true;
		new Task(delegate
		{
			StartServer(cfg.GetMSPerFrame());
		}).Start();
		Console.WriteLine("Server is running. Accepting clients.");
		totalDuration.Start();
		using var shutdownEvent = new ManualResetEventSlim(false);
		Console.CancelKeyPress += (_, e) =>
		{
			e.Cancel = true;
			Console.WriteLine("Shutdown requested (Ctrl+C).");
			shutdownEvent.Set();
		};
		AppDomain.CurrentDomain.ProcessExit += (_, _) => shutdownEvent.Set();
		shutdownEvent.Wait();
		running = false;
	}

	public static void StartServer(int msPerFrame)
	{
		IPAddress bindAddress = ResolveGameBindAddress();
		Networking.StartServer(HandleNewClientConnection, 11000, bindAddress);
		start_time = DateTime.Now;
		while (running)
		{
			Update(msPerFrame);
		}
	}

	private static bool IsPrivateIPv4(IPAddress address)
	{
		byte[] b = address.GetAddressBytes();
		return b[0] == 10
			|| (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
			|| (b[0] == 192 && b[1] == 168);
	}

	private static IPAddress ResolveGameBindAddress()
	{
		// Optional override still supported, but not required.
		string? bindIpEnv = Environment.GetEnvironmentVariable("GAME_BIND_IP");
		if (!string.IsNullOrWhiteSpace(bindIpEnv) && IPAddress.TryParse(bindIpEnv, out IPAddress? explicitBind))
		{
			return explicitBind;
		}

		return IPAddress.Any;
	}

	private static void HandleNewClientConnection(SocketState state)
	{
		if (!state.ErrorOccurred && running)
		{
			Console.WriteLine("Accepted new connection.");
			state.OnNetworkAction = ReceivePlayerName;
			Networking.GetData(state);
		}
	}

	private static void ReceivePlayerName(SocketState state)
	{
		if (state.ErrorOccurred || !running)
		{
			return;
		}
		if (state.GetData() == string.Empty)
		{
			Networking.GetData(state);
			return;
		}
		Socket theSocket = state.TheSocket;
		string text = "not set";
		string rawData = state.GetData();
		string[] lines = rawData.Split('\n');
		text = lines[0].Trim();
		int skinIndex = 0;
		if (lines.Length > 1 && !string.IsNullOrWhiteSpace(lines[1]))
			int.TryParse(lines[1].Trim(), out skinIndex);
		Console.WriteLine("Player(" + state.ID + ") \"" + text + "\" joined with skin " + skinIndex + ".");
		state.OnNetworkAction = DataCameFromClient;
		Snake snake;
		lock (world)
		{
			snake = world.AddRandomSnake(text, (int)state.ID, skinIndex);
			if (text.Contains("spectate"))
			{
				snake.Discontinue(0u);
			}
		}
		SendStartupInfo(theSocket, snake);
		lock (connections)
		{
			connections.AddLast(state);
		}
		Networking.GetData(state);
	}

	private static void DataCameFromClient(SocketState state)
	{
		if (state.ErrorOccurred || !running)
		{
			return;
		}
		int key = (int)state.ID;
		try
		{
			string[] separator = new string[2] { "\n", "\r\n" };
			string[] array = state.GetData().Split(separator, StringSplitOptions.RemoveEmptyEntries);
			for (int i = 0; i < array.Length; i++)
			{
				lock (world)
				{
					if (world.Snakes.ContainsKey(key))
					{
						world.ProcessCommand(world.Snakes[key], array[i]);
					}
				}
				state.RemoveData(0, array[i].Length + 1);
			}
			Networking.GetData(state);
		}
		catch (Exception ex)
		{
			Console.WriteLine("Error receiving command from client: " + ex);
		}
	}

	private static void SendStartupInfo(Socket socket, Snake new_player)
	{
		Networking.Send(socket, new_player.ID + "\n" + world.Size + "\n");
		StringBuilder stringBuilder = new StringBuilder();
		foreach (Wall value in world.Walls.Values)
		{
			stringBuilder.Append(value.ToString() + "\n");
		}
		Networking.Send(socket, stringBuilder.ToString());
	}

	private static void Update(int msPerFrame)
	{
		if (watch.IsRunning)
		{
			// Use tick-precise timing for the configured interval
			double exactMs = msPerFrame;
			long targetTicks = (long)(exactMs * Stopwatch.Frequency / 1000.0);
			long sleepThreshold = 2 * Stopwatch.Frequency / 1000;
			while (watch.ElapsedTicks < targetTicks - sleepThreshold)
			{
				Thread.Sleep(1);
			}
			while (watch.ElapsedTicks < targetTicks)
			{
				Thread.SpinWait(100);
			}
			interval += watch.Elapsed.TotalMilliseconds;
			watch.Restart();
			if (interval >= 1000)
			{
				interval -= 1000.0;
				FPS = totalFrames - startInterval;
				Console.WriteLine("FPS: " + FPS);
				startInterval = totalFrames;
			}
		}
		else
		{
			watch.Start();
		}
		totalFrames++;
		StringBuilder stringBuilder = new StringBuilder();
		lock (world)
		{
			world.Update();
			foreach (Snake value2 in world.Snakes.Values)
			{
				stringBuilder.Append(value2.ToString() + "\n");
			}
			foreach (Powerup value3 in world.Powerups.Values)
			{
				stringBuilder.Append(value3.ToString() + "\n");
			}
			world.Cleanup();
		}
		lock (connections)
		{
			LinkedListNode<SocketState> linkedListNode = connections.First;
			while (linkedListNode != null)
			{
				SocketState value = linkedListNode.Value;
				if (!value.TheSocket.Connected || !Networking.Send(value.TheSocket, stringBuilder.ToString()))
				{
					Console.WriteLine("Client " + value.ID + " disconnected");
					lock (world)
					{
						if (world.Snakes.ContainsKey((int)linkedListNode.Value.ID))
						{
							world.Snakes[(int)linkedListNode.Value.ID].Discontinue(world.GetTime());
						}
					}
					LinkedListNode<SocketState> next = linkedListNode.Next;
					connections.Remove(linkedListNode);
					linkedListNode = next;
				}
				else
				{
					linkedListNode = linkedListNode.Next;
				}
			}
		}
	}

	public static void ErrorExit(string msg)
	{
		Console.WriteLine("Error: " + msg);
		Console.Read();
		Environment.Exit(1);
	}
}
internal class GameConfig
{
	protected int universeSize;

	protected int msPerFrame;

	protected uint framesPerShot;

	protected int respawnRate;

	protected List<Wall> walls;

	protected GameConfig()
	{
		universeSize = 750;
		msPerFrame = 16;
		respawnRate = 300;
		walls = new List<Wall>();
	}

	public int GetSize()
	{
		return universeSize;
	}

	public int GetMSPerFrame()
	{
		return msPerFrame;
	}

	public uint GetFramesPerShot()
	{
		return framesPerShot;
	}

	public int GetRespawnRate()
	{
		return respawnRate;
	}

	public IEnumerable<Wall> GetWalls()
	{
		return new List<Wall>(walls);
	}
}
internal class OldGameConfig : GameConfig
{
	private static Vector2D ReadPoint(XmlReader reader)
	{
		Vector2D result = null;
		int num = 0;
		int num2 = 0;
		bool flag = false;
		bool flag2 = false;
		try
		{
			while (reader.Read())
			{
				if (reader.IsStartElement())
				{
					string name = reader.Name;
					if (!(name == "x"))
					{
						if (name == "y")
						{
							reader.Read();
							num2 = int.Parse(reader.Value);
							flag2 = true;
						}
						else
						{
							Server.ErrorExit("Server found invalid \"Wall\" in xml settings.");
						}
					}
					else
					{
						reader.Read();
						num = int.Parse(reader.Value);
						flag = true;
					}
				}
				if (flag && flag2)
				{
					result = new Vector2D(num, num2);
					break;
				}
			}
		}
		catch (Exception)
		{
			Server.ErrorExit("Server found invalid \"Wall\" in xml settings.");
		}
		if (!(flag && flag2))
		{
			Server.ErrorExit("Server found invalid \"Wall\" in xml settings.");
		}
		return result;
	}

	private static Wall ReadWall(XmlReader reader)
	{
		Wall result = null;
		Vector2D vector2D = null;
		Vector2D vector2D2 = null;
		bool flag = false;
		bool flag2 = false;
		try
		{
			while (reader.Read())
			{
				if (reader.IsStartElement())
				{
					string name = reader.Name;
					if (!(name == "p1"))
					{
						if (name == "p2")
						{
							vector2D2 = ReadPoint(reader);
							if (vector2D2 == null)
							{
								Server.ErrorExit("Server found invalid \"Wall\" in xml settings.");
							}
							flag2 = true;
						}
						else
						{
							Server.ErrorExit("Server found invalid \"Wall\" in xml settings.");
						}
					}
					else
					{
						vector2D = ReadPoint(reader);
						if (vector2D == null)
						{
							Server.ErrorExit("Server found invalid \"Wall\" in xml settings.");
						}
						flag = true;
					}
				}
				if (flag && flag2)
				{
					result = new Wall(vector2D, vector2D2);
					break;
				}
			}
		}
		catch (Exception)
		{
			Server.ErrorExit("Server found invalid \"Wall\" in xml settings.");
		}
		if (!(flag && flag2))
		{
			Server.ErrorExit("Server found invalid \"Wall\" in xml settings.");
		}
		return result;
	}

	public static GameConfig ReadXml(string filepath)
	{
		OldGameConfig oldGameConfig = new OldGameConfig();
		try
		{
			using XmlReader xmlReader = XmlReader.Create(filepath);
			while (xmlReader.Read())
			{
				if (!xmlReader.IsStartElement())
				{
					continue;
				}
				switch (xmlReader.Name)
				{
				case "UniverseSize":
					xmlReader.Read();
					oldGameConfig.universeSize = int.Parse(xmlReader.Value);
					break;
				case "MSPerFrame":
					xmlReader.Read();
					oldGameConfig.msPerFrame = int.Parse(xmlReader.Value);
					break;
				case "FramesPerShot":
					xmlReader.Read();
					oldGameConfig.framesPerShot = uint.Parse(xmlReader.Value);
					break;
				case "RespawnRate":
					xmlReader.Read();
					oldGameConfig.respawnRate = int.Parse(xmlReader.Value);
					break;
				case "Wall":
				{
					Wall wall = ReadWall(xmlReader);
					if (wall == null)
					{
						Server.ErrorExit("Server found invalid \"Wall\" in xml settings.");
					}
					oldGameConfig.walls.Add(wall);
					break;
				}
				}
			}
		}
		catch (Exception)
		{
			Server.ErrorExit("unable to read settings file: " + filepath);
		}
		return oldGameConfig;
	}
}
public static class View
{
	private const string httpOkHeader = "HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Type: text/html; charset=UTF-8\r\n\r\n";

	private const string httpBadHeader = "HTTP/1.1 404 Not Found\r\nConnection: close\r\nContent-Type: text/html; charset=UTF-8\r\n\r\n";

	private const string htmlHeader = "<!DOCTYPE html><html><head><title>TankWars</title></head><body>";

	private const string htmlFooter = "</body></html>";

	public static string Get404()
	{
		return "HTTP/1.1 404 Not Found\r\nConnection: close\r\nContent-Type: text/html; charset=UTF-8\r\n\r\n" + WrapHtml("Bad http request");
	}

	public static string GetLog(string log)
	{
		string content = "log:\n" + log;
		log = log.Replace("\n", "<br>");
		return "HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Type: text/html; charset=UTF-8\r\n\r\n" + WrapHtml(content);
	}

	public static string GetAllGames(Dictionary<uint, GameModel> games)
	{
		StringBuilder stringBuilder = new StringBuilder();
		foreach (uint key in games.Keys)
		{
			stringBuilder.Append("Game " + key + " (" + games[key].Duration + " seconds)<br>");
			stringBuilder.Append("<table border=\"1\">");
			stringBuilder.Append("<tr><th>Name</th><th>Score</th></tr>");
			foreach (PlayerModel player in games[key].GetPlayers())
			{
				stringBuilder.Append("<tr>");
				stringBuilder.Append("<td>" + player.Name + "</td>");
				stringBuilder.Append("<td>" + player.Score + "</td>");
				stringBuilder.Append("</tr>");
			}
			stringBuilder.Append("</table><br><hr>");
		}
		return "HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Type: text/html; charset=UTF-8\r\n\r\n" + WrapHtml(stringBuilder.ToString());
	}

	public static string GetPlayerGames(string name, List<SessionModel> games)
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.Append("Games for " + name + "<br>");
		stringBuilder.Append("<table border=\"1\">");
		stringBuilder.Append("<tr><th>GameID</th><th>Duration</th><th>Score</th></tr>");
		foreach (SessionModel game in games)
		{
			stringBuilder.Append("<tr>");
			stringBuilder.Append("<td>" + game.GameID + "</td>");
			stringBuilder.Append("<td>" + game.Duration + "</td>");
			stringBuilder.Append("<td>" + game.Score + "</td>");
			stringBuilder.Append("</tr>");
		}
		stringBuilder.Append("</table><br><hr>");
		return "HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Type: text/html; charset=UTF-8\r\n\r\n" + WrapHtml(stringBuilder.ToString());
	}

	public static string GetHomePage(int numPlayers)
	{
		string content = numPlayers + " / 8";
		return "HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Type: text/html; charset=UTF-8\r\n\r\n" + WrapHtml(content);
	}

	private static string WrapHtml(string content)
	{
		return "<!DOCTYPE html><html><head><title>TankWars</title></head><body>" + content + "</body></html>";
	}
}
public class WebServer
{
	private World theWorld;

	private StringBuilder log;

	public WebServer(World w, StringBuilder l)
	{
		theWorld = w;
		log = l;
	}

	public void StartSterver()
	{
		Networking.StartServer(ReceiveRequest, 80);
	}

	public void ReceiveRequest(SocketState state)
	{
		state.OnNetworkAction = ServeHttpRequest;
		Networking.GetData(state);
	}

	private void ServeHttpRequest(SocketState state)
	{
		string data = state.GetData();
		Console.WriteLine("http request: (" + data.Length + ") " + data);
		if (data.IndexOf('\n') < 0)
		{
			SendResponse(state, View.Get404());
			return;
		}
		data = data.Trim();
		if (data.IndexOf("GET /log") >= 0)
		{
			string text = "";
			lock (log)
			{
				text = log.ToString();
			}
			SendResponse(state, View.GetLog(text));
		}
		else if (data.IndexOf("GET /games?player=") >= 0)
		{
			int num = data.IndexOf("GET /games?player=");
			num += "GET /games?player=".Length;
			int num2 = data.IndexOf(" HTTP", num);
			string name = data.Substring(num, num2 - num);
			SendResponse(state, View.GetPlayerGames(name, DatabaseController.GetPlayerGames(name)));
		}
		else if (data.IndexOf("GET /games") >= 0)
		{
			SendResponse(state, View.GetAllGames(DatabaseController.GetAllGames()));
		}
		else
		{
			int numPlayers = 0;
			lock (theWorld)
			{
				numPlayers = theWorld.Snakes.Count;
			}
			SendResponse(state, View.GetHomePage(numPlayers));
		}
	}

	private void SendResponse(SocketState state, string html)
	{
		Networking.SendAndClose(state.TheSocket, html);
	}
}
