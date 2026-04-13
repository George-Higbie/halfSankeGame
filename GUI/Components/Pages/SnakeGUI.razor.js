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

/** @type {number} Last dither radius sent to the DOM (avoids redundant writes). */
let _lastDitherR = -999;

/**
 * Updates the scoreboard overlay's CSS mask to produce a star-shaped dither
 * effect that fades the scoreboard when the player's head is nearby.
 * Called from C# every frame via JSInterop.
 * @param {number} r - Star radius in pixels. Negative = no dithering (fully visible).
 */
window.updateScoreboardDither = (r) => {
    if (!_ditherEl) _ditherEl = document.getElementById('scoreboardOverlay');
    if (!_ditherEl) return;

    // Avoid redundant DOM writes — only update if radius changed meaningfully
    if (Math.abs(r - _lastDitherR) < 0.01) return;
    _lastDitherR = r;

    if (r < 0) {
        // No dithering — clear mask, fully visible
        _ditherEl.style.maskImage = '';
        _ditherEl.style.webkitMaskImage = '';
        return;
    }

    const cell = 5, half = 2.5;
    const pull = r * 0.35;
    const py = half - r, by = half + r, rx = half + r, lx = half - r;

    const d = `M ${half.toFixed(2)} ${py.toFixed(2)} ` +
        `C ${(half + pull).toFixed(2)} ${(py + pull).toFixed(2)}, ${(rx - pull).toFixed(2)} ${(half - pull).toFixed(2)}, ${rx.toFixed(2)} ${half.toFixed(2)} ` +
        `C ${(rx - pull).toFixed(2)} ${(half + pull).toFixed(2)}, ${(half + pull).toFixed(2)} ${(by - pull).toFixed(2)}, ${half.toFixed(2)} ${by.toFixed(2)} ` +
        `C ${(half - pull).toFixed(2)} ${(by - pull).toFixed(2)}, ${(lx + pull).toFixed(2)} ${(half + pull).toFixed(2)}, ${lx.toFixed(2)} ${half.toFixed(2)} ` +
        `C ${(lx + pull).toFixed(2)} ${(half - pull).toFixed(2)}, ${(half - pull).toFixed(2)} ${(py + pull).toFixed(2)}, ${half.toFixed(2)} ${py.toFixed(2)} Z`;

    const svg = `<svg xmlns='http://www.w3.org/2000/svg' width='${cell}' height='${cell}'><path d='${d}' fill='white'/></svg>`;
    const url = `url("data:image/svg+xml,${encodeURIComponent(svg)}")`;

    _ditherEl.style.maskImage = url;
    _ditherEl.style.webkitMaskImage = url;
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
