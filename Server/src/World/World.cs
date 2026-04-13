using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using Newtonsoft.Json;

namespace SnakeGame;

public static class Constants
{
	public const int SnakeWidth = 10;

	public const int MinSnakeLength = 120;

	public const int HPBarWidth = 40;

	public const int WallWidth = 50;

	public const float SnakeSpeed = 3f;

	public const int MaxHP = 3;

	public const int MaxPowerupDelay = 200;

	public const int MaxPowerups = 20;

	public const int GrowthFrames = 12;

	public const float ViewScale = 1f;

	public const int ViewSize = 900;

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

	public Powerup()
	{
		ID = nextPowerID++;
		location = new Vector2D(0.0, 0.0);
	}

	public Powerup(Vector2D loc)
	{
		ID = nextPowerID++;
		location = new Vector2D(loc);
	}

	public int GetID()
	{
		return ID;
	}

	public Vector2D GetLocation()
	{
		return location;
	}

	public void Die()
	{
		died = true;
	}

	public bool IsAlive()
	{
		return !died;
	}

	public override string ToString()
	{
		return JsonConvert.SerializeObject((object)this);
	}
}
[JsonObject(MemberSerialization.OptIn)]
public class Snake
{
	private uint lastDeath;

	[JsonProperty(PropertyName = "snake")]
	public int ID { get; private set; }

	[JsonProperty(PropertyName = "body")]
	public LinkedList<Vector2D> body { get; private set; }

	[JsonProperty(PropertyName = "dir")]
	public Vector2D direction { get; private set; }

	[JsonProperty(PropertyName = "name")]
	public string name { get; private set; } = "";

	[JsonProperty(PropertyName = "score")]
	public int score { get; private set; }

	[JsonProperty(PropertyName = "died")]
	public bool died { get; private set; }

	[JsonProperty(PropertyName = "alive")]
	public bool Alive { get; private set; } = true;

	[JsonProperty(PropertyName = "dc")]
	public bool disconnected { get; private set; }

	[JsonProperty(PropertyName = "join")]
	public bool joined { get; private set; }

	[JsonProperty(PropertyName = "skin")]
	public int skin { get; set; }

	public int NumSegments => body.Count - 1;

	public float speed { get; set; } = 3f;

	public int growing { get; set; }

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

	public bool updated { get; set; }

	public IEnumerable<(Vector2D v1, Vector2D v2)> Segments()
	{
		LinkedListNode<Vector2D> current = body.First;
		if (current != null)
		{
			while (current.Next != null)
			{
				yield return (v1: current.Value, v2: current.Next.Value);
				current = current.Next;
			}
		}
	}

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

	public Snake(string _name, Vector2D t, Vector2D h, int _id, int _skin = 0)
	{
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

	public void Powerup()
	{
		IncreaseScore();
		growing += 12;
	}

	public void Discontinue(uint time)
	{
		if (Alive)
		{
			Die(time);
		}
		disconnected = true;
	}

	public bool IsDisconnected()
	{
		return disconnected;
	}

	public void Join()
	{
		joined = true;
	}

	public void FinishJoin()
	{
		joined = false;
	}

	public bool IsJoining()
	{
		return joined;
	}

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

	public bool CollidesWith(Snake other, bool checkForAttemptedTurn = false)
	{
		IEnumerable<(Vector2D, Vector2D)> enumerable;
		if (ID == other.ID)
		{
			int num = 2;
			LinkedListNode<Vector2D> linkedListNode = body.Last?.Previous?.Previous?.Previous;
			while (linkedListNode != null)
			{
				Vector2D vector2D = linkedListNode.Value - linkedListNode.Next.Value;
				Vector2D vector2D2 = linkedListNode.Next.Next.Value - linkedListNode.Next.Next.Next.Value;
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

	public bool CollidesWith(Wall w)
	{
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

	public void Die(uint time)
	{
		died = true;
		lastDeath = time;
		Alive = false;
	}

	public void ResetDie()
	{
		died = false;
	}

	public bool Died()
	{
		return died;
	}

	public uint GetLastDeath()
	{
		return lastDeath;
	}

	public int GetScore()
	{
		return score;
	}

	public void IncreaseScore()
	{
		score++;
	}

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

	public int GetID()
	{
		return ID;
	}

	public string GetName()
	{
		return name;
	}

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
		if (body.First.Value.Equals(body.First.Next.Value))
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

	public void UndoMoveHead(Vector2D oldHead, bool needsPop)
	{
		if (needsPop)
		{
			body.RemoveLast();
		}
		Head = new Vector2D(oldHead);
	}

	public bool MoveHead(Vector2D dir, int worldSize)
	{
		Vector2D vector2D = Head - body.Last.Previous.Value;
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

	public void Update(uint time, int worldSize)
	{
		MoveHead(direction, worldSize);
		if (growing == 0)
		{
			Vector2D vector2D = body.First.Next.Value - Tail;
			vector2D.Normalize();
			Tail += vector2D * speed;
		}
		if (Tail.Equals(body.First.Next.Value))
		{
			body.RemoveFirst();
		}
		CheckWrap(worldSize);
		growing = Math.Max(0, growing - 1);
	}

	public override string ToString()
	{
		return JsonConvert.SerializeObject((object)this);
	}
}
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

	[JsonProperty(PropertyName = "p1")]
	public Vector2D P1 { get; private set; }

	[JsonProperty(PropertyName = "p2")]
	public Vector2D P2 { get; private set; }

	public Wall()
	{
		ID = -1;
		P1 = (P2 = null);
	}

	public Wall(Vector2D _p1, Vector2D _p2)
	{
		ID = nextWallID++;
		P1 = new Vector2D(_p1);
		P2 = new Vector2D(_p2);
		Init();
	}

	private void Init()
	{
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

	public Vector2D GetP1()
	{
		return P1;
	}

	public Vector2D GetP2()
	{
		return P2;
	}

	public int GetID()
	{
		return ID;
	}

	public bool Intersects(Vector2D p, double radius)
	{
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

	public override string ToString()
	{
		return JsonConvert.SerializeObject((object)this);
	}
}
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

	public int Size { get; private set; }

	public Dictionary<int, Snake> Snakes { get; private set; } = new Dictionary<int, Snake>();

	public Dictionary<int, Wall> Walls { get; private set; } = new Dictionary<int, Wall>();

	public Dictionary<int, Powerup> Powerups { get; private set; } = new Dictionary<int, Powerup>();

	public World()
	{
		Size = 750;
		time = 0u;
		nextPowerup = 0u;
	}

	public World(int size)
		: this()
	{
		Size = size;
	}

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

	public void Clear()
	{
		Snakes.Clear();
		Walls.Clear();
		Powerups.Clear();
		pendingDeathDrops.Clear();
	}

	public uint GetTime()
	{
		return time;
	}

	public Snake AddSnake(string name, Vector2D t, Vector2D h, int ID, int skin = 0)
	{
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

	public Snake AddRandomSnake(string name, int ID, int skin = 0)
	{
		var (t, h) = RandomSnakeSpawn();
		return AddSnake(name, t, h, ID, skin);
	}

	public Powerup AddRandomPowerup()
	{
		Powerup powerup = new Powerup(RandomLocation(5f));
		Powerups.Add(powerup.GetID(), powerup);
		return powerup;
	}

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
			if (sDir == null) continue;
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

	public void ProcessCommand(Snake s, string cmd)
	{
		JsonSerializerSettings val = new JsonSerializerSettings();
		val.MissingMemberHandling = (MissingMemberHandling)0;
		ControlCommand controlCommand;
		try
		{
			controlCommand = JsonConvert.DeserializeObject<ControlCommand>(cmd, val);
		}
		catch (Exception)
		{
			return;
		}
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

	public bool CanMove(Snake s, Vector2D dir)
	{
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

	public bool DoesSnakeCollide(Snake s)
	{
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
