// <copyright file="SnakeTests.cs" company="Snake PS9">
// Copyright (c) 2026 Alex Waldmann, George Higbie, & CS 3500 Course Staff + Associates. All rights reserved.
// </copyright>
// Author: Alex Waldmann
// Date: 2026-04-12

using GUI.Components.Models;

namespace GUI.Tests.Models;

/// <summary>
/// Tests for <see cref="Snake"/>.
/// </summary>
[TestClass]
public class SnakeTests
{
    // ==================== Default State ====================

    [TestMethod]
    public void Snake_DefaultConstructor_NoArgs_AllPropertiesAreDefault()
    {
        var s = new Snake();

        Assert.AreEqual(0, s.Id);
        Assert.IsNull(s.Name);
        Assert.IsNull(s.Body);
        Assert.IsNull(s.Direction);
        Assert.IsNull(s.Score);
        Assert.IsNull(s.Died);
        Assert.IsNull(s.Alive);
        Assert.IsNull(s.Disconnected);
        Assert.IsNull(s.Joined);
        Assert.IsNull(s.Skin);
    }

    // ==================== Clone ====================

    [TestMethod]
    public void Snake_Clone_AllScalarFields_DeepCopiesValues()
    {
        var original = new Snake
        {
            Id = 5,
            Name = "TestSnake",
            Score = 42,
            Died = false,
            Alive = true,
            Disconnected = false,
            Joined = true,
            Skin = 3
        };

        var clone = original.Clone();

        Assert.AreEqual(5, clone.Id);
        Assert.AreEqual("TestSnake", clone.Name);
        Assert.AreEqual(42, clone.Score);
        Assert.IsFalse(clone.Died);
        Assert.IsTrue(clone.Alive);
        Assert.IsFalse(clone.Disconnected);
        Assert.IsTrue(clone.Joined);
        Assert.AreEqual(3, clone.Skin);
    }

    [TestMethod]
    public void Snake_Clone_BodyList_MutatingOriginalDoesNotAffectClone()
    {
        var original = new Snake
        {
            Body = new List<Point2D>
            {
                new(0, 0),
                new(10, 0),
                new(20, 0)
            }
        };

        var clone = original.Clone();

        // Mutate original body
        original.Body.Add(new Point2D(30, 0));

        Assert.HasCount(4, original.Body);
        Assert.HasCount(3, clone.Body!);
    }

    [TestMethod]
    public void Snake_Clone_BodyPoints_MutatingOriginalPointDoesNotAffectClonePoint()
    {
        var original = new Snake
        {
            Body = new List<Point2D>
            {
                new(0, 0),
                new(10, 0)
            }
        };

        var clone = original.Clone();

        original.Body[0].X = 99;

        Assert.AreEqual(99, original.Body[0].X);
        Assert.AreEqual(0, clone.Body![0].X);
        Assert.AreNotSame(original.Body[0], clone.Body[0]);
    }

    [TestMethod]
    public void Snake_Clone_Direction_MutatingOriginalDoesNotAffectClone()
    {
        var original = new Snake
        {
            Direction = new Point2D(1, 0)
        };

        var clone = original.Clone();

        // Mutate original direction
        original.Direction.X = -1;

        Assert.AreEqual(-1, original.Direction.X);
        Assert.AreEqual(1, clone.Direction!.X);
    }

    [TestMethod]
    public void Snake_Clone_NullBody_CloneBodyIsNull()
    {
        var original = new Snake { Body = null };

        var clone = original.Clone();

        Assert.IsNull(clone.Body);
    }

    [TestMethod]
    public void Snake_Clone_NullDirection_CloneDirectionIsNull()
    {
        var original = new Snake { Direction = null };

        var clone = original.Clone();

        Assert.IsNull(clone.Direction);
    }

    [TestMethod]
    public void Snake_Clone_AnySnake_ReturnsNewInstance()
    {
        var original = new Snake { Id = 1 };

        var clone = original.Clone();

        Assert.AreNotSame(original, clone);
    }

    // ==================== Property Assignment ====================

    [TestMethod]
    public void Snake_Properties_NullableBooleans_CanBeSetToTrue()
    {
        var s = new Snake
        {
            Died = true,
            Alive = true,
            Disconnected = true,
            Joined = true
        };

        Assert.IsTrue(s.Died!.Value);
        Assert.IsTrue(s.Alive!.Value);
        Assert.IsTrue(s.Disconnected!.Value);
        Assert.IsTrue(s.Joined!.Value);
    }
}
