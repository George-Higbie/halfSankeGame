// <copyright file="PowerupTests.cs" company="Snake PS9">
// Copyright (c) 2026 Alex Waldmann, George Higbie, & CS 3500 Course Staff + Associates. All rights reserved.
// </copyright>
// Author: Alex Waldmann
// Date: 2026-04-12

using GUI.Components.Models;

namespace GUI.Tests.Models;

/// <summary>
/// Tests for <see cref="Powerup"/>.
/// </summary>
[TestClass]
public class PowerupTests
{
    [TestMethod]
    public void Powerup_DefaultConstructor_NoArgs_HasZeroIdAndNullLocation()
    {
        var p = new Powerup();

        Assert.AreEqual(0, p.Id);
        Assert.IsNull(p.Location);
        Assert.IsFalse(p.Died);
    }

    [TestMethod]
    public void Powerup_Properties_SetAllValues_ReflectsCorrectly()
    {
        var p = new Powerup
        {
            Id = 7,
            Location = new Point2D(100, 200),
            Died = true
        };

        Assert.AreEqual(7, p.Id);
        Assert.AreEqual(100, p.Location!.X);
        Assert.AreEqual(200, p.Location.Y);
        Assert.IsTrue(p.Died);
    }

    [TestMethod]
    public void Powerup_Died_DefaultFalse_CanBeSetTrue()
    {
        var p = new Powerup();

        Assert.IsFalse(p.Died);

        p.Died = true;

        Assert.IsTrue(p.Died);
    }
    [TestMethod]
    public void Powerup_Id_LargeValue_RoundtripsWithoutLoss()
    {
        var p = new Powerup { Id = int.MaxValue };

        Assert.AreEqual(int.MaxValue, p.Id);
    }
}
