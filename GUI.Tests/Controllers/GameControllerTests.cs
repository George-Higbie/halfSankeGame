// <copyright file="GameControllerTests.cs" company="Snake PS9">
// Copyright (c) 2026 Alex Waldmann & George Higbie. All rights reserved.
// </copyright>
// Author: Alex Waldmann
// Date: 2026-04-12

using System.Reflection;
using GUI.Components.Controllers;
using GUI.Components.Models;

namespace GUI.Tests.Controllers;

/// <summary>
/// Tests for <see cref="GameController"/>.
/// Exercises state management by raising events on the underlying <see cref="NetworkController"/>.
/// </summary>
[TestClass]
public class GameControllerTests
{
    private NetworkController _network = null!;
    private GameController _game = null!;

    [TestInitialize]
    public void Setup()
    {
        _network = new NetworkController();
        _game = new GameController(_network);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _network.Dispose();
    }

    // ==================== Constructor ====================

    [TestMethod]
    public void GameController_Constructor_NullNetworkArg_ThrowsArgumentNullException()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => new GameController(null!));
    }

    // ==================== Initial State ====================

    [TestMethod]
    public void GameController_PlayerId_InitialState_IsNull()
    {
        Assert.IsNull(_game.PlayerId);
    }

    [TestMethod]
    public void GameController_WorldSize_InitialState_IsNull()
    {
        Assert.IsNull(_game.WorldSize);
    }

    [TestMethod]
    public void GameController_GetSnakes_InitialState_ReturnsEmptyList()
    {
           Assert.IsEmpty(_game.GetSnakes());
    }

    [TestMethod]
    public void GameController_GetWalls_InitialState_ReturnsEmptyList()
    {
           Assert.IsEmpty(_game.GetWalls());
    }

    [TestMethod]
    public void GameController_GetPowerups_InitialState_ReturnsEmptyList()
    {
           Assert.IsEmpty(_game.GetPowerups());
    }

    [TestMethod]
    public void GameController_GetPlayerSnake_InitialState_ReturnsNull()
    {
        Assert.IsNull(_game.GetPlayerSnake());
    }

    // ==================== Event Handling ====================

    [TestMethod]
    public void GameController_OnPlayerIdReceived_ValidId_SetsPlayerId()
    {
        RaiseEvent(_network, "OnPlayerIdReceived", 42);

        Assert.AreEqual(42, _game.PlayerId);
    }

    [TestMethod]
    public void GameController_OnWorldSizeReceived_ValidSize_SetsWorldSize()
    {
        RaiseEvent(_network, "OnWorldSizeReceived", 2000);

        Assert.AreEqual(2000, _game.WorldSize);
    }

    [TestMethod]
    public void GameController_OnWallReceived_NewWall_AddsToCollection()
    {
        var wall = new Wall { Id = 1, Point1 = new Point2D(0, 0), Point2 = new Point2D(100, 0) };

        RaiseEvent(_network, "OnWallReceived", wall);

        Assert.HasCount(1, _game.GetWalls());
    }

    [TestMethod]
    public void GameController_OnSnakeReceived_NewSnake_AddsToCollection()
    {
        RaiseEvent(_network, "OnPlayerIdReceived", 1);
        RaiseEvent(_network, "OnWorldSizeReceived", 2000);

        var snake = new Snake
        {
            Id = 1,
            Name = "Test",
            Body = new List<Point2D> { new(0, 0), new(10, 0) },
            Score = 0,
            Died = false,
            Alive = true
        };
        RaiseEvent(_network, "OnSnakeReceived", snake);

        Assert.HasCount(1, _game.GetSnakes());
    }

    [TestMethod]
    public void GameController_OnPowerupReceived_AlivePowerup_AddsToCollection()
    {
        RaiseEvent(_network, "OnPlayerIdReceived", 1);
        RaiseEvent(_network, "OnWorldSizeReceived", 2000);

        // First snake to trigger commit
        var snake = new Snake { Id = 1, Name = "Test" };
        RaiseEvent(_network, "OnSnakeReceived", snake);

        var powerup = new Powerup { Id = 5, Location = new Point2D(50, 50), Died = false };
        RaiseEvent(_network, "OnPowerupReceived", powerup);

        Assert.HasCount(1, _game.GetPowerups());
    }

    [TestMethod]
    public void GameController_OnPowerupReceived_DiedPowerup_RemovesFromCollection()
    {
        RaiseEvent(_network, "OnPlayerIdReceived", 1);
        RaiseEvent(_network, "OnWorldSizeReceived", 2000);

        var snake = new Snake { Id = 1, Name = "Test" };
        RaiseEvent(_network, "OnSnakeReceived", snake);

        var p = new Powerup { Id = 5, Location = new Point2D(50, 50), Died = false };
        RaiseEvent(_network, "OnPowerupReceived", p);
        Assert.HasCount(1, _game.GetPowerups());

        var pDied = new Powerup { Id = 5, Died = true };
        RaiseEvent(_network, "OnPowerupReceived", pDied);
        Assert.IsEmpty(_game.GetPowerups());
    }

    // ==================== Snapshots Are Deep Copies ====================

    [TestMethod]
    public void GameController_GetSnakes_AfterSnakeAdded_ReturnsDeepCopy()
    {
        RaiseEvent(_network, "OnPlayerIdReceived", 1);
        RaiseEvent(_network, "OnWorldSizeReceived", 2000);

        var snake = new Snake
        {
            Id = 1,
            Name = "Test",
            Body = new List<Point2D> { new(0, 0), new(10, 0) },
            Alive = true
        };
        RaiseEvent(_network, "OnSnakeReceived", snake);

        var snapshot1 = _game.GetSnakes();
        var snapshot2 = _game.GetSnakes();

        Assert.AreNotSame(snapshot1[0], snapshot2[0]);
    }

    [TestMethod]
    public void GameController_GetPlayerSnake_AfterPlayerSnakeAdded_ReturnsDeepCopy()
    {
        RaiseEvent(_network, "OnPlayerIdReceived", 1);
        RaiseEvent(_network, "OnWorldSizeReceived", 2000);

        var snake = new Snake
        {
            Id = 1,
            Name = "Test",
            Body = new List<Point2D> { new(0, 0), new(10, 0) },
            Alive = true
        };
        RaiseEvent(_network, "OnSnakeReceived", snake);

        var p1 = _game.GetPlayerSnake();
        var p2 = _game.GetPlayerSnake();

        Assert.IsNotNull(p1);
        Assert.IsNotNull(p2);
        Assert.AreNotSame(p1, p2);
    }

    // ==================== Disconnect ====================

    [TestMethod]
    public void GameController_Disconnect_AfterSetup_ClearsAllState()
    {
        RaiseEvent(_network, "OnPlayerIdReceived", 1);
        RaiseEvent(_network, "OnWorldSizeReceived", 2000);

        _game.Disconnect();

        Assert.IsNull(_game.PlayerId);
        Assert.IsNull(_game.WorldSize);
        Assert.IsEmpty(_game.GetSnakes());
        Assert.IsEmpty(_game.GetWalls());
        Assert.IsEmpty(_game.GetPowerups());
    }

    // ==================== Snake State Merging ====================

    [TestMethod]
    public void GameController_OnSnakeReceived_ExistingSnakeUpdate_MergesFields()
    {
        RaiseEvent(_network, "OnPlayerIdReceived", 1);
        RaiseEvent(_network, "OnWorldSizeReceived", 2000);

        var snake = new Snake { Id = 1, Name = "Test", Score = 0, Alive = true };
        RaiseEvent(_network, "OnSnakeReceived", snake);

        var update = new Snake { Id = 1, Score = 5 };
        RaiseEvent(_network, "OnSnakeReceived", update);

        var result = _game.GetSnakes().First(s => s.Id == 1);
        Assert.AreEqual(5, result.Score);
        Assert.AreEqual("Test", result.Name);
    }

    [TestMethod]
    public void GameController_OnSnakeReceived_DisconnectedSnake_RemovesFromCollection()
    {
        RaiseEvent(_network, "OnPlayerIdReceived", 1);
        RaiseEvent(_network, "OnWorldSizeReceived", 2000);

        var snake = new Snake { Id = 2, Name = "Other", Alive = true };
        RaiseEvent(_network, "OnSnakeReceived", snake);
        Assert.HasCount(1, _game.GetSnakes());

        var dc = new Snake { Id = 2, Disconnected = true };
        RaiseEvent(_network, "OnSnakeReceived", dc);
        Assert.IsEmpty(_game.GetSnakes());
    }

    // ==================== OnStateUpdated Event ====================

    [TestMethod]
    public void GameController_OnStateUpdated_SnakeReceived_EventFires()
    {
        RaiseEvent(_network, "OnPlayerIdReceived", 1);
        RaiseEvent(_network, "OnWorldSizeReceived", 2000);

        bool fired = false;
        _game.OnStateUpdated += () => fired = true;

        var snake = new Snake { Id = 1 };
        RaiseEvent(_network, "OnSnakeReceived", snake);

        Assert.IsTrue(fired);
    }

    // ==================== Additional Coverage ====================

    [TestMethod]
    public void GameController_OnPlayerDied_PlayerSnakeDies_EventFires()
    {
        RaiseEvent(_network, "OnPlayerIdReceived", 7);
        RaiseEvent(_network, "OnWorldSizeReceived", 2000);

        var snake = new Snake { Id = 7, Name = "Hero", Score = 0, Died = false, Alive = true };
        RaiseEvent(_network, "OnSnakeReceived", snake);

        bool fired = false;
        _game.OnPlayerDied += () => fired = true;

        var diedUpdate = new Snake { Id = 7, Died = true };
        RaiseEvent(_network, "OnSnakeReceived", diedUpdate);

        Assert.IsTrue(fired, "OnPlayerDied should fire when the player's snake receives Died=true.");
    }

    [TestMethod]
    public void GameController_SendMoveAsync_BeforeHandshake_IsNoOp()
    {
        // PlayerId and WorldSize are null — SendMoveAsync must return without throwing.
        var task = _game.SendMoveAsync("right");
        Assert.IsNotNull(task);
        Assert.IsTrue(task.IsCompleted, "SendMoveAsync before handshake should return a completed task.");
    }

    [TestMethod]
    public void GameController_GetPlayerSnake_PlayerIdSetKeyAbsent_ReturnsNull()
    {
        // Set the player ID but never add a snake with that ID.
        RaiseEvent(_network, "OnPlayerIdReceived", 99);
        RaiseEvent(_network, "OnWorldSizeReceived", 2000);
        // Commit pending state by adding a snake with a DIFFERENT id.
        var other = new Snake { Id = 1, Name = "Other", Alive = true };
        RaiseEvent(_network, "OnSnakeReceived", other);

        Assert.IsNull(_game.GetPlayerSnake(),
            "GetPlayerSnake should return null when player ID 99 has no snake in the collection.");
    }

    // ==================== Helpers ====================
    /// <summary>
    /// Raises an event on <see cref="NetworkController"/> via reflection,
    /// since we cannot connect to a real server in tests.
    /// </summary>
    private static void RaiseEvent<T>(NetworkController nc, string eventName, T arg)
    {
        var field = typeof(NetworkController).GetField(eventName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (field != null)
        {
            var del = field.GetValue(nc) as Delegate;
            del?.DynamicInvoke(arg);
            return;
        }

        // Events backed by event keyword — find the backing field
        var eventInfo = typeof(NetworkController).GetEvent(eventName);
        if (eventInfo != null)
        {
            var backingField = typeof(NetworkController).GetField(eventName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (backingField != null)
            {
                var del = backingField.GetValue(nc) as Delegate;
                del?.DynamicInvoke(arg);
                return;
            }
        }

        throw new InvalidOperationException($"Could not find event or field '{eventName}' on NetworkController");
    }
}
