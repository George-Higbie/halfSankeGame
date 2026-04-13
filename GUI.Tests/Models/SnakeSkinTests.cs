// <copyright file="SnakeSkinTests.cs" company="Snake PS9">
// Copyright (c) 2026 Alex Waldmann, George Higbie, & CS 3500 Course Staff + Associates. All rights reserved.
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
    private static SnakeSkin CreateCustomSkin() => new()
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

    // ==================== Defaults ====================

    [TestMethod]
    public void SnakeSkin_DefaultConstructor_Name_DefaultsToClassic()
    {
        var skin = new SnakeSkin();

        Assert.AreEqual("Classic", skin.Name);
    }

    [TestMethod]
    public void SnakeSkin_DefaultConstructor_PrimaryColors_DefaultToClassicPalette()
    {
        var skin = new SnakeSkin();

        Assert.AreEqual("#4caf50", skin.BodyColor);
        Assert.AreEqual("#4caf50", skin.HeadColor);
        Assert.AreEqual("white", skin.EyeColor);
        Assert.AreEqual("#111", skin.PupilColor);
        Assert.AreEqual("#4caf50", skin.DeathColor);
    }

    [TestMethod]
    public void SnakeSkin_DefaultConstructor_OptionalAccents_DefaultToNull()
    {
        var skin = new SnakeSkin();

        Assert.IsNull(skin.BodyAccent);
        Assert.IsNull(skin.BodyAccent2);
        Assert.IsNull(skin.BellyColor);
        Assert.IsNull(skin.OutlineColor);
    }

    [TestMethod]
    public void SnakeSkin_DefaultConstructor_Pattern_DefaultsToSolid()
    {
        var skin = new SnakeSkin();

        Assert.AreEqual(BodyPattern.Solid, skin.Pattern);
    }

    // ==================== AllSkins Collection ====================

    [TestMethod]
    public void SnakeSkin_AllSkins_StaticProperty_IsNotNull()
    {
        Assert.IsNotNull(SnakeSkin.AllSkins);
    }

    [TestMethod]
    public void SnakeSkin_AllSkins_StaticProperty_ContainsMultipleSkins()
    {
        Assert.IsGreaterThan(1, SnakeSkin.AllSkins.Length,
            "Expected more than one skin in AllSkins.");
    }

    [TestMethod]
    public void SnakeSkin_AllSkins_EachSkin_HasNonEmptyName()
    {
        foreach (var skin in SnakeSkin.AllSkins)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(skin.Name),
                "Every skin must have a non-empty Name.");
        }
    }

    [TestMethod]
    public void SnakeSkin_AllSkins_EachSkin_HasNonEmptyBodyColor()
    {
        foreach (var skin in SnakeSkin.AllSkins)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(skin.BodyColor),
                $"Skin '{skin.Name}' must have a BodyColor.");
        }
    }

    [TestMethod]
    public void SnakeSkin_AllSkins_EachSkin_HasNonEmptyHeadColor()
    {
        foreach (var skin in SnakeSkin.AllSkins)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(skin.HeadColor),
                $"Skin '{skin.Name}' must have a HeadColor.");
        }
    }

    [TestMethod]
    public void SnakeSkin_AllSkins_EachSkin_HasNonEmptyDeathColor()
    {
        foreach (var skin in SnakeSkin.AllSkins)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(skin.DeathColor),
                $"Skin '{skin.Name}' must have a DeathColor.");
        }
    }

    [TestMethod]
    public void SnakeSkin_AllSkins_AllSkinNames_AreUnique()
    {
        var names = SnakeSkin.AllSkins.Select(s => s.Name).ToList();
        var distinct = names.Distinct().ToList();

        Assert.HasCount(names.Count, distinct,
            "Skin names must be unique.");
    }

    [TestMethod]
    public void SnakeSkin_AllSkins_StripedSkins_HaveBodyAccent()
    {
        var striped = SnakeSkin.AllSkins.Where(s => s.Pattern == BodyPattern.Stripe);

        foreach (var skin in striped)
        {
            Assert.IsNotNull(skin.BodyAccent,
                $"Striped skin '{skin.Name}' must have a BodyAccent color.");
        }
    }

    [TestMethod]
    public void SnakeSkin_AllSkins_NonSolidPatterns_HaveBodyAccent()
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
    public void BodyPattern_EnumValues_AllVariants_HaveCorrectIntegerValues()
    {
        Assert.AreEqual(0, (int)BodyPattern.Solid);
        Assert.AreEqual(1, (int)BodyPattern.Stripe);
        Assert.AreEqual(2, (int)BodyPattern.Checker);
        Assert.AreEqual(3, (int)BodyPattern.Diamond);
        Assert.AreEqual(4, (int)BodyPattern.Wave);
    }

    // ==================== Init-Only Properties ====================

    [TestMethod]
    public void SnakeSkin_InitProperties_ObjectInitializer_SetsIdentityAndPatternFields()
    {
        var skin = CreateCustomSkin();

        Assert.AreEqual("Custom", skin.Name);
        Assert.AreEqual("#ff0000", skin.BodyColor);
        Assert.AreEqual("#00ff00", skin.BodyAccent);
        Assert.AreEqual("#0000ff", skin.BodyAccent2);
        Assert.AreEqual(BodyPattern.Diamond, skin.Pattern);
    }

    [TestMethod]
    public void SnakeSkin_InitProperties_ObjectInitializer_SetsSecondaryVisualFields()
    {
        var skin = CreateCustomSkin();

        Assert.AreEqual("#aaa", skin.BellyColor);
        Assert.AreEqual("#bbb", skin.OutlineColor);
    }

    [TestMethod]
    public void SnakeSkin_InitProperties_ObjectInitializer_SetsHeadAndEyeFields()
    {
        var skin = CreateCustomSkin();

        Assert.AreEqual("#ccc", skin.HeadColor);
        Assert.AreEqual("#ddd", skin.EyeColor);
        Assert.AreEqual("#eee", skin.PupilColor);
    }

    [TestMethod]
    public void SnakeSkin_InitProperties_ObjectInitializer_SetsDeathColor()
    {
        var skin = CreateCustomSkin();

        Assert.AreEqual("#fff", skin.DeathColor);
    }
}
