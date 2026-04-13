// <copyright file="World.cs" company="Snake PS9">
// Copyright (c) 2026 Alex Waldmann & George Higbie. All rights reserved.
// </copyright>
// Authors: Alex Waldmann, George Higbie
// Date: 2026-04-12

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using Newtonsoft.Json;

namespace SnakeGame;

/// <summary>
/// Compile-time constants that govern visual appearance and gameplay parameters
/// shared between the server simulation and the GUI client.
/// </summary>
public static class Constants
{
	/// <summary>Half-width of a snake's body in world units.</summary>
	public const int SnakeWidth = 10;

	/// <summary>Minimum snake body length before powerup growth counts.</summary>
	public const int MinSnakeLength = 120;

	/// <summary>Width of the HP bar overlay in pixels.</summary>
	public const int HPBarWidth = 40;

	/// <summary>Width of each wall segment in world units.</summary>
	public const int WallWidth = 50;

	/// <summary>Pixels a snake moves per frame.</summary>
	public const float SnakeSpeed = 3f;

	/// <summary>Maximum hit-points (unused in base variant).</summary>
	public const int MaxHP = 3;

	/// <summary>Maximum delay in frames between powerup spawns.</summary>
	public const int MaxPowerupDelay = 200;

	/// <summary>Cap on simultaneously active powerups.</summary>
	public const int MaxPowerups = 20;

	/// <summary>Number of frames a snake grows after collecting a powerup.</summary>
	public const int GrowthFrames = 12;

	/// <summary>GUI zoom scale factor.</summary>
	public const float ViewScale = 1f;

	/// <summary>Client viewport size in pixels.</summary>
	public const int ViewSize = 900;

	/// <summary>Maps direction name strings to normalized unit vectors.</summary>
	public static readonly Dictionary<string, Vector2D> CardinalDirections = new Dictionary<string, Vector2D>
	{
		{
			"up",
			new Vector2D(0.0, -1.0)
		},
		{
			"down",
			new Vector2D(0.0, 1.0)
		},
		{
			"left",
			new Vector2D(-1.0, 0.0)
		},
		{
			"right",
			new Vector2D(1.0, 0.0)
		}
	};
}
/// <summary>
/// Represents a collectible powerup on the game board.
/// Serialized to/from JSON for network transmission.
/// </summary>
[JsonObject(MemberSerialization.OptIn)]
public class Powerup
{
	private static int nextPowerID;

	[JsonProperty(PropertyName = "power")]
	private int ID;

	[JsonProperty(PropertyName = "loc")]
	private Vector2D location;

	[JsonProperty(PropertyName = "died")]
	private bool died;

	/// <summary>Creates a powerup at the origin with an auto-assigned ID.</summary>
	public Powerup()
	{
		ID = nextPowerID++;
		location = new Vector2D(0.0, 0.0);
	}

	/// <summary>
	/// Creates a powerup at <paramref name="loc"/> with an auto-assigned ID.
	/// </summary>
	/// <param name="loc">Spawn location. Must not be null.</param>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="loc"/> is null.</exception>
	public Powerup(Vector2D loc)
	{
		ArgumentNullException.ThrowIfNull(loc);
		ID = nextPowerID++;
		location = new Vector2D(loc);
	}

	/// <summary>Returns the powerup's unique integer ID.</summary>
	public int GetID() => ID;

	/// <summary>Returns the powerup's location in world coordinates.</summary>
	/// <returns>The location vector (never null for properly constructed instances).</returns>
	public Vector2D GetLocation() => location;

	/// <summary>Marks this powerup as collected/dead so it is removed on the next cleanup pass.</summary>
	public void Die()
	{
		died = true;
	}

	/// <summary>Returns <c>true</c> while this powerup has not yet been collected.</summary>
	public bool IsAlive() => !died;

	/// <summary>Serializes this powerup to a JSON string for network transmission.</summary>
	/// <returns>JSON representation of this powerup.</returns>
	public override string ToString()
	{
		return JsonConvert.SerializeObject((object)this);
	}
}
/// <summary>
/// Represents a single player's snake, including body segments, direction, score,
/// and lifecycle state (alive, dead, disconnected, joining).
/// Serialized to/from JSON for network transmission.
/// </summary>
[JsonObject(MemberSerialization.OptIn)]
public class Snake
{
	private uint lastDeath;

	/// <summary>The unique numeric player ID assigned by the server (JSON key: "snake").</summary>
	[JsonProperty(PropertyName = "snake")]
	public int ID { get; private set; }

	/// <summary>Ordered set of body-segment vertices from tail to head.</summary>
	[JsonProperty(PropertyName = "body")]
	public LinkedList<Vector2D> body { get; private set; }

	/// <summary>Current unit-vector direction the snake is travelling.</summary>
	[JsonProperty(PropertyName = "dir")]
	public Vector2D direction { get; private set; }

	/// <summary>Player display name.</summary>
	[JsonProperty(PropertyName = "name")]
	public string name { get; private set; } = "";

	/// <summary>Number of powerups collected this life.</summary>
	[JsonProperty(PropertyName = "score")]
	public int score { get; private set; }

	/// <summary><c>true</c> for exactly one frame when the snake dies.</summary>
	[JsonProperty(PropertyName = "died")]
	public bool died { get; private set; }

	/// <summary><c>true</c> while the snake is alive.</summary>
	[JsonProperty(PropertyName = "alive")]
	public bool Alive { get; private set; } = true;

	/// <summary><c>true</c> after the client disconnects.</summary>
	[JsonProperty(PropertyName = "dc")]
	public bool disconnected { get; private set; }

	/// <summary><c>true</c> while the snake is waiting to complete its join animation.</summary>
	[JsonProperty(PropertyName = "join")]
	public bool joined { get; private set; }

	/// <summary>Skin palette index (0 = classic).</summary>
	[JsonProperty(PropertyName = "skin")]
	public int skin { get; set; }

	/// <summary>Number of body segments between tail and head (exclusive).</summary>
	public int NumSegments => body.Count - 1;

	/// <summary>Movement speed in world units per frame.</summary>
	public float speed { get; set; } = 3f;

	/// <summary>Remaining frames of growth after a powerup is consumed.</summary>
	public int growing { get; set; }

	/// <summary>Gets or sets the head position (last element of the body list).</summary>
	public Vector2D Head
	{
		get
		{
			return body.Last();
		}
		private set
		{
			body.RemoveLast();
			body.AddLast(value);
		}
	}

	/// <summary>Gets or sets the tail position (first element of the body list).</summary>
	public Vector2D Tail
	{
		get
		{
			return body.First();
		}
		private set
		{
			body.RemoveFirst();
			body.AddFirst(value);
		}
	}

	/// <summary><c>true</c> when the snake state changed this tick and should be serialized.</summary>
	public bool updated { get; set; }

	/// <summary>Enumerates consecutive (tail→head) segments of the snake body as (v1, v2) pairs.</summary>
	/// <returns>Sequence of segment endpoint pairs, from tail to head.</returns>
	public IEnumerable<(Vector2D v1, Vector2D v2)> Segments()
	{
		LinkedListNode<Vector2D>? current = body.First;
		if (current != null)
		{
			while (current.Next != null)
			{
				yield return (v1: current.Value, v2: current.Next.Value);
				current = current.Next;
			}
		}
	}

	/// <summary>Creates a default snake with no body segments. Use the named constructor for game play.</summary>
	public Snake()
	{
		ID = 0;
		body = new LinkedList<Vector2D>();
		direction = new Vector2D();
		name = "";
		score = 0;
		died = false;
		disconnected = false;
		joined = false;
	}

	/// <summary>
	/// Creates a snake with the given name, skin, and initial body segment.
	/// </summary>
	/// <param name="_name">Player display name. Must not be null.</param>
	/// <param name="t">Tail position. Must not be null.</param>
	/// <param name="h">Head position. Must not be null.</param>
	/// <param name="_id">Unique player ID assigned by the server.</param>
	/// <param name="_skin">Skin palette index (0 = default).</param>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="_name"/>, <paramref name="t"/>, or <paramref name="h"/> is null.</exception>
	public Snake(string _name, Vector2D t, Vector2D h, int _id, int _skin = 0)
	{
		ArgumentNullException.ThrowIfNull(_name);
		ArgumentNullException.ThrowIfNull(t);
		ArgumentNullException.ThrowIfNull(h);
		ID = _id;
		name = _name;
		skin = _skin;
		body = new LinkedList<Vector2D>();
		body.AddLast(t);
		body.AddLast(h);
		direction = h - t;
		direction.Normalize();
		score = 0;
		died = false;
		disconnected = false;
		joined = false;
	}

	/// <summary>Applies the effect of collecting a powerup: increments score and enqueues growth frames.</summary>
	public void Powerup()
	{
		IncreaseScore();
		growing += 12;
	}

	/// <summary>Marks the snake as disconnected and triggers a death event if still alive.</summary>
	/// <param name="time">Current game tick used to record the death timestamp.</param>
	public void Discontinue(uint time)
	{
		if (Alive)
		{
			Die(time);
		}
		disconnected = true;
	}

	/// <summary>Returns <c>true</c> after the controlling client disconnected.</summary>
	public bool IsDisconnected()
	{
		return disconnected;
	}

	/// <summary>Begins the join animation phase for a newly connected snake.</summary>
	public void Join()
	{
		joined = true;
	}

	/// <summary>Ends the join animation phase, allowing normal play.</summary>
	public void FinishJoin()
	{
		joined = false;
	}

	/// <summary>Returns <c>true</c> while the snake is in its join animation.</summary>
	public bool IsJoining()
	{
		return joined;
	}

	/// <summary>Attempts to change the snake's direction based on a control command.</summary>
	/// <param name="m">Direction string: "up", "down", "left", "right", or "none".</param>
	/// <param name="w">The world used for turn-validation collision checks.</param>
	public void ChangeDirection(string m, World w)
	{
		Vector2D other = new Vector2D(direction);
		switch (m)
		{
		case "up":
			direction = new Vector2D(0.0, -1.0);
			break;
		case "down":
			direction = new Vector2D(0.0, 1.0);
			break;
		case "left":
			direction = new Vector2D(-1.0, 0.0);
			break;
		case "right":
			direction = new Vector2D(1.0, 0.0);
			break;
		case "none":
			return;
		}
		if (direction.IsOppositeCardinalDirection(other))
		{
			direction = other;
		}
		else if (!CanTurn(direction, w))
		{
			direction = other;
		}
	}

	/// <summary>Returns <c>true</c> if turning toward <paramref name="dir"/> would not immediately collide.</summary>
	/// <param name="dir">Proposed unit-vector direction.</param>
	/// <param name="w">The world used for collision checking.</param>
	public bool CanTurn(Vector2D dir, World w)
	{
		bool result = true;
		Vector2D oldHead = new Vector2D(Head);
		bool needsPop = MoveHead(dir, w.Size);
		if (CollidesWith(this, checkForAttemptedTurn: true))
		{
			result = false;
		}
		UndoMoveHead(oldHead, needsPop);
		return result;
	}

	/// <summary>Returns <c>true</c> if this snake's head overlaps any segment of <paramref name="other"/>.</summary>
	/// <param name="other">The snake to test against.</param>
	/// <param name="checkForAttemptedTurn">If <c>true</c>, skips the first few tail segments to allow u-turn detection.</param>
	public bool CollidesWith(Snake other, bool checkForAttemptedTurn = false)
	{
		IEnumerable<(Vector2D, Vector2D)> enumerable;
		if (ID == other.ID)
		{
			int num = 2;
			LinkedListNode<Vector2D>? linkedListNode = body.Last?.Previous?.Previous?.Previous;
			while (linkedListNode != null)
			{
				Vector2D vector2D = linkedListNode.Value - linkedListNode.Next!.Value;
				Vector2D vector2D2 = linkedListNode.Next!.Next!.Value - linkedListNode.Next!.Next!.Next!.Value;
				vector2D.Normalize();
				vector2D2.Normalize();
				float num2 = vector2D.ToAngle();
				float num3 = vector2D2.ToAngle();
				if (Math.Abs(num2 - num3) == 180f)
				{
					break;
				}
				linkedListNode = linkedListNode.Previous;
				num++;
			}
			enumerable = other.Segments().SkipLast(num);
		}
		else
		{
			enumerable = other.Segments();
		}
		int num4 = 0;
		foreach (var item in enumerable)
		{
			if ((!checkForAttemptedTurn || num4++ >= NumSegments - 3) && IntersectsRectangle(Head, item.Item1, item.Item2, 10.0))
			{
				return true;
			}
		}
		return false;
	}

	/// <summary>
	/// Returns <c>true</c> if this snake's head overlaps the rectangular hitbox of <paramref name="w"/>.
	/// </summary>
	/// <param name="w">The wall to test. Must not be null.</param>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="w"/> is null.</exception>
	public bool CollidesWith(Wall w)
	{
		ArgumentNullException.ThrowIfNull(w);
		if (w.P1 is null || w.P2 is null) return false;
		return IntersectsRectangle(Head, w.P1, w.P2, 30.0);
	}

	private bool IntersectsRectangle(Vector2D h, Vector2D v1, Vector2D v2, double padding)
	{
		double num = Math.Max(v1.X_f, v2.X_f) + padding;
		double num2 = Math.Min(v1.X_f, v2.X_f) - padding;
		double num3 = Math.Max(v1.Y_f, v2.Y_f) + padding;
		double num4 = Math.Min(v1.Y_f, v2.Y_f) - padding;
		if (h.X_f > num2 && h.X_f < num && h.Y_f > num4)
		{
			return h.Y_f < num3;
		}
		return false;
	}

	/// <summary>Kills the snake at the given game tick.</summary>
	/// <param name="time">Current game tick.</param>
	public void Die(uint time)
	{
		died = true;
		lastDeath = time;
		Alive = false;
	}

	/// <summary>Clears the one-frame death flag after it has been broadcast.</summary>
	public void ResetDie()
	{
		died = false;
	}

	/// <summary>Returns <c>true</c> for the single frame after the snake died.</summary>
	public bool Died()
	{
		return died;
	}

	/// <summary>Returns the game tick on which this snake last died.</summary>
	public uint GetLastDeath()
	{
		return lastDeath;
	}

	/// <summary>Returns the snake's current score (powerups collected).</summary>
	public int GetScore()
	{
		return score;
	}

	/// <summary>Increments the snake's score by one.</summary>
	public void IncreaseScore()
	{
		score++;
	}

	/// <summary>Resets the snake body and score to begin a new life.</summary>
	/// <param name="t">New tail position.</param>
	/// <param name="h">New head position.</param>
	public void Respawn(Vector2D t, Vector2D h)
	{
		body = new LinkedList<Vector2D>();
		body.AddLast(t);
		body.AddLast(h);
		score = 0;
		direction = h - t;
		direction.Normalize();
		Alive = true;
	}

	/// <summary>Returns this snake's unique player ID.</summary>
	public int GetID()
	{
		return ID;
	}

	/// <summary>Returns the player's display name.</summary>
	public string GetName()
	{
		return name;
	}

	/// <summary>Wraps the snake body across world boundaries when the head crosses an edge.</summary>
	/// <param name="worldSize">Total side length of the square world.</param>
	public void CheckWrap(int worldSize)
	{
		Vector2D vector2D = new Vector2D(Head);
		Wrap(vector2D, worldSize);
		Wrap(Tail, worldSize);
		if (!vector2D.Equals(Head))
		{
			body.AddLast(vector2D);
			body.AddLast(new Vector2D(vector2D));
		}
		if (body.First!.Value.Equals(body.First.Next!.Value))
		{
			body.RemoveFirst();
		}
	}

	private void Wrap(Vector2D p, int worldSize)
	{
		int num = worldSize / 2;
		if (p.X_f > (double)num)
		{
			p.X_f = -num;
		}
		else if (p.X_f < (double)(-num))
		{
			p.X_f = num;
		}
		else if (p.Y_f > (double)num)
		{
			p.Y_f = -num;
		}
		else if (p.Y_f < (double)(-num))
		{
			p.Y_f = num;
		}
	}

	/// <summary>Reverts a trial head move performed by <see cref="MoveHead"/>.</summary>
	/// <param name="oldHead">The pre-move head position.</param>
	/// <param name="needsPop">Whether a segment needs to be removed from the tail of the list.</param>
	public void UndoMoveHead(Vector2D oldHead, bool needsPop)
	{
		if (needsPop)
		{
			body.RemoveLast();
		}
		Head = new Vector2D(oldHead);
	}

	/// <summary>Advances the head one step in <paramref name="dir"/>.</summary>
	/// <param name="dir">Unit-vector movement direction.</param>
	/// <param name="worldSize">Total world side length for wrap calculation.</param>
	/// <returns><c>true</c> when the previous head was pushed onto the body list.</returns>
	public bool MoveHead(Vector2D dir, int worldSize)
	{
		Vector2D vector2D = Head - body.Last!.Previous!.Value;
		Vector2D vector2D2 = new Vector2D(vector2D);
		vector2D2.Normalize();
		bool result = false;
		if (!vector2D2.Equals(dir) && vector2D.Length() <= (double)worldSize && vector2D.Length() > 0.0)
		{
			body.AddLast(new Vector2D(Head));
			result = true;
		}
		Head += dir * speed;
		return result;
	}

	/// <summary>Advances the snake simulation by one tick.</summary>
	/// <param name="time">Current game tick (unused but kept for consistency).</param>
	/// <param name="worldSize">Total side length of the square world for wrap detection.</param>
	public void Update(uint time, int worldSize)
	{
		MoveHead(direction, worldSize);
		if (growing == 0)
		{
			Vector2D vector2D = body.First!.Next!.Value - Tail;
			vector2D.Normalize();
			Tail += vector2D * speed;
		}
		if (Tail.Equals(body.First!.Next!.Value))
		{
			body.RemoveFirst();
		}
		CheckWrap(worldSize);
		growing = Math.Max(0, growing - 1);
	}

	/// <summary>Serializes this object to a JSON string for network transmission.</summary>
	/// <returns>JSON representation of this instance.</returns>
	public override string ToString()
	{
		return JsonConvert.SerializeObject((object)this);
	}
}
/// <summary>
/// An immutable wall segment defined by two endpoint vectors.
/// Used for collision detection and rendering.
/// Serialized to/from JSON for the initial handshake.
/// </summary>
[JsonObject(MemberSerialization.OptIn)]
public class Wall
{
	private static int nextWallID;

	[JsonProperty(PropertyName = "wall")]
	private int ID;

	private int minX;

	private int minY;

	private int maxX;

	private int maxY;

	private bool initialized;

	/// <summary>First endpoint of the wall segment. May be null for JSON-deserialized walls before validation.</summary>
	[JsonProperty(PropertyName = "p1")]
	public Vector2D? P1 { get; private set; }

	/// <summary>Second endpoint of the wall segment. May be null for JSON-deserialized walls before validation.</summary>
	[JsonProperty(PropertyName = "p2")]
	public Vector2D? P2 { get; private set; }

	/// <summary>Creates an uninitialized wall (used by JSON deserializer).</summary>
	public Wall()
	{
		ID = -1;
		P1 = null;
		P2 = null;
	}

	/// <summary>
	/// Creates a wall with endpoints at <paramref name="_p1"/> and <paramref name="_p2"/>.
	/// </summary>
	/// <param name="_p1">First endpoint. Must not be null.</param>
	/// <param name="_p2">Second endpoint. Must not be null.</param>
	/// <exception cref="ArgumentNullException">Thrown when either endpoint is null.</exception>
	public Wall(Vector2D _p1, Vector2D _p2)
	{
		ArgumentNullException.ThrowIfNull(_p1);
		ArgumentNullException.ThrowIfNull(_p2);
		ID = nextWallID++;
		P1 = new Vector2D(_p1);
		P2 = new Vector2D(_p2);
		Init();
	}

	private void Init()
	{
		if (P1 is null || P2 is null) return;
		initialized = true;
		if (P1.GetX() <= P2.GetX())
		{
			minX = (int)P1.GetX();
			maxX = (int)P2.GetX();
		}
		else
		{
			minX = (int)P2.GetX();
			maxX = (int)P1.GetX();
		}
		if (P1.GetY() <= P2.GetY())
		{
			minY = (int)P1.GetY();
			maxY = (int)P2.GetY();
		}
		else
		{
			minY = (int)P2.GetY();
			maxY = (int)P1.GetY();
		}
	}

	/// <summary>Returns the first endpoint of this wall (may be null for uninitialized JSON-deserialized instances).</summary>
	/// <returns>First endpoint vector, or null if not set.</returns>
	public Vector2D? GetP1() => P1;

	/// <summary>Returns the second endpoint of this wall (may be null for uninitialized JSON-deserialized instances).</summary>
	/// <returns>Second endpoint vector, or null if not set.</returns>
	public Vector2D? GetP2() => P2;

	/// <summary>Returns this wall's unique integer ID.</summary>
	public int GetID() => ID;

	/// <summary>
	/// Tests whether point <paramref name="p"/> is within <paramref name="radius"/> units of this wall segment.
	/// </summary>
	/// <param name="p">The point to test. Must not be null.</param>
	/// <param name="radius">The proximity threshold in world units.</param>
	/// <returns><c>true</c> if the point is inside the expanded bounding box.</returns>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="p"/> is null.</exception>
	public bool Intersects(Vector2D p, double radius)
	{
		ArgumentNullException.ThrowIfNull(p);
		if (!initialized)
		{
			Init();
		}
		if (p.GetX() >= (double)minX - radius && p.GetX() <= (double)maxX + radius && p.GetY() >= (double)minY - radius)
		{
			return p.GetY() <= (double)maxY + radius;
		}
		return false;
	}

	/// <summary>Enumerates grid-aligned sample points along this wall at 50-unit intervals, used for rendering.</summary>
	/// <returns>Sequence of world-coordinate points along the wall.</returns>
	public IEnumerable<Vector2D> Segments()
	{
		if (!initialized)
		{
			Init();
		}
		int x = minX;
		int y = minY;
		if (minX == maxX)
		{
			while (y <= maxY)
			{
				Vector2D vector2D = new Vector2D(x, y);
				y += 50;
				yield return vector2D;
			}
		}
		else
		{
			while (x <= maxX)
			{
				Vector2D vector2D2 = new Vector2D(x, y);
				x += 50;
				yield return vector2D2;
			}
		}
	}

	/// <summary>Serializes this object to a JSON string for network transmission.</summary>
	/// <returns>JSON representation of this instance.</returns>
	public override string ToString()
	{
		return JsonConvert.SerializeObject((object)this);
	}
}
/// <summary>
/// The game world: manages all snakes, walls, powerups, and the simulation tick.
/// Thread-safety is the caller's responsibility; lock before mutating shared state.
/// </summary>
public class World
{
	private const uint SpawnSafetyFrames = 60u;
	private const int DeathDropGrowthUnitsPerGroup = 4;
	private const double DeathDropLengthPerGrowthUnit = Constants.GrowthFrames * Constants.SnakeSpeed;
	private const int DeathDropMaxPowerupsPerBurst = 4;
	private const uint DeathDropInitialDelayFrames = 12u;
	private const double DeathExplosionSpeedUnitsPerSecond = 400.0;
	private const double SimulationFramesPerSecond = 60.0;

	private sealed class PendingDeathDrop
	{
		public Vector2D Location { get; init; } = new Vector2D();
		public uint SpawnTime { get; init; }
		public int Count { get; init; }
	}

	private int respawnRate;

	private uint time;

	private uint nextPowerup;

	private Random rand = new Random();
	private readonly List<PendingDeathDrop> pendingDeathDrops = new List<PendingDeathDrop>();

	/// <summary>Side length of the square game world in world units.</summary>
	public int Size { get; private set; }

	/// <summary>All snakes currently tracked by the world, keyed by player ID.</summary>
	public Dictionary<int, Snake> Snakes { get; private set; } = new Dictionary<int, Snake>();

	/// <summary>All wall segments in the world, keyed by wall ID.</summary>
	public Dictionary<int, Wall> Walls { get; private set; } = new Dictionary<int, Wall>();

	/// <summary>All active powerups in the world, keyed by powerup ID.</summary>
	public Dictionary<int, Powerup> Powerups { get; private set; } = new Dictionary<int, Powerup>();

	/// <summary>Creates a default 750-unit world with no walls or powerups.</summary>
	public World()
	{
		Size = 750;
		time = 0u;
		nextPowerup = 0u;
	}

	/// <summary>Creates a square world with the given side length.</summary>
	/// <param name="size">Side length of the square world in world units.</param>
	public World(int size)
		: this()
	{
		Size = size;
	}

	/// <summary>Creates a world with the given dimensions, walls, and respawn rate.</summary>
	/// <param name="size">Side length of the square world in world units.</param>
	/// <param name="_walls">Initial wall segments.</param>
	/// <param name="respawn">Respawn delay in ticks.</param>
	/// <param name="fire">Unused fire-mode parameter (reserved).</param>
	public World(int size, IEnumerable<Wall> _walls, int respawn, uint fire)
		: this()
	{
		Size = size;
		respawnRate = respawn;
		foreach (Wall _wall in _walls)
		{
			Walls[_wall.GetID()] = _wall;
		}
	}

	/// <summary>Removes all snakes, walls, powerups, and pending death drops from the world.</summary>
	public void Clear()
	{
		Snakes.Clear();
		Walls.Clear();
		Powerups.Clear();
		pendingDeathDrops.Clear();
	}

	/// <summary>Returns the current simulation tick count.</summary>
	public uint GetTime()
	{
		return time;
	}

	/// <summary>
	/// Creates a snake with the given body positions, registers it in the world, and marks it as joining.
	/// </summary>
	/// <param name="name">Player display name. Must not be null.</param>
	/// <param name="t">Initial tail position. Must not be null.</param>
	/// <param name="h">Initial head position. Must not be null.</param>
	/// <param name="ID">Unique player/socket ID.</param>
	/// <param name="skin">Skin palette index (0 = default).</param>
	/// <returns>The newly created and registered <see cref="Snake"/>.</returns>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="name"/>, <paramref name="t"/>, or <paramref name="h"/> is null.</exception>
	public Snake AddSnake(string name, Vector2D t, Vector2D h, int ID, int skin = 0)
	{
		ArgumentNullException.ThrowIfNull(name);
		ArgumentNullException.ThrowIfNull(t);
		ArgumentNullException.ThrowIfNull(h);
		Snake snake = new Snake(name, t, h, ID, skin);
		snake.Join();
		Snakes.Add(ID, snake);
		return snake;
	}

	private Vector2D RandomSnakeDirection(Vector2D t)
	{
		return rand.Next(4) switch
		{
			0 => t + new Vector2D(120.0, 0.0), 
			1 => t + new Vector2D(-120.0, 0.0), 
			2 => t + new Vector2D(0.0, 120.0), 
			3 => t + new Vector2D(0.0, -120.0), 
			_ => throw new Exception(), 
		};
	}

	/// <summary>
	/// Finds a safe random spawn location and creates a registered snake for the given player.
	/// </summary>
	/// <param name="name">Player display name. Must not be null.</param>
	/// <param name="ID">Unique player/socket ID.</param>
	/// <param name="skin">Skin palette index (0 = default).</param>
	/// <returns>The newly spawned <see cref="Snake"/>.</returns>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="name"/> is null.</exception>
	public Snake AddRandomSnake(string name, int ID, int skin = 0)
	{
		ArgumentNullException.ThrowIfNull(name);
		var (t, h) = RandomSnakeSpawn();
		return AddSnake(name, t, h, ID, skin);
	}

	/// <summary>Spawns a new powerup at a random safe location and adds it to the world.</summary>
	/// <returns>The newly created <see cref="Powerup"/>.</returns>
	public Powerup AddRandomPowerup()
	{
		Powerup powerup = new Powerup(RandomLocation(5f));
		Powerups.Add(powerup.GetID(), powerup);
		return powerup;
	}

	/// <summary>Returns a random world-coordinate position that does not overlap any wall.</summary>
	/// <param name="padding">Minimum clearance from wall edges in world units.</param>
	/// <returns>A safe spawn location, or a random position after 500 attempts.</returns>
	public Vector2D RandomLocation(float padding)
	{
		const int maxAttempts = 500;
		for (int attempt = 0; attempt < maxAttempts; attempt++)
		{
			float num = (float)rand.NextDouble() * 2f - 1f;
			float num2 = (float)rand.NextDouble() * 2f - 1f;
			Vector2D vector2D = new Vector2D(num, num2);
			vector2D *= (double)(Size / 2);
			bool flag = true;
			foreach (Wall value in Walls.Values)
			{
				if (value.Intersects(vector2D, 50f + padding))
				{
					flag = false;
					break;
				}
			}
			if (flag)
			{
				foreach (Snake s in Snakes.Values)
				{
					if (!ShouldSnakeBlockSpace(s)) continue;
					foreach (var (v1, v2) in s.Segments())
					{
						double segMinX = Math.Min(v1.X_f, v2.X_f) - padding;
						double segMaxX = Math.Max(v1.X_f, v2.X_f) + padding;
						double segMinY = Math.Min(v1.Y_f, v2.Y_f) - padding;
						double segMaxY = Math.Max(v1.Y_f, v2.Y_f) + padding;
						if (vector2D.X_f >= segMinX && vector2D.X_f <= segMaxX &&
						    vector2D.Y_f >= segMinY && vector2D.Y_f <= segMaxY)
						{
							flag = false;
							break;
						}
					}
					if (!flag) break;
				}
			}
			if (flag) return vector2D;
		}
		// Fallback: return a random position ignoring snake proximity
		Vector2D fallback;
		do
		{
			float fx = (float)rand.NextDouble() * 2f - 1f;
			float fy = (float)rand.NextDouble() * 2f - 1f;
			fallback = new Vector2D(fx, fy);
			fallback *= (double)(Size / 2);
			bool wallClear = true;
			foreach (Wall value in Walls.Values)
			{
				if (value.Intersects(fallback, 50f + padding))
				{
					wallClear = false;
					break;
				}
			}
			if (wallClear) return fallback;
		}
		while (true);
	}

	private (Vector2D tail, Vector2D head) RandomSnakeSpawn()
	{
		const double aheadClearance = 300.0;
		const double step = 30.0;
		const double bodyBuffer = 50.0;
		const double headDangerZone = 300.0;
		const double corridorWidth = 50.0;
		const int maxSpawnSearchAttempts = 2500;

		bool hasBestCandidate = false;
		uint bestSafetyFrames = 0u;
		Vector2D bestTail = new Vector2D();
		Vector2D bestHead = new Vector2D();

		int attempt = 0;
		while (attempt < maxSpawnSearchAttempts)
		{
			Vector2D t = RandomLocation(120f);
			Vector2D h = RandomSnakeDirection(t);

			if (!IsSpawnGeometrySafe(t, h, aheadClearance, step, bodyBuffer, headDangerZone, corridorWidth))
			{
				attempt++;
				continue;
			}

			uint safetyFrames = GetSpawnSafetyFrames(t, h, SpawnSafetyFrames);
			if (safetyFrames >= SpawnSafetyFrames)
			{
				return (t, h);
			}

			if (!hasBestCandidate || safetyFrames > bestSafetyFrames)
			{
				hasBestCandidate = true;
				bestSafetyFrames = safetyFrames;
				bestTail = t;
				bestHead = h;
			}

			attempt++;
		}

		if (hasBestCandidate)
		{
			return (bestTail, bestHead);
		}

		// Emergency fallback if no geometric-safe candidate was found.
		Vector2D fallbackT = RandomLocation(120f);
		return (fallbackT, RandomSnakeDirection(fallbackT));
	}

	private bool IsSpawnGeometrySafe(Vector2D tail, Vector2D head, double aheadClearance, double step, double bodyBuffer, double headDangerZone, double corridorWidth)
	{
		Vector2D dir = head - tail;
		dir.Normalize();

		for (double d = 0; d <= aheadClearance; d += step)
		{
			Vector2D checkPoint = head + dir * d;
			foreach (Wall wall in Walls.Values)
			{
				if (wall.Intersects(checkPoint, 50.0))
				{
					return false;
				}
			}
		}

		foreach (Snake s in Snakes.Values)
		{
			if (!ShouldSnakeBlockSpace(s)) continue;
			foreach (var (v1, v2) in s.Segments())
			{
				double segMinX = Math.Min(v1.X_f, v2.X_f) - bodyBuffer;
				double segMaxX = Math.Max(v1.X_f, v2.X_f) + bodyBuffer;
				double segMinY = Math.Min(v1.Y_f, v2.Y_f) - bodyBuffer;
				double segMaxY = Math.Max(v1.Y_f, v2.Y_f) + bodyBuffer;
				if (head.X_f >= segMinX && head.X_f <= segMaxX &&
				    head.Y_f >= segMinY && head.Y_f <= segMaxY)
				{
					return false;
				}
			}
		}

		foreach (Snake s in Snakes.Values)
		{
			if (!s.Alive) continue;
			Vector2D sDir = s.direction;
			Vector2D toSpawn = head - s.Head;
			double dot = toSpawn.X_f * sDir.X_f + toSpawn.Y_f * sDir.Y_f;
			if (dot > 0 && dot <= headDangerZone)
			{
				double cross = Math.Abs(toSpawn.X_f * sDir.Y_f - toSpawn.Y_f * sDir.X_f);
				if (cross < corridorWidth)
				{
					return false;
				}
			}
		}

		return true;
	}

	private uint GetSpawnSafetyFrames(Vector2D tail, Vector2D head, uint maxFrames)
	{
		Snake probe = new Snake("spawn_probe", tail, head, -1);
		for (uint i = 0u; i < maxFrames; i++)
		{
			if (DoesSnakeCollide(probe))
			{
				return i;
			}
			probe.Update(time + i, Size);
		}
		return maxFrames;
	}

	private bool IsSpawnSafeForReactionWindow(Vector2D tail, Vector2D head)
	{
		return GetSpawnSafetyFrames(tail, head, SpawnSafetyFrames) >= SpawnSafetyFrames;
	}

	private bool ShouldSnakeBlockSpace(Snake s)
	{
		if (s.IsDisconnected())
		{
			return false;
		}
		if (s.Alive)
		{
			return true;
		}
		if (time < s.GetLastDeath())
		{
			return false;
		}
		return time - s.GetLastDeath() <= SpawnSafetyFrames;
	}

	/// <summary>
	/// Parses and applies a JSON movement or respawn command from <paramref name="s"/>.
	/// Silently ignores malformed JSON.
	/// </summary>
	/// <param name="s">The snake issuing the command. Must not be null.</param>
	/// <param name="cmd">Raw JSON command string. Must not be null.</param>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="s"/> or <paramref name="cmd"/> is null.</exception>
	public void ProcessCommand(Snake s, string cmd)
	{
		ArgumentNullException.ThrowIfNull(s);
		ArgumentNullException.ThrowIfNull(cmd);
		JsonSerializerSettings val = new JsonSerializerSettings();
		val.MissingMemberHandling = (MissingMemberHandling)0;
		ControlCommand? controlCommand;
		try
		{
			controlCommand = JsonConvert.DeserializeObject<ControlCommand>(cmd, val);
		}
		catch (Exception)
		{
			return;
		}
		if (controlCommand is null) return;
		if (controlCommand.moving == "respawn")
		{
			if (!s.Alive && !s.name.Contains("spectate"))
			{
				var (t, h) = RandomSnakeSpawn();
				s.Respawn(t, h);
			}
			return;
		}
		if (s.Alive)
		{
			s.ChangeDirection(controlCommand.moving, this);
		}
	}

	/// <summary>Advances the world simulation by one tick: moves snakes, spawns powerups, processes collisions.</summary>
	public void Update()
	{
		ProcessPendingDeathDrops();

		if (nextPowerup == 0)
		{
			nextPowerup = time + (uint)rand.Next(0, 67);
		}
		if (nextPowerup <= time)
		{
			if (Powerups.Count < 60)
			{
				AddRandomPowerup();
			}
			nextPowerup = time + (uint)rand.Next(0, 67);
		}
		_ = Size / 2;
		foreach (Snake value in Snakes.Values)
		{
			if (!value.Alive)
			{
				continue;
			}
			value.Update(time, Size);
			foreach (Powerup value2 in Powerups.Values)
			{
				if (value2.IsAlive() && (value2.GetLocation() - value.Head).Length() <= 16.0)
				{
					value.Powerup();
					value2.Die();
				}
			}
			if (DoesSnakeCollide(value))
			{
				QueueDeathDrops(value);
				value.Die(time);
			}
		}
		time++;
	}

	private void QueueDeathDrops(Snake snake)
	{
		var segments = snake.Segments().ToList();
		if (segments.Count == 0)
		{
			return;
		}

		double totalLength = segments.Sum(segment => (segment.v2 - segment.v1).Length());
		if (totalLength <= 0.0)
		{
			return;
		}

		double grownLength = Math.Max(0.0, totalLength - Constants.MinSnakeLength);
		int grownUnits = (int)Math.Floor(grownLength / DeathDropLengthPerGrowthUnit);
		int groupCount = grownUnits / DeathDropGrowthUnitsPerGroup;
		if (groupCount <= 0)
		{
			return;
		}

		double groupLength = DeathDropGrowthUnitsPerGroup * DeathDropLengthPerGrowthUnit;
		for (int groupIndex = 0; groupIndex < groupCount; groupIndex++)
		{
			double groupStartDistance = groupIndex * groupLength;
			double groupEndDistance = Math.Min(groupStartDistance + groupLength, grownLength);
			int powerupCount = RollDeathDropPowerupCount();

			for (int i = 0; i < powerupCount; i++)
			{
				double dropDistance = groupStartDistance + rand.NextDouble() * (groupEndDistance - groupStartDistance);
				Vector2D dropLocation = GetExplosionLocationAtDistance(segments, dropDistance);
				double travelSeconds = dropDistance / DeathExplosionSpeedUnitsPerSecond;
				uint delayFrames = (uint)Math.Round(travelSeconds * SimulationFramesPerSecond);
				pendingDeathDrops.Add(new PendingDeathDrop
				{
					Location = dropLocation,
					SpawnTime = time + DeathDropInitialDelayFrames + delayFrames,
					Count = 1
				});
			}
		}
	}

	private Vector2D GetExplosionLocationAtDistance(IReadOnlyList<(Vector2D v1, Vector2D v2)> segments, double distanceFromHead)
	{
		double traversed = 0.0;
		for (int i = segments.Count - 1; i >= 0; i--)
		{
			var (v1, v2) = segments[i];
			double segDx = v1.X_f - v2.X_f;
			double segDy = v1.Y_f - v2.Y_f;
			double segLength = Math.Sqrt(segDx * segDx + segDy * segDy);
			if (distanceFromHead <= traversed + segLength)
			{
				double t = segLength <= 0.0 ? 0.0 : (distanceFromHead - traversed) / segLength;
				double x = v2.X_f + (v1.X_f - v2.X_f) * t;
				double y = v2.Y_f + (v1.Y_f - v2.Y_f) * t;
				return new Vector2D(x, y);
			}

			traversed += segLength;
		}

		return new Vector2D(segments[0].v1);
	}

	private int RollDeathDropPowerupCount()
	{
		return rand.Next(1, DeathDropMaxPowerupsPerBurst + 1);
	}

	private void ProcessPendingDeathDrops()
	{
		for (int i = pendingDeathDrops.Count - 1; i >= 0; i--)
		{
			PendingDeathDrop drop = pendingDeathDrops[i];
			if (drop.SpawnTime > time)
			{
				continue;
			}

			for (int count = 0; count < drop.Count; count++)
			{
				Powerup p = new Powerup(drop.Location);
				Powerups.Add(p.GetID(), p);
			}

			pendingDeathDrops.RemoveAt(i);
		}
	}

	/// <summary>
	/// Tests whether <paramref name="s"/> can legally move one step in <paramref name="dir"/>
	/// without colliding with anything.
	/// </summary>
	/// <param name="s">The snake to test. Must not be null.</param>
	/// <param name="dir">The candidate direction. Must not be null.</param>
	/// <returns><c>true</c> if the move is collision-free.</returns>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="s"/> or <paramref name="dir"/> is null.</exception>
	public bool CanMove(Snake s, Vector2D dir)
	{
		ArgumentNullException.ThrowIfNull(s);
		ArgumentNullException.ThrowIfNull(dir);
		bool result = true;
		Vector2D oldHead = new Vector2D(s.Head);
		bool needsPop = s.MoveHead(dir, Size);
		if (DoesSnakeCollide(s))
		{
			result = false;
		}
		s.UndoMoveHead(oldHead, needsPop);
		return result;
	}

	/// <summary>
	/// Returns <c>true</c> if <paramref name="s"/>'s head currently overlaps any wall or any collidable snake segment.
	/// </summary>
	/// <param name="s">The snake to test. Must not be null.</param>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="s"/> is null.</exception>
	public bool DoesSnakeCollide(Snake s)
	{
		ArgumentNullException.ThrowIfNull(s);
		foreach (Snake item in Snakes.Values.Where((Snake x) => ShouldSnakeBlockSpace(x)))
		{
			if (s.CollidesWith(item))
			{
				return true;
			}
		}
		foreach (Wall value in Walls.Values)
		{
			if (s.CollidesWith(value))
			{
				return true;
			}
		}
		return false;
	}

	/// <summary>Removes dead powerups and disconnected snakes; resets per-tick state flags.</summary>
	public void Cleanup()
	{
		foreach (int item in new List<int>(Snakes.Keys))
		{
			Snakes[item].FinishJoin();
			Snakes[item].ResetDie();
			if (Snakes[item].IsDisconnected())
			{
				Snakes.Remove(item);
			}
		}
		foreach (int item2 in new List<int>(Powerups.Keys))
		{
			if (!Powerups[item2].IsAlive())
			{
				Powerups.Remove(item2);
			}
		}
	}
}
