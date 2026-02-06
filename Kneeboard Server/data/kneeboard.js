// VERSION MARKER
var KNEEBOARD_JS_VERSION = "2025-12-19-v2.0";
// Use centralized DEBUG_CONFIG if available
var KNEEBOARD_DEBUG = (typeof DEBUG_CONFIG !== 'undefined' && DEBUG_CONFIG.SIMBRIEF) || false;

// State that gets shared across the SPA tabs
var activeTab = "navlog";
var keyboardActive = false;
var receiveMessages = true;
var btnNavlog;
var btnDocuments;
var btnNotepad;
var btnFormulas;
var btnMap;
var style = 1;
var colorLight;
var colorDark;
var fontColorLight;
var fontColorDark;
// Keeps rendered tab DOM fragments alive to avoid costly reloads when switching tabs.
var tabContentCache = Object.create(null);
var tabInitialized = Object.create(null);
var TAB_LOG_PREFIX = "[Tab]";
// Tab-Reload Debouncing (verhindert Race Conditions bei schnellem Doppelklick)
var mapLoadDebounceTimer = null;
var mapIsLoading = false;

// Zentraler Logger - nutzt KneeboardLogger falls verfügbar
const logger = (typeof KneeboardLogger !== 'undefined')
  ? KneeboardLogger.createLogger('Kneeboard', { minLevel: 'DEBUG', debugConfigKey: 'SIMBRIEF' })
  : { info: function(){}, warn: function(){}, error: function(){}, debug: function(){} };

// Mapping between tab identifiers and their HTML fragments.
const PAGE_MAP = {
  navlog: "navlog.html",
  documents: "documents.html",
  map: "map.html?v=1",
  notepad: "notepad.html",
  formulas: "formulas.html?v=32",
};

/**
 * Broadcasts a message to the map iframe/parent window so it stays in sync
 * with the other tabs, but fails gracefully when cross-origin access is blocked.
 * @param {string} message
 */
function mapMessage(message) {
  try {
    window.postMessage(message, "*");
  } catch (err) {
    logger.warn("Unable to forward message to map view:", err);
  }
}

// Track if reload is in progress to prevent race conditions
var tabReloadInProgress = false;

/**
 * Reloads the currently active tab by clearing its cache and reloading the content.
 * @param {string} tabKey - The tab key to reload (e.g., "navlog", "map", etc.)
 */
function reloadActiveTab(tabKey) {
  // Only reload if this tab is currently active
  if (activeTab !== tabKey) {
    logger.info("Tab", tabKey, "is not active, skipping reload");
    return;
  }

  // Prevent concurrent reloads
  if (tabReloadInProgress) {
    logger.info("Tab reload already in progress, ignoring duplicate request");
    return;
  }

  tabReloadInProgress = true;
  window.lastReloadStartTime = Date.now();
  logger.info("Reloading active tab:", tabKey);

  // TEIL 3: Kompletten Cache für diesen Tab löschen bei Doppelklick-Reload
  if (tabKey === "navlog") {
    logger.info("[NavlogTab] Clearing all navlog caches on reload");
    // Reset navlog event listener flag so they get re-registered
    if (typeof navlogEventListenersInitialized !== 'undefined') {
      navlogEventListenersInitialized = false;
    }
    // Clear navlog sessionStorage cache (SimBrief data)
    try {
      sessionStorage.removeItem('kneeboard_cachedFlightplanData');
      sessionStorage.removeItem('kneeboard_cachedFlightplanMeta');
      sessionStorage.removeItem('kneeboard_cachedOfpData');
      logger.info("[NavlogTab] sessionStorage cache cleared");
    } catch (e) {
      logger.warn("[NavlogTab] Error clearing sessionStorage:", e);
    }
    // Clear navlog RAM cache
    if (typeof cachedFlightplanData !== 'undefined') cachedFlightplanData = null;
    if (typeof cachedFlightplanMeta !== 'undefined') cachedFlightplanMeta = {};
    if (typeof cachedOfpData !== 'undefined') cachedOfpData = null;
    // Clear navlog localStorage
    if (typeof clearNavlogLocalStorage === 'function') {
      clearNavlogLocalStorage();
      logger.info("[NavlogTab] localStorage cache cleared");
    }
  }

  // Special handling for map tab - hide elements BEFORE clearing cache
  if (tabKey === "map") {
    try {
      // Clear map UI state from localStorage on double-click reload
      logger.info("[MapTab] Clearing map UI state cache on reload");
      try {
        // Clear the versioned map UI state
        var CACHE_VERSION = 'v1.0.0';
        var CACHE_PREFIX = 'kbMap_' + CACHE_VERSION + '_';
        localStorage.removeItem(CACHE_PREFIX + 'mapUiState');
        // Also clear legacy keys
        localStorage.removeItem('mapUiState');
        logger.info("[MapTab] Map UI state cache cleared");
      } catch (cacheErr) {
        logger.warn("[MapTab] Error clearing map UI state cache:", cacheErr);
      }

      // Hide waypoint list overlay
      var overlay = document.getElementById("overlay");
      if (overlay) overlay.style.visibility = "hidden";

      var banner = document.getElementById("banner");
      if (banner) banner.style.visibility = "hidden";

      var overlayContainer = document.getElementById("overlayContainer");
      if (overlayContainer) overlayContainer.style.visibility = "hidden";

      var overlayList = document.getElementById("overlayList");
      if (overlayList) overlayList.style.visibility = "hidden";

      // Hide elevation profile section
      var elevationSection = document.getElementById('elevationProfileSection');
      if (elevationSection) {
        elevationSection.style.display = 'none';
        // Reset visibility flags so panel will be re-shown after reload
        if (typeof elevationProfileVisible !== 'undefined') {
          elevationProfileVisible = false;
        }
        // WICHTIG: Reset user-closed flag so elevation profile shows after animation
        if (typeof elevationProfileUserClosed !== 'undefined') {
          elevationProfileUserClosed = false;
        }
      }

      // Clear polylines if they exist
      if (typeof pLineGroup !== 'undefined' && pLineGroup) {
        pLineGroup.clearLayers();
      }
      if (typeof pLineGroupDEP !== 'undefined' && pLineGroupDEP) {
        pLineGroupDEP.clearLayers();
      }
      if (typeof pLineGroupARR !== 'undefined' && pLineGroupARR) {
        pLineGroupARR.clearLayers();
      }

      // Clear markers if they exist
      if (typeof waypointMarkers !== 'undefined' && waypointMarkers) {
        waypointMarkers.clearLayers();
      }
      if (typeof middleMarkers !== 'undefined' && middleMarkers) {
        middleMarkers.clearLayers();
      }

    } catch (e) {
      logger.warn("Error hiding map elements during reload:", e);
    }
  }

  // Clear the cache and initialization state for this tab
  if (tabContentCache[tabKey]) {
    tabContentCache[tabKey].dataset.loaded = "false";
    tabContentCache[tabKey].innerHTML = '<div class="loader">Reloading...</div>';
  }
  tabInitialized[tabKey] = false;

  // Reload the tab content based on which tab it is
  // Use setTimeout to ensure reload completes before resetting flag
  setTimeout(function() {
    switch (tabKey) {
      case "navlog":
        btnNavlogClicked();
        break;
      case "documents":
        btnDocumentsClicked();
        break;
      case "map":
        // Call performMapTabSwitch directly to bypass the tabReloadInProgress check in btnMapClicked
        performMapTabSwitch();
        break;
      case "notepad":
        btnNotepadClicked();
        break;
      case "formulas":
        btnFormulasClicked();
        break;
      default:
        logger.warn("Unknown tab key for reload:", tabKey);
    }
    // Reset reload flag after tab switch completes
    setTimeout(function() {
      tabReloadInProgress = false;
    }, 300);
  }, 50);
}

/**
 * Converts a SimBrief/Microsoft PLN coordinate like
 * "N48° 46' 45.67\"" into a decimal degree number.
 */
function parseSimbriefCoordinate(fragment) {
  if (!fragment || typeof fragment !== "string") {
    return null;
  }
  var match = fragment.match(
    /([NSWE])\s*(\d+)[^\d]+(\d+)[^\d]+(\d+(?:\.\d+)?)/i
  );
  if (!match) {
    return null;
  }
  var direction = match[1].toUpperCase();
  var degrees = parseFloat(match[2]);
  var minutes = parseFloat(match[3]);
  var seconds = parseFloat(match[4]);
  if (
    !Number.isFinite(degrees) ||
    !Number.isFinite(minutes) ||
    !Number.isFinite(seconds)
  ) {
    return null;
  }
  var value = degrees + minutes / 60 + seconds / 3600;
  if (direction === "S" || direction === "W") {
    value = -value;
  }
  return value;
}

function parseSimbriefAltitude(fragment) {
  if (!fragment || typeof fragment !== "string") {
    return 0;
  }
  var match = fragment.match(/([+-]?\d+(?:\.\d+)?)/);
  if (!match) {
    return 0;
  }
  var value = parseFloat(match[1]);
  return Number.isFinite(value) ? value : 0;
}

function parseSimbriefWorldPosition(value) {
  if (!value || typeof value !== "string") {
    return null;
  }
  var parts = value.split(",");
  if (parts.length < 2) {
    return null;
  }
  var lat = parseSimbriefCoordinate(parts[0]);
  var lng = parseSimbriefCoordinate(parts[1]);
  var alt = parseSimbriefAltitude(parts[2] || "");
  if (!Number.isFinite(lat) || !Number.isFinite(lng)) {
    return null;
  }
  return {
    lat: lat,
    lng: lng,
    alt: alt,
  };
}

function normalizeSimbriefString(value) {
  if (typeof value === "string") {
    var trimmed = value.trim();
    return trimmed ? trimmed : null;
  }
  if (typeof value === "number" || typeof value === "bigint") {
    return value.toString();
  }
  return null;
}

function convertSimbriefFlightplan(document, ofpData, procedures) {
  logger.debug('convertSimbriefFlightplan CALLED - procedures:', procedures ? JSON.stringify(procedures).substring(0, 500) : 'null');
  if (!document || !document.FlightPlanFlightPlan) {
    return { waypoints: [], ofp: ofpData || null };
  }
  var plan = document.FlightPlanFlightPlan;
  if (!plan.ATCWaypoint) {
    return { waypoints: [], ofp: ofpData || null };
  }
  var waypoints = plan.ATCWaypoint;
  if (!Array.isArray(waypoints)) {
    waypoints = [waypoints];
  }

  var sidWaypointNames = {};
  var starWaypointNames = {};
  var approachWaypointNames = {};
  var sidName = null;
  var starName = null;
  var approachName = null;

  if (procedures && procedures.sid && procedures.sid.waypoints) {
    sidName = procedures.sid.name;
    procedures.sid.waypoints.forEach(function(wp) {
      if (wp.name) sidWaypointNames[wp.name.toUpperCase()] = true;
    });
    logger.debug('SID waypoints from Navigraph:', Object.keys(sidWaypointNames).join(', '));
  }
  if (procedures && procedures.star && procedures.star.waypoints) {
    starName = procedures.star.name;
    procedures.star.waypoints.forEach(function(wp) {
      if (wp.name) starWaypointNames[wp.name.toUpperCase()] = true;
    });
    logger.debug('STAR waypoints from Navigraph:', Object.keys(starWaypointNames).join(', '));
  }
  if (procedures && procedures.approach && procedures.approach.waypoints) {
    approachName = procedures.approach.name;
    procedures.approach.waypoints.forEach(function(wp) {
      if (wp.name) approachWaypointNames[wp.name.toUpperCase()] = true;
    });
    logger.debug('Approach waypoints from Navigraph:', Object.keys(approachWaypointNames).join(', '));
  }

  var normalized = [];
  var fallbackDep = null;
  var fallbackArr = null;
  waypoints.forEach(function (wp) {
    if (!wp) {
      return;
    }
    var coords = parseSimbriefWorldPosition(wp.WorldPosition || "");
    if (!coords) {
      return;
    }
    var rawType = normalizeSimbriefString(wp && wp.ATCWaypointType);
    var typeLabel = rawType;
    var depName = normalizeSimbriefString(wp && wp.DepartureFP) || "";
    var arrName = normalizeSimbriefString(wp && wp.ArrivalFP) || "";
    var wpName = (wp.id || "").toUpperCase();

    if (depName) {
      typeLabel = formatDepArrType("DEP", depName);
      if (!fallbackDep) {
        fallbackDep = depName;
      }
    } else if (arrName) {
      typeLabel = formatDepArrType("ARR", arrName);
      fallbackArr = arrName;
    } else if (sidWaypointNames[wpName]) {
      typeLabel = formatDepArrType("DEP", sidName || "SID");
      depName = sidName || "SID";
    } else if (starWaypointNames[wpName]) {
      typeLabel = formatDepArrType("ARR", starName || "STAR");
      arrName = starName || "STAR";
    } else if (approachWaypointNames[wpName]) {
      typeLabel = formatDepArrType("ARR", approachName || "APP");
      arrName = approachName || "APP";
    }

    normalized.push({
      lat: coords.lat,
      lng: coords.lng,
      altitude: coords.alt,
      name: wp.id || "",
      waypointType: typeLabel,
      atcWaypointType: rawType,
      sourceAtcWaypointType: rawType,
      DepartureFP: depName || "",
      ArrivalFP: arrName || "",
      ATCAirway: normalizeSimbriefString(wp && wp.ATCAirway) || "",
      airway: normalizeSimbriefString(wp && wp.ATCAirway) || "",
      runwayNumberFP: normalizeSimbriefString(wp && wp.RunwayNumberFP) || "",
      runwayDesignatorFP:
        normalizeSimbriefString(wp && wp.RunwayDesignatorFP) || "",
      departurePosition: normalizeSimbriefString(wp && wp.DeparturePosition),
    });
  });
  if (normalized.length > 0) {
    var first = normalized[0];
    if (!isDepOrArrType(first.waypointType, "DEP")) {
      first.waypointType = formatDepArrType(
        "DEP",
        fallbackDep || first.name || ""
      );
    }
    var last = normalized[normalized.length - 1];
    if (!isDepOrArrType(last.waypointType, "ARR")) {
      last.waypointType = formatDepArrType(
        "ARR",
        fallbackArr || last.name || ""
      );
    }
  }
  // Meta-Objekt mit allen Flugplan-Metadaten erstellen
  var meta = {
    source: "simbrief",
    departureName: normalizeSimbriefString(plan.DepartureName),
    departureId: normalizeSimbriefString(plan.DepartureID),
    destinationName: normalizeSimbriefString(plan.DestinationName),
    destinationId: normalizeSimbriefString(plan.DestinationID),
    departurePosition: normalizeSimbriefString(plan.DeparturePosition)
  };

  // OFP-Daten direkt in meta extrahieren
  if (ofpData && typeof ofpData === 'object') {
    if (ofpData.Atc || ofpData.atc) {
      var atc = ofpData.Atc || ofpData.atc;
      if (atc.Callsign || atc.callsign) meta.callsign = atc.Callsign || atc.callsign;
    }
    if (ofpData.Aircraft || ofpData.aircraft) {
      var aircraft = ofpData.Aircraft || ofpData.aircraft;
      if (aircraft.Icaocode || aircraft.icaocode) meta.aircraftType = aircraft.Icaocode || aircraft.icaocode;
      if (aircraft.Equip || aircraft.equip) meta.aircraftEquip = aircraft.Equip || aircraft.equip;
    }
    if (ofpData.Times || ofpData.times) {
      var times = ofpData.Times || ofpData.times;
      if (times.Sched_off || times.sched_off) meta.departureTime = times.Sched_off || times.sched_off;
      else if (times.Est_off || times.est_off) meta.departureTime = times.Est_off || times.est_off;
    }
    if (ofpData.Alternate || ofpData.alternate) {
      var alternate = ofpData.Alternate || ofpData.alternate;
      var altIcao = alternate.Icao_code || alternate.icao_code;
      var altName = alternate.Name || alternate.name;
      if (altIcao && altName) meta.alternateAirport = altIcao + ' ' + altName;
      else if (altIcao) meta.alternateAirport = altIcao;
    }
  }

  return {
    waypoints: normalized,
    meta: meta,
    ofp: ofpData  // Include full OFP object for ETA calculation in navlog.js
  };
}

function isDepOrArrType(value, prefix) {
  if (typeof value !== "string" || !value) {
    return false;
  }
  return value.toUpperCase().indexOf(prefix) === 0;
}

function formatDepArrType(prefix, ident) {
  var trimmed = (ident || "").toString().trim();
  if (!trimmed) {
    return prefix;
  }
  return prefix + " " + trimmed;
}

// Long click handler for Navlog tab theme switching
var longClickTimer = null;
var longClickTriggered = false;

/**
 * Initializes the long-click handler for the Navlog tab to switch themes.
 */
function initNavlogLongClick() {
  const navlogBtn = document.getElementById("tabNavlog");
  if (!navlogBtn) {
    logger.warn("Navlog button not found for long-click handler");
    return;
  }

  navlogBtn.addEventListener("mousedown", function (e) {
    longClickTriggered = false;
    longClickTimer = setTimeout(function () {
      longClickTriggered = true;
      switchTheme();
      logger.info("Long-click detected on Navlog tab, switching theme");
    }, 500); // 500ms for long click
  });

  navlogBtn.addEventListener("mouseup", function (e) {
    if (longClickTimer) {
      clearTimeout(longClickTimer);
      longClickTimer = null;
    }
  });

  navlogBtn.addEventListener("mouseleave", function (e) {
    if (longClickTimer) {
      clearTimeout(longClickTimer);
      longClickTimer = null;
    }
  });

  // Prevent double-click if long-click was triggered
  navlogBtn.addEventListener("dblclick", function (e) {
    if (longClickTriggered) {
      e.preventDefault();
      e.stopPropagation();
      longClickTriggered = false;
      return false;
    }
  });
}

// ===== Input Blocking für MSFS =====
/**
 * Verhindert, dass Eingaben an den Simulator weitergegeben werden
 * Verwendet nur Focus-Management ohne Event-Blockierung (für Textfeld-Kompatibilität)
 */
function blockSimulatorInputs() {
  // 1. Setze Body als focusable
  document.body.setAttribute('tabindex', '0');

  // Helper: Prüft ob ein Element ein Input-Feld ist
  function isInputElement(el) {
    if (!el) return false;
    var tagName = el.tagName ? el.tagName.toUpperCase() : '';
    if (tagName === 'INPUT' || tagName === 'TEXTAREA' || tagName === 'SELECT') {
      return true;
    }
    // Prüfe contenteditable
    if (el.isContentEditable) {
      return true;
    }
    return false;
  }

  // 2. Fokussiere das Window kontinuierlich (aber nicht wenn ein Input fokussiert ist)
  function maintainFocus() {
    // Nicht fokussieren wenn ein Input-Element aktiv ist
    if (isInputElement(document.activeElement)) {
      return;
    }
    try {
      window.focus();
      document.body.focus();
    } catch(e) {
      // Ignoriere Fehler
    }
  }

  // Setze initialen Focus
  maintainFocus();

  // PERFORMANCE OPTIMIERT: Focus-Check nur alle 500ms statt 200ms
  // und nur wenn Tab sichtbar ist
  var focusMaintainInterval = null;

  function startFocusMaintain() {
    if (!focusMaintainInterval) {
      focusMaintainInterval = setInterval(maintainFocus, 500);
    }
  }

  function stopFocusMaintain() {
    if (focusMaintainInterval) {
      clearInterval(focusMaintainInterval);
      focusMaintainInterval = null;
    }
  }

  // Starte Focus-Maintain nur wenn Seite sichtbar
  if (!document.hidden) {
    startFocusMaintain();
  }

  // Pausiere bei verstecktem Tab (spart CPU)
  document.addEventListener('visibilitychange', function() {
    if (document.hidden) {
      stopFocusMaintain();
    } else {
      startFocusMaintain();
    }
  });

  // 3. Coherent GT API falls verfügbar
  var coherentInterval = null;
  if (typeof engine !== 'undefined' && engine.trigger) {
    try {
      engine.trigger('FOCUS_INPUT_FIELD');
      // PERFORMANCE OPTIMIERT: 1000ms statt 500ms
      coherentInterval = setInterval(function() {
        if (!document.hidden) {
          try {
            engine.trigger('FOCUS_INPUT_FIELD');
          } catch(e) {}
        }
      }, 1000);
      logger.info("Coherent GT API aktiviert");
    } catch(e) {
      logger.warn("Coherent API not available");
    }
  }

  // 4. Reagiere auf Window-Focus-Verlust (aber nicht wenn ein Input fokussiert ist)
  window.addEventListener('blur', function() {
    // Kurze Verzögerung um zu prüfen ob Focus zu einem Input geht
    setTimeout(function() {
      if (!isInputElement(document.activeElement)) {
        maintainFocus();
      }
    }, 50);
  }, false);

  // 5. Verhindere Kontext-Menü (Rechtsklick)
  document.addEventListener('contextmenu', function(e) {
    e.preventDefault();
  }, false);

  logger.info("Focus-basiertes Input Management für MSFS aktiviert (Meta-Tags + Coherent API)");
}

document.addEventListener("DOMContentLoaded", function () {
  // Blockiere Eingaben an den Simulator
  blockSimulatorInputs();

  // Restore the selected theme and make sure CSS custom properties are in sync.
  const savedStyle = parseInt(localStorage.getItem("style"), 10);
  if (!isNaN(savedStyle) && savedStyle > 0) {
    style = savedStyle;
  }
  document.documentElement.setAttribute("data-style", style);
  loadStyleData();

  // Initialize long-click handler for Navlog tab
  initNavlogLongClick();

  // Re-open the last active tab if possible so the user stays on the same view.
  const lastTab = localStorage.getItem("activeTab") || "navlog";
  try {
    switch (lastTab) {
      case "documents":
        btnDocumentsClicked();
        break;
      case "map":
        btnMapClicked();
        break;
      case "notepad":
        btnNotepadClicked();
        break;
      case "formulas":
        btnFormulasClicked();
        break;
      default:
        btnNavlogClicked();
        break;
    }
    logger.info("Restored active tab:", lastTab);
  } catch (err) {
    logger.error("Failed to restore saved tab, falling back to navlog:", err);
    btnNavlogClicked();
  }

  // Attach the on-screen keyboard when it is available.
  if (window.Keyboard && typeof window.Keyboard.init === "function") {
    window.Keyboard.init();
    document.body.appendChild(window.Keyboard.elements.main);
    logger.info("Virtual keyboard initialised");
  } else {
    logger.error("Virtual keyboard helpers are missing, falling back to inputs");
  }

  // Listen for navlog, document, map and PLN messages from the desktop host.
  window.addEventListener("message", function (e) {
    // NEUE LOGIK: Handle Objekt-Nachrichten (z.B. von map.js)
    if (typeof e.data === 'object' && e.data !== null) {
      if (e.data.type === 'resetFlightplanHash') {
        logger.info('Resetting flightplan hash');
        lastFlightplanHash = null;
      }
      // Für alle Objekt-Nachrichten: nicht weiter verarbeiten mit String-Methoden
      return;
    }

    // Bestehende String-Nachrichten-Logik - nur für Strings
    if (typeof e.data !== 'string') {
      return;
    }
    var sender2 = e.data.substr(0, e.data.indexOf(":"));
    if (sender2 !== "PLN" && lastMessage === e.data) {
      return;
    }
    var message2 = e.data.substr(e.data.indexOf(":") + 1, e.data.length);
    lastMessage = e.data;

    if (sender2 == "navlog") {
        receiving = true;
        if (receiveMessages == true) {
          var values = message2.split("~");
          if (flightType != values[0]) {
            flightType = values[0];
            setFlightType();
          }
          for (var i = 1; i < values.length; i++) {
            try {
              var el = document
                .getElementById("navlog")
                .getElementsByTagName("textarea")[i - 1];
              if (el.matches(":focus")) {
                if (tempValue != values[i]) {
                  if (el.value != values[i]) {
                    el.value = values[i];
                    localStorage.setItem(i - 1, values[i]);
                    localStorage.setItem(flightType, values[0]);
                    valueChanged = true;
                    Keyboard.properties.value = values[1];
                    tempValue = values[1];
                  }
                }
              } else {
                if (el.value != values[i]) {
                  el.value = values[i];
                  localStorage.setItem(i - 1, values[i]);
                  localStorage.setItem(flightType, values[0]);
                  valueChanged = true;
                }
              }
            } catch (syncError) {
              logger.warn("Unable to sync Navlog textarea:", syncError);
            }
          }
        }
        receiving = false;
      } else if (sender2 == "PLN") {
        if (message2 != "") {
          try {
            var plnData = JSON.parse(message2);

            // Prüfen ob kombiniertes Format (pln + ofp + procedures) von Simbrief
            var flightplanRoot = plnData;
            var ofpData = null;
            var proceduresData = null;
            if (plnData && plnData.pln && typeof plnData.pln === 'object') {
              flightplanRoot = plnData.pln;
              ofpData = plnData.ofp || null;
              proceduresData = plnData.procedures || null;
            }

            var flightplan = convertSimbriefFlightplan(flightplanRoot, ofpData, proceduresData);
            logger.info("PLN message processed, has OFP: " + (ofpData !== null) + ", has Procedures: " + (proceduresData !== null));
            mapMessage("Flightplan:" + JSON.stringify(flightplan));
          } catch (parseError) {
            logger.error("Unable to parse SimBrief payload:", parseError);
          }
        }
      } else if (sender2 == "doc") {
        var docsPages = message2.split("_");
        var doc = docsPages[0];
        var docs = docsPages[1];
        var page = docsPages[2];
        var pages = docsPages[3];
        document.getElementById("current_doc").value = doc + "/" + docs;
        document.getElementById("current_page").value = page + "/" + pages;
      } else if (sender2 == "Position") {
        mapMessage("Position:" + message2);
      } else if (sender2 == "map") {
        try {
          if (window.parent && window.parent !== window) {
            window.parent.postMessage(e.data, "*");
          }
        } catch (err) {
          logger.warn("Unable to forward teleport request:", err);
        }
      }
  });

  // ========================================================================
  // PRELOAD: Flugplan im Hintergrund prüfen (BEVOR User zur Map wechselt)
  // ========================================================================
  // Starte Preload sofort - spart Zeit wenn User zur Map wechselt
  if (typeof preloadServerFlightplanCheck === 'function') {
    logger.info('Starting background flightplan preload...');
    preloadServerFlightplanCheck();
  }

  // v1.42: Setze lastFlightplanHash wenn Preload fertig ist
  // Verhindert dass Polling den gleichen Flugplan nochmal vom Server lädt!
  window.addEventListener('kneeboard:preload-complete', function(e) {
    if (e.detail && e.detail.hash) {
      logger.info('Preload complete, setting lastFlightplanHash to:', e.detail.hash);
      lastFlightplanHash = e.detail.hash;
    }
  }, { once: true });

  // v1.32: Legacy setTimeout für loadFlightplanFromServer ENTFERNT
  // Grund: preloadServerFlightplanCheck() (Zeile 662) übernimmt das Laden.
  // Der 2s-Timeout verursachte doppeltes Laden bei leerem LocalCache (BUG 4).

  // Flightplan-Polling ENTFERNT - Flugplan wird NUR bei manuellem Sync geladen
});

const KNEEBOARD_PROXY_FALLBACK = "http://localhost:815";
var lastFlightplanHash = null;

// Hilfsfunktion: Bestimme Proxy Root
function getProxyRoot() {
  var proxyRoot = KNEEBOARD_PROXY_FALLBACK;
  try {
    var origin = window.location.origin;
    if (origin && origin !== "null" && origin !== "file:" && /^(https?:\/\/)/i.test(origin)) {
      proxyRoot = origin;
    }
  } catch (e) {
    // Use fallback
  }
  return proxyRoot;
}

// NEUE FUNKTION: Optimierter Hash-Check vor vollständigem Datenabruf
function checkFlightplanHashAndLoad() {
  try {
    var proxyRoot = getProxyRoot();
    var hashUrl = proxyRoot + "/getFlightplanHash";

    var hashXhr = new XMLHttpRequest();
    hashXhr.open("GET", hashUrl, true);
    hashXhr.onreadystatechange = function() {
      if (hashXhr.readyState !== XMLHttpRequest.DONE) return;

      if (hashXhr.status === 200) {
        try {
          var hashData = JSON.parse(hashXhr.responseText);

          // Keine Daten vorhanden? Silent return
          if (!hashData.exists) {
            return;
          }

          // Hash unverändert? Silent return (verhindert unnötige Requests)
          if (hashData.hash === lastFlightplanHash) {
            return;
          }

          // Hash hat sich geändert: Vollständige Daten laden
          logger.info("[FlightplanPoll] Hash changed, loading full flightplan data");
          // Hash VORHER speichern um Doppel-Requests zu vermeiden
          lastFlightplanHash = hashData.hash;
          loadFlightplanFromServer();
        } catch (e) {
          logger.warn("[FlightplanPoll] Hash check parse error:", e);
        }
      } else if (hashXhr.status !== 0) {
        // Status 0 ist normal beim Beenden, andere Fehler loggen
        logger.warn("[FlightplanPoll] Hash check failed with status:", hashXhr.status);
      }
    };
    hashXhr.onerror = function() {
      // Network error - nicht loggen, da dies bei normalem Betrieb vorkommen kann
    };
    hashXhr.send();
  } catch (e) {
    logger.warn("[FlightplanPoll] Error in hash check:", e);
  }
}

// Load flightplan from server and display on map
function loadFlightplanFromServer() {
  try {
    var proxyRoot = getProxyRoot();
    var url = proxyRoot + "/getFlightplan";
    // Only log when actually loading new data, not on every poll

    var xhr = new XMLHttpRequest();
    xhr.open("GET", url, true);
    xhr.onreadystatechange = function () {
      if (xhr.readyState == XMLHttpRequest.DONE) {

        if (xhr.status == 200) {
          var response = xhr.responseText;

          if (response && response.length > 0) {
            // Check if it starts with PLN:
            if (response.indexOf("PLN:") === 0) {
              response = response.substring(4);
            }

            try {
              // Server-Hash wird nun in checkFlightplanHashAndLoad() geprüft
              // Lokaler Hash nur als Backup
              var currentHash = response.length + "_" + response.substring(0, 100);

              // Only log when we have new data
              logger.info("New flightplan data received, length: " + response.length);

              var plnData = JSON.parse(response);

              // Prüfen ob kombiniertes Format (pln + ofp + procedures) von Simbrief
              logger.debug('plnData keys:', plnData ? Object.keys(plnData) : 'null');
              logger.debug('plnData.procedures?', plnData && plnData.procedures ? 'YES' : 'NO');
              var flightplanRoot = plnData;
              var ofpData = null;
              var proceduresData = null;
              if (plnData && plnData.pln && typeof plnData.pln === 'object') {
                flightplanRoot = plnData.pln;
                ofpData = plnData.ofp || null;
                proceduresData = plnData.procedures || null;
                logger.debug('proceduresData extracted:', proceduresData ? JSON.stringify(proceduresData).substring(0, 300) : 'null');
              }

              if (flightplanRoot && flightplanRoot.FlightPlanFlightPlan) {
                var flightplan = convertSimbriefFlightplan(flightplanRoot, ofpData, proceduresData);
                if (flightplan.waypoints && flightplan.waypoints.length > 0) {
                  // Hash vom Server merken (wird in checkFlightplanHashAndLoad gesetzt)
                  // Lokaler Hash als Fallback
                  if (!lastFlightplanHash) {
                    lastFlightplanHash = currentHash;
                  }

                  // v1.38: Direkt executeFlightplanPath aufrufen wenn neuer Flugplan via Polling ankommt
                  // Dies funktioniert sowohl während Initialisierung als auch nach ready-State
                  if (typeof executeFlightplanPath === 'function') {
                    logger.info("Flightplan loaded via polling, calling executeFlightplanPath");
                    executeFlightplanPath(flightplan);

                    // FIX: Auch Broadcast an Navlog senden mit vollständigen OFP-Daten
                    // (executeFlightplanPath sendet nur an Map, nicht an Navlog)
                    mapMessage("Flightplan:" + JSON.stringify(flightplan));
                    logger.info("Flightplan broadcast to navlog with OFP: " + (flightplan.ofp !== null));
                  } else {
                    logger.error("executeFlightplanPath not available!");
                  }
                } else {
                  logger.info("Flightplan has no waypoints");
                }
              } else {
                logger.info("No FlightPlanFlightPlan in response");
              }
            } catch (e) {
              logger.warn("Error parsing auto-loaded flightplan:", e);
            }
          } else {
            logger.info("Empty flightplan response");
          }
        }
      }
    };
    xhr.onerror = function() {
      logger.warn("Network error loading flightplan");
    };
    xhr.send();
  } catch (e) {
    logger.warn("Error auto-loading flightplan:", e);
  }
}

/**
 * Validates and normalises a proxy URL string (protocol + no trailing slash).
 * @param {string} url
 * @returns {string|null}
 */
function normalizeKneeboardProxyRoot(url) {
  if (!url) {
    return null;
  }
  var value = url.toString().trim();
  if (!value) {
    return null;
  }
  if (!/^(https?:\/\/)/i.test(value)) {
    return null;
  }
  if (value.endsWith("/")) {
    value = value.slice(0, -1);
  }
  return value;
}

/**
 * Resolves the best proxy root based on injected globals, parent window or host.
 */
function getKneeboardProxyRoot() {
  var root = null;
  try {
    if (typeof window !== "undefined") {
      if (window.KneeboardApiProxyUrl) {
        root = normalizeKneeboardProxyRoot(window.KneeboardApiProxyUrl);
      }
      if (
        !root &&
        window.parent &&
        window.parent !== window &&
        window.parent.KneeboardApiProxyUrl
      ) {
        root = normalizeKneeboardProxyRoot(window.parent.KneeboardApiProxyUrl);
      }
    }
  } catch (error) {
    logger.warn("Unable to read KneeboardApiProxyUrl:", error);
  }
  if (!root) {
    try {
      var origin = window.location.origin;
      if (
        origin &&
        origin !== "null" &&
        origin !== "file:" &&
        /^(https?:\/\/)/i.test(origin)
      ) {
        root = normalizeKneeboardProxyRoot(origin);
      }
    } catch (error) {
      logger.warn("Unable to read window.location.origin:", error);
    }
  }
  if (!root) {
    root = normalizeKneeboardProxyRoot(KNEEBOARD_PROXY_FALLBACK);
  }
  if (!root) {
    root = "http://localhost:815";
  }
  return root;
}

/**
 * Resolves a kneeboard-relative path into an absolute URL on the proxy root.
 */
function getKneeboardAssetUrl(path) {
  if (!path) {
    return getKneeboardProxyRoot();
  }
  var normalizedPath = path;
  if (!normalizedPath.startsWith("/")) {
    normalizedPath = "/" + normalizedPath;
  }
  if (normalizedPath.indexOf("{") >= 0 || normalizedPath.indexOf("}") >= 0) {
    return getKneeboardProxyRoot() + normalizedPath;
  }
  return new URL(normalizedPath, getKneeboardProxyRoot() + "/").href;
}

/**
 * Rewrites asset tags (img/script/etc) in fetched HTML with absolute URLs.
 */
function rewriteAssetReferences(html) {
  if (!html) {
    return html;
  }
  var container = document.createElement("div");
  container.innerHTML = html;
  var selector =
    "script[src], link[rel], img[src], source[src], video[src], audio[src], iframe[src]";
  var absoluteTest = /^(?:[a-z]+:)?\/\//i;
  container.querySelectorAll(selector).forEach(function (element) {
    var attrName = element.hasAttribute("src")
      ? "src"
      : element.hasAttribute("href")
      ? "href"
      : null;
    if (!attrName) {
      return;
    }
    var value = element.getAttribute(attrName);
    if (!value) {
      return;
    }
    var trimmed = value.trim();
    if (
      !trimmed ||
      trimmed.startsWith("data:") ||
      trimmed.startsWith("coui:") ||
      trimmed.startsWith("file:") ||
      absoluteTest.test(trimmed)
    ) {
      return;
    }
    if (
      attrName === "href" &&
      element.tagName === "LINK" &&
      !/stylesheet|icon/i.test(element.rel || "")
    ) {
      return;
    }
    element.setAttribute(attrName, getKneeboardAssetUrl(trimmed));
  });
  return container.innerHTML;
}

// ===== Theme/Styles =====
/**
 * Cycles through the available CSS themes by bumping the data-style attribute.
 */
function switchTheme() {
  var styles =
    parseInt(
      window
        .getComputedStyle(document.documentElement)
        .getPropertyValue("--numberOfStyles")
    ) || 1;
  style++;
  if (style > styles) style = 1;
  localStorage.setItem("style", style);
  document.documentElement.setAttribute("data-style", style);
  loadStyleData();
}

/**
 * Reads the CSS custom properties for the current theme and propagates them to
 * the rest of the application (local storage + custom event).
 */
function loadStyleData() {
  // Pull palette values from CSS custom properties
  colorLight = getComputedStyle(document.documentElement)
    .getPropertyValue("--light")
    .trim();
  colorDark = getComputedStyle(document.documentElement)
    .getPropertyValue("--dark")
    .trim();
  fontColorLight = getComputedStyle(document.documentElement)
    .getPropertyValue("--fontLight")
    .trim();
  fontColorDark = getComputedStyle(document.documentElement)
    .getPropertyValue("--fontDark")
    .trim();

  // Ã°Å¸â€™Â¾ Speichern im localStorage
  localStorage.setItem("colorLight", colorLight);
  localStorage.setItem("colorDark", colorDark);
  localStorage.setItem("fontColorLight", fontColorLight);
  // Notify the dynamically loaded tab content so it can restyle itself

  updateTabColors();

  // An geladene Inhalte melden (ersetzt postMessage an iframes)
  document.dispatchEvent(
    new CustomEvent("themechange", {
      detail: { colorLight, colorDark, fontColorLight, fontColorDark },
    })
  );

  logger.info("Theme updated", {
    style,
    colorLight,
    colorDark,
    fontColorLight,
    fontColorDark,
  });
}

/**
 * Applies the active theme colors to all tab buttons and highlights the active tab.
 */
function updateTabColors() {
  const root = getComputedStyle(document.documentElement);
  const dark = root.getPropertyValue("--dark").trim();
  const fontLight = root.getPropertyValue("--fontLight").trim();
  const fontDark = root.getPropertyValue("--fontDark").trim();

  document.querySelectorAll(".tablink").forEach((btn) => {
    btn.style.backgroundColor = dark;
    btn.style.color = fontLight;
  });

  const active = document.querySelector(".tablink.active-tab");
  if (active) {
    active.style.backgroundColor = "white";
    active.style.color = fontDark;
  }
}

/**
 * Streamlined logging helper so tab switch events keep a consistent prefix.
 */
function logTabEvent(tabKey, action, details) {
  try {
    logger.info(TAB_LOG_PREFIX, tabKey, action, details || "");
  } catch (err) {
    logger.warn("Tab logging failed:", err);
  }
}

function hideAllTabContainers(exceptKey) {
  Object.keys(tabContentCache).forEach(function (key) {
    var node = tabContentCache[key];
    if (!node) return;
    node.style.display = key === exceptKey ? "" : "none";
  });
}

/**
 * Injects (or reveals) the HTML fragment for the requested tab. When a tab has been loaded
 * before, we simply re-display the cached DOM to keep imports/running scripts alive.
 */
function loadPageIntoContent(tabKey, onDone) {
  const contentHost = document.getElementById("content");
  if (!contentHost) return;

  const pageFile = PAGE_MAP[tabKey];
  if (!pageFile) {
    logger.warn("Unknown tab key:", tabKey);
    return;
  }

  hideAllTabContainers(tabKey);

  let container = tabContentCache[tabKey];
  if (container && container.dataset.loaded === "true") {
    container.style.display = "";
    logTabEvent(tabKey, "show-cached");
    if (typeof onDone === "function") onDone({ cached: true });
    return;
  }

  if (!container) {
    container = document.createElement("div");
    container.className = "tab-panel";
    container.dataset.tabKey = tabKey;
    contentHost.appendChild(container);
    tabContentCache[tabKey] = container;
  }


  container.style.display = "";
  container.innerHTML = '<div class="loader">Loading...</div>';

  const pageUrl = getKneeboardAssetUrl(pageFile);

  const applyHtml = (html) => {
    container.innerHTML = rewriteAssetReferences(html);
    container.dataset.loaded = "true";
    finalizeLoadedContent();
    logTabEvent(tabKey, "content-loaded");
    if (typeof onDone === "function") onDone({ cached: false });
  };

  if (typeof window.fetch === "function") {
    fetch(pageUrl)
      .then((res) => {
        if (!res.ok) throw new Error(res.status + " " + res.statusText);
        return res.text();
      })
      .then((html) => {
        applyHtml(html);
      })
      .catch((err) => {
        container.innerHTML = `<div class="loader" style="color:#b00">Fehler beim Laden: ${pageFile}<br>${err.message}</div>`;
        logTabEvent(tabKey, "load-error", { message: err.message });
      });
  } else {
    // XHR-Fallback
    const xhr = new XMLHttpRequest();
    xhr.open("GET", pageUrl, true);
    xhr.onreadystatechange = function () {
      if (xhr.readyState === 4) {
        if (xhr.status >= 200 && xhr.status < 300) {
          applyHtml(xhr.responseText);
        } else {
          container.innerHTML = `<div class="loader" style="color:#b00">Fehler beim Laden: ${pageFile}<br>Status ${xhr.status}</div>`;
          logTabEvent(tabKey, "load-error", { status: xhr.status });
        }
      }
    };
    xhr.send();
  }
}

/**
 * Re-dispatches the current theme and rebinds keyboard inputs after a load.
 */
function finalizeLoadedContent() {
  document.dispatchEvent(
    new CustomEvent("themechange", {
      detail: { colorLight, colorDark, fontColorLight, fontColorDark },
    })
  );
  bindKeyboardInputs();
}

/**
 * Hooks every .use-keyboard-input to the shared on-screen keyboard helper.
 */
function bindKeyboardInputs() {
  if (!(window.Keyboard && typeof window.Keyboard.init === "function")) {
    return;
  }
  document.querySelectorAll(".use-keyboard-input").forEach((element) => {
    const reopen = () => {
      window.Keyboard.open(
        element.value,
        (currentValue) => {
          element.value = currentValue;
          try {
            element.dispatchEvent(
              new InputEvent("input", {
                bubbles: true,
                data: currentValue,
                inputType: "insertText",
              })
            );
          } catch (err) {
            const inputEvent = new Event("input", { bubbles: true });
            element.dispatchEvent(inputEvent);
          }
        },
        element
      );
    };
    element.addEventListener("focus", reopen);
    element.addEventListener("click", reopen);
  });
}

/**
 * Loads the Navlog tab and triggers its page-level initialiser when present.
 */
function btnNavlogClicked() {
  // Pause map intervals when leaving map tab (Coherent GT optimization)
  if (activeTab === 'map' && typeof pauseMapIntervals === 'function') {
    pauseMapIntervals();
  }
  logTabEvent("navlog", "switch-request");
  btnNavlog = document.querySelector("#tabNavlog");
  activeTab = "navlog";
  setActiveTabButton(btnNavlog);
  localStorage.setItem("activeTab", activeTab);
  updateTabColors();
  setNavlogOverflow(true);
  loadPageIntoContent(activeTab, function (result) {
    if (result && result.cached === true && tabInitialized.navlog) {
      logTabEvent("navlog", "show-cached");
      // CRITICAL FIX: Even when showing cached tab, apply any pending flightplan data
      // SimBrief may have sent data while user was on another tab
      setTimeout(function () {
        // Reset navlog DOM cache - it may have been null when data arrived
        if (typeof resetNavlogDomCache === "function") {
          resetNavlogDomCache();
        }
        if (typeof applyCachedFlightplanIfReady === "function") {
          applyCachedFlightplanIfReady();
        }
      }, 50);
      return;
    }
    setTimeout(function () {
      if (typeof initNavlogPage === "function") {
        initNavlogPage();
        tabInitialized.navlog = true;
      } else {
        logger.warn("initNavlogPage() is not available");
      }
    }, 100);
  });
}


/**
 * Loads the Documents tab and initialises the PDF/reader helpers.
 */
function btnDocumentsClicked() {
  // Pause map intervals when leaving map tab (Coherent GT optimization)
  if (activeTab === 'map' && typeof pauseMapIntervals === 'function') {
    pauseMapIntervals();
  }
  logTabEvent("documents", "switch-request");
  activeTab = "documents";
  btnDocuments = document.querySelector("#tabDocuments");
  setActiveTabButton(btnDocuments);
  localStorage.setItem("activeTab", activeTab);
  updateTabColors();
  setNavlogOverflow(false);
  loadPageIntoContent(activeTab, function () {
    if (typeof initDocumentsPage === "function") {
      logger.info("Initialising Documents tab content");
      initDocumentsPage();
      // Bind keyboard to dynamically loaded inputs
      bindKeyboardInputs();
    } else {
      logger.warn("initDocumentsPage() is not defined");
    }
  });
  if (keyboardActive && window.Keyboard) {
    Keyboard.close();
  }
}


/**
 * Loads the Notepad tab which hosts editable notes for the pilot.
 */
function btnNotepadClicked() {
  // Pause map intervals when leaving map tab (Coherent GT optimization)
  if (activeTab === 'map' && typeof pauseMapIntervals === 'function') {
    pauseMapIntervals();
  }
  logTabEvent("notepad", "switch-request");
  activeTab = "notepad";
  const btnNotepad = document.querySelector("#tabNotepad");
  setActiveTabButton(btnNotepad);
  localStorage.setItem("activeTab", activeTab);
  updateTabColors();
  setNavlogOverflow(false);
  loadPageIntoContent(activeTab, function (result) {
    if (result && result.cached === true && tabInitialized.notepad) {
      logTabEvent("notepad", "show-cached");
      return;
    }
    if (typeof initNotepadPage === "function") {
      logger.info("Initialising Notepad tab content");
      initNotepadPage();
      tabInitialized.notepad = true;
    } else {
      logger.error("initNotepadPage() is not defined or failed to load");
    }
  });
  if (keyboardActive && window.Keyboard) {
    Keyboard.close();
  }
}


/**
 * Loads the interactive map tab and sets it up once the HTML is injected.
 */
function btnMapClicked() {
  // Verhindere parallele Map-Loads (Race Condition Protection)
  if (mapIsLoading) {
    logger.info("[MapTab] Map already loading, debouncing click");
    return;
  }

  // Prevent double-click interference with single clicks
  // BUT: Allow if it's been more than 500ms since reload started (double-click might be delayed in simulator)
  if (tabReloadInProgress) {
    var timeSinceReloadStart = Date.now() - (window.lastReloadStartTime || 0);
    if (timeSinceReloadStart < 500) {
      logger.info("[MapTab] Tab reload in progress, skipping click");
      return;
    }
  }

  // Debouncing: Bei schnellen Mehrfach-Klicks nur letzten ausführen
  clearTimeout(mapLoadDebounceTimer);
  mapLoadDebounceTimer = setTimeout(function() {
    performMapTabSwitch();
  }, 200); // 200ms Debounce-Zeit (reduziert für bessere Simulator-Kompatibilität)
}

/**
 * Interner Helper: Führt den eigentlichen Map-Tab-Switch durch
 */
function performMapTabSwitch() {
  mapIsLoading = true;

  logTabEvent("map", "switch-request");
  activeTab = "map";
  btnMap = document.querySelector("#tabMap");
  setActiveTabButton(btnMap);
  localStorage.setItem("activeTab", activeTab);
  updateTabColors();
  setNavlogOverflow(false);

  loadPageIntoContent(activeTab, function (result) {
    var canReuseMap = result && result.cached === true && tabInitialized.map;
    if (canReuseMap) {
      logTabEvent("map", "show-cached");
      mapIsLoading = false; // Reset flag
      // Fix: Refresh map after container was hidden (prevents pLineGroup visibility issues)
      if (typeof map !== 'undefined' && map && typeof map.invalidateSize === 'function') {
        setTimeout(function() {
          map.invalidateSize({ pan: false });
        }, 50);
      }
      // Resume map intervals that were paused when leaving the tab (Coherent GT optimization)
      if (typeof resumeMapIntervals === 'function') {
        resumeMapIntervals();
      }
      return;
    }

    // Optional: Cleanup vor Re-Init
    if (typeof cleanupMapIntervals === "function") {
      logger.info("[MapTab] Cleaning up before re-initialization");
      try {
        cleanupMapIntervals();
      } catch (e) {
        logger.warn("[MapTab] Cleanup error:", e);
      }
    }

    // Map initialisieren
    if (typeof initMapPage === "function") {
      logger.info("Initialising Map tab content");
      initMapPage();
      tabInitialized.map = true;
      mapIsLoading = false; // Reset flag nach erfolgreicher Init
    } else {
      logger.warn("initMapPage() is not defined");
      mapIsLoading = false; // Reset auch bei Fehler
    }
  });

  if (keyboardActive && window.Keyboard) {
    Keyboard.close();
  }
}


/**
 * Loads the quick-reference formulas tab.
 */
function btnFormulasClicked() {
  // Pause map intervals when leaving map tab (Coherent GT optimization)
  if (activeTab === 'map' && typeof pauseMapIntervals === 'function') {
    pauseMapIntervals();
  }
  logTabEvent("formulas", "switch-request");
  activeTab = "formulas";
  const btnFormulas = document.querySelector("#tabFormulas");
  setActiveTabButton(btnFormulas);
  localStorage.setItem("activeTab", activeTab);
  updateTabColors();
  setNavlogOverflow(true);
  loadPageIntoContent(activeTab, function (result) {
    if (result && result.cached === true && tabInitialized.formulas) {
      logTabEvent("formulas", "show-cached");
      return;
    }
    if (typeof initFormulasPage === "function") {
      logger.info("Initialising Formulas tab content");
      initFormulasPage();
      tabInitialized.formulas = true;
    } else {
      logger.error("initFormulasPage() is not defined or failed to load");
    }
  });
  if (keyboardActive && window.Keyboard) {
    Keyboard.close();
  }
}


/**
 * Applies the active-tab class to the given button and clears the others.
 */
function setActiveTabButton(btn) {
  document
    .querySelectorAll(".tablink")
    .forEach((t) => t.classList.remove("active-tab"));
  if (btn) btn.classList.add("active-tab");
}

/**
 * Marks the tab content by id/tabName as active and persists the selection.
 */
function setActiveTab(tabName, tabId) {
  document
    .querySelectorAll(".tabcontent")
    .forEach((t) => t.classList.remove("active-tabcontent"));
  // Den aktiven Tab markieren
  document.getElementById(tabId).classList.add("active-tabcontent");

  // Die zuletzt aktive Tab speichern
  localStorage.setItem("activeTab", tabName);
  updateTabColors();
}

/**
 * Toggles scrolling in #content specifically for the Navlog tab.
 */
function setNavlogOverflow(enabled) {
  document.body.classList.toggle("navlog-active", !!enabled);
}

// On-screen keyboard helper used throughout navlog + notepad.
var tempValue = ""; // caches last Navlog value to avoid redundant updates

window.Keyboard = {
  elements: {
    main: null,
    keysContainer: null,
    keys: [],
  },

  eventHandlers: {
    oninput: null,
  },

  properties: {
    value: "",
    capsLock: false,
    targetElement: null,
    autoShiftPending: false,
  },

  init() {
    if (this.elements.main) {
      return;
    }
    // Hauptstruktur erstellen
    this.elements.main = document.createElement("div");
    this.elements.keysContainer = document.createElement("div");

    this.elements.main.classList.add("keyboard", "keyboard--hidden");
    this.elements.keysContainer.classList.add("keyboard__keys");
    this.elements.keysContainer.appendChild(this._createKeys());
    this.elements.main.appendChild(this.elements.keysContainer);
    document.body.appendChild(this.elements.main);

    // Ãƒâ€“ffne Tastatur bei Fokus oder Klick auf Eingabefeld
    document.addEventListener("focusin", (e) => {
      if (
        e.target &&
        e.target.classList &&
        e.target.classList.contains("use-keyboard-input")
      ) {
        // Wenn Tastatur bereits für dieses Element offen ist, Callback nicht überschreiben
        if (this.properties.targetElement === e.target) return;
        const element = e.target;
        this.open(
          element.value,
          (currentValue) => {
            element.value = currentValue;
          },
          element
        );
      }
    });

    document.addEventListener("click", (e) => {
      // Schließe Tastatur nur bei Klick außerhalb der Tastatur oder auf den "Done"-Button
      if (
        this.properties.targetElement &&
        !e.target.classList.contains("keyboard__key") &&
        e.target !== this.properties.targetElement &&
        !e.target.classList.contains("keyboard__key--dark")
      ) {
        this.close();
      }
    });
  },

  _createKeys() {
    const fragment = document.createDocumentFragment();

    const keyLayout = [
      "rwy",
      "sqwk",
      "ent",
      "fl",
      "clear",
      "1",
      "2",
      "3",
      "4",
      "5",
      "6",
      "7",
      "8",
      "9",
      "0",
      ".",
      "backspace",
      "q",
      "w",
      "e",
      "r",
      "t",
      "y",
      "u",
      "i",
      "o",
      "p",
      "a",
      "s",
      "d",
      "f",
      "g",
      "h",
      "j",
      "k",
      "l",
      "enter",
      "caps",
      "z",
      "x",
      "c",
      "v",
      "b",
      "n",
      "m",
      "done",
      "space",
      "clearAll",
    ];

    keyLayout.forEach((key) => {
      const keyElement = document.createElement("button");
      const insertLineBreak = [
        "clear",
        "backspace",
        "p",
        "enter",
        "m",
      ].includes(key);

      keyElement.setAttribute("type", "button");
      keyElement.classList.add("keyboard__key");
      switch (key) {
        // === SONDERFUNKTIONEN ===
        case "rwy":
          keyElement.classList.add(
            "keyboard__key--spezial",
            "keyboard__key--dark"
          );
          keyElement.textContent = "RWY";
          keyElement.addEventListener("click", () =>
            this._insertText("Runway: ")
          );
          break;

        case "sqwk":
          keyElement.classList.add(
            "keyboard__key--spezial",
            "keyboard__key--dark"
          );
          keyElement.textContent = "SQWK";
          keyElement.addEventListener("click", () =>
            this._insertText("Squawk: ")
          );
          break;

        case "ent":
          keyElement.classList.add(
            "keyboard__key--spezial",
            "keyboard__key--dark"
          );
          keyElement.textContent = "ENTR";
          keyElement.addEventListener("click", () =>
            this._insertText("Entry: ")
          );
          break;

        case "fl":
          keyElement.classList.add(
            "keyboard__key--spezial",
            "keyboard__key--dark"
          );
          keyElement.textContent = "FL";
          keyElement.addEventListener("click", () =>
            this._insertText("Flightlevel: ")
          );
          break;

        case "clear":
          keyElement.classList.add(
            "keyboard__key--spezial",
            "keyboard__key--clear"
          );
          keyElement.textContent = "Clear";
          keyElement.addEventListener("click", () => this._clearCurrent());
          break;

        case "clearAll":
          keyElement.classList.add(
            "keyboard__key--wide",
            "keyboard__key--clear"
          );
          keyElement.textContent = "Reset";
          keyElement.addEventListener("click", () => this._clearAll());
          break;

        // === STANDARD-TASTEN ===
        case "backspace":
          keyElement.classList.add("keyboard__key--wide");
          keyElement.textContent = "Bksp";
          keyElement.addEventListener("click", () => this._handleBackspace());
          break;

        case "caps":
          keyElement.classList.add(
            "keyboard__key--wide",
            "keyboard__key--activatable"
          );
          keyElement.textContent = "Shift";
          keyElement.addEventListener("click", () => {
            this._toggleCapsLock();
            keyElement.classList.toggle(
              "keyboard__key--active",
              this.properties.capsLock
            );
          });
          break;

        case "enter":
          keyElement.classList.add("keyboard__key--wide");
          keyElement.textContent = "Enter";
          keyElement.addEventListener("click", () => {
            var target = this.properties.targetElement;
            // Check if target is a METAR input field
            if (target && (target.id === "aeropuerto" || target.id === "aeropuerto2")) {
              this.close();
              // Trigger METAR search
              if (typeof metarSearch === "function") {
                metarSearch();
              } else if (window.metarSearch) {
                window.metarSearch();
              } else {
                var btn = document.getElementById("boton");
                if (btn) btn.click();
              }
            }
            // Check if target is page/doc navigation input
            else if (target && (target.id === "current_page" || target.id === "current_doc")) {
              this.close();
              var rawValue = target.value;
              var desiredValue = parseInt(rawValue, 10);

              if (!isNaN(desiredValue)) {
                if (target.id === "current_page") {
                  if (typeof go_page === "function") {
                    go_page(desiredValue);
                  } else if (window.go_page) {
                    window.go_page(desiredValue);
                  }
                } else if (target.id === "current_doc") {
                  if (typeof go_doc === "function") {
                    go_doc(desiredValue);
                  } else if (window.go_doc) {
                    window.go_doc(desiredValue);
                  }
                }
              }
            }
            else {
              this._insertText("\n");
            }
          });
          break;

        case "space":
          keyElement.classList.add("keyboard__key--extra-wide");
          keyElement.textContent = "Space";
          keyElement.addEventListener("click", () => this._insertText(" "));
          break;

        case "done":
          keyElement.classList.add(
            "keyboard__key--wide",
            "keyboard__key--dark"
          );
          keyElement.textContent = "Close";
          keyElement.addEventListener("click", () => this.close());
          break;

        default:
          keyElement.textContent = key.toLowerCase();
          keyElement.dataset.key = key;
          keyElement.addEventListener("click", () => {
            const useUppercase =
              this.properties.capsLock || this.properties.autoShiftPending;
            const output = useUppercase ? key.toUpperCase() : key.toLowerCase();
            this._insertText(output);
          });
        break;
      }

      fragment.appendChild(keyElement);
      if (insertLineBreak) fragment.appendChild(document.createElement("br"));
    });

    return fragment;
  },

  _insertText(text) {
    const el = this.properties.targetElement;
    if (!el) return;
    var start =
      typeof el.selectionStart === "number"
        ? el.selectionStart
        : el.value.length;
    var end =
      typeof el.selectionEnd === "number" ? el.selectionEnd : el.value.length;
    const before = el.value.slice(0, start);
    const after = el.value.slice(end);
    el.value = before + text + after;
    el.selectionStart = el.selectionEnd = start + text.length;
    this.properties.value = el.value;
    if (typeof el.focus === "function") {
      el.focus();
      if (typeof el.setSelectionRange === "function") {
        const pos = start + text.length;
        el.setSelectionRange(pos, pos);
      }
    }
    this._handleAutoShiftAfterInsert(text);
    this._triggerEvent("oninput");
  },

  _handleBackspace() {
    const el = this.properties.targetElement;
    if (!el) return;
    var start =
      typeof el.selectionStart === "number"
        ? el.selectionStart
        : el.value.length;
    var end =
      typeof el.selectionEnd === "number" ? el.selectionEnd : el.value.length;
    if (start === 0 && end === 0) return;
    let before = el.value.slice(0, start);
    let after = el.value.slice(end);
    if (start === end) before = before.slice(0, -1);
    el.value = before + after;
    el.selectionStart = el.selectionEnd = Math.max(0, start - 1);
    this.properties.value = el.value;
    if (typeof el.focus === "function") {
      el.focus();
      if (typeof el.setSelectionRange === "function") {
        const pos = Math.max(0, start - (start === end ? 1 : 0));
        el.setSelectionRange(pos, pos);
      }
    }
    this._triggerEvent("oninput");
  },

  _clearCurrent() {
    const el = this.properties.targetElement;
    if (!el) return;
    el.value = "";
    this.properties.value = "";
    if (typeof el.focus === "function") {
      el.focus();
      if (typeof el.setSelectionRange === "function") {
        el.setSelectionRange(0, 0);
      }
    }
    this._triggerEvent("oninput");
  },

  _clearAll() {
    // Reset: alle Textfelder auf Platzhalter zurücksetzen
    const activeTab = localStorage.getItem("activeTab");

    // Für Formulas: nutze die existierende clearFormula() Funktion
    if (activeTab === "formulas") {
      if (typeof window.clearFormula === "function" && typeof window.setId2 === "function") {
        // Setze id2 auf 'Reset' um alle Formeln zurückzusetzen
        window.setId2('Reset');
        window.clearFormula();
        window.setId2('');
      } else {
        // Fallback: alle Formulas-Felder direkt zurücksetzen
        const formulaDefaults = {
          'FtDistance': 'Distance (nm)',
          'FtGroundSpeed': 'Ground speed (kts)',
          'COFT': '',
          'IDDOTFH': 'Height difference (ft)',
          'ID': '',
          'SROTTSS': 'True self speed (kts)',
          'RCBA': 'Bank angle (°)',
          'CMBINMB': 'Millibar (mb)',
          'CMBINHG': '',
          'SRCGS': 'GS (kts)',
          'CTIDEG': 'DEG (Grad)',
          'CTIIAS': 'IAS (KIAS)',
          'CTIIAS2': 'IAS (KIAS)',
          'CTIHIF': 'Height (ft)',
          'CTI': '',
          'HWCWD': '( Wind dir. (°)',
          'HWCC': 'Course (°) )',
          'HWCWS': 'Wind speed (kts)',
          'HWC': '',
          'SWCWD': '( Wind dir. (°)',
          'SWCC': 'Course (°) )',
          'SWCWS': 'Wind speed (kts)',
          'SWC': '',
          'WCACC': 'Crosswind component (kts)',
          'WCATSS': 'True self speed (kts)',
          'WCA': '',
          'SROT': '',
          'RC': '',
          'ROD': ''
        };
        Object.entries(formulaDefaults).forEach(([id, val]) => {
          const el = document.getElementById(id);
          if (el) el.value = val;
        });
      }

      // Lösche localStorage für Formulas
      const formulaIds = ["FtDistance", "FtGroundSpeed", "COFT", "IDDOTFH", "ID",
                         "SROTTSS", "RCBA", "RC", "CMBINMB", "CMBINHG", "SRCGS", "CTIDEG",
                         "CTIIAS", "CTIIAS2", "CTIHIF", "CTI", "HWCWD", "HWCC", "HWCWS", "HWC",
                         "SWCWD", "SWCC", "SWCWS", "SWC", "WCACC", "WCATSS", "WCA", "SROT", "ROD"];
      formulaIds.forEach(id => localStorage.removeItem(id));
    } else {
      // Für andere Tabs: alle Textfelder auf Platzhalter zurücksetzen
      const content = document.getElementById("content");
      if (content) {
        content
          .querySelectorAll('input[type="text"], input[type="number"], textarea')
          .forEach((field) => {
            // Prüfe ob das Feld einen data-placeholder Attribut hat
            if (field.dataset.placeholder) {
              field.placeholder = field.dataset.placeholder;
              field.value = "";
            } else if (field.placeholder) {
              // Nutze das placeholder Attribut falls vorhanden, aber als Placeholder anzeigen
              field.value = "";
            } else {
              // Fallback: leeren
              field.value = "";
            }
          });

        // Lösche localStorage für Navlog
        if (activeTab === "navlog") {
          const keysToRemove = [];
          for (let i = 0; i < localStorage.length; i++) {
            const key = localStorage.key(i);
            if (key && !key.includes("activeTab") && !key.includes("Style") && !key.includes("theme")) {
              keysToRemove.push(key);
            }
          }
          keysToRemove.forEach(key => localStorage.removeItem(key));
        }
      }
    }

    // Auch das aktive Feld auf seinen Platzhalter zurücksetzen
    if (this.properties.targetElement) {
      const target = this.properties.targetElement;
      const targetId = target.id;

      if (activeTab === "formulas") {
        // Hole den Platzhalter-Wert für das aktive Feld
        const formulaDefaults = {
          'FtDistance': 'Distance (nm)',
          'FtGroundSpeed': 'Ground speed (kts)',
          'COFT': '',
          'IDDOTFH': 'Height difference (ft)',
          'ID': '',
          'SROTTSS': 'True self speed (kts)',
          'RCBA': 'Bank angle (°)',
          'CMBINMB': 'Millibar (mb)',
          'CMBINHG': '',
          'SRCGS': 'GS (kts)',
          'CTIDEG': 'DEG (Grad)',
          'CTIIAS': 'IAS (KIAS)',
          'CTIIAS2': 'IAS (KIAS)',
          'CTIHIF': 'Height (ft)',
          'CTI': '',
          'HWCWD': '( Wind dir. (°)',
          'HWCC': 'Course (°) )',
          'HWCWS': 'Wind speed (kts)',
          'HWC': '',
          'SWCWD': '( Wind dir. (°)',
          'SWCC': 'Course (°) )',
          'SWCWS': 'Wind speed (kts)',
          'SWC': '',
          'WCACC': 'Crosswind component (kts)',
          'WCATSS': 'True self speed (kts)',
          'WCA': '',
          'SROT': '',
          'RC': '',
          'ROD': ''
        };

        if (formulaDefaults.hasOwnProperty(targetId)) {
          this.properties.value = formulaDefaults[targetId];
        } else {
          this.properties.value = "";
        }
      } else {
        // Für andere Tabs
        if (target.dataset && target.dataset.placeholder) {
          target.placeholder = target.dataset.placeholder;
          this.properties.value = "";
        } else if (target.placeholder) {
          this.properties.value = "";
        } else {
          this.properties.value = "";
        }
      }
    } else {
      this.properties.value = "";
    }

    this._triggerEvent("oninput");

    // Trigger calculate() falls auf Formulas-Seite
    if (typeof window.calculate === "function") {
      window.calculate();
    }
  },

  _toggleCapsLock() {
    this.properties.autoShiftPending = false;
    this._setCapsLockState(!this.properties.capsLock);
    // Optional: Keylabels aktualisieren
  },

  _triggerEvent(handlerName) {
    if (typeof this.eventHandlers[handlerName] === "function") {
      this.eventHandlers[handlerName](this.properties.value);
    }
    if (handlerName === "oninput" && this.properties.targetElement) {
      const target = this.properties.targetElement;
      const inputEvent = new Event("input", { bubbles: true });
      target.dispatchEvent(inputEvent);
    }
  },

  open(initialValue, oninput, element) {
    if (!this.elements.main) {
      this.init();
      if (!this.elements.main) {
        logger.error("Keyboard.init() failed - cannot open keyboard.");
        return;
      }
    }
    this.properties.value = initialValue || "";
    this.eventHandlers.oninput = oninput;
    this.properties.targetElement = element;
    this._enableAutoShift();
    this._refreshKeyLabels();
    this.elements.main.classList.remove("keyboard--hidden");
    // Content über der Tastatur verkleinern (deferred, damit Click-Events korrekt feuern)
    var self = this;
    clearTimeout(this._kbLayoutTimeout);
    this._kbLayoutTimeout = setTimeout(function() {
      var kbHeight = self.elements.main.offsetHeight;
      if (kbHeight > 0) {
        var viewport = document.getElementById('appViewport');
        if (viewport) {
          var totalHeight = window.innerHeight || document.documentElement.clientHeight;
          viewport.style.height = (totalHeight - kbHeight) + 'px';
          viewport.style.flex = 'none';
        }
      }
    }, 80);
    if (element && typeof element.focus === "function") {
      element.focus();
      var isRangeInput =
        element.tagName === "INPUT" &&
        typeof element.type === "string" &&
        element.type.toLowerCase() === "range";
      if (!isRangeInput && typeof element.setSelectionRange === "function") {
        const pos = element.value.length;
        try {
          element.setSelectionRange(pos, pos);
        } catch (rangeError) {
          logger.warn("Unable to set selection on element:", rangeError);
        }
      }
    }
    keyboardActive = true;
  },

  close() {
    if (!this.elements.main) {
      return;
    }
    this.properties.value = "";
    this.properties.autoShiftPending = false;
    this._setCapsLockState(false);
    this.eventHandlers.oninput = null;
    this.properties.targetElement = null;
    this.elements.main.classList.add("keyboard--hidden");
    clearTimeout(this._kbLayoutTimeout);
    var viewport = document.getElementById('appViewport');
    if (viewport) {
      viewport.style.height = '';
      viewport.style.flex = '';
    }
    keyboardActive = false;
  },

  _handleAutoShiftAfterInsert(text) {
    var inserted =
      typeof text === "string" ? text : text !== null && text !== undefined ? String(text) : "";
    if (!inserted) {
      return;
    }
    if (inserted.indexOf(".") !== -1) {
      this._enableAutoShift();
      return;
    }
    if (this.properties.autoShiftPending) {
      this._disableAutoShift();
    }
  },

  _enableAutoShift() {
    this.properties.autoShiftPending = true;
    this._setCapsLockState(true);
    this._refreshKeyLabels();
  },

  _disableAutoShift() {
    this.properties.autoShiftPending = false;
    this._setCapsLockState(false);
    this._refreshKeyLabels();
  },

  _setCapsLockState(on) {
    this.properties.capsLock = !!on;
    if (this.elements && this.elements.keysContainer) {
      var capsKey = this.elements.keysContainer.querySelector(".keyboard__key--activatable");
      if (capsKey) {
        capsKey.classList.toggle("keyboard__key--active", this.properties.capsLock);
      }
    }
    this._refreshKeyLabels();
  },

  _refreshKeyLabels() {
    if (!(this.elements && this.elements.keysContainer)) {
      return;
    }
    var useUppercase = this.properties.capsLock || this.properties.autoShiftPending;
    this.elements.keysContainer.querySelectorAll(".keyboard__key").forEach((btn) => {
      var raw = btn.dataset && btn.dataset.key ? btn.dataset.key : null;
      if (!raw || raw.length !== 1 || !/[a-z]/i.test(raw)) {
        return;
      }
      btn.textContent = useUppercase ? raw.toUpperCase() : raw.toLowerCase();
    });
  },
};

// ===== Messaging-Receiver Toggle =====
var messageReceiverTimeout;
/**
 * Re-enables message syncing after a short debounce.
 */
function enableMessageReceiver() {
  clearTimeout(messageReceiverTimeout);
  messageReceiverTimeout = setTimeout(function () {
    receiveMessages = true;
  }, 1000);
}
/**
 * Pauses message syncing immediately (usually while typing locally).
 */
function disableMessageReceiver() {
  clearTimeout(messageReceiverTimeout);
  receiveMessages = false;
}
var lastMessage = "";
var receiving = false;
var valueChanged = false;

