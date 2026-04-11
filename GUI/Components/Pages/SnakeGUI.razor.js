let instance = null;
let canvasEl = null;

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
