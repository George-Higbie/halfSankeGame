// <copyright file="Common.cs" company="Snake PS9">
// Copyright (c) 2026 Alex Waldmann & George Higbie. All rights reserved.
// </copyright>
// Authors: Alex Waldmann, George Higbie
// Date: 2026-04-12

using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using Newtonsoft.Json;

namespace SnakeGame;

/// <summary>
/// Represents a directional control command sent from a client to the server.
/// Serializes to the JSON format the server expects: <c>{"moving":"&lt;direction&gt;"}</c>.
/// </summary>
public class ControlCommand
{
    /// <summary>The movement direction string (e.g. "up", "down", "left", "right", "none").</summary>
    public string moving;

    /// <summary>
    /// Initializes a new <see cref="ControlCommand"/> with the given movement direction.
    /// </summary>
    /// <param name="m">The movement direction. Must not be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="m"/> is null.</exception>
    public ControlCommand(string m)
    {
        ArgumentNullException.ThrowIfNull(m);
        moving = m;
    }

    /// <summary>Returns the JSON-serialized form of this command.</summary>
    /// <returns>A JSON string such as <c>{"moving":"up"}</c>.</returns>
    public override string ToString()
    {
        return JsonConvert.SerializeObject((object)this);
    }
}
