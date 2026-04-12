let instance = null;
let canvasEl = null;
let _ditherEl = null;
let _lastDitherR = -999;

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

window.initRenderJS = (dotnetRef) => {
    instance = dotnetRef;
    window.theInstance = dotnetRef;

    ensureCanvas();
    resizeCanvas();
    window.addEventListener('resize', resizeCanvas);
};

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

function resizeCanvas() {
    ensureCanvas();
    if (!canvasEl) return;
    canvasEl.width = window.innerWidth;
    canvasEl.height = window.innerHeight;
    if (instance) {
        instance.invokeMethodAsync('UpdateViewportSize', [window.innerWidth, window.innerHeight]);
    }
}

function ensureCanvas() {
    if (!canvasEl) {
        canvasEl = document.querySelector('#snakeCanvas canvas');
    }
}

if (!window.snakeKeyHandlerAttached) {
    document.addEventListener('keydown', function (event) {
        if (window.theInstance) {
            var cmdKeys = ["w", "a", "s", "d", "ArrowUp", "ArrowLeft", "ArrowDown", "ArrowRight"];
            if (cmdKeys.includes(event.key)) {
                event.preventDefault();
            }
            if (event.key === "Shift") {
                var side = event.code === "ShiftRight" ? "ShiftRight" : "ShiftLeft";
                window.theInstance.invokeMethodAsync('HandleKeyPress', side);
                return;
            }
            window.theInstance.invokeMethodAsync('HandleKeyPress', event.key);
        }
    });
    window.snakeKeyHandlerAttached = true;
}
