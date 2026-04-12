// <copyright file="SnakeSkin.cs" company="Snake PS9">
// Copyright (c) 2026 Alex Waldmann & George Higbie. All rights reserved.
// </copyright>
// Authors: Alex Waldmann, George Higbie
// Date: 2026-04-12

namespace GUI.Components.Models
{
    /// <summary>
    /// Defines the visual appearance of a snake including body pattern.
    /// To add a new skin, append an entry to <see cref="AllSkins"/>.
    /// </summary>
    public class SnakeSkin
    {
        /// <summary>Human-readable skin name shown in the picker.</summary>
        public string Name { get; init; } = "Classic";

        /// <summary>Primary body color.</summary>
        public string BodyColor { get; init; } = "#4caf50";

        /// <summary>Secondary color for stripe/checker/diamond patterns. Null = solid body.</summary>
        public string? BodyAccent { get; init; }

        /// <summary>Third color used alternating with accent in multi-color patterns.</summary>
        public string? BodyAccent2 { get; init; }

        /// <summary>Body pattern type rendered on the canvas.</summary>
        public BodyPattern Pattern { get; init; } = BodyPattern.Solid;

        /// <summary>Belly (underside) highlight color drawn as a thinner line on top of the body.</summary>
        public string? BellyColor { get; init; }

        /// <summary>Outline/border color around the body. Null = dark semi-transparent default.</summary>
        public string? OutlineColor { get; init; }

        /// <summary>Color of the head circle.</summary>
        public string HeadColor { get; init; } = "#4caf50";

        /// <summary>Sclera (white of eye) color.</summary>
        public string EyeColor { get; init; } = "white";

        /// <summary>Pupil color.</summary>
        public string PupilColor { get; init; } = "#111";

        /// <summary>Color used for death-explosion particles.</summary>
        public string DeathColor { get; init; } = "#4caf50";

        /// <summary>
        /// All available skins. Add new skins here — they automatically appear in the picker.
        /// </summary>
        public static readonly SnakeSkin[] AllSkins =
        {
            // ── Classic / clean ──
            new() { Name = "Classic",      BodyColor = "#4caf50", BodyAccent = "#2e7d32",  Pattern = BodyPattern.Solid,   BellyColor = "#a5d6a7", HeadColor = "#388e3c", DeathColor = "#4caf50" },
            new() { Name = "Ocean",        BodyColor = "#1e88e5", BodyAccent = "#0d47a1",  Pattern = BodyPattern.Solid,   BellyColor = "#90caf9", HeadColor = "#1565c0", DeathColor = "#1e88e5" },
            new() { Name = "Shadow",       BodyColor = "#37474f", BodyAccent = "#263238",  Pattern = BodyPattern.Solid,   BellyColor = "#78909c", HeadColor = "#263238", DeathColor = "#455a64", EyeColor = "#ff1744" },

            // ── Striped (multi-color) ──
            new() { Name = "Coral Snake",  BodyColor = "#e53935", BodyAccent = "#fff9c4",  BodyAccent2 = "#212121", Pattern = BodyPattern.Stripe,  BellyColor = "#ef9a9a", HeadColor = "#212121", DeathColor = "#e53935", OutlineColor = "#1a1a1a" },
            new() { Name = "Bumblebee",    BodyColor = "#fdd835", BodyAccent = "#212121",  Pattern = BodyPattern.Stripe,  BellyColor = "#fff9c4", HeadColor = "#212121", DeathColor = "#fdd835", OutlineColor = "#1a1a1a" },
            new() { Name = "Candy Cane",   BodyColor = "#ffffff", BodyAccent = "#d32f2f",  Pattern = BodyPattern.Stripe,  BellyColor = "#ffcdd2", HeadColor = "#d32f2f", DeathColor = "#ef5350", OutlineColor = "#bdbdbd" },
            new() { Name = "Tiger",        BodyColor = "#ef6c00", BodyAccent = "#1a1a1a",  Pattern = BodyPattern.Stripe,  BellyColor = "#ffe0b2", HeadColor = "#e65100", DeathColor = "#ff9800", OutlineColor = "#3e2723" },

            // ── Diamond / scale ──
            new() { Name = "Emerald",      BodyColor = "#00897b", BodyAccent = "#004d40",  BodyAccent2 = "#a5d6a7", Pattern = BodyPattern.Diamond, BellyColor = "#80cbc4", HeadColor = "#004d40", DeathColor = "#009688" },
            new() { Name = "Amethyst",     BodyColor = "#8e24aa", BodyAccent = "#4a148c",  BodyAccent2 = "#e1bee7", Pattern = BodyPattern.Diamond, BellyColor = "#ce93d8", HeadColor = "#6a1b9a", DeathColor = "#9c27b0", EyeColor = "#b9f6ca", PupilColor = "#1b5e20" },

            // ── Checker ──
            new() { Name = "Neon",         BodyColor = "#e91e63", BodyAccent = "#f50057",  BodyAccent2 = "#ff80ab", Pattern = BodyPattern.Checker, BellyColor = "#f8bbd0", HeadColor = "#c2185b", DeathColor = "#e91e63" },
            new() { Name = "Arctic",       BodyColor = "#e0f7fa", BodyAccent = "#00acc1",  Pattern = BodyPattern.Checker, BellyColor = "#b2ebf2", HeadColor = "#00838f", DeathColor = "#00acc1" },

            // ── Wave ──
            new() { Name = "Void",         BodyColor = "#212121", BodyAccent = "#7c4dff",  BodyAccent2 = "#ea80fc", Pattern = BodyPattern.Wave,    BellyColor = "#424242", HeadColor = "#311b92", DeathColor = "#6200ea", PupilColor = "#7c4dff" },
            new() { Name = "Ember",        BodyColor = "#ff6d00", BodyAccent = "#ffab00",  BodyAccent2 = "#d50000", Pattern = BodyPattern.Wave,    BellyColor = "#ffe0b2", HeadColor = "#e65100", DeathColor = "#ff6d00" },

            // ── RGB stripe ──
            new() { Name = "Rainbow",      BodyColor = "#f44336", BodyAccent = "#4caf50",  BodyAccent2 = "#2196f3", Pattern = BodyPattern.Stripe,  BellyColor = "#fff9c4", HeadColor = "#9c27b0", DeathColor = "#ff9800" },
            new() { Name = "Mint Choc",    BodyColor = "#4db6ac", BodyAccent = "#4e342e",  BodyAccent2 = "#efebe9", Pattern = BodyPattern.Stripe,  BellyColor = "#b2dfdb", HeadColor = "#3e2723", DeathColor = "#4db6ac", OutlineColor = "#3e2723" },
        };
    }

    /// <summary>
    /// Pattern types that can be applied to a snake's body.
    /// </summary>
    public enum BodyPattern
    {
        /// <summary>Single solid color.</summary>
        Solid,

        /// <summary>Alternating color bands along the body.</summary>
        Stripe,

        /// <summary>Checkerboard dots along the body.</summary>
        Checker,

        /// <summary>Diamond-shaped scale marks.</summary>
        Diamond,

        /// <summary>Sinusoidal wave dots along the body.</summary>
        Wave
    }
}
