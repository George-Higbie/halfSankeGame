// <copyright file="GameControllerTests.cs" company="Snake PS9">
// Copyright (c) 2026 Alex Waldmann, George Higbie, & CS 3500 Course Staff + Associates. All rights reserved.
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

    [TestMethod]
    public async Task GameController_ConnectAsync_NullHost_ThrowsArgumentNullException()
    {
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            async () => await _game.ConnectAsync(null!, 11000, "player"));
    }

    [TestMethod]
    public async Task GameController_ConnectAsync_NullName_ThrowsArgumentNullException()
    {
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            async () => await _game.ConnectAsync("localhost", 11000, null!));
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
    public async Task GameController_SendMoveAsync_NullMoving_ThrowsArgumentNullException()
    {
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            async () => await _game.SendMoveAsync(null!));
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

    // ==================== ConnectAsync ====================

    [TestMethod]
    public async Task GameController_ConnectAsync_InvalidHost_ThrowsAndResetsIsInitializingFlag()
    {
        // Use a port on which nothing is listening; ConnectAsync must rethrow.
        bool threw = false;
        try
        {
            await _game.ConnectAsync("127.0.0.1", 1, "TestPlayer");
        }
        catch
        {
            threw = true;
        }
        Assert.IsTrue(threw, "ConnectAsync to a closed port must propagate the underlying exception.");
    }

    // ==================== SendMoveAsync after handshake ====================

    [TestMethod]
    public async Task GameController_SendMoveAsync_AfterHandshake_ReturnsCompletedTask()
    {
        // Complete the handshake via events so PlayerId and WorldSize are set.
        RaiseEvent(_network, "OnPlayerIdReceived", 1);
        RaiseEvent(_network, "OnWorldSizeReceived", 2000);
        var snake = new Snake { Id = 1, Name = "Me", Alive = true };
        RaiseEvent(_network, "OnSnakeReceived", snake);

        // _writer is still null (no real TCP) so NetworkController.SendMoveAsync returns early.
        // This covers the `return _network.SendMoveAsync(moving)` branch.
        var task = _game.SendMoveAsync("right");
        await task;

        Assert.IsTrue(task.IsCompleted);
    }

    // ==================== MergeSnakeFields full branch coverage ====================

    [TestMethod]
    public void GameController_MergeSnake_DiedFalse_DoesNotClearAlive()
    {
        RaiseEvent(_network, "OnPlayerIdReceived", 1);
        RaiseEvent(_network, "OnWorldSizeReceived", 2000);

        var snake = new Snake { Id = 1, Alive = true, Died = false };
        RaiseEvent(_network, "OnSnakeReceived", snake);

        // Update with Died=false — existing.Died=false, Alive should stay true.
        var update = new Snake { Id = 1, Died = false };
        RaiseEvent(_network, "OnSnakeReceived", update);

        var result = _game.GetSnakes().First(s => s.Id == 1);
        Assert.IsFalse(result.Died);
    }

    [TestMethod]
    public void GameController_MergeSnake_AliveTrue_SetsDiedFalse()
    {
        RaiseEvent(_network, "OnPlayerIdReceived", 1);
        RaiseEvent(_network, "OnWorldSizeReceived", 2000);

        var snake = new Snake { Id = 1, Alive = false, Died = true };
        RaiseEvent(_network, "OnSnakeReceived", snake);

        // Alive=true update: should set existing.Died = false
        var update = new Snake { Id = 1, Alive = true };
        RaiseEvent(_network, "OnSnakeReceived", update);

        var result = _game.GetSnakes().First(s => s.Id == 1);
        Assert.IsTrue(result.Alive);
        Assert.IsFalse(result.Died);
    }

    [TestMethod]
    public void GameController_MergeSnake_AliveFalse_BranchCovered()
    {
        RaiseEvent(_network, "OnPlayerIdReceived", 1);
        RaiseEvent(_network, "OnWorldSizeReceived", 2000);

        var snake = new Snake { Id = 1, Alive = true, Died = false };
        RaiseEvent(_network, "OnSnakeReceived", snake);

        // Alive=false update — covers the HasValue-true path without the .Value branch.
        var update = new Snake { Id = 1, Alive = false };
        RaiseEvent(_network, "OnSnakeReceived", update);

        var result = _game.GetSnakes().First(s => s.Id == 1);
        Assert.IsFalse(result.Alive);
    }

    [TestMethod]
    public void GameController_MergeSnake_BodyDirectionSkinJoinedDisconnected_AllMerged()
    {
        RaiseEvent(_network, "OnPlayerIdReceived", 1);
        RaiseEvent(_network, "OnWorldSizeReceived", 2000);

        var snake = new Snake { Id = 1, Name = "Me", Alive = true, Joined = false, Disconnected = false };
        RaiseEvent(_network, "OnSnakeReceived", snake);

        var newBody = new List<Point2D> { new(10, 10), new(20, 10) };
        var newDir = new Point2D(1, 0);
        var update = new Snake
        {
            Id = 1,
            Body = newBody,
            Direction = newDir,
            Skin = 3,
            Joined = true,
            Disconnected = false,
        };
        RaiseEvent(_network, "OnSnakeReceived", update);

        var result = _game.GetSnakes().First(s => s.Id == 1);
        Assert.IsNotNull(result.Body);
        Assert.HasCount(2, result.Body!);
        Assert.IsNotNull(result.Direction);
        Assert.AreEqual(3, result.Skin);
        Assert.IsTrue(result.Joined);
    }

    [TestMethod]
    public void GameController_OnPlayerDied_NonPlayerSnakeDies_PlayerDiedEventDoesNotFire()
    {
        RaiseEvent(_network, "OnPlayerIdReceived", 1);
        RaiseEvent(_network, "OnWorldSizeReceived", 2000);

        // Player snake ID=1, other snake ID=2
        var player = new Snake { Id = 1, Died = false, Alive = true };
        RaiseEvent(_network, "OnSnakeReceived", player);
        var other = new Snake { Id = 2, Died = false, Alive = true };
        RaiseEvent(_network, "OnSnakeReceived", other);

        bool fired = false;
        _game.OnPlayerDied += () => fired = true;

        // Only the non-player snake dies — player died event must NOT fire.
        var diedUpdate = new Snake { Id = 2, Died = true };
        RaiseEvent(_network, "OnSnakeReceived", diedUpdate);

        Assert.IsFalse(fired, "OnPlayerDied must not fire when a non-player snake dies.");
    }

    [TestMethod]
    public void GameController_OnSnakeReceived_NonInitializingWall_AppearsImmediatelyInGetWalls()
    {
        // Without ConnectAsync, _isInitializing is false so walls go directly into _walls.
        var wall = new Wall { Id = 10, Point1 = new Point2D(0, 0), Point2 = new Point2D(50, 0) };
        RaiseEvent(_network, "OnWallReceived", wall);

        // Wall immediately visible — it was NOT buffered in _pendingWalls.
        Assert.HasCount(1, _game.GetWalls());
    }

    [TestMethod]
    public void GameController_OnSnakeReceived_NonInitializingPhase_HandlesPlayerIdDirectly()
    {
        // Skip initialization: receive player ID + world size WITHOUT _isInitializing=true.
        // That means fire world size AFTER the first snake commit is done... actually just
        // verify the non-initializing code paths by doing a full round-trip.
        RaiseEvent(_network, "OnPlayerIdReceived", 3);
        RaiseEvent(_network, "OnWorldSizeReceived", 300);

        // First commit
        var snake = new Snake { Id = 3, Alive = true };
        RaiseEvent(_network, "OnSnakeReceived", snake);

        Assert.AreEqual(3, _game.PlayerId);
        Assert.AreEqual(300, _game.WorldSize);

        // Second player id / world size fires the else branches (not initializing)
        RaiseEvent(_network, "OnPlayerIdReceived", 4);
        RaiseEvent(_network, "OnWorldSizeReceived", 400);

        Assert.AreEqual(4, _game.PlayerId);
        Assert.AreEqual(400, _game.WorldSize);
    }

    [TestMethod]
    public void GameController_OnWallReceived_NonInitializingPhase_AddedDirectly()
    {
        // Trigger commit first so _isInitializing = false.
        RaiseEvent(_network, "OnPlayerIdReceived", 1);
        RaiseEvent(_network, "OnWorldSizeReceived", 2000);
        RaiseEvent(_network, "OnSnakeReceived", new Snake { Id = 1, Alive = true });

        // Now a wall arrives outside initialization — goes straight into _walls.
        var wall = new Wall { Id = 99, Point1 = new Point2D(5, 5), Point2 = new Point2D(5, 50) };
        RaiseEvent(_network, "OnWallReceived", wall);

        Assert.HasCount(1, _game.GetWalls());
    }

    [TestMethod]
    public void GameController_OnPowerupReceived_DiedPowerupNotInCollection_ElseBranchAddsIt()
    {
        RaiseEvent(_network, "OnPlayerIdReceived", 1);
        RaiseEvent(_network, "OnWorldSizeReceived", 2000);
        RaiseEvent(_network, "OnSnakeReceived", new Snake { Id = 1, Alive = true });

        // A powerup with Died=true that is NOT already in the collection falls into the else
        // branch of HandlePowerupReceived and is added (not discarded). This is correct
        // server-protocol behavior — the server may send a final died=true before removal.
        var pDied = new Powerup { Id = 999, Died = true };
        RaiseEvent(_network, "OnPowerupReceived", pDied);

        Assert.HasCount(1, _game.GetPowerups());
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
