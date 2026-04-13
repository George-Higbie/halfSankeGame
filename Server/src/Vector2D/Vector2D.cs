// <copyright file="Vector2D.cs" company="Snake PS9">
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
/// A two-dimensional vector that stores floating-point components internally
/// but exposes integer X/Y properties for JSON serialization.
/// </summary>
public class Vector2D
{
    // ==================== Properties ====================

    /// <summary>The floating-point X component. Not serialized to JSON.</summary>
    [JsonIgnore]
    public double X_f { get; set; }

    /// <summary>The floating-point Y component. Not serialized to JSON.</summary>
    [JsonIgnore]
    public double Y_f { get; set; }

    /// <summary>The integer X component used for JSON serialization (truncated from <see cref="X_f"/>).</summary>
    [JsonProperty]
    public int X
    {
        get => (int)X_f;
        set => X_f = value;
    }

    /// <summary>The integer Y component used for JSON serialization (truncated from <see cref="Y_f"/>).</summary>
    [JsonProperty]
    public int Y
    {
        get => (int)Y_f;
        set => Y_f = value;
    }

    // ==================== Constructors ====================

    /// <summary>Initializes a new <see cref="Vector2D"/> at (-1, -1).</summary>
    public Vector2D()
    {
        X_f = -1.0;
        Y_f = -1.0;
    }

    /// <summary>Initializes a new <see cref="Vector2D"/> with the given components.</summary>
    /// <param name="_x">The X component.</param>
    /// <param name="_y">The Y component.</param>
    public Vector2D(double _x, double _y)
    {
        X_f = _x;
        Y_f = _y;
    }

    /// <summary>Copy-constructs a <see cref="Vector2D"/> from another instance.</summary>
    /// <param name="other">The source vector. Must not be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="other"/> is null.</exception>
    public Vector2D(Vector2D other)
    {
        ArgumentNullException.ThrowIfNull(other);
        X_f = other.X_f;
        Y_f = other.Y_f;
    }

    // ==================== Public Methods ====================

    /// <summary>
    /// Determines whether this vector is equal to another object.
    /// Equality is based on the string representation.
    /// </summary>
    /// <param name="obj">The object to compare against.</param>
    /// <returns><c>true</c> if <paramref name="obj"/> is a <see cref="Vector2D"/> with the same components.</returns>
    public override bool Equals(object? obj)
    {
        if (obj is not Vector2D vector2D)
        {
            return false;
        }
        return ToString() == vector2D.ToString();
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        return ToString().GetHashCode();
    }

    /// <summary>Returns a string in the form <c>(x,y)</c>.</summary>
    /// <returns>String representation of this vector.</returns>
    public override string ToString()
    {
        return "(" + X_f + "," + Y_f + ")";
    }

    /// <summary>Gets the X component as a <c>double</c>.</summary>
    /// <returns>The floating-point X value.</returns>
    public double GetX()
    {
        return X_f;
    }

    /// <summary>Gets the Y component as a <c>double</c>.</summary>
    /// <returns>The floating-point Y value.</returns>
    public double GetY()
    {
        return Y_f;
    }

    /// <summary>
    /// Clamps both components to the range [-1, 1].
    /// </summary>
    public void Clamp()
    {
        if (X_f > 1.0)  X_f = 1.0;
        if (X_f < -1.0) X_f = -1.0;
        if (Y_f > 1.0)  Y_f = 1.0;
        if (Y_f < -1.0) Y_f = -1.0;
    }

    /// <summary>Rotates this vector by the given number of degrees and clamps components to [-1, 1].</summary>
    /// <param name="degrees">Rotation angle in degrees.</param>
    public void Rotate(double degrees)
    {
        double num = degrees / 180.0 * Math.PI;
        double x_f = X_f * Math.Cos(num) - Y_f * Math.Sin(num);
        double y_f = X_f * Math.Sin(num) + Y_f * Math.Cos(num);
        X_f = x_f;
        Y_f = y_f;
        Clamp();
    }

    /// <summary>
    /// Converts this vector to an angle in degrees relative to the (0, -1) (up) direction.
    /// Used for rendering the snake head orientation.
    /// </summary>
    /// <returns>Angle in degrees.</returns>
    public float ToAngle()
    {
        float num = (float)Math.Acos(0.0 - Y_f);
        if (X_f < 0.0)
        {
            num *= -1f;
        }
        return num * (180f / (float)Math.PI);
    }

    /// <summary>Computes the angle in degrees between two world-space points.</summary>
    /// <param name="a">The first point. Must not be null.</param>
    /// <param name="b">The second point. Must not be null.</param>
    /// <returns>Angle in degrees from <paramref name="b"/> to <paramref name="a"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="a"/> or <paramref name="b"/> is null.</exception>
    public static float AngleBetweenPoints(Vector2D a, Vector2D b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        Vector2D vector2D = a - b;
        vector2D.Normalize();
        return vector2D.ToAngle();
    }

    /// <summary>Adds two vectors component-wise.</summary>
    /// <param name="v1">The left operand. Must not be null.</param>
    /// <param name="v2">The right operand. Must not be null.</param>
    /// <returns>A new vector that is the component-wise sum.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="v1"/> or <paramref name="v2"/> is null.</exception>
    public static Vector2D operator +(Vector2D v1, Vector2D v2)
    {
        ArgumentNullException.ThrowIfNull(v1);
        ArgumentNullException.ThrowIfNull(v2);
        return new Vector2D(v1.X_f + v2.X_f, v1.Y_f + v2.Y_f);
    }

    /// <summary>Subtracts two vectors component-wise.</summary>
    /// <param name="v1">The left operand. Must not be null.</param>
    /// <param name="v2">The right operand. Must not be null.</param>
    /// <returns>A new vector that is the component-wise difference.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="v1"/> or <paramref name="v2"/> is null.</exception>
    public static Vector2D operator -(Vector2D v1, Vector2D v2)
    {
        ArgumentNullException.ThrowIfNull(v1);
        ArgumentNullException.ThrowIfNull(v2);
        return new Vector2D(v1.X_f - v2.X_f, v1.Y_f - v2.Y_f);
    }

    /// <summary>Scales a vector by a scalar.</summary>
    /// <param name="v">The vector. Must not be null.</param>
    /// <param name="s">The scalar multiplier.</param>
    /// <returns>A new scaled vector.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="v"/> is null.</exception>
    public static Vector2D operator *(Vector2D v, double s)
    {
        ArgumentNullException.ThrowIfNull(v);
        return new Vector2D
        {
            X_f = v.GetX() * s,
            Y_f = v.GetY() * s
        };
    }

    /// <summary>Computes the Euclidean length (magnitude) of this vector.</summary>
    /// <returns>The length as a <c>double</c>.</returns>
    public double Length()
    {
        return Math.Sqrt(X_f * X_f + Y_f * Y_f);
    }

    /// <summary>
    /// Normalizes this vector in place so its length is 1.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the vector has zero length and cannot be normalized.
    /// </exception>
    public void Normalize()
    {
        double len = Length();
        if (len == 0.0)
        {
            throw new InvalidOperationException("Cannot normalize a zero-length vector.");
        }
        X_f /= len;
        Y_f /= len;
    }

    /// <summary>Computes the dot product of this vector with another.</summary>
    /// <param name="v">The other vector. Must not be null.</param>
    /// <returns>The scalar dot product.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="v"/> is null.</exception>
    public double Dot(Vector2D v)
    {
        ArgumentNullException.ThrowIfNull(v);
        return GetX() * v.GetX() + GetY() * v.GetY();
    }

    /// <summary>
    /// Returns <c>true</c> if this vector and <paramref name="other"/> are cardinal directions
    /// that point directly opposite each other (e.g. up vs down, left vs right).
    /// </summary>
    /// <param name="other">The other direction vector. Must not be null.</param>
    /// <returns><c>true</c> if the vectors are opposite cardinal directions.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="other"/> is null.</exception>
    public bool IsOppositeCardinalDirection(Vector2D other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (X_f != 0.0 || other.X_f != 0.0 || Y_f != 0.0 - other.Y_f)
        {
            if (Y_f == 0.0 && other.Y_f == 0.0)
            {
                return X_f == 0.0 - other.X_f;
            }
            return false;
        }
        return true;
    }
}
