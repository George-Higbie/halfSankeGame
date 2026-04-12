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
    public void Constructor_NullNetwork_Throws()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => new GameController(null!));
    }

    // ==================== Initial State ====================

    [TestMethod]
    public void InitialState_PlayerIdIsNull()
    {
        Assert.IsNull(_game.PlayerId);
    }

    [TestMethod]
    public void InitialState_WorldSizeIsNull()
    {
        Assert.IsNull(_game.WorldSize);
    }

    [TestMethod]
    public void InitialState_SnakesAreEmpty()
    {
        Assert.AreEqual(0, _game.GetSnakes().Count);
    }

    [TestMethod]
    public void InitialState_WallsAreEmpty()
    {
        Assert.AreEqual(0, _game.GetWalls().Count);
    }

    [TestMethod]
    public void InitialState_PowerupsAreEmpty()
    {
        Assert.AreEqual(0, _game.GetPowerups().Count);
    }

    [TestMethod]
    public void InitialState_PlayerSnakeIsNull()
    {
        Assert.IsNull(_game.GetPlayerSnake());
    }

    // ==================== Event Handling ====================

    [TestMethod]
    public void OnPlayerIdReceived_SetsPlayerId()
    {
        RaiseEvent(_network, "OnPlayerIdReceived", 42);

        Assert.AreEqual(42, _game.PlayerId);
    }

    [TestMethod]
    public void OnWorldSizeReceived_SetsWorldSize()
    {
        RaiseEvent(_network, "OnWorldSizeReceived", 2000);

        Assert.AreEqual(2000, _game.WorldSize);
    }

    [TestMethod]
    public void OnWallReceived_AddsWall()
    {
        var wall = new Wall { Id = 1, Point1 = new Point2D(0, 0), Point2 = new Point2D(100, 0) };

        RaiseEvent(_network, "OnWallReceived", wall);

        Assert.AreEqual(1, _game.GetWalls().Count);
    }

    [TestMethod]
    public void OnSnakeReceived_AddsSnake()
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

        Assert.AreEqual(1, _game.GetSnakes().Count);
    }

    [TestMethod]
    public void OnPowerupReceived_AddsPowerup()
    {
        RaiseEvent(_network, "OnPlayerIdReceived", 1);
        RaiseEvent(_network, "OnWorldSizeReceived", 2000);

        // First snake to trigger commit
        var snake = new Snake { Id = 1, Name = "Test" };
        RaiseEvent(_network, "OnSnakeReceived", snake);

        var powerup = new Powerup { Id = 5, Location = new Point2D(50, 50), Died = false };
        RaiseEvent(_network, "OnPowerupReceived", powerup);

        Assert.AreEqual(1, _game.GetPowerups().Count);
    }

    [TestMethod]
    public void OnPowerupReceived_DiedPowerup_RemovesPowerup()
    {
        RaiseEvent(_network, "OnPlayerIdReceived", 1);
        RaiseEvent(_network, "OnWorldSizeReceived", 2000);

        var snake = new Snake { Id = 1, Name = "Test" };
        RaiseEvent(_network, "OnSnakeReceived", snake);

        var p = new Powerup { Id = 5, Location = new Point2D(50, 50), Died = false };
        RaiseEvent(_network, "OnPowerupReceived", p);
        Assert.AreEqual(1, _game.GetPowerups().Count);

        var pDied = new Powerup { Id = 5, Died = true };
        RaiseEvent(_network, "OnPowerupReceived", pDied);
        Assert.AreEqual(0, _game.GetPowerups().Count);
    }

    // ==================== Snapshots Are Deep Copies ====================

    [TestMethod]
    public void GetSnakes_ReturnsDeepCopy()
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
    public void GetPlayerSnake_ReturnsDeepCopy()
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
    public void Disconnect_ClearsAllState()
    {
        RaiseEvent(_network, "OnPlayerIdReceived", 1);
        RaiseEvent(_network, "OnWorldSizeReceived", 2000);

        _game.Disconnect();

        Assert.IsNull(_game.PlayerId);
        Assert.IsNull(_game.WorldSize);
        Assert.AreEqual(0, _game.GetSnakes().Count);
        Assert.AreEqual(0, _game.GetWalls().Count);
        Assert.AreEqual(0, _game.GetPowerups().Count);
    }

    // ==================== Snake State Merging ====================

    [TestMethod]
    public void OnSnakeReceived_UpdateExisting_MergesFields()
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
    public void OnSnakeReceived_DisconnectedSnake_RemovesIt()
    {
        RaiseEvent(_network, "OnPlayerIdReceived", 1);
        RaiseEvent(_network, "OnWorldSizeReceived", 2000);

        var snake = new Snake { Id = 2, Name = "Other", Alive = true };
        RaiseEvent(_network, "OnSnakeReceived", snake);
        Assert.AreEqual(1, _game.GetSnakes().Count);

        var dc = new Snake { Id = 2, Disconnected = true };
        RaiseEvent(_network, "OnSnakeReceived", dc);
        Assert.AreEqual(0, _game.GetSnakes().Count);
    }

    // ==================== OnStateUpdated Event ====================

    [TestMethod]
    public void OnStateUpdated_FiresWhenSnakeReceived()
    {
        RaiseEvent(_network, "OnPlayerIdReceived", 1);
        RaiseEvent(_network, "OnWorldSizeReceived", 2000);

        bool fired = false;
        _game.OnStateUpdated += () => fired = true;

        var snake = new Snake { Id = 1 };
        RaiseEvent(_network, "OnSnakeReceived", snake);

        Assert.IsTrue(fired);
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
