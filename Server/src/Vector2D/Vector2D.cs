using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using Newtonsoft.Json;

namespace SnakeGame;

public class Vector2D
{
	[JsonIgnore]
	public double X_f { get; set; }

	[JsonIgnore]
	public double Y_f { get; set; }

	[JsonProperty]
	public int X
	{
		get
		{
			return (int)X_f;
		}
		set
		{
			X_f = value;
		}
	}

	[JsonProperty]
	public int Y
	{
		get
		{
			return (int)Y_f;
		}
		set
		{
			Y_f = value;
		}
	}

	public Vector2D()
	{
		X_f = -1.0;
		Y_f = -1.0;
	}

	public Vector2D(double _x, double _y)
	{
		X_f = _x;
		Y_f = _y;
	}

	public Vector2D(Vector2D other)
	{
		X_f = other.X_f;
		Y_f = other.Y_f;
	}

	public override bool Equals(object obj)
	{
		if (!(obj is Vector2D vector2D))
		{
			return false;
		}
		return ToString() == vector2D.ToString();
	}

	public override int GetHashCode()
	{
		return ToString().GetHashCode();
	}

	public override string ToString()
	{
		return "(" + X_f + "," + Y_f + ")";
	}

	public double GetX()
	{
		return X_f;
	}

	public double GetY()
	{
		return Y_f;
	}

	public void Clamp()
	{
		if (X_f > 1.0)
		{
			X_f = 1.0;
		}
		if (X_f < -1.0)
		{
			X_f = -1.0;
		}
		if (Y_f > 1.0)
		{
			Y_f = 1.0;
		}
		if (Y_f < -1.0)
		{
			Y_f = -1.0;
		}
	}

	public void Rotate(double degrees)
	{
		double num = degrees / 180.0 * Math.PI;
		double x_f = X_f * Math.Cos(num) - Y_f * Math.Sin(num);
		double y_f = X_f * Math.Sin(num) + Y_f * Math.Cos(num);
		X_f = x_f;
		Y_f = y_f;
		Clamp();
	}

	public float ToAngle()
	{
		float num = (float)Math.Acos(0.0 - Y_f);
		if (X_f < 0.0)
		{
			num *= -1f;
		}
		return num * (180f / (float)Math.PI);
	}

	public static float AngleBetweenPoints(Vector2D a, Vector2D b)
	{
		Vector2D vector2D = a - b;
		vector2D.Normalize();
		return vector2D.ToAngle();
	}

	public static Vector2D operator +(Vector2D v1, Vector2D v2)
	{
		return new Vector2D(v1.X_f + v2.X_f, v1.Y_f + v2.Y_f);
	}

	public static Vector2D operator -(Vector2D v1, Vector2D v2)
	{
		return new Vector2D(v1.X_f - v2.X_f, v1.Y_f - v2.Y_f);
	}

	public static Vector2D operator *(Vector2D v, double s)
	{
		return new Vector2D
		{
			X_f = v.GetX() * s,
			Y_f = v.GetY() * s
		};
	}

	public double Length()
	{
		return Math.Sqrt(X_f * X_f + Y_f * Y_f);
	}

	public void Normalize()
	{
		double num = Length();
		X_f /= num;
		Y_f /= num;
	}

	public double Dot(Vector2D v)
	{
		return GetX() * v.GetX() + GetY() * v.GetY();
	}

	public bool IsOppositeCardinalDirection(Vector2D other)
	{
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
