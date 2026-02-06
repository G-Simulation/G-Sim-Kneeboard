(function () {
    'use strict';

    // Zentraler Logger - nutzt KneeboardLogger falls verfügbar
    var notepadLogger = (typeof KneeboardLogger !== 'undefined')
        ? KneeboardLogger.createLogger('Notepad')
        : { info: function(){}, warn: console.warn.bind(console), error: console.error.bind(console) };

    const DRAWING_STORAGE_KEY = 'notepad:drawing';

    const MIN_SURFACE_RENDER_SIZE = 32;
    let surface;
    let svgRoot;
    let surfaceWidth = 0;
    let surfaceHeight = 0;
    let surfaceMonitor = null;
    let drawing = [];
    let currentStroke = null;
    let isDrawing = false;
    let currentColor = '#000000';
    let currentSize = 5;
    let colorMode = 'A';
    let theme = {
        light: '#000000',
        dark: '#000000',
        fontLight: '#ffffff',
        fontDark: '#000000'
    };

    let colorButtons = {
        A: null,
        B: null,
        C: null,
        D: null
    };
    let sizeInput = null;
    let resetButton = null;
    let resizeHandler = null;
    let visibilityHandler = null;
    let scheduledRedraw = null;
    let currentStrokeElement = null;
    const requestFrame = (typeof window !== 'undefined' && window.requestAnimationFrame) || function (callback) {
        return setTimeout(callback, 16);
    };
    const cancelFrame = (typeof window !== 'undefined' && window.cancelAnimationFrame) || clearTimeout;
    const SVG_NS = 'http://www.w3.org/2000/svg';

    // Maximum distance between points before interpolation kicks in
    // Lower value = smoother curves but more points (important for Kneeboard where events are sparse)
    const MAX_POINT_DISTANCE = 3;
    // Minimum distance to add a new point (avoid duplicates)
    const MIN_POINT_DISTANCE = 0.5;

    // Chaikin corner-cutting algorithm for post-stroke smoothing
    // This smooths angular paths caused by sparse pointer events in Kneeboard browsers
    function chaikinSmooth(points, iterations) {
        if (!points || points.length < 3) return points;

        let result = points.slice();

        for (let iter = 0; iter < iterations; iter++) {
            const smoothed = [];
            // Keep first point
            smoothed.push(result[0]);

            for (let i = 0; i < result.length - 1; i++) {
                const p0 = result[i];
                const p1 = result[i + 1];

                // Q = 3/4 * P0 + 1/4 * P1
                const q = {
                    x: 0.75 * p0.x + 0.25 * p1.x,
                    y: 0.75 * p0.y + 0.25 * p1.y
                };

                // R = 1/4 * P0 + 3/4 * P1
                const r = {
                    x: 0.25 * p0.x + 0.75 * p1.x,
                    y: 0.25 * p0.y + 0.75 * p1.y
                };

                smoothed.push(q);
                smoothed.push(r);
            }

            // Keep last point
            smoothed.push(result[result.length - 1]);
            result = smoothed;
        }

        return result;
    }

    function hydrateThemeFromStorage() {
        const savedLight = localStorage.getItem('colorLight');
        const savedDark = localStorage.getItem('colorDark');
        const savedFontLight = localStorage.getItem('fontColorLight');
        const savedFontDark = localStorage.getItem('fontColorDark');

        if (savedLight && savedDark && savedFontLight && savedFontDark) {
            theme.light = savedLight;
            theme.dark = savedDark;
            theme.fontLight = savedFontLight;
            theme.fontDark = savedFontDark;

            document.documentElement.style.setProperty('--light', theme.light);
            document.documentElement.style.setProperty('--dark', theme.dark);
            document.documentElement.style.setProperty('--fontLight', theme.fontLight);
            document.documentElement.style.setProperty('--fontDark', theme.fontDark);
        }
    }

    function cleanupListeners() {
        if (resizeHandler) {
            window.removeEventListener('resize', resizeHandler);
            resizeHandler = null;
        }
        if (visibilityHandler) {
            document.removeEventListener('visibilitychange', visibilityHandler);
            visibilityHandler = null;
        }
        cancelScheduledRedraw();
        stopSurfaceMonitor();
    }

    function scheduleRedraw() {
        if (scheduledRedraw !== null) {
            return;
        }
        scheduledRedraw = requestFrame(() => {
            scheduledRedraw = null;
            redraw();
        });
    }

    function cancelScheduledRedraw() {
        if (scheduledRedraw === null) {
            return;
        }
        cancelFrame(scheduledRedraw);
        scheduledRedraw = null;
    }

    function initNotepadPage() {
        cleanupListeners();
        hydrateThemeFromStorage();

        colorButtons.A = document.getElementById('colorA');
        colorButtons.B = document.getElementById('colorB');
        colorButtons.C = document.getElementById('colorC');
        colorButtons.D = document.getElementById('colorD');
        sizeInput = document.getElementById('myRange');
        resetButton = document.getElementById('reset');

        bindColorButtons();
        bindSizeSlider();
        bindResetButton();

        surface = document.getElementById('note');
        if (!surface) {
            console.error('Notepad: drawing surface #note not found.');
            return;
        }

        surface.innerHTML = '';
        svgRoot = document.createElementNS(SVG_NS, 'svg');
        svgRoot.setAttribute('class', 'notepad-surface-svg');
        svgRoot.setAttribute('xmlns', SVG_NS);
        svgRoot.setAttribute('fill', 'none');
        svgRoot.setAttribute('stroke-linecap', 'round');
        svgRoot.setAttribute('stroke-linejoin', 'round');
        surface.appendChild(svgRoot);

        ensureSurfaceSize();
        bindSurfaceEvents();
        startSurfaceMonitor();

        setBrushColor('A');

        waitForSurfaceReady(() => {
            drawing = loadDrawing();
            redraw();
            scheduleSurfaceRefresh(200);
            scheduleSurfaceRefresh(800);
            scheduleSurfaceRefresh(1600);
            scheduleSurfaceRefresh(3200);
        });

        resizeHandler = () => {
            ensureSurfaceSize();
            redraw();
        };
        window.addEventListener('resize', resizeHandler);

        visibilityHandler = () => {
            if (!document.hidden) {
                ensureSurfaceSize();
                redraw();
            }
        };
        document.addEventListener('visibilitychange', visibilityHandler);
    }

    function bindColorButtons() {
        if (colorButtons.A) colorButtons.A.onclick = () => setBrushColor('A');
        if (colorButtons.B) colorButtons.B.onclick = () => setBrushColor('B');
        if (colorButtons.C) colorButtons.C.onclick = () => setBrushColor('C');
        if (colorButtons.D) colorButtons.D.onclick = () => setBrushColor('D');
    }

    function bindSizeSlider() {
        if (!sizeInput) return;
        const handle = () => {
            const parsed = Number(sizeInput.value);
            currentSize = Number.isFinite(parsed) && parsed > 0 ? parsed : 5;
        };
        sizeInput.addEventListener('input', handle);
        sizeInput.addEventListener('change', handle);
        handle();
    }

    function bindResetButton() {
        if (!resetButton) return;
        resetButton.addEventListener('click', () => {
            drawing = [];
            currentStroke = null;
            isDrawing = false;
            localStorage.removeItem(DRAWING_STORAGE_KEY);
            redraw();
        });
    }

    function setBrushColor(mode) {
        colorMode = mode;
        switch (mode) {
            case 'A':
                currentColor = theme.light || '#000000';
                break;
            case 'B':
                currentColor = '#000000';
                break;
            case 'C':
                currentColor = '#ff0000';
                break;
            case 'D':
                currentColor = '#ffffff';
                break;
            default:
                currentColor = '#000000';
        }

        Object.keys(colorButtons).forEach(key => {
            const button = colorButtons[key];
            if (button) {
                button.className = key === mode ? 'border' : 'noborder';
            }
        });
    }

    const POINTER_EVENTS_SUPPORTED = typeof window !== 'undefined' && 'PointerEvent' in window;

    function bindSurfaceEvents() {
        if (!surface) return;

        if (POINTER_EVENTS_SUPPORTED) {
            surface.addEventListener('pointerdown', handlePointerDown, { passive: false });
            surface.addEventListener('pointermove', handlePointerMove, { passive: false });
            surface.addEventListener('pointerup', handlePointerUp, { passive: false });
            surface.addEventListener('pointercancel', handlePointerUp, { passive: false });
            surface.addEventListener('pointerleave', handlePointerLeave, { passive: false });
            // Global listeners as fallback when pointer capture fails
            window.addEventListener('pointerup', handleGlobalPointerUp, { passive: false });
        } else {
            surface.addEventListener('mousedown', handleMouseDown, false);
            surface.addEventListener('mousemove', handleMouseMove, false);
            window.addEventListener('mouseup', handleMouseUp, false);

            surface.addEventListener('touchstart', handleTouchStart, { passive: false });
            surface.addEventListener('touchmove', handleTouchMove, { passive: false });
            surface.addEventListener('touchend', handleTouchEnd, false);
            surface.addEventListener('touchcancel', handleTouchEnd, false);
        }
    }

    function handlePointerDown(event) {
        if (event.pointerType === 'mouse' && event.button !== 0) return;
        event.preventDefault();
        if (surface && surface.setPointerCapture && typeof event.pointerId === 'number') {
            try {
                surface.setPointerCapture(event.pointerId);
            } catch (error) {
                // ignore
            }
        }
        startStroke(event);
    }

    function handlePointerMove(event) {
        if (!isDrawing) return;
        if (event.pointerType === 'mouse' && event.buttons === 0) {
            handlePointerUp(event);
            return;
        }
        event.preventDefault();
        extendStroke(event);
    }

    function handlePointerUp(event) {
        if (!isDrawing) return;
        event.preventDefault();
        if (surface && surface.releasePointerCapture && typeof event.pointerId === 'number') {
            try {
                surface.releasePointerCapture(event.pointerId);
            } catch (error) {
                // ignore
            }
        }
        finishStroke();
    }

    function handlePointerLeave(event) {
        // Finish stroke when pointer leaves the surface (fallback for browsers where capture fails)
        if (!isDrawing) return;
        // Always finish stroke on leave - event.buttons is unreliable for pen input
        finishStroke();
    }

    function handleGlobalPointerUp(event) {
        // Global fallback to catch pointerup events outside the surface
        if (!isDrawing) return;
        finishStroke();
    }

    function handleMouseDown(event) {
        if (event.button !== 0) return;
        event.preventDefault();
        startStroke(event);
    }

    function handleMouseMove(event) {
        if (!isDrawing) return;
        event.preventDefault();
        extendStroke(event);
    }

    function handleMouseUp(event) {
        if (!isDrawing) return;
        event.preventDefault();
        finishStroke();
    }

    function handleTouchStart(event) {
        const normalized = normalizeTouchEvent(event);
        if (!normalized) return;
        event.preventDefault();
        startStroke(normalized);
    }

    function handleTouchMove(event) {
        if (!isDrawing) return;
        const normalized = normalizeTouchEvent(event);
        if (!normalized) return;
        event.preventDefault();
        extendStroke(normalized);
    }

    function handleTouchEnd(event) {
        if (!isDrawing) return;
        event.preventDefault();
        finishStroke();
    }

    function normalizeTouchEvent(event) {
        if (!event) return null;
        const touch = (event.touches && event.touches[0]) || (event.changedTouches && event.changedTouches[0]);
        if (!touch) {
            return null;
        }
        return {
            clientX: touch.clientX,
            clientY: touch.clientY,
            target: surface
        };
    }

    function startStroke(event) {
        const pos = getSurfacePosition(event);
        if (!pos) return;
        isDrawing = true;
        currentStroke = {
            color: currentColor,
            size: currentSize,
            points: [pos],
            surfaceWidth: Math.max(surfaceWidth, MIN_SURFACE_RENDER_SIZE),
            surfaceHeight: Math.max(surfaceHeight, MIN_SURFACE_RENDER_SIZE)
        };
        // Create the stroke element immediately for incremental updates
        if (svgRoot && surfaceWidth >= MIN_SURFACE_RENDER_SIZE && surfaceHeight >= MIN_SURFACE_RENDER_SIZE) {
            currentStrokeElement = createStrokeElement(currentStroke);
            if (currentStrokeElement) {
                svgRoot.appendChild(currentStrokeElement);
            }
        }
    }

    function extendStroke(event) {
        if (!currentStroke) return;
        if (!appendEventPoints(event, currentStroke.points)) {
            return;
        }
        // Update the current stroke element directly instead of full redraw
        updateCurrentStrokeElement();
    }

    function updateCurrentStrokeElement() {
        if (!currentStroke || !currentStrokeElement || !svgRoot) return;

        const points = scaleStrokePoints(currentStroke);
        if (!points.length) return;

        if (points.length === 1) {
            // Convert to circle if needed
            if (currentStrokeElement.tagName !== 'circle') {
                const circle = document.createElementNS(SVG_NS, 'circle');
                circle.setAttribute('cx', points[0].x);
                circle.setAttribute('cy', points[0].y);
                circle.setAttribute('r', currentStroke.size / 2);
                circle.setAttribute('fill', currentStroke.color);
                svgRoot.replaceChild(circle, currentStrokeElement);
                currentStrokeElement = circle;
            } else {
                currentStrokeElement.setAttribute('cx', points[0].x);
                currentStrokeElement.setAttribute('cy', points[0].y);
            }
        } else {
            // Convert to path if needed
            if (currentStrokeElement.tagName !== 'path') {
                const path = document.createElementNS(SVG_NS, 'path');
                path.setAttribute('stroke', currentStroke.color);
                path.setAttribute('stroke-width', currentStroke.size);
                path.setAttribute('fill', 'none');
                path.setAttribute('stroke-linecap', 'round');
                path.setAttribute('stroke-linejoin', 'round');
                svgRoot.replaceChild(path, currentStrokeElement);
                currentStrokeElement = path;
            }
            // Use simple polyline for live drawing (faster), smooth path on finish
            currentStrokeElement.setAttribute('d', buildFastPath(points));
        }
    }

    function buildFastPath(points) {
        if (!points || !points.length) return '';
        if (points.length === 1) {
            return `M ${points[0].x} ${points[0].y} L ${points[0].x} ${points[0].y}`;
        }
        if (points.length === 2) {
            return `M ${points[0].x} ${points[0].y} L ${points[1].x} ${points[1].y}`;
        }

        // Use quadratic curves for smoother live drawing
        let d = `M ${points[0].x} ${points[0].y}`;

        // First segment to midpoint
        const firstMid = {
            x: (points[0].x + points[1].x) / 2,
            y: (points[0].y + points[1].y) / 2
        };
        d += ` L ${firstMid.x} ${firstMid.y}`;

        // Quadratic curves through control points
        for (let i = 1; i < points.length - 1; i++) {
            const xc = (points[i].x + points[i + 1].x) / 2;
            const yc = (points[i].y + points[i + 1].y) / 2;
            d += ` Q ${points[i].x} ${points[i].y} ${xc} ${yc}`;
        }

        // Last point
        const last = points.length - 1;
        d += ` L ${points[last].x} ${points[last].y}`;

        return d;
    }

    function distance(p1, p2) {
        const dx = p2.x - p1.x;
        const dy = p2.y - p1.y;
        return Math.sqrt(dx * dx + dy * dy);
    }

    function interpolatePoints(p1, p2, maxDist) {
        const dist = distance(p1, p2);
        if (dist <= maxDist) {
            return [p2];
        }
        const steps = Math.ceil(dist / maxDist);
        const result = [];
        for (let i = 1; i <= steps; i++) {
            const t = i / steps;
            result.push({
                x: p1.x + (p2.x - p1.x) * t,
                y: p1.y + (p2.y - p1.y) * t
            });
        }
        return result;
    }

    // Catmull-Rom spline interpolation for smoother curves
    function catmullRomInterpolate(p0, p1, p2, p3, t) {
        const t2 = t * t;
        const t3 = t2 * t;
        return {
            x: 0.5 * ((2 * p1.x) + (-p0.x + p2.x) * t + (2 * p0.x - 5 * p1.x + 4 * p2.x - p3.x) * t2 + (-p0.x + 3 * p1.x - 3 * p2.x + p3.x) * t3),
            y: 0.5 * ((2 * p1.y) + (-p0.y + p2.y) * t + (2 * p0.y - 5 * p1.y + 4 * p2.y - p3.y) * t2 + (-p0.y + 3 * p1.y - 3 * p2.y + p3.y) * t3)
        };
    }

    function interpolateWithSpline(target, newPos, maxDist) {
        if (!target || target.length < 2) {
            return interpolatePoints(target[target.length - 1] || newPos, newPos, maxDist);
        }

        const p0 = target.length >= 3 ? target[target.length - 3] : target[target.length - 2];
        const p1 = target[target.length - 2];
        const p2 = target[target.length - 1];
        const p3 = newPos;

        const dist = distance(p2, p3);
        if (dist <= maxDist) {
            return [p3];
        }

        const steps = Math.ceil(dist / maxDist);
        const result = [];
        for (let i = 1; i <= steps; i++) {
            const t = i / steps;
            result.push(catmullRomInterpolate(p0, p1, p2, p3, t));
        }
        return result;
    }

    function addPointWithInterpolation(target, newPos) {
        if (!target || !newPos) return false;

        if (target.length === 0) {
            target.push(newPos);
            return true;
        }

        const lastPos = target[target.length - 1];
        const dist = distance(lastPos, newPos);

        // Skip if too close (avoid duplicates)
        if (dist < MIN_POINT_DISTANCE) {
            return false;
        }

        // Interpolate if too far apart - use linear interpolation for reliability
        if (dist > MAX_POINT_DISTANCE) {
            const interpolated = interpolatePoints(lastPos, newPos, MAX_POINT_DISTANCE);
            for (let i = 0; i < interpolated.length; i++) {
                target.push(interpolated[i]);
            }
            return true;
        }

        target.push(newPos);
        return true;
    }

    function appendEventPoints(event, target) {
        if (!target) return false;

        // Pointer events in MSFS (and some browsers) are heavily coalesced.
        // Using the buffered high-resolution points prevents the "polygon" look.
        if (event && typeof event.getCoalescedEvents === 'function') {
            const coalesced = event.getCoalescedEvents();
            if (coalesced && coalesced.length) {
                let added = false;
                for (let i = 0; i < coalesced.length; i++) {
                    const pos = getSurfacePosition(coalesced[i]);
                    if (pos && addPointWithInterpolation(target, pos)) {
                        added = true;
                    }
                }
                if (added) {
                    return true;
                }
            }
        }

        const fallbackPos = getSurfacePosition(event);
        if (fallbackPos && addPointWithInterpolation(target, fallbackPos)) {
            return true;
        }

        return false;
    }

    function finishStroke() {
        if (!currentStroke || !currentStroke.points.length) {
            // Remove empty stroke element if it exists
            if (currentStrokeElement && svgRoot && currentStrokeElement.parentNode === svgRoot) {
                svgRoot.removeChild(currentStrokeElement);
            }
            currentStrokeElement = null;
            currentStroke = null;
            isDrawing = false;
            return;
        }
        // Convert fast path to smooth path for final rendering
        // Apply Chaikin smoothing to reduce angular appearance in Kneeboard browsers
        if (currentStrokeElement && currentStrokeElement.tagName === 'path') {
            let points = scaleStrokePoints(currentStroke);
            if (points.length > 2) {
                // Apply 5 iterations of Chaikin smoothing for maximum smoothness
                points = chaikinSmooth(points, 5);
            }
            if (points.length > 1) {
                currentStrokeElement.setAttribute('d', buildSmoothPath(points));
            }
        }
        drawing.push(currentStroke);
        saveDrawing();
        currentStrokeElement = null;
        currentStroke = null;
        isDrawing = false;
    }

    function getSurfacePosition(event) {
        if (!surface) return null;

        // Prefer offset coordinates when the pointer event is delivered directly by the surface,
        // because certain embedded browsers misreport client coordinates near the top edge.
        if (event && event.target === surface) {
            const ox = typeof event.offsetX === 'number' ? event.offsetX : null;
            const oy = typeof event.offsetY === 'number' ? event.offsetY : null;
            if (ox !== null && oy !== null && isFinite(ox) && isFinite(oy)) {
                return clampPointToSurface(ox, oy);
            }
        }

        const rect = surface.getBoundingClientRect();
        const x = typeof event.clientX === 'number' ? event.clientX - rect.left : null;
        const y = typeof event.clientY === 'number' ? event.clientY - rect.top : null;
        if (x === null || y === null || !isFinite(x) || !isFinite(y)) {
            return null;
        }
        return clampPointToSurface(x, y);
    }

    function clampPointToSurface(x, y) {
        const width = surfaceWidth || (surface && surface.clientWidth) || 0;
        const height = surfaceHeight || (surface && surface.clientHeight) || 0;
        const clampedX = Math.min(Math.max(x, 0), Math.max(0, width));
        const clampedY = Math.min(Math.max(y, 0), Math.max(0, height));
        return { x: clampedX, y: clampedY };
    }

    function ensureSurfaceSize() {
        if (!surface) {
            return false;
        }

        const rect = surface.getBoundingClientRect();
        let width = Math.round(rect.width || 0);
        let height = Math.round(rect.height || 0);

        if (width < MIN_SURFACE_RENDER_SIZE || height < MIN_SURFACE_RENDER_SIZE) {
            const container = surface.closest('#Notepad');
            const toolbar = container ? container.querySelector('#toolbar') : null;
            const parent = surface.parentElement;

            if (!width || width < MIN_SURFACE_RENDER_SIZE) {
                width = Math.round(surface.clientWidth || (parent && parent.clientWidth) || window.innerWidth || 0);
            }
            if (!height || height < MIN_SURFACE_RENDER_SIZE) {
                const parentRect = parent ? parent.getBoundingClientRect() : null;
                const parentHeight = parentRect ? parentRect.height : (parent && parent.clientHeight) || window.innerHeight || 0;
                const toolbarHeight = (toolbar && toolbar.offsetHeight) || 0;
                height = Math.round(parentHeight - toolbarHeight);
            }
        }

        width = Math.max(1, width);
        height = Math.max(1, height);

        if (surfaceWidth === width && surfaceHeight === height) {
            if (width >= MIN_SURFACE_RENDER_SIZE && height >= MIN_SURFACE_RENDER_SIZE) {
                return false;
            }
        }

        surfaceWidth = width;
        surfaceHeight = height;
        if (svgRoot) {
            svgRoot.setAttribute('viewBox', `0 0 ${width} ${height}`);
            svgRoot.setAttribute('width', width);
            svgRoot.setAttribute('height', height);
        }
        return true;
    }

    function renderStroke(stroke) {
        if (!svgRoot || surfaceWidth < MIN_SURFACE_RENDER_SIZE || surfaceHeight < MIN_SURFACE_RENDER_SIZE) return;
        const element = createStrokeElement(stroke);
        if (element) {
            svgRoot.appendChild(element);
        }
    }

    function createStrokeElement(stroke) {
        if (!stroke || !stroke.points || stroke.points.length === 0) return null;
        const points = scaleStrokePoints(stroke);
        if (!points.length) {
            return null;
        }

        if (points.length === 1) {
            const circle = document.createElementNS(SVG_NS, 'circle');
            circle.setAttribute('cx', points[0].x);
            circle.setAttribute('cy', points[0].y);
            circle.setAttribute('r', stroke.size / 2);
            circle.setAttribute('fill', stroke.color);
            return circle;
        }

        const path = document.createElementNS(SVG_NS, 'path');
        path.setAttribute('d', buildSmoothPath(points));
        path.setAttribute('stroke', stroke.color);
        path.setAttribute('stroke-width', stroke.size);
        path.setAttribute('fill', 'none');
        path.setAttribute('stroke-linecap', 'round');
        path.setAttribute('stroke-linejoin', 'round');
        return path;
    }

    function scaleStrokePoints(stroke) {
        if (!Array.isArray(stroke.points)) {
            return [];
        }
        const sourceWidth = Math.max(Number(stroke.surfaceWidth) || surfaceWidth || 0, 1);
        const sourceHeight = Math.max(Number(stroke.surfaceHeight) || surfaceHeight || 0, 1);
        if (surfaceWidth < MIN_SURFACE_RENDER_SIZE || surfaceHeight < MIN_SURFACE_RENDER_SIZE) {
            return [];
        }
        const scaleX = surfaceWidth / sourceWidth;
        const scaleY = surfaceHeight / sourceHeight;
        return stroke.points.map(point => ({
            x: (Number(point.x) || 0) * scaleX,
            y: (Number(point.y) || 0) * scaleY
        }));
    }

    function buildSmoothPath(points) {
        if (!points || !points.length) {
            return '';
        }
        if (points.length === 1) {
            const p = points[0];
            return `M ${p.x} ${p.y} L ${p.x} ${p.y}`;
        }
        if (points.length === 2) {
            // For 2 points, draw a simple line
            return `M ${points[0].x} ${points[0].y} L ${points[1].x} ${points[1].y}`;
        }
        if (points.length === 3) {
            // For 3 points, use a single quadratic curve through the middle point
            const mid = points[1];
            return `M ${points[0].x} ${points[0].y} Q ${mid.x} ${mid.y} ${points[2].x} ${points[2].y}`;
        }

        // For 4+ points, use smooth quadratic bezier curves
        let d = `M ${points[0].x} ${points[0].y}`;

        // First segment: line to midpoint between first and second point
        const firstMid = {
            x: (points[0].x + points[1].x) / 2,
            y: (points[0].y + points[1].y) / 2
        };
        d += ` L ${firstMid.x} ${firstMid.y}`;

        // Middle segments: quadratic curves through control points
        for (let i = 1; i < points.length - 2; i++) {
            const xc = (points[i].x + points[i + 1].x) / 2;
            const yc = (points[i].y + points[i + 1].y) / 2;
            d += ` Q ${points[i].x} ${points[i].y} ${xc} ${yc}`;
        }

        // Last segment: curve to final point
        const last = points.length - 1;
        d += ` Q ${points[last - 1].x} ${points[last - 1].y} ${points[last].x} ${points[last].y}`;
        return d;
    }

    function redraw() {
        if (!svgRoot || !surface) return;
        if (surfaceWidth < MIN_SURFACE_RENDER_SIZE || surfaceHeight < MIN_SURFACE_RENDER_SIZE) {
            return;
        }
        svgRoot.textContent = '';
        drawing.forEach(renderStroke);
        if (currentStroke) {
            renderStroke(currentStroke);
        }
    }

    function saveDrawing() {
        try {
            const payload = {
                version: 2,
                width: surfaceWidth,
                height: surfaceHeight,
                strokes: drawing.map(stroke => normalizeStroke(stroke, surfaceWidth, surfaceHeight))
            };
            localStorage.setItem(DRAWING_STORAGE_KEY, JSON.stringify(payload));
        } catch (error) {
            console.warn('Notepad: Unable to save drawing.', error);
        }
    }

    function loadDrawing() {
        try {
            const raw = localStorage.getItem(DRAWING_STORAGE_KEY);
            if (!raw) return [];
            const parsed = JSON.parse(raw);
            if (parsed && Array.isArray(parsed.strokes)) {
                const fallbackWidth = Number(parsed.width) || surfaceWidth || MIN_SURFACE_RENDER_SIZE;
                const fallbackHeight = Number(parsed.height) || surfaceHeight || MIN_SURFACE_RENDER_SIZE;
                return parsed.strokes.map(stroke => normalizeStroke(stroke, fallbackWidth, fallbackHeight));
            }
            if (Array.isArray(parsed)) {
                return parsed.map(stroke => normalizeStroke(stroke, surfaceWidth || MIN_SURFACE_RENDER_SIZE, surfaceHeight || MIN_SURFACE_RENDER_SIZE));
            }
        } catch (error) {
            console.warn('Notepad: Unable to load drawing.', error);
        }
        return [];
    }

    function normalizeStroke(stroke, fallbackWidth, fallbackHeight) {
        const width = Math.max(Number(stroke && stroke.surfaceWidth) || Number(fallbackWidth) || MIN_SURFACE_RENDER_SIZE, 1);
        const height = Math.max(Number(stroke && stroke.surfaceHeight) || Number(fallbackHeight) || MIN_SURFACE_RENDER_SIZE, 1);
        const normalizedPoints = Array.isArray(stroke && stroke.points)
            ? stroke.points.map(point => ({
                x: Number(point && point.x) || 0,
                y: Number(point && point.y) || 0
            }))
            : [];
        return {
            color: (stroke && stroke.color) || '#000000',
            size: Number(stroke && stroke.size) || 5,
            points: normalizedPoints,
            surfaceWidth: width,
            surfaceHeight: height
        };
    }

    function updateNotepadColors(light, dark) {
        if (light) theme.light = light;
        if (dark) theme.dark = dark;
        if (colorMode === 'A') {
            currentColor = theme.light;
        }
    }

    function handleIncomingMessage(event) {
        if (typeof event.data === 'string' && event.data.startsWith('colors(')) {
            const payload = event.data.slice(7, -1);
            const [light, dark, fontLight, fontDark] = payload.split('_');
            if (light) theme.light = light.trim();
            if (dark) theme.dark = dark.trim();
            if (fontLight) theme.fontLight = fontLight.trim();
            if (fontDark) theme.fontDark = fontDark.trim();

            document.documentElement.style.setProperty('--light', theme.light);
            document.documentElement.style.setProperty('--dark', theme.dark);
            document.documentElement.style.setProperty('--fontLight', theme.fontLight);
            document.documentElement.style.setProperty('--fontDark', theme.fontDark);

            localStorage.setItem('colorLight', theme.light);
            localStorage.setItem('colorDark', theme.dark);
            localStorage.setItem('fontColorLight', theme.fontLight);
            localStorage.setItem('fontColorDark', theme.fontDark);

            if (colorMode === 'A') {
                currentColor = theme.light;
            }
        }
    }

    document.addEventListener('themechange', function (event) {
        if (!event || !event.detail) return;
        updateNotepadColors(event.detail.colorLight, event.detail.colorDark);
        if (ensureSurfaceSize()) {
            redraw();
        } else {
            scheduleSurfaceRefresh(100);
            scheduleSurfaceRefresh(300);
        }
    });

    window.addEventListener('message', handleIncomingMessage);

    window.initNotepadPage = initNotepadPage;
    window.updateNotepadColors = updateNotepadColors;

    function startSurfaceMonitor() {
        if (surfaceMonitor) {
            return;
        }
        surfaceMonitor = setInterval(() => {
            if (!surface) {
                return;
            }
            // Skip while the Notepad tab is hidden (display: none).
            if (!surface.offsetParent) {
                return;
            }
            const changed = ensureSurfaceSize();
            const needsImmediateRefresh = surfaceWidth < MIN_SURFACE_RENDER_SIZE || surfaceHeight < MIN_SURFACE_RENDER_SIZE;
            if (changed || needsImmediateRefresh) {
                redraw();
            }
        }, 400);
    }

    function stopSurfaceMonitor() {
        if (surfaceMonitor) {
            clearInterval(surfaceMonitor);
            surfaceMonitor = null;
        }
    }

    function scheduleSurfaceRefresh(delay) {
        if (typeof delay !== 'number' || delay <= 0) return;
        setTimeout(() => {
            if (ensureSurfaceSize()) {
                redraw();
            }
        }, delay);
    }

    function waitForSurfaceReady(callback, attempts = 20) {
        if (ensureSurfaceSize() || (surfaceWidth >= MIN_SURFACE_RENDER_SIZE && surfaceHeight >= MIN_SURFACE_RENDER_SIZE)) {
            callback();
            return;
        }
        if (attempts <= 0) {
            callback();
            return;
        }
        requestFrame(() => waitForSurfaceReady(callback, attempts - 1));
    }
})();
