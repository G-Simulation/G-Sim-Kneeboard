// GSim Kneeboard - Toolbar (MSFS 2024/2020)
// Simple iframe container - all logic runs in map.js on the server
(function () {
  // Debug Configuration
  const TOOLBAR_DEBUG = true;

  // Configuration
  const KNEEBOARD_PORT = window.GSIM_KNEEBOARD_PORT || 815;
  const KNEEBOARD_HOST = window.GSIM_KNEEBOARD_HOST || "localhost";
  const LOCAL_KNEEBOARD_URL = `http://${KNEEBOARD_HOST}:${KNEEBOARD_PORT}/kneeboard.html`;
  const SERVER_PROBE_RATE_MS = 5000;

  // DOM references
  let iframe = null;
  let fallback = null;
  let container = null;

  // State
  let isConnected = false;
  let reconnectTimerId = null;
  let serverProbeIntervalId = null;

  // ============================================================
  // UI & Connection Management
  // ============================================================
  function setupClickThroughPrevention(containerEl) {
    if (!containerEl) return;
    const eventsToBlock = ["wheel", "mousedown", "mouseup", "click", "contextmenu"];
    eventsToBlock.forEach((eventName) => {
      containerEl.addEventListener(eventName, (e) => {
        e.stopPropagation();
      }, { capture: true });
    });
  }

  function setConnectionState(connected) {
    isConnected = connected;
    if (fallback) {
      fallback.style.display = connected ? "none" : "flex";
    }
    if (iframe) {
      iframe.style.display = connected ? "block" : "none";
    }
  }

  function scheduleReconnect() {
    clearReconnectTimer();
    reconnectTimerId = setTimeout(() => {
      reloadIframe();
    }, 4000);
  }

  function clearReconnectTimer() {
    if (reconnectTimerId) {
      window.clearTimeout(reconnectTimerId);
      reconnectTimerId = null;
    }
  }

  function reloadIframe() {
    if (!iframe) return;
    iframe.src = `${LOCAL_KNEEBOARD_URL}?t=${Date.now()}`;
  }

  function handleIframeLoad() {
    // Only mark as connected if we have a valid src (not about:blank or empty)
    if (iframe && iframe.src && iframe.src.startsWith("http")) {
      setConnectionState(true);
      clearReconnectTimer();
      stopServerProbe();
      if (TOOLBAR_DEBUG) console.log("[Kneeboard] iframe loaded successfully:", iframe.src);
    }
  }

  function handleIframeError() {
    setConnectionState(false);
    scheduleReconnect();
    startServerProbe();
  }

  function startServerProbe() {
    if (serverProbeIntervalId !== null) return;
    serverProbeIntervalId = setInterval(probeServerReachability, SERVER_PROBE_RATE_MS);
  }

  function stopServerProbe() {
    if (serverProbeIntervalId !== null) {
      window.clearInterval(serverProbeIntervalId);
      serverProbeIntervalId = null;
    }
  }

  function probeServerReachability() {
    fetch(LOCAL_KNEEBOARD_URL, { method: 'HEAD', cache: 'no-cache' })
      .then(() => {
        if (!isConnected) {
          reloadIframe();
        }
      })
      .catch(() => {
        if (isConnected) {
          setConnectionState(false);
        }
      });
  }

  // Initialize
  function init() {
    container = document.getElementById("kneeboard-container");
    iframe = document.getElementById("kneeboard-iframe");
    fallback = document.getElementById("connection-fallback");

    if (!container || !iframe || !fallback) {
      console.error("[Kneeboard] Required DOM elements not found");
      return;
    }

    // Set logo (local path, not from server since server might be offline)
    const logoImg = document.getElementById("logo-img");
    if (logoImg) {
      logoImg.src = "/InGamePanels/Kneeboard/Assets/Logo.png";
    }

    setupClickThroughPrevention(container);
    setConnectionState(false);

    // Iframe event listeners
    iframe.addEventListener("load", handleIframeLoad);
    iframe.addEventListener("error", handleIframeError);

    // Start server probing and immediately try to load
    startServerProbe();
    reloadIframe();

    if (TOOLBAR_DEBUG) console.log("[Kneeboard] Initialized successfully");
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", init);
  } else {
    init();
  }

  // Cleanup
  window.addEventListener("unload", () => {
    if (iframe) {
      iframe.removeEventListener("load", handleIframeLoad);
      iframe.removeEventListener("error", handleIframeError);
    }
    clearReconnectTimer();
    stopServerProbe();
  });

  // Reinitialize when panel is shown again (after being hidden/closed)
  // Coherent GT may not fully unload the page, leaving JS state but resetting DOM
  window.addEventListener("pageshow", (event) => {
    if (event.persisted || !iframe || !fallback || !container) {
      if (TOOLBAR_DEBUG) console.log("[Kneeboard] pageshow - reinitializing");
      init();
    }
  });
})();
