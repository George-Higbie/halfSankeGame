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
 */
window.updateScoreboardDither = (leftR, rightR) => {
    if (!_ditherEl) _ditherEl = document.getElementById('scoreboardOverlay');
    if (!_ditherEl) return;

    // Avoid redundant DOM writes
    if (Math.abs(leftR - _lastDitherL) < 0.01 && Math.abs(rightR - _lastDitherR) < 0.01) return;
    _lastDitherL = leftR;
    _lastDitherR = rightR;

    const isSplit = rightR >= -0.5;  // rightR === -1 means single-player mode

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
    if (instance) {
        instance.invokeMethodAsync('UpdateViewportSize', [window.innerWidth, window.innerHeight]);
    }
}

/**
 * Lazily queries the DOM for the game canvas if it hasn't been found yet.
 */
function ensureCanvas() {
    if (!canvasEl) {
        canvasEl = document.querySelector('#snakeCanvas canvas');
    }
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
