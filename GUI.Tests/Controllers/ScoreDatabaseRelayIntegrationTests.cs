// <copyright file="ScoreDatabaseRelayIntegrationTests.cs" company="Snake PS9">
// Copyright (c) 2026 Alex Waldmann, George Higbie, & CS 3500 Course Staff + Associates. All rights reserved.
// </copyright>
// Author: Alex Waldmann
// Date: 2026-04-21

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using GUI.Components.Controllers;

namespace GUI.Tests.Controllers;

[TestClass]
[DoNotParallelize]
public sealed class ScoreDatabaseRelayIntegrationTests
{
    private string? _originalRelayBaseUrl;

    [TestInitialize]
    public void Setup()
    {
        _originalRelayBaseUrl = Environment.GetEnvironmentVariable("SCORE_RELAY_BASE_URL");
    }

    [TestCleanup]
    public void Cleanup()
    {
        Environment.SetEnvironmentVariable("SCORE_RELAY_BASE_URL", _originalRelayBaseUrl);
    }

    [TestMethod]
    public void ScoreDatabaseController_RelayMode_CreateAndUpdateCalls_HitExpectedEndpoints()
    {
        using var relay = new FakeRelayServer();
        Environment.SetEnvironmentVariable("SCORE_RELAY_BASE_URL", relay.BaseUrl);

        var scoreDb = new ScoreDatabaseController();
        DateTime now = DateTime.UtcNow;

        int? gameId = scoreDb.TryCreateGame(now, "relay-host", 5155);
        scoreDb.TryInsertPlayer(gameId ?? 0, 7, "P1", 3, now);
        scoreDb.TryUpdatePlayerMaxScore(gameId ?? 0, 7, 9);
        scoreDb.TrySetPlayerLeaveTime(gameId ?? 0, 7, now.AddSeconds(4));
        scoreDb.TrySetGameEndTime(gameId ?? 0, now.AddSeconds(5));

        Assert.AreEqual(321, gameId, "Relay should return mocked game ID.");
        relay.AssertSaw("POST", "/api/games/start");
        relay.AssertSaw("POST", "/api/players/upsert");
        relay.AssertSaw("POST", "/api/players/score");
        relay.AssertSaw("POST", "/api/players/leave");
        relay.AssertSaw("POST", "/api/games/end");
    }

    [TestMethod]
    public void ScoreDatabaseController_RelayMode_GetOpenGames_ReturnsRelaySessions()
    {
        using var relay = new FakeRelayServer();
        relay.OpenGames =
        [
            new RelayGameDto(11, "10.128.60.12", 5155, DateTime.UtcNow),
            new RelayGameDto(12, "192.168.1.25", 5155, DateTime.UtcNow.AddSeconds(-10)),
        ];

        Environment.SetEnvironmentVariable("SCORE_RELAY_BASE_URL", relay.BaseUrl);
        var scoreDb = new ScoreDatabaseController();

        IReadOnlyList<LiveGameSession> sessions = scoreDb.GetOpenGameSessions();

        Assert.HasCount(2, sessions);
        Assert.AreEqual(11, sessions[0].GameId);
        Assert.AreEqual("10.128.60.12", sessions[0].Host);
        Assert.AreEqual(12, sessions[1].GameId);
        relay.AssertSaw("GET", "/api/games/open");
    }

    [TestMethod]
    public void ScoreDatabaseController_RelayMode_GetGlobalTopScores_ReturnsTopRows()
    {
        using var relay = new FakeRelayServer();
        relay.TopScores =
        [
            new RelayTopScoreDto("Alice", 42, DateTime.UtcNow.AddMinutes(-5), 12, 1),
            new RelayTopScoreDto("Bob", 37, DateTime.UtcNow.AddMinutes(-2), 13, 4),
        ];

        Environment.SetEnvironmentVariable("SCORE_RELAY_BASE_URL", relay.BaseUrl);
        var scoreDb = new ScoreDatabaseController();

        IReadOnlyList<GlobalHighScoreEntry> scores = scoreDb.GetGlobalTopScores(10);

        Assert.HasCount(2, scores);
        Assert.AreEqual("Alice", scores[0].PlayerName);
        Assert.AreEqual(42, scores[0].MaxScore);
        Assert.AreEqual(13, scores[1].GameId);
        relay.AssertSaw("GET", "/api/scores/top");
    }

    private sealed class FakeRelayServer : IDisposable
    {
        private readonly HttpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _loopTask;

        public ConcurrentQueue<RelayRequest> Requests { get; } = new();

        public string BaseUrl { get; }

        public int CreateGameId { get; set; } = 321;

        public RelayGameDto[] OpenGames { get; set; } = [];

        public RelayTopScoreDto[] TopScores { get; set; } = [];

        public FakeRelayServer()
        {
            int port = GetFreePort();
            BaseUrl = $"http://127.0.0.1:{port}";
            _listener = new HttpListener();
            _listener.Prefixes.Add(BaseUrl + "/");
            _listener.Start();
            _loopTask = Task.Run(LoopAsync);
        }

        public void AssertSaw(string method, string path)
        {
            bool found = Requests.Any(r => r.Method == method && r.Path == path);
            Assert.IsTrue(found, $"Expected request {method} {path} but did not observe it.");
        }

        public void Dispose()
        {
            _cts.Cancel();
            try
            {
                _listener.Stop();
                _listener.Close();
                _loopTask.GetAwaiter().GetResult();
            }
            catch
            {
                // Ignore shutdown races while disposing test infrastructure.
            }
            finally
            {
                _cts.Dispose();
            }
        }

        private async Task LoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                HttpListenerContext? context = null;
                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (HttpListenerException)
                {
                    break;
                }

                if (context != null)
                {
                    Handle(context);
                }
            }
        }

        private void Handle(HttpListenerContext context)
        {
            string path = context.Request.Url?.AbsolutePath ?? string.Empty;
            string method = context.Request.HttpMethod;
            string body = ReadBody(context.Request);
            Requests.Enqueue(new RelayRequest(method, path, body));

            if (method == "GET" && path == "/api/games/open")
            {
                WriteJson(context.Response, 200, OpenGames);
                return;
            }

            if (method == "GET" && path == "/api/scores/top")
            {
                WriteJson(context.Response, 200, TopScores);
                return;
            }

            if (method == "POST" && path == "/api/games/start")
            {
                WriteJson(context.Response, 200, new { gameId = CreateGameId });
                return;
            }

            if (method == "POST" && path is "/api/games/end" or "/api/players/upsert" or "/api/players/score" or "/api/players/leave")
            {
                WriteJson(context.Response, 200, new { ok = true });
                return;
            }

            WriteJson(context.Response, 404, new { error = "Not found" });
        }

        private static string ReadBody(HttpListenerRequest request)
        {
            if (!request.HasEntityBody)
            {
                return string.Empty;
            }

            using var reader = new StreamReader(request.InputStream, request.ContentEncoding ?? Encoding.UTF8);
            return reader.ReadToEnd();
        }

        private static void WriteJson(HttpListenerResponse response, int statusCode, object payload)
        {
            string json = JsonSerializer.Serialize(payload);
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            response.StatusCode = statusCode;
            response.ContentType = "application/json; charset=UTF-8";
            response.ContentLength64 = bytes.Length;
            response.OutputStream.Write(bytes, 0, bytes.Length);
            response.OutputStream.Close();
        }

        private static int GetFreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
    }

    private sealed record RelayRequest(string Method, string Path, string Body);

    private sealed record RelayGameDto(int GameId, string Host, int Port, DateTime StartTime);

    private sealed record RelayTopScoreDto(string PlayerName, int MaxScore, DateTime EnterTime, int GameId, int PlayerId);
}
