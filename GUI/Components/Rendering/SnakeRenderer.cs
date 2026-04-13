// <copyright file="SnakeRenderer.cs" company="Snake PS9">
// Copyright (c) 2026 Alex Waldmann & George Higbie. All rights reserved.
// </copyright>
// Authors: Alex Waldmann, George Higbie
// Date: 2026-04-12

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Blazor.Extensions.Canvas.Canvas2D;
using GUI.Components.Controllers;
using GUI.Components.Models;

namespace GUI.Components.Rendering;

/// <summary>
/// Handles all HTML5 canvas drawing for the Snake game.
/// Extracted from SnakeGUI.razor to keep rendering logic separate from UI concerns.
/// </summary>
public class SnakeRenderer
{
    // ==================== Constants ====================

    /// <summary>Width of the snake body stroke in pixels.</summary>
    public const int SnakeWidth = 10;

    /// <summary>Half the wall segment width used for bounding-box calculation.</summary>
    private const int WallHalfWidth = 25;

    /// <summary>Full thickness of a wall segment in pixels.</summary>
    private const int WallThickness = 50;

    /// <summary>Distance between explosion particle sample points along the body.</summary>
    private const double BitDistance = 20.0;

    /// <summary>Seconds between successive particle spawns along the body during a death explosion.</summary>
    private const double ExplosionDelay = 0.05;

    /// <summary>How long each explosion particle remains visible (seconds).</summary>
    private const double ParticleLifespan = 0.6;

    /// <summary>Distance between background grid lines in pixels.</summary>
    private const int GridSpacing = 50;

    /// <summary>Side length of a single wall brick cell in pixels.</summary>
    private const int BrickSize = 25;

    /// <summary>Duration of the powerup pop-in animation (seconds).</summary>
    private const double PowerupPopDurationSeconds = 0.11;

    /// <summary>Bounce amplitude of the powerup pop-in easing curve.</summary>
    private const double PowerupPopBounceAmplitude = 0.30;

    // ==================== Inner Types ====================

    /// <summary>Tracks a per-snake death explosion animation.</summary>
    public class DeathAnim
    {
        /// <summary>Seconds elapsed since the explosion started.</summary>
        public double ElapsedSeconds { get; set; }

        /// <summary>The body path captured at death.</summary>
        public IReadOnlyList<Point2D> Path { get; set; } = Array.Empty<Point2D>();

        /// <summary>Visual appearance to render during the explosion.</summary>
        public SnakeSkin Skin { get; set; } = SnakeSkin.AllSkins[0];

        /// <summary>Whether the animation has fully played out.</summary>
        public bool IsFinished { get; set; }
    }

    /// <summary>Smooth camera state for a viewport.</summary>
    public class CameraState
    {
        /// <summary>Current camera X position.</summary>
        public double X;

        /// <summary>Current camera Y position.</summary>
        public double Y;

        /// <summary>Whether the camera has been initialized to a target.</summary>
        public bool Initialized;

        /// <summary>Resets camera to the uninitialized state.</summary>
        public void Reset() { X = 0; Y = 0; Initialized = false; }
    }

    // ==================== Wall Cache ====================

    /// <summary>Per-controller cache of occupied wall cell coordinates for brick rendering.</summary>
    private readonly Dictionary<GameController, HashSet<(int cx, int cy)>> _wallCaches = new();

    /// <summary>Tracks when each powerup was first seen (for pop-in animation timing).</summary>
    private readonly Dictionary<int, DateTime> _powerupFirstSeenAt = new();

    /// <summary>Clears the cached wall cell grid for the given controller.</summary>
    public void ClearWallCache(GameController gc) => _wallCaches.Remove(gc);

    // ==================== Skin Preview ====================

    /// <summary>Static wavy path for the skin preview canvases.</summary>
    private static readonly IReadOnlyList<Point2D> PreviewPath = BuildPreviewPath();

    /// <summary>Builds the static sine-wave body path used for skin preview canvases.</summary>
    private static List<Point2D> BuildPreviewPath()
    {
        var pts = new List<Point2D>();
        for (int x = 10; x <= 140; x += 5)
            pts.Add(new Point2D(x, 18 + (int)(5 * Math.Sin(x * 0.065))));
        return pts;
    }

    /// <summary>Draws a skin preview on a small canvas.</summary>
    public async Task DrawSkinPreview(Canvas2DContext ctx, SnakeSkin skin)
    {
        await ctx.ClearRectAsync(0, 0, 160, 40);
        await ctx.SetFillStyleAsync("rgba(255,255,255,0.05)");
        await ctx.FillRectAsync(0, 0, 160, 40);
        await DrawSnakeBody(ctx, PreviewPath, skin);
    }

    // ==================== Viewport Rendering ====================

    /// <summary>
    /// Draws one viewport (full-screen or half in split mode).
    /// Sets up camera transform, draws background, grid, entities, and restores state.
    /// </summary>
    /// <returns>The screen-space head position of the player snake (for scoreboard dithering).</returns>
    public async Task<(double headX, double headY)?> DrawViewport(
        Canvas2DContext ctx,
        GameController gc, Dictionary<int, DeathAnim> deathAnims,
        bool showDeath, int frozenCamX, int frozenCamY, int? centerTargetId,
        int vx, int vy, int vw, int vh, double elapsed, CameraState cam,
        SnakeSkin playerSkin, bool drawGrid, bool highQuality)
    {
        var worldSize = gc.WorldSize ?? 2000;
        var snakes = gc.GetSnakes();
        var playerId = gc.PlayerId;

        Snake? centerSnake = null;
        if (centerTargetId.HasValue)
            centerSnake = snakes.FirstOrDefault(s => s.Id == centerTargetId.Value);
        centerSnake ??= playerId.HasValue ? snakes.FirstOrDefault(s => s.Id == playerId.Value) : null;

        double targetCamX = 0, targetCamY = 0;
        if (showDeath) { targetCamX = frozenCamX; targetCamY = frozenCamY; }
        else if (centerSnake?.Body?.Count > 0)
        {
            var head = centerSnake.Body[^1];
            targetCamX = head.X;
            targetCamY = head.Y;
        }

        var halfWorld = worldSize / 2;
        var minCx = -halfWorld + vw / 2;
        var maxCx = halfWorld - vw / 2;
        var minCy = -halfWorld + vh / 2;
        var maxCy = halfWorld - vh / 2;
        targetCamX = minCx < maxCx ? Math.Clamp(targetCamX, minCx, maxCx) : 0;
        targetCamY = minCy < maxCy ? Math.Clamp(targetCamY, minCy, maxCy) : 0;

        if (!cam.Initialized) { cam.X = targetCamX; cam.Y = targetCamY; cam.Initialized = true; }
        else
        {
            double smoothing = 8.0;
            double lerpFactor = 1.0 - Math.Exp(-smoothing * elapsed);
            cam.X += (targetCamX - cam.X) * lerpFactor;
            cam.Y += (targetCamY - cam.Y) * lerpFactor;
        }

        await ctx.SaveAsync();
        await ctx.BeginPathAsync();
        await ctx.RectAsync(vx, vy, vw, vh);
        await ctx.ClipAsync();

        var camX = vx + vw / 2.0 - cam.X;
        var camY = vy + vh / 2.0 - cam.Y;
        await ctx.TranslateAsync(camX, camY);

        (double headX, double headY)? headScreen = null;
        var playerSnake = playerId.HasValue ? snakes.FirstOrDefault(s => s.Id == playerId.Value) : null;
        if (playerSnake?.Body?.Count > 0)
        {
            var h = playerSnake.Body[^1];
            headScreen = (h.X + camX, h.Y + camY);
        }

        if (drawGrid && highQuality)
            await DrawGrid(ctx, worldSize);

        await DrawDeathAnimations(ctx, elapsed, deathAnims);
        await DrawWalls(ctx, gc, highQuality);
        await DrawPowerups(ctx, gc, highQuality);
        await DrawSnakes(ctx, snakes, playerId, deathAnims, showDeath, playerSkin);

        await ctx.RestoreAsync();
        return headScreen;
    }

    // ==================== Grid ====================

    /// <summary>Draws the faint background grid lines across the world.</summary>
    private static async Task DrawGrid(Canvas2DContext ctx, int worldSize)
    {
        await ctx.SetStrokeStyleAsync("rgba(0,0,0,0.05)");
        await ctx.SetLineWidthAsync(2);
        await ctx.BeginPathAsync();
        var hw = worldSize / 2;
        for (int i = -hw; i <= hw; i += GridSpacing)
        {
            await ctx.MoveToAsync(i, -hw);
            await ctx.LineToAsync(i, hw);
        }
        for (int i = -hw; i <= hw; i += GridSpacing)
        {
            await ctx.MoveToAsync(-hw, i);
            await ctx.LineToAsync(hw, i);
        }
        await ctx.StrokeAsync();
    }

    // ==================== Death Animations ====================

    /// <summary>Draws active death explosion animations and advances their timers.</summary>
    public async Task DrawDeathAnimations(Canvas2DContext ctx, double elapsed, Dictionary<int, DeathAnim> deathAnims)
    {
        foreach (var (id, anim) in deathAnims)
        {
            if (anim.IsFinished) continue;

            var path = anim.Path;
            if (path == null || path.Count < 2) { anim.IsFinished = true; continue; }

            double totalLength = 0;
            var lengths = new List<double>();
            for (int i = 0; i < path.Count - 1; i++)
            {
                var dx = path[i + 1].X - path[i].X;
                var dy = path[i + 1].Y - path[i].Y;
                lengths.Add(Math.Sqrt(dx * dx + dy * dy));
                totalLength += lengths[^1];
            }

            double explosionSpeed = BitDistance / ExplosionDelay;
            double explodedLength = anim.ElapsedSeconds * explosionSpeed;
            double currentDist = 0;

            var remainPts = new List<Point2D>();
            for (int i = 0; i < path.Count - 1; i++)
            {
                var segLen = lengths[i];
                var segDistEnd = totalLength - currentDist;
                if (segDistEnd > explodedLength)
                {
                    if (remainPts.Count == 0) remainPts.Add(path[i]);
                    if (segDistEnd - segLen > explodedLength)
                        remainPts.Add(path[i + 1]);
                    else
                    {
                        var t = segLen > 0.001 ? (segDistEnd - explodedLength) / segLen : 0;
                        remainPts.Add(new Point2D(
                            (int)Math.Round(path[i].X + (path[i + 1].X - path[i].X) * t),
                            (int)Math.Round(path[i].Y + (path[i + 1].Y - path[i].Y) * t)));
                        break;
                    }
                }
                currentDist += segLen;
            }

            if (remainPts.Count >= 2)
                await DrawSnakeBody(ctx, remainPts, anim.Skin);

            currentDist = 0;
            bool headParticleDone = false;
            for (int i = path.Count - 1; i > 0; i--)
            {
                var segLen = lengths[i - 1];
                double steps = Math.Max(1, Math.Ceiling(segLen / BitDistance));
                for (int j = 0; j <= steps; j++)
                {
                    var t = j / steps;
                    var ptDist = currentDist + segLen * t;
                    var timeSinceExplosion = anim.ElapsedSeconds - ptDist / explosionSpeed;

                    if (timeSinceExplosion >= 0 && timeSinceExplosion <= ParticleLifespan)
                    {
                        var bx = path[i].X + (path[i - 1].X - path[i].X) * t;
                        var by = path[i].Y + (path[i - 1].Y - path[i].Y) * t;
                        var bAlpha = Math.Max(0, 1.0 - timeSinceExplosion / ParticleLifespan);
                        bool isHead = !headParticleDone && ptDist < 1.0;
                        if (isHead) headParticleDone = true;
                        var baseRadius = isHead ? 10 : 3;
                        var expandFactor = isHead ? 25 : 12;
                        var bRadius = baseRadius + timeSinceExplosion / ParticleLifespan * expandFactor;

                        await ctx.SetGlobalAlphaAsync((float)bAlpha);
                        await ctx.SetFillStyleAsync(anim.Skin.DeathColor);
                        await ctx.BeginPathAsync();
                        await ctx.ArcAsync(bx, by, bRadius, 0, Math.PI * 2);
                        await ctx.FillAsync();
                    }
                }
                currentDist += segLen;
            }
            await ctx.SetGlobalAlphaAsync(1.0f);

            anim.ElapsedSeconds += elapsed;
            if (anim.ElapsedSeconds > totalLength / explosionSpeed + 1.0)
                anim.IsFinished = true;
        }
    }

    // ==================== Walls ====================

    /// <summary>Draws all wall segments, using high-quality brick rendering when enabled.</summary>
    private async Task DrawWalls(Canvas2DContext ctx, GameController gc, bool highQuality)
    {
        var walls = gc.GetWalls();
        const int bs = BrickSize;

        if (!highQuality)
        {
            await ctx.SetFillStyleAsync("#555555");
            foreach (var w in walls)
            {
                if (w.Point1 == null || w.Point2 == null) continue;
                var (x, y, wdt, hgt) = WallRect(w);
                await ctx.FillRectAsync(x, y, wdt, hgt);
            }
            return;
        }

        if (!_wallCaches.TryGetValue(gc, out var occupied) ||
            (occupied.Count == 0 && walls.Count > 0))
        {
            occupied = new HashSet<(int cx, int cy)>();
            foreach (var w in walls)
            {
                if (w.Point1 == null || w.Point2 == null) continue;
                var (rx, ry, rw, rh) = WallRect(w);
                int cols = (int)Math.Round(rw / bs);
                int rows = (int)Math.Round(rh / bs);
                int startCx = (int)Math.Round(rx / bs);
                int startCy = (int)Math.Round(ry / bs);
                for (int r = 0; r < rows; r++)
                    for (int c = 0; c < cols; c++)
                        occupied.Add((startCx + c, startCy + r));
            }
            _wallCaches[gc] = occupied;
        }

        await ctx.SetFillStyleAsync("#7c9c45");
        foreach (var (cx, cy) in occupied)
            await ctx.FillRectAsync(cx * bs - 3, cy * bs - 3, bs + 6, bs + 6);

        static int FM(int a, int b) => ((a % b) + b) % b;

        static (int col, int row) BrickId(int cx, int cy) =>
            (FM(cy, 2) == 0 ? (int)Math.Floor(cx / 2.0) : (int)Math.Floor((cx - 1) / 2.0), cy);

        static int BrickHash(int a, int b)
        {
            int h = a * 374761393 + b * 668265263;
            h = (h ^ (h >> 13)) * 1274126177;
            return (h ^ (h >> 16)) & 0x7FFFFFFF;
        }

        var bricks = new Dictionary<(int, int), (int minCx, int maxCx, int cy)>();
        foreach (var (cx, cy) in occupied)
        {
            var bid = BrickId(cx, cy);
            if (bricks.TryGetValue(bid, out var prev))
                bricks[bid] = (Math.Min(prev.minCx, cx), Math.Max(prev.maxCx, cx), cy);
            else
                bricks[bid] = (cx, cx, cy);
        }

        const int mt = 1;

        // Outer border with concave-corner patches
        const double bdr = 3;
        await ctx.SetFillStyleAsync("#1a1c1e");
        foreach (var (cx, cy) in occupied)
        {
            double px = cx * bs, py = cy * bs;
            bool nT = occupied.Contains((cx, cy - 1));
            bool nB = occupied.Contains((cx, cy + 1));
            bool nL = occupied.Contains((cx - 1, cy));
            bool nR = occupied.Contains((cx + 1, cy));

            if (!nT) await ctx.FillRectAsync(px - (nL ? 0 : bdr), py - bdr,
                        bs + (nL ? 0 : bdr) + (nR ? 0 : bdr), bdr);
            if (!nB) await ctx.FillRectAsync(px - (nL ? 0 : bdr), py + bs,
                        bs + (nL ? 0 : bdr) + (nR ? 0 : bdr), bdr);
            if (!nL) await ctx.FillRectAsync(px - bdr, py - (nT ? 0 : bdr),
                        bdr, bs + (nT ? 0 : bdr) + (nB ? 0 : bdr));
            if (!nR) await ctx.FillRectAsync(px + bs, py - (nT ? 0 : bdr),
                        bdr, bs + (nT ? 0 : bdr) + (nB ? 0 : bdr));

            if (nT && nR && !occupied.Contains((cx + 1, cy - 1)))
                await ctx.FillRectAsync(px + bs, py - bdr, bdr, bdr);
            if (nT && nL && !occupied.Contains((cx - 1, cy - 1)))
                await ctx.FillRectAsync(px - bdr, py - bdr, bdr, bdr);
            if (nB && nR && !occupied.Contains((cx + 1, cy + 1)))
                await ctx.FillRectAsync(px + bs, py + bs, bdr, bdr);
            if (nB && nL && !occupied.Contains((cx - 1, cy + 1)))
                await ctx.FillRectAsync(px - bdr, py + bs, bdr, bdr);
        }

        // Mortar background
        await ctx.SetFillStyleAsync("#2a2420");
        foreach (var (cx, cy) in occupied)
            await ctx.FillRectAsync(cx * bs, cy * bs, bs, bs);

        // Brick faces
        string[] palette = { "#7a6e63", "#6e6358", "#736860", "#80756a",
                             "#6b6055", "#78706b", "#847a6f", "#716659" };
        for (int ci = 0; ci < palette.Length; ci++)
        {
            await ctx.SetFillStyleAsync(palette[ci]);
            foreach (var (bid, info) in bricks)
            {
                if (BrickHash(bid.Item1, bid.Item2) % palette.Length != ci) continue;
                double bx = info.minCx * bs + mt;
                double by = info.cy * bs + mt;
                double bw = (info.maxCx - info.minCx + 1) * bs - mt * 2;
                double bh = bs - mt * 2;
                await ctx.FillRectAsync(bx, by, bw, bh);
            }
        }

        // Bevel highlights and shadows
        const int bv = 2;
        await ctx.SetFillStyleAsync("rgba(255,255,255,0.13)");
        foreach (var (_, info) in bricks)
        {
            double bx = info.minCx * bs + mt;
            double by = info.cy * bs + mt;
            double bw = (info.maxCx - info.minCx + 1) * bs - mt * 2;
            await ctx.FillRectAsync(bx, by, bw, bv);
        }
        await ctx.SetFillStyleAsync("rgba(255,255,255,0.08)");
        foreach (var (_, info) in bricks)
        {
            double bx = info.minCx * bs + mt;
            double by = info.cy * bs + mt;
            double bh = bs - mt * 2;
            await ctx.FillRectAsync(bx, by, bv, bh);
        }
        await ctx.SetFillStyleAsync("rgba(0,0,0,0.18)");
        foreach (var (_, info) in bricks)
        {
            double bx = info.minCx * bs + mt;
            double by = info.cy * bs + mt;
            double bw = (info.maxCx - info.minCx + 1) * bs - mt * 2;
            double bh = bs - mt * 2;
            await ctx.FillRectAsync(bx, by + bh - bv, bw, bv);
        }
        await ctx.SetFillStyleAsync("rgba(0,0,0,0.12)");
        foreach (var (_, info) in bricks)
        {
            double bx = info.minCx * bs + mt;
            double by = info.cy * bs + mt;
            double bw = (info.maxCx - info.minCx + 1) * bs - mt * 2;
            double bh = bs - mt * 2;
            await ctx.FillRectAsync(bx + bw - bv, by, bv, bh);
        }
    }

    /// <summary>Computes the axis-aligned bounding rectangle for a wall segment.</summary>
    private static (double x, double y, double w, double h) WallRect(Wall wall)
    {
        var x = (double)Math.Min(wall.Point1!.X, wall.Point2!.X) - WallHalfWidth;
        var y = (double)Math.Min(wall.Point1.Y, wall.Point2.Y) - WallHalfWidth;
        var wdt = (double)Math.Abs(wall.Point1.X - wall.Point2.X);
        var hgt = (double)Math.Abs(wall.Point1.Y - wall.Point2.Y);
        if (wdt == 0) { wdt = WallThickness; hgt += WallThickness; }
        else { hgt = WallThickness; wdt += WallThickness; }
        return (x, y, wdt, hgt);
    }

    // ==================== Powerups ====================

    /// <summary>Draws all active powerups with optional pulse and pop-in animation.</summary>
    private async Task DrawPowerups(Canvas2DContext ctx, GameController gc, bool highQuality)
    {
        var now = DateTime.UtcNow;
        var activePowerupIds = new HashSet<int>();
        foreach (var p in gc.GetPowerups())
        {
            if (p.Location == null || p.Died) continue;

            activePowerupIds.Add(p.Id);
            if (!_powerupFirstSeenAt.TryGetValue(p.Id, out var firstSeenAt))
            {
                firstSeenAt = now;
                _powerupFirstSeenAt[p.Id] = now;
            }

            double ageSeconds = (now - firstSeenAt).TotalSeconds;
            double popScale = ComputePowerupPopScale(ageSeconds);
            var pulse = highQuality ? Math.Abs(Math.Sin(now.TimeOfDay.TotalSeconds * 5)) * 3 : 0;
            double outerRadius = (8 + pulse) * popScale;
            double innerRadius = 4 * Math.Max(0.35, popScale);

            await ctx.SetFillStyleAsync("gold");
            await ctx.BeginPathAsync();
            await ctx.ArcAsync(p.Location.X, p.Location.Y, outerRadius, 0, Math.PI * 2);
            await ctx.FillAsync();

            if (highQuality)
            {
                await ctx.SetFillStyleAsync("yellow");
                await ctx.BeginPathAsync();
                await ctx.ArcAsync(p.Location.X, p.Location.Y, innerRadius, 0, Math.PI * 2);
                await ctx.FillAsync();
            }
        }

        if (activePowerupIds.Count > 0 && _powerupFirstSeenAt.Count > activePowerupIds.Count)
        {
            foreach (var id in _powerupFirstSeenAt.Keys.Where(id => !activePowerupIds.Contains(id)).ToList())
                _powerupFirstSeenAt.Remove(id);
        }
    }

    /// <summary>Computes the pop-in scale factor for a newly spawned powerup.</summary>
    private static double ComputePowerupPopScale(double ageSeconds)
    {
        if (ageSeconds <= 0)
            return 0.1;
        if (ageSeconds >= PowerupPopDurationSeconds)
            return 1.0;

        double t = ageSeconds / PowerupPopDurationSeconds;
        double easeOut = 1.0 - Math.Pow(1.0 - t, 4.0);
        double bounce = Math.Sin(t * Math.PI * 1.25) * PowerupPopBounceAmplitude * (1.0 - t * 0.6);
        return Math.Max(0.1, easeOut + bounce);
    }

    // ==================== Snakes ====================

    /// <summary>Draws all living snakes and creates death animations for newly dead ones.</summary>
    private async Task DrawSnakes(Canvas2DContext ctx, IReadOnlyList<Snake> snakes, int? playerId, Dictionary<int, DeathAnim> deathAnims, bool showDeath, SnakeSkin playerSkin)
    {
        foreach (var s in snakes)
        {
            if (s.Disconnected == true) continue;
            if (s.Alive != true) continue;
            if (deathAnims.ContainsKey(s.Id)) continue;
            if (s.Id == playerId && showDeath) continue;

            var body = s.Body;
            if (body == null || body.Count < 2) continue;
            bool isPlayer = s.Id == playerId;
            var skin = isPlayer ? playerSkin : ResolveSkin(s.Skin);

            await DrawSnakeBody(ctx, body, skin);

            if (!string.IsNullOrEmpty(s.Name))
                await DrawNameplate(ctx, s, body);
        }

        // Create death animations for any snake that just died
        foreach (var s in snakes)
        {
            if ((s.Died == true || s.Alive != true) && s.Body?.Count >= 2 && !deathAnims.ContainsKey(s.Id) && s.Disconnected != true)
            {
                var deathSkin = (s.Id == playerId) ? playerSkin : ResolveSkin(s.Skin);
                deathAnims[s.Id] = new DeathAnim
                {
                    Path = s.Body.ToList(),
                    Skin = deathSkin
                };
            }
            else if (s.Alive == true && deathAnims.ContainsKey(s.Id))
            {
                deathAnims.Remove(s.Id);
            }
        }
    }

    /// <summary>Draws the floating name/score pill above a snake's head.</summary>
    private static async Task DrawNameplate(Canvas2DContext ctx, Snake s, IReadOnlyList<Point2D> body)
    {
        var scoreTxt = (s.Score ?? 0).ToString();
        var nameTxt = s.Name!;
        var fullTxt = $"{nameTxt}  {scoreTxt}";

        var npHead = body[^1];
        double npHeadR = SnakeWidth / 2.0;

        await ctx.SetFontAsync("bold 11px 'Segoe UI', Arial, sans-serif");
        var fullMeasure = await ctx.MeasureTextAsync(fullTxt);
        var tw = fullMeasure.Width;

        var padX = 8.0;
        var pillH = 18.0;
        var pillW = tw + padX * 2;
        var rr = pillH / 2.0;
        var px = npHead.X - pillW / 2.0;
        var py = npHead.Y - (npHeadR + 2) - pillH - 6;

        // Shadow
        await ctx.SetFillStyleAsync("rgba(0,0,0,0.3)");
        await DrawRoundRect(ctx, px + 1, py + 1, pillW, pillH, rr);
        await ctx.FillAsync();

        // Background pill
        await ctx.SetFillStyleAsync("rgba(0,0,0,0.65)");
        await DrawRoundRect(ctx, px, py, pillW, pillH, rr);
        await ctx.FillAsync();

        // Subtle border
        await ctx.SetStrokeStyleAsync("rgba(255,255,255,0.12)");
        await ctx.SetLineWidthAsync(1);
        await DrawRoundRect(ctx, px, py, pillW, pillH, rr);
        await ctx.StrokeAsync();

        // Name text
        var textY = py + pillH / 2.0 + 4.0;
        await ctx.SetFillStyleAsync("rgba(255,255,255,0.95)");
        await ctx.SetTextAlignAsync(TextAlign.Center);
        await ctx.FillTextAsync(fullTxt, npHead.X, textY);
        await ctx.SetTextAlignAsync(TextAlign.Left);
    }

    /// <summary>Draws a rounded rectangle path (caller must Fill or Stroke after).</summary>
    private static async Task DrawRoundRect(Canvas2DContext ctx, double x, double y, double w, double h, double r)
    {
        await ctx.BeginPathAsync();
        await ctx.MoveToAsync(x + r, y);
        await ctx.LineToAsync(x + w - r, y);
        await ctx.ArcAsync(x + w - r, y + r, r, -Math.PI / 2, 0);
        await ctx.LineToAsync(x + w, y + h - r);
        await ctx.ArcAsync(x + w - r, y + h - r, r, 0, Math.PI / 2);
        await ctx.LineToAsync(x + r, y + h);
        await ctx.ArcAsync(x + r, y + h - r, r, Math.PI / 2, Math.PI);
        await ctx.LineToAsync(x, y + r);
        await ctx.ArcAsync(x + r, y + r, r, Math.PI, Math.PI * 1.5);
    }

    /// <summary>Resolves a skin from the server-provided index, falling back to the default.</summary>
    public static SnakeSkin ResolveSkin(int? skinIndex)
    {
        if (skinIndex.HasValue && skinIndex.Value >= 0 && skinIndex.Value < SnakeSkin.AllSkins.Length)
            return SnakeSkin.AllSkins[skinIndex.Value];
        return SnakeSkin.AllSkins[0];
    }

    // ==================== Snake Body Drawing ====================

    /// <summary>Draws a complete snake (outline, fill, pattern, belly, specular, head + eyes).</summary>
    public async Task DrawSnakeBody(Canvas2DContext ctx, IReadOnlyList<Point2D> body, SnakeSkin skin)
    {
        if (body.Count < 2) return;

        var outlineCol = skin.OutlineColor ?? "rgba(0,0,0,0.35)";

        // 1. Outline
        await ctx.SetStrokeStyleAsync(outlineCol);
        await ctx.SetLineWidthAsync(SnakeWidth + 4);
        await ctx.SetLineCapAsync(LineCap.Round);
        await ctx.SetLineJoinAsync(LineJoin.Round);
        await ctx.BeginPathAsync();
        await ctx.MoveToAsync(body[0].X, body[0].Y);
        for (int i = 1; i < body.Count; i++)
            await ctx.LineToAsync(body[i].X, body[i].Y);
        await ctx.StrokeAsync();

        // 2. Fill
        await ctx.SetStrokeStyleAsync(skin.BodyColor);
        await ctx.SetLineWidthAsync(SnakeWidth);
        await ctx.SetLineCapAsync(LineCap.Round);
        await ctx.SetLineJoinAsync(LineJoin.Round);
        await ctx.BeginPathAsync();
        await ctx.MoveToAsync(body[0].X, body[0].Y);
        for (int i = 1; i < body.Count; i++)
            await ctx.LineToAsync(body[i].X, body[i].Y);
        await ctx.StrokeAsync();

        // 3. Pattern
        if (skin.BodyAccent != null && skin.Pattern != BodyPattern.Solid)
            await DrawBodyPattern(ctx, body, skin);

        // 4. Belly
        if (skin.BellyColor != null)
        {
            await ctx.SetStrokeStyleAsync(skin.BellyColor);
            await ctx.SetLineWidthAsync(3);
            await ctx.SetLineCapAsync(LineCap.Round);
            await ctx.SetLineJoinAsync(LineJoin.Round);
            await ctx.SetGlobalAlphaAsync(0.35f);
            await ctx.BeginPathAsync();
            await ctx.MoveToAsync(body[0].X, body[0].Y);
            for (int i = 1; i < body.Count; i++)
                await ctx.LineToAsync(body[i].X, body[i].Y);
            await ctx.StrokeAsync();
            await ctx.SetGlobalAlphaAsync(1.0f);
        }

        // 5. Specular
        await ctx.SetStrokeStyleAsync("rgba(255,255,255,0.12)");
        await ctx.SetLineWidthAsync(2);
        await ctx.SetLineCapAsync(LineCap.Round);
        await ctx.SetLineJoinAsync(LineJoin.Round);
        await ctx.BeginPathAsync();
        await ctx.MoveToAsync(body[0].X, body[0].Y);
        for (int i = 1; i < body.Count; i++)
            await ctx.LineToAsync(body[i].X, body[i].Y);
        await ctx.StrokeAsync();

        // 6. Head
        await DrawHead(ctx, body, skin);
    }

    /// <summary>Draws the snake head circle with eyes and pupils, rotated to face the movement direction.</summary>
    private static async Task DrawHead(Canvas2DContext ctx, IReadOnlyList<Point2D> body, SnakeSkin skin)
    {
        var head = body[^1];
        var neck = body[^2];
        double headAngle = Math.Atan2(head.Y - neck.Y, head.X - neck.X);
        double headR = SnakeWidth * 0.7;

        await ctx.SaveAsync();
        await ctx.TranslateAsync(head.X, head.Y);
        await ctx.RotateAsync((float)headAngle);

        await ctx.SetFillStyleAsync(skin.HeadColor);
        await ctx.BeginPathAsync();
        await ctx.ArcAsync(1, 0, (float)headR, 0, Math.PI * 2);
        await ctx.FillAsync();

        float eyeOff = (float)(headR * 0.45);
        float eyeR = (float)(headR * 0.42);
        float pupilR = (float)(headR * 0.22);
        float eyeFwd = (float)(headR * 0.35);

        await ctx.SetFillStyleAsync(skin.EyeColor);
        await ctx.BeginPathAsync();
        await ctx.ArcAsync(eyeFwd, -eyeOff, eyeR, 0, Math.PI * 2);
        await ctx.FillAsync();
        await ctx.BeginPathAsync();
        await ctx.ArcAsync(eyeFwd, eyeOff, eyeR, 0, Math.PI * 2);
        await ctx.FillAsync();

        await ctx.SetFillStyleAsync(skin.PupilColor);
        await ctx.BeginPathAsync();
        await ctx.ArcAsync(eyeFwd + pupilR * 0.3f, -eyeOff, pupilR, 0, Math.PI * 2);
        await ctx.FillAsync();
        await ctx.BeginPathAsync();
        await ctx.ArcAsync(eyeFwd + pupilR * 0.3f, eyeOff, pupilR, 0, Math.PI * 2);
        await ctx.FillAsync();

        await ctx.RestoreAsync();
    }

    // ==================== Body Patterns ====================

    /// <summary>Dispatches body pattern rendering based on the skin's pattern type.</summary>
    private async Task DrawBodyPattern(Canvas2DContext ctx, IReadOnlyList<Point2D> body, SnakeSkin skin)
    {
        if (skin.BodyAccent == null || skin.Pattern == BodyPattern.Solid) return;
        if (body.Count < 2) return;

        if (skin.Pattern == BodyPattern.Stripe)
        {
            await DrawStripePattern(ctx, body, skin);
            return;
        }

        await DrawPerpendicularPattern(ctx, body, skin);
    }

    /// <summary>Draws alternating color stripe bands along the snake body using dash patterns.</summary>
    private static async Task DrawStripePattern(Canvas2DContext ctx, IReadOnlyList<Point2D> body, SnakeSkin skin)
    {
        float band = 10f;
        int colorCount = skin.BodyAccent2 != null ? 3 : 2;

        await ctx.SaveAsync();
        await ctx.SetLineWidthAsync(SnakeWidth);
        await ctx.SetLineCapAsync(LineCap.Butt);
        await ctx.SetLineJoinAsync(LineJoin.Round);

        if (colorCount == 2)
        {
            await ctx.SetStrokeStyleAsync(skin.BodyAccent!);
            await ctx.SetLineDashAsync(new float[] { band, band });
            await ctx.SetLineDashOffsetAsync(band);
            await ctx.BeginPathAsync();
            await ctx.MoveToAsync(body[0].X, body[0].Y);
            for (int i = 1; i < body.Count; i++)
                await ctx.LineToAsync(body[i].X, body[i].Y);
            await ctx.StrokeAsync();
        }
        else
        {
            await ctx.SetStrokeStyleAsync(skin.BodyAccent!);
            await ctx.SetLineDashAsync(new float[] { band, 2 * band });
            await ctx.SetLineDashOffsetAsync(2 * band);
            await ctx.BeginPathAsync();
            await ctx.MoveToAsync(body[0].X, body[0].Y);
            for (int i = 1; i < body.Count; i++)
                await ctx.LineToAsync(body[i].X, body[i].Y);
            await ctx.StrokeAsync();

            await ctx.SetStrokeStyleAsync(skin.BodyAccent2!);
            await ctx.SetLineDashAsync(new float[] { band, 2 * band });
            await ctx.SetLineDashOffsetAsync(band);
            await ctx.BeginPathAsync();
            await ctx.MoveToAsync(body[0].X, body[0].Y);
            for (int i = 1; i < body.Count; i++)
                await ctx.LineToAsync(body[i].X, body[i].Y);
            await ctx.StrokeAsync();
        }

        await ctx.SetLineDashAsync(new float[] { });
        await ctx.RestoreAsync();
    }

    /// <summary>Draws checker, diamond, or wave marks perpendicular to the body path.</summary>
    private static async Task DrawPerpendicularPattern(Canvas2DContext ctx, IReadOnlyList<Point2D> body, SnakeSkin skin)
    {
        int segCount = body.Count - 1;
        var segNormals = new (double nx, double ny)[segCount];
        for (int s = 0; s < segCount; s++)
        {
            double dx = body[s + 1].X - body[s].X;
            double dy = body[s + 1].Y - body[s].Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 0.001) { segNormals[s] = (0, 1); continue; }
            segNormals[s] = (-dy / len, dx / len);
        }

        var vtxNormals = new (double nx, double ny)[body.Count];
        vtxNormals[0] = segCount > 0 ? segNormals[0] : (0, 1);
        vtxNormals[^1] = segCount > 0 ? segNormals[^1] : (0, 1);
        for (int v = 1; v < body.Count - 1; v++)
        {
            double bx = segNormals[v - 1].nx + segNormals[v].nx;
            double by = segNormals[v - 1].ny + segNormals[v].ny;
            double bLen = Math.Sqrt(bx * bx + by * by);
            if (bLen > 0.001) { bx /= bLen; by /= bLen; }
            else { bx = segNormals[v].nx; by = segNormals[v].ny; }
            vtxNormals[v] = (bx, by);
        }

        double spacing = skin.Pattern switch
        {
            BodyPattern.Checker => 12,
            BodyPattern.Diamond => 18,
            BodyPattern.Wave => 10,
            _ => 14
        };

        double blendDist = SnakeWidth * 1.5;
        double accumulated = 0;
        int markIdx = 0;
        for (int i = 1; i < body.Count; i++)
        {
            int seg = i - 1;
            double sdx = body[i].X - body[i - 1].X;
            double sdy = body[i].Y - body[i - 1].Y;
            double segLen = Math.Sqrt(sdx * sdx + sdy * sdy);
            if (segLen < 0.001) continue;
            double dirX = sdx / segLen;
            double dirY = sdy / segLen;

            var (snx, sny) = segNormals[seg];
            var (bisStartNx, bisStartNy) = vtxNormals[seg];
            var (bisEndNx, bisEndNy) = vtxNormals[seg + 1];

            double walked = 0;
            while (walked + (spacing - accumulated) <= segLen)
            {
                walked += spacing - accumulated;
                accumulated = 0;
                double px = body[i - 1].X + sdx * (walked / segLen);
                double py = body[i - 1].Y + sdy * (walked / segLen);

                double distFromStart = walked;
                double distFromEnd = segLen - walked;

                double nx, ny;
                if (distFromStart < blendDist && seg > 0)
                {
                    double t = distFromStart / blendDist;
                    t = t * t * (3 - 2 * t);
                    nx = bisStartNx + (snx - bisStartNx) * t;
                    ny = bisStartNy + (sny - bisStartNy) * t;
                }
                else if (distFromEnd < blendDist && seg < segCount - 1)
                {
                    double t = distFromEnd / blendDist;
                    t = t * t * (3 - 2 * t);
                    nx = bisEndNx + (snx - bisEndNx) * t;
                    ny = bisEndNy + (sny - bisEndNy) * t;
                }
                else
                {
                    nx = snx;
                    ny = sny;
                }

                double nLen = Math.Sqrt(nx * nx + ny * ny);
                if (nLen > 0.001) { nx /= nLen; ny /= nLen; }

                await DrawPatternMark(ctx, skin, px, py, nx, ny, dirX, dirY, markIdx);
                markIdx++;
            }
            accumulated += segLen - walked;
        }
    }

    /// <summary>Draws a single pattern mark (checker dot, diamond, or wave dot) at the given position.</summary>
    private static async Task DrawPatternMark(Canvas2DContext ctx, SnakeSkin skin, double px, double py, double nx, double ny, double dx, double dy, int idx)
    {
        double halfW = SnakeWidth / 2.0;

        switch (skin.Pattern)
        {
            case BodyPattern.Stripe:
                int colorCount = skin.BodyAccent2 != null ? 3 : 2;
                int bandPhase = idx % colorCount;
                if (bandPhase == 0) break;
                string stripeCol = (bandPhase == 2 && skin.BodyAccent2 != null) ? skin.BodyAccent2 : skin.BodyAccent!;
                await ctx.SetStrokeStyleAsync(stripeCol);
                await ctx.SetLineWidthAsync(SnakeWidth);
                await ctx.SetLineCapAsync(LineCap.Butt);
                await ctx.BeginPathAsync();
                double ext = SnakeWidth;
                await ctx.MoveToAsync(px + nx * ext, py + ny * ext);
                await ctx.LineToAsync(px - nx * ext, py - ny * ext);
                await ctx.StrokeAsync();
                break;

            case BodyPattern.Checker:
                string checkCol = (skin.BodyAccent2 != null && idx % 2 == 1) ? skin.BodyAccent2 : skin.BodyAccent!;
                await ctx.SetFillStyleAsync(checkCol);
                double side = idx % 2 == 0 ? 1 : -1;
                double ccx = px + nx * halfW * 0.4 * side;
                double ccy = py + ny * halfW * 0.4 * side;
                await ctx.BeginPathAsync();
                await ctx.ArcAsync(ccx, ccy, 3.5, 0, Math.PI * 2);
                await ctx.FillAsync();
                break;

            case BodyPattern.Diamond:
                string diaCol = (skin.BodyAccent2 != null && idx % 2 == 1) ? skin.BodyAccent2 : skin.BodyAccent!;
                await ctx.SetFillStyleAsync(diaCol);
                double dSize = halfW * 0.7;
                await ctx.BeginPathAsync();
                await ctx.MoveToAsync(px + dx * dSize, py + dy * dSize);
                await ctx.LineToAsync(px + nx * dSize, py + ny * dSize);
                await ctx.LineToAsync(px - dx * dSize, py - dy * dSize);
                await ctx.LineToAsync(px - nx * dSize, py - ny * dSize);
                await ctx.ClosePathAsync();
                await ctx.FillAsync();
                break;

            case BodyPattern.Wave:
                string waveCol = (skin.BodyAccent2 != null && idx % 2 == 1) ? skin.BodyAccent2 : skin.BodyAccent!;
                await ctx.SetFillStyleAsync(waveCol);
                double waveSide = Math.Sin(idx * 1.2) * halfW * 0.5;
                double wx = px + nx * waveSide;
                double wy = py + ny * waveSide;
                await ctx.SetGlobalAlphaAsync(0.8f);
                await ctx.BeginPathAsync();
                await ctx.ArcAsync(wx, wy, 3.0, 0, Math.PI * 2);
                await ctx.FillAsync();
                await ctx.SetGlobalAlphaAsync(1.0f);
                break;
        }
    }
}
