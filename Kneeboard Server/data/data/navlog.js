// ============================================================================
// VERSION MARKER
// ============================================================================
var NAVLOG_JS_VERSION = "2025-12-19-v2.0";
var NAVLOG_DEBUG = false;  // Set to true for verbose logging

// Zentraler Logger - nutzt KneeboardLogger falls verfügbar
var navlogLogger = (typeof KneeboardLogger !== 'undefined')
	? KneeboardLogger.createLogger('Navlog', { minLevel: NAVLOG_DEBUG ? 'DEBUG' : 'INFO' })
	: { info: function(){}, warn: console.warn.bind(console), error: console.error.bind(console) };

// ============================================================================
// GLOBAL VARIABLES
// ============================================================================
var scroled = false;
var colorLight;
var colorDark;
var flightType = '';
var saveVal;
var btnIFR;
var btnVFR;
var btnZY;
var saveTimeout;
var sendValue;
var timeout;
var waypointIdCounters = Object.create(null);
var cachedFlightplanData = null;
var cachedFlightplanMeta = {};
var cachedOfpData = null;  // OFP-Daten für Mapping bei DOM-Ready
var ofpMappingInProgress = false;  // Flag: OFP-Mapping läuft - Server-Sync blockieren
var flightplanLogPrefix = '[Navlog Flightplan]';
var waypointFixedCountSettings = {
	vfr: 5,
	ifr: 5,
	zy: 5
};

// ============================================================================
// CONSTANTS
// ============================================================================
var SAVE_DEBOUNCE_MS = 2000;
var SAVE_STORAGE_DELAY_MS = 500;
var MESSAGE_RECEIVER_TIMEOUT_MS = 2000;
var KEYDOWN_SAVE_DELAY_MS = 1000;
var SAFETY_LOOP_LIMIT = 200;
var IMMEDIATE_SAVE_THROTTLE_MS = 200;
var lastImmediateSave = 0;
var navlogSyncChannel = null;
var navlogSyncId = null;
var lastBroadcastNavlogHash = null;
var lastAppliedNavlogHash = null;
var lastLocalNavlogHash = null;
var NAVLOG_SYNC_FALLBACK = "http://localhost:815";
var navlogServerPushTimeout = null;
var NAVLOG_PUSH_DELAY_MS = 500;
var NAVLOG_PULL_INTERVAL_MS = 4000; // etwas entspannteres Polling
var lastServerNavlogHash = null;
var lastServerTimestamp = 0;
var lastLocalTimestamp = 0;
var initialNavlogLoadComplete = false; // Flag to track if initial load is done
var suppressNavlogPush = false;
var navlogEventListenersInitialized = false; // Prevent duplicate event listeners on rapid tab switching
var receiveMessages = true; // allow incoming sync by default in browser mirror
var NAVLOG_VERBOSE = false; // optional verbose logging toggle
var waypointTemplatesCache = { vfr: [], ifr: [], zy: [] };

// ============================================================================
// METAR/TAF PARSER FOR WEATHER LOG
// ============================================================================

/**
 * Parst eine METAR-Meldung und extrahiert relevante Wetterdaten für das Weather Log
 * @param {string} metar - Die METAR-Meldung
 * @returns {object} - Objekt mit geparsten Wetterdaten
 */
function parseMetarForWeatherLog(metar) {
	if (!metar || typeof metar !== 'string') {
		return null;
	}
	var result = {
		raw: metar,
		wind: '',
		visibility: '',
		ceiling: '',
		weather: '',
		temperature: '',
		qnh: '',
		icing: '',
		turbulence: ''
	};

	try {
		// Wind: z.B. "27015G25KT" oder "VRB03KT" oder "00000KT"
		var windMatch = metar.match(/\b(\d{3}|VRB)(\d{2,3})(G(\d{2,3}))?KT\b/);
		if (windMatch) {
			var dir = windMatch[1];
			var speed = windMatch[2];
			var gust = windMatch[4];
			if (dir === 'VRB') {
				result.wind = 'VRB/' + speed + 'kt';
			} else {
				result.wind = dir + '°/' + speed + 'kt';
			}
			if (gust) {
				result.wind += 'G' + gust;
			}
		}

		// Sichtweite: z.B. "9999" (Meter) oder "10SM" (Statute Miles)
		var visMatch = metar.match(/\s(\d{4})\s/) || metar.match(/\s(\d+)SM\b/);
		if (visMatch) {
			var vis = visMatch[1];
			if (vis === '9999') {
				result.visibility = '>10km';
			} else if (metar.indexOf('SM') > -1) {
				result.visibility = vis + 'SM';
			} else {
				result.visibility = (parseInt(vis) / 1000).toFixed(1) + 'km';
			}
		}

		// Wolken/Ceiling: NUR BKN, OVC, VV zählen als Ceiling (NICHT FEW, SCT!)
		// FEW und SCT sind keine Ceiling-Bedingungen nach ICAO-Definition
		var ceilingLayers = [];
		var cloudRegex = /(BKN|OVC|VV)(\d{3})(CB|TCU)?/g;
		var cloudMatch;
		while ((cloudMatch = cloudRegex.exec(metar)) !== null) {
			var coverage = cloudMatch[1];
			var height = parseInt(cloudMatch[2]) * 100; // In Fuß
			var type = cloudMatch[3] || '';
			ceilingLayers.push(coverage + height + type);
		}
		if (ceilingLayers.length > 0) {
			result.ceiling = ceilingLayers.join(' ');
		} else if (metar.indexOf('CAVOK') > -1 || metar.indexOf('SKC') > -1 || metar.indexOf('CLR') > -1 || metar.indexOf('NSC') > -1) {
			result.ceiling = 'CAVOK';
		}

		// Wetter-Phänomene: RA, SN, FG, BR, TS, etc.
		var wxRegex = /\s([+-]?)(VC)?(MI|PR|BC|DR|BL|SH|TS|FZ)?(DZ|RA|SN|SG|IC|PL|GR|GS|UP|BR|FG|FU|VA|DU|SA|HZ|PO|SQ|FC|SS|DS)+\s/g;
		var wxPhenomena = [];
		var wxMatch;
		while ((wxMatch = wxRegex.exec(' ' + metar + ' ')) !== null) {
			wxPhenomena.push(wxMatch[0].trim());
		}
		if (wxPhenomena.length > 0) {
			result.weather = wxPhenomena.join(' ');
		}

		// Temperatur/Taupunkt: z.B. "08/02" oder "M02/M05"
		var tempMatch = metar.match(/\s(M?\d{2})\/(M?\d{2})\s/);
		if (tempMatch) {
			var temp = tempMatch[1].replace('M', '-');
			var dewpoint = tempMatch[2].replace('M', '-');
			result.temperature = temp + '°C/' + dewpoint + '°C';
		}

		// QNH: z.B. "Q1013" oder "A2992"
		var qnhMatch = metar.match(/\b[QA](\d{4})\b/);
		if (qnhMatch) {
			if (metar.indexOf('Q' + qnhMatch[1]) > -1) {
				result.qnh = qnhMatch[1] + 'hPa';
			} else {
				// Inches of mercury (A2992 = 29.92 inHg)
				result.qnh = (parseInt(qnhMatch[1]) / 100).toFixed(2) + 'inHg';
			}
		}

		// Icing/Turbulence Hinweise aus METAR ableiten (ICAO-Abkürzungen)
		// Severity: NIL, LGT (Light), MOD (Moderate), SEV (Severe)
		var icingSeverity = 'NIL';
		var turbSeverity = 'NIL';

		// Thunderstorm = SEV für beides
		if (metar.indexOf('TS') > -1) {
			turbSeverity = 'SEV';
			icingSeverity = 'SEV';
		}
		// CB (Cumulonimbus) = SEV turb, MOD-SEV icing
		else if (metar.indexOf('CB') > -1) {
			turbSeverity = 'SEV';
			icingSeverity = 'MOD-SEV';
		}
		// TCU (Towering Cumulus) = MOD turb, MOD icing
		else if (metar.indexOf('TCU') > -1) {
			turbSeverity = 'MOD';
			icingSeverity = 'MOD';
		}
		// Freezing precipitation = SEV icing
		if (metar.indexOf('FZRA') > -1) {
			icingSeverity = 'SEV';
		} else if (metar.indexOf('FZDZ') > -1 && icingSeverity !== 'SEV') {
			icingSeverity = 'MOD';
		} else if (metar.indexOf('FZFG') > -1 && icingSeverity === 'NIL') {
			icingSeverity = 'LGT';
		}
		// Freezing level aus Temperatur ableiten
		if (tempMatch && icingSeverity === 'NIL') {
			var tempVal = parseInt(tempMatch[1].replace('M', '-'));
			if (tempVal <= 0) {
				icingSeverity = 'LGT';
			}
		}
		// Starker Wind = Turbulenz
		if (windMatch && turbSeverity === 'NIL') {
			var windSpeed = parseInt(windMatch[2]);
			var gustSpeed = windMatch[4] ? parseInt(windMatch[4]) : 0;
			if (gustSpeed >= 40 || windSpeed >= 30) {
				turbSeverity = 'MOD';
			} else if (gustSpeed >= 25 || windSpeed >= 20) {
				turbSeverity = 'LGT';
			}
		}

		result.icing = icingSeverity;
		result.turbulence = turbSeverity;

	} catch (e) {
		console.warn('[METAR Parser] Error parsing METAR:', e);
	}

	return result;
}

/**
 * Parst eine TAF-Meldung und extrahiert Vorhersagedaten
 * @param {string} taf - Die TAF-Meldung
 * @returns {object} - Objekt mit geparsten Vorhersagedaten
 */
function parseTafForWeatherLog(taf) {
	if (!taf || typeof taf !== 'string') {
		return null;
	}
	var result = {
		raw: taf,
		summary: '',
		wind: '',
		visibility: '',
		ceiling: '',
		weather: ''
	};

	try {
		// Extrahiere die Hauptvorhersage (erste Zeile nach TAF)
		var lines = taf.split(/\s+(?=TEMPO|BECMG|PROB|FM\d{6})/);
		if (lines.length > 0) {
			var mainForecast = lines[0];

			// Wind aus TAF
			var windMatch = mainForecast.match(/\b(\d{3}|VRB)(\d{2,3})(G(\d{2,3}))?KT\b/);
			if (windMatch) {
				var dir = windMatch[1];
				var speed = windMatch[2];
				var gust = windMatch[4];
				result.wind = dir + '°/' + speed + 'kt';
				if (gust) {
					result.wind += ' G' + gust;
				}
			}

			// Sichtweite
			var visMatch = mainForecast.match(/\s(\d{4})\s/) || mainForecast.match(/\s(\d+)SM\b/);
			if (visMatch) {
				var vis = visMatch[1];
				if (vis === '9999') {
					result.visibility = '>10km';
				} else if (taf.indexOf('SM') > -1) {
					result.visibility = vis + 'SM';
				} else {
					result.visibility = (parseInt(vis) / 1000).toFixed(1) + 'km';
				}
			}

			// Wolken
			var cloudLayers = [];
			var cloudRegex = /(FEW|SCT|BKN|OVC|VV)(\d{3})(CB|TCU)?/g;
			var cloudMatch;
			while ((cloudMatch = cloudRegex.exec(mainForecast)) !== null) {
				var coverage = cloudMatch[1];
				var height = parseInt(cloudMatch[2]) * 100;
				var type = cloudMatch[3] || '';
				cloudLayers.push(coverage + height + type);
			}
			if (cloudLayers.length > 0) {
				result.ceiling = cloudLayers.join(' ');
			} else if (mainForecast.indexOf('CAVOK') > -1 || mainForecast.indexOf('SKC') > -1 || mainForecast.indexOf('NSC') > -1) {
				result.ceiling = 'CAVOK';
			}

			// Wetter-Phänomene aus TAF
			var wxRegex = /\s([+-]?)(VC)?(MI|PR|BC|DR|BL|SH|TS|FZ)?(DZ|RA|SN|SG|IC|PL|GR|GS|UP|BR|FG|FU|VA|DU|SA|HZ|PO|SQ|FC|SS|DS)+\s/g;
			var wxPhenomena = [];
			var wxMatch;
			while ((wxMatch = wxRegex.exec(' ' + mainForecast + ' ')) !== null) {
				wxPhenomena.push(wxMatch[0].trim());
			}
			if (wxPhenomena.length > 0) {
				result.weather = wxPhenomena.join(' ');
			}
		}

		// Zusammenfassung: Gibt es TEMPO oder PROB Änderungen?
		var hasChanges = [];
		if (taf.indexOf('TEMPO') > -1) hasChanges.push('TEMPO');
		if (taf.indexOf('BECMG') > -1) hasChanges.push('BECMG');
		if (taf.indexOf('PROB') > -1) hasChanges.push('PROB');
		if (hasChanges.length > 0) {
			result.summary = hasChanges.join('/');
		}

	} catch (e) {
		console.warn('[TAF Parser] Error parsing TAF:', e);
	}

	return result;
}

// ============================================================================
// OFP MAPPING HELPER FUNCTIONS
// ============================================================================

/**
 * Holt einen verschachtelten Wert aus einem Objekt via Punkt-Notation
 * z.B. getNestedValue(ofp, 'Origin.Elevation') → ofp.Origin.Elevation
 */
function getNestedValue(obj, path) {
	if (!obj || !path) return undefined;

	var parts = path.split('.');
	var current = obj;

	for (var i = 0; i < parts.length; i++) {
		if (current === null || current === undefined) return undefined;
		current = current[parts[i]];
	}

	return current;
}

/**
 * Holt Navlog Fixes aus OFP-Daten
 */
function getNavlogFixes(ofp) {
	if (!ofp) return null;
	var navlog = ofp.Navlog || ofp.navlog;
	if (!navlog) return null;

	var fixes = navlog.Fix || navlog.fix;
	return Array.isArray(fixes) ? fixes : null;
}

/**
 * Verarbeitet das OFP basierend auf der Mapping-Konfiguration
 * Verwendet die globalen OFP_NAVLOG_MAPPING, OFP_COMPUTED_FIELDS und OFP_PARSERS
 */
function processOFPWithMapping(ofp, waypoints) {
	// Prüfen ob Mapping-Konfiguration geladen ist
	if (typeof OFP_NAVLOG_MAPPING === 'undefined') {
		console.warn('[OFP Mapping] OFP_NAVLOG_MAPPING not defined - skipping mapping');
		return {};
	}

	var result = {};

	// 1. Berechnete Felder zuerst verarbeiten
	var computed = {};
	if (typeof OFP_COMPUTED_FIELDS !== 'undefined') {
		for (var compKey in OFP_COMPUTED_FIELDS) {
			if (OFP_COMPUTED_FIELDS.hasOwnProperty(compKey)) {
				try {
					computed[compKey] = OFP_COMPUTED_FIELDS[compKey](ofp, waypoints);
				} catch (e) {
					console.warn('[OFP Mapping] Error computing field', compKey, e);
				}
			}
		}
	}

	// 2. Mapping durchgehen
	for (var fieldId in OFP_NAVLOG_MAPPING) {
		if (!OFP_NAVLOG_MAPPING.hasOwnProperty(fieldId)) continue;

		var config = OFP_NAVLOG_MAPPING[fieldId];

		// Skip wenn deaktiviert
		if (config.enabled === false) continue;

		var value = null;

		// Wert aus source holen
		if (config.source) {
			if (config.source.indexOf('_computed.') === 0) {
				// Berechneter Wert
				var compField = config.source.replace('_computed.', '');
				value = computed[compField];
			} else {
				// Direkter OFP-Pfad
				value = getNestedValue(ofp, config.source);
			}
		}

		// Fallback auf alternative Quellen
		if ((value === undefined || value === null) && config.sourceAlt) {
			for (var a = 0; a < config.sourceAlt.length; a++) {
				var altPath = config.sourceAlt[a];
				if (altPath.indexOf('_computed.') === 0) {
					var altCompField = altPath.replace('_computed.', '');
					value = computed[altCompField];
				} else {
					value = getNestedValue(ofp, altPath);
				}
				if (value !== undefined && value !== null) break;
			}
		}

		// Skip wenn kein Wert
		if (value === undefined || value === null) continue;

		// Parser anwenden
		if (config.parse) {
			try {
				if (typeof config.parse === 'function') {
					value = config.parse(value, ofp);
				} else if (typeof config.parse === 'string' && typeof OFP_PARSERS !== 'undefined' && OFP_PARSERS[config.parse]) {
					value = OFP_PARSERS[config.parse](value, ofp);
				}
			} catch (e) {
				console.warn('[OFP Mapping] Error parsing field', fieldId, e);
				continue;
			}
		}

		// Skip wenn Parser null zurückgibt
		if (value === undefined || value === null) continue;

		// Format anwenden
		if (config.format && typeof value !== 'undefined') {
			value = config.format.replace('{value}', value);
		}

		// In Result speichern
		result[fieldId] = value;
	}

	NAVLOG_DEBUG && console.log('[OFP Mapping] Processed', Object.keys(result).length, 'fields');
	NAVLOG_DEBUG && console.log('[OFP Mapping] Result keys:', Object.keys(result));
	// Debug: Spezifische Felder loggen
	if (result['Arrival-Star']) {
		console.log('[OFP Mapping] Arrival-Star =', result['Arrival-Star']);
	} else {
		console.log('[OFP Mapping] Arrival-Star is EMPTY - checking ALL OFP paths...');
		console.log('[OFP Mapping] - General.Star_name =', getNestedValue(ofp, 'General.Star_name'));
		console.log('[OFP Mapping] - General.star_name =', getNestedValue(ofp, 'General.star_name'));
		console.log('[OFP Mapping] - General.Star =', getNestedValue(ofp, 'General.Star'));
		console.log('[OFP Mapping] - General.star =', getNestedValue(ofp, 'General.star'));
		console.log('[OFP Mapping] - Atc.Star =', getNestedValue(ofp, 'Atc.Star'));
		console.log('[OFP Mapping] - Destination.Star =', getNestedValue(ofp, 'Destination.Star'));
		console.log('[OFP Mapping] - Destination.star =', getNestedValue(ofp, 'Destination.star'));
		console.log('[OFP Mapping] - _computed.star =', computed['star']);
		// Zeige alle Keys aus verschiedenen Objekten
		if (ofp.General) {
			console.log('[OFP Mapping] - General ALL KEYS:', Object.keys(ofp.General));
			var starKeys = Object.keys(ofp.General).filter(function(k) {
				return k.toLowerCase().indexOf('star') !== -1;
			});
			if (starKeys.length) {
				console.log('[OFP Mapping] - General STAR-related keys:', starKeys);
				starKeys.forEach(function(k) {
					console.log('[OFP Mapping] - General.' + k + ' =', ofp.General[k]);
				});
			}
		}
		if (ofp.Atc) {
			console.log('[OFP Mapping] - Atc ALL KEYS:', Object.keys(ofp.Atc));
		}
		if (ofp.Destination) {
			console.log('[OFP Mapping] - Destination ALL KEYS:', Object.keys(ofp.Destination));
		}
	}
	if (result['Weather-Log-Enr-Winds']) {
		console.log('[OFP Mapping] Weather-Log-Enr-Winds =', result['Weather-Log-Enr-Winds'].substring(0, 100));
	}
	// Debug: Pilot_in_com
	if (result['Pilot_in_com']) {
		console.log('[OFP Mapping] Pilot_in_com =', result['Pilot_in_com']);
	} else {
		console.log('[OFP Mapping] Pilot_in_com is EMPTY - checking OFP structure...');
		console.log('[OFP Mapping] - OFP top-level keys:', Object.keys(ofp));
		if (ofp.Crew) console.log('[OFP Mapping] - Crew keys:', Object.keys(ofp.Crew));
		if (ofp.Params) console.log('[OFP Mapping] - Params keys:', Object.keys(ofp.Params));
		if (ofp.General) console.log('[OFP Mapping] - General keys:', Object.keys(ofp.General));
	}
	return result;
}

/**
 * Wendet die gemappten Werte auf NavLog-Felder an
 */
function applyMappedValuesToNavlog(mappedValues) {
	var count = 0;
	for (var fieldId in mappedValues) {
		if (mappedValues.hasOwnProperty(fieldId)) {
			setNavlogFieldValue(fieldId, String(mappedValues[fieldId]));
			count++;
		}
	}
	NAVLOG_DEBUG && console.log('[OFP Mapping] Applied', count, 'values to NavLog fields');
}

/**
 * Reichert Waypoints mit OFP Navlog Fix-Daten an (Wetter, Fuel, etc.)
 */
function enrichWaypointsWithOFP(waypoints, ofpData) {
	if (!Array.isArray(waypoints) || !waypoints.length || !ofpData) return;

	var fixes = getNavlogFixes(ofpData);
	if (!fixes) return;

	NAVLOG_DEBUG && console.log('[OFP Enrichment] Merging data from', fixes.length, 'fixes to', waypoints.length, 'waypoints');

	// DEBUG: Ersten Fix komplett ausgeben um alle verfügbaren Felder zu sehen
	if (fixes.length > 0) {
		console.log('[OFP Enrichment] FIRST FIX ALL KEYS:', Object.keys(fixes[0]));
		console.log('[OFP Enrichment] FIRST FIX DATA:', JSON.stringify(fixes[0], null, 2));
	}

	// Map für schnelles Lookup nach Ident
	var fixByIdent = {};
	for (var f = 0; f < fixes.length; f++) {
		var fix = fixes[f];
		var ident = fix.Ident || fix.ident;
		if (ident) {
			fixByIdent[ident.toUpperCase()] = fix;
		}
	}

	// Daten auf Waypoints anwenden
	var matchCount = 0;
	var noMatchList = [];
	for (var w = 0; w < waypoints.length; w++) {
		var wp = waypoints[w];
		var wpName = wp.name || wp.Name || wp.ident || wp.Ident || '';
		if (wpName) {
			var matchingFix = fixByIdent[wpName.toUpperCase()];
			if (matchingFix) {
				matchCount++;
				// Wind
				if (matchingFix.Wind_dir !== undefined) wp.Wind_dir = matchingFix.Wind_dir;
				else if (matchingFix.wind_dir !== undefined) wp.Wind_dir = matchingFix.wind_dir;
				if (matchingFix.Wind_spd !== undefined) wp.Wind_spd = matchingFix.Wind_spd;
				else if (matchingFix.wind_spd !== undefined) wp.Wind_spd = matchingFix.wind_spd;
				// Temperature
				if (matchingFix.Oat !== undefined) wp.Oat = matchingFix.Oat;
				else if (matchingFix.oat !== undefined) wp.Oat = matchingFix.oat;
				// Ground Speed
				if (matchingFix.Groundspeed !== undefined) wp.Groundspeed = matchingFix.Groundspeed;
				else if (matchingFix.groundspeed !== undefined) wp.Groundspeed = matchingFix.groundspeed;
				else if (matchingFix.Ground_spd !== undefined) wp.Groundspeed = matchingFix.Ground_spd;
				else if (matchingFix.ground_spd !== undefined) wp.Groundspeed = matchingFix.ground_spd;
				// ETE
				if (matchingFix.Ete !== undefined) wp.Ete = matchingFix.Ete;
				else if (matchingFix.ete !== undefined) wp.Ete = matchingFix.ete;
				else if (matchingFix.Time_leg !== undefined) wp.Ete = matchingFix.Time_leg;
				else if (matchingFix.time_leg !== undefined) wp.Ete = matchingFix.time_leg;
				// ETA
				if (matchingFix.Eta !== undefined) wp.Eta = matchingFix.Eta;
				else if (matchingFix.eta !== undefined) wp.Eta = matchingFix.eta;
				else if (matchingFix.Time_total !== undefined) wp.Eta = matchingFix.Time_total;
				else if (matchingFix.time_total !== undefined) wp.Eta = matchingFix.time_total;
				// Fuel Flow
				if (matchingFix.Fuel_flow !== undefined) wp.Fuel_flow = matchingFix.Fuel_flow;
				else if (matchingFix.fuel_flow !== undefined) wp.Fuel_flow = matchingFix.fuel_flow;
				// Fuel Burn
				if (matchingFix.Fuel_burn !== undefined) wp.Fuel_burn = matchingFix.Fuel_burn;
				else if (matchingFix.fuel_burn !== undefined) wp.Fuel_burn = matchingFix.fuel_burn;
				else if (matchingFix.Fuel_leg !== undefined) wp.Fuel_burn = matchingFix.Fuel_leg;
				else if (matchingFix.fuel_leg !== undefined) wp.Fuel_burn = matchingFix.fuel_leg;
				// Fuel Remaining (Simbrief uses Fuel_plan_onboard)
				if (matchingFix.Fuel_plan_onboard !== undefined) wp.Fuel_rem = matchingFix.Fuel_plan_onboard;
				else if (matchingFix.fuel_plan_onboard !== undefined) wp.Fuel_rem = matchingFix.fuel_plan_onboard;
				else if (matchingFix.Fuel_min_onboard !== undefined) wp.Fuel_rem = matchingFix.Fuel_min_onboard;
				else if (matchingFix.fuel_min_onboard !== undefined) wp.Fuel_rem = matchingFix.fuel_min_onboard;
				else if (matchingFix.Fuel_rem !== undefined) wp.Fuel_rem = matchingFix.Fuel_rem;
				else if (matchingFix.fuel_rem !== undefined) wp.Fuel_rem = matchingFix.fuel_rem;
				// Fuel Total Used
				if (matchingFix.Fuel_totalused !== undefined) wp.Fuel_totalused = matchingFix.Fuel_totalused;
				else if (matchingFix.fuel_totalused !== undefined) wp.Fuel_totalused = matchingFix.fuel_totalused;
				// Track Magnetic (für MH/CRS Berechnung) - Simbrief uses Track_mag
				if (matchingFix.Track_mag !== undefined) wp.Trk_mag = matchingFix.Track_mag;
				else if (matchingFix.track_mag !== undefined) wp.Trk_mag = matchingFix.track_mag;
				else if (matchingFix.Trk_mag !== undefined) wp.Trk_mag = matchingFix.Trk_mag;
				else if (matchingFix.trk_mag !== undefined) wp.Trk_mag = matchingFix.trk_mag;
				// Track True - Simbrief uses Track_true
				if (matchingFix.Track_true !== undefined) wp.Trk_true = matchingFix.Track_true;
				else if (matchingFix.track_true !== undefined) wp.Trk_true = matchingFix.track_true;
				else if (matchingFix.Trk_true !== undefined) wp.Trk_true = matchingFix.Trk_true;
				else if (matchingFix.trk_true !== undefined) wp.Trk_true = matchingFix.trk_true;
				// Heading Magnetic - Simbrief uses Heading_mag
				if (matchingFix.Heading_mag !== undefined) wp.Hdg_mag = matchingFix.Heading_mag;
				else if (matchingFix.heading_mag !== undefined) wp.Hdg_mag = matchingFix.heading_mag;
				// Altitude
				if (matchingFix.Altitude_feet !== undefined) wp.altitude = matchingFix.Altitude_feet;
				else if (matchingFix.altitude_feet !== undefined) wp.altitude = matchingFix.altitude_feet;
				// Magnetic Variation (für Deviation Berechnung)
				// Simbrief hat kein Mag_var Feld, berechne aus Track_true - Track_mag
				if (matchingFix.Mag_var !== undefined) {
					wp.Mag_var = matchingFix.Mag_var;
				} else if (matchingFix.mag_var !== undefined) {
					wp.Mag_var = matchingFix.mag_var;
				} else if (matchingFix.Track_true !== undefined && matchingFix.Track_mag !== undefined) {
					// Berechne Mag Var aus Track-Differenz
					var trackTrue = parseFloat(matchingFix.Track_true);
					var trackMag = parseFloat(matchingFix.Track_mag);
					wp.Mag_var = trackTrue - trackMag;
				}
				// Wind Component (Gegenwind/Rückenwind)
				if (matchingFix.Wind_component !== undefined) wp.Wind_component = matchingFix.Wind_component;
				else if (matchingFix.wind_component !== undefined) wp.Wind_component = matchingFix.wind_component;
				// WCA (Wind Correction Angle)
				if (matchingFix.Wca !== undefined) wp.Wca = matchingFix.Wca;
				else if (matchingFix.wca !== undefined) wp.Wca = matchingFix.wca;
				// Time Total (Gesamtzeit seit Start)
				if (matchingFix.Time_total !== undefined) wp.Time_total = matchingFix.Time_total;
				else if (matchingFix.time_total !== undefined) wp.Time_total = matchingFix.time_total;
			} else {
				noMatchList.push(wpName);
			}
		}
	}
	console.log('[OFP Enrichment] Matched', matchCount, 'of', waypoints.length, 'waypoints');
	if (noMatchList.length > 0) {
		console.log('[OFP Enrichment] No matches found for:', noMatchList.join(', '));
	}

	// Für den ersten Waypoint (Abflughafen): Winddaten aus METAR extrahieren
	if (waypoints.length > 0 && waypoints[0]) {
		var firstWp = waypoints[0];
		var firstWpName = (firstWp.name || firstWp.Name || firstWp.ident || '').toUpperCase();
		var originData = ofpData.Origin || ofpData.origin;

		// Prüfen ob erster Waypoint der Abflughafen ist und keine Winddaten hat
		if (originData && !firstWp.Wind_dir && !firstWp.Wind_spd) {
			var originIcao = (originData.Icao_code || originData.icao_code || '').toUpperCase();

			// Nur wenn der erste Waypoint der Abflughafen ist
			if (firstWpName === originIcao || firstWpName.indexOf(originIcao) === 0) {
				var metar = originData.Metar || originData.metar;
				if (metar) {
					// Wind aus METAR extrahieren: z.B. "VRB01KT" oder "25010G15KT" oder "24015KT"
					var windMatch = metar.match(/\b(\d{3}|VRB)(\d{2,3})(G\d{2,3})?KT\b/i);
					if (windMatch) {
						var windDir = windMatch[1];
						var windSpd = windMatch[2];

						if (windDir === 'VRB') {
							firstWp.Wind_dir = 'VRB';
						} else {
							firstWp.Wind_dir = windDir;
						}
						firstWp.Wind_spd = windSpd;

						console.log('[OFP Enrichment] Departure airport', firstWpName, 'wind from METAR:', windDir + '°/' + windSpd + 'kt');
					}
				}

				// Abflughafen-spezifische Defaults setzen
				// ETE und Time_total sind 0 am Startpunkt
				if (firstWp.Ete === undefined) firstWp.Ete = '0';
				if (firstWp.Time_total === undefined) firstWp.Time_total = '0';
				// Fuel_burn am Start = 0
				if (firstWp.Fuel_burn === undefined) firstWp.Fuel_burn = '0';
				// Fuel_rem am Start = Plan Ramp Fuel
				var fuelData = ofpData.Fuel || ofpData.fuel;
				if (fuelData) {
					if (firstWp.Fuel_rem === undefined) {
						var rampFuel = fuelData.Plan_ramp || fuelData.plan_ramp;
						if (rampFuel) {
							firstWp.Fuel_rem = rampFuel;
							console.log('[OFP Enrichment] Departure airport Fuel_rem set to Plan_ramp:', rampFuel);
						}
					}
					// Fuel_flow für Abflughafen = Avg Fuel Flow
					if (firstWp.Fuel_flow === undefined) {
						var avgFlow = fuelData.Avg_fuel_flow || fuelData.avg_fuel_flow;
						if (avgFlow) {
							firstWp.Fuel_flow = avgFlow;
							console.log('[OFP Enrichment] Departure airport Fuel_flow set to Avg:', avgFlow);
						}
					}
				}
				// Elevation als Altitude
				var elevation = originData.Elevation || originData.elevation;
				if (elevation && firstWp.altitude === undefined) {
					firstWp.altitude = elevation;
				}
				// Groundspeed am Boden = 0
				if (firstWp.Groundspeed === undefined) firstWp.Groundspeed = '0';
			}
		}
	}

	// Debug: Ersten (Abflughafen) und zweiten Waypoint ausgeben
	if (waypoints.length > 0 && waypoints[0]) {
		console.log('[OFP Enrichment] ENRICHED WP[0] (Departure):', JSON.stringify({
			name: waypoints[0].name,
			Wind_dir: waypoints[0].Wind_dir,
			Wind_spd: waypoints[0].Wind_spd,
			Fuel_rem: waypoints[0].Fuel_rem,
			Fuel_flow: waypoints[0].Fuel_flow,
			Time_total: waypoints[0].Time_total,
			Ete: waypoints[0].Ete,
			Groundspeed: waypoints[0].Groundspeed,
			altitude: waypoints[0].altitude
		}));
	}
	if (waypoints.length > 1 && waypoints[1]) {
		console.log('[OFP Enrichment] ENRICHED WP[1]:', JSON.stringify({
			name: waypoints[1].name,
			Wind_dir: waypoints[1].Wind_dir,
			Wind_spd: waypoints[1].Wind_spd,
			Oat: waypoints[1].Oat,
			Groundspeed: waypoints[1].Groundspeed,
			Fuel_rem: waypoints[1].Fuel_rem,
			Time_total: waypoints[1].Time_total,
			Fuel_flow: waypoints[1].Fuel_flow,
			Ete: waypoints[1].Ete,
			Fuel_burn: waypoints[1].Fuel_burn,
			Mag_var: waypoints[1].Mag_var,
			Trk_mag: waypoints[1].Trk_mag,
			Hdg_mag: waypoints[1].Hdg_mag,
			altitude: waypoints[1].altitude
		}));
	}
}

// ============================================================================
// OFP FREQUENCY LOOKUP
// ============================================================================
function fetchNavaidFrequency(waypoint, callback) {
	if (!waypoint) {
		callback(null);
		return;
	}

	// Frequenz direkt aus OFP-Daten verwenden
	// Simbrief liefert Frequenzen in waypoint.Frequency oder waypoint.frequency
	if (waypoint.Frequency || waypoint.frequency) {
		var freq = waypoint.Frequency || waypoint.frequency;
		callback(String(freq));
		return;
	}

	// Keine Frequenz vorhanden
	callback(null);
}

function fetchAllNavaidFrequencies(waypoints, callback) {
	if (!Array.isArray(waypoints) || !waypoints.length) {
		callback([]);
		return;
	}

	var results = new Array(waypoints.length);
	var completed = 0;
	var total = waypoints.length;

	for (var i = 0; i < waypoints.length; i++) {
		(function(index) {
			fetchNavaidFrequency(waypoints[index], function(frequency) {
				results[index] = frequency;
				completed++;

				if (completed === total) {
					callback(results);
				}
			});
		})(i);
	}
}

// ============================================================================
// DOM CACHE
// ============================================================================
var domCache = {
	navlogContainer: null,
	buttonElements: {},
	waypointContainers: {}
};

function initWaypointTemplates() {
	var types = ['vfr', 'ifr', 'zy'];
	for (var ti = 0; ti < types.length; ti++) {
		var type = types[ti];
		var tableId = getWaypointTableIdForFlightType(type);
		var table = document.getElementById(tableId);
		if (!table) continue;
		var tbody = table.querySelector('tbody');
		if (!tbody) continue;
		var rows = tbody.querySelectorAll('tr');
		var collected = [];
		for (var r = 0; r < rows.length; r++) {
			var row = rows[r];
			var attr = row.getAttribute('data-waypoint-template');
			var isTemplate = (attr && attr.toString().trim().toLowerCase() === 'true') ||
				(row.classList && row.classList.contains('waypoint-template-row'));
			if (isTemplate) {
				collected.push(row.cloneNode(true));
				// Hide the original template row so it doesn't appear in the table
				row.style.display = 'none';
			}
		}
		if (collected.length === 0) {
			// Fallback: rows containing waypoint textareas
			var prefix = ('Waypoints-' + type).toUpperCase();
			for (var rr = 0; rr < rows.length; rr++) {
				var tAreas = rows[rr].querySelectorAll('textarea[id]');
				for (var tt = 0; tt < tAreas.length; tt++) {
					if (tAreas[tt].id && tAreas[tt].id.toUpperCase().indexOf(prefix) === 0) {
						collected.push(rows[rr].cloneNode(true));
						break;
					}
				}
				if (collected.length >= 2) break;
			}
		}
		if (collected.length > 0) {
			waypointTemplatesCache[type] = collected;
		}
	}
}

function logVerbose() {
	if (!NAVLOG_VERBOSE) return;
	if (typeof console !== 'undefined' && console.log) {
		console.log.apply(console, arguments);
	}
}

function isNavlogActiveTab() {
	// Für Kneeboard-iframe nur synchronisieren, wenn Tab "navlog" aktiv ist.
	try {
		var isIframe = window.parent !== window;
		if (!isIframe) return true; // Browser-Mirror oder Standalone
		if (typeof localStorage === 'undefined') return true;
		var active = localStorage.getItem('activeTab') || '';
		return active === 'navlog';
	} catch (e) {
		return true; // Fallback: nicht blockieren
	}
}

function getCachedNavlogContainer() {
	// IMPORTANT: Only cache if element was found
	// If null was cached, we need to re-query when tab is loaded
	if (!domCache.navlogContainer) {
		var el = document.getElementById('navlog');
		if (el) {
			domCache.navlogContainer = el;
		}
		return el;
	}
	return domCache.navlogContainer;
}

function ensureNavlogContainer() {
	return getCachedNavlogContainer();
}

// Called from kneeboard.js when showing cached tab - resets DOM cache
// so that getCachedNavlogContainer() will re-query the DOM
function resetNavlogDomCache() {
	NAVLOG_DEBUG && console.log('[Navlog Debug] resetNavlogDomCache() called - clearing DOM cache');
	domCache.navlogContainer = null;
	domCache.buttonElements = {};
	domCache.waypointContainers = {};
}

// ============================================================================
// WAYPOINT SETTINGS
// ============================================================================
function loadWaypointFixedCountSettings() {
	if (typeof localStorage === 'undefined') return;

	var keys = Object.keys(waypointFixedCountSettings);
	for (var i = 0; i < keys.length; i++) {
		var key = keys[i];
		var stored = localStorage.getItem('waypointFixedCount-' + key);
		if (stored) {
			var parsed = parseInt(stored, 10);
			if (Number.isFinite(parsed) && parsed > 0) {
				waypointFixedCountSettings[key] = parsed;
			}
		}
	}
}

function persistWaypointFixedCountSetting(key, value) {
	if (typeof localStorage === 'undefined' || !key) return;

	if (Number.isFinite(value) && value > 0) {
		localStorage.setItem('waypointFixedCount-' + key, String(value));
	} else {
		localStorage.removeItem('waypointFixedCount-' + key);
	}
}

function restoreStoredWaypointCounts() {
	var keys = Object.keys(waypointFixedCountSettings);
	for (var i = 0; i < keys.length; i++) {
		var key = keys[i];
		var stored = waypointFixedCountSettings[key];
		if (Number.isFinite(stored) && stored > 0) {
			setWaypointFixedCount(key, stored);
		}
	}
}

// ============================================================================
// MESSAGE HANDLING
// ============================================================================
window.addEventListener('message', handleGlobalFlightplanBroadcast, false);

// ============================================================================
// INITIALIZATION
// ============================================================================
function initNavlogPage() {
	// CRITICAL: Reset DOM cache when Navlog tab is (re-)opened
	// This ensures we find the new DOM elements after AJAX load
	domCache.navlogContainer = null;
	domCache.buttonElements = {};
	domCache.waypointContainers = {};

	ensureThemeColors();

	initWaypointTemplates();
	loadWaypointFixedCountSettings();
	initializeWaypointIdCounters();
	setupWaypointAutoExpand();
	restoreStoredWaypointCounts();

	// CRITICAL: Load localStorage values BEFORE applying cached flightplan
	// This ensures OFP fields from server override old localStorage values
	readValues();

	// TEIL 2: Event Listeners nur einmal registrieren (verhindert Duplikate bei Tab-Wechsel)
	if (!navlogEventListenersInitialized) {
		window.addEventListener("message", handleIncomingMessage);
		document.addEventListener("visibilitychange", function () {
			if (document.hidden) {
				saveValuesImmediate();
			}
		});
		window.addEventListener("beforeunload", saveValuesImmediate);
		document.addEventListener('keydown', function(event) {
			clearTimeout(saveVal);
			saveVal = setTimeout(saveValues, KEYDOWN_SAVE_DELAY_MS);
		}, false);
		navlogEventListenersInitialized = true;
		NAVLOG_DEBUG && console.log('[Navlog Debug] Event listeners registered (first time)');
	} else {
		NAVLOG_DEBUG && console.log('[Navlog Debug] Event listeners already registered - skipping');
	}

	initNavlogSync();

	// Initialize live clocks for Block_out, Block_in, Time-off fields
	initTimeIcons();

	// Initialize Block_out/Block_in listeners for Log-time calculation
	initBlockTimeListeners();

	// Apply cached flightplan AFTER readValues so OFP fields are not overwritten
	applyCachedFlightplanIfReady();

	// TEIL 2: Textarea Listeners mit Deduplizierung
	var inputs = document.querySelectorAll("input, textarea");
	for (var i = 0; i < inputs.length; i++) {
		var el = inputs[i];
		// Skip if already initialized (prevent duplicate listeners on rapid tab switching)
		if (el.dataset.navlogInitialized === 'true') continue;
		el.dataset.navlogInitialized = 'true';

		el.classList.add("use-keyboard-input");

		// Save promptly on blur/change to reduce data loss risk when switching tabs.
		el.addEventListener("blur", saveValuesImmediate);
		el.addEventListener("change", saveValuesImmediate);

		el.addEventListener("focus", function(element) {
			return function() {
				var kb = (window.parent && window.parent.Keyboard) || window.Keyboard;
				if (kb && typeof kb.open === "function") {
					kb.open(element.value, function(val) { element.value = val; }, element);
				}
			};
		}(el));
	}
}

// ============================================================================
// WAYPOINT ID MANAGEMENT
// ============================================================================
function initializeWaypointIdCounters() {
	var navRoot = ensureNavlogContainer();
	if (!navRoot) return;

	var textareas = navRoot.querySelectorAll('textarea[id]');
	for (var i = 0; i < textareas.length; i++) {
		registerWaypointId(textareas[i].id);
	}
}

function registerWaypointId(id) {
	var info = parseWaypointId(id);
	if (!info || !info.base || !info.number) return;

	var current = waypointIdCounters[info.base] || 0;
	if (info.number > current) {
		waypointIdCounters[info.base] = info.number;
	}
}

function getNextWaypointId(base) {
	if (!base) return null;

	var current = waypointIdCounters[base] || 0;
	waypointIdCounters[base] = ++current;
	return current;
}

function parseWaypointId(id) {
	if (!id || typeof id !== 'string') return null;

	var match = id.match(/^(.*?)-(\d+)$/);
	if (match) {
		return {
			base: match[1],
			number: parseInt(match[2], 10)
		};
	}
	return { base: id, number: 1 };
}

function normalizeFixedWaypointValue(value) {
	var numeric = Number(value);
	return (Number.isFinite(numeric) && numeric > 0) ? Math.floor(numeric) : 0;
}

function getConfiguredFixedWaypointCount(key) {
	if (!key || !waypointFixedCountSettings.hasOwnProperty(key)) return 0;
	return normalizeFixedWaypointValue(waypointFixedCountSettings[key]);
}

function findAncestorId(element, tags) {
	return '';
}

// ============================================================================
// WAYPOINT CONFIGURATION
// ============================================================================
var waypointAutoExpandConfigs = {
	vfr: {
		key: 'vfr',
		tbodyId: 'waypointsVFRWaypoints',
		templateRows: [],
		totalRow: null,
		startMarker: 'VFR Zeilenanfang',
		endMarker: 'VFR Zeilenende',
		fixedWaypointCount: getConfiguredFixedWaypointCount('vfr')
	},
	ifr: {
		key: 'ifr',
		tbodyId: 'waypointsIFRWaypoints',
		templateRows: [],
		totalRow: null,
		startMarker: 'IFR Zeilenanfang',
		endMarker: 'IFR Zeilenende',
		fixedWaypointCount: getConfiguredFixedWaypointCount('ifr')
	},
	zy: {
		key: 'zy',
		tbodyId: 'waypointsZYWaypoints',
		templateRows: [],
		totalRow: null,
		startMarker: 'ZY Zeileanfang',
		endMarker: 'ZY Zeilenende',
		fixedWaypointCount: getConfiguredFixedWaypointCount('zy')
	}
};

function setWaypointFixedCount(key, value) {
	if (!key) return;

	var normalized = normalizeFixedWaypointValue(value);
	waypointFixedCountSettings[key] = normalized;
	persistWaypointFixedCountSetting(key, normalized);

	var config = waypointAutoExpandConfigs[key];
	if (!config) return;

	config.fixedWaypointCount = normalized;
	if (!config.tbody || !config.templateRows || !config.templateRows.length) return;

	var groupSize = getTemplateGroupSize(config);
	if (hasFixedWaypointCount(config)) {
		var fixedGroups = Math.max(1, Math.floor(Number(config.fixedWaypointCount)));
		config.baseRowCount = Math.max(groupSize, fixedGroups * groupSize);
		ensureFixedWaypointGroupCount(config);
	} else {
		config.baseRowCount = Math.max(groupSize, getWaypointDataRows(config).length);
	}
	updateWaypointPlaceholder(config);
}

if (typeof window !== 'undefined') {
	window.setNavlogWaypointFixedCount = setWaypointFixedCount;
	window.ensureExactRowCount = ensureExactRowCount;
}

// ============================================================================
// WAYPOINT AUTO-EXPAND SETUP
// ============================================================================
function setupWaypointAutoExpand() {
	var keys = Object.keys(waypointAutoExpandConfigs);
	for (var idx = 0; idx < keys.length; idx++) {
		var key = keys[idx];
		var config = waypointAutoExpandConfigs[key];
		var tbody = document.getElementById(config.tbodyId);
		if (!tbody) {
			continue;
		}
		config.tbody = tbody;
		config.totalRow = findWaypointTotalRow(tbody, config.key);
		config.templateRows = captureWaypointTemplateRows(tbody, config.totalRow, config);
		ensureWaypointCheckpointClasses(Array.from(tbody.querySelectorAll('tr')));

		tbody.addEventListener('input', function(cfg) {
			return function(event) {
				handleWaypointInput(cfg, event);
			};
		}(config));

		var groupSize = getTemplateGroupSize(config);
		if (hasFixedWaypointCount(config)) {
			var fixedGroups = Math.max(1, Math.floor(Number(config.fixedWaypointCount)));
			var fixedRowCount = Math.max(groupSize, fixedGroups * groupSize);
			config.baseRowCount = fixedRowCount;
			ensureFixedWaypointGroupCount(config);
		}
		else {
			config.baseRowCount = Math.max(groupSize, getWaypointDataRows(config).length);
		}
		updateWaypointPlaceholder(config);
	}

	// NEW: Apply any cached flightplan data now that DOM is ready
	// This handles the timing issue where SimBrief sends data BEFORE the navlog tab is opened
	if (cachedFlightplanData !== null || cachedOfpData !== null) {
		NAVLOG_DEBUG && console.log('[Navlog Debug] setupWaypointAutoExpand complete - applying cached flightplan data');
		setTimeout(function() {
			applyCachedFlightplanIfReady();
		}, 50);
	}
}

function ensureWaypointCapacity(minCount) {
	var keys = Object.keys(waypointAutoExpandConfigs);
	for (var i = 0; i < keys.length; i++) {
		var key = keys[i];
		var config = waypointAutoExpandConfigs[key];
		if (!config) continue;
		var current = Number(config.fixedWaypointCount || waypointFixedCountSettings[key] || 0);
		var target = Number.isFinite(current) ? Math.max(current, minCount) : minCount;
		if (!Number.isFinite(current) || current !== target) {
			setWaypointFixedCount(key, target);
		}
	}
}

function getWaypointTableIdForFlightType(flightType) {
	switch (flightType) {
		case 'vfr':
			return 'waypointsVFR';
		case 'ifr':
			return 'waypointsIFR';
		case 'zy':
		case 'z':
		case 'y':
			return 'waypointsZY';
		default:
			return null;
	}
}

function ensureExactRowCount(targetFlightType, exactRowCount) {
	// Get the waypoint table for the specified flight type
	var waypointTableId = getWaypointTableIdForFlightType(targetFlightType);
	if (!waypointTableId) {
		NAVLOG_DEBUG && console.log('[ensureExactRowCount] No waypoint table ID for flight type:', targetFlightType);
		return;
	}

	var navlogContainer = ensureNavlogContainer();
	if (!navlogContainer) {
		NAVLOG_DEBUG && console.log('[ensureExactRowCount] No navlog container found');
		return;
	}

	var waypointTable = navlogContainer.querySelector('#' + waypointTableId);
	if (!waypointTable) {
		NAVLOG_DEBUG && console.log('[ensureExactRowCount] Waypoint table not found:', waypointTableId);
		return;
	}

	var tbody = waypointTable.querySelector('tbody');
	if (!tbody) {
		NAVLOG_DEBUG && console.log('[ensureExactRowCount] No tbody found in waypoint table');
		return;
	}

	// Find template rows
	function isTemplateRow(row) {
		if (!row) return false;
		var attr = row.getAttribute('data-waypoint-template');
		if (attr && attr.toString().trim().toLowerCase() === 'true') {
			return true;
		}
		if (row.classList && row.classList.contains('waypoint-template-row')) {
			return true;
		}
		return false;
	}

	var templateRows = [];
	var allRows = tbody.querySelectorAll('tr');
	// Prefer cached templates if available
	if (waypointTemplatesCache[targetFlightType] && waypointTemplatesCache[targetFlightType].length) {
		templateRows = waypointTemplatesCache[targetFlightType];
	} else {
		for (var i = 0; i < allRows.length; i++) {
			if (isTemplateRow(allRows[i])) {
				templateRows.push(allRows[i].cloneNode(true));
			}
		}
		if (templateRows.length === 0) {
			initWaypointTemplates();
			if (waypointTemplatesCache[targetFlightType] && waypointTemplatesCache[targetFlightType].length) {
				templateRows = waypointTemplatesCache[targetFlightType];
			}
		}
	}

	if (templateRows.length === 0) {
		NAVLOG_DEBUG && console.warn('[ensureExactRowCount] No template rows available for', targetFlightType);
		return;
	}

	// Count current visible rows (excluding templates)
	var visibleNonTemplateRows = [];
	for (var i = 0; i < allRows.length; i++) {
		if (!isTemplateRow(allRows[i]) &&
		    allRows[i].style.display !== 'none') {
			visibleNonTemplateRows.push(allRows[i]);
		}
	}

	var groupSize = templateRows.length; // rows per waypoint
	var currentVisibleGroups = Math.floor(visibleNonTemplateRows.length / groupSize);
	NAVLOG_DEBUG && console.log('[ensureExactRowCount] Current visible groups:', currentVisibleGroups, ', Target:', exactRowCount, ', groupSize:', groupSize);

	var groupDiff = exactRowCount - currentVisibleGroups;

	if (groupDiff > 0) {
		NAVLOG_DEBUG && console.log('[ensureExactRowCount] Adding', groupDiff, 'groups');

		// Find the first template row in the DOM (not from cache)
		var firstDomTemplateRow = null;
		for (var i = 0; i < allRows.length; i++) {
			if (isTemplateRow(allRows[i])) {
				firstDomTemplateRow = allRows[i];
				break;
			}
		}

		// Insert in REVERSE order to maintain correct sequence
		// (insertBefore always inserts at the same position, so we need to reverse)
		for (var g = groupDiff - 1; g >= 0; g--) {
			for (var t = templateRows.length - 1; t >= 0; t--) {
				var newRow = templateRows[t].cloneNode(true);
				newRow.removeAttribute('data-waypoint-template');
				newRow.style.display = '';

				// Clear all textarea values in the new row
				var textareas = newRow.getElementsByTagName('textarea');
				for (var ta = 0; ta < textareas.length; ta++) {
					textareas[ta].value = '';
				}

				// Insert before the first template row in DOM, or at end if no template found
				if (firstDomTemplateRow) {
					tbody.insertBefore(newRow, firstDomTemplateRow);
				} else {
					tbody.appendChild(newRow);
				}
			}
		}
	} else if (groupDiff < 0) {
		var groupsToHide = Math.abs(groupDiff);
		NAVLOG_DEBUG && console.log('[ensureExactRowCount] Hiding', groupsToHide, 'groups');

		// recompute visible rows after templates (up to date)
		visibleNonTemplateRows = [];
		var updatedRows = tbody.querySelectorAll('tr');
		for (var r = 0; r < updatedRows.length; r++) {
			if (!isTemplateRow(updatedRows[r]) && updatedRows[r].style.display !== 'none') {
				visibleNonTemplateRows.push(updatedRows[r]);
			}
		}

		// Hide groups from the end
		for (var idx = visibleNonTemplateRows.length - 1; idx >= 0 && groupsToHide > 0; ) {
			for (var gt = 0; gt < groupSize && idx >= 0; gt++, idx--) {
				visibleNonTemplateRows[idx].style.display = 'none';
				var tAreas = visibleNonTemplateRows[idx].getElementsByTagName('textarea');
				for (var ta2 = 0; ta2 < tAreas.length; ta2++) {
					tAreas[ta2].value = '';
				}
			}
			groupsToHide--;
		}
	}

	NAVLOG_DEBUG && console.log('[ensureExactRowCount] Row adjustment complete. Final visible groups:', exactRowCount);
}

function ensureCapacityForNavlogString(navlogString) {
	if (!navlogString) {
		NAVLOG_DEBUG && console.log('[ensureCapacity] No navlog string provided');
		return;
	}

	var values = navlogString.split('~');
	NAVLOG_DEBUG && console.log('[ensureCapacity] Received', values.length, 'values for flight type:', values[0]);

	// Get the current flight type from the navlog string
	var incomingFlightType = values[0] || flightType;

	// Find the waypoint table for this flight type
	// IDs are: waypointsVFR, waypointsIFR, waypointsZY
	var tableId = 'waypoints' + incomingFlightType.toUpperCase();
	var waypointTable = document.getElementById(tableId);

	if (!waypointTable) {
		NAVLOG_DEBUG && console.log('[ensureCapacity] No waypoint table found for', incomingFlightType, '(tried ID:', tableId + ')');
		return;
	}

	NAVLOG_DEBUG && console.log('[ensureCapacity] Found waypoint table:', tableId);

	// Find template rows for this flight type
	var templateRows = waypointTable.querySelectorAll('tr[data-waypoint-template="true"]');
	if (templateRows.length === 0) {
		NAVLOG_DEBUG && console.log('[ensureCapacity] No template rows found for', incomingFlightType);
		return;
	}

	NAVLOG_DEBUG && console.log('[ensureCapacity] Found', templateRows.length, 'template rows for', incomingFlightType);

	// Count current visible (non-template) waypoint rows
	var allRows = waypointTable.querySelectorAll('tr');
	var currentVisibleRows = 0;
	for (var i = 0; i < allRows.length; i++) {
		if (allRows[i].getAttribute('data-waypoint-template') !== 'true' &&
		    allRows[i].style.display !== 'none') {
			currentVisibleRows++;
		}
	}

	// Estimate how many waypoint rows we need based on the number of values
	// Each waypoint typically has ~15-20 fields, grouped in sets of 2 rows
	var fieldsPerWaypointGroup = templateRows.length > 0 ?
		templateRows[0].getElementsByTagName('textarea').length +
		(templateRows[1] ? templateRows[1].getElementsByTagName('textarea').length : 0) : 15;

	var estimatedNeededRows = Math.ceil(values.length / fieldsPerWaypointGroup) * templateRows.length;
	var rowsToAdd = Math.max(0, estimatedNeededRows - currentVisibleRows);

	NAVLOG_DEBUG && console.log('[ensureCapacity] Current visible rows:', currentVisibleRows,
	            ', Estimated needed:', estimatedNeededRows,
	            ', Will add:', rowsToAdd);

	// Add rows if needed (in groups matching template size)
	if (rowsToAdd > 0) {
		var groupsToAdd = Math.ceil(rowsToAdd / templateRows.length);
		NAVLOG_DEBUG && console.log('[ensureCapacity] Adding', groupsToAdd, 'groups of', templateRows.length, 'rows');

		// Get the parent tbody of the template rows
		var tbody = templateRows[0].parentNode;
		if (!tbody) {
			NAVLOG_DEBUG && console.log('[ensureCapacity] No parent tbody found for template rows');
			return;
		}

		for (var g = 0; g < groupsToAdd; g++) {
			for (var t = 0; t < templateRows.length; t++) {
				var newRow = templateRows[t].cloneNode(true);
				newRow.removeAttribute('data-waypoint-template');
				newRow.style.display = '';

				// Clear all textarea values in the new row
				var textareas = newRow.getElementsByTagName('textarea');
				for (var ta = 0; ta < textareas.length; ta++) {
					textareas[ta].value = '';
				}

				// Insert before the template rows in the tbody
				tbody.insertBefore(newRow, templateRows[0]);
			}
		}

		NAVLOG_DEBUG && console.log('[ensureCapacity] Successfully added', groupsToAdd * templateRows.length, 'rows');

		// Make sure all rows are visible (hideExcess might have hidden them)
		var allRows = tbody.querySelectorAll('tr');
		for (var r = 0; r < allRows.length; r++) {
			if (allRows[r].getAttribute('data-waypoint-template') !== 'true' &&
			    allRows[r].getAttribute('data-waypoint-total') !== incomingFlightType) {
				allRows[r].style.display = '';
			}
		}
		NAVLOG_DEBUG && console.log('[ensureCapacity] All rows made visible');
	}
}

function isWaypointCheckpoint(area) {
	return !!(area && area.classList && area.classList.contains('waypoint-checkpoint'));
}

function hasTextValue(area) {
	return !!(area && typeof area.value === 'string' && area.value.trim().length);
}

function findCheckpointTextarea(row) {
	return findCheckpointDisplayTextarea(row);
}

function findCheckpointDisplayTextarea(row) {
	if (!row || !row.querySelector) return null;

	var firstCell = row.querySelector('td');
	if (!firstCell) return null;

	var span = firstCell.getAttribute ? firstCell.getAttribute('rowspan') : null;
	if (!span) return null;

	var spanValue = parseInt(span, 10);
	if (!isFinite(spanValue) || spanValue < 2) return null;

	return firstCell.querySelector('textarea');
}

function ensureWaypointCheckpointClasses(rows) {
	if (!rows || !rows.length) return;

	for (var i = 0; i < rows.length; i++) {
		var checkpoint = findCheckpointTextarea(rows[i]);
		if (checkpoint && !isWaypointCheckpoint(checkpoint)) {
			checkpoint.classList.add('waypoint-checkpoint');
		}
	}
}

function updateWaypointPlaceholder(config) {
	if (!config || !config.tbody) return;

	var rows = config.tbody.querySelectorAll('tr');

	// Erst alle "Add" Platzhalter entfernen
	for (var i = 0; i < rows.length; i++) {
		var displayField = findCheckpointDisplayTextarea(rows[i]);
		if (displayField) {
			displayField.removeAttribute('placeholder');
			var displayCell = displayField.parentElement;
			if (displayCell && displayCell.classList) {
				displayCell.classList.remove('waypoint-placeholder-add-cell');
			}
		}
	}

	// Dann "Add" in der letzten sichtbaren Gruppe setzen
	var lastGroup = getLastWaypointsGroup(config);
	if (lastGroup && lastGroup.length) {
		for (var j = 0; j < lastGroup.length; j++) {
			var display = findCheckpointDisplayTextarea(lastGroup[j]);
			if (display) {
				display.setAttribute('placeholder', 'Add');
				var displayCell = display.parentElement;
				if (displayCell && displayCell.classList) {
					displayCell.classList.add('waypoint-placeholder-add-cell');
				}
				break;
			}
		}
	}
}

function normalizeMarkerText(text) {
	return text ? String(text).toLowerCase().replace(/[^a-z0-9]+/g, '') : '';
}

function findTemplateRowsByComment(tbody, config) {
	if (!tbody || !config) return [];

	var startKey = normalizeMarkerText(config.startMarker);
	var endKey = normalizeMarkerText(config.endMarker);
	if (!startKey || !endKey) return [];

	var nodes = Array.from(tbody.childNodes || []);
	var collecting = false;
	var rows = [];

	for (var i = 0; i < nodes.length; i++) {
		var node = nodes[i];
		if (!node) continue;

		if (node.nodeType === 8) {
			var marker = normalizeMarkerText(node.textContent || node.nodeValue || '');
			if (!collecting && marker.indexOf(startKey) !== -1) {
				collecting = true;
				continue;
			}
			if (collecting && marker.indexOf(endKey) !== -1) break;
			continue;
		}
		if (collecting && node.nodeType === 1 && node.tagName && node.tagName.toLowerCase() === 'tr') {
			rows.push(node);
		}
	}
	return rows;
}

function captureWaypointTemplateRows(tbody, totalRow, config) {
	var rows = Array.from(tbody.children);
	var limitIndex = totalRow ? rows.indexOf(totalRow) : rows.length;
	if (limitIndex === -1) {
		limitIndex = rows.length;
	}

	var templateCandidates = Array.from(tbody.querySelectorAll('tr[data-waypoint-template="true"]')).filter(function (row) {
		var rowIndex = rows.indexOf(row);
		return rowIndex !== -1 && rowIndex < limitIndex;
	});
	if (templateCandidates.length >= 2) {
		return templateCandidates;
	}

	var commentTemplates = findTemplateRowsByComment(tbody, config);
	if (commentTemplates.length) {
		return commentTemplates;
	}

	var checkpointPair = findWaypointCheckpointPair(rows, limitIndex);
	if (checkpointPair.length) {
		return checkpointPair;
	}

	var dataRows = [];
	for (var i = 0; i < limitIndex; i++) {
		var row = rows[i];
		if (row.querySelector('textarea')) {
			dataRows.push(row);
		}
	}
	if (dataRows.length < 2) {
		return [];
	}
	return [dataRows[0], dataRows[1]];
}

function rowHasCheckpoint(row) {
	return !!findCheckpointDisplayTextarea(row);
}

function findWaypointCheckpointPair(rows, limitIndex) {
	for (var i = 0; i < limitIndex - 1; i++) {
		var row = rows[i];
		if (rowHasCheckpoint(row)) {
			var nextRow = rows[i + 1];
			if (nextRow && nextRow.querySelector && nextRow.querySelector('textarea')) {
				return [row, nextRow];
			}
		}
	}
	return [];
}

function findWaypointTotalRow(tbody, key) {
	var rows = Array.from(tbody.children);
	for (var i = rows.length - 1; i >= 0; i--) {
		var row = rows[i];
		if (key && row.dataset.waypointTotal === key) return row;

		var text = (row.textContent || row.innerText || '').toLowerCase();
		if (text.includes('totals')) return row;
	}
	return null;
}

function getCurrentCheckpointCount(config) {
	var rows = getWaypointDataRows(config);
	var count = 0;
	for (var i = 0; i < rows.length; i++) {
		if (rows[i].style.display === 'none') continue;
		if (findCheckpointDisplayTextarea(rows[i])) count++;
	}
	return count;
}

function hasFixedWaypointCount(config) {
	var value = config ? Number(config.fixedWaypointCount) : NaN;
	return Number.isFinite(value) && value > 0;
}

// ============================================================================
// WAYPOINT INPUT HANDLING
// ============================================================================
function handleWaypointInput(config, event) {
	var target = event.target;
	if (!target || target.tagName !== 'TEXTAREA') return;
	if (!isWaypointCheckpoint(target)) return;

	var row = target.closest('tr');
	if (!row || row.dataset.waypointTotal === config.key) return;

	var currentGroup = getWaypointGroupForRow(config, row);
	if (!currentGroup.length) return;

	var isAddField = target.getAttribute('placeholder') === 'Add' ||
	                 (target.parentElement && target.parentElement.classList.contains('waypoint-placeholder-add-cell'));
	var hasValue = hasTextValue(target);

	// Fall 1: "Add"-Feld leer → Aktuelle Gruppe entfernen (aber Minimum beachten)
	if (isAddField && !hasValue) {
		removeWaypointGroup(config, currentGroup, false);
		updateWaypointPlaceholder(config);
		return;
	}

	// Fall 2: "Add"-Feld hat Text → Neue Gruppe hinzufügen
	if (isAddField && hasValue) {
		var triggerGroup = getLastWaypointsGroup(config);
		var templates = config.templateRows.length ? config.templateRows : triggerGroup;
		if (!templates.length) return;

		var alreadyExpanded = triggerGroup.some(function (r) { return r.dataset.waypointPairExpanded === 'true'; });
		if (alreadyExpanded) return;

		var groupHasValue = waypointGroupHasValue(triggerGroup);
		if (!groupHasValue) {
			removeWaypointGroup(config, triggerGroup);
			updateWaypointPlaceholder(config);
			return;
		}

		markWaypointGroupExpanded(triggerGroup);
		insertWaypointBlock(config, templates);
		updateWaypointPlaceholder(config);
		return;
	}

	// Fall 3: Nicht-"Add"-Feld leer → Letzte Gruppe entfernen (wenn mehr als Minimum)
	if (!isAddField && !hasValue) {
		var lastGroup = getLastWaypointsGroup(config);
		if (!lastGroup.length) return;

		var lastGroupHasValue = waypointGroupHasValue(lastGroup);
		if (lastGroupHasValue) return;

		removeWaypointGroup(config, lastGroup, false);
		updateWaypointPlaceholder(config);
	}
}

function getTemplateGroupSize(config) {
	var count = config.templateRows.length;
	return count > 0 ? count : 2;
}

function getWaypointDataRows(config) {
	var tbody = config.tbody;
	if (!tbody) return [];

	var rows = Array.from(tbody.children);
	var totalIndex = rows.findIndex(function (row) {
		return row.dataset.waypointTotal === config.key;
	});
	return rows.slice(0, totalIndex === -1 ? rows.length : totalIndex);
}

function ensureFixedWaypointGroupCount(config) {
	if (!hasFixedWaypointCount(config)) return;
	if (!config || !config.tbody || !config.templateRows || !config.templateRows.length) return;

	var desiredCheckpoints = Math.max(1, Math.floor(Number(config.fixedWaypointCount)));

	// Erst ALLE Zeilen sichtbar machen (Reset)
	showAllWaypointGroups(config);

	// Fehlende Zeilen hinzufügen (wenn nötig)
	var templates = config.templateRows || [];
	var safety = 0;
	var currentGroups = collectWaypointGroups(config);

	while (currentGroups.length < desiredCheckpoints && safety < SAFETY_LOOP_LIMIT) {
		insertWaypointBlock(config, templates);
		currentGroups = collectWaypointGroups(config);
		safety++;
	}

	// Überschüssige Zeilen verstecken (ab desiredCheckpoints)
	hideExcessWaypointGroups(config, desiredCheckpoints);
}

function getLastWaypointsGroup(config) {
	if (!config || !config.tbody) return [];

	var rows = Array.from(config.tbody.children || []).filter(function (row) {
		return isWaypointRowForConfig(row, config) && row.style.display !== 'none';
	});

	if (!rows.length) return [];

	var groupSize = getTemplateGroupSize(config);
	var groups = [];
	var index = 0;

	while (index < rows.length) {
		var group = [];
		for (var i = 0; i < groupSize && index < rows.length; i++) {
			group.push(rows[index++]);
		}
		if (group.length > 0) groups.push(group);
	}

	return groups.length > 0 ? groups[groups.length - 1] : [];
}

function getWaypointGroupForRow(config, targetRow) {
	if (!config || !targetRow) {
		return [];
	}

	var rows = Array.from(config.tbody.children || []).filter(function (row) {
		if (!isWaypointRowForConfig(row, config)) {
			return false;
		}
		if (row.style.display === 'none') {
			return false;
		}
		return true;
	});

	var groupSize = getTemplateGroupSize(config);
	var groups = [];
	var index = 0;

	while (index < rows.length) {
		var group = [];
		for (var i = 0; i < groupSize && index < rows.length; i++) {
			group.push(rows[index]);
			index++;
		}
		if (group.length > 0) {
			groups.push(group);
		}
	}

	for (var j = 0; j < groups.length; j++) {
		var grp = groups[j];
		if (grp.indexOf(targetRow) !== -1) {
			return grp;
		}
	}
	return [];
}

function waypointGroupHasValue(group) {
	return group.some(function (row) {
		var area = row.querySelector('textarea');
		return hasTextValue(area);
	});
}

function removeWaypointGroup(config, group, forceRemove) {
	if (!config || !config.tbody || !group || !group.length) {
		return;
	}

	var minCheckpoints = hasFixedWaypointCount(config) ? Math.max(1, Math.floor(Number(config.fixedWaypointCount))) : 5;
	var currentCheckpoints = getCurrentCheckpointCount(config);

	NAVLOG_DEBUG && console.log('[removeGroup] ' + config.key + ' - currentCheckpoints: ' + currentCheckpoints + ', minCheckpoints: ' + minCheckpoints + ', forceRemove: ' + forceRemove);

	if (currentCheckpoints <= minCheckpoints) {
		NAVLOG_DEBUG && console.log('[removeGroup] ABORT: Already at minimum checkpoints');
		return;
	}

	if (!forceRemove && group.some(function (row) { return row.dataset.waypointPairExpanded === 'true'; })) {
		return;
	}

	if (!forceRemove && group.some(function (row) { return waypointRowHasValues(row); })) {
		return;
	}

	for (var i = 0; i < group.length; i++) {
		if (group[i].parentNode === config.tbody) {
			config.tbody.removeChild(group[i]);
		}
	}

	resetLastGroupExpansionState(config);

	var currentVisibleCheckpoints = getCurrentCheckpointCount(config);
	hideExcessWaypointGroups(config, currentVisibleCheckpoints + 1);

	updateWaypointPlaceholder(config);
}

function waypointRowHasValues(row) {
	if (!row) {
		return false;
	}
	var areas = row.querySelectorAll ? row.querySelectorAll('textarea') : [];
	for (var i = 0; i < areas.length; i++) {
		if (hasTextValue(areas[i])) {
			return true;
		}
	}
	return false;
}

function resetLastGroupExpansionState(config) {
	if (!config) {
		return;
	}
	var remainingGroup = getLastWaypointsGroup(config);
	for (var i = 0; i < remainingGroup.length; i++) {
		if (remainingGroup[i].dataset) {
			remainingGroup[i].dataset.waypointPairExpanded = 'false';
		}
	}
}

function markWaypointGroupExpanded(group) {
	for (var i = 0; i < group.length; i++) {
		group[i].dataset.waypointPairExpanded = 'true';
	}
}

function insertWaypointBlock(config, templates) {
	var sourceRows = templates && templates.length ? templates : config.templateRows;
	if (!sourceRows.length) {
		return;
	}
	var clones = sourceRows.map(function (row) {
		var clone = row.cloneNode(true);
		cleanWaypointRow(clone);
		return clone;
	});
	var tbody = config.tbody;
	var totalRow = config.totalRow;
	if (!tbody) {
		return;
	}
	for (var i = 0; i < clones.length; i++) {
		clones[i].dataset.waypointPairExpanded = 'false';
		if (totalRow) {
			tbody.insertBefore(clones[i], totalRow);
		}
		else {
			tbody.appendChild(clones[i]);
		}
	}
	updateWaypointPlaceholder(config);

	// Time-Icons für neu eingefügte ATA-Felder initialisieren
	initTimeIcons();
}

function cleanWaypointRow(row) {
	var areas = row.getElementsByTagName('textarea');
	for (var i = 0; i < areas.length; i++) {
		var area = areas[i];
		var templateId = area.getAttribute('id');
		var baseInfo = parseWaypointId(templateId);
		area.value = '';
		area.removeAttribute('placeholder');
		if (baseInfo && baseInfo.base) {
			var nextNumber = getNextWaypointId(baseInfo.base);
			if (nextNumber !== null) {
				area.id = baseInfo.base + '-' + nextNumber;
			}
			else {
				area.removeAttribute('id');
			}
		}
		else {
			area.removeAttribute('id');
		}
	}
	row.removeAttribute('data-waypoint-template');
	row.removeAttribute('data-waypoint-pair-expanded');
}

function hasNumberedWaypointSuffix(id, prefix) {
	if (!id || !prefix || typeof id !== 'string') {
		return false;
	}
	if (id.indexOf(prefix) !== 0) {
		return false;
	}
	var remainder = id.slice(prefix.length);
	if (!remainder) {
		return false;
	}
	var parts = remainder.split('-').filter(function (segment) {
		return !!segment;
	});
	if (!parts.length) {
		return false;
	}
	var last = parts[parts.length - 1];
	return /^\d+$/.test(last);
}

function isWaypointRowForConfig(row, config) {
	if (!row || !config || !config.key) {
		return false;
	}
	if (row.dataset && row.dataset.waypointTotal === config.key) {
		return false;
	}
	if (!row.querySelectorAll) {
		return false;
	}
	var areas = row.querySelectorAll('textarea[id]');
	if (!areas.length) {
		return false;
	}
	var prefix = 'Waypoints-' + config.key.toUpperCase() + '-';
	for (var i = 0; i < areas.length; i++) {
		var id = areas[i].id || '';
		if (!hasNumberedWaypointSuffix(id, prefix)) {
			continue;
		}
		return true;
	}
	return false;
}

function hideExcessWaypointGroups(config, desiredCount) {
	if (!config || !config.tbody) {
		return;
	}

	var allRows = Array.from(config.tbody.children || []).filter(function (row) {
		return isWaypointRowForConfig(row, config);
	});

	var groupSize = getTemplateGroupSize(config);
	var maxVisibleRows = (desiredCount - 1) * groupSize + 1;

	NAVLOG_DEBUG && console.log('[hideExcess] ' + config.key + ' - desiredCount: ' + desiredCount + ', groupSize: ' + groupSize + ', maxVisibleRows: ' + maxVisibleRows + ', totalRows: ' + allRows.length);

	var hiddenCount = 0;
	for (var i = maxVisibleRows; i < allRows.length; i++) {
		var row = allRows[i];
		row.style.display = 'none';
		hiddenCount++;
		var textareas = row.getElementsByTagName('textarea');
		for (var j = 0; j < textareas.length; j++) {
			textareas[j].value = '';
		}
	}
	NAVLOG_DEBUG && console.log('[hideExcess] ' + config.key + ' - Hidden ' + hiddenCount + ' rows, visible: ' + (allRows.length - hiddenCount));
}

function showAllWaypointGroups(config) {
	if (!config || !config.tbody) {
		return;
	}

	var allRows = Array.from(config.tbody.children || []);
	for (var i = 0; i < allRows.length; i++) {
		if (isWaypointRowForConfig(allRows[i], config)) {
			allRows[i].style.display = '';
		}
	}
}

function showHiddenWaypointGroups(config, desiredCount) {
	if (!config || !config.tbody) {
		return;
	}
	var allGroups = collectWaypointGroups(config);

	for (var i = 0; i < Math.min(desiredCount, allGroups.length); i++) {
		var group = allGroups[i];
		for (var j = 0; j < group.length; j++) {
			group[j].style.display = '';
		}
	}
}

function collectAllWaypointGroups(config) {
	if (!config || !config.tbody) {
		return [];
	}
	var rows = Array.from(config.tbody.children || []).filter(function (row) {
		return isWaypointRowForConfig(row, config);
	});
	if (!rows.length) {
		return [];
	}

	var groupSize = getTemplateGroupSize(config);
	var groups = [];
	var index = 0;

	while (index < rows.length) {
		var group = [];
		for (var i = 0; i < groupSize && index < rows.length; i++) {
			group.push(rows[index]);
			index++;
		}
		if (group.length === groupSize) {
			groups.push(group);
		}
	}
	return groups;
}

function collectWaypointGroups(config) {
	if (!config || !config.tbody) {
		return [];
	}
	var rows = Array.from(config.tbody.children || []).filter(function (row) {
		if (!isWaypointRowForConfig(row, config)) {
			return false;
		}
		if (row.style.display === 'none') {
			return false;
		}
		return true;
	});
	if (!rows.length) {
		return [];
	}

	var groupSize = getTemplateGroupSize(config);
	var groups = [];
	var index = 0;

	while (index < rows.length) {
		var group = [];
		for (var i = 0; i < groupSize && index < rows.length; i++) {
			group.push(rows[index]);
			index++;
		}
		if (group.length === groupSize) {
			groups.push(group);
		}
	}
	return groups;
}

function renumberWaypointIdentifiers(config) {
	if (!config) {
		return;
	}
	var prefix = 'Waypoints-' + config.key.toUpperCase() + '-';
	var groupSize = getTemplateGroupSize(config);
	var dataRows = getWaypointDataRows(config);
	if (!dataRows.length || !groupSize) {
		return;
	}
	var counters = Object.create(null);
	for (var i = 0; i < dataRows.length; i++) {
		var row = dataRows[i];
		if (!row || !row.querySelectorAll) {
			continue;
		}
		var areas = row.querySelectorAll('textarea[id]');
		for (var j = 0; j < areas.length; j++) {
			var id = areas[j].id || '';
			if (!hasNumberedWaypointSuffix(id, prefix)) {
				continue;
			}
			var info = parseWaypointId(id);
			if (!info || !info.base) {
				continue;
			}
			var base = info.base;
			var next = (counters[base] || 0) + 1;
			counters[base] = next;
			areas[j].id = base + '-' + next;
		}
	}
	waypointIdCounters = Object.create(null);
	initializeWaypointIdCounters();
}

function resetWaypointGroupValues(groupRows) {
	if (!groupRows || !groupRows.length) {
		return;
	}
	for (var i = 0; i < groupRows.length; i++) {
		var row = groupRows[i];
		if (!row || !row.querySelectorAll) {
			continue;
		}
		var areas = row.querySelectorAll('textarea');
		for (var j = 0; j < areas.length; j++) {
			areas[j].value = '';
		}
	}
}

function setWaypointGroupField(groupRows, prefix, value) {
	if (!groupRows || !groupRows.length || !prefix) {
		return;
	}
	var text = (value === null || value === undefined) ? '' : String(value);
	for (var i = 0; i < groupRows.length; i++) {
		var row = groupRows[i];
		if (!row || !row.querySelectorAll) {
			continue;
		}
		var areas = row.querySelectorAll('textarea[id]');
		for (var j = 0; j < areas.length; j++) {
			if (areas[j].id && hasNumberedWaypointSuffix(areas[j].id, prefix)) {
				areas[j].value = text;
			}
		}
	}
}

function setWaypointGroupSingleField(groupRows, prefix, value) {
	if (!groupRows || !groupRows.length || !prefix) {
		return;
	}
	var text = (value === null || value === undefined) ? '' : String(value);
	for (var i = 0; i < groupRows.length; i++) {
		var row = groupRows[i];
		if (!row || !row.querySelectorAll) {
			continue;
		}
		var areas = row.querySelectorAll('textarea[id]');
		for (var j = 0; j < areas.length; j++) {
			var area = areas[j];
			if (area.id && hasNumberedWaypointSuffix(area.id, prefix)) {
				area.value = text;
				return;
			}
		}
	}
}

// ============================================================================
// FORMATTING HELPERS
// ============================================================================
function formatDistanceValue(value, includeZero) {
	if (!Number.isFinite(value)) {
		return includeZero ? '0' : '';
	}
	if (value <= 0) {
		return includeZero ? '0' : '';
	}
	// Immer auf ganze Meilen runden (luftfahrtüblich)
	return String(Math.round(value));
}

function formatAltitudeValue(value) {
	// Konvertiere String zu Zahl falls nötig
	if (typeof value === 'string') {
		value = parseFloat(value);
	}
	if (!Number.isFinite(value)) {
		return '';
	}
	return String(Math.round(value));
}

function getNavlogFieldValue(id) {
	if (!id) return '';
	var element = document.getElementById(id);
	if (!element) return '';
	return element.value || '';
}

// Liste der OFP-gemappten Felder die NICHT von anderen Funktionen überschrieben werden sollen
var OFP_PROTECTED_FIELDS = [
	'Cruise-Alt',
	'Destination-Remarks',
	'Departure-Name',
	'Arrival-Name',
	'Departure-Elev',
	'Arrival-Elev',
	'Departure-RWY',
	'Arrival-RWY',
	'Departure-Sid',
	'Arrival-Star',
	'Destination-Airport',
	'Route-of-flight',
	'Destination-Hours',
	'Destination-Minutes',
	'Alternate-Airport',
	'Alternate-Route',
	'Alternate-Hours',
	'Alternate-Minutes',
	'Aircraft-identification',
	'Aircraft-Type-Equipment',
	'True-Airspeed',
	'Dep-Est',
	'Waypoints-IFR-GPH',
	'Waypoints-VFR-GPH',
	'Waypoints-ZY-GPH',
	'Waypoints-IFR-Dist'
];

// Setzt ein Navlog-Feld NUR wenn es leer ist (schützt OFP-gemappte Werte)
function setNavlogFieldValueIfEmpty(id, value) {
	if (!id) return;
	// Prüfen ob Feld bereits einen Wert hat
	var existing = getNavlogFieldValue(id);
	if (existing && existing.trim() !== '') {
		NAVLOG_DEBUG && console.log('[Navlog FieldSetter] PROTECTED - Field', id, 'already has value:', existing);
		return;
	}
	setNavlogFieldValue(id, value);
}

function setNavlogFieldValue(id, value) {
	NAVLOG_DEBUG && console.log('[Navlog FieldSetter] setNavlogFieldValue called - id:', id, 'value:', value);
	if (!id) {
		NAVLOG_DEBUG && console.log('[Navlog FieldSetter] ERROR: No id provided');
		return;
	}
	var element = document.getElementById(id);
	if (!element) {
		NAVLOG_DEBUG && console.log('[Navlog FieldSetter] ERROR: Element not found for id:', id);
		return;
	}
	NAVLOG_DEBUG && console.log('[Navlog FieldSetter] Element found, type:', element.tagName, 'current value:', element.value);
	if (value === null || value === undefined) {
		element.value = '';
		NAVLOG_DEBUG && console.log('[Navlog FieldSetter] Set to empty string (value was null/undefined)');
		return;
	}
	element.value = String(value);
	NAVLOG_DEBUG && console.log('[Navlog FieldSetter] ✅ Value set successfully to:', element.value);
}

function formatHeadingValue(value) {
	if (!Number.isFinite(value)) {
		return '';
	}
	var normalized = value % 360;
	if (normalized < 0) {
		normalized += 360;
	}
	var rounded = Math.round(normalized);
	if (rounded === 360) {
		rounded = 0;
	}
	return rounded.toString().padStart(3, '0');
}

function toRadians(value) {
	return value * Math.PI / 180;
}

function hasValidCoordinates(entry) {
	return entry && Number.isFinite(entry.lat) && Number.isFinite(entry.lng);
}

// ============================================================================
// NAVIGATION CALCULATIONS
// ============================================================================
function calculateGreatCircleDistanceNM(start, end) {
	if (!hasValidCoordinates(start) || !hasValidCoordinates(end)) {
		return 0;
	}
	var lat1 = toRadians(start.lat);
	var lat2 = toRadians(end.lat);
	var dLat = toRadians(end.lat - start.lat);
	var dLng = toRadians(end.lng - start.lng);
	var sinLat = Math.sin(dLat / 2);
	var sinLng = Math.sin(dLng / 2);
	var a = sinLat * sinLat + Math.cos(lat1) * Math.cos(lat2) * sinLng * sinLng;
	var c = 2 * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a));
	var earthRadiusNm = 3440.065;
	return earthRadiusNm * c;
}

function calculateInitialBearingDegrees(start, end) {
	if (!hasValidCoordinates(start) || !hasValidCoordinates(end)) {
		return null;
	}
	var lat1 = toRadians(start.lat);
	var lat2 = toRadians(end.lat);
	var dLng = toRadians(end.lng - start.lng);
	var y = Math.sin(dLng) * Math.cos(lat2);
	var x = Math.cos(lat1) * Math.sin(lat2) - Math.sin(lat1) * Math.cos(lat2) * Math.cos(dLng);
	var bearing = Math.atan2(y, x) * (180 / Math.PI);
	return bearing;
}

function buildFlightplanLegMetrics(points) {
	var info = {
		legs: [],
		remainingDistances: [],
		totalDistance: 0
	};
	if (!Array.isArray(points) || !points.length) {
		return info;
	}
	for (var i = 0; i < points.length - 1; i++) {
		var legDistance = calculateGreatCircleDistanceNM(points[i], points[i + 1]);
		var legBearing = calculateInitialBearingDegrees(points[i], points[i + 1]);
		info.legs[i] = {
			distance: legDistance,
			bearing: legBearing
		};
		if (Number.isFinite(legDistance)) {
			info.totalDistance += legDistance;
		}
	}
	info.legs[points.length - 1] = {
		distance: 0,
		bearing: null
	};
	var remaining = info.totalDistance;
	for (var j = 0; j < points.length; j++) {
		if (j > 0) {
			var prevLeg = info.legs[j - 1];
			if (prevLeg && Number.isFinite(prevLeg.distance)) {
				remaining = Math.max(0, remaining - prevLeg.distance);
			}
		}
		info.remainingDistances[j] = remaining;
	}
	return info;
}

// ============================================================================
// WAYPOINT FORMATTING
// ============================================================================
function getWaypointRunwayLabel(point) {
	if (!point) {
		return '';
	}
	var number = point.runwayNumberFP || point.RunwayNumberFP || point.runwaynumberfp || '';
	var designator = point.runwayDesignatorFP || point.RunwayDesignatorFP || point.runwaydesignatorfp || '';
	var parts = [];
	if (number) {
		parts.push(String(number));
	}
	if (designator) {
		parts.push(String(designator));
	}
	return parts.join('');
}

function formatWaypointCheckpointLabel(point) {
	if (!point) {
		return '';
	}
	var typeSource = point.atcWaypointType || point.waypointType || '';
	var normalizedType = typeSource.toLowerCase();
	var typeLabel = typeSource || point.name || '';
	// Abkürzungen für lange Waypoint-Typen
	if (normalizedType === 'intersection') {
		typeLabel = 'Int.';
	}
	var lines = [];
	if (normalizedType === 'airport') {
		if (typeLabel) {
			lines.push(typeLabel);
		}
		var runwayLabel = getWaypointRunwayLabel(point);
		if (runwayLabel) {
			lines.push('RWY ' + runwayLabel);
		}
	}
	else if (normalizedType === 'intersection') {
		var proc = formatWaypointProcedureLabel(point);
		var airway = point.airway || point.ATCAirway || point.atcAirway || point.atcairway || '';
		// Kompakte zweizeilige Darstellung: "Int." auf Zeile 1, Prozedur/Airway auf Zeile 2
		lines.push(typeLabel);
		if (proc && airway) {
			lines.push(proc);
			lines.push(airway);
		} else if (proc) {
			lines.push(proc);
		} else if (airway) {
			lines.push(airway);
		}
	}
	else if (typeLabel) {
		lines.push(typeLabel);
	}
	var airway = point.airway || point.ATCAirway || point.atcAirway || point.atcairway || '';
	if (airway && normalizedType === 'vor') {
		lines.push(airway);
	}
	return lines.join('\n');
}

function formatWaypointIdentifier(point) {
	if (!point) {
		return '';
	}
	if (point.departurePosition) {
		return point.departurePosition;
	}
	var waypointName = point.name || '';
	var waypointType = point.waypointType || point.ATCWaypointType || '';
	if (waypointName && waypointType) {
		if (waypointType.indexOf(waypointName) !== -1) {
			return waypointType;
		}
		return waypointName + ' ' + waypointType;
	}
	return waypointName || waypointType || (point.id || '');
}

function deriveWaypointLabel(point) {
	if (!point || typeof point !== 'object') {
		return '';
	}
	var identifier = formatWaypointIdentifier(point);
	if (identifier) {
		return identifier;
	}
	var candidates = [];
	if (point.DepartureFP || point.departureFP) {
		candidates.push(point.DepartureFP || point.departureFP);
	}
	if (point.ArrivalFP || point.arrivalFP) {
		candidates.push(point.ArrivalFP || point.arrivalFP);
	}
	if (point.name) {
		candidates.push(point.name);
	}
	if (point.ICAO && point.ICAO.ICAOIdent) {
		candidates.push(point.ICAO.ICAOIdent);
	}
	if (point.id) {
		candidates.push(point.id);
	}
	var typeLabel = point.waypointType || point.ATCWaypointType || point.sourceAtcWaypointType || '';
	if (typeLabel) {
		candidates.push(typeLabel);
	}
	for (var i = 0; i < candidates.length; i++) {
		var value = candidates[i];
		if (typeof value === 'string') {
			var trimmed = value.trim();
			if (trimmed.length) {
				return trimmed;
			}
		}
	}
	return '';
}

function buildRouteSummary(points) {
	if (!Array.isArray(points) || !points.length) {
		return '';
	}
	var labels = points.map(function (entry) {
		return entry && entry.name ? entry.name : null;
	}).filter(function (value) {
		return !!value;
	});
	return labels.join(' ');
}

function formatWaypointProcedureLabel(point) {
	if (!point) {
		return '';
	}
	var departure = point.departureFP || point.DepartureFP || point.departurefp;
	var arrival = point.arrivalFP || point.ArrivalFP || point.arrivalfp;
	if (departure) {
		return departure;
	}
	if (arrival) {
		return arrival;
	}
	return '';
}

function deriveCruiseAltitude(points) {
	if (!Array.isArray(points) || !points.length) {
		return '';
	}
	var maxAltitude = null;
	for (var i = 0; i < points.length; i++) {
		var entry = points[i];
		if (entry && Number.isFinite(entry.altitude)) {
			if (!Number.isFinite(maxAltitude) || entry.altitude > maxAltitude) {
				maxAltitude = entry.altitude;
			}
		}
	}
	if (!Number.isFinite(maxAltitude)) {
		return '';
	}
	if (maxAltitude >= 1000) {
		return 'FL' + String(Math.round(maxAltitude / 100));
	}
	return String(Math.round(maxAltitude));
}

// ============================================================================
// FLIGHTPLAN POPULATION
// ============================================================================
function populateWaypointRows(config, points, metrics, fieldPrefix, meta) {
	if (!config) {
		return;
	}
	var groups = collectWaypointGroups(config);
	if (!groups.length) {
		return;
	}
	for (var i = 0; i < groups.length; i++) {
		var group = groups[i];
		resetWaypointGroupValues(group);
		var waypoint = points[i];
		if (!waypoint) {
			continue;
		}
		setWaypointGroupField(group, fieldPrefix + 'CP-', formatWaypointCheckpointLabel(waypoint));
		setWaypointGroupField(group, fieldPrefix + 'Ident-', waypoint.name || '');
		setWaypointGroupField(group, fieldPrefix + 'ALT-', formatAltitudeValue(waypoint.altitude));
		if (waypoint.SpeedMaxFP !== undefined && waypoint.SpeedMaxFP !== null) {
			setWaypointGroupSingleField(group, fieldPrefix + 'Est-', String(Math.round(waypoint.SpeedMaxFP)));
		}

		var leg = metrics && metrics.legs ? metrics.legs[i] : null;
		if (leg) {
			var headingText = formatHeadingValue(leg.bearing);
			var legDistanceText = formatDistanceValue(leg.distance, true);
			setWaypointGroupSingleField(group, fieldPrefix + 'CRS-', headingText);
			setWaypointGroupSingleField(group, fieldPrefix + 'MH-', headingText);
			setWaypointGroupSingleField(group, fieldPrefix + 'Leg-', legDistanceText);
		}
		else {
			setWaypointGroupSingleField(group, fieldPrefix + 'CRS-', '');
			setWaypointGroupSingleField(group, fieldPrefix + 'MH-', '');
			setWaypointGroupSingleField(group, fieldPrefix + 'Leg-', '');
		}
		if (metrics && metrics.remainingDistances) {
			var remaining = metrics.remainingDistances[i];
			setWaypointGroupSingleField(group, fieldPrefix + 'Dist-Rem-', formatDistanceValue(remaining, true));
		}
		else {
			setWaypointGroupSingleField(group, fieldPrefix + 'Dist-Rem-', '');
		}
	}

	// Fetch and populate navaid frequencies asynchronously
	fetchAllNavaidFrequencies(points, function(frequencies) {
		for (var i = 0; i < frequencies.length; i++) {
			if (frequencies[i] && groups[i]) {
				setWaypointGroupSingleField(groups[i], fieldPrefix + 'Freq-', frequencies[i]);
			}
		}
		// Save after frequencies are populated
		if (typeof saveValues === 'function') {
			saveValues();
		}
	});

	// Populate weather data and flight data from Simbrief if available
	for (var i = 0; i < points.length; i++) {
		var wp = points[i];
		if (!wp || !groups[i]) {
			continue;
		}
		// Use Simbrief weather data (Wind_dir, Wind_spd, Oat)
		var windDir = wp.Wind_dir || wp.wind_dir;
		var windSpd = wp.Wind_spd || wp.wind_spd;
		var temp = wp.Oat || wp.oat;
		if (windDir !== undefined && windDir !== null && windDir !== '') {
			// VRB (Variable) als Text behalten, sonst als Zahl formatieren
			if (String(windDir).toUpperCase() === 'VRB') {
				setWaypointGroupSingleField(groups[i], fieldPrefix + 'Dir-', 'VRB');
			} else {
				var windDirNum = parseFloat(windDir);
				if (!isNaN(windDirNum)) {
					setWaypointGroupSingleField(groups[i], fieldPrefix + 'Dir-', String(Math.round(windDirNum)));
				}
			}
		}
		if (windSpd !== undefined && windSpd !== null && windSpd !== '') {
			var windSpdNum = parseFloat(windSpd);
			if (!isNaN(windSpdNum)) {
				setWaypointGroupSingleField(groups[i], fieldPrefix + 'Vel-', String(Math.round(windSpdNum)));
			}
		}
		if (temp !== undefined && temp !== null && temp !== '') {
			var tempNum = parseFloat(temp);
			if (!isNaN(tempNum)) {
				setWaypointGroupSingleField(groups[i], fieldPrefix + 'Temp-', String(Math.round(tempNum)));
			}
		}
		// Ground Speed (Est = Estimated GS)
		// Erster (Departure) und letzter (Arrival) Waypoint = 0
		if (i === 0 || i === points.length - 1) {
			setWaypointGroupSingleField(groups[i], fieldPrefix + 'Est-', '0');
		} else {
			var gs = wp.Groundspeed || wp.groundspeed;
			if (gs !== undefined && gs !== null && gs !== '') {
				setWaypointGroupSingleField(groups[i], fieldPrefix + 'Est-', String(Math.round(parseFloat(gs))));
			}
		}
		// ETE (Estimated Time Enroute for this leg) - Minuten zu HHMM formatieren (luftfahrtüblich)
		var ete = wp.Ete || wp.ete;
		if (ete !== undefined && ete !== null && ete !== '') {
			var eteMinutes = parseInt(ete);
			// ETE als 4-stelliges HHMM Format (z.B. 0045 für 45 Min, 0130 für 1h 30min)
			var hours = ('0' + Math.floor(eteMinutes / 60)).slice(-2);
			var mins = ('0' + (eteMinutes % 60)).slice(-2);
			var eteFormatted = hours + mins;  // Format: HHMM
			setWaypointGroupSingleField(groups[i], fieldPrefix + 'ETE-', eteFormatted);
		} else if (i === 0) {
			// Erster Waypoint (Departure) hat ETE 0000
			setWaypointGroupSingleField(groups[i], fieldPrefix + 'ETE-', '0000');
		}
		// ETA (Estimated Time of Arrival) - Uhrzeit berechnen aus Abflugzeit + Time_total (HHMM Format)
		var eta = wp.Eta || wp.eta;
		if (eta !== undefined && eta !== null && eta !== '') {
			var etaMinutes = parseInt(eta);
			// ETA als Uhrzeit: Abflugzeit + Gesamtzeit (4-stellig HHMM)
			if (meta && meta.departureTime) {
				var depTimestamp = parseInt(meta.departureTime) * 1000;
				var etaTimestamp = depTimestamp + (etaMinutes * 60 * 1000);
				var etaDate = new Date(etaTimestamp);
				var etaFormatted = ('0' + etaDate.getUTCHours()).slice(-2) + ('0' + etaDate.getUTCMinutes()).slice(-2);
				setWaypointGroupSingleField(groups[i], fieldPrefix + 'ETA-', etaFormatted);
			} else {
				// Fallback: nur die Gesamtzeit seit Start (4-stellig HHMM)
				var hours = ('0' + Math.floor(etaMinutes / 60)).slice(-2);
				var mins = ('0' + (etaMinutes % 60)).slice(-2);
				var etaFormatted = hours + mins;
				setWaypointGroupSingleField(groups[i], fieldPrefix + 'ETA-', etaFormatted);
			}
		} else if (i === 0 && meta && meta.departureTime) {
			// Erster Waypoint: Abflugzeit als ETA (4-stellig HHMM)
			var depDate = new Date(parseInt(meta.departureTime) * 1000);
			var depFormatted = ('0' + depDate.getUTCHours()).slice(-2) + ('0' + depDate.getUTCMinutes()).slice(-2);
			setWaypointGroupSingleField(groups[i], fieldPrefix + 'ETA-', depFormatted);
		}
		// Fuel Burn (Burn) - Feld heißt Burn-1, Burn-2, etc.
		var fuelBurn = wp.Fuel_burn || wp.fuel_burn;
		if (fuelBurn !== undefined && fuelBurn !== null && fuelBurn !== '') {
			setWaypointGroupSingleField(groups[i], fieldPrefix + 'Burn-', String(Math.round(parseFloat(fuelBurn))));
		} else if (i === 0) {
			// Erster Waypoint (Departure) hat Burn 0
			setWaypointGroupSingleField(groups[i], fieldPrefix + 'Burn-', '0');
		}
		// Fuel Remaining - Feld heißt Fuel-Rem-1, Fuel-Rem-2, etc.
		var fuelRem = wp.Fuel_rem || wp.fuel_rem;
		if (fuelRem !== undefined && fuelRem !== null && fuelRem !== '') {
			setWaypointGroupSingleField(groups[i], fieldPrefix + 'Fuel-Rem-', String(Math.round(parseFloat(fuelRem))));
		}
		// Magnetic Variation / Deviation (Dev) - Feld heißt Dev-1, Dev-2, etc.
		// Mag_var aus Simbrief gibt die magnetische Variation für diesen Waypoint an
		var magVar = wp.Mag_var || wp.mag_var;
		if (magVar !== undefined && magVar !== null && magVar !== '') {
			var magVarNum = parseFloat(magVar);
			// Format: mit Gradsymbol, z.B. "3°E" oder "15°W" (luftfahrtüblich)
			var devFormatted;
			var absValue = Math.abs(Math.round(magVarNum));
			if (magVarNum >= 0) {
				devFormatted = absValue + '°E';
			} else {
				devFormatted = absValue + '°W';
			}
			setWaypointGroupSingleField(groups[i], fieldPrefix + 'Dev-', devFormatted);
		}
		// Total Time - Gesamtzeit seit Departure
		var totalTime = wp.Time_total || wp.time_total || wp.Eta || wp.eta;
		if (totalTime !== undefined && totalTime !== null && totalTime !== '') {
			var totalMinutes = parseInt(totalTime);
			var totalFormatted;
			if (totalMinutes < 60) {
				totalFormatted = '0:' + ('0' + totalMinutes).slice(-2);
			} else {
				totalFormatted = Math.floor(totalMinutes / 60) + ':' + ('0' + (totalMinutes % 60)).slice(-2);
			}
			setWaypointGroupSingleField(groups[i], fieldPrefix + 'Total-Time-', totalFormatted);
		} else if (i === 0) {
			setWaypointGroupSingleField(groups[i], fieldPrefix + 'Total-Time-', '0:00');
		}
		// Total GPH - Fuel Flow / GPH (Gallons per Hour)
		var fuelFlow = wp.Fuel_flow || wp.fuel_flow;
		if (fuelFlow !== undefined && fuelFlow !== null && fuelFlow !== '') {
			setWaypointGroupSingleField(groups[i], fieldPrefix + 'Total-GPH-', String(Math.round(parseFloat(fuelFlow))));
		}
	}
}

function populateIfrWaypointRows(config, points, metrics, meta) {
	populateWaypointRows(config, points, metrics, 'Waypoints-IFR-', meta);
}

function populateVfrWaypointRows(config, points, metrics, meta) {
	populateWaypointRows(config, points, metrics, 'Waypoints-VFR-', meta);
}

function populateZyWaypointRows(config, points, metrics, meta) {
	populateWaypointRows(config, points, metrics, 'Waypoints-ZY-', meta);
}

// ============================================================================
// STATIC & DYNAMIC DATA MANAGEMENT
// ============================================================================
function populateFlightplanSummaryFields(points, metrics, meta) {
	NAVLOG_DEBUG && console.log('[Navlog Summary] ====== populateFlightplanSummaryFields CALLED ======');
	NAVLOG_DEBUG && console.log('[Navlog Summary] points:', Array.isArray(points) ? points.length + ' waypoints' : 'NOT ARRAY');
	NAVLOG_DEBUG && console.log('[Navlog Summary] meta:', meta);
	NAVLOG_DEBUG && console.log('[Navlog Summary] meta keys:', meta ? Object.keys(meta) : 'NULL');

	var summaryMeta = (meta && typeof meta === 'object') ? meta : {};
	NAVLOG_DEBUG && console.log('[Navlog Summary] summaryMeta after processing:', summaryMeta);
	NAVLOG_DEBUG && console.log('[Navlog Summary] OFP fields to set:');
	NAVLOG_DEBUG && console.log('[Navlog Summary]   - callsign:', summaryMeta.callsign);
	NAVLOG_DEBUG && console.log('[Navlog Summary]   - aircraftType:', summaryMeta.aircraftType);
	NAVLOG_DEBUG && console.log('[Navlog Summary]   - aircraftEquip:', summaryMeta.aircraftEquip);
	NAVLOG_DEBUG && console.log('[Navlog Summary]   - departureTime:', summaryMeta.departureTime);
	NAVLOG_DEBUG && console.log('[Navlog Summary]   - alternateAirport:', summaryMeta.alternateAirport);
	console.log('[Fuel Summary] enduranceHours:', summaryMeta.enduranceHours);
	console.log('[Fuel Summary] enduranceMinutes:', summaryMeta.enduranceMinutes);
	console.log('[Fuel Summary] avgFuelFlow:', summaryMeta.avgFuelFlow);

	var departure = Array.isArray(points) && points.length ? points[0] : null;
	var destination = Array.isArray(points) && points.length ? points[points.length - 1] : null;
	var departureLabel = summaryMeta.departureName || summaryMeta.departureId || deriveWaypointLabel(departure);
	var arrivalLabel = summaryMeta.destinationName || summaryMeta.destinationId || summaryMeta.arrivalName || deriveWaypointLabel(destination);
	// OFP-geschützte Felder: NUR setzen wenn leer (OFP-Mapping hat Priorität!)
	setNavlogFieldValueIfEmpty('Departure-Name', departureLabel || '');
	setNavlogFieldValueIfEmpty('Arrival-Name', arrivalLabel || '');
	// Departure-Point = Punkt an dem man die CTR verlässt (Ende der SID / erster Enroute Fix)
	if (summaryMeta.departurePoint) {
		setNavlogFieldValueIfEmpty('Departure-Point', summaryMeta.departurePoint);
	}
	// Departure Elevation
	if (summaryMeta.departureElevation !== undefined && summaryMeta.departureElevation !== null) {
		setNavlogFieldValueIfEmpty('Departure-Elev', summaryMeta.departureElevation + ' ft');
	}
	// Arrival Elevation
	if (summaryMeta.arrivalElevation !== undefined && summaryMeta.arrivalElevation !== null) {
		setNavlogFieldValueIfEmpty('Arrival-Elev', summaryMeta.arrivalElevation + ' ft');
	}
	// Departure RWY
	if (summaryMeta.departureRwy) {
		setNavlogFieldValueIfEmpty('Departure-RWY', summaryMeta.departureRwy);
	}
	// Arrival RWY
	if (summaryMeta.arrivalRwy) {
		setNavlogFieldValueIfEmpty('Arrival-RWY', summaryMeta.arrivalRwy);
	}
	// Departure SID
	if (summaryMeta.departureSid) {
		setNavlogFieldValueIfEmpty('Departure-Sid', summaryMeta.departureSid);
	}
	// Arrival STAR
	if (summaryMeta.arrivalStar) {
		setNavlogFieldValueIfEmpty('Arrival-Star', summaryMeta.arrivalStar);
	}
	// HINWEIS: Airport und ATIS Advisory Felder (Wind, Ceiling, Altimeter, Time Check)
	// werden NICHT automatisch gefüllt - diese trägt der Pilot vor dem Flug selbst ein
	if (!Array.isArray(points) || !points.length) {
		setNavlogFieldValueIfEmpty('Route-of-flight', '');
		var emptyTotal = metrics && Number.isFinite(metrics.totalDistance) ? metrics.totalDistance : 0;
		setNavlogFieldValueIfEmpty('Waypoints-IFR-Dist', formatDistanceValue(emptyTotal, true));
		return;
	}
	departure = points[0];
	destination = points[points.length - 1];
	if (departure && summaryMeta.departurePosition && !departure.departurePosition) {
		departure.departurePosition = summaryMeta.departurePosition;
	}
	var destinationAirportParts = [];
	if (summaryMeta.destinationId) {
		destinationAirportParts.push(summaryMeta.destinationId);
	}
	if (arrivalLabel) {
		destinationAirportParts.push(arrivalLabel);
	}
	setNavlogFieldValueIfEmpty('Destination-Airport', destinationAirportParts.join(' - '));
	setNavlogFieldValueIfEmpty('Route-of-flight', buildRouteSummary(points));
	var totalDistance = metrics && Number.isFinite(metrics.totalDistance) ? metrics.totalDistance : 0;
	setNavlogFieldValueIfEmpty('Waypoints-IFR-Dist', formatDistanceValue(totalDistance, true));
	// Cruise-Alt: OFP-Mapping hat Priorität
	setNavlogFieldValueIfEmpty('Cruise-Alt', deriveCruiseAltitude(points));
	// Alternate
	if (summaryMeta.alternateAirport) {
		setNavlogFieldValueIfEmpty('Alternate-Airport', summaryMeta.alternateAirport);
	}
	if (summaryMeta.alternateRoute) {
		setNavlogFieldValueIfEmpty('Alternate-Route', summaryMeta.alternateRoute);
	}
	// Fuel Endurance (Stunden und Minuten)
	if (summaryMeta.enduranceHours !== undefined) {
		setNavlogFieldValue('Alternate-Hours', String(summaryMeta.enduranceHours));
	}
	if (summaryMeta.enduranceMinutes !== undefined) {
		setNavlogFieldValue('Alternate-Minutes', String(summaryMeta.enduranceMinutes));
	}
	// Avg Fuel Flow (GPH)
	if (summaryMeta.avgFuelFlow) {
		setNavlogFieldValue('Waypoints-IFR-GPH', String(Math.round(parseFloat(summaryMeta.avgFuelFlow))));
		setNavlogFieldValue('Waypoints-VFR-GPH', String(Math.round(parseFloat(summaryMeta.avgFuelFlow))));
		setNavlogFieldValue('Waypoints-ZY-GPH', String(Math.round(parseFloat(summaryMeta.avgFuelFlow))));
	}
	// Simbrief-spezifische Felder (FIX: Korrekte HTML-Element-IDs verwenden!)
	if (summaryMeta.callsign) {
		// FIX: 'Aircraft-identification' mit kleinem 'i'
		setNavlogFieldValue('Aircraft-identification', summaryMeta.callsign);
	}
	// FIX: Aircraft Type und Equipment sind in EINEM kombinierten Feld!
	if (summaryMeta.aircraftType || summaryMeta.aircraftEquip) {
		var typeEquip = [];
		if (summaryMeta.aircraftType) {
			typeEquip.push(summaryMeta.aircraftType);
		}
		if (summaryMeta.aircraftEquip) {
			typeEquip.push(summaryMeta.aircraftEquip);
		}
		setNavlogFieldValue('Aircraft-Type-Equipment', typeEquip.join(' '));
	}
	if (summaryMeta.trueAirspeed) {
		setNavlogFieldValue('True-Airspeed', summaryMeta.trueAirspeed);
	}
	// Aircraft Registration
	if (summaryMeta.aircraftReg) {
		setNavlogFieldValue('Aircraft_reg', summaryMeta.aircraftReg);
	}
	// Notes
	if (summaryMeta.notes) {
		setNavlogFieldValue('Notes', summaryMeta.notes);
	}
	// HINWEIS: Destination-Remarks wird NICHT automatisch gefüllt
	// Der Pilot trägt hier eigene Bemerkungen ein
	// Departure Time (Unix-Timestamp → HH:MM formatieren)
	// FIX: Feld heißt 'Dep-Est' (Departure Estimated) nicht 'Departure-Time'!
	if (summaryMeta.departureTime) {
		var depDate = new Date(parseInt(summaryMeta.departureTime) * 1000);
		var depTimeStr = ('0' + depDate.getUTCHours()).slice(-2) + ':' +
		                 ('0' + depDate.getUTCMinutes()).slice(-2);
		setNavlogFieldValue('Dep-Est', depTimeStr);
	}
	// Flugzeit berechnen (est_time_enroute ist in SEKUNDEN von Simbrief!)
	if (summaryMeta.estTimeEnroute) {
		var totalSeconds = parseInt(summaryMeta.estTimeEnroute);
		var totalMinutes = Math.floor(totalSeconds / 60);
		var hours = Math.floor(totalMinutes / 60);
		var minutes = totalMinutes % 60;
		setNavlogFieldValue('Destination-Hours', String(hours));
		setNavlogFieldValue('Destination-Minutes', ('0' + minutes).slice(-2));
	}

	// ========================================
	// WEATHER LOG - All fields are now set by OFP_NAVLOG_MAPPING in ofp-mapping-config.js
	// DO NOT set Weather-Log fields here - they would overwrite the correct OFP values!
	// The OFP mapping provides proper "Ceiling / Visibility / Precipitation" format.
	// ========================================

	NAVLOG_DEBUG && console.log('[Navlog Summary] ====== populateFlightplanSummaryFields COMPLETE ======');
}

// ============================================================================
// FLIGHTPLAN DATA MANAGEMENT
// ============================================================================
function normalizeFlightplanData(list) {
	if (!Array.isArray(list)) {
		return [];
	}
	return list.filter(function (entry) {
		return entry && typeof entry === 'object';
	});
}

function cacheFlightplanData(list, meta) {
	cachedFlightplanData = normalizeFlightplanData(list);
	if (meta && typeof meta === 'object') {
		var baseMeta = (cachedFlightplanMeta && typeof cachedFlightplanMeta === 'object') ? cachedFlightplanMeta : {};
		var merged = Object.assign({}, baseMeta);
		var keys = Object.keys(meta);
		for (var i = 0; i < keys.length; i++) {
			var key = keys[i];
			var value = meta[key];
			if (typeof value !== 'undefined' && value !== null && value !== '') {
				var existing = merged[key];
				var existingHasValue =
					(typeof existing === 'string' && existing.trim().length) ||
					(typeof existing !== 'undefined' && existing !== null && existing !== '');
				if (!existingHasValue) {
					merged[key] = value;
				}
			}
		}
		cachedFlightplanMeta = merged;
	}
	else {
		cachedFlightplanMeta = {};
	}
	return cachedFlightplanData;
}

// Called when user switches to Navlog tab - applies any cached data
// NO POLLING - this is only called from kneeboard.js when tab is opened
function applyCachedFlightplanIfReady() {
	// TEIL 1: Restore from sessionStorage if RAM cache is empty (after page reload)
	if (cachedFlightplanData === null && cachedOfpData === null) {
		try {
			var storedWaypoints = sessionStorage.getItem('kneeboard_cachedFlightplanData');
			var storedMeta = sessionStorage.getItem('kneeboard_cachedFlightplanMeta');
			var storedOfp = sessionStorage.getItem('kneeboard_cachedOfpData');
			if (storedWaypoints || storedOfp) {
				NAVLOG_DEBUG && console.log('[Navlog] Restoring cache from sessionStorage after page reload');
				if (storedWaypoints) cachedFlightplanData = JSON.parse(storedWaypoints);
				if (storedMeta) cachedFlightplanMeta = JSON.parse(storedMeta);
				if (storedOfp) cachedOfpData = JSON.parse(storedOfp);
			}
		} catch (e) {
			console.warn('[Navlog] Failed to restore from sessionStorage:', e);
		}
	}

	NAVLOG_DEBUG && console.log('[Navlog Debug] applyCachedFlightplanIfReady() called - user opened Navlog tab');
	NAVLOG_DEBUG && console.log('[Navlog Debug] cachedFlightplanData:', cachedFlightplanData ? ('Array[' + cachedFlightplanData.length + ']') : 'NULL');
	NAVLOG_DEBUG && console.log('[Navlog Debug] cachedFlightplanMeta:', cachedFlightplanMeta);
	NAVLOG_DEBUG && console.log('[Navlog Debug] cachedOfpData:', cachedOfpData ? 'exists' : 'NULL');

	// Check if DOM is ready
	var navRoot = ensureNavlogContainer();
	if (!navRoot) {
		NAVLOG_DEBUG && console.log('[Navlog Debug] applyCachedFlightplanIfReady - navlog container not ready yet');
		return;
	}

	// OFP-Mapping ausführen wenn DOM jetzt ready ist und OFP-Daten gecacht sind
	if (cachedOfpData && typeof OFP_NAVLOG_MAPPING !== 'undefined' && typeof processOFPWithMapping === 'function') {
		NAVLOG_DEBUG && console.log('[Navlog Debug] Applying OFP mapping now that DOM is ready...');
		var mappedValues = processOFPWithMapping(cachedOfpData, cachedFlightplanData || []);
		applyMappedValuesToNavlog(mappedValues);
		NAVLOG_DEBUG && console.log('[Navlog Debug] OFP mapping applied successfully');

		// KRITISCH: Nach OFP-Mapping sofort zum Server pushen
		// Das überschreibt alte Server-Daten mit den neuen OFP-Werten
		if (typeof saveValuesImmediate === 'function') {
			NAVLOG_DEBUG && console.log('[Navlog Debug] Forcing server push after OFP mapping...');
			saveValuesImmediate();
		}
	}

	if (cachedFlightplanData === null) {
		NAVLOG_DEBUG && console.log('[Navlog Debug] No cached flightplan data - skipping');
		return;
	}
	NAVLOG_DEBUG && console.log('[Navlog Debug] Applying cached flightplan data...');
	var result = applyFlightplanToNavlog(cachedFlightplanData);
	if (result) {
		console.log('[Navlog] Cached flightplan applied successfully (' + cachedFlightplanData.length + ' waypoints)');
	} else {
		NAVLOG_DEBUG && console.log('[Navlog Debug] applyFlightplanToNavlog returned false');
	}
}

function logFlightplanInfo(message, extra) {
	if (!NAVLOG_DEBUG) return;
	try {
		var parts = [flightplanLogPrefix, message];
		if (typeof extra !== 'undefined') {
			parts.push(extra);
		}
		console.log.apply(console, parts);
	}
	catch (err) {
		// logging must never break functionality
	}
}

function handleGlobalFlightplanBroadcast(e) {
	console.log('[Navlog] handleGlobalFlightplanBroadcast called');
	NAVLOG_DEBUG && console.log('[Navlog Debug] handleGlobalFlightplanBroadcast called');
	if (!e || typeof e.data !== 'string') {
		NAVLOG_DEBUG && console.log('[Navlog Debug] No event or data is not string');
		return;
	}
	NAVLOG_DEBUG && console.log('[Navlog Debug] Event data length:', e.data.length);
	var separatorIndex = e.data.indexOf(':');
	if (separatorIndex === -1) {
		NAVLOG_DEBUG && console.log('[Navlog Debug] No separator found in data');
		return;
	}
	var sender = e.data.substr(0, separatorIndex);
	NAVLOG_DEBUG && console.log('[Navlog Debug] Sender:', sender);
	if (sender !== 'Flightplan') {
		NAVLOG_DEBUG && console.log('[Navlog Debug] Sender is not Flightplan, ignoring');
		return;
	}
	var payload = e.data.substr(separatorIndex + 1);
	NAVLOG_DEBUG && console.log('[Navlog Debug] Payload length:', payload.length);
	NAVLOG_DEBUG && console.log('[Navlog Debug] Calling handleFlightplanPayload...');
	handleFlightplanPayload(payload);
}

// ============================================================================
// FLIGHT TYPE MANAGEMENT
// ============================================================================
function activateFlightType(targetKey) {
	var requested = (targetKey || '').toLowerCase();
	if (requested === 'vfr') {
		btnVFRClicked();
		flightType = 'vfr';
		if (typeof localStorage !== 'undefined') {
			// localStorage.setItem("flightType", flightType); // Disabled - using server sync
		}
		return 'vfr';
	}
	if (requested === 'zy') {
		btnZYClicked();
		flightType = 'zy';
		if (typeof localStorage !== 'undefined') {
			// localStorage.setItem("flightType", flightType); // Disabled - using server sync
		}
		return 'zy';
	}
	btnIFRClicked();
	flightType = 'ifr';
	if (typeof localStorage !== 'undefined') {
		// localStorage.setItem("flightType", flightType); // Disabled - using server sync
	}
	return 'ifr';
}

function applyFlightplanToNavlog(flightplan, targetKey) {
	var navRoot = ensureNavlogContainer();
	var activatedKey = activateFlightType(targetKey);
	var config = waypointAutoExpandConfigs[activatedKey] || waypointAutoExpandConfigs.ifr;
	if (!navRoot || !config) {
		NAVLOG_DEBUG && console.log('[Navlog Debug] navRoot:', navRoot ? 'OK' : 'NULL');
		NAVLOG_DEBUG && console.log('[Navlog Debug] activatedKey:', activatedKey);
		NAVLOG_DEBUG && console.log('[Navlog Debug] config:', config ? 'OK' : 'NULL');
		NAVLOG_DEBUG && console.log('[Navlog Debug] waypointAutoExpandConfigs:', waypointAutoExpandConfigs);
		logFlightplanInfo('Navlog not ready or configuration missing.');
		return false;
	}
	if (activatedKey !== 'ifr' && activatedKey !== 'vfr') {
		logFlightplanInfo('Flightplan switched to ' + activatedKey.toUpperCase() + ' view (no auto-import).');
		return true;
	}
	if (!config.tbody || !config.templateRows || !config.templateRows.length) {
		NAVLOG_DEBUG && console.log('[Navlog Debug] config.tbody:', config.tbody ? 'OK' : 'NULL');
		NAVLOG_DEBUG && console.log('[Navlog Debug] config.templateRows:', config.templateRows ? ('Array[' + config.templateRows.length + ']') : 'NULL');
		logFlightplanInfo('Navlog not ready - ' + activatedKey.toUpperCase() + ' blocks missing.');
		return false;
	}
	var normalized = normalizeFlightplanData(flightplan);

	// Beim Löschen: auf 5 Zeilen zurücksetzen
	if (normalized.length === 0) {
		var defaultCount = 5;
		logFlightplanInfo('Flightplan deleted, resetting to default:', defaultCount);

		clearAllNavlogTextareas();

		if (typeof localStorage !== 'undefined') {
			localStorage.removeItem('waypointFixedCount-ifr');
			localStorage.removeItem('waypointFixedCount-vfr');
			localStorage.removeItem('waypointFixedCount-zy');
			clearNavlogLocalStorage();
		}

		cachedFlightplanData = null;
		cachedFlightplanMeta = {};
		cachedOfpData = null;

		// Reset sync hashes um Server-Updates zu ermöglichen
		lastAppliedNavlogHash = null;
		lastBroadcastNavlogHash = null;
		lastServerNavlogHash = null;
		lastLocalNavlogHash = null;

		waypointFixedCountSettings[activatedKey] = defaultCount;

		receiving = true;
		setWaypointFixedCount(activatedKey, defaultCount);
		receiving = false;

		var emptyMetrics = buildFlightplanLegMetrics([]);
		populateFlightplanSummaryFields([], emptyMetrics, {});

		// Alle NavLog-Felder löschen (nur OFP-gefüllte Felder)
		setNavlogFieldValue('Departure-Name', '');
		setNavlogFieldValue('Arrival-Name', '');
		setNavlogFieldValue('Departure-Point', '');
		setNavlogFieldValue('Departure-Elev', '');
		setNavlogFieldValue('Arrival-Elev', '');
		setNavlogFieldValue('Departure-RWY', '');
		setNavlogFieldValue('Arrival-RWY', '');
		setNavlogFieldValue('Departure-Sid', '');
		setNavlogFieldValue('Arrival-Star', '');
		// HINWEIS: ATIS Advisory Felder (Wind, Ceil, Alt, TC) werden NICHT geleert
		// da diese vom Piloten manuell eingetragen werden
		setNavlogFieldValue('Destination-Airport', '');
		setNavlogFieldValue('Destination-Hours', '');
		setNavlogFieldValue('Destination-Minutes', '');
		setNavlogFieldValue('Route-of-flight', '');
		setNavlogFieldValue('Cruise-Alt', '');
		setNavlogFieldValue('Alternate-Airport', '');
		setNavlogFieldValue('Alternate-Route', '');
		setNavlogFieldValue('Alternate-Hours', '');
		setNavlogFieldValue('Alternate-Minutes', '');
		setNavlogFieldValue('Aircraft-identification', '');
		setNavlogFieldValue('Aircraft-Type-Equipment', '');
		setNavlogFieldValue('Aircraft_reg', '');
		setNavlogFieldValue('True-Airspeed', '');
		setNavlogFieldValue('Dep-Est', '');
		setNavlogFieldValue('Notes', '');
		setNavlogFieldValue('Destination-Remarks', '');
		setNavlogFieldValue('Waypoints-IFR-GPH', '');
		setNavlogFieldValue('Waypoints-VFR-GPH', '');
		setNavlogFieldValue('Waypoints-ZY-GPH', '');
		setNavlogFieldValue('Waypoints-IFR-Dist', '');

		saveValues();
		console.log('[Navlog] Flightplan reset completed - all fields cleared');
		return true;
	}

	// Beim Import: Zeilen erweitern
	var desiredRows = Math.max(1, normalized.length + 1);
	logFlightplanInfo('Flightplan ' + activatedKey.toUpperCase() + ' row request:', desiredRows);
	receiving = true;
	setWaypointFixedCount(activatedKey, desiredRows);
	receiving = false;
	renumberWaypointIdentifiers(config);
	updateWaypointPlaceholder(config);
	var legMetrics = buildFlightplanLegMetrics(normalized);
	if (activatedKey === 'vfr') {
		populateVfrWaypointRows(config, normalized, legMetrics, cachedFlightplanMeta);
	} else {
		populateIfrWaypointRows(config, normalized, legMetrics, cachedFlightplanMeta);
	}
	populateFlightplanSummaryFields(normalized, legMetrics, cachedFlightplanMeta);
	saveValues();
	logFlightplanInfo(activatedKey.toUpperCase() + ' rows adjusted.');
	return true;
}

function parseFlightplanPayload(payload) {
	console.log('[parseFlightplanPayload] ====== FUNCTION CALLED ======');
	console.log('[parseFlightplanPayload] payload type:', typeof payload);
	console.log('[parseFlightplanPayload] payload keys:', payload ? Object.keys(payload) : 'NULL');
	console.log('[parseFlightplanPayload] has ofp?', !!(payload && payload.ofp));

	var info = {
		waypoints: [],
		flightType: null,
		meta: {},
		forceReset: false,
		ofpData: null  // OFP-Daten für Mapping
	};
	if (Array.isArray(payload)) {
		console.log('[parseFlightplanPayload] payload is Array - returning early');
		info.waypoints = payload;
		return info;
	}
	if (!payload || typeof payload !== 'object') {
		console.log('[parseFlightplanPayload] payload is invalid - returning early');
		return info;
	}
	var root = payload;
	var ofpData = null;

	// Prüfen ob kombiniertes Format (pln + ofp) von Simbrief
	if (payload.pln && typeof payload.pln === 'object') {
		root = payload.pln;
		ofpData = payload.ofp || null;
		console.log('[parseFlightplanPayload] Found pln+ofp format, ofpData:', ofpData ? 'exists' : 'NULL');
	} else if (payload.ofp) {
		// OFP-Daten direkt im Payload (z.B. von kneeboard.js konvertiert)
		ofpData = payload.ofp;
		console.log('[parseFlightplanPayload] Found direct ofp format, ofpData:', ofpData ? 'exists' : 'NULL');
	} else {
		console.log('[parseFlightplanPayload] No ofp data found in payload');
	}

	// OFP-Daten für Mapping speichern
	info.ofpData = ofpData;

	var nested =
		root.FlightPlanFlightPlan ||
		root.flightPlan ||
		root.flightplan ||
		root.FlightPlan ||
		null;
	if (nested && typeof nested === 'object') {
		root = nested;
	}
	var points = root.waypoints || root.points || root.route || root.data || root.ATCWaypoint;
	if (Array.isArray(points)) {
		info.waypoints = points;
	}
	var metaSource = (root.meta && typeof root.meta === 'object') ? Object.assign({}, root.meta) : {};

	// OFP-Daten in metaSource übernehmen (Simbrief-spezifisch)
	console.log('[OFP] ofpData:', ofpData);
	console.log('[OFP] ofpData keys:', ofpData ? Object.keys(ofpData) : 'NULL');
	NAVLOG_DEBUG && console.log('[OFP Debug] ofpData value:', ofpData);
	NAVLOG_DEBUG && console.log('[OFP Debug] ofpData type:', typeof ofpData);
	NAVLOG_DEBUG && console.log('[OFP Debug] ofpData is null?', ofpData === null);
	NAVLOG_DEBUG && console.log('[OFP Debug] ofpData is undefined?', ofpData === undefined);

	if (ofpData && typeof ofpData === 'object') {
		NAVLOG_DEBUG && console.log('[OFP Debug] Inside ofpData block - ofpData keys:', Object.keys(ofpData));
		NAVLOG_DEBUG && console.log('[OFP Debug] Has Atc?', !!ofpData.Atc, 'Has atc?', !!ofpData.atc);
		NAVLOG_DEBUG && console.log('[OFP Debug] Has Aircraft?', !!ofpData.Aircraft, 'Has aircraft?', !!ofpData.aircraft);

		// Versuche beide Schreibweisen (PascalCase und lowercase)
		var atcData = ofpData.Atc || ofpData.atc;
		var aircraftData = ofpData.Aircraft || ofpData.aircraft;
		var generalData = ofpData.General || ofpData.general;
		var timesData = ofpData.Times || ofpData.times;
		var alternateData = ofpData.Alternate || ofpData.alternate;
		var fuelData = ofpData.Fuel || ofpData.fuel;
		console.log('[OFP] fuelData:', fuelData);
		console.log('[OFP] fuelData keys:', fuelData ? Object.keys(fuelData) : 'NULL');

		if (atcData && atcData.Callsign) {
			metaSource.callsign = atcData.Callsign;
		} else if (atcData && atcData.callsign) {
			metaSource.callsign = atcData.callsign;
		}
		if (aircraftData) {
			if (aircraftData.Icaocode) metaSource.aircraftType = aircraftData.Icaocode;
			else if (aircraftData.icaocode) metaSource.aircraftType = aircraftData.icaocode;
			if (aircraftData.Equip) metaSource.aircraftEquip = aircraftData.Equip;
			else if (aircraftData.equip) metaSource.aircraftEquip = aircraftData.equip;
			if (aircraftData.Reg) metaSource.aircraftReg = aircraftData.Reg;
			else if (aircraftData.reg) metaSource.aircraftReg = aircraftData.reg;
		}
		if (generalData) {
			if (generalData.Cruise_tas) metaSource.trueAirspeed = generalData.Cruise_tas;
			else if (generalData.cruise_tas) metaSource.trueAirspeed = generalData.cruise_tas;
			// Debug: Alle General-Felder ausgeben um Departure Point zu finden
			console.log('[OFP Debug] General keys:', Object.keys(generalData));
			console.log('[OFP Debug] General data:', JSON.stringify(generalData, null, 2));
		}
		// Origin/Departure Daten
		var originData = ofpData.Origin || ofpData.origin;
		// Debug: Origin-Felder prüfen
		if (originData) {
			console.log('[OFP Debug] Origin keys:', Object.keys(originData));
		}
		if (originData) {
			var originIcao = originData.Icao_code || originData.icao_code;
			var originName = originData.Name || originData.name;
			var originRwy = originData.Plan_rwy || originData.plan_rwy;
			if (originIcao) {
				metaSource.departureId = originIcao;
				var depParts = [originIcao];
				if (originName) depParts.push(originName);
				if (originRwy) depParts.push('RWY ' + originRwy);
				metaSource.departurePosition = depParts.join(' ');
				NAVLOG_DEBUG && console.log('[OFP Debug] Departure Position:', metaSource.departurePosition);
			}
			// Departure RWY
			if (originRwy) {
				metaSource.departureRwy = originRwy;
				NAVLOG_DEBUG && console.log('[OFP Debug] Departure RWY:', originRwy);
			}
			// Departure Elevation
			var originElev = originData.Elevation || originData.elevation || originData.Elev || originData.elev;
			if (originElev !== undefined && originElev !== null) {
				metaSource.departureElevation = originElev;
				NAVLOG_DEBUG && console.log('[OFP Debug] Departure Elevation:', originElev, 'ft');
			}
		}
		if (timesData) {
			if (timesData.Sched_off) metaSource.departureTime = timesData.Sched_off;
			else if (timesData.sched_off) metaSource.departureTime = timesData.sched_off;
			else if (timesData.Est_off) metaSource.departureTime = timesData.Est_off;
			else if (timesData.est_off) metaSource.departureTime = timesData.est_off;
			// Est Time Enroute für Destination-Hours/Minutes
			var estEnroute = timesData.Est_time_enroute || timesData.est_time_enroute;
			if (estEnroute) {
				metaSource.estTimeEnroute = estEnroute;
				// WICHTIG: Simbrief liefert Est_time_enroute in SEKUNDEN!
				var enrouteSeconds = parseInt(estEnroute);
				if (!isNaN(enrouteSeconds)) {
					var enrouteMinutes = Math.floor(enrouteSeconds / 60);
					metaSource.destinationHours = Math.floor(enrouteMinutes / 60);
					metaSource.destinationMinutes = enrouteMinutes % 60;
					NAVLOG_DEBUG && console.log('[OFP Debug] Est Time Enroute:', enrouteSeconds, 'sec =', metaSource.destinationHours + 'h ' + metaSource.destinationMinutes + 'm');
				}
			}
		}
		if (alternateData) {
			// ICAO Code und Name kombinieren
			var altIcao = alternateData.Icao_code || alternateData.icao_code;
			var altName = alternateData.Name || alternateData.name;
			if (altIcao && altName) {
				metaSource.alternateAirport = altIcao + ' - ' + altName;
			} else if (altIcao) {
				metaSource.alternateAirport = altIcao;
			}
			if (alternateData.Route) metaSource.alternateRoute = alternateData.Route;
			else if (alternateData.route) metaSource.alternateRoute = alternateData.route;
		}
		// Destination RWY and Elevation (NICHT Remarks - die trägt der Pilot ein)
		var destData = ofpData.Destination || ofpData.destination;
		if (destData) {
			var destRwy = destData.Plan_rwy || destData.plan_rwy;
			// Arrival RWY
			if (destRwy) {
				metaSource.arrivalRwy = destRwy;
				NAVLOG_DEBUG && console.log('[OFP Debug] Arrival RWY:', destRwy);
			}
			// Arrival Elevation
			var destElev = destData.Elevation || destData.elevation || destData.Elev || destData.elev;
			if (destElev !== undefined && destElev !== null) {
				metaSource.arrivalElevation = destElev;
				NAVLOG_DEBUG && console.log('[OFP Debug] Arrival Elevation:', destElev, 'ft');
			}
		}

		// SID und STAR aus General oder Navlog extrahieren
		// SID und STAR aus verschiedenen OFP-Quellen extrahieren
		// Simbrief speichert diese in General, Origin oder Navlog
		var sid = null;
		var star = null;

		// Versuche General
		if (generalData) {
			sid = generalData.Sid || generalData.sid || generalData.SID || generalData.Sid_name || generalData.sid_name;
			star = generalData.Star || generalData.star || generalData.STAR || generalData.Star_name || generalData.star_name;
			NAVLOG_DEBUG && console.log('[OFP Debug] General keys:', Object.keys(generalData));
		}

		// Fallback: Origin für SID
		if (!sid && originData) {
			sid = originData.Sid || originData.sid || originData.SID || originData.Sid_name || originData.sid_name;
		}

		// Fallback: Destination für STAR
		if (!star && destData) {
			star = destData.Star || destData.star || destData.STAR || destData.Star_name || destData.star_name;
		}

		// Fallback: Aus der Route extrahieren (erste/letzte Navlog Fixes)
		// WICHTIG: SID/STAR-Namen stehen im Via_airway Feld, NICHT im Ident!
		// Ident enthält den Fix-Namen (z.B. "DS046"), Via_airway enthält die Prozedur (z.B. "ABTA4B")
		var navlogData = ofpData.Navlog || ofpData.navlog;
		var fixes = navlogData && (navlogData.Fix || navlogData.fix);
		if (Array.isArray(fixes) && fixes.length > 0) {
			// SID: Erstes Fix mit Is_sid_star === "1" - Name aus Via_airway
			if (!sid) {
				for (var fi = 0; fi < fixes.length; fi++) {
					var fix = fixes[fi];
					var isSidFix = fix.Is_sid_star === '1' || fix.is_sid_star === '1' ||
								   fix.Type === 'sid' || fix.type === 'sid';
					if (isSidFix) {
						// SID-Name aus Via_airway holen (dort steht z.B. "ABTA4B")
						var sidVia = fix.Via_airway || fix.via_airway || fix.Via || fix.via;
						if (sidVia) {
							sid = sidVia;
							NAVLOG_DEBUG && console.log('[OFP Debug] SID from Via_airway:', sid, 'at fix:', fix.Ident || fix.ident);
							break;
						}
					}
				}
			}
			// STAR: Vom Ende suchen nach Fixes mit Is_sid_star === "1" (am Ende = STAR)
			// Der Name steht im Via_airway Feld
			if (!star) {
				var starCandidate = null;
				for (var fj = fixes.length - 1; fj >= 0; fj--) {
					var fixEnd = fixes[fj];
					var isStarFix = fixEnd.Is_sid_star === '1' || fixEnd.is_sid_star === '1' ||
									fixEnd.Type === 'star' || fixEnd.type === 'star';
					var starVia = fixEnd.Via_airway || fixEnd.via_airway || fixEnd.Via || fixEnd.via;

					if (isStarFix && starVia) {
						starCandidate = starVia;
						NAVLOG_DEBUG && console.log('[OFP Debug] STAR candidate from Via_airway:', starVia, 'at fix:', fixEnd.Ident || fixEnd.ident);
					}
					// Wenn wir auf einen Fix ohne Is_sid_star stoßen und schon einen Kandidaten haben, sind wir aus der STAR raus
					if (!isStarFix && starCandidate) {
						break;
					}
				}
				// Stelle sicher, dass STAR nicht gleich SID ist
				if (starCandidate && starCandidate !== sid) {
					star = starCandidate;
					NAVLOG_DEBUG && console.log('[OFP Debug] Final STAR:', star);
				}
			}
		}

		if (sid) {
			metaSource.departureSid = sid;
			NAVLOG_DEBUG && console.log('[OFP Debug] Departure SID:', sid);
		}
		if (star) {
			metaSource.arrivalStar = star;
			NAVLOG_DEBUG && console.log('[OFP Debug] Arrival STAR:', star);
		}

		// Departure Point (Punkt an dem man die CTR verlässt) = Ende der SID oder erster Enroute Fix
		// Suche nach dem letzten SID-Fix oder ersten Enroute-Fix in den Navlog Fixes
		if (Array.isArray(fixes) && fixes.length > 0) {
			var departurePoint = null;
			for (var dp = 0; dp < fixes.length; dp++) {
				var dpFix = fixes[dp];
				var dpType = dpFix.Type || dpFix.type || '';
				var dpIdent = dpFix.Ident || dpFix.ident || dpFix.Name || dpFix.name;

				// Suche nach dem letzten SID-Punkt oder ersten "normalen" Wegpunkt
				// SID-Ende ist oft als "sid" markiert, oder wir nehmen den ersten nicht-Airport Fix
				if (dpType.toLowerCase() === 'sid' || dpFix.Is_sid_star === '1') {
					departurePoint = dpIdent; // Überschreibe bis zum letzten SID-Punkt
				} else if (!departurePoint && dpType.toLowerCase() !== 'apt' && dpIdent) {
					// Erster nicht-Airport Waypoint wenn keine SID gefunden
					departurePoint = dpIdent;
					break;
				}
			}
			if (departurePoint) {
				metaSource.departurePoint = departurePoint;
				NAVLOG_DEBUG && console.log('[OFP Debug] Departure Point:', departurePoint);
			}
		}
		// Fuel Endurance aus OFP extrahieren (Stunden und Minuten)
		console.log('[Fuel] fuelData:', fuelData);
		console.log('[Fuel] fuelData keys:', fuelData ? Object.keys(fuelData) : 'NULL');
		if (fuelData) {
			// Total Fuel (Plan Ramp = Gesamtkraftstoff beim Start)
			var planRamp = fuelData.Plan_ramp || fuelData.plan_ramp;
			if (planRamp !== undefined && planRamp !== null) {
				metaSource.totalFuel = planRamp;
				console.log('[Fuel] Total Fuel (Plan Ramp):', planRamp);
			}

			// Avg Fuel Flow (Verbrauch pro Stunde)
			var avgFuelFlow = fuelData.Avg_fuel_flow || fuelData.avg_fuel_flow;
			if (avgFuelFlow !== undefined && avgFuelFlow !== null) {
				metaSource.avgFuelFlow = avgFuelFlow;
				console.log('[Fuel] Avg Fuel Flow:', avgFuelFlow);
			}

			// Endurance berechnen: Gesamtkraftstoff / Verbrauch pro Stunde
			// WICHTIG: enroute_burn ist der Verbrauch für die Strecke, NICHT die Endurance!
			if (planRamp && avgFuelFlow && parseFloat(avgFuelFlow) > 0) {
				var totalFuel = parseFloat(planRamp);
				var fuelFlow = parseFloat(avgFuelFlow);
				var enduranceHoursDecimal = totalFuel / fuelFlow;
				var enduranceHours = Math.floor(enduranceHoursDecimal);
				var enduranceMins = Math.round((enduranceHoursDecimal - enduranceHours) * 60);
				metaSource.enduranceHours = enduranceHours;
				metaSource.enduranceMinutes = enduranceMins;
				console.log('[Fuel] Fuel Endurance CALCULATED:', enduranceHours + 'h ' + enduranceMins + 'm (from ' + totalFuel + ' / ' + fuelFlow + ' = ' + enduranceHoursDecimal.toFixed(2) + 'h)');
			} else {
				console.log('[Fuel] Cannot calculate endurance - planRamp:', planRamp, 'avgFuelFlow:', avgFuelFlow);
			}
		} else {
			console.log('[Fuel] fuelData is NULL/undefined! ofpData keys:', ofpData ? Object.keys(ofpData) : 'ofpData also NULL');
		}

		// Notes aus OFP extrahieren
		var textData = ofpData.Text || ofpData.text;
		if (textData) {
			var planHtml = textData.Plan_html || textData.plan_html;
			var briefingText = textData.Briefing || textData.briefing;
			if (briefingText) {
				metaSource.notes = briefingText;
				NAVLOG_DEBUG && console.log('[OFP Debug] Notes (Briefing):', briefingText.substring(0, 100) + '...');
			}
		}
		// Alternativ: General.Notes
		if (!metaSource.notes && generalData) {
			var notes = generalData.Notes || generalData.notes;
			if (notes) {
				metaSource.notes = notes;
				NAVLOG_DEBUG && console.log('[OFP Debug] Notes (General):', notes.substring(0, 100) + '...');
			}
		}

		// Weather Log aus METAR/TAF extrahieren
		// Origin (Departure) Weather
		if (originData) {
			var originMetar = originData.Metar || originData.metar;
			var originTaf = originData.Taf || originData.taf;
			if (originMetar) {
				metaSource.departureMetar = originMetar;
				metaSource.departureWeather = parseMetarForWeatherLog(originMetar);
				NAVLOG_DEBUG && console.log('[OFP Debug] Departure METAR:', originMetar);
			}
			if (originTaf) {
				metaSource.departureTaf = originTaf;
				metaSource.departureForecast = parseTafForWeatherLog(originTaf);
				NAVLOG_DEBUG && console.log('[OFP Debug] Departure TAF:', originTaf.substring(0, 100) + '...');
			}
		}
		// Destination (Arrival) Weather
		if (destData) {
			var destMetarFull = destData.Metar || destData.metar;
			var destTafFull = destData.Taf || destData.taf;
			if (destMetarFull) {
				metaSource.arrivalMetar = destMetarFull;
				metaSource.arrivalWeather = parseMetarForWeatherLog(destMetarFull);
				NAVLOG_DEBUG && console.log('[OFP Debug] Arrival METAR:', destMetarFull);
			}
			if (destTafFull) {
				metaSource.arrivalTaf = destTafFull;
				metaSource.arrivalForecast = parseTafForWeatherLog(destTafFull);
				NAVLOG_DEBUG && console.log('[OFP Debug] Arrival TAF:', destTafFull.substring(0, 100) + '...');
			}
		}
		// Alternate Weather
		if (alternateData) {
			var altMetar = alternateData.Metar || alternateData.metar;
			var altTaf = alternateData.Taf || alternateData.taf;
			if (altMetar) {
				metaSource.alternateMetar = altMetar;
				metaSource.alternateWeather = parseMetarForWeatherLog(altMetar);
				NAVLOG_DEBUG && console.log('[OFP Debug] Alternate METAR:', altMetar);
			}
			if (altTaf) {
				metaSource.alternateTaf = altTaf;
				metaSource.alternateForecast = parseTafForWeatherLog(altTaf);
			}
		}

		// Debug: Zeige extrahierte Werte
		if (NAVLOG_DEBUG) {
			console.log('[OFP Debug] Extracted values from OFP:');
			console.log('  - callsign:', metaSource.callsign);
			console.log('  - aircraftType:', metaSource.aircraftType);
			console.log('  - aircraftEquip:', metaSource.aircraftEquip);
			console.log('  - departureTime:', metaSource.departureTime);
			console.log('  - alternateAirport:', metaSource.alternateAirport);
		}

		// Wetterdaten aus OFP Navlog Fixes auf Waypoints anwenden
		var navlogData = ofpData.Navlog || ofpData.navlog;
		var fixes = navlogData && (navlogData.Fix || navlogData.fix);
		if (Array.isArray(fixes) && Array.isArray(info.waypoints) && info.waypoints.length > 0) {
			NAVLOG_DEBUG && console.log('[OFP Debug] Merging weather data from', fixes.length, 'fixes to', info.waypoints.length, 'waypoints');

			// Map für schnelles Lookup nach Ident
			var fixByIdent = {};
			for (var f = 0; f < fixes.length; f++) {
				var fix = fixes[f];
				var ident = fix.Ident || fix.ident;
				if (ident) {
					fixByIdent[ident.toUpperCase()] = fix;
				}
			}

			// Wetterdaten auf Waypoints anwenden
			for (var w = 0; w < info.waypoints.length; w++) {
				var wp = info.waypoints[w];
				var wpName = wp.name || wp.Name || wp.ident || wp.Ident || '';
				if (wpName) {
					var matchingFix = fixByIdent[wpName.toUpperCase()];
					if (matchingFix) {
						// Wetterdaten übertragen
						if (matchingFix.Wind_dir !== undefined) {
							wp.Wind_dir = matchingFix.Wind_dir;
						} else if (matchingFix.wind_dir !== undefined) {
							wp.Wind_dir = matchingFix.wind_dir;
						}
						if (matchingFix.Wind_spd !== undefined) {
							wp.Wind_spd = matchingFix.Wind_spd;
						} else if (matchingFix.wind_spd !== undefined) {
							wp.Wind_spd = matchingFix.wind_spd;
						}
						if (matchingFix.Oat !== undefined) {
							wp.Oat = matchingFix.Oat;
						} else if (matchingFix.oat !== undefined) {
							wp.Oat = matchingFix.oat;
						}
						// Ground Speed (GS Est)
						if (matchingFix.Groundspeed !== undefined) {
							wp.Groundspeed = matchingFix.Groundspeed;
						} else if (matchingFix.groundspeed !== undefined) {
							wp.Groundspeed = matchingFix.groundspeed;
						} else if (matchingFix.Ground_spd !== undefined) {
							wp.Groundspeed = matchingFix.Ground_spd;
						} else if (matchingFix.ground_spd !== undefined) {
							wp.Groundspeed = matchingFix.ground_spd;
						}
						// ETE (Estimated Time Enroute)
						if (matchingFix.Ete !== undefined) {
							wp.Ete = matchingFix.Ete;
						} else if (matchingFix.ete !== undefined) {
							wp.Ete = matchingFix.ete;
						} else if (matchingFix.Time_leg !== undefined) {
							wp.Ete = matchingFix.Time_leg;
						} else if (matchingFix.time_leg !== undefined) {
							wp.Ete = matchingFix.time_leg;
						}
						// ETA (Estimated Time of Arrival)
						if (matchingFix.Eta !== undefined) {
							wp.Eta = matchingFix.Eta;
						} else if (matchingFix.eta !== undefined) {
							wp.Eta = matchingFix.eta;
						} else if (matchingFix.Time_total !== undefined) {
							wp.Eta = matchingFix.Time_total;
						} else if (matchingFix.time_total !== undefined) {
							wp.Eta = matchingFix.time_total;
						}
						// Fuel Burn Rate (GPH)
						if (matchingFix.Fuel_flow !== undefined) {
							wp.Fuel_flow = matchingFix.Fuel_flow;
						} else if (matchingFix.fuel_flow !== undefined) {
							wp.Fuel_flow = matchingFix.fuel_flow;
						}
						// Fuel Burn (Burn)
						if (matchingFix.Fuel_burn !== undefined) {
							wp.Fuel_burn = matchingFix.Fuel_burn;
						} else if (matchingFix.fuel_burn !== undefined) {
							wp.Fuel_burn = matchingFix.fuel_burn;
						} else if (matchingFix.Fuel_leg !== undefined) {
							wp.Fuel_burn = matchingFix.Fuel_leg;
						} else if (matchingFix.fuel_leg !== undefined) {
							wp.Fuel_burn = matchingFix.fuel_leg;
						}
						// Fuel Remaining (Rem)
						if (matchingFix.Fuel_rem !== undefined) {
							wp.Fuel_rem = matchingFix.Fuel_rem;
						} else if (matchingFix.fuel_rem !== undefined) {
							wp.Fuel_rem = matchingFix.fuel_rem;
						} else if (matchingFix.Fuel_totalused !== undefined) {
							wp.Fuel_totalused = matchingFix.Fuel_totalused;
						} else if (matchingFix.fuel_totalused !== undefined) {
							wp.Fuel_totalused = matchingFix.fuel_totalused;
						}
						NAVLOG_DEBUG && console.log('[OFP Debug] Merged data for', wpName, '- Wind:', wp.Wind_dir + '°/' + wp.Wind_spd + 'kt, Temp:', wp.Oat + '°C, GS:', wp.Groundspeed);
					}
				}
			}
		}
	}
	var typeValue =
		root.flightType ||
		root.flighttype ||
		root.FPType ||
		root.fptype ||
		root.type ||
		root.routeType ||
		metaSource.flightType;
	if (typeof typeValue === 'string') {
		info.flightType = typeValue.toLowerCase();
	}
	var departureName =
		root.departureName ||
		root.DepartureName ||
		root.departurename ||
		metaSource.departureName;
	var departureId =
		root.departureId ||
		root.DepartureId ||
		root.departureid ||
		root.departureID ||
		metaSource.departureId;
	var destinationName =
		root.destinationName ||
		root.DestinationName ||
		root.destinationname ||
		metaSource.destinationName;
	var destinationId =
		root.destinationId ||
		root.DestinationId ||
		root.destinationid ||
		root.destinationID ||
		metaSource.destinationId;
	var departurePosition =
		root.departurePosition ||
		root.DeparturePosition ||
		root.departureposition ||
		metaSource.departurePosition;
	var alternateAirport =
		root.alternateId ||
		root.AlternateId ||
		root.alternateid ||
		root.alternateID ||
		root.alternate ||
		root.Alternate ||
		root.altn ||
		root.Altn ||
		metaSource.alternateId ||
		metaSource.alternate ||
		metaSource.alternateAirport;
	var alternateRoute =
		root.alternateRoute ||
		root.AlternateRoute ||
		root.alternateroute ||
		root.altnRoute ||
		root.AltnRoute ||
		metaSource.alternateRoute;
	// Simbrief-spezifische Felder extrahieren
	var callsign =
		root.callsign ||
		root.Callsign ||
		(root.atc && root.atc.callsign) ||
		metaSource.callsign;
	var aircraftType =
		root.aircraftType ||
		root.icaocode ||
		(root.aircraft && root.aircraft.icaocode) ||
		metaSource.aircraftType;
	var aircraftEquip =
		root.equip ||
		(root.aircraft && root.aircraft.equip) ||
		metaSource.aircraftEquip;
	var trueAirspeed =
		root.cruise_tas ||
		(root.general && root.general.cruise_tas) ||
		metaSource.trueAirspeed;
	var departureTime =
		root.sched_off ||
		root.est_off ||
		(root.times && (root.times.sched_off || root.times.est_off)) ||
		metaSource.departureTime;
	var estTimeEnroute =
		root.est_time_enroute ||
		(root.times && root.times.est_time_enroute) ||
		metaSource.estTimeEnroute;
	var resetFlag =
		root.resetNavlog ||
		root.clearNavlog ||
		metaSource.resetNavlog ||
		metaSource.clearNavlog;
	if (resetFlag) {
		info.forceReset = true;
	}
	info.meta = Object.assign({}, metaSource);
	if (typeof departureName === 'string') {
		info.meta.departureName = departureName;
	}
	if (typeof departureId === 'string') {
		info.meta.departureId = departureId;
	}
	if (typeof destinationName === 'string') {
		info.meta.destinationName = destinationName;
	}
	if (typeof destinationId === 'string') {
		info.meta.destinationId = destinationId;
	}
	if (typeof departurePosition === 'string') {
		info.meta.departurePosition = departurePosition;
	}
	if (typeof alternateAirport === 'string') {
		info.meta.alternateAirport = alternateAirport;
	}
	if (typeof alternateRoute === 'string') {
		info.meta.alternateRoute = alternateRoute;
	}
	// Simbrief-spezifische Felder speichern
	if (callsign) {
		info.meta.callsign = callsign;
	}
	if (aircraftType) {
		info.meta.aircraftType = aircraftType;
	}
	if (aircraftEquip) {
		info.meta.aircraftEquip = aircraftEquip;
	}
	if (trueAirspeed) {
		info.meta.trueAirspeed = trueAirspeed;
	}
	if (departureTime) {
		info.meta.departureTime = departureTime;
	}
	if (estTimeEnroute) {
		info.meta.estTimeEnroute = estTimeEnroute;
	}

	// Debug: Zeige finale meta-Werte
	if (NAVLOG_DEBUG) {
		console.log('[OFP Debug] Final info.meta:');
		console.log('  - callsign:', info.meta.callsign);
		console.log('  - aircraftType:', info.meta.aircraftType);
		console.log('  - aircraftEquip:', info.meta.aircraftEquip);
		console.log('  - departureTime:', info.meta.departureTime);
		console.log('  - alternateAirport:', info.meta.alternateAirport);
	}

	return info;
}

function handleFlightplanPayload(message) {
	try {
		NAVLOG_DEBUG && console.log('[Navlog Debug] ====== handleFlightplanPayload CALLED ======');
		NAVLOG_DEBUG && console.log('[Navlog Debug] Raw message length:', message ? message.length : 'NULL');

		var raw = message ? JSON.parse(message) : [];
		NAVLOG_DEBUG && console.log('[Navlog Debug] Parsed raw data type:', Array.isArray(raw) ? 'Array' : typeof raw);
		NAVLOG_DEBUG && console.log('[Navlog Debug] Raw data keys:', raw && typeof raw === 'object' ? Object.keys(raw) : 'N/A');

		var parsed = parseFlightplanPayload(raw);
		NAVLOG_DEBUG && console.log('[Navlog Debug] Parsed result - waypoints:', Array.isArray(parsed.waypoints) ? parsed.waypoints.length : 'NOT ARRAY');
		NAVLOG_DEBUG && console.log('[Navlog Debug] Parsed result - meta keys:', parsed.meta ? Object.keys(parsed.meta) : 'NULL');
		NAVLOG_DEBUG && console.log('[Navlog Debug] Parsed result - forceReset:', parsed.forceReset);

		if (parsed.forceReset) {
			cachedFlightplanData = [];
			cachedFlightplanMeta = {};
			cachedOfpData = null;
			logFlightplanInfo('Flightplan reset request received.');
			applyFlightplanToNavlog([], parsed.flightType);
			return;
		}
		var waypointSummary = Array.isArray(parsed.waypoints) ? parsed.waypoints.length + ' Wegpunkte' : typeof parsed.waypoints;
		logFlightplanInfo('Flightplan payload received.', waypointSummary);

		// WICHTIG: OFP-Enrichment ZUERST, BEVOR die Waypoints gecached/angewendet werden!
		// So haben die Waypoints alle Daten (Wind, Fuel, etc.) wenn sie in die Tabellen geschrieben werden
		if (parsed.ofpData && typeof OFP_NAVLOG_MAPPING !== 'undefined') {
			// FLAG SETZEN: OFP-Mapping läuft - Server-Sync blockieren!
			ofpMappingInProgress = true;
			NAVLOG_DEBUG && console.log('[Navlog Debug] OFP MAPPING STARTED - Server sync blocked');

			// KRITISCH: SERVER-CACHE ZUERST LÖSCHEN bevor neue OFP-Werte gesetzt werden!
			// Sonst holt loadNavlogFromServer() (200ms timeout) die alten Werte und überschreibt die neuen
			try {
				var clearUrl = (window.KneeboardApiProxyUrl || 'http://localhost:815') + '/clearNavlogValues';
				fetch(clearUrl, { method: 'GET' })
					.then(function(res) {
						if (res.ok) {
							NAVLOG_DEBUG && console.log('[Navlog Debug] Server navlog cache cleared for new flightplan');
						}
					})
					.catch(function(err) {
						console.warn('[Navlog] Failed to clear server cache:', err);
					});
			} catch (e) {
				console.warn('[Navlog] Error clearing server cache:', e);
			}

			// localStorage für OFP-Felder löschen bevor neue Werte gesetzt werden
			// Sonst überschreibt readValues() die neuen Werte mit alten cached Werten
			clearOFPFieldsFromLocalStorage();
			NAVLOG_DEBUG && console.log('[Navlog Debug] Cleared OFP fields from localStorage and server cache');
			NAVLOG_DEBUG && console.log('[Navlog Debug] Enriching waypoints with OFP data BEFORE caching...');
			// Waypoints mit OFP-Daten anreichern (Wind, Fuel, Time, etc.)
			enrichWaypointsWithOFP(parsed.waypoints, parsed.ofpData);

			// OFP-Daten cachen für späteren Zugriff bei DOM-Ready
			cachedOfpData = parsed.ofpData;
			NAVLOG_DEBUG && console.log('[Navlog Debug] Cached OFP data for DOM-ready mapping');

			// TEIL 1: Persistentes Caching in sessionStorage für Page-Reload-Resistenz
			try {
				sessionStorage.setItem('kneeboard_cachedFlightplanData', JSON.stringify(parsed.waypoints));
				sessionStorage.setItem('kneeboard_cachedFlightplanMeta', JSON.stringify(parsed.meta || {}));
				sessionStorage.setItem('kneeboard_cachedOfpData', JSON.stringify(parsed.ofpData));
				NAVLOG_DEBUG && console.log('[Navlog Debug] Cached flightplan data to sessionStorage');
			} catch (e) {
				console.warn('[Navlog] Failed to cache to sessionStorage:', e);
			}

			NAVLOG_DEBUG && console.log('[Navlog Debug] Applying OFP mapping (may fail if DOM not ready)...');
			var mappedValues = processOFPWithMapping(parsed.ofpData, parsed.waypoints);
			applyMappedValuesToNavlog(mappedValues);
			// FLAG wird in pushNavlogToServer() zurückgesetzt nach erfolgreichem Push
		}

		NAVLOG_DEBUG && console.log('[Navlog Debug] Calling cacheFlightplanData with', parsed.waypoints.length, 'waypoints');
		var cached = cacheFlightplanData(parsed.waypoints, parsed.meta);
		NAVLOG_DEBUG && console.log('[Navlog Debug] Cached result:', cached ? cached.length : 'NULL', 'waypoints');
		NAVLOG_DEBUG && console.log('[Navlog Debug] cachedFlightplanData after caching:', cachedFlightplanData ? cachedFlightplanData.length : 'NULL');
		NAVLOG_DEBUG && console.log('[Navlog Debug] cachedFlightplanMeta after caching:', cachedFlightplanMeta ? Object.keys(cachedFlightplanMeta) : 'NULL');

		NAVLOG_DEBUG && console.log('[Navlog Debug] Calling applyFlightplanToNavlog...');
		var applyResult = applyFlightplanToNavlog(cached, parsed.flightType);
		NAVLOG_DEBUG && console.log('[Navlog Debug] applyFlightplanToNavlog returned:', applyResult);

		if (!applyResult) {
			logFlightplanInfo('Navlog not ready, data cached. Will be applied when user opens Navlog tab.');
			NAVLOG_DEBUG && console.log('[Navlog Debug] FINAL CHECK - cachedFlightplanData:', cachedFlightplanData ? cachedFlightplanData.length : 'NULL');
			NAVLOG_DEBUG && console.log('[Navlog Debug] FINAL CHECK - cachedFlightplanMeta:', cachedFlightplanMeta);
			// Data is cached - it will be applied when user switches to Navlog tab
			// See: kneeboard.js btnNavlogClicked() and initNavlogPage() which call applyCachedFlightplanIfReady()
		}

		NAVLOG_DEBUG && console.log('[Navlog Debug] ====== handleFlightplanPayload COMPLETE ======');
	}
	catch (err) {
		console.error('[Navlog Debug] ERROR in handleFlightplanPayload:', err);
		logFlightplanInfo('Flightplan import failed', err);
	}
}

// ============================================================================
// BUTTON HANDLERS (Optimized)
// ============================================================================
function ensureThemeColors() {
	// Falls Farben nicht gesetzt sind, aus CSS-Variablen lesen oder Defaults nutzen.
	if (!colorLight || !colorLight.trim()) {
		try {
			var cssLight = getComputedStyle(document.documentElement).getPropertyValue('--light');
			colorLight = (cssLight && cssLight.trim()) ? cssLight.trim() : '#2874b2';
		} catch (e) {
			colorLight = '#2874b2';
		}
	}
	if (!colorDark || !colorDark.trim()) {
		try {
			var cssDark = getComputedStyle(document.documentElement).getPropertyValue('--dark');
			colorDark = (cssDark && cssDark.trim()) ? cssDark.trim() : '#205d8e';
		} catch (e) {
			colorDark = '#205d8e';
		}
	}
}

function setFlightTypeUI(type, displayId, headerText) {
	if (!ensureNavlogContainer()) return;

	ensureThemeColors();

	btnIFR = btnIFR || document.getElementById("ifr");
	btnVFR = btnVFR || document.getElementById("vfr");
	btnZY = btnZY || document.getElementById("zy");

	flightType = type;

	var buttons = {vfr: btnVFR, ifr: btnIFR, zy: btnZY};
	var displays = {
		vfr: document.getElementById("waypointsVFR"),
		ifr: document.getElementById("waypointsIFR"),
		zy: document.getElementById("waypointsZY")
	};

	for (var key in buttons) {
		if (buttons.hasOwnProperty(key) && buttons[key]) {
			if (key === type) {
				buttons[key].style.color = colorLight;
				buttons[key].style.backgroundColor = '#ffffff';
			} else {
				buttons[key].style.color = '#ffffff';
				buttons[key].style.backgroundColor = colorLight;
			}
		}
	}

	for (var displayKey in displays) {
		if (displays.hasOwnProperty(displayKey) && displays[displayKey]) {
			displays[displayKey].style.display = (displayKey === type) ? "" : "none";
		}
	}

	if (headerText && type === 'vfr') {
		var header = document.getElementById("waypointsVFRHeader");
		if (header) {
			header.innerHTML = headerText;
		}
	}

	saveValues();
}

function btnVFRClicked() {
	setFlightTypeUI('vfr', 'waypointsVFR', 'Waypoints VFR');
}

function btnIFRClicked() {
	setFlightTypeUI('ifr', 'waypointsIFR', null);
}

function btnZYClicked() {
	setFlightTypeUI('zy', 'waypointsZY', null);
}

// ============================================================================
// JQUERY EVENT HANDLERS (iframe-safe)
// ============================================================================
if (typeof $ !== 'undefined') {
	$('textarea').parent('td').on('click', function (e) {
		var textarea = $(this).find('textarea:first-child');
		textarea.focus();
		setCaretToPos(textarea, -1);
		if (typeof keyboardActive !== 'undefined' && keyboardActive === false) {
			if (typeof Keyboard !== 'undefined' && Keyboard.open) {
				Keyboard.open();
			}
		}
	});

	$('textarea').parent('td').on('dblclick', function () {
		var textarea = $(this).find('textarea:first-child');
		textarea.focus();
		textarea.select();
	});
}

function setSelectionRange(input, selectionStart, selectionEnd) {
	if (input.setSelectionRange) {
		input.focus();
		input.setSelectionRange(selectionStart, selectionEnd);
	}
	else if (input.createTextRange) {
		var range = input.createTextRange();
		range.collapse(true);
		range.moveEnd('character', selectionEnd);
		range.moveStart('character', selectionStart);
		range.select();
	}
}

function setCaretToPos(input, pos) {
	if (typeof activeTab !== 'undefined' && activeTab != 'Documents') {
		setSelectionRange(input, pos, pos);
	}
}

function hashNavlogString(value) {
	if (!value) {
		return 0;
	}
	// Use proper hash function (same as navlog-sync.js)
	var hash = 0;
	for (var i = 0; i < value.length; i++) {
		var char = value.charCodeAt(i);
		hash = ((hash << 5) - hash) + char;
		hash = hash & hash; // Convert to 32bit integer
	}
	return hash;
}

function getNavlogSyncRoot() {
	try {
		var origin = window.location.origin;
		if (origin && origin !== "null" && origin !== "file:" && /^(https?:\/\/)/i.test(origin)) {
			return origin;
		}
	} catch (e) {}
	return NAVLOG_SYNC_FALLBACK;
}

function getNavlogSyncUrl(path) {
	var root = getNavlogSyncRoot();
	if (!path.startsWith("/")) {
		path = "/" + path;
	}
	return root + path;
}

function collectNavlogString() {
	var navRoot = ensureNavlogContainer();
	if (!navRoot) {
		return "";
	}

	// Count waypoint rows for the active flight type
	var waypointRowCount = 0;
	var waypointTableId = getWaypointTableIdForFlightType(flightType);

	if (waypointTableId) {
		// Get the configured fixed count from the waypoint settings
		var configKey = flightType === 'zy' ? 'zy' : flightType;
		if (typeof waypointFixedCountSettings !== 'undefined' && waypointFixedCountSettings[configKey]) {
			// Use the configured waypoint count (this is what the user has set)
			waypointRowCount = waypointFixedCountSettings[configKey];
		} else {
			// Fallback: count actual visible rows
			var waypointTable = navRoot.querySelector('#' + waypointTableId);
			if (waypointTable) {
				var tbody = waypointTable.querySelector('tbody');
				if (tbody) {
					var allRows = tbody.querySelectorAll('tr');
					for (var r = 0; r < allRows.length; r++) {
						if (allRows[r].getAttribute('data-waypoint-template') !== 'true' &&
						    allRows[r].style.display !== 'none') {
							waypointRowCount++;
						}
					}
				}
			}
		}
	}

	// Start with flight type and row count
	var navlogString = (flightType || "") + '~' + waypointRowCount;

	// Collect all textarea values
	var textareas = navRoot.getElementsByTagName('textarea');
	for (var i = 0; i < textareas.length; i++) {
		var value = textareas[i].value || '';
		// Replace tilde with hyphen to avoid breaking the delimiter
		var saveValue = value.replace(/~/g, "-");
		navlogString += '~' + saveValue;
	}

	logVerbose('[collectNavlogString]', flightType, '- Waypoint rows:', waypointRowCount, '- Total fields:', textareas.length);

	return navlogString;
}

function broadcastNavlogString(navlogString) {
	if (!navlogSyncChannel || !navlogString) {
		return;
	}
	var hash = hashNavlogString(navlogString);
	if (hash === lastBroadcastNavlogHash) {
		return;
	}
	lastBroadcastNavlogHash = hash;
	// DISABLED FOR TESTING - LocalStorage sync interferes with server-based sync
	// try {
	// 	localStorage.setItem('navlogSyncLast', navlogString);
	// } catch (e) {}
	try {
		navlogSyncChannel.postMessage({
			type: "navlog-sync",
			navlogString: navlogString,
			hash: hash,
			senderId: navlogSyncId
		});
	} catch (err) {
		console.warn('[Navlog] Unable to broadcast navlog:', err);
	}
}

function pushNavlogToServer(navlogString) {
	if (!navlogString) {
		return;
	}
	clearTimeout(navlogServerPushTimeout);
	navlogServerPushTimeout = setTimeout(function () {
		try {
			var url = getNavlogSyncUrl("setNavlogValues");
			fetch(url, {
				method: "POST",
				body: navlogString
			})
				.then(function (res) {
					if (!res.ok) throw new Error(res.status + " " + res.statusText);
					return res.text();
				})
				.then(function (timestampStr) {
					// Update our timestamp with server's response
					var timestamp = parseInt(timestampStr) || 0;
					if (timestamp > 0) {
						lastServerTimestamp = timestamp;
						lastLocalTimestamp = timestamp;
						// ECHO PREVENTION: Store hash of what we just pushed
						lastServerNavlogHash = hashNavlogString(navlogString);
					}
					console.info("[Navlog] → Pushed to server, timestamp:", timestamp, "hash stored for echo prevention");

					// FLAG FREIGEBEN: Server-Push erfolgreich - Server-Sync wieder erlauben
					if (ofpMappingInProgress) {
						ofpMappingInProgress = false;
						NAVLOG_DEBUG && console.log('[Navlog] OFP MAPPING COMPLETE - Server sync unblocked after successful push');
					}
				})
				.catch(function (e) {
					NAVLOG_DEBUG && console.warn("[Navlog] push to server failed:", e);
					// Bei Fehler auch Flag freigeben damit System nicht blockiert bleibt
					if (ofpMappingInProgress) {
						ofpMappingInProgress = false;
						NAVLOG_DEBUG && console.log('[Navlog] OFP mapping flag cleared after push error');
					}
				});
		} catch (err) {
			NAVLOG_DEBUG && console.warn("[Navlog] push to server failed:", err);
			if (ofpMappingInProgress) {
				ofpMappingInProgress = false;
			}
		}
	}, NAVLOG_PUSH_DELAY_MS);
}

function applyNavlogSyncString(navlogString) {
	if (!navlogString) {
		return;
	}

	// KRITISCH: Wenn OFP-Mapping läuft, Server-Sync blockieren!
	// Sonst überschreiben alte Server-Werte die neuen OFP-Werte (FL13 statt FL170)
	if (ofpMappingInProgress) {
		NAVLOG_DEBUG && console.log('[applyNavlogSyncString] BLOCKED - OFP mapping in progress, ignoring server data');
		return;
	}

	var hash = hashNavlogString(navlogString);

	// Skip hash check on initial load - always accept first data
	if (!initialNavlogLoadComplete) {
		NAVLOG_DEBUG && console.log('[applyNavlogSyncString] Initial load - accepting data without hash check');
	}

	// Wenn bereits angewendet, nicht erneut verarbeiten
	if (hash && lastAppliedNavlogHash && hash === lastAppliedNavlogHash) {
		logVerbose('[applyNavlogSyncString] Incoming payload hash matches last applied, skipping');
		return;
	}

	// Im Kneeboard nur anwenden, wenn Navlog-Tab aktiv ist
	if (!isNavlogActiveTab()) {
		logVerbose('[applyNavlogSyncString] Navlog tab not active, skipping apply');
		return;
	}

	// Parse incoming payload
	var parts = navlogString.split('~');
	if (parts.length < 3) {
		NAVLOG_DEBUG && console.warn('[applyNavlogSyncString] Not enough parts to apply payload');
		return;
	}

	var incomingFlightType = parts[0] || flightType;
	var incomingRowCount = parseInt(parts[1], 10) || 0;
	var startIndex = 2; // skip flightType + rowCount

	try {
		suppressNavlogPush = true; // avoid echoing back while applying

		// Switch flight type if needed
		if (incomingFlightType && flightType !== incomingFlightType) {
			NAVLOG_DEBUG && console.log('[applyNavlogSyncString] Switching flight type from', flightType, 'to', incomingFlightType);
			flightType = incomingFlightType;
			setFlightType();
		}

		// Get navlog container FIRST (needed for clearing)
		var navRoot = ensureNavlogContainer();
		if (!navRoot) {
			NAVLOG_DEBUG && console.warn('[applyNavlogSyncString] Navlog container not found');
			return;
		}

		// STEP 1: Reset to 5 rows (Kneeboard default) to clear old data
		NAVLOG_DEBUG && console.log('[applyNavlogSyncString] Resetting to 5 rows (Kneeboard default)...');
		if (typeof setWaypointFixedCount === 'function') {
			setWaypointFixedCount(flightType, 5);
		}
		ensureExactRowCount(flightType, 5);

		// STEP 2: Clear all textareas
		NAVLOG_DEBUG && console.log('[applyNavlogSyncString] Clearing all textareas...');
		var allTextareas = navRoot.getElementsByTagName('textarea');
		for (var j = 0; j < allTextareas.length; j++) {
			allTextareas[j].value = '';
		}

		// STEP 3: Set the actual row count from incoming data
		NAVLOG_DEBUG && console.log('[applyNavlogSyncString] Setting row count to:', incomingRowCount);
		if (typeof setWaypointFixedCount === 'function') {
			setWaypointFixedCount(flightType, incomingRowCount);
		}
		ensureExactRowCount(flightType, incomingRowCount);

		// STEP 4: Apply values to all textareas in document order
		var textareas = navRoot.getElementsByTagName('textarea');
		var updatedCount = 0;
		var skippedCount = 0;

		if (textareas.length < parts.length - startIndex) {
			NAVLOG_DEBUG && console.warn('[applyNavlogSyncString] Fewer textareas than incoming values. Values:', (parts.length - startIndex), 'Textareas:', textareas.length);
		}

		for (var i = startIndex; i < parts.length; i++) {
			var targetIndex = i - startIndex;
			var target = textareas[targetIndex];
			if (!target) {
				skippedCount++;
				continue;
			}
			var newValue = parts[i] || '';
			if (target.value !== newValue) {
				target.value = newValue;
				updatedCount++;
			}
		}

		NAVLOG_DEBUG && console.log('[applyNavlogSyncString] Applied payload. Updated:', updatedCount, 'Skipped:', skippedCount);

		// Track hashes to avoid duplicates
		lastAppliedNavlogHash = hash;
		var isBrowserMirror = window.location.pathname.indexOf('navigationlog.html') >= 0;
		var isInIframe = window.parent !== window;
		if (isBrowserMirror || !isInIframe) {
			lastBroadcastNavlogHash = hash;
		} else {
			NAVLOG_DEBUG && console.log('[applyNavlogSyncString] Kneeboard iframe context, skipping broadcast hash update');
		}

		// Mark initial load complete
		if (!initialNavlogLoadComplete) {
			initialNavlogLoadComplete = true;
			NAVLOG_DEBUG && console.log('[applyNavlogSyncString] Initial navlog load complete - future updates will use hash checking');
		}

		// Log-time IMMER neu berechnen wenn Block-Zeiten aus Sync kommen
		calculateLogTime(true);
	} catch (err) {
		console.warn('[Navlog] Failed to apply synced navlog payload:', err);
	} finally {
		setTimeout(function () { suppressNavlogPush = false; }, 300);
	}
}

// Exporte für Browser-Mirror (navlog-sync.js) zur gemeinsamen Nutzung
if (typeof window !== 'undefined') {
	window.NavlogSyncHelpers = window.NavlogSyncHelpers || {};
	window.NavlogSyncHelpers.hashNavlogString = hashNavlogString;
	window.NavlogSyncHelpers.collectNavlogString = collectNavlogString;
	window.NavlogSyncHelpers.applyNavlogSyncString = applyNavlogSyncString;
}

function initNavlogSync() {
	try {
		navlogSyncId = Math.random().toString(36).slice(2);
		if (typeof BroadcastChannel === "function") {
			navlogSyncChannel = new BroadcastChannel("kneeboard-navlog");
			navlogSyncChannel.onmessage = function (event) {
				var data = event.data || {};
				if (!data || data.type !== "navlog-sync" || !data.navlogString) {
					return;
				}
				if (data.senderId && data.senderId === navlogSyncId) {
					return;
				}
				if (data.hash && data.hash === lastBroadcastNavlogHash) {
					return;
				}
				applyNavlogSyncString(data.navlogString);
			};
		}

		// Fallback: listen to localStorage changes (other tabs same origin)
		window.addEventListener('storage', function (ev) {
			if (!ev || ev.key !== 'navlogSyncLast') {
				return;
			}
			if (typeof ev.newValue !== 'string') {
				return;
			}
			var incomingHash = hashNavlogString(ev.newValue);
			if (incomingHash === lastBroadcastNavlogHash) {
				return;
			}
			applyNavlogSyncString(ev.newValue);
		});

		// Fallback polling (covers contexts where storage events are suppressed)
		// Reduced from 1000ms to 4000ms - storage events are the primary sync mechanism
		setInterval(function () {
			try {
				var stored = localStorage.getItem('navlogSyncLast');
				if (typeof stored !== 'string') {
					return;
				}
				var hash = hashNavlogString(stored);
				if (hash && hash !== lastLocalNavlogHash && hash !== lastBroadcastNavlogHash) {
					lastLocalNavlogHash = hash;
					applyNavlogSyncString(stored);
				}
			} catch (e) {
				// ignore polling errors
			}
		}, 4000);

		// Initial pull from server storage if available
		setTimeout(function () {
			loadNavlogFromServer();
		}, 200);

		// Periodic pull to keep external mirror in sync even across origins.
		setInterval(function () {
			pollNavlogFromServer();
		}, NAVLOG_PULL_INTERVAL_MS);

	} catch (err) {
		console.warn('[Navlog] Sync init failed:', err);
	}
}

function pollNavlogFromServer() {
	if (!isNavlogActiveTab()) {
		logVerbose("[Navlog] Skip poll - navlog tab not active in Kneeboard");
		return;
	}
	// First check if there's a newer version on the server
	var timestampUrl = getNavlogSyncUrl("getNavlogTimestamp");
	fetch(timestampUrl)
		.then(function (res) {
			if (!res.ok) throw new Error(res.status + " " + res.statusText);
			return res.text();
		})
		.then(function (timestampStr) {
			var serverTimestamp = parseInt(timestampStr) || 0;

			// Only fetch data if server has newer version
			if (serverTimestamp > lastServerTimestamp) {
				NAVLOG_DEBUG && console.log("[Navlog] Server has newer data, timestamp:", serverTimestamp, "vs local:", lastServerTimestamp);

				// Fetch the actual data
				var dataUrl = getNavlogSyncUrl("getNavlogValues");
				return fetch(dataUrl)
					.then(function (res) {
						if (!res.ok) throw new Error(res.status + " " + res.statusText);
						// Get timestamp from header
						var timestamp = res.headers.get("X-Navlog-Timestamp");
						if (timestamp) {
							lastServerTimestamp = parseInt(timestamp) || 0;
						}
						return res.text();
					})
					.then(function (text) {
						if (typeof text !== "string" || !text) {
							return;
						}
						var incomingHash = hashNavlogString(text);
						if (incomingHash && incomingHash === lastServerNavlogHash) {
							NAVLOG_DEBUG && console.log("[Navlog] ✓ Pull prevented - server data hash unchanged (echo detected)");
							return;
						}
						applyNavlogSyncString(text);
						console.info("[Navlog] Polled and applied server data with timestamp:", lastServerTimestamp);
						lastServerNavlogHash = incomingHash;
					});
			}
		})
		.catch(function (err) {
			NAVLOG_DEBUG && console.warn("[Navlog] poll from server failed:", err);
		});
}

function loadNavlogFromServer() {
	var url = getNavlogSyncUrl("getNavlogValues");
	fetch(url)
		.then(function (res) {
			if (!res.ok) throw new Error(res.status + " " + res.statusText);
			return res.text();
		})
		.then(function (text) {
			if (typeof text !== "string") return;
			if (!text) return;
			if (!isNavlogActiveTab()) {
				logVerbose("[Navlog] Skip initial load apply - navlog tab not active in Kneeboard");
				return;
			}
			var incomingHash = hashNavlogString(text);
			lastServerNavlogHash = incomingHash;
			// Don't set lastServerNavlogHash here for initial load
			// Let pollNavlogFromServer handle hash tracking for subsequent updates
			// This ensures that polling will detect changes after initial load
			applyNavlogSyncString(text);
			// Log-time wird automatisch in applyNavlogSyncString berechnet
			// Mark initial load as complete after first successful load
			if (!initialNavlogLoadComplete) {
				initialNavlogLoadComplete = true;
				NAVLOG_DEBUG && console.info("[Navlog] Initial load complete - future updates will use normal processing");
			}
			NAVLOG_DEBUG && console.info("[Navlog] loaded server navlog (initial load, not setting hash)");
		})
		.catch(function (err) {
			NAVLOG_DEBUG && console.warn("[Navlog] load from server failed:", err);
		});
}

// ============================================================================
// LOG-TIME BERECHNUNG - Automatisch aus Block_out und Block_in
// ============================================================================

/**
 * Parst einen Zeit-String in verschiedenen Formaten zu Minuten seit Mitternacht
 * Akzeptierte Formate: "12:34", "1234", "12.34", "12 34"
 * @param {string} timeStr - Zeit-String
 * @returns {number|null} - Minuten seit Mitternacht oder null bei ungültigem Format
 */
function parseTimeToMinutes(timeStr) {
	if (!timeStr || typeof timeStr !== 'string') return null;

	// Whitespace entfernen
	timeStr = timeStr.trim();
	if (timeStr === '') return null;

	var hours, minutes;

	// Format mit Trennzeichen: "12:34", "12.34", "12 34"
	var match = timeStr.match(/^(\d{1,2})[\s:.\-](\d{2})$/);
	if (match) {
		hours = parseInt(match[1], 10);
		minutes = parseInt(match[2], 10);
	} else {
		// Format ohne Trennzeichen: "1234" oder "234" (2:34)
		var digitsOnly = timeStr.replace(/\D/g, '');
		if (digitsOnly.length === 4) {
			hours = parseInt(digitsOnly.substring(0, 2), 10);
			minutes = parseInt(digitsOnly.substring(2, 4), 10);
		} else if (digitsOnly.length === 3) {
			hours = parseInt(digitsOnly.substring(0, 1), 10);
			minutes = parseInt(digitsOnly.substring(1, 3), 10);
		} else {
			return null;
		}
	}

	// Validierung
	if (isNaN(hours) || isNaN(minutes)) return null;
	if (hours < 0 || hours > 23) return null;
	if (minutes < 0 || minutes > 59) return null;

	return hours * 60 + minutes;
}

/**
 * Formatiert Minuten zu "H:MM" Format
 * @param {number} totalMinutes - Gesamtminuten
 * @returns {string} - Formatierte Zeit "H:MM"
 */
function formatMinutesToTime(totalMinutes) {
	if (totalMinutes < 0) totalMinutes += 24 * 60; // Über Mitternacht
	var hours = Math.floor(totalMinutes / 60);
	var minutes = totalMinutes % 60;
	return hours + ':' + (minutes < 10 ? '0' : '') + minutes;
}

/**
 * Berechnet die Log-time aus Block_out und Block_in
 * Berechnet IMMER wenn beide Block-Zeiten gültig sind UND Log-time leer ist
 * @param {boolean} forceOverwrite - Wenn true, überschreibt auch vorhandene Log-time
 */
function calculateLogTime(forceOverwrite) {
	var blockOutField = document.getElementById('Block_out');
	var blockInField = document.getElementById('Block_in');
	var logTimeField = document.getElementById('Log-time');

	if (!blockOutField || !blockInField || !logTimeField) return;

	// Nicht überschreiben wenn Log-time bereits gefüllt ist (außer bei forceOverwrite)
	if (!forceOverwrite && logTimeField.value && logTimeField.value.trim() !== '') {
		return;
	}

	var blockOutMinutes = parseTimeToMinutes(blockOutField.value);
	var blockInMinutes = parseTimeToMinutes(blockInField.value);

	// Nur berechnen wenn beide Zeiten gültig sind
	if (blockOutMinutes === null || blockInMinutes === null) return;

	// Differenz berechnen (berücksichtigt Mitternacht)
	var diffMinutes = blockInMinutes - blockOutMinutes;
	if (diffMinutes < 0) {
		diffMinutes += 24 * 60; // Über Mitternacht geflogen
	}

	// Log-time setzen
	var logTime = formatMinutesToTime(diffMinutes);
	if (logTimeField.value !== logTime) {
		logTimeField.value = logTime;
		NAVLOG_DEBUG && console.log('[Navlog] Log-time berechnet:', logTime, 'aus Block_out:', blockOutField.value, 'Block_in:', blockInField.value);
	}
}

/**
 * Initialisiert die Event-Listener für Block_out und Block_in
 * Bei Änderung wird Log-time neu berechnet (mit Überschreiben)
 */
function initBlockTimeListeners() {
	var blockOutField = document.getElementById('Block_out');
	var blockInField = document.getElementById('Block_in');

	if (blockOutField && !blockOutField.dataset.blockTimeListenerAdded) {
		blockOutField.addEventListener('input', function() {
			calculateLogTime(true); // Mit Überschreiben
		});
		blockOutField.dataset.blockTimeListenerAdded = 'true';
	}

	if (blockInField && !blockInField.dataset.blockTimeListenerAdded) {
		blockInField.addEventListener('input', function() {
			calculateLogTime(true); // Mit Überschreiben
		});
		blockInField.dataset.blockTimeListenerAdded = 'true';
	}
}

function saveValuesImmediate() {
	// Log-time automatisch berechnen
	calculateLogTime();

	var now = Date.now();
	if (now - lastImmediateSave < IMMEDIATE_SAVE_THROTTLE_MS) {
		return;
	}
	lastImmediateSave = now;
	if (typeof receiveMessages !== 'undefined') {
		receiveMessages = false;
	}
	try {
		saveNavlogToLocalStorage();
	} catch (e) {
		console.warn('[Navlog] Immediate save failed:', e);
	}
	clearTimeout(timeout);
	timeout = setTimeout(enableMessageReceiver, MESSAGE_RECEIVER_TIMEOUT_MS);
}

// ============================================================================
// SAVE/LOAD FUNCTIONS
// ============================================================================
function saveValues() {
	// Log-time automatisch berechnen
	calculateLogTime();

	// Don't disable receiveMessages in browser mirror mode
	// Both sides should be able to receive updates at any time
	// The hash check will prevent update loops
	logVerbose('[saveValues] Keeping receiveMessages enabled for bidirectional sync');
	clearTimeout(saveTimeout);
	saveTimeout = setTimeout(function () {
		if (typeof receiving !== 'undefined' && receiving === true) {
			logVerbose('[saveValues] Skipping save - currently receiving data');
			return;
		}
		var navRoot = ensureNavlogContainer();
		if (!navRoot) {
			return;
		}

		var textareas = navRoot.getElementsByTagName('textarea');
		var navlogString = collectNavlogString();

		// Check if data has actually changed before sending
		var currentHash = hashNavlogString(navlogString);
		if (currentHash === lastBroadcastNavlogHash) {
			logVerbose('[saveValues] No changes detected, skipping broadcast');
			// Still save to localStorage but don't broadcast
			clearTimeout(sendValue);
			sendValue = setTimeout(function () { saveNavlogToLocalStorage(); }, SAVE_STORAGE_DELAY_MS);
			timeout = setTimeout(enableMessageReceiver, MESSAGE_RECEIVER_TIMEOUT_MS);
			return;
		}

		logVerbose('[saveValues] Changes detected, broadcasting navlog');

		if (typeof localStorage !== 'undefined') {
			// localStorage.setItem("flightType", flightType); // Disabled - using server sync
		}

		for (var j = 0; j < textareas.length; j++) {
			if (textareas[j].matches(":focus")) {
				if (typeof tempValue !== 'undefined') {
					tempValue = textareas[j].value;
				}
			}
		}

		clearTimeout(timeout);
		clearTimeout(sendValue);
		sendValue = setTimeout(function () { saveNavlogToLocalStorage(); }, SAVE_STORAGE_DELAY_MS);
		timeout = setTimeout(enableMessageReceiver, MESSAGE_RECEIVER_TIMEOUT_MS);
		if (!suppressNavlogPush) {
			broadcastNavlogString(navlogString);
			pushNavlogToServer(navlogString);
		}
	}, SAVE_DEBOUNCE_MS);
}

function saveNavlogToLocalStorage() {
	// DEAKTIVIERT: Server ist Single Source of Truth
	// localStorage-Backup verursacht Konflikte mit OFP-Mapping (FL13 statt FL170)
	NAVLOG_DEBUG && console.log("[Navlog] localStorage saving DISABLED - using server sync only");
	return;
}

function clearNavlogLocalStorage() {
	if (typeof localStorage === 'undefined') {
		return;
	}
	var navRoot = ensureNavlogContainer();
	if (!navRoot) {
		return;
	}
	var total = navRoot.getElementsByTagName('textarea').length;
	for (var i = 0; i < total; i++) {
		localStorage.removeItem(i);
	}
}

/**
 * Löscht localStorage-Einträge für OFP-gemappte Felder
 * Wird aufgerufen bevor ein neuer Flugplan geladen wird,
 * damit die neuen OFP-Werte nicht von alten localStorage-Werten überschrieben werden
 */
function clearOFPFieldsFromLocalStorage() {
	if (typeof localStorage === 'undefined') return;
	if (typeof OFP_NAVLOG_MAPPING === 'undefined') return;

	var navRoot = ensureNavlogContainer();
	var textareas = navRoot ? navRoot.getElementsByTagName('textarea') : [];

	// Alle OFP-Feld-IDs sammeln
	var ofpFieldIds = Object.keys(OFP_NAVLOG_MAPPING);

	// Auch berechnete Felder einbeziehen
	if (typeof OFP_COMPUTED_FIELDS !== 'undefined') {
		for (var key in OFP_COMPUTED_FIELDS) {
			if (OFP_COMPUTED_FIELDS.hasOwnProperty(key)) {
				ofpFieldIds.push(key);
			}
		}
	}

	// Für jede Textarea: prüfen ob ihre ID in den OFP-Feldern ist
	for (var i = 0; i < textareas.length; i++) {
		var textarea = textareas[i];
		var id = textarea.id || textarea.name;
		if (id && ofpFieldIds.indexOf(id) !== -1) {
			// Diese Textarea ist ein OFP-Feld - localStorage-Eintrag löschen
			localStorage.removeItem(String(i));
			NAVLOG_DEBUG && console.log('[OFP Clear] Removed localStorage for OFP field:', id, 'index:', i);
		}
	}

	NAVLOG_DEBUG && console.log('[OFP Clear] Cleared localStorage for', ofpFieldIds.length, 'OFP field definitions');
}

function clearAllNavlogTextareas() {
	var navRoot = ensureNavlogContainer();
	if (!navRoot) {
		return;
	}
	var areas = navRoot.getElementsByTagName('textarea');
	for (var i = 0; i < areas.length; i++) {
		if (areas[i]) {
			areas[i].value = '';
		}
	}
}

function enableMessageReceiver() {
	if (typeof receiveMessages !== 'undefined') {
		receiveMessages = true;
	}
}

function readValues() {
	// Style und activeTab aus localStorage - OK
	if (typeof localStorage !== 'undefined') {
		if (localStorage.getItem("style")) {
			if (typeof style !== 'undefined') {
				style = localStorage.getItem("style");
			}
		}

		if (localStorage.getItem("activeTab")) {
			if (typeof activeTab !== 'undefined') {
				activeTab = localStorage.getItem("activeTab");
			}
		}
	}

	// TEXTAREA-WERTE NICHT AUS LOCALSTORAGE LADEN!
	// Server ist Single Source of Truth - localStorage verursacht FL13 statt FL170
	NAVLOG_DEBUG && console.log("[Navlog] localStorage textarea loading DISABLED - server is single source of truth");

	btnVFRClicked(); // Default to VFR

	// Log-time berechnen falls Block_out und Block_in gefüllt sind aber Log-time leer
	calculateLogTime(false);
}

function setFlightType() {
	if (flightType == "vfr") {
		btnVFRClicked();
	}
	else if (flightType == "ifr") {
		btnIFRClicked();
	}
	else if (flightType == "zy") {
		btnZYClicked();
	}
}

// ============================================================================
// UI TOGGLE FUNCTIONS
// ============================================================================
function toggleDisplay(elementId, pairedElementId) {
	var el = document.getElementById(elementId);
	if (!el) {
		return;
	}
	if (el.style.display == "none") {
		el.style.display = "";
		if (pairedElementId) {
			var paired = document.getElementById(pairedElementId);
			if (paired) {
				paired.style.display = "";
			}
		}
	}
	else {
		el.style.display = "none";
		if (pairedElementId) {
			var paired = document.getElementById(pairedElementId);
			if (paired) {
				paired.style.display = "none";
			}
		}
	}
}

function hideClearance() {
	toggleDisplay("clearance");
}

function hideAdvisatories() {
	toggleDisplay("advisatories", "frequencys");
}

function hideFlightPlan() {
	toggleDisplay("flightPlan");
}

function hideDestination() {
	toggleDisplay("destination");
}

function hideAlternate() {
	toggleDisplay("alternate");
}

function hideWeatherLog() {
	toggleDisplay("weatherLog");
}

function hideNotam() {
	toggleDisplay("notam");
}

function hideWaypointsVFR() {
	toggleDisplay("waypointsVFRWaypoints");
}

function hideWaypointsIFR() {
	toggleDisplay("waypointsIFRWaypoints");
}

function hideWaypointsZY() {
	toggleDisplay("waypointsZYWaypoints");
}

// ============================================================================
// MESSAGE HANDLING
// ============================================================================
var lastMessage = "";
var receiving = false;
var valueChanged = false;
var lastPositionUpdate = 0;
var positionUpdateInterval = 5000; // 5 seconds throttle for Position updates

function handleIncomingMessage(e) {
	if (typeof e.data === "string" && e.data.startsWith("colors(")) {
		var colorString = e.data.slice(7, -1);
		var parts = colorString.split("_");
		var light = parts[0];
		var dark = parts[1];
		var fontLight = parts[2];
		var fontDark = parts[3];

		colorLight = light ? light.trim() : colorLight;
		colorDark = dark ? dark.trim() : colorDark;
		var fontColorLight = fontLight ? fontLight.trim() : '';
		var fontColorDark = fontDark ? fontDark.trim() : '';

		document.documentElement.style.setProperty('--light', colorLight);
		document.documentElement.style.setProperty('--dark', colorDark);
		document.documentElement.style.setProperty('--fontLight', fontColorLight);
		document.documentElement.style.setProperty('--fontDark', fontColorDark);

		if (typeof localStorage !== 'undefined') {
			localStorage.setItem("colorLight", colorLight);
			localStorage.setItem("colorDark", colorDark);
			localStorage.setItem("fontColorLight", fontColorLight);
			localStorage.setItem("fontColorDark", fontColorDark);
		}
		NAVLOG_DEBUG && console.log("Farben aktualisiert & gespeichert:", colorLight, colorDark, fontColorLight, fontColorDark);
	}

	if (typeof e.data !== "string") {
		return;
	}
	var separatorIndex = e.data.indexOf(':');
	if (separatorIndex === -1) {
		return;
	}
	var sender2 = e.data.substr(0, separatorIndex);
	var message2 = e.data.substr(separatorIndex + 1, e.data.length);

	// Handle Position messages with throttling and tab-awareness
	if (sender2 == 'Position') {
		// Check if we're on Map or Navlog tab
		var activeTab = '';
		if (typeof localStorage !== 'undefined') {
			activeTab = localStorage.getItem('activeTab') || '';
		}
		if (activeTab !== 'map' && activeTab !== 'navlog') {
			return; // Don't process Position if not on Map or Navlog
		}

		// Throttle updates to every 5 seconds
		var now = Date.now();
		if (now - lastPositionUpdate < positionUpdateInterval) {
			return;
		}
		lastPositionUpdate = now;

		// Extract IAS and altitude from Position message, calculate TAS
		var posData = message2.split("_");
		var altitudeFt = parseFloat(posData[2]);
		var iasValue = parseFloat(posData[4]);

		if (Number.isFinite(iasValue) && Number.isFinite(altitudeFt)) {
			// Calculate TAS from IAS using altitude correction
			// Based on: TAS = IAS / sqrt(density ratio)
			// Using standard atmosphere: temperature decreases 1.98°C per 1000ft
			// At sea level: T0 = 288.15K (15°C)
			var altitudeThousands = altitudeFt / 1000;
			var tempAtAlt = 288.15 - (1.98 * altitudeThousands);
			if (tempAtAlt > 0) {
				var densityCorrection = Math.sqrt(288.15 / tempAtAlt);
				var tasValue = iasValue * densityCorrection;

				var tasField = document.getElementById('True-Airspeed');
				if (tasField) {
					var roundedTas = Math.round(tasValue);
					if (tasField.value !== String(roundedTas)) {
						tasField.value = roundedTas;
						NAVLOG_DEBUG && console.log('[Navlog] TAS updated:', roundedTas, 'from IAS:', iasValue, 'at alt:', altitudeFt);
					}
				} else {
					console.warn('[Navlog] True-Airspeed element not found in DOM');
				}
			}
		}
		return;
	}

	if (lastMessage === e.data) {
		return;
	}
	lastMessage = e.data;

	if (sender2 == 'navlog') {
		// Check if this is the same data we already have
		var incomingHash = hashNavlogString(message2);
		var currentNavlogString = collectNavlogString();
		var currentHash = hashNavlogString(currentNavlogString);

		// Skip hash check on initial load - always accept first data from Kneeboard
		if (!initialNavlogLoadComplete) {
			NAVLOG_DEBUG && console.log('[handleIncomingMessage] Initial load - accepting data without hash check');
		} else {
			// For data coming from applyNavlogSyncString (server polling), we trust it
			// The polling function already verified this is new data from server
			// Check for explicit server poll flag
			var isFromServerPolling = e.isServerPoll === true;

			if (!isFromServerPolling) {
				// Only check hashes for broadcast messages between browser tabs
				// Check if this is the same data we already have (only for broadcast messages)
				if (incomingHash === currentHash) {
					NAVLOG_DEBUG && console.log('[handleIncomingMessage] Broadcast data is identical to current data, skipping update');
					return;
				}

				if (incomingHash === lastAppliedNavlogHash) {
					NAVLOG_DEBUG && console.log('[handleIncomingMessage] Already applied this broadcast data, skipping duplicate');
					return;
				}
			} else {
				NAVLOG_DEBUG && console.log('[handleIncomingMessage] Data from server polling, applying without hash check');
			}
		}

		receiving = true;

		// Always allow receiving in browser mirror mode
		if (typeof receiveMessages === 'undefined') {
			receiveMessages = true;
		}

		NAVLOG_DEBUG && console.log('[handleIncomingMessage] receiveMessages:', receiveMessages);

		if (receiveMessages === true) {
			var values = message2.split("~");

			NAVLOG_DEBUG && console.log('[handleIncomingMessage] Received navlog with', values.length, 'values (hash:', incomingHash.substring(0, 20), ')');

			// Extract flight type and row count
			var incomingFlightType = values[0];
			var incomingRowCount = parseInt(values[1]) || 0;

			NAVLOG_DEBUG && console.log('[handleIncomingMessage] Flight type:', incomingFlightType, ', Row count:', incomingRowCount);

			if (flightType != incomingFlightType) {
				NAVLOG_DEBUG && console.log('[handleIncomingMessage] Switching flight type from', flightType, 'to', incomingFlightType);
				flightType = incomingFlightType;
				setFlightType();
				NAVLOG_DEBUG && console.log('[handleIncomingMessage] Flight type switched');
			}

			// Ensure we have the exact number of waypoint rows
			NAVLOG_DEBUG && console.log('[handleIncomingMessage] Ensuring exact row count:', incomingRowCount, 'for', flightType);
			ensureExactRowCount(flightType, incomingRowCount);

			var allNavlogTextareas = document.getElementById('navlog') ? document.getElementById('navlog').getElementsByTagName('textarea') : [];
			// Apply values to all textareas in document order to keep indexes aligned with the sync string
			var navlogTextareas = Array.prototype.slice.call(allNavlogTextareas);
			var visibleCount = 0;
			for (var j = 0; j < navlogTextareas.length; j++) {
				if (navlogTextareas[j].offsetParent !== null) {
					visibleCount++;
				}
			}
			NAVLOG_DEBUG && console.log('[handleIncomingMessage] Total textareas:', navlogTextareas.length, 'Visible:', visibleCount, 'Hidden:', (navlogTextareas.length - visibleCount));
			if (navlogTextareas.length < values.length - 1) {
				NAVLOG_DEBUG && console.warn('[handleIncomingMessage] Fewer textareas than incoming values. Values:', (values.length - 1), 'Textareas:', navlogTextareas.length);
			}

			var updatedCount = 0;
			var skippedCount = 0;
			var firstWaypointIndex = -1;
			var lastNonEmptyIndex = -1;
			var startIndex = 2; // skip flightType and row count

			// Find the first waypoint value and last non-empty value (skip meta fields)
			for (var k = startIndex; k < values.length; k++) {
				if (values[k] && values[k].trim()) {
					lastNonEmptyIndex = k;
					if (firstWaypointIndex === -1 && k > 30) { // Waypoints typically start after ~30 header fields
						firstWaypointIndex = k;
					}
				}
			}
			NAVLOG_DEBUG && console.log('[handleIncomingMessage] First waypoint at index:', firstWaypointIndex, ', Last non-empty:', lastNonEmptyIndex);

			// Assign values to textareas
			NAVLOG_DEBUG && console.log('[handleIncomingMessage] Assigning values to textareas...');
			for (var i = startIndex; i < values.length; i++) {
				try {
					var targetIndex = i - startIndex;
					var targetTextarea = navlogTextareas[targetIndex];
					if (!targetTextarea) {
						skippedCount++;
						if (values[i] && values[i].trim()) {
							NAVLOG_DEBUG && console.log('[handleIncomingMessage] Skipped non-empty value at index', i, ':', values[i].substring(0, 20));
						}
						continue;
					}

					if (targetTextarea.matches(":focus")) {
						if (typeof tempValue !== 'undefined' && tempValue != values[i]) {
							if (targetTextarea.value != values[i]) {
								targetTextarea.value = values[i];
								if (typeof localStorage !== 'undefined') {
									localStorage.setItem(targetIndex, values[i]);
									// localStorage.setItem(flightType, values[0]); // Disabled - using server sync
								}
								valueChanged = true;
								updatedCount++;
								if (typeof Keyboard !== 'undefined' && Keyboard.properties) {
									Keyboard.properties.value = values[i];
								}
								if (typeof tempValue !== 'undefined') {
									tempValue = values[i];
								}
								if (typeof textstart !== 'undefined') {
									textstart = values[i];
								}
								if (typeof currentValue !== 'undefined') {
									currentValue = values[i];
								}
							}
						}
					}
					else {
						if (targetTextarea.value != values[i]) {
							targetTextarea.value = values[i];
							if (typeof localStorage !== 'undefined') {
								localStorage.setItem(targetIndex, values[i]);
								// localStorage.setItem(flightType, values[0]); // Disabled - using server sync
							}
							valueChanged = true;
							updatedCount++;
						}
					}
				}
				catch (e) {
					NAVLOG_DEBUG && console.log('[handleIncomingMessage] Error at index', i, ':', e);
				}
			}

			NAVLOG_DEBUG && console.log('[handleIncomingMessage] Updated', updatedCount, 'fields, skipped', skippedCount);

			// Update the hash to mark this data as applied (even though we're only syncing rows)
			lastAppliedNavlogHash = incomingHash;
			// Only update broadcast hash if we're in browser mirror (navigationlog.html)
			// Don't update it in kneeboard context (navlog.html in iframe)
			var isBrowserMirror = window.location.pathname.indexOf('navigationlog.html') >= 0;
			var isInIframe = window.parent !== window;
			if (isBrowserMirror || !isInIframe) {
				// We're in browser mirror or standalone navlog, update broadcast hash
				lastBroadcastNavlogHash = incomingHash;
			} else {
				// We're in kneeboard (iframe), skip broadcast hash update
				NAVLOG_DEBUG && console.log('[handleIncomingMessage] Kneeboard iframe context, skipping broadcast hash update');
			}

			// Mark initial load as complete after first successful update
			if (!initialNavlogLoadComplete) {
				initialNavlogLoadComplete = true;
				NAVLOG_DEBUG && console.log('[handleIncomingMessage] Initial navlog load complete - future updates will use hash checking');
			}
		} else {
			NAVLOG_DEBUG && console.log('[handleIncomingMessage] receiveMessages is false, skipping update');
		}
		saveNavlogToLocalStorage();
		receiving = false;

		// Re-enable receiving for browser mirror mode
		if (typeof receiveMessages !== 'undefined') {
			receiveMessages = true;
		}
	}
}

// ============================================================================
// TIME/DATE ICON FUNCTIONALITY - Click icon to fill current time/date
// ============================================================================
var timeIconFields = {
	'Block_out': 'time',
	'Block_in': 'time',
	'Waypoints-VFR-Time-off': 'time',
	'Waypoints-IFR-Time-off': 'time',
	'Waypoints-ZY-Time-off': 'time',
	'Dep-Act': 'time',
	'Date': 'date'
};

function formatUTCTime() {
	var now = new Date();
	var hours = ('0' + now.getUTCHours()).slice(-2);
	var minutes = ('0' + now.getUTCMinutes()).slice(-2);
	return hours + minutes;  // Format: HHMM (ohne Doppelpunkt, luftfahrtüblich)
}

function formatUTCDate() {
	var now = new Date();
	var day = ('0' + now.getUTCDate()).slice(-2);
	var months = ['JAN', 'FEB', 'MAR', 'APR', 'MAY', 'JUN',
	              'JUL', 'AUG', 'SEP', 'OCT', 'NOV', 'DEC'];
	var month = months[now.getUTCMonth()];
	var year = now.getUTCFullYear();
	return day + '-' + month + '-' + year;  // Format: DD-MMM-YYYY
}

/**
 * Klick auf Uhr-Icon = aktuelle Zeit/Datum eintragen
 * Keine Live-Uhr mehr - nur einmaliges Eintragen bei Klick
 */
function fillCurrentTime(fieldId) {
	var field = document.getElementById(fieldId);
	if (!field) return;

	var fieldType = timeIconFields[fieldId];
	if (fieldType === 'date') {
		field.value = formatUTCDate();
	} else {
		field.value = formatUTCTime();
	}

	// Log-time neu berechnen wenn Block_out oder Block_in geändert wurde
	if (fieldId === 'Block_out' || fieldId === 'Block_in') {
		calculateLogTime(true);
	}

	saveValues();
	NAVLOG_DEBUG && console.log('[TimeIcon] Filled', fieldId, 'with current', fieldType);
}

function createTimeIcon(fieldId) {
	var fieldType = timeIconFields[fieldId];
	var icon = document.createElement('span');
	icon.id = 'timeIcon_' + fieldId;

	// Use cssgg SVG icons - calendar for date, time (clock) for time
	var iconName = fieldType === 'date' ? 'calendar' : 'time';
	if (typeof cssggSvg !== 'undefined' && cssggSvg[iconName]) {
		icon.innerHTML = cssggSvg[iconName];
		// Style the SVG
		var svg = icon.querySelector('svg');
		if (svg) {
			svg.style.width = '16px';
			svg.style.height = '16px';
			svg.style.verticalAlign = 'middle';
			svg.style.color = 'var(--fontDark)';
		}
	} else {
		// Fallback to emoji if cssgg not available
		icon.textContent = fieldType === 'date' ? '\uD83D\uDCC5' : '\u23F0';
	}

	// Style
	icon.style.cssText = 'cursor:pointer;color:var(--fontDark);position:absolute;top:2px;right:4px;z-index:10;';
	icon.title = fieldType === 'date' ? 'Aktuelles Datum eintragen' : 'Aktuelle Zeit eintragen';

	icon.addEventListener('click', function(e) {
		e.preventDefault();
		e.stopPropagation();
		fillCurrentTime(fieldId);
	});

	// Prevent touch from triggering textarea focus
	icon.addEventListener('touchstart', function(e) {
		e.preventDefault();
		e.stopPropagation();
		fillCurrentTime(fieldId);
	});

	return icon;
}

function initTimeIcons() {
	// Pulse-Animation deaktiviert - keine addPulseStyle() mehr

	Object.keys(timeIconFields).forEach(function(fieldId) {
		var field = document.getElementById(fieldId);
		if (!field) return;

		var parentCell = field.closest('td');
		if (!parentCell) return;

		// Prüfe ob Icon bereits existiert
		if (parentCell.querySelector('#timeIcon_' + fieldId)) return;

		// Position parent cell relative for absolute icon positioning
		parentCell.style.position = 'relative';

		// Create and insert icon
		var icon = createTimeIcon(fieldId);
		parentCell.appendChild(icon);

		// NICHT automatisch starten - Zeit erst bei Klick eintragen
		// var fieldType = timeIconFields[fieldId];
		// if (fieldType !== 'date') {
		// 	startLiveClock(fieldId);
		// }
	});

	// Füge Time-Icons für alle ATA-Felder hinzu (VFR, IFR, ZY)
	var ataFields = document.querySelectorAll('[id^="Waypoints-VFR-ATA-"], [id^="Waypoints-IFR-ATA-"], [id^="Waypoints-ZY-ATA-"]');
	ataFields.forEach(function(field) {
		var fieldId = field.id;
		var parentCell = field.closest('td');
		if (!parentCell) return;

		// Prüfe ob Icon bereits existiert
		if (parentCell.querySelector('#timeIcon_' + fieldId)) return;

		// Position parent cell relative for absolute icon positioning
		parentCell.style.position = 'relative';

		// Create and insert icon
		timeIconFields[fieldId] = 'time';  // Registriere Feld temporär
		var icon = createTimeIcon(fieldId);
		parentCell.appendChild(icon);
	});

	NAVLOG_DEBUG && console.log('[TimeIcon] Initialized icons for:', Object.keys(timeIconFields).join(', '));
}
