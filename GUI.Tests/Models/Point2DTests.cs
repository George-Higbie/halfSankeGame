// <copyright file="Point2DTests.cs" company="Snake PS9">
// Copyright (c) 2026 Alex Waldmann & George Higbie. All rights reserved.
// </copyright>
// Author: Alex Waldmann
// Date: 2026-04-12

using GUI.Components.Models;

namespace GUI.Tests.Models;

/// <summary>
/// Tests for <see cref="Point2D"/>.
/// </summary>
[TestClass]
public class Point2DTests
{
    // ==================== Constructor ====================

    [TestMethod]
    public void DefaultConstructor_SetsXAndYToZero()
    {
        var p = new Point2D();

        Assert.AreEqual(0, p.X);
        Assert.AreEqual(0, p.Y);
    }

    [TestMethod]
    public void ParameterizedConstructor_SetsXAndY()
    {
        var p = new Point2D(42, -7);

        Assert.AreEqual(42, p.X);
        Assert.AreEqual(-7, p.Y);
    }

    [TestMethod]
    public void ParameterizedConstructor_NegativeValues_PreservesSign()
    {
        var p = new Point2D(-100, -200);

        Assert.AreEqual(-100, p.X);
        Assert.AreEqual(-200, p.Y);
    }

    [TestMethod]
    public void ParameterizedConstructor_ZeroValues_SetsToZero()
    {
        var p = new Point2D(0, 0);

        Assert.AreEqual(0, p.X);
        Assert.AreEqual(0, p.Y);
    }

    // ==================== Mutability ====================

    [TestMethod]
    public void SetX_UpdatesValue()
    {
        var p = new Point2D(1, 2);

        p.X = 99;

        Assert.AreEqual(99, p.X);
        Assert.AreEqual(2, p.Y);
    }

    [TestMethod]
    public void SetY_UpdatesValue()
    {
        var p = new Point2D(1, 2);

        p.Y = 99;

        Assert.AreEqual(1, p.X);
        Assert.AreEqual(99, p.Y);
    }

    // ==================== Boundary ====================

    [TestMethod]
    public void LargeValues_DoNotOverflow()
    {
        var p = new Point2D(int.MaxValue, int.MinValue);

        Assert.AreEqual(int.MaxValue, p.X);
        Assert.AreEqual(int.MinValue, p.Y);
    }
}
