/**
 * @file SnakeGUI.razor.js
 * JS interop module for the Snake game page.
 * Handles canvas sizing, keyboard input forwarding to Blazor,
 * and the scoreboard dither mask animation.
 *
 * @authors Alex Waldmann, George Higbie
 * @copyright 2026 Alex Waldmann & George Higbie. All rights reserved.
 */

/** @type {DotNet.DotNetObject|null} Blazor .NET object reference for invoking C# methods. */
let instance = null;

/** @type {HTMLCanvasElement|null} The main game canvas element. */
let canvasEl = null;

/** @type {CanvasRenderingContext2D|null} 2D rendering context for the gameplay canvas. */
let renderCtx = null;

/** @type {any} Latest render state pushed from .NET. */
let latestRenderState = null;

/** @type {any} Previous render state used for snapshot interpolation. */
let previousRenderState = null;

/** @type {number} Performance timestamp when the latest render state arrived. */
let latestRenderStateReceivedAt = 0;

/** @type {number} Estimated milliseconds between snapshots for interpolation. */
let interpolationWindowMs = 33;

const INTERPOLATION_MIN_MS = 16;
const INTERPOLATION_MAX_MS = 150;
const INTERPOLATION_EMA_FACTOR = 0.20;
const MAX_EXTRAPOLATION_ALPHA = 1.0;
const MAX_FALLBACK_HEAD_BLEND_DISTANCE = 80;
const TURN_DEBOUNCE_SNAPSHOTS = 1;

/** @type {Map<number, number>} Remaining snapshot-debounce frames per snake after a turn. */
const snakeTurnDebounceById = new Map();

/** @type {Array<any>} Skin catalog pushed from .NET and used by the browser renderer. */
let skinCatalog = [];

/** @type {number} requestAnimationFrame handle for the browser render loop. */
let renderLoopHandle = 0;

/** @type {number} Timestamp of the last browser-rendered frame. */
let lastFrameAt = 0;

// ==================== DIAGNOSTIC INSTRUMENTATION ====================
/** @type {boolean} Enable on-screen debug overlay. */
let debugOverlayEnabled = true;

/** @type {number} Current interpolation alpha for the local player (0.0 - 1.0). */
let currentInterpolationAlpha = 0;

/** @type {boolean} Whether a turn transition was detected on the local player in the current frame. */
let currentFrameTurnTransition = false;

/** @type {Array<number>} Ringbuffer of last 10 alpha values for display. */
const alphaHistory = [];

/** @type {number} Count of snapshots received. */
let snapshotCount = 0;

/** @type {number} Timestamp of the first snapshot (for frequency calculation). */
let firstSnapshotTime = 0;

/** @type {Array<number>} Last 10 snapshot deltas in ms for moving average. */
const snapshotDeltaHistory = [];

/** @type {number} Time of the last snapshot arrival (for computing deltas). */
let lastSnapshotTime = 0;

/** Persistent camera state for each viewport. */
const cameraStates = {
    p1: { x: 0, y: 0, initialized: false },
    p2: { x: 0, y: 0, initialized: false }
};

const BODY_PATTERN = {
    SOLID: 0,
    STRIPE: 1,
    CHECKER: 2,
    DIAMOND: 3,
    WAVE: 4
};

const SNAKE_WIDTH = 10;
const GRID_SPACING = 50;
const WALL_HALF_WIDTH = 25;
const WALL_THICKNESS = 50;
const BIT_DISTANCE = 20;
const EXPLOSION_DELAY = 0.05;
const PARTICLE_LIFESPAN = 0.6;
const POWERUP_POP_DURATION_SECONDS = 0.11;
const POWERUP_POP_BOUNCE_AMPLITUDE = 0.30;

const DEFAULT_SKIN = {
    bodyColor: '#4caf50',
    bodyAccent: '#2e7d32',
    bodyAccent2: null,
    pattern: BODY_PATTERN.SOLID,
    bellyColor: '#a5d6a7',
    outlineColor: null,
    headColor: '#388e3c',
    eyeColor: 'white',
    pupilColor: '#111',
    deathColor: '#4caf50'
};

const viewportFxStates = {
    p1: createViewportFxState(),
    p2: createViewportFxState()
};

/** @type {HTMLElement|null} The scoreboard overlay element used for dither masking. */
let _ditherEl = null;

/** @type {number} Last left-half dither radius (avoids redundant writes). */
let _lastDitherL = -999;
/** @type {number} Last right-half dither radius (avoids redundant writes). */
let _lastDitherR = -999;

/** @type {OffscreenCanvas|HTMLCanvasElement|null} Reusable offscreen canvas for dither mask. */
let _ditherCanvas = null;

/**
 * Draws a star shape at the given position on a 2D context.
 * @param {CanvasRenderingContext2D} c - The drawing context.
 * @param {number} cx - Center X.
 * @param {number} cy - Center Y.
 * @param {number} r  - Star radius.
 */
function _drawDitherStar(c, cx, cy, r) {
    const pull = r * 0.35;
    const py = cy - r, by = cy + r, rx = cx + r, lx = cx - r;
    c.moveTo(cx, py);
    c.bezierCurveTo(cx + pull, py + pull, rx - pull, cy - pull, rx, cy);
    c.bezierCurveTo(rx - pull, cy + pull, cx + pull, by - pull, cx, by);
    c.bezierCurveTo(cx - pull, by - pull, lx + pull, cy + pull, lx, cy);
    c.bezierCurveTo(lx + pull, cy - pull, cx - pull, py + pull, cx, py);
}

/**
 * Fills an area of the offscreen canvas with a tiled dither star pattern.
 * @param {CanvasRenderingContext2D} c - The drawing context.
 * @param {number} x0 - Left x.
 * @param {number} w  - Width to fill.
 * @param {number} h  - Height to fill.
 * @param {number} r  - Star radius. Negative = solid white (no dither).
 */
function _fillDitherRegion(c, x0, w, h, r) {
    if (r < 0) {
        c.fillStyle = 'white';
        c.fillRect(x0, 0, w, h);
        return;
    }
    const cell = 5, half = 2.5;
    c.fillStyle = 'white';
    c.beginPath();
    for (let y = 0; y < h; y += cell) {
        for (let x = x0; x < x0 + w; x += cell) {
            _drawDitherStar(c, x + half, y + half, r);
        }
    }
    c.fill();
}

/**
 * Updates the scoreboard overlay's CSS mask. Supports uniform dithering (single
 * player mode) and split dithering (left half reacts to P1, right half to P2).
 * @param {number} leftR  - Star radius for the left half (or full board if rightR < 0).
 * @param {number} rightR - Star radius for the right half. -1 = single-mode (use leftR for all).
 * @param {boolean} forceSplit - When true, always use split-mask rendering regardless of radii sentinels.
 */
window.updateScoreboardDither = (leftR, rightR, forceSplit = false) => {
    if (!_ditherEl || !_ditherEl.isConnected) {
        _ditherEl = document.getElementById('scoreboardOverlay');
        _lastDitherL = -999;
        _lastDitherR = -999;
    }
    if (!_ditherEl) return;

    // Avoid redundant DOM writes
    if (Math.abs(leftR - _lastDitherL) < 0.01 && Math.abs(rightR - _lastDitherR) < 0.01) return;
    _lastDitherL = leftR;
    _lastDitherR = rightR;

    const isSplit = forceSplit || rightR >= -0.5;

    // ── Single-player mode ──
    if (!isSplit) {
        if (leftR < 0) {
            _ditherEl.style.maskImage = '';
            _ditherEl.style.webkitMaskImage = '';
            return;
        }
        const cell = 5, half = 2.5;
        const pull = leftR * 0.35;
        const py = half - leftR, by = half + leftR, rx = half + leftR, lx = half - leftR;
        const d = `M ${half.toFixed(2)} ${py.toFixed(2)} ` +
            `C ${(half + pull).toFixed(2)} ${(py + pull).toFixed(2)}, ${(rx - pull).toFixed(2)} ${(half - pull).toFixed(2)}, ${rx.toFixed(2)} ${half.toFixed(2)} ` +
            `C ${(rx - pull).toFixed(2)} ${(half + pull).toFixed(2)}, ${(half + pull).toFixed(2)} ${(by - pull).toFixed(2)}, ${half.toFixed(2)} ${by.toFixed(2)} ` +
            `C ${(half - pull).toFixed(2)} ${(by - pull).toFixed(2)}, ${(lx + pull).toFixed(2)} ${(half + pull).toFixed(2)}, ${lx.toFixed(2)} ${half.toFixed(2)} ` +
            `C ${(lx + pull).toFixed(2)} ${(half - pull).toFixed(2)}, ${(half - pull).toFixed(2)} ${(py + pull).toFixed(2)}, ${half.toFixed(2)} ${py.toFixed(2)} Z`;
        const svg = `<svg xmlns='http://www.w3.org/2000/svg' width='${cell}' height='${cell}'><path d='${d}' fill='white'/></svg>`;
        const url = `url("data:image/svg+xml,${encodeURIComponent(svg)}")`;
        _ditherEl.style.maskImage = url;
        _ditherEl.style.webkitMaskImage = url;
        _ditherEl.style.maskSize = '5px 5px';
        _ditherEl.style.webkitMaskSize = '5px 5px';
        _ditherEl.style.maskRepeat = 'repeat';
        _ditherEl.style.webkitMaskRepeat = 'repeat';
        return;
    }

    // ── Split-screen mode: canvas-based per-half dither ──
    const rect = _ditherEl.getBoundingClientRect();
    const w = Math.round(rect.width) || 220;
    const h = Math.round(rect.height) || 180;

    // Allocate a hidden <canvas> once; reuse every frame
    if (!_ditherCanvas) {
        _ditherCanvas = document.createElement('canvas');
    }
    if (_ditherCanvas.width !== w || _ditherCanvas.height !== h) {
        _ditherCanvas.width = w;
        _ditherCanvas.height = h;
    }

    const c = _ditherCanvas.getContext('2d');
    c.clearRect(0, 0, w, h);

    const midX = Math.round(w / 2);
    _fillDitherRegion(c, 0, midX, h, leftR);
    _fillDitherRegion(c, midX, w - midX, h, rightR);

    const url = `url("${_ditherCanvas.toDataURL()}")`;
    _ditherEl.style.maskImage = url;
    _ditherEl.style.webkitMaskImage = url;
    _ditherEl.style.maskSize = `${w}px ${h}px`;
    _ditherEl.style.webkitMaskSize = `${w}px ${h}px`;
    _ditherEl.style.maskRepeat = 'no-repeat';
    _ditherEl.style.webkitMaskRepeat = 'no-repeat';
};

/**
 * Stores the full skin catalog pushed from .NET.
 * @param {Array<any>} skins - The available snake skins.
 */
window.setSnakeSkinCatalog = (skins) => {
    skinCatalog = Array.isArray(skins) ? skins : [];
};

/**
 * Stores the latest render state for the browser-side gameplay canvas.
 * @param {any} state - Latest world/render state from .NET.
 */
window.setSnakeRenderState = (state) => {
    const receivedAt = performance.now();
    
    // Track snapshot frequency for diagnostics.
    snapshotCount++;
    if (firstSnapshotTime === 0) {
        firstSnapshotTime = receivedAt;
    }
    if (lastSnapshotTime > 0) {
        const delta = receivedAt - lastSnapshotTime;
        snapshotDeltaHistory.push(delta);
        if (snapshotDeltaHistory.length > 10) {
            snapshotDeltaHistory.shift();
        }
    }
    lastSnapshotTime = receivedAt;
    
    if (latestRenderState) {
        previousRenderState = latestRenderState;
        const snapshotDelta = receivedAt - latestRenderStateReceivedAt;
        if (Number.isFinite(snapshotDelta) && snapshotDelta > 0) {
            // Smooth cadence changes so one delayed/early packet does not visibly jitter motion.
            const clampedDelta = clamp(snapshotDelta, INTERPOLATION_MIN_MS, INTERPOLATION_MAX_MS);
            interpolationWindowMs = clamp(
                interpolationWindowMs + ((clampedDelta - interpolationWindowMs) * INTERPOLATION_EMA_FACTOR),
                INTERPOLATION_MIN_MS,
                INTERPOLATION_MAX_MS
            );
        }
    }
    else {
        previousRenderState = state;
    }

    latestRenderState = state;
    latestRenderStateReceivedAt = receivedAt;

    if (!state || !state.splitScreen) {
        resetCamera(cameraStates.p2);
        resetViewportFx(viewportFxStates.p2);
    }
};

/**
 * Initializes the JS render environment. Stores the Blazor .NET reference,
 * locates the canvas element, performs the initial resize, and attaches
 * the window resize listener.
 * @param {DotNet.DotNetObject} dotnetRef - Reference used to call C# methods.
 */
window.initRenderJS = (dotnetRef) => {
    instance = dotnetRef;
    window.theInstance = dotnetRef;

    ensureCanvas();
    resizeCanvas();
    window.addEventListener('resize', resizeCanvas);

    // Debug overlay toggle: press 'D' to show/hide diagnostics.
    window.addEventListener('keydown', (e) => {
        if (e.key.toLowerCase() === 'd') {
            debugOverlayEnabled = !debugOverlayEnabled;
        }
    });

    if (!renderLoopHandle) {
        lastFrameAt = performance.now();
        renderLoopHandle = requestAnimationFrame(renderGameFrame);
    }
};

/**
 * Returns the current viewport dimensions and resizes the canvas to match.
 * Called from C# when the render loop needs the latest size.
 * @returns {number[]} Two-element array [width, height] in CSS pixels.
 */
window.getViewportSize = () => {
    ensureCanvas();
    const w = window.innerWidth;
    const h = window.innerHeight;
    if (canvasEl && (canvasEl.width !== w || canvasEl.height !== h)) {
        canvasEl.width = w;
        canvasEl.height = h;
    }
    return [w, h];
};

/**
 * Resizes the canvas element to fill the browser window and notifies
 * the Blazor component of the new dimensions.
 */
function resizeCanvas() {
    ensureCanvas();
    if (!canvasEl) return;
    canvasEl.width = window.innerWidth;
    canvasEl.height = window.innerHeight;
    renderCtx = canvasEl.getContext('2d');
    if (instance) {
        instance.invokeMethodAsync('UpdateViewportSize', [window.innerWidth, window.innerHeight]);
    }
}

/**
 * Lazily queries the DOM for the game canvas if it hasn't been found yet.
 */
function ensureCanvas() {
    if (!canvasEl) {
        canvasEl = document.getElementById('snakeGameCanvas');
    }
}

/** Resets a viewport camera to an uninitialized state. */
function resetCamera(camera) {
    camera.x = 0;
    camera.y = 0;
    camera.initialized = false;
}

/** Resets cached visual-effect state for a viewport. */
function resetViewportFx(fxState) {
    fxState.deathAnims.clear();
    fxState.completedDeathAnims.clear();
    fxState.powerupFirstSeenAt.clear();
    fxState.wallCache = null;
    fxState.wallCacheKey = '';
}

/** Creates per-viewport caches for animations and wall rendering. */
function createViewportFxState() {
    return {
        deathAnims: new Map(),
        /** Snake IDs whose death animation has already played — prevents re-spawning on repeated dead snapshots. */
        completedDeathAnims: new Set(),
        powerupFirstSeenAt: new Map(),
        wallCache: null,
        wallCacheKey: ''
    };
}

/** Builds an interpolated render state between the previous and latest snapshots. */
function buildInterpolatedRenderState(now) {
    if (!latestRenderState) {
        return null;
    }

    if (!previousRenderState || previousRenderState === latestRenderState) {
        return latestRenderState;
    }

    const alpha = clamp((now - latestRenderStateReceivedAt) / interpolationWindowMs, 0, MAX_EXTRAPOLATION_ALPHA);
    
    // Record alpha for diagnostics.
    currentInterpolationAlpha = alpha;
    alphaHistory.push(alpha);
    if (alphaHistory.length > 10) {
        alphaHistory.shift();
    }
    
    return {
        ...latestRenderState,
        p1: interpolateViewport(previousRenderState.p1, latestRenderState.p1, alpha),
        p2: latestRenderState.p2 ? interpolateViewport(previousRenderState.p2, latestRenderState.p2, alpha) : null
    };
}

/** Interpolates one viewport's snakes and powerups. */
function interpolateViewport(previousViewport, currentViewport, alpha) {
    if (!currentViewport) {
        return null;
    }

    return {
        ...currentViewport,
        snakes: interpolateSnakes(previousViewport?.snakes, currentViewport.snakes, currentViewport.playerId, alpha),
        powerups: interpolatePowerups(previousViewport?.powerups, currentViewport.powerups, alpha)
    };
}

/** Interpolates snake bodies between snapshots for smoother movement. */
function interpolateSnakes(previousSnakes, currentSnakes, playerId, alpha) {
    if (!Array.isArray(currentSnakes)) {
        return [];
    }

    const previousById = new Map(Array.isArray(previousSnakes)
        ? previousSnakes.map((snake) => [snake.snake, snake])
        : []);

    return currentSnakes.map((snake) => interpolateSnake(previousById.get(snake.snake), snake, playerId, alpha));
}

/** Interpolates a single snake body when the topology is stable. */
function interpolateSnake(previousSnake, currentSnake, playerId, alpha) {
    if (!previousSnake || currentSnake.died === true || currentSnake.alive !== true) {
        return currentSnake;
    }

    const isLocalPlayer = currentSnake.snake === playerId;
    if (isLocalPlayer) {
        return interpolateLocalPlayerSnake(previousSnake, currentSnake, alpha);
    }

    const snakeId = typeof currentSnake.snake === 'number' ? currentSnake.snake : null;
    if (snakeId !== null && consumeTurnDebounce(snakeId)) {
        return currentSnake;
    }

    if (isDirectionTurnTransition(previousSnake.dir, currentSnake.dir)) {
        if (snakeId !== null) {
            snakeTurnDebounceById.set(snakeId, TURN_DEBOUNCE_SNAPSHOTS);
        }
        return currentSnake;
    }

    if (!Array.isArray(previousSnake.body) || !Array.isArray(currentSnake.body)) {
        return currentSnake;
    }

    if (previousSnake.body.length !== currentSnake.body.length) {
        return interpolateSnakeHeadFallback(previousSnake, currentSnake, alpha);
    }

    return {
        ...currentSnake,
        body: currentSnake.body.map((point, index) => interpolatePoint(previousSnake.body[index], point, alpha)),
        dir: previousSnake.dir && currentSnake.dir ? interpolatePoint(previousSnake.dir, currentSnake.dir, alpha) : currentSnake.dir
    };
}

/**
 * Interpolates snake motion even when point counts differ between snapshots.
 * We blend the head only and keep current topology to avoid body-shape flicker.
 */
function interpolateSnakeHeadFallback(previousSnake, currentSnake, alpha) {
    const currentBody = Array.isArray(currentSnake.body) ? currentSnake.body : [];
    if (currentBody.length === 0) {
        return currentSnake;
    }

    const previousBody = Array.isArray(previousSnake.body) ? previousSnake.body : [];
    const body = currentBody.slice();
    const currentHeadIndex = body.length - 1;
    const previousHeadIndex = previousBody.length - 1;

    if (previousHeadIndex >= 0) {
        const previousHead = previousBody[previousHeadIndex];
        const currentHead = body[currentHeadIndex];
        const dx = currentHead.X - previousHead.X;
        const dy = currentHead.Y - previousHead.Y;
        const stepDistance = Math.hypot(dx, dy);

        // Large one-frame deltas are usually topology churn/respawn transitions.
        // Blending them causes visible flashes, so trust the current snapshot instead.
        if (Number.isFinite(stepDistance) && stepDistance <= MAX_FALLBACK_HEAD_BLEND_DISTANCE) {
            body[currentHeadIndex] = interpolatePoint(previousHead, currentHead, alpha);
        }
    }

    return {
        ...currentSnake,
        body,
        dir: previousSnake.dir && currentSnake.dir ? interpolatePoint(previousSnake.dir, currentSnake.dir, alpha) : currentSnake.dir
    };
}

/**
 * Local-player interpolation prioritizes control accuracy on turns.
 * - If the direction axis changes, render the latest authoritative snapshot.
 * - Otherwise interpolate only the head (not full body topology).
 */
function interpolateLocalPlayerSnake(previousSnake, currentSnake, alpha) {
    currentFrameTurnTransition = false;
    
    if (isDirectionTurnTransition(previousSnake?.dir, currentSnake?.dir)) {
        currentFrameTurnTransition = true;
        return currentSnake;
    }

    const currentBody = Array.isArray(currentSnake.body) ? currentSnake.body : [];
    if (currentBody.length === 0) {
        return currentSnake;
    }

    const previousBody = Array.isArray(previousSnake.body) ? previousSnake.body : [];
    const currentHeadIndex = currentBody.length - 1;
    const previousHeadIndex = previousBody.length - 1;
    if (previousHeadIndex < 0) {
        return currentSnake;
    }

    const body = currentBody.slice();
    const previousHead = previousBody[previousHeadIndex];
    const currentHead = body[currentHeadIndex];
    const dx = currentHead.X - previousHead.X;
    const dy = currentHead.Y - previousHead.Y;
    const stepDistance = Math.hypot(dx, dy);

    if (Number.isFinite(stepDistance) && stepDistance <= MAX_FALLBACK_HEAD_BLEND_DISTANCE) {
        body[currentHeadIndex] = interpolatePoint(previousHead, currentHead, alpha);
    }

    return {
        ...currentSnake,
        body,
        dir: previousSnake.dir && currentSnake.dir ? interpolatePoint(previousSnake.dir, currentSnake.dir, alpha) : currentSnake.dir
    };
}

/**
 * Returns true when movement axis changes between snapshots (a turn transition).
 * Interpolating across axis changes can create transient body kinks while spamming turns.
 */
function isDirectionTurnTransition(previousDir, currentDir) {
    const previousAxis = directionAxis(previousDir);
    const currentAxis = directionAxis(currentDir);
    return previousAxis !== null && currentAxis !== null && previousAxis !== currentAxis;
}

/** Returns 'x', 'y', or null when the direction vector is missing/degenerate. */
function directionAxis(dir) {
    if (!dir || (!Number.isFinite(dir.X) && !Number.isFinite(dir.Y))) {
        return null;
    }

    const absX = Math.abs(dir.X || 0);
    const absY = Math.abs(dir.Y || 0);
    if (absX === 0 && absY === 0) {
        return null;
    }

    return absX >= absY ? 'x' : 'y';
}

/**
 * Consumes one debounce snapshot for the given snake.
 * Returns true when interpolation should be skipped this snapshot.
 */
function consumeTurnDebounce(snakeId) {
    const remaining = snakeTurnDebounceById.get(snakeId);
    if (!Number.isFinite(remaining) || remaining <= 0) {
        return false;
    }

    if (remaining <= 1) {
        snakeTurnDebounceById.delete(snakeId);
    }
    else {
        snakeTurnDebounceById.set(snakeId, remaining - 1);
    }

    return true;
}

/** Interpolates powerup locations between snapshots. */
function interpolatePowerups(previousPowerups, currentPowerups, alpha) {
    if (!Array.isArray(currentPowerups)) {
        return [];
    }

    const previousById = new Map(Array.isArray(previousPowerups)
        ? previousPowerups.map((powerup) => [powerup.power, powerup])
        : []);

    return currentPowerups.map((powerup) => {
        const previousPowerup = previousById.get(powerup.power);
        if (!previousPowerup?.loc || !powerup.loc || powerup.died) {
            return powerup;
        }

        return {
            ...powerup,
            loc: interpolatePoint(previousPowerup.loc, powerup.loc, alpha)
        };
    });
}

/**
 * Draws diagnostic debug information overlay (FPS, alpha, turn flags, snapshot frequency).
 * @param {CanvasRenderingContext2D} ctx - Canvas rendering context.
 * @param {number} width - Canvas width.
 * @param {number} height - Canvas height.
 */
function drawDebugOverlay(ctx, width, height) {
    if (!debugOverlayEnabled) return;

    ctx.save();
    ctx.fillStyle = 'rgba(0, 0, 0, 0.7)';
    ctx.fillRect(10, 10, 320, 180);

    ctx.fillStyle = '#00ff00';
    ctx.font = "11px 'Courier New', monospace";
    ctx.textAlign = 'left';

    const avgSnapshotDelta = snapshotDeltaHistory.length > 0
        ? snapshotDeltaHistory.reduce((a, b) => a + b, 0) / snapshotDeltaHistory.length
        : 0;

    const lines = [
        `[DIAGNOSTICS]`,
        `Alpha: ${currentInterpolationAlpha.toFixed(3)} (${(currentInterpolationAlpha * 100).toFixed(1)}%)`,
        `Turn?: ${currentFrameTurnTransition ? 'YES' : 'NO'}`,
        `Snapshot Δ: ${avgSnapshotDelta.toFixed(1)}ms (${snapshotDeltaHistory.length} samples)`,
        `InterpolWindow: ${interpolationWindowMs.toFixed(1)}ms`,
        `Snapshots: ${snapshotCount} total`,
        `Alpha History: [${alphaHistory.map(a => a.toFixed(2)).join(', ')}]`,
        `Debug: Press 'D' to toggle overlay`,
    ];

    let y = 25;
    for (const line of lines) {
        ctx.fillText(line, 20, y);
        y += 15;
    }

    ctx.restore();
}

/**
 * Interpolates between two points.
 */
function interpolatePoint(previousPoint, currentPoint, alpha) {
    if (!previousPoint || !currentPoint) {
        return currentPoint;
    }

    return {
        X: previousPoint.X + (currentPoint.X - previousPoint.X) * alpha,
        Y: previousPoint.Y + (currentPoint.Y - previousPoint.Y) * alpha
    };
}

/**
 * Main browser-side render loop for gameplay.
 * @param {number} now - Current performance timestamp.
 */
function renderGameFrame(now) {
    ensureCanvas();
    if (canvasEl && !renderCtx) {
        renderCtx = canvasEl.getContext('2d');
    }

    const dt = Math.min(0.05, Math.max(0.001, (now - lastFrameAt) / 1000 || 0.016));
    lastFrameAt = now;

    if (renderCtx && latestRenderState) {
        const interpolatedState = buildInterpolatedRenderState(now);
        if (interpolatedState) {
            drawGame(renderCtx, interpolatedState, dt, now / 1000);
        }
    }

    renderLoopHandle = requestAnimationFrame(renderGameFrame);
}

/**
 * Draws the current game state into the main canvas.
 * @param {CanvasRenderingContext2D} ctx - Canvas rendering context.
 * @param {any} state - Latest render state from .NET.
 * @param {number} dt - Seconds since last rendered frame.
 * @param {number} timeSeconds - Wall-clock time in seconds for animations.
 */
function drawGame(ctx, state, dt, timeSeconds) {
    const width = canvasEl.width;
    const height = canvasEl.height;

    ctx.clearRect(0, 0, width, height);
    ctx.fillStyle = state.highQualityTextures ? '#7c9c45' : '#e8f5ff';
    ctx.fillRect(0, 0, width, height);

    let p1Head = null;
    let p2Head = null;

    if (state.splitScreen && state.p2) {
        const halfW = Math.floor(width / 2);
        p1Head = drawViewport(ctx, state, state.p1, 0, 0, halfW, height, dt, timeSeconds, cameraStates.p1, viewportFxStates.p1);
        p2Head = drawViewport(ctx, state, state.p2, halfW, 0, halfW, height, dt, timeSeconds, cameraStates.p2, viewportFxStates.p2);

        ctx.save();
        ctx.strokeStyle = 'rgba(255,255,255,0.4)';
        ctx.lineWidth = 2;
        ctx.beginPath();
        ctx.moveTo(halfW, 0);
        ctx.lineTo(halfW, height);
        ctx.stroke();

        ctx.fillStyle = 'rgba(255,255,255,0.55)';
        ctx.font = "700 16px 'Avenir Next', 'Segoe UI', sans-serif";
        ctx.textAlign = 'center';
        ctx.fillText('P1 (WASD)', halfW / 2, 24);
        ctx.fillText('P2 (Arrows)', halfW + halfW / 2, 24);
        ctx.restore();
    }
    else if (state.p1) {
        resetCamera(cameraStates.p2);
        p1Head = drawViewport(ctx, state, state.p1, 0, 0, width, height, dt, timeSeconds, cameraStates.p1, viewportFxStates.p1);
    }

    if (state.showScoreboard && state.p1 && state.p1.playerId != null) {
        const scoreboardBounds = getScoreboardBounds(state.splitScreen, width);
        if (state.splitScreen && state.p2) {
            const radii = computeSplitDitherRadii(p1Head, p2Head, state.p1.showDeath, state.p2.showDeath, scoreboardBounds);
            window.updateScoreboardDither(radii.leftR, radii.rightR, true);
        }
        else {
            const ditherR = state.p1.showDeath
                ? -1
                : computeDitherForHead(
                    p1Head,
                    scoreboardBounds.left,
                    scoreboardBounds.top,
                    scoreboardBounds.right,
                    scoreboardBounds.bottom
                );
            window.updateScoreboardDither(ditherR, -1, false);
        }
    }
    else {
        window.updateScoreboardDither(-1, -1, false);
    }

    // Draw diagnostic overlay if enabled.
    drawDebugOverlay(ctx, width, height);
}

/**
 * Draws a single viewport and returns the player's head in screen space.
 * @returns {{x:number,y:number}|null}
 */
function drawViewport(ctx, state, viewport, vx, vy, vw, vh, dt, timeSeconds, camera, fxState) {
    if (!viewport) {
        return null;
    }

    const snakes = Array.isArray(viewport.snakes) ? viewport.snakes : [];
    const worldSize = viewport.worldSize || 2000;
    const playerId = viewport.playerId;

    let centerSnake = null;
    if (viewport.centerTargetSnakeId != null) {
        centerSnake = snakes.find((snake) => snake.snake === viewport.centerTargetSnakeId) || null;
    }
    if (!centerSnake && playerId != null) {
        centerSnake = snakes.find((snake) => snake.snake === playerId) || null;
    }

    let targetCamX = 0;
    let targetCamY = 0;
    if (viewport.showDeath) {
        targetCamX = viewport.frozenCameraX || 0;
        targetCamY = viewport.frozenCameraY || 0;
    }
    else {
        const centerBody = centerSnake?.body;
        if (Array.isArray(centerBody) && centerBody.length > 0) {
            const head = centerBody[centerBody.length - 1];
            targetCamX = head.X;
            targetCamY = head.Y;
        }
    }

    const halfWorld = worldSize / 2;
    const minCx = -halfWorld + vw / 2;
    const maxCx = halfWorld - vw / 2;
    const minCy = -halfWorld + vh / 2;
    const maxCy = halfWorld - vh / 2;
    targetCamX = minCx < maxCx ? clamp(targetCamX, minCx, maxCx) : 0;
    targetCamY = minCy < maxCy ? clamp(targetCamY, minCy, maxCy) : 0;

    if (!camera.initialized) {
        camera.x = targetCamX;
        camera.y = targetCamY;
        camera.initialized = true;
    }
    else {
        const lerpFactor = 1 - Math.exp(-8 * dt);
        camera.x += (targetCamX - camera.x) * lerpFactor;
        camera.y += (targetCamY - camera.y) * lerpFactor;
    }

    ctx.save();
    ctx.beginPath();
    ctx.rect(vx, vy, vw, vh);
    ctx.clip();

    const camX = vx + vw / 2 - camera.x;
    const camY = vy + vh / 2 - camera.y;
    ctx.translate(camX, camY);

    if (state.drawGrid && state.highQualityTextures) {
        drawGrid(ctx, worldSize);
    }

    drawDeathAnimations(ctx, snakes, playerId, viewport.playerSkinIndex, fxState, dt);
    drawWalls(ctx, viewport.walls || [], state.highQualityTextures, fxState);
    drawPowerups(ctx, viewport.powerups || [], state.highQualityTextures, timeSeconds, fxState);
    const head = drawSnakes(ctx, snakes, playerId, viewport.playerSkinIndex, viewport.showDeath, fxState);

    ctx.restore();
    return head ? { x: head.x + camX, y: head.y + camY } : null;
}

/** Draws the faint world grid. */
function drawGrid(ctx, worldSize) {
    const hw = worldSize / 2;
    ctx.save();
    ctx.strokeStyle = 'rgba(0,0,0,0.05)';
    ctx.lineWidth = 2;
    ctx.beginPath();
    for (let i = -hw; i <= hw; i += GRID_SPACING) {
        ctx.moveTo(i, -hw);
        ctx.lineTo(i, hw);
        ctx.moveTo(-hw, i);
        ctx.lineTo(hw, i);
    }
    ctx.stroke();
    ctx.restore();
}

/** Draws all walls using a light bevel in high-quality mode. */
function drawWalls(ctx, walls, highQuality, fxState) {
    if (!highQuality) {
        ctx.fillStyle = '#555555';
        for (const wall of walls) {
            const rect = wallRect(wall);
            if (!rect) continue;
            ctx.fillRect(rect.x, rect.y, rect.w, rect.h);
        }
        return;
    }

    const cache = getWallCache(walls, fxState);
    if (!cache) {
        return;
    }

    ctx.fillStyle = '#7c9c45';
    for (const cell of cache.occupiedCells) {
        ctx.fillRect(cell.cx * 25 - 3, cell.cy * 25 - 3, 31, 31);
    }

    const hasCell = (cx, cy) => cache.occupiedSet.has(`${cx},${cy}`);
    const border = 3;
    ctx.fillStyle = '#1a1c1e';
    for (const cell of cache.occupiedCells) {
        const px = cell.cx * 25;
        const py = cell.cy * 25;
        const nT = hasCell(cell.cx, cell.cy - 1);
        const nB = hasCell(cell.cx, cell.cy + 1);
        const nL = hasCell(cell.cx - 1, cell.cy);
        const nR = hasCell(cell.cx + 1, cell.cy);

        if (!nT) ctx.fillRect(px - (nL ? 0 : border), py - border, 25 + (nL ? 0 : border) + (nR ? 0 : border), border);
        if (!nB) ctx.fillRect(px - (nL ? 0 : border), py + 25, 25 + (nL ? 0 : border) + (nR ? 0 : border), border);
        if (!nL) ctx.fillRect(px - border, py - (nT ? 0 : border), border, 25 + (nT ? 0 : border) + (nB ? 0 : border));
        if (!nR) ctx.fillRect(px + 25, py - (nT ? 0 : border), border, 25 + (nT ? 0 : border) + (nB ? 0 : border));

        if (nT && nR && !hasCell(cell.cx + 1, cell.cy - 1)) ctx.fillRect(px + 25, py - border, border, border);
        if (nT && nL && !hasCell(cell.cx - 1, cell.cy - 1)) ctx.fillRect(px - border, py - border, border, border);
        if (nB && nR && !hasCell(cell.cx + 1, cell.cy + 1)) ctx.fillRect(px + 25, py + 25, border, border);
        if (nB && nL && !hasCell(cell.cx - 1, cell.cy + 1)) ctx.fillRect(px - border, py + 25, border, border);
    }

    ctx.fillStyle = '#2a2420';
    for (const cell of cache.occupiedCells) {
        ctx.fillRect(cell.cx * 25, cell.cy * 25, 25, 25);
    }

    const palette = ['#7a6e63', '#6e6358', '#736860', '#80756a', '#6b6055', '#78706b', '#847a6f', '#716659'];
    for (let paletteIndex = 0; paletteIndex < palette.length; paletteIndex++) {
        ctx.fillStyle = palette[paletteIndex];
        for (const brick of cache.bricks) {
            if (brickHash(brick.bidCol, brick.bidRow) % palette.length !== paletteIndex) {
                continue;
            }

            const bx = brick.minCx * 25 + 1;
            const by = brick.cy * 25 + 1;
            const bw = (brick.maxCx - brick.minCx + 1) * 25 - 2;
            const bh = 23;
            ctx.fillRect(bx, by, bw, bh);
        }
    }

    ctx.fillStyle = 'rgba(255,255,255,0.13)';
    for (const brick of cache.bricks) {
        const bx = brick.minCx * 25 + 1;
        const by = brick.cy * 25 + 1;
        const bw = (brick.maxCx - brick.minCx + 1) * 25 - 2;
        ctx.fillRect(bx, by, bw, 2);
    }

    ctx.fillStyle = 'rgba(255,255,255,0.08)';
    for (const brick of cache.bricks) {
        const bx = brick.minCx * 25 + 1;
        const by = brick.cy * 25 + 1;
        ctx.fillRect(bx, by, 2, 23);
    }

    ctx.fillStyle = 'rgba(0,0,0,0.18)';
    for (const brick of cache.bricks) {
        const bx = brick.minCx * 25 + 1;
        const by = brick.cy * 25 + 1;
        const bw = (brick.maxCx - brick.minCx + 1) * 25 - 2;
        ctx.fillRect(bx, by + 21, bw, 2);
    }

    ctx.fillStyle = 'rgba(0,0,0,0.12)';
    for (const brick of cache.bricks) {
        const bx = brick.minCx * 25 + 1;
        const by = brick.cy * 25 + 1;
        const bw = (brick.maxCx - brick.minCx + 1) * 25 - 2;
        ctx.fillRect(bx + bw - 2, by, 2, 23);
    }
}

/** Draws active powerups. */
function drawPowerups(ctx, powerups, highQuality, timeSeconds, fxState) {
    const now = performance.now();
    const activePowerupIds = new Set();

    for (const powerup of powerups) {
        if (!powerup || powerup.died || !powerup.loc) continue;

        activePowerupIds.add(powerup.power);
        if (!fxState.powerupFirstSeenAt.has(powerup.power)) {
            fxState.powerupFirstSeenAt.set(powerup.power, now);
        }

        const ageSeconds = (now - fxState.powerupFirstSeenAt.get(powerup.power)) / 1000;
        const popScale = computePowerupPopScale(ageSeconds);

        const pulse = highQuality ? Math.abs(Math.sin(timeSeconds * 5)) * 3 : 0;
        const x = powerup.loc.X;
        const y = powerup.loc.Y;

        ctx.fillStyle = 'gold';
        ctx.beginPath();
        ctx.arc(x, y, (8 + pulse) * popScale, 0, Math.PI * 2);
        ctx.fill();

        if (highQuality) {
            ctx.fillStyle = 'yellow';
            ctx.beginPath();
            ctx.arc(x, y, 4 * Math.max(0.35, popScale), 0, Math.PI * 2);
            ctx.fill();
        }
    }

    for (const cachedPowerupId of Array.from(fxState.powerupFirstSeenAt.keys())) {
        if (!activePowerupIds.has(cachedPowerupId)) {
            fxState.powerupFirstSeenAt.delete(cachedPowerupId);
        }
    }
}

/** Draws all visible snakes and returns the current player's head. */
function drawSnakes(ctx, snakes, playerId, playerSkinIndex, showDeath, fxState) {
    let playerHead = null;

    for (const snake of snakes) {
        if (!snake || snake.dc === true || snake.alive !== true) continue;
        if (fxState.deathAnims.has(snake.snake)) continue;
        if (snake.snake === playerId && showDeath) continue;
        if (!Array.isArray(snake.body) || snake.body.length < 2) continue;

        const skin = resolveSnakeSkin(snake, playerId, playerSkinIndex);
        drawSnakeBody(ctx, snake.body, skin);
        drawNameplate(ctx, snake);

        if (snake.snake === playerId) {
            const head = snake.body[snake.body.length - 1];
            playerHead = { x: head.X, y: head.Y };
        }
    }

    return playerHead;
}

/** Resolves a snake's appearance using the local player's selected skin when needed. */
function resolveSnakeSkin(snake, playerId, playerSkinIndex) {
    const skinIndex = snake.snake === playerId ? playerSkinIndex : snake.skin;
    return resolveSkin(skinIndex);
}

/** Resolves a skin from the catalog or falls back to the default skin. */
function resolveSkin(index) {
    if (Number.isInteger(index) && index >= 0 && index < skinCatalog.length) {
        return skinCatalog[index];
    }
    return skinCatalog[0] || DEFAULT_SKIN;
}

/** Draws a complete snake with outline, fill, pattern, highlight, and head. */
function drawSnakeBody(ctx, body, skin) {
    if (!Array.isArray(body) || body.length < 2) return;

    const outlineColor = skin.outlineColor || 'rgba(0,0,0,0.35)';

    ctx.save();

    ctx.strokeStyle = outlineColor;
    ctx.lineWidth = SNAKE_WIDTH + 4;
    ctx.lineCap = 'round';
    ctx.lineJoin = 'round';
    traceBodyPath(ctx, body);
    ctx.stroke();

    ctx.strokeStyle = skin.bodyColor;
    ctx.lineWidth = SNAKE_WIDTH;
    traceBodyPath(ctx, body);
    ctx.stroke();

    if (skin.bodyAccent && skin.pattern !== BODY_PATTERN.SOLID) {
        if (skin.pattern === BODY_PATTERN.STRIPE) {
            drawStripePattern(ctx, body, skin);
        }
        else {
            drawPerpendicularPattern(ctx, body, skin);
        }
    }

    if (skin.bellyColor) {
        ctx.globalAlpha = 0.35;
        ctx.strokeStyle = skin.bellyColor;
        ctx.lineWidth = 3;
        traceBodyPath(ctx, body);
        ctx.stroke();
        ctx.globalAlpha = 1;
    }

    ctx.strokeStyle = 'rgba(255,255,255,0.12)';
    ctx.lineWidth = 2;
    traceBodyPath(ctx, body);
    ctx.stroke();

    drawHead(ctx, body, skin);
    ctx.restore();
}

/** Traces the snake body path. */
function traceBodyPath(ctx, body) {
    ctx.beginPath();
    ctx.moveTo(body[0].X, body[0].Y);
    for (let i = 1; i < body.length; i++) {
        ctx.lineTo(body[i].X, body[i].Y);
    }
}

/** Draws the head and eyes. */
function drawHead(ctx, body, skin) {
    const head = body[body.length - 1];
    const neck = body[body.length - 2];
    const headAngle = Math.atan2(head.Y - neck.Y, head.X - neck.X);
    const headRadius = SNAKE_WIDTH * 0.7;

    ctx.save();
    ctx.translate(head.X, head.Y);
    ctx.rotate(headAngle);

    ctx.fillStyle = skin.headColor;
    ctx.beginPath();
    ctx.arc(1, 0, headRadius, 0, Math.PI * 2);
    ctx.fill();

    const eyeOffset = headRadius * 0.45;
    const eyeRadius = headRadius * 0.42;
    const pupilRadius = headRadius * 0.22;
    const eyeForward = headRadius * 0.35;

    ctx.fillStyle = skin.eyeColor;
    ctx.beginPath();
    ctx.arc(eyeForward, -eyeOffset, eyeRadius, 0, Math.PI * 2);
    ctx.arc(eyeForward, eyeOffset, eyeRadius, 0, Math.PI * 2);
    ctx.fill();

    ctx.fillStyle = skin.pupilColor;
    ctx.beginPath();
    ctx.arc(eyeForward + pupilRadius * 0.3, -eyeOffset, pupilRadius, 0, Math.PI * 2);
    ctx.arc(eyeForward + pupilRadius * 0.3, eyeOffset, pupilRadius, 0, Math.PI * 2);
    ctx.fill();

    ctx.restore();
}

/** Draws alternating stripe bands along the body. */
function drawStripePattern(ctx, body, skin) {
    const band = 10;
    ctx.save();
    ctx.lineWidth = SNAKE_WIDTH;
    ctx.lineCap = 'butt';
    ctx.lineJoin = 'round';

    ctx.strokeStyle = skin.bodyAccent;
    ctx.setLineDash(skin.bodyAccent2 ? [band, 2 * band] : [band, band]);
    ctx.lineDashOffset = skin.bodyAccent2 ? 2 * band : band;
    traceBodyPath(ctx, body);
    ctx.stroke();

    if (skin.bodyAccent2) {
        ctx.strokeStyle = skin.bodyAccent2;
        ctx.setLineDash([band, 2 * band]);
        ctx.lineDashOffset = band;
        traceBodyPath(ctx, body);
        ctx.stroke();
    }

    ctx.setLineDash([]);
    ctx.restore();
}

/** Draws checker, diamond, or wave marks perpendicular to the body path. */
function drawPerpendicularPattern(ctx, body, skin) {
    const segCount = body.length - 1;
    const segNormals = new Array(segCount);
    for (let seg = 0; seg < segCount; seg++) {
        const dx = body[seg + 1].X - body[seg].X;
        const dy = body[seg + 1].Y - body[seg].Y;
        const len = Math.hypot(dx, dy);
        segNormals[seg] = len < 0.001 ? { nx: 0, ny: 1 } : { nx: -dy / len, ny: dx / len };
    }

    const vtxNormals = new Array(body.length);
    vtxNormals[0] = segCount > 0 ? segNormals[0] : { nx: 0, ny: 1 };
    vtxNormals[body.length - 1] = segCount > 0 ? segNormals[segCount - 1] : { nx: 0, ny: 1 };
    for (let vertex = 1; vertex < body.length - 1; vertex++) {
        let bx = segNormals[vertex - 1].nx + segNormals[vertex].nx;
        let by = segNormals[vertex - 1].ny + segNormals[vertex].ny;
        const blendLength = Math.hypot(bx, by);
        if (blendLength > 0.001) {
            bx /= blendLength;
            by /= blendLength;
        }
        else {
            bx = segNormals[vertex].nx;
            by = segNormals[vertex].ny;
        }
        vtxNormals[vertex] = { nx: bx, ny: by };
    }

    const spacing = skin.pattern === BODY_PATTERN.CHECKER ? 12
        : skin.pattern === BODY_PATTERN.DIAMOND ? 18
            : 10;

    const blendDistance = SNAKE_WIDTH * 1.5;
    let accumulated = 0;
    let markIndex = 0;
    for (let i = 1; i < body.length; i++) {
        const seg = i - 1;
        const sdx = body[i].X - body[i - 1].X;
        const sdy = body[i].Y - body[i - 1].Y;
        const segLen = Math.hypot(sdx, sdy);
        if (segLen < 0.001) continue;

        const dirX = sdx / segLen;
        const dirY = sdy / segLen;
        const segmentNormal = segNormals[seg];
        const bisStart = vtxNormals[seg];
        const bisEnd = vtxNormals[seg + 1];

        let walked = 0;
        while (walked + (spacing - accumulated) <= segLen) {
            walked += spacing - accumulated;
            accumulated = 0;

            const px = body[i - 1].X + sdx * (walked / segLen);
            const py = body[i - 1].Y + sdy * (walked / segLen);

            const distFromStart = walked;
            const distFromEnd = segLen - walked;

            let nx;
            let ny;
            if (distFromStart < blendDistance && seg > 0) {
                let t = distFromStart / blendDistance;
                t = t * t * (3 - 2 * t);
                nx = bisStart.nx + (segmentNormal.nx - bisStart.nx) * t;
                ny = bisStart.ny + (segmentNormal.ny - bisStart.ny) * t;
            }
            else if (distFromEnd < blendDistance && seg < segCount - 1) {
                let t = distFromEnd / blendDistance;
                t = t * t * (3 - 2 * t);
                nx = bisEnd.nx + (segmentNormal.nx - bisEnd.nx) * t;
                ny = bisEnd.ny + (segmentNormal.ny - bisEnd.ny) * t;
            }
            else {
                nx = segmentNormal.nx;
                ny = segmentNormal.ny;
            }

            const normalLength = Math.hypot(nx, ny);
            if (normalLength > 0.001) {
                nx /= normalLength;
                ny /= normalLength;
            }

            drawPatternMark(ctx, skin, px, py, nx, ny, dirX, dirY, markIndex);
            markIndex++;
        }

        accumulated += segLen - walked;
    }
}

/** Draws a single checker, diamond, or wave pattern mark. */
function drawPatternMark(ctx, skin, px, py, nx, ny, dx, dy, index) {
    const halfWidth = SNAKE_WIDTH / 2;

    if (skin.pattern === BODY_PATTERN.CHECKER) {
        const color = skin.bodyAccent2 && index % 2 === 1 ? skin.bodyAccent2 : skin.bodyAccent;
        const side = index % 2 === 0 ? 1 : -1;
        ctx.fillStyle = color;
        ctx.beginPath();
        ctx.arc(px + nx * halfWidth * 0.4 * side, py + ny * halfWidth * 0.4 * side, 3.5, 0, Math.PI * 2);
        ctx.fill();
        return;
    }

    if (skin.pattern === BODY_PATTERN.DIAMOND) {
        const color = skin.bodyAccent2 && index % 2 === 1 ? skin.bodyAccent2 : skin.bodyAccent;
        const diamondSize = halfWidth * 0.7;
        ctx.fillStyle = color;
        ctx.beginPath();
        ctx.moveTo(px + dx * diamondSize, py + dy * diamondSize);
        ctx.lineTo(px + nx * diamondSize, py + ny * diamondSize);
        ctx.lineTo(px - dx * diamondSize, py - dy * diamondSize);
        ctx.lineTo(px - nx * diamondSize, py - ny * diamondSize);
        ctx.closePath();
        ctx.fill();
        return;
    }

    if (skin.pattern === BODY_PATTERN.WAVE) {
        const color = skin.bodyAccent2 && index % 2 === 1 ? skin.bodyAccent2 : skin.bodyAccent;
        const waveSide = Math.sin(index * 1.2) * halfWidth * 0.5;
        ctx.save();
        ctx.globalAlpha = 0.8;
        ctx.fillStyle = color;
        ctx.beginPath();
        ctx.arc(px + nx * waveSide, py + ny * waveSide, 3, 0, Math.PI * 2);
        ctx.fill();
        ctx.restore();
    }
}

/** Draws active death animations and spawns them when snakes die. */
function drawDeathAnimations(ctx, snakes, playerId, playerSkinIndex, fxState, dt) {
    for (const snake of snakes) {
        const snakeId = snake?.snake;
        if (snakeId == null) continue;

        if ((snake.died === true || snake.alive !== true) && Array.isArray(snake.body) && snake.body.length >= 2 && snake.dc !== true) {
            // Only spawn the animation once per death — don't restart when the snake stays dead
            if (!fxState.deathAnims.has(snakeId) && !fxState.completedDeathAnims.has(snakeId)) {
                fxState.deathAnims.set(snakeId, {
                    elapsedSeconds: 0,
                    path: snake.body.map((point) => ({ X: point.X, Y: point.Y })),
                    skin: resolveSnakeSkin(snake, playerId, playerSkinIndex),
                    isFinished: false
                });
            }
        }
        else if (snake.alive === true) {
            // Snake respawned — clear both the active anim and the completed guard so it can die again
            fxState.deathAnims.delete(snakeId);
            fxState.completedDeathAnims.delete(snakeId);
        }
    }

    for (const [snakeId, anim] of Array.from(fxState.deathAnims.entries())) {
        if (anim.isFinished || !Array.isArray(anim.path) || anim.path.length < 2) {
            fxState.deathAnims.delete(snakeId);
            continue;
        }

        const lengths = [];
        let totalLength = 0;
        for (let i = 0; i < anim.path.length - 1; i++) {
            const dx = anim.path[i + 1].X - anim.path[i].X;
            const dy = anim.path[i + 1].Y - anim.path[i].Y;
            const segmentLength = Math.hypot(dx, dy);
            lengths.push(segmentLength);
            totalLength += segmentLength;
        }

        const explosionSpeed = BIT_DISTANCE / EXPLOSION_DELAY;
        const explodedLength = anim.elapsedSeconds * explosionSpeed;
        let currentDistance = 0;
        const remainingPoints = [];

        for (let i = 0; i < anim.path.length - 1; i++) {
            const segmentLength = lengths[i];
            const segmentDistanceEnd = totalLength - currentDistance;
            if (segmentDistanceEnd > explodedLength) {
                if (remainingPoints.length === 0) {
                    remainingPoints.push(anim.path[i]);
                }

                if (segmentDistanceEnd - segmentLength > explodedLength) {
                    remainingPoints.push(anim.path[i + 1]);
                }
                else {
                    const t = segmentLength > 0.001 ? (segmentDistanceEnd - explodedLength) / segmentLength : 0;
                    remainingPoints.push({
                        X: anim.path[i].X + (anim.path[i + 1].X - anim.path[i].X) * t,
                        Y: anim.path[i].Y + (anim.path[i + 1].Y - anim.path[i].Y) * t
                    });
                    break;
                }
            }
            currentDistance += segmentLength;
        }

        if (remainingPoints.length >= 2) {
            drawSnakeBody(ctx, remainingPoints, anim.skin);
        }

        currentDistance = 0;
        let headParticleDone = false;
        for (let i = anim.path.length - 1; i > 0; i--) {
            const segmentLength = lengths[i - 1];
            const steps = Math.max(1, Math.ceil(segmentLength / BIT_DISTANCE));
            for (let step = 0; step <= steps; step++) {
                const t = step / steps;
                const pointDistance = currentDistance + segmentLength * t;
                const timeSinceExplosion = anim.elapsedSeconds - pointDistance / explosionSpeed;
                if (timeSinceExplosion < 0 || timeSinceExplosion > PARTICLE_LIFESPAN) continue;

                const bx = anim.path[i].X + (anim.path[i - 1].X - anim.path[i].X) * t;
                const by = anim.path[i].Y + (anim.path[i - 1].Y - anim.path[i].Y) * t;
                const alpha = Math.max(0, 1 - timeSinceExplosion / PARTICLE_LIFESPAN);
                const isHead = !headParticleDone && pointDistance < 1;
                if (isHead) headParticleDone = true;

                const baseRadius = isHead ? 10 : 3;
                const expandFactor = isHead ? 25 : 12;
                const radius = baseRadius + (timeSinceExplosion / PARTICLE_LIFESPAN) * expandFactor;

                ctx.save();
                ctx.globalAlpha = alpha;
                ctx.fillStyle = anim.skin.deathColor;
                ctx.beginPath();
                ctx.arc(bx, by, radius, 0, Math.PI * 2);
                ctx.fill();
                ctx.restore();
            }
            currentDistance += segmentLength;
        }

        anim.elapsedSeconds += dt;
        if (anim.elapsedSeconds > totalLength / explosionSpeed + 1) {
            anim.isFinished = true;
            fxState.deathAnims.delete(snakeId);
            fxState.completedDeathAnims.add(snakeId);
        }
    }
}

/** Computes the cached high-quality wall layout. */
function getWallCache(walls, fxState) {
    const wallKey = walls
        .filter((wall) => wall?.p1 && wall?.p2)
        .map((wall) => `${wall.wall}:${wall.p1.X},${wall.p1.Y}:${wall.p2.X},${wall.p2.Y}`)
        .join('|');

    if (fxState.wallCache && fxState.wallCacheKey === wallKey) {
        return fxState.wallCache;
    }

    const occupiedSet = new Set();
    const occupiedCells = [];
    for (const wall of walls) {
        const rect = wallRect(wall);
        if (!rect) continue;
        const cols = Math.round(rect.w / 25);
        const rows = Math.round(rect.h / 25);
        const startCx = Math.round(rect.x / 25);
        const startCy = Math.round(rect.y / 25);
        for (let row = 0; row < rows; row++) {
            for (let col = 0; col < cols; col++) {
                const cx = startCx + col;
                const cy = startCy + row;
                const key = `${cx},${cy}`;
                if (!occupiedSet.has(key)) {
                    occupiedSet.add(key);
                    occupiedCells.push({ cx, cy });
                }
            }
        }
    }

    const bricks = new Map();
    for (const cell of occupiedCells) {
        const brick = brickId(cell.cx, cell.cy);
        const key = `${brick.col},${brick.row}`;
        if (bricks.has(key)) {
            const existing = bricks.get(key);
            existing.minCx = Math.min(existing.minCx, cell.cx);
            existing.maxCx = Math.max(existing.maxCx, cell.cx);
        }
        else {
            bricks.set(key, { bidCol: brick.col, bidRow: brick.row, minCx: cell.cx, maxCx: cell.cx, cy: cell.cy });
        }
    }

    fxState.wallCacheKey = wallKey;
    fxState.wallCache = {
        occupiedSet,
        occupiedCells,
        bricks: Array.from(bricks.values())
    };
    return fxState.wallCache;
}

/** Computes the pop-in scale for a powerup. */
function computePowerupPopScale(ageSeconds) {
    if (ageSeconds <= 0) return 0.1;
    if (ageSeconds >= POWERUP_POP_DURATION_SECONDS) return 1;

    const t = ageSeconds / POWERUP_POP_DURATION_SECONDS;
    const easeOut = 1 - Math.pow(1 - t, 4);
    const bounce = Math.sin(t * Math.PI * 1.25) * POWERUP_POP_BOUNCE_AMPLITUDE * (1 - t * 0.6);
    return Math.max(0.1, easeOut + bounce);
}

/** Converts a wall cell into a brick-group identifier. */
function brickId(cx, cy) {
    return {
        col: mod(cy, 2) === 0 ? Math.floor(cx / 2) : Math.floor((cx - 1) / 2),
        row: cy
    };
}

/** Hash used to vary wall brick colors deterministically. */
function brickHash(a, b) {
    let h = a * 374761393 + b * 668265263;
    h = (h ^ (h >> 13)) * 1274126177;
    return (h ^ (h >> 16)) & 0x7fffffff;
}

/** Mathematical modulo that behaves for negative coordinates. */
function mod(a, b) {
    return ((a % b) + b) % b;
}

/** Draws a floating name and score pill above the head. */
function drawNameplate(ctx, snake) {
    if (!snake.name || !Array.isArray(snake.body) || snake.body.length < 2) return;

    const head = snake.body[snake.body.length - 1];
    const fullText = `${snake.name}  ${snake.score ?? 0}`;

    ctx.save();
    ctx.font = "700 11px 'Avenir Next', 'Segoe UI', sans-serif";
    const textWidth = ctx.measureText(fullText).width;
    const padX = 8;
    const pillH = 18;
    const pillW = textWidth + padX * 2;
    const radius = pillH / 2;
    const pillX = head.X - pillW / 2;
    const pillY = head.Y - (SNAKE_WIDTH / 2 + 2) - pillH - 6;

    ctx.fillStyle = 'rgba(0,0,0,0.3)';
    roundRect(ctx, pillX + 1, pillY + 1, pillW, pillH, radius);
    ctx.fill();

    ctx.fillStyle = 'rgba(0,0,0,0.65)';
    roundRect(ctx, pillX, pillY, pillW, pillH, radius);
    ctx.fill();

    ctx.strokeStyle = 'rgba(255,255,255,0.12)';
    ctx.lineWidth = 1;
    roundRect(ctx, pillX, pillY, pillW, pillH, radius);
    ctx.stroke();

    ctx.fillStyle = 'rgba(255,255,255,0.95)';
    ctx.textAlign = 'center';
    ctx.fillText(fullText, head.X, pillY + pillH / 2 + 4);
    ctx.restore();
}

/** Draws a rounded rectangle path. */
function roundRect(ctx, x, y, w, h, r) {
    ctx.beginPath();
    ctx.moveTo(x + r, y);
    ctx.lineTo(x + w - r, y);
    ctx.arc(x + w - r, y + r, r, -Math.PI / 2, 0);
    ctx.lineTo(x + w, y + h - r);
    ctx.arc(x + w - r, y + h - r, r, 0, Math.PI / 2);
    ctx.lineTo(x + r, y + h);
    ctx.arc(x + r, y + h - r, r, Math.PI / 2, Math.PI);
    ctx.lineTo(x, y + r);
    ctx.arc(x + r, y + r, r, Math.PI, Math.PI * 1.5);
}

/** Computes a wall's axis-aligned rectangle. */
function wallRect(wall) {
    if (!wall?.p1 || !wall?.p2) return null;
    let x = Math.min(wall.p1.X, wall.p2.X) - WALL_HALF_WIDTH;
    let y = Math.min(wall.p1.Y, wall.p2.Y) - WALL_HALF_WIDTH;
    let w = Math.abs(wall.p1.X - wall.p2.X);
    let h = Math.abs(wall.p1.Y - wall.p2.Y);
    if (w === 0) {
        w = WALL_THICKNESS;
        h += WALL_THICKNESS;
    }
    else {
        h = WALL_THICKNESS;
        w += WALL_THICKNESS;
    }
    return { x, y, w, h };
}

/** Returns current scoreboard screen bounds, with legacy fallbacks when not mounted yet. */
function getScoreboardBounds(isSplitScreen, viewWidth) {
    if (!_ditherEl || !_ditherEl.isConnected) {
        _ditherEl = document.getElementById('scoreboardOverlay');
    }

    if (_ditherEl) {
        const rect = _ditherEl.getBoundingClientRect();
        if (rect.width > 0 && rect.height > 0) {
            return {
                left: rect.left,
                top: rect.top,
                right: rect.right,
                bottom: rect.bottom,
                midX: rect.left + rect.width / 2
            };
        }
    }

    if (isSplitScreen) {
        const centerX = viewWidth / 2;
        const sbW = 212;
        const sbH = 180;
        const sbLeft = centerX - sbW / 2;
        const sbTop = 36;
        const sbRight = centerX + sbW / 2;
        const sbBottom = sbTop + sbH;
        return {
            left: sbLeft,
            top: sbTop,
            right: sbRight,
            bottom: sbBottom,
            midX: centerX
        };
    }

    return {
        left: 12,
        top: 12,
        right: 224,
        bottom: 192,
        midX: (12 + 224) / 2
    };
}

/** Computes a single-player scoreboard dither radius. */
function computeDitherForHead(head, sbLeft, sbTop, sbRight, sbBottom) {
    if (!head) return -1;

    const fadeDistance = 80;
    const nearX = clamp(head.x, sbLeft, sbRight);
    const nearY = clamp(head.y, sbTop, sbBottom);
    const dx = head.x - nearX;
    const dy = head.y - nearY;
    const dist = Math.hypot(dx, dy);

    if (dist >= fadeDistance) return -1;

    const tLinear = 1 - dist / fadeDistance;
    const t = tLinear * tLinear;
    const ditherHalf = 2.5;
    const ditherMin = ditherHalf * 0.4;
    const ditherMax = ditherHalf * 2;
    return ditherMax - t * (ditherMax - ditherMin);
}

/** Computes split-screen scoreboard dither radii. */
function computeSplitDitherRadii(p1Head, p2Head, p1Dead, p2Dead, scoreboardBounds) {
    const sbLeft = scoreboardBounds.left;
    const sbTop = scoreboardBounds.top;
    const sbRight = scoreboardBounds.right;
    const sbBottom = scoreboardBounds.bottom;
    const centerX = scoreboardBounds.midX;

    return {
        leftR: p1Dead ? -1 : computeDitherForHead(p1Head, sbLeft, sbTop, centerX, sbBottom),
        rightR: p2Dead ? -1 : computeDitherForHead(p2Head, centerX, sbTop, sbRight, sbBottom)
    };
}

/** Clamps a value between a minimum and maximum bound. */
function clamp(value, min, max) {
    return Math.min(Math.max(value, min), max);
}

/**
 * Global keydown listener (attached once). Routes keyboard events to C#:
 * - Shift keys always forwarded (respawn/connect shortcuts).
 * - Text input fields swallow most keys except Tab/Escape/Enter.
 * - Game keys (WASD, arrows, menu keys) are forwarded and have
 *   their browser defaults prevented.
 */
if (!window.snakeKeyHandlerAttached) {
    document.addEventListener('keydown', function (event) {
        if (!window.theInstance) return;

        var activeTag = document.activeElement ? document.activeElement.tagName : '';
        var activeType = document.activeElement ? (document.activeElement.type || '').toLowerCase() : '';
        var isInTextInput = (activeTag === 'INPUT' && activeType !== 'checkbox' && activeType !== 'radio' && activeType !== 'button')
            || activeTag === 'TEXTAREA' || activeTag === 'SELECT';

        // Always forward Shift signals (for respawn/connect) — blur any focused input first
        if (event.key === "Shift") {
            if (isInTextInput && document.activeElement) document.activeElement.blur();
            var side = event.code === "ShiftRight" ? "ShiftRight" : "ShiftLeft";
            window.theInstance.invokeMethodAsync('HandleKeyPress', side);
            return;
        }

        // When typing in a text input, don't forward other keys to the game
        if (isInTextInput) {
            if (event.key === 'Tab' || event.key === 'Escape' || event.key === 'Enter') {
                event.preventDefault();
                document.activeElement.blur();
                if (event.key === 'Enter') {
                    window.theInstance.invokeMethodAsync('HandleKeyPress', 'Enter');
                }
            }
            return;
        }

        // Prevent defaults on game keys and menu keys
        var preventKeys = ["w", "a", "s", "d", "ArrowUp", "ArrowLeft", "ArrowDown", "ArrowRight", "Tab", "Delete", "Backspace", "Escape", "Enter"];
        if (preventKeys.includes(event.key)) {
            event.preventDefault();
        }

        window.theInstance.invokeMethodAsync('HandleKeyPress', event.key);
    });
    window.snakeKeyHandlerAttached = true;
}

/**
 * Programmatically focuses a sidebar input element by ID.
 * Enables the element first (it may be disabled while playing).
 * @param {string} id - The DOM element ID to focus.
 */
window.focusSidebarInput = (id) => {
    const el = document.getElementById(id);
    if (el) { el.disabled = false; el.focus(); el.select(); }
};
