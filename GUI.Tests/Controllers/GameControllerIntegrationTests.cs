// <copyright file="GameControllerIntegrationTests.cs" company="Snake PS9">
// Copyright (c) 2026 Alex Waldmann, George Higbie, & CS 3500 Course Staff + Associates. All rights reserved.
// </copyright>
// Author: Alex Waldmann
// Date: 2026-04-12

using System.Net;
using System.Net.Sockets;
using System.Text;
using GUI.Components.Controllers;

namespace GUI.Tests.Controllers;

/// <summary>
/// Integration tests for <see cref="GameController"/> that spin up an in-process
/// <see cref="TcpListener"/> to exercise the <c>ConnectAsync</c> success path and
/// <c>CommitPendingStateIfInitializing</c>, which require a live TCP connection.
/// </summary>
[TestClass]
public class GameControllerIntegrationTests
{
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(5);

    private static (TcpListener listener, int port) StartFakeServer()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return (listener, ((IPEndPoint)listener.LocalEndpoint!).Port);
    }

    /// <summary>
    /// Runs the full ConnectAsync handshake for <paramref name="game"/>:
    /// accepts the client, reads the two hello lines, returns the server-side writer.
    /// ConnectAsync completes as soon as the hello lines are written (before the server sends IDs).
    /// </summary>
    private static async Task<(TcpClient serverClient, StreamWriter serverWriter)> AcceptAndHandshakeAsync(
        TcpListener listener,
        GameController game,
        string playerName = "Tester")
    {
        var port = ((IPEndPoint)listener.LocalEndpoint!).Port;
        var connectTask = game.ConnectAsync("127.0.0.1", port, playerName);

        var serverClient = await listener.AcceptTcpClientAsync();
        var stream = serverClient.GetStream();
        var serverReader = new StreamReader(stream, Encoding.UTF8);
        var serverWriter = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

        // Consume the two hello lines the client sends.
        await serverReader.ReadLineAsync();
        await serverReader.ReadLineAsync();

        // ConnectAsync returns once the TCP connect + write of the hello lines is done.
        await connectTask;

        return (serverClient, serverWriter);
    }

    [TestMethod]
    public async Task GameController_ConnectAsync_FullHandshakeAndFirstSnake_CommitsInitializingState()
    {
        // This test covers:
        //   • ConnectAsync success path (the try-block completion and method returning normally)
        //   • CommitPendingStateIfInitializing when _isInitializing is true
        //     (the body that copies _pendingPlayerId/_pendingWorldSize and swaps _walls)
        var (listener, _) = StartFakeServer();
        using var nc = new NetworkController();
        var game = new GameController(nc);
        try
        {
            var (serverClient, serverWriter) = await AcceptAndHandshakeAsync(listener, game);

            // Send handshake lines so the read loop records _pendingPlayerId + _pendingWorldSize.
            await serverWriter.WriteLineAsync("7");   // player ID
            await serverWriter.WriteLineAsync("200"); // world size
            await Task.Delay(50);                     // let read loop process the two lines

            // A wall sent while _isInitializing=true goes to _pendingWalls (not yet in GetWalls).
            await serverWriter.WriteLineAsync("{\"wall\":1,\"p1\":{\"x\":0,\"y\":0},\"p2\":{\"x\":50,\"y\":0}}");
            await Task.Delay(50);
            Assert.IsEmpty(game.GetWalls(), "Wall should be pending (not yet committed)");

            // Subscribe before sending the snake so we won't miss the state-update event.
            var snakeTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            game.OnStateUpdated += () =>
            {
                if (game.GetSnakes().Count > 0)
                    snakeTcs.TrySetResult();
            };

            // First snake triggers CommitPendingStateIfInitializing, which:
            //   - clears _isInitializing
            //   - promotes _pendingPlayerId → PlayerId
            //   - promotes _pendingWorldSize → WorldSize
            //   - swaps _pendingWalls → _walls
            await serverWriter.WriteLineAsync("{\"snake\":7,\"name\":\"Hero\",\"alive\":true}");
            await snakeTcs.Task.WaitAsync(_timeout);

            Assert.AreEqual(7, game.PlayerId,    "PlayerId should be committed after first snake");
            Assert.AreEqual(200, game.WorldSize,  "WorldSize should be committed after first snake");
            Assert.HasCount(1, game.GetWalls(), "Pending wall should be visible after commit");
            Assert.HasCount(1, game.GetSnakes(), "Snake should be present after commit");

            serverClient.Dispose();
        }
        finally
        {
            listener.Stop();
        }
    }
}
