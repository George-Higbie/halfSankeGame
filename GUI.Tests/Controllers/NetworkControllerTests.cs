// <copyright file="NetworkControllerTests.cs" company="Snake PS9">
// Copyright (c) 2026 Alex Waldmann, George Higbie, & CS 3500 Course Staff + Associates. All rights reserved.
// </copyright>
// Author: Alex Waldmann
// Date: 2026-04-12

using GUI.Components.Controllers;

namespace GUI.Tests.Controllers;

/// <summary>
/// Tests focused on argument validation, connection-state guards, and lifecycle behavior
/// in <see cref="NetworkController"/> without opening real sockets.
/// </summary>
[TestClass]
public class NetworkControllerTests
{
    [TestMethod]
    public async Task NetworkController_ConnectAsync_NullHost_ThrowsArgumentNullException()
    {
        using var nc = new NetworkController();

        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            async () => await nc.ConnectAsync(null!, 11000, "player"));
    }

    [TestMethod]
    public async Task NetworkController_ConnectAsync_NullPlayerName_ThrowsArgumentNullException()
    {
        using var nc = new NetworkController();

        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            async () => await nc.ConnectAsync("localhost", 11000, null!));
    }

    [TestMethod]
    public async Task NetworkController_SendMoveAsync_NullMoving_ThrowsArgumentNullException()
    {
        using var nc = new NetworkController();

        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            async () => await nc.SendMoveAsync(null!));
    }

    [TestMethod]
    public async Task NetworkController_SendMoveAsync_NoWriter_ReturnsWithoutThrowing()
    {
        using var nc = new NetworkController();

        await nc.SendMoveAsync("right");

        Assert.IsFalse(nc.IsConnected);
    }

    [TestMethod]
    public async Task NetworkController_ReconnectAsync_NoPriorConnection_ThrowsInvalidOperationException()
    {
        using var nc = new NetworkController();

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            async () => await nc.ReconnectAsync());
    }

    [TestMethod]
    public void NetworkController_Disconnect_InvokesOnDisconnected()
    {
        using var nc = new NetworkController();
        var fired = false;
        nc.OnDisconnected += () => fired = true;

        nc.Disconnect();

        Assert.IsTrue(fired);
    }

    [TestMethod]
    public void NetworkController_Disconnect_ResetsHandshakeStateToNull()
    {
        using var nc = new NetworkController();

        nc.Disconnect();

        Assert.IsNull(nc.PlayerId);
        Assert.IsNull(nc.WorldSize);
    }

    [TestMethod]
    public void NetworkController_NewInstance_IsNotConnected()
    {
        using var nc = new NetworkController();

        Assert.IsFalse(nc.IsConnected);
    }
}
