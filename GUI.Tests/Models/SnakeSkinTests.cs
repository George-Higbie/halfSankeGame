// <copyright file="SnakeSkinTests.cs" company="Snake PS9">
// Copyright (c) 2026 Alex Waldmann & George Higbie. All rights reserved.
// </copyright>
// Author: Alex Waldmann
// Date: 2026-04-12

using GUI.Components.Models;

namespace GUI.Tests.Models;

/// <summary>
/// Tests for <see cref="SnakeSkin"/> and <see cref="BodyPattern"/>.
/// </summary>
[TestClass]
public class SnakeSkinTests
{
    // ==================== Defaults ====================

    [TestMethod]
    public void DefaultSkin_HasClassicValues()
    {
        var skin = new SnakeSkin();

        Assert.AreEqual("Classic", skin.Name);
        Assert.AreEqual("#4caf50", skin.BodyColor);
        Assert.IsNull(skin.BodyAccent);
        Assert.IsNull(skin.BodyAccent2);
        Assert.AreEqual(BodyPattern.Solid, skin.Pattern);
        Assert.IsNull(skin.BellyColor);
        Assert.IsNull(skin.OutlineColor);
        Assert.AreEqual("#4caf50", skin.HeadColor);
        Assert.AreEqual("white", skin.EyeColor);
        Assert.AreEqual("#111", skin.PupilColor);
        Assert.AreEqual("#4caf50", skin.DeathColor);
    }

    // ==================== AllSkins Collection ====================

    [TestMethod]
    public void AllSkins_IsNotNull()
    {
        Assert.IsNotNull(SnakeSkin.AllSkins);
    }

    [TestMethod]
    public void AllSkins_ContainsMultipleSkins()
    {
        Assert.IsTrue(SnakeSkin.AllSkins.Length > 1,
            "Expected more than one skin in AllSkins.");
    }

    [TestMethod]
    public void AllSkins_AllHaveNonEmptyNames()
    {
        foreach (var skin in SnakeSkin.AllSkins)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(skin.Name),
                "Every skin must have a non-empty Name.");
        }
    }

    [TestMethod]
    public void AllSkins_AllHaveNonEmptyBodyColor()
    {
        foreach (var skin in SnakeSkin.AllSkins)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(skin.BodyColor),
                $"Skin '{skin.Name}' must have a BodyColor.");
        }
    }

    [TestMethod]
    public void AllSkins_AllHaveNonEmptyHeadColor()
    {
        foreach (var skin in SnakeSkin.AllSkins)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(skin.HeadColor),
                $"Skin '{skin.Name}' must have a HeadColor.");
        }
    }

    [TestMethod]
    public void AllSkins_AllHaveNonEmptyDeathColor()
    {
        foreach (var skin in SnakeSkin.AllSkins)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(skin.DeathColor),
                $"Skin '{skin.Name}' must have a DeathColor.");
        }
    }

    [TestMethod]
    public void AllSkins_NamesAreUnique()
    {
        var names = SnakeSkin.AllSkins.Select(s => s.Name).ToList();
        var distinct = names.Distinct().ToList();

        Assert.AreEqual(names.Count, distinct.Count,
            "Skin names must be unique.");
    }

    [TestMethod]
    public void AllSkins_StripedSkinsHaveAccent()
    {
        var striped = SnakeSkin.AllSkins.Where(s => s.Pattern == BodyPattern.Stripe);

        foreach (var skin in striped)
        {
            Assert.IsNotNull(skin.BodyAccent,
                $"Striped skin '{skin.Name}' must have a BodyAccent color.");
        }
    }

    [TestMethod]
    public void AllSkins_NonSolidPatternsHaveAccent()
    {
        var patterned = SnakeSkin.AllSkins.Where(s => s.Pattern != BodyPattern.Solid);

        foreach (var skin in patterned)
        {
            Assert.IsNotNull(skin.BodyAccent,
                $"Patterned skin '{skin.Name}' ({skin.Pattern}) must have a BodyAccent.");
        }
    }

    // ==================== BodyPattern Enum ====================

    [TestMethod]
    public void BodyPattern_HasExpectedValues()
    {
        Assert.AreEqual(0, (int)BodyPattern.Solid);
        Assert.AreEqual(1, (int)BodyPattern.Stripe);
        Assert.AreEqual(2, (int)BodyPattern.Checker);
        Assert.AreEqual(3, (int)BodyPattern.Diamond);
        Assert.AreEqual(4, (int)BodyPattern.Wave);
    }

    // ==================== Init-Only Properties ====================

    [TestMethod]
    public void InitProperties_CanBeSetViaObjectInitializer()
    {
        var skin = new SnakeSkin
        {
            Name = "Custom",
            BodyColor = "#ff0000",
            BodyAccent = "#00ff00",
            BodyAccent2 = "#0000ff",
            Pattern = BodyPattern.Diamond,
            BellyColor = "#aaa",
            OutlineColor = "#bbb",
            HeadColor = "#ccc",
            EyeColor = "#ddd",
            PupilColor = "#eee",
            DeathColor = "#fff"
        };

        Assert.AreEqual("Custom", skin.Name);
        Assert.AreEqual("#ff0000", skin.BodyColor);
        Assert.AreEqual("#00ff00", skin.BodyAccent);
        Assert.AreEqual("#0000ff", skin.BodyAccent2);
        Assert.AreEqual(BodyPattern.Diamond, skin.Pattern);
        Assert.AreEqual("#aaa", skin.BellyColor);
        Assert.AreEqual("#bbb", skin.OutlineColor);
        Assert.AreEqual("#ccc", skin.HeadColor);
        Assert.AreEqual("#ddd", skin.EyeColor);
        Assert.AreEqual("#eee", skin.PupilColor);
        Assert.AreEqual("#fff", skin.DeathColor);
    }
}
