// <copyright file="Server.cs" company="Snake PS9">
// Copyright (c) 2026 Alex Waldmann & George Higbie. All rights reserved.
// </copyright>
// Authors: Alex Waldmann, George Higbie
// Date: 2026-04-12

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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

/// <summary>
/// Entry point and game loop for the Snake server.
/// Accepts client connections, maintains the <see cref="World"/>, and broadcasts state
/// every frame at the configured rate. Hosts an HTTP endpoint on port 80 for score queries.
/// </summary>
public class Server
{
    // ==================== Private Fields ====================

    private static double _interval = 0.0;
    private static ulong _startInterval = 0uL;
    private static ulong _totalFrames = 0uL;
    private static ulong _fps = 0uL;
    private static Stopwatch _watch = new Stopwatch();
    private static StringBuilder _log = new StringBuilder();

    /// <summary>Dedicated lock object for the shared world state.</summary>
    private static readonly object _worldLock = new();

    /// <summary>Dedicated lock object for the client connections list.</summary>
    private static readonly object _connectionsLock = new();

    /// <summary>
    /// The shared game world. Assigned once in <c>Main</c> before any threads access it.
    /// </summary>
    public static World world = null!;

    /// <summary>All currently connected client socket states.</summary>
    public static LinkedList<SocketState> connections = new LinkedList<SocketState>();

    private static DateTime _startTime;

    /// <summary>
    /// Server configuration loaded from <c>settings.xml</c>.
    /// Assigned once in <c>Main</c> before the server starts.
    /// </summary>
    private static GameConfig _cfg = null!;

    private static bool _running = false;
    private static Stopwatch _totalDuration = new Stopwatch();

    // ==================== Private Helpers ====================

    /// <summary>Entry point: reads config, creates the world, and starts the game loop.</summary>
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
        _cfg = OldGameConfig.ReadXml(text);
        world = new World(_cfg.GetSize(), _cfg.GetWalls(), _cfg.GetRespawnRate(), _cfg.GetFramesPerShot());
        _watch = new Stopwatch();
        _watch.Start();
        _running = true;
        new Task(delegate
        {
            StartServer(_cfg.GetMSPerFrame());
        }).Start();
        Console.WriteLine("Server is running. Accepting clients.");
        _totalDuration.Start();
        Console.ReadLine();
        _running = false;
    }

    /// <summary>
    /// Starts the TCP listener on port 11000 and runs the main update loop.
    /// </summary>
    /// <param name="msPerFrame">Milliseconds per game frame.</param>
    public static void StartServer(int msPerFrame)
    {
        Networking.StartServer(HandleNewClientConnection, 11000);
        _startTime = DateTime.Now;
        while (_running)
        {
            Update(msPerFrame);
        }
    }

    /// <summary>
    /// Callback invoked by the networking layer when a brand-new TCP client connects.
    /// Transitions the socket to the name-receive state.
    /// </summary>
    private static void HandleNewClientConnection(SocketState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!state.ErrorOccurred && _running)
        {
            Console.WriteLine("Accepted new connection.");
            state.OnNetworkAction = ReceivePlayerName;
            Networking.GetData(state);
        }
    }

    /// <summary>
    /// Callback that receives the player name (and optional skin index) from a newly connected client.
    /// Spawns the snake, sends startup info, and registers the connection for updates.
    /// </summary>
    private static void ReceivePlayerName(SocketState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.ErrorOccurred || !_running)
        {
            return;
        }
        if (state.GetData() == string.Empty)
        {
            Networking.GetData(state);
            return;
        }
        Socket theSocket = state.TheSocket!;
        string text = "not set";
        string rawData = state.GetData();
        string[] lines = rawData.Split('\n');
        text = lines[0].Trim();
        int skinIndex = 0;
        if (lines.Length > 1 && !string.IsNullOrWhiteSpace(lines[1]))
        {
            int.TryParse(lines[1].Trim(), out skinIndex);
        }
        Console.WriteLine("Player(" + state.ID + ") \"" + text + "\" joined with skin " + skinIndex + ".");
        state.OnNetworkAction = DataCameFromClient;
        Snake snake;
        lock (_worldLock)
        {
            snake = world.AddRandomSnake(text, (int)state.ID, skinIndex);
            if (text.Contains("spectate"))
            {
                snake.Discontinue(0u);
            }
        }
        SendStartupInfo(theSocket, snake);
        lock (_connectionsLock)
        {
            connections.AddLast(state);
        }
        Networking.GetData(state);
    }

    /// <summary>
    /// Callback invoked each time data arrives from an already-connected client.
    /// Parses and applies direction commands from the client's message buffer.
    /// </summary>
    private static void DataCameFromClient(SocketState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.ErrorOccurred || !_running)
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
                lock (_worldLock)
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

    /// <summary>
    /// Sends the player ID, world size, and all wall definitions to a newly joined client.
    /// </summary>
    /// <param name="socket">The client socket. Must not be null.</param>
    /// <param name="newPlayer">The snake assigned to this player. Must not be null.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="socket"/> or <paramref name="newPlayer"/> is null.
    /// </exception>
    private static void SendStartupInfo(Socket socket, Snake newPlayer)
    {
        ArgumentNullException.ThrowIfNull(socket);
        ArgumentNullException.ThrowIfNull(newPlayer);
        Networking.Send(socket, newPlayer.ID + "\n" + world.Size + "\n");
        StringBuilder stringBuilder = new StringBuilder();
        foreach (Wall value in world.Walls.Values)
        {
            stringBuilder.Append(value.ToString() + "\n");
        }
        Networking.Send(socket, stringBuilder.ToString());
    }

    /// <summary>
    /// Advances the world by one frame, broadcasts the updated state to all clients,
    /// and removes disconnected clients from the connection list.
    /// Busy-waits to hit the configured frame deadline.
    /// </summary>
    /// <param name="msPerFrame">Target frame period in milliseconds.</param>
    private static void Update(int msPerFrame)
    {
        if (_watch.IsRunning)
        {
            // Use tick-precise timing: 16 ms integer would give ~62.5 fps,
            // so compute exact tick target from the desired ms interval.
            double exactMs = msPerFrame < 17 ? 1000.0 / 60.0 : msPerFrame;
            long targetTicks = (long)(exactMs * Stopwatch.Frequency / 1000.0);
            long sleepThreshold = 2 * Stopwatch.Frequency / 1000;
            while (_watch.ElapsedTicks < targetTicks - sleepThreshold)
            {
                Thread.Sleep(1);
            }
            while (_watch.ElapsedTicks < targetTicks)
            {
                Thread.SpinWait(100);
            }
            _interval += _watch.Elapsed.TotalMilliseconds;
            _watch.Restart();
            if (_interval >= 1000)
            {
                _interval -= 1000.0;
                _fps = _totalFrames - _startInterval;
                Console.WriteLine("FPS: " + _fps);
                _startInterval = _totalFrames;
            }
        }
        else
        {
            _watch.Start();
        }
        _totalFrames++;
        StringBuilder stringBuilder = new StringBuilder();
        lock (_worldLock)
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
        lock (_connectionsLock)
        {
            LinkedListNode<SocketState>? linkedListNode = connections.First;
            while (linkedListNode != null)
            {
                SocketState value = linkedListNode.Value;
                if (!value.TheSocket!.Connected || !Networking.Send(value.TheSocket, stringBuilder.ToString()))
                {
                    Console.WriteLine("Client " + value.ID + " disconnected");
                    lock (_worldLock)
                    {
                        if (world.Snakes.ContainsKey((int)linkedListNode.Value.ID))
                        {
                            world.Snakes[(int)linkedListNode.Value.ID].Discontinue(world.GetTime());
                        }
                    }
                    LinkedListNode<SocketState>? next = linkedListNode.Next;
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

    /// <summary>
    /// Prints an error message to the console and terminates the process.
    /// Used during fatal startup errors only.
    /// </summary>
    /// <param name="msg">Error description. Must not be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="msg"/> is null.</exception>
    public static void ErrorExit(string msg)
    {
        ArgumentNullException.ThrowIfNull(msg);
        Console.WriteLine("Error: " + msg);
        Console.Read();
        Environment.Exit(1);
    }
}

/// <summary>
/// Base class for server configuration loaded from <c>settings.xml</c>.
/// Provides getters for all gameplay parameters.
/// </summary>
internal class GameConfig
{
    // ==================== Protected Fields ====================

    /// <summary>World half-size in pixels.</summary>
    protected int universeSize;

    /// <summary>Target milliseconds per simulation frame.</summary>
    protected int msPerFrame;

    /// <summary>Number of frames between allowed shots (unused in base snake rules).</summary>
    protected uint framesPerShot;

    /// <summary>Number of frames a dead snake must wait before respawning.</summary>
    protected int respawnRate;

    /// <summary>List of wall definitions loaded from settings.</summary>
    protected List<Wall> walls;

    // ==================== Constructor ====================

    /// <summary>Initializes <see cref="GameConfig"/> with sensible default values.</summary>
    protected GameConfig()
    {
        universeSize = 750;
        msPerFrame = 16;
        respawnRate = 300;
        walls = new List<Wall>();
    }

    // ==================== Public Methods ====================

    /// <summary>Returns the world size (side length in world units).</summary>
    /// <returns>World size.</returns>
    public int GetSize() => universeSize;

    /// <summary>Returns the target milliseconds per frame.</summary>
    /// <returns>Milliseconds per frame.</returns>
    public int GetMSPerFrame() => msPerFrame;

    /// <summary>Returns the frames-per-shot cooldown.</summary>
    /// <returns>Frames per shot.</returns>
    public uint GetFramesPerShot() => framesPerShot;

    /// <summary>Returns the number of frames a dead snake must wait before it can respawn.</summary>
    /// <returns>Respawn delay in frames.</returns>
    public int GetRespawnRate() => respawnRate;

    /// <summary>Returns a defensive copy of the wall list.</summary>
    /// <returns>An enumerable of <see cref="Wall"/> objects.</returns>
    public IEnumerable<Wall> GetWalls() => new List<Wall>(walls);
}

/// <summary>
/// Reads server configuration from a legacy XML settings file format.
/// </summary>
internal class OldGameConfig : GameConfig
{
    // ==================== Private Helpers ====================

    /// <summary>Reads a single <c>&lt;p&gt;</c> element from the XML as a <see cref="Vector2D"/>.</summary>
    /// <param name="reader">The active <see cref="XmlReader"/> positioned inside a <c>&lt;p&gt;</c> tag.</param>
    /// <returns>The parsed <see cref="Vector2D"/>, or <c>null</c> if parsing fails.</returns>
    private static Vector2D? ReadPoint(XmlReader reader)
    {
        Vector2D? result = null;
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
                    if (name == "x")
                    {
                        reader.Read();
                        num = int.Parse(reader.Value);
                        flag = true;
                    }
                    else if (name == "y")
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

    /// <summary>Reads a single <c>&lt;Wall&gt;</c> element from the XML reader.</summary>
    /// <param name="reader">The active <see cref="XmlReader"/> positioned inside a <c>&lt;Wall&gt;</c> tag.</param>
    /// <returns>The parsed <see cref="Wall"/>, or <c>null</c> if parsing fails.</returns>
    private static Wall? ReadWall(XmlReader reader)
    {
        Wall? result = null;
        Vector2D? vector2D = null;
        Vector2D? vector2D2 = null;
        bool flag = false;
        bool flag2 = false;
        try
        {
            while (reader.Read())
            {
                if (reader.IsStartElement())
                {
                    string name = reader.Name;
                    if (name == "p1")
                    {
                        vector2D = ReadPoint(reader);
                        if (vector2D == null)
                        {
                            Server.ErrorExit("Server found invalid \"Wall\" in xml settings.");
                        }
                        flag = true;
                    }
                    else if (name == "p2")
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
                if (flag && flag2)
                {
                    result = new Wall(vector2D!, vector2D2!);
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

    /// <summary>
    /// Parses the <c>settings.xml</c> file at <paramref name="filepath"/> into a <see cref="GameConfig"/>.
    /// Calls <see cref="Server.ErrorExit"/> and terminates if the file is missing or malformed.
    /// </summary>
    /// <param name="filepath">Path to the settings XML file. Must not be null.</param>
    /// <returns>A populated <see cref="GameConfig"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="filepath"/> is null.</exception>
    public static GameConfig ReadXml(string filepath)
    {
        ArgumentNullException.ThrowIfNull(filepath);
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
                        Wall? wall = ReadWall(xmlReader);
                        if (wall == null)
                        {
                            Server.ErrorExit("Server found invalid \"Wall\" in xml settings.");
                        }
                        oldGameConfig.walls.Add(wall!);
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

/// <summary>
/// Generates HTTP responses for the web-based scoreboard and logging endpoints.
/// All methods return complete HTTP responses (headers + body) as strings.
/// </summary>
public static class View
{
    // ==================== Private Fields ====================

    private const string HttpOkHeader = "HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Type: text/html; charset=UTF-8\r\n\r\n";
    private const string HttpBadHeader = "HTTP/1.1 404 Not Found\r\nConnection: close\r\nContent-Type: text/html; charset=UTF-8\r\n\r\n";
    private const string HtmlHeader = "<!DOCTYPE html><html><head><title>TankWars</title></head><body>";
    private const string HtmlFooter = "</body></html>";

    // ==================== Public Methods ====================

    /// <summary>Returns a 404 HTTP response with a generic error body.</summary>
    /// <returns>Complete HTTP 404 response string.</returns>
    public static string Get404()
    {
        return HttpBadHeader + WrapHtml("Bad http request");
    }

    /// <summary>
    /// Returns a 200 HTTP response containing the server log.
    /// </summary>
    /// <param name="log">The raw log string. Must not be null.</param>
    /// <returns>Complete HTTP 200 response string with the log as HTML.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="log"/> is null.</exception>
    public static string GetLog(string log)
    {
        ArgumentNullException.ThrowIfNull(log);
        string content = "log:\n" + log;
        log = log.Replace("\n", "<br>");
        return HttpOkHeader + WrapHtml(content);
    }

    /// <summary>
    /// Returns a 200 HTTP response containing a score table for all recorded games.
    /// </summary>
    /// <param name="games">Game records indexed by game ID. Must not be null.</param>
    /// <returns>Complete HTTP 200 response string.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="games"/> is null.</exception>
    public static string GetAllGames(Dictionary<uint, GameModel> games)
    {
        ArgumentNullException.ThrowIfNull(games);
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
        return HttpOkHeader + WrapHtml(stringBuilder.ToString());
    }

    /// <summary>
    /// Returns a 200 HTTP response containing per-session game records for a player.
    /// </summary>
    /// <param name="name">The player's display name. Must not be null.</param>
    /// <param name="games">List of session records for this player. Must not be null.</param>
    /// <returns>Complete HTTP 200 response string.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="name"/> or <paramref name="games"/> is null.
    /// </exception>
    public static string GetPlayerGames(string name, List<SessionModel> games)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(games);
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
        return HttpOkHeader + WrapHtml(stringBuilder.ToString());
    }

    /// <summary>Returns a 200 HTTP response showing the current connected player count.</summary>
    /// <param name="numPlayers">The number of currently connected players.</param>
    /// <returns>Complete HTTP 200 response string.</returns>
    public static string GetHomePage(int numPlayers)
    {
        string content = numPlayers + " / 8";
        return HttpOkHeader + WrapHtml(content);
    }

    // ==================== Private Helpers ====================

    /// <summary>Wraps HTML body content in a standard HTML document envelope.</summary>
    private static string WrapHtml(string content)
    {
        return HtmlHeader + content + HtmlFooter;
    }
}

/// <summary>
/// Lightweight HTTP server that serves the Snake scoreboard on port 80.
/// Parses inbound HTTP GET requests and delegates to <see cref="View"/> and <see cref="DatabaseController"/>.
/// </summary>
public class WebServer
{
    // ==================== Private Fields ====================

    /// <summary>Dedicated lock object for the shared game world reference.</summary>
    private readonly object _worldLock = new();

    /// <summary>Dedicated lock object for the server log <see cref="StringBuilder"/>.</summary>
    private readonly object _logLock = new();

    private readonly World _theWorld;
    private readonly StringBuilder _log;

    // ==================== Constructor ====================

    /// <summary>
    /// Initializes a new <see cref="WebServer"/>.
    /// </summary>
    /// <param name="w">The active game world. Must not be null.</param>
    /// <param name="l">The server log builder. Must not be null.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="w"/> or <paramref name="l"/> is null.
    /// </exception>
    public WebServer(World w, StringBuilder l)
    {
        ArgumentNullException.ThrowIfNull(w);
        ArgumentNullException.ThrowIfNull(l);
        _theWorld = w;
        _log = l;
    }

    // ==================== Public Methods ====================

    /// <summary>Begins listening for HTTP requests on port 80.</summary>
    public void StartServer()
    {
        Networking.StartServer(ReceiveRequest, 80);
    }

    /// <summary>
    /// Callback invoked when a new HTTP connection arrives.
    /// Switches the socket to the HTTP request parsing state.
    /// </summary>
    /// <param name="state">The incoming socket state. Must not be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="state"/> is null.</exception>
    public void ReceiveRequest(SocketState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        state.OnNetworkAction = ServeHttpRequest;
        Networking.GetData(state);
    }

    // ==================== Private Helpers ====================

    /// <summary>
    /// Parses a buffered HTTP GET request and sends the appropriate HTML response.
    /// </summary>
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
            lock (_logLock)
            {
                text = _log.ToString();
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
            lock (_worldLock)
            {
                numPlayers = _theWorld.Snakes.Count;
            }
            SendResponse(state, View.GetHomePage(numPlayers));
        }
    }

    /// <summary>Sends an HTTP response and closes the connection.</summary>
    private void SendResponse(SocketState state, string html)
    {
        Networking.SendAndClose(state.TheSocket!, html);
    }
}
