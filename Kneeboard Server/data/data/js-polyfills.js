/**
 * JavaScript Polyfills for MSFS Coherent GT Browser Compatibility
 *
 * MSFS uses an older Coherent GT browser engine that doesn't support
 * modern ECMAScript 2022+ features. These polyfills ensure compatibility
 * while maintaining functionality in modern browsers.
 *
 * Created: 2025-11-29
 * Purpose: Fix Leaflet.SmoothMarkerBouncing.js Object.hasOwn() error
 */

// ============================================================================
// Object.hasOwn() Polyfill (ES2022)
// ============================================================================
// Used by: Leaflet.SmoothMarkerBouncing.js (isRealMarker function)
// Spec: https://tc39.es/ecma262/#sec-object.hasown
//
// This method is a safer alternative to Object.prototype.hasOwnProperty.call()
// and was introduced in ECMAScript 2022. MSFS Coherent GT uses an older
// JavaScript engine that doesn't support it.
if (!Object.hasOwn) {
  Object.hasOwn = function(obj, prop) {
    if (obj === null || obj === undefined) {
      throw new TypeError('Cannot convert undefined or null to object');
    }
    return Object.prototype.hasOwnProperty.call(obj, prop);
  };
}

// ============================================================================
// CustomEvent Polyfill (for older browsers/GT Coherent)
// ============================================================================
// Used by: kneeboard.js for theme changes and other custom events
// GT Coherent may have issues with CustomEvent constructor for complex detail objects
if (typeof window.CustomEvent !== "function") {
  (function() {
    function CustomEvent(event, params) {
      params = params || { bubbles: false, cancelable: false, detail: null };
      var evt = document.createEvent('CustomEvent');
      evt.initCustomEvent(event, params.bubbles, params.cancelable, params.detail);
      return evt;
    }
    CustomEvent.prototype = window.Event.prototype;
    window.CustomEvent = CustomEvent;
  })();
}

// ============================================================================
// Coherent GT Detection (MSFS Browser Engine)
// ============================================================================
// Detects if running inside MSFS Coherent GT engine for performance optimizations
// Used by: map.js for tile loading optimizations, animation settings
window.isCoherentGT = (function() {
  // Check for Coherent global object (present in MSFS panels)
  if (typeof window.Coherent !== 'undefined') {
    return true;
  }
  // Check user agent string
  if (typeof navigator !== 'undefined' && navigator.userAgent &&
      navigator.userAgent.indexOf('CoherentGT') !== -1) {
    return true;
  }
  // Check if running as iframe in MSFS EFB (parent has KneeboardApiProxyUrl)
  try {
    if (window.parent && window.parent !== window &&
        window.parent.KneeboardApiProxyUrl !== undefined) {
      return true;
    }
  } catch (e) {
    // Cross-origin access blocked - likely in MSFS
    return true;
  }
  return false;
})();

if (window.isCoherentGT) {
  console.log('[Polyfills] Coherent GT detected - performance optimizations enabled');
}

// ============================================================================
// Coherent GT Cursor Polyfill
// ============================================================================
// Coherent GT hat eingeschränkte Unterstützung für CSS cursor: grab/pointer
// Diese Funktion setzt cursor styles programmatisch wenn nötig

(function() {
  if (!window.isCoherentGT) return;

  // Warte bis DOM geladen ist
  document.addEventListener('DOMContentLoaded', function() {
    // Setze default cursor für Map Container
    var mapContainer = document.getElementById('map');
    if (mapContainer) {
      mapContainer.style.cursor = 'grab';
    }

    // MutationObserver um neue interaktive Elemente zu behandeln
    var observer = new MutationObserver(function(mutations) {
      mutations.forEach(function(mutation) {
        mutation.addedNodes.forEach(function(node) {
          if (node.nodeType === 1) { // Element node
            // Setze pointer cursor für interaktive Leaflet Elemente
            if (node.classList && node.classList.contains('leaflet-interactive')) {
              node.style.cursor = 'pointer';
            }
            // Auch für Kind-Elemente
            var interactives = node.querySelectorAll ? node.querySelectorAll('.leaflet-interactive') : [];
            interactives.forEach(function(el) {
              el.style.cursor = 'pointer';
            });
          }
        });
      });
    });

    // Starte Observer für das gesamte Dokument
    observer.observe(document.body, {
      childList: true,
      subtree: true
    });

    console.log('[Polyfills] Coherent GT cursor polyfill initialized');
  });
})();

// ============================================================================
// Future polyfills can be added below as needed
// ============================================================================
