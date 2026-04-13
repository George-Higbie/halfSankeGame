// <copyright file="WallTests.cs" company="Snake PS9">
// Copyright (c) 2026 Alex Waldmann, George Higbie, & CS 3500 Course Staff + Associates. All rights reserved.
// </copyright>
// Author: Alex Waldmann
// Date: 2026-04-12

using GUI.Components.Models;

namespace GUI.Tests.Models;

/// <summary>
/// Tests for <see cref="Wall"/>.
/// </summary>
[TestClass]
public class WallTests
{
    [TestMethod]
    public void Wall_DefaultConstructor_NoArgs_HasZeroIdAndNullEndpoints()
    {
        var w = new Wall();

        Assert.AreEqual(0, w.Id);
        Assert.IsNull(w.Point1);
        Assert.IsNull(w.Point2);
    }

    [TestMethod]
    public void Wall_Properties_SetAllValues_ReflectsCorrectly()
    {
        var w = new Wall
        {
            Id = 3,
            Point1 = new Point2D(-50, 0),
            Point2 = new Point2D(50, 0)
        };

        Assert.AreEqual(3, w.Id);
        Assert.AreEqual(-50, w.Point1!.X);
        Assert.AreEqual(0, w.Point1.Y);
        Assert.AreEqual(50, w.Point2!.X);
        Assert.AreEqual(0, w.Point2.Y);
    }

    [TestMethod]
    public void Wall_Point1Point2_VerticalOrientation_SameXCoordinate()
    {
        var w = new Wall
        {
            Point1 = new Point2D(100, -200),
            Point2 = new Point2D(100, 200)
        };

        Assert.AreEqual(w.Point1!.X, w.Point2!.X);
    }

    [TestMethod]
    public void Wall_Point1Point2_HorizontalOrientation_SameYCoordinate()
    {
        var w = new Wall
        {
            Point1 = new Point2D(-300, 50),
            Point2 = new Point2D(300, 50)
        };

        Assert.AreEqual(w.Point1!.Y, w.Point2!.Y);
    }
    [TestMethod]
    public void Wall_BothPoints_Equal_DegenerateWall_PointsAreEqual()
    {
        var w = new Wall
        {
            Point1 = new Point2D(50, 50),
            Point2 = new Point2D(50, 50)
        };

        Assert.AreEqual(w.Point1!.X, w.Point2!.X);
        Assert.AreEqual(w.Point1!.Y, w.Point2!.Y);
    }
}
