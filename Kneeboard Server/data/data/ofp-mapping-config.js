/**
 * OFP zu NavLog Mapping-Konfiguration
 *
 * Diese Datei definiert das Mapping zwischen Simbrief OFP-Feldern und NavLog-Feldern.
 *
 * Struktur pro Feld:
 * {
 *   navlogFieldId: {
 *     source: 'OFP.Pfad.Zum.Feld',      // Pfad im OFP-Objekt (Punkt-Notation)
 *     sourceAlt: ['Alternative.Pfad'],   // Alternative Pfade falls source leer (optional)
 *     parse: function(value, ofp) {},    // Parser-Funktion (optional) - oder String-Referenz auf OFP_PARSERS
 *     format: '{value} ft',              // Format-String mit {value} Platzhalter (optional)
 *     enabled: true                       // Aktiviert/Deaktiviert (default: true)
 *   }
 * }
 *
 * Spezielle Pfade:
 * - '_computed.fieldName' - Verwendet berechnete Felder aus OFP_COMPUTED_FIELDS
 */

var OFP_NAVLOG_MAPPING = {

    // ========================================
    // FLUGHAFEN-DATEN
    // ========================================
    'Departure-Name': {
        source: 'Origin.Name',
        sourceAlt: ['Origin.name', 'Origin.Icao_code'],
        enabled: true
    },
    'Arrival-Name': {
        source: 'Destination.Name',
        sourceAlt: ['Destination.name', 'Destination.Icao_code'],
        enabled: true
    },
    'Departure-Point': {
        source: '_computed.departurePoint',
        enabled: true
    },
    'Departure-Elev': {
        source: 'Origin.Elevation',
        sourceAlt: ['Origin.elevation'],
        format: '{value} ft',
        enabled: true
    },
    'Arrival-Elev': {
        source: 'Destination.Elevation',
        sourceAlt: ['Destination.elevation'],
        format: '{value} ft',
        enabled: true
    },
    'Departure-RWY': {
        source: 'Origin.Plan_rwy',
        sourceAlt: ['Origin.plan_rwy'],
        enabled: true
    },
    'Arrival-RWY': {
        source: 'Destination.Plan_rwy',
        sourceAlt: ['Destination.plan_rwy'],
        enabled: true
    },
    'Departure-Sid': {
        source: 'General.Sid_name',
        sourceAlt: ['General.sid_name', 'General.Sid', 'General.sid', 'Origin.Sid', 'Origin.sid', '_computed.sid'],
        enabled: true
    },
    'Arrival-Star': {
        source: 'General.Star_name',
        sourceAlt: [
            'General.star_name',
            'General.Star',
            'General.star',
            'Atc.Star',
            'Atc.star',
            'Atc.Star_name',
            'Atc.star_name',
            'Destination.Star',
            'Destination.star',
            'Destination.Star_name',
            'Destination.star_name',
            '_computed.star'
        ],
        enabled: true
    },

    // ========================================
    // FLUGZEUG-DATEN
    // ========================================
    'Aircraft-identification': {
        source: 'Atc.Callsign',
        sourceAlt: ['Atc.callsign'],
        enabled: true
    },
    'Aircraft-Type-Equipment': {
        source: 'Aircraft.Icaocode',
        sourceAlt: ['Aircraft.icaocode'],
        parse: function(value, ofp) {
            var equip = getNestedValue(ofp, 'Aircraft.Equip') ||
                        getNestedValue(ofp, 'Aircraft.equip') || '';
            return equip ? value + '/' + equip : value;
        },
        enabled: true
    },
    'Aircraft_reg': {
        source: 'Aircraft.Reg',
        sourceAlt: ['Aircraft.reg'],
        enabled: true
    },
    'True-Airspeed': {
        source: 'General.Cruise_tas',
        sourceAlt: ['General.cruise_tas'],
        enabled: true
    },

    // ========================================
    // ZEIT-DATEN
    // ========================================
    'Dep-Est': {
        source: 'Times.Sched_off',
        sourceAlt: ['Times.sched_off', 'Times.Est_off', 'Times.est_off'],
        parse: function(value) {
            if (!value) return null;
            var date = new Date(parseInt(value) * 1000);
            var hours = ('0' + date.getUTCHours()).slice(-2);
            var mins = ('0' + date.getUTCMinutes()).slice(-2);
            return hours + mins;  // Format: HHMM (ohne Doppelpunkt, luftfahrtüblich)
        },
        enabled: true
    },
    'Destination-Hours': {
        source: 'Times.Est_time_enroute',
        sourceAlt: ['Times.est_time_enroute'],
        parse: function(value) {
            // WICHTIG: Simbrief liefert Est_time_enroute in SEKUNDEN!
            var secs = parseInt(value);
            if (isNaN(secs)) return null;
            var totalMins = Math.floor(secs / 60);
            return Math.floor(totalMins / 60);
        },
        enabled: true
    },
    'Destination-Minutes': {
        source: 'Times.Est_time_enroute',
        sourceAlt: ['Times.est_time_enroute'],
        parse: function(value) {
            // WICHTIG: Simbrief liefert Est_time_enroute in SEKUNDEN!
            var secs = parseInt(value);
            if (isNaN(secs)) return null;
            var totalMins = Math.floor(secs / 60);
            return ('0' + (totalMins % 60)).slice(-2);
        },
        enabled: true
    },
    'Date': {
        source: 'Times.Sched_off',
        sourceAlt: ['Times.sched_off', 'Times.Est_off', 'Times.est_off'],
        parse: 'parseDateFromTimestamp',
        enabled: true
    },

    // ========================================
    // ALTERNATE
    // ========================================
    'Alternate-Airport': {
        source: 'Alternate.Icao_code',
        sourceAlt: ['Alternate.icao_code'],
        parse: function(value, ofp) {
            var name = getNestedValue(ofp, 'Alternate.Name') ||
                       getNestedValue(ofp, 'Alternate.name') || '';
            // 3-zeiliges Format: ICAO, -, Name
            return name ? value + '\n-\n' + name : value;
        },
        enabled: true
    },
    'Alternate-Route': {
        source: 'Alternate.Route',
        sourceAlt: ['Alternate.route'],
        enabled: true
    },

    // ========================================
    // FUEL / ENDURANCE
    // ========================================
    'Alternate-Hours': {
        source: '_computed.enduranceHours',
        enabled: true
    },
    'Alternate-Minutes': {
        source: '_computed.enduranceMinutes',
        enabled: true
    },
    'Waypoints-IFR-GPH': {
        source: 'Fuel.Avg_fuel_flow',
        sourceAlt: ['Fuel.avg_fuel_flow'],
        parse: function(value) {
            return Math.round(parseFloat(value));
        },
        enabled: true
    },
    'Waypoints-VFR-GPH': {
        source: 'Fuel.Avg_fuel_flow',
        sourceAlt: ['Fuel.avg_fuel_flow'],
        parse: function(value) {
            return Math.round(parseFloat(value));
        },
        enabled: true
    },
    'Waypoints-ZY-GPH': {
        source: 'Fuel.Avg_fuel_flow',
        sourceAlt: ['Fuel.avg_fuel_flow'],
        parse: function(value) {
            return Math.round(parseFloat(value));
        },
        enabled: true
    },
    'Waypoints-IFR-Dist': {
        source: 'General.Route_distance',
        sourceAlt: ['General.route_distance', 'General.Air_distance', 'General.air_distance'],
        parse: function(value) {
            var dist = parseFloat(value);
            return isNaN(dist) ? null : Math.round(dist) + ' NM';
        },
        enabled: true
    },

    // ========================================
    // TOTALS (Gesamtwerte für den Flug)
    // ========================================
    'Waypoints-IFR-Total-Time-1': {
        source: 'Times.Est_time_enroute',
        sourceAlt: ['Times.est_time_enroute', 'General.Est_time_enroute'],
        parse: function(value) {
            // WICHTIG: Simbrief liefert Est_time_enroute in SEKUNDEN!
            var secs = parseInt(value);
            if (isNaN(secs)) return null;
            var totalMins = Math.floor(secs / 60);
            var hours = Math.floor(totalMins / 60);
            var minutes = totalMins % 60;
            return hours + ('0' + minutes).slice(-2);  // Format: HMM oder HHMM (luftfahrtüblich)
        },
        enabled: true
    },
    'Waypoints-IFR-Total-GPH-1': {
        // Total Fuel = Enroute Burn (Treibstoff für die Strecke)
        source: 'Fuel.Enroute_burn',
        sourceAlt: ['Fuel.enroute_burn', 'Fuel.Trip', 'Fuel.trip'],
        parse: function(value) {
            return Math.round(parseFloat(value));
        },
        enabled: true
    },
    'Waypoints-VFR-Total-Time-1': {
        source: 'Times.Est_time_enroute',
        sourceAlt: ['Times.est_time_enroute', 'General.Est_time_enroute'],
        parse: function(value) {
            // WICHTIG: Simbrief liefert Est_time_enroute in SEKUNDEN!
            var secs = parseInt(value);
            if (isNaN(secs)) return null;
            var totalMins = Math.floor(secs / 60);
            var hours = Math.floor(totalMins / 60);
            var minutes = totalMins % 60;
            return hours + ('0' + minutes).slice(-2);  // Format: HMM oder HHMM (luftfahrtüblich)
        },
        enabled: true
    },
    'Waypoints-VFR-Total-GPH-1': {
        // Total Fuel = Enroute Burn (Treibstoff für die Strecke)
        source: 'Fuel.Enroute_burn',
        sourceAlt: ['Fuel.enroute_burn', 'Fuel.Trip', 'Fuel.trip'],
        parse: function(value) {
            return Math.round(parseFloat(value));
        },
        enabled: true
    },
    'Waypoints-ZY-Total-Time-1': {
        source: 'Times.Est_time_enroute',
        sourceAlt: ['Times.est_time_enroute', 'General.Est_time_enroute'],
        parse: function(value) {
            // WICHTIG: Simbrief liefert Est_time_enroute in SEKUNDEN!
            var secs = parseInt(value);
            if (isNaN(secs)) return null;
            var totalMins = Math.floor(secs / 60);
            var hours = Math.floor(totalMins / 60);
            var minutes = totalMins % 60;
            return hours + ('0' + minutes).slice(-2);  // Format: HMM oder HHMM (luftfahrtüblich)
        },
        enabled: true
    },
    'Waypoints-ZY-Total-GPH-1': {
        // Total Fuel = Enroute Burn (Treibstoff für die Strecke)
        source: 'Fuel.Enroute_burn',
        sourceAlt: ['Fuel.enroute_burn', 'Fuel.Trip', 'Fuel.trip'],
        parse: function(value) {
            return Math.round(parseFloat(value));
        },
        enabled: true
    },

    // ========================================
    // WEATHER LOG - DEPARTURE
    // ========================================
    'Weather-Log-Dep-Pos': {
        source: 'Origin.Metar',
        sourceAlt: ['Origin.metar'],
        parse: 'parseMetarPosition',
        enabled: true
    },
    'Weather-Log-Dep-Rep': {
        source: 'Origin.Metar',
        sourceAlt: ['Origin.metar'],
        parse: 'parseMetarReported',
        enabled: true
    },
    'Weather-Log-Dep-Fore': {
        source: 'Origin.Taf',
        sourceAlt: ['Origin.taf'],
        parse: 'parseTafForecast',
        enabled: true
    },
    'Weather-Log-Dep-Winds': {
        source: 'Origin.Metar',
        parse: 'parseMetarWind',
        enabled: true
    },
    'Weather-Log-Dep-Ice': {
        source: 'Origin.Metar',
        parse: 'parseMetarIcing',
        enabled: true
    },
    'Weather-Log-Dep-Turb': {
        source: 'Origin.Metar',
        parse: 'parseMetarTurbulence',
        enabled: true
    },

    // ========================================
    // WEATHER LOG - ARRIVAL
    // ========================================
    'Weather-Log-Arr-Pos': {
        source: 'Destination.Metar',
        sourceAlt: ['Destination.metar'],
        parse: 'parseMetarPosition',
        enabled: true
    },
    'Weather-Log-Arr-Rep': {
        source: 'Destination.Metar',
        sourceAlt: ['Destination.metar'],
        parse: 'parseMetarReported',
        enabled: true
    },
    'Weather-Log-Arr-Fore': {
        source: 'Destination.Taf',
        sourceAlt: ['Destination.taf'],
        parse: 'parseTafForecast',
        enabled: true
    },
    'Weather-Log-Arr-Winds': {
        source: 'Destination.Metar',
        parse: 'parseMetarWind',
        enabled: true
    },
    'Weather-Log-Arr-Ice': {
        source: 'Destination.Metar',
        parse: 'parseMetarIcing',
        enabled: true
    },
    'Weather-Log-Arr-Turb': {
        source: 'Destination.Metar',
        parse: 'parseMetarTurbulence',
        enabled: true
    },

    // ========================================
    // WEATHER LOG - ALTERNATE
    // ========================================
    'Weather-Log-Alt-Pos': {
        source: 'Alternate.Metar',
        sourceAlt: ['Alternate.metar'],
        parse: 'parseMetarPosition',
        enabled: true
    },
    'Weather-Log-Alt-Rep': {
        source: 'Alternate.Metar',
        sourceAlt: ['Alternate.metar'],
        parse: 'parseMetarReported',
        enabled: true
    },
    'Weather-Log-Alt-Fore': {
        source: 'Alternate.Taf',
        sourceAlt: ['Alternate.taf'],
        parse: 'parseTafForecast',
        enabled: true
    },
    'Weather-Log-Alt-Winds': {
        source: 'Alternate.Metar',
        parse: 'parseMetarWind',
        enabled: true
    },
    'Weather-Log-Alt-Ice': {
        source: 'Alternate.Metar',
        parse: 'parseMetarIcing',
        enabled: true
    },
    'Weather-Log-Alt-Turb': {
        source: 'Alternate.Metar',
        parse: 'parseMetarTurbulence',
        enabled: true
    },

    // ========================================
    // WEATHER LOG - ENROUTE
    // ========================================
    // Rep/Fore = "Ceiling / Visibility / Precipitation"
    // Abgeleitet aus Waypoint-Daten (OAT für Wolkenbildung, etc.)
    'Weather-Log-Enr-Rep': {
        source: '_computed.enrouteReported',
        enabled: true
    },
    'Weather-Log-Enr-Fore': {
        source: '_computed.enrouteForecast',
        enabled: true
    },
    'Weather-Log-Enr-Winds': {
        source: '_computed.enrouteWinds',
        enabled: true
    },
    'Weather-Log-Enr-Ice': {
        source: '_computed.enrouteIcing',
        enabled: true
    },
    'Weather-Log-Enr-Turb': {
        source: '_computed.enrouteTurbulence',
        enabled: true
    },
    'Weather-Log-Enr-Pos': {
        source: '_computed.enroutePosition',
        enabled: true
    },

    // ========================================
    // ROUTE / FLUGPLAN
    // ========================================
    'Route-of-flight': {
        source: 'General.Route',
        sourceAlt: ['General.route', 'Atc.Route', 'Atc.route'],
        enabled: true
    },
    'Cruise-Alt': {
        source: 'General.Initial_altitude',
        sourceAlt: ['General.initial_altitude', 'General.Initial_alt', 'General.initial_alt', 'General.Avg_altitude', 'General.avg_altitude'],
        parse: function(value) {
            console.log('[OFP DEBUG] Cruise-Alt raw value:', value, 'type:', typeof value);
            var alt = parseInt(value);
            if (isNaN(alt)) return null;
            // SimBrief liefert Initial_altitude in feet (z.B. 17000, 35000)
            // Durch 100 teilen für FL, dann 3-stellig formatieren
            var fl = Math.round(alt / 100);
            var flStr = String(fl);
            while (flStr.length < 3) flStr = '0' + flStr;
            console.log('[OFP DEBUG] Cruise-Alt result: FL' + flStr);
            return 'FL' + flStr;
        },
        enabled: true
    },
    'Destination-Airport': {
        source: 'Destination.Icao_code',
        sourceAlt: ['Destination.icao_code'],
        parse: function(value, ofp) {
            var name = getNestedValue(ofp, 'Destination.Name') ||
                       getNestedValue(ofp, 'Destination.name') || '';
            // 3-zeiliges Format: ICAO, -, Name
            return name ? value + '\n-\n' + name : value;
        },
        enabled: true
    },
    'Notes': {
        source: 'Text.Briefing',
        sourceAlt: ['Text.briefing', 'General.Notes', 'General.notes'],
        enabled: true
    },
    'Notam': {
        source: 'General.Notams',
        sourceAlt: ['General.notams', 'Text.Notams', 'Text.notams', 'General.Notam', 'General.notam'],
        enabled: true
    },

    // ========================================
    // PILOT-DATEN (aus Simbrief OFP)
    // ========================================
    'Pilot_in_com': {
        source: 'Crew.Cpt',
        sourceAlt: [
            'Crew.cpt',
            'Crew.Pic',
            'Crew.pic',
            'Crew.Captain',
            'Crew.captain',
            'Params.Pilot',
            'Params.pilot',
            'General.Pilot',
            'General.pilot',
            'User.Name',
            'User.name'
        ],
        enabled: true
    },

    // ========================================
    // NICHT AUTOMATISCH GEFÜLLT (Pilot trägt manuell ein)
    // ========================================
    'Departure-TC': { enabled: false },
    'Departure-Wind': { enabled: false },
    'Arrival-Wind': { enabled: false },
    'Departure-Ceil': { enabled: false },
    'Arrival-Ceil': { enabled: false },
    'Departure-Alt': { enabled: false },
    'Arrival-Alt': { enabled: false },
    'Destination-Remarks': {
        // Kombiniert Transition Altitude und Level für den Anflug
        source: 'Destination.Trans_alt',
        sourceAlt: ['Destination.trans_alt'],
        parse: function(value, ofp) {
            var parts = [];

            // Transition Altitude (als Zahl ohne führende Nullen)
            var transAlt = parseInt(value);
            if (!isNaN(transAlt) && transAlt > 0) {
                parts.push('TA: ' + transAlt + ' ft');
            }

            // Transition Level - SimBrief liefert in Feet (z.B. 6000 für FL060)
            var destData = ofp.Destination || ofp.destination;
            if (destData) {
                var transLevelRaw = parseInt(destData.Trans_level || destData.trans_level);
                if (!isNaN(transLevelRaw) && transLevelRaw > 0) {
                    // Durch 100 teilen um FL zu bekommen, dann 3-stellig formatieren
                    var fl = Math.round(transLevelRaw / 100);
                    var flStr = String(fl);
                    while (flStr.length < 3) flStr = '0' + flStr;
                    parts.push('TL: FL' + flStr);
                }
            }

            // Wenn nichts vorhanden, leer lassen
            if (parts.length === 0) return null;

            return parts.join(' / ');
        },
        enabled: true
    }
};


/**
 * Berechnete Felder die aus mehreren OFP-Werten abgeleitet werden
 *
 * Jede Funktion erhält (ofp, waypoints) und gibt den berechneten Wert zurück
 */
var OFP_COMPUTED_FIELDS = {

    // Departure Point = Ende der SID / erster Enroute Fix (Punkt wo man CTR verlässt)
    departurePoint: function(ofp) {
        var fixes = getNavlogFixes(ofp);
        if (!fixes) return null;

        var departurePoint = null;
        for (var i = 0; i < fixes.length; i++) {
            var fix = fixes[i];
            var type = (fix.Type || fix.type || '').toLowerCase();
            var ident = fix.Ident || fix.ident || fix.Name || fix.name;

            // SID-Fixes sammeln, der letzte ist der Departure Point
            if (type === 'sid' || fix.Is_sid_star === '1') {
                departurePoint = ident;
            } else if (!departurePoint && type !== 'apt' && ident) {
                // Falls keine SID, nimm ersten Nicht-Airport Fix
                departurePoint = ident;
                break;
            }
        }
        return departurePoint;
    },

    // SID aus Navlog Fixes extrahieren
    // Simbrief: Is_sid_star = "1" für SID, "2" für STAR
    // Der SID-Name steht im Via_airway Feld des ersten SID-Fixes
    sid: function(ofp) {
        var fixes = getNavlogFixes(ofp);
        if (!fixes) return null;

        // Suche von vorne nach hinten nach dem ersten Fix mit Is_sid_star === "1"
        for (var i = 0; i < fixes.length; i++) {
            var fix = fixes[i];
            var isSidFix = fix.Is_sid_star === '1' || fix.is_sid_star === '1' ||
                          fix.Type === 'sid' || fix.type === 'sid';

            if (isSidFix) {
                // SID-Name aus Via_airway holen (dort steht z.B. "ABTA4B")
                var via = fix.Via_airway || fix.via_airway || fix.Via || fix.via;
                if (via) {
                    console.log('[OFP Computed] Found SID:', via, 'at fix:', fix.Ident || fix.ident);
                    return via;
                }
            }
        }
        return null;
    },

    // STAR aus Navlog Fixes extrahieren
    // Simbrief: Is_sid_star = "1" bedeutet "gehört zu SID oder STAR Prozedur"
    // Am ENDE der Route mit Is_sid_star = "1" ist es die STAR
    // Der STAR-Name steht im Via_airway Feld (z.B. "ROKI1B")
    star: function(ofp) {
        var fixes = getNavlogFixes(ofp);
        if (!fixes || fixes.length < 2) return null;

        // Strategie: Vom Ende suchen nach Fixes mit Is_sid_star === "1"
        // Diese gehören zur STAR. Der Via_airway enthält den STAR-Namen.
        var starName = null;

        // Vom Ende her suchen (letzter Fix ist meist der Destination Airport)
        for (var i = fixes.length - 1; i >= 0; i--) {
            var fix = fixes[i];
            var isSidStar = fix.Is_sid_star === '1' || fix.is_sid_star === '1';
            var via = fix.Via_airway || fix.via_airway || fix.Via || fix.via;

            // Wenn Is_sid_star === "1" und Via_airway vorhanden
            // und es ist NICHT die SID (die SID steht am Anfang)
            if (isSidStar && via) {
                // Prüfe ob es eine SID ist (SID endet typischerweise auf Zahl+Buchstabe wie "4B")
                // oder STAR (STAR endet typischerweise auf Zahl+Buchstabe wie "1B")
                // Wir nehmen die Via_airway vom letzten Is_sid_star Fix
                starName = via;
                console.log('[OFP Computed] Found potential STAR:', via, 'at fix:', fix.Ident || fix.ident);
                // Weiter suchen um die erste STAR-Fix zu finden (nicht weitermachen)
            }

            // Wenn wir auf einen Fix ohne Is_sid_star stoßen, sind wir aus der STAR raus
            if (!isSidStar && starName) {
                break;
            }
        }

        // Wenn gefunden, prüfen ob es nicht die SID ist
        if (starName) {
            // SID-Name holen zum Vergleich
            var sidName = null;
            for (var s = 0; s < Math.min(5, fixes.length); s++) {
                var sidFix = fixes[s];
                if (sidFix.Is_sid_star === '1' || sidFix.is_sid_star === '1') {
                    sidName = sidFix.Via_airway || sidFix.via_airway;
                    break;
                }
            }

            // Wenn STAR-Name gleich SID-Name, dann keine separate STAR
            if (sidName && starName === sidName) {
                console.log('[OFP Computed] STAR same as SID, likely no STAR in flight plan');
                return null;
            }

            console.log('[OFP Computed] Final STAR:', starName);
            return starName;
        }

        console.log('[OFP Computed] No STAR found');
        return null;
    },

    // Fuel Endurance Stunden berechnen
    enduranceHours: function(ofp) {
        var fuel = ofp.Fuel || ofp.fuel;
        if (!fuel) return null;

        var planRamp = parseFloat(fuel.Plan_ramp || fuel.plan_ramp);
        var avgFlow = parseFloat(fuel.Avg_fuel_flow || fuel.avg_fuel_flow);

        if (!planRamp || !avgFlow || avgFlow <= 0) return null;
        return Math.floor(planRamp / avgFlow);
    },

    // Fuel Endurance Minuten berechnen
    enduranceMinutes: function(ofp) {
        var fuel = ofp.Fuel || ofp.fuel;
        if (!fuel) return null;

        var planRamp = parseFloat(fuel.Plan_ramp || fuel.plan_ramp);
        var avgFlow = parseFloat(fuel.Avg_fuel_flow || fuel.avg_fuel_flow);

        if (!planRamp || !avgFlow || avgFlow <= 0) return null;
        var hours = planRamp / avgFlow;
        return Math.round((hours - Math.floor(hours)) * 60);
    },

    // Enroute Winds - Durchschnittswert aus allen Enroute-Fixes
    enrouteWinds: function(ofp, waypoints) {
        var fixes = getNavlogFixes(ofp);
        if (!fixes || fixes.length < 3) return null;

        var depIcao = getNestedValue(ofp, 'Origin.Icao_code') || getNestedValue(ofp, 'Origin.icao_code') || '';
        var arrIcao = getNestedValue(ofp, 'Destination.Icao_code') || getNestedValue(ofp, 'Destination.icao_code') || '';

        var totalDir = 0;
        var totalSpd = 0;
        var count = 0;

        for (var i = 0; i < fixes.length; i++) {
            var fix = fixes[i];
            var ident = fix.Ident || fix.ident || fix.Name || fix.name || '';
            var type = (fix.Type || fix.type || '').toLowerCase();

            // Überspringe SID/STAR Fixes und Airports
            if (fix.Is_sid_star === '1' || fix.is_sid_star === '1') continue;
            if (type === 'apt' || ident === depIcao || ident === arrIcao) continue;

            var dir = parseFloat(fix.Wind_dir || fix.wind_dir);
            var spd = parseFloat(fix.Wind_spd || fix.wind_spd);

            if (!isNaN(dir) && !isNaN(spd)) {
                totalDir += dir;
                totalSpd += spd;
                count++;
            }
        }

        if (count === 0) return null;
        var avgDir = Math.round(totalDir / count);
        var avgSpd = Math.round(totalSpd / count);
        return avgDir + '\u00B0/' + avgSpd + 'kt';
    },

    // Enroute Reported - Ceiling / Visibility / Precipitation aus METAR-Daten
    // Format: "BKN045 / 10SM / -RA" oder "CAVOK" wenn keine signifikanten Wetter
    enrouteReported: function(ofp, waypoints) {
        // Versuche Departure METAR zu nutzen als Basis für Enroute
        var originMetar = getNestedValue(ofp, 'Origin.Metar') || getNestedValue(ofp, 'Origin.metar');
        var destMetar = getNestedValue(ofp, 'Destination.Metar') || getNestedValue(ofp, 'Destination.metar');

        // Nehme den schlechteren Wert von Departure/Destination als Enroute-Schätzung
        var result = [];
        var ceilingValue = null;
        var visValue = null;
        var wxValue = null;

        // Parse beide METARs und nehme den schlechteren Wert
        var metars = [originMetar, destMetar].filter(function(m) { return m; });

        for (var m = 0; m < metars.length; m++) {
            var metar = metars[m];
            if (!metar) continue;

            // Ceiling: BKN, OVC, VV mit Höhe (nur diese zählen als Ceiling)
            var cloudRegex = /(BKN|OVC|VV)(\d{3})(CB|TCU)?/g;
            var cloudMatch;
            while ((cloudMatch = cloudRegex.exec(metar)) !== null) {
                var type = cloudMatch[1];
                var height = parseInt(cloudMatch[2]) * 100;
                var suffix = cloudMatch[3] || '';
                var ceilingStr = type + cloudMatch[2] + suffix;

                // Nimm den niedrigsten (schlechtesten) Ceiling
                if (ceilingValue === null || height < parseInt(ceilingValue.match(/\d+/)[0]) * 100) {
                    ceilingValue = ceilingStr;
                }
            }

            // Visibility: Suche nach SM oder Meter-Angaben
            var visMatchSM = metar.match(/\b(\d+)\s*SM\b/);
            var visMatchM = metar.match(/\s(\d{4})\s/);
            if (visMatchSM) {
                var visSM = parseInt(visMatchSM[1]);
                if (visValue === null || visSM < visValue) {
                    visValue = visSM;
                }
            } else if (visMatchM && visMatchM[1] !== '9999') {
                var visM = parseInt(visMatchM[1]);
                var visSMConverted = Math.round(visM / 1609); // Meter to SM
                if (visValue === null || visSMConverted < visValue) {
                    visValue = visSMConverted;
                }
            }

            // Precipitation: RA, SN, DZ, PL, etc.
            var wxMatch = metar.match(/\s([+-]?)(RA|SN|DZ|PL|GR|GS|SG|IC|PE|UP|FG|BR|HZ|FU|SA|DU|SQ|FC|TS|SH)/g);
            if (wxMatch && wxMatch.length > 0) {
                // Nimm den ersten/signifikantesten Niederschlag
                wxValue = wxMatch[0].trim();
            }
        }

        // Zusammenbauen: Ceiling / Visibility / Precipitation
        if (ceilingValue) {
            result.push(ceilingValue);
        }
        if (visValue !== null) {
            if (visValue >= 10) {
                result.push('>10SM');
            } else {
                result.push(visValue + 'SM');
            }
        }
        if (wxValue) {
            result.push(wxValue);
        }

        if (result.length === 0) {
            return 'CAVOK';
        }

        return result.join(' / ');
    },

    // Enroute Forecast - Ceiling / Visibility / Precipitation aus TAF-Daten
    // Format: "BKN050 / 6SM / -SHRA" oder "CAVOK"
    enrouteForecast: function(ofp, waypoints) {
        // Versuche TAF von Departure und Destination
        var originTaf = getNestedValue(ofp, 'Origin.Taf') || getNestedValue(ofp, 'Origin.taf');
        var destTaf = getNestedValue(ofp, 'Destination.Taf') || getNestedValue(ofp, 'Destination.taf');

        var result = [];
        var ceilingValue = null;
        var visValue = null;
        var wxValue = null;

        // Parse beide TAFs und nehme den schlechteren Wert
        var tafs = [originTaf, destTaf].filter(function(t) { return t; });

        for (var t = 0; t < tafs.length; t++) {
            var taf = tafs[t];
            if (!taf) continue;

            // Ceiling: BKN, OVC, VV mit Höhe
            var cloudRegex = /(BKN|OVC|VV)(\d{3})(CB|TCU)?/g;
            var cloudMatch;
            while ((cloudMatch = cloudRegex.exec(taf)) !== null) {
                var type = cloudMatch[1];
                var height = parseInt(cloudMatch[2]) * 100;
                var suffix = cloudMatch[3] || '';
                var ceilingStr = type + cloudMatch[2] + suffix;

                if (ceilingValue === null || height < parseInt(ceilingValue.match(/\d+/)[0]) * 100) {
                    ceilingValue = ceilingStr;
                }
            }

            // Visibility
            var visMatchSM = taf.match(/\b(\d+)\s*SM\b/);
            var visMatchM = taf.match(/\s(\d{4})\s/);
            if (visMatchSM) {
                var visSM = parseInt(visMatchSM[1]);
                if (visValue === null || visSM < visValue) {
                    visValue = visSM;
                }
            } else if (visMatchM && visMatchM[1] !== '9999') {
                var visM = parseInt(visMatchM[1]);
                var visSMConverted = Math.round(visM / 1609);
                if (visValue === null || visSMConverted < visValue) {
                    visValue = visSMConverted;
                }
            }

            // Precipitation
            var wxMatch = taf.match(/\s([+-]?)(RA|SN|DZ|PL|GR|GS|SG|IC|PE|UP|FG|BR|HZ|FU|SA|DU|SQ|FC|TS|SH)/g);
            if (wxMatch && wxMatch.length > 0) {
                wxValue = wxMatch[0].trim();
            }
        }

        // Zusammenbauen
        if (ceilingValue) {
            result.push(ceilingValue);
        }
        if (visValue !== null) {
            if (visValue >= 10) {
                result.push('>10SM');
            } else {
                result.push(visValue + 'SM');
            }
        }
        if (wxValue) {
            result.push(wxValue);
        }

        if (result.length === 0) {
            return 'CAVOK';
        }

        return result.join(' / ');
    },

    // Enroute Icing - Severity + Freezing Level
    // Format: "MOD FZLVL 80" (Icing Severity + Freezing Level in 100s ft)
    enrouteIcing: function(ofp, waypoints) {
        var fixes = getNavlogFixes(ofp);

        var tempSum = 0;
        var tempCount = 0;
        var freezingLevel = null;
        var minAlt = 99999;
        var maxAlt = 0;

        if (fixes && fixes.length > 2) {
            for (var i = 1; i < fixes.length - 1; i++) {
                var fix = fixes[i];
                if (fix.Is_sid_star === '1' || fix.is_sid_star === '1') continue;

                var oat = fix.Oat || fix.oat;
                var alt = fix.Altitude_feet || fix.altitude_feet;

                if (oat !== undefined) {
                    var temp = parseFloat(oat);
                    if (!isNaN(temp)) {
                        tempSum += temp;
                        tempCount++;

                        // Freezing Level finden (wo OAT nahe 0°C)
                        if (alt && Math.abs(temp) <= 3) {
                            var altNum = parseFloat(alt);
                            if (!isNaN(altNum) && (freezingLevel === null || Math.abs(temp) < Math.abs(freezingLevel - 0))) {
                                freezingLevel = altNum;
                            }
                        }
                    }
                }

                if (alt) {
                    var a = parseFloat(alt);
                    if (!isNaN(a)) {
                        if (a < minAlt) minAlt = a;
                        if (a > maxAlt) maxAlt = a;
                    }
                }
            }
        }

        if (tempCount === 0) return 'NIL';

        var avgTemp = Math.round(tempSum / tempCount);

        // Severity basierend auf Durchschnittstemperatur (ICAO: NIL, LGT, MOD, SEV)
        var severity = 'NIL';
        if (avgTemp >= -20 && avgTemp <= 2) {
            if (avgTemp <= -15) severity = 'LGT';
            else if (avgTemp <= -5) severity = 'MOD';
            else if (avgTemp <= 2) severity = 'LGT';
        }

        // Freezing Level hinzufügen
        if (freezingLevel !== null) {
            var fzlvl = Math.round(freezingLevel / 100);
            return severity + ' FZLVL ' + fzlvl;
        }

        return severity;
    },

    // Enroute Turbulence & Cloud Tops - aus Shear und Tropopause_feet
    // Format: "NIL FL398" (Turbulenz + Cloud Tops in einer Zeile)
    enrouteTurbulence: function(ofp, waypoints) {
        var fixes = getNavlogFixes(ofp);
        var depIcao = getNestedValue(ofp, 'Origin.Icao_code') || getNestedValue(ofp, 'Origin.icao_code') || '';
        var arrIcao = getNestedValue(ofp, 'Destination.Icao_code') || getNestedValue(ofp, 'Destination.icao_code') || '';

        var maxShear = 0;
        var minTropopause = 99999;

        if (fixes && fixes.length > 2) {
            for (var i = 0; i < fixes.length; i++) {
                var fix = fixes[i];
                var ident = fix.Ident || fix.ident || fix.Name || fix.name || '';
                var type = (fix.Type || fix.type || '').toLowerCase();

                if (fix.Is_sid_star === '1' || fix.is_sid_star === '1') continue;
                if (type === 'apt' || ident === depIcao || ident === arrIcao) continue;

                // Shear-Wert (Windscherung) - höher = mehr Turbulenz
                var shear = parseFloat(fix.Shear || fix.shear);
                if (!isNaN(shear) && shear > maxShear) {
                    maxShear = shear;
                }

                // Tropopause/Cloud Tops - niedrigste Höhe
                var tropo = parseFloat(fix.Tropopause_feet || fix.tropopause_feet);
                if (!isNaN(tropo) && tropo < minTropopause) {
                    minTropopause = tropo;
                }
            }
        }

        // Turbulenz: CAT basierend auf Shear-Wert
        var turb = 'NIL';
        if (maxShear >= 8) turb = 'SEV CAT';
        else if (maxShear >= 5) turb = 'MOD CAT';
        else if (maxShear >= 2) turb = 'LGT CAT';

        // Cloud Tops hinzufügen
        if (minTropopause < 99999) {
            var flLevel = Math.round(minTropopause / 100);
            return turb + ' FL' + flLevel;
        }
        return turb;
    },

    // Enroute Position - Fronten/Hoch/Tief
    // Diese Information kommt aus Wetterkarten (SIGWx), nicht aus OFP-Daten
    // Feld bleibt leer für manuelle Eingabe durch den Piloten
    enroutePosition: function() {
        return '-';
    }
};


/**
 * Parser-Funktionen für METAR/TAF Daten
 *
 * Diese können in der Mapping-Konfiguration als String referenziert werden:
 * parse: 'parseMetarReported'
 */
var OFP_PARSERS = {

    // METAR Reported: Ceiling / Visibility / Precipitation Format
    // Format: "BKN045 / 10SM / -RA" oder "CAVOK"
    parseMetarReported: function(metar) {
        if (!metar || typeof metar !== 'string') return null;

        var result = [];
        var ceilingValue = null;
        var visValue = null;
        var wxValue = null;

        // Ceiling: Nur BKN, OVC, VV zählen als Ceiling (NICHT FEW, SCT!)
        var cloudRegex = /(BKN|OVC|VV)(\d{3})(CB|TCU)?/g;
        var cloudMatch;
        while ((cloudMatch = cloudRegex.exec(metar)) !== null) {
            var height = parseInt(cloudMatch[2]) * 100;
            var ceilingStr = cloudMatch[1] + cloudMatch[2] + (cloudMatch[3] || '');

            // Niedrigsten (schlechtesten) Ceiling nehmen
            if (ceilingValue === null || height < parseInt(ceilingValue.match(/\d+/)[0]) * 100) {
                ceilingValue = ceilingStr;
            }
        }

        // Visibility: SM oder Meter
        var visMatchSM = metar.match(/\b(\d+)\s*SM\b/);
        var visMatchM = metar.match(/\s(\d{4})\s/);
        if (visMatchSM) {
            visValue = parseInt(visMatchSM[1]);
        } else if (visMatchM && visMatchM[1] !== '9999') {
            visValue = Math.round(parseInt(visMatchM[1]) / 1609); // Meter to SM
        } else if (visMatchM && visMatchM[1] === '9999') {
            visValue = 10; // CAVOK equivalent
        }

        // Precipitation: RA, SN, DZ, etc.
        var wxMatch = metar.match(/\s([+-]?)(RA|SN|DZ|PL|GR|GS|SG|IC|PE|UP|FG|BR|HZ|TS|SH|FZRA|FZDZ)/);
        if (wxMatch) {
            wxValue = wxMatch[0].trim();
        }

        // Zusammenbauen: Ceiling / Visibility / Precipitation
        if (ceilingValue) {
            result.push(ceilingValue);
        }
        if (visValue !== null) {
            if (visValue >= 10) {
                result.push('>10SM');
            } else {
                result.push(visValue + 'SM');
            }
        }
        if (wxValue) {
            result.push(wxValue);
        }

        if (result.length === 0) {
            return 'CAVOK';
        }

        return result.join(' / ');
    },

    // TAF Forecast: Ceiling / Visibility / Precipitation Format
    // Format: "BKN050 / 6SM / -SHRA" oder "CAVOK"
    parseTafForecast: function(taf) {
        if (!taf || typeof taf !== 'string') return null;

        var result = [];
        var ceilingValue = null;
        var visValue = null;
        var wxValue = null;

        // Ceiling: Nur BKN, OVC, VV zählen als Ceiling (NICHT FEW, SCT!)
        var cloudRegex = /(BKN|OVC|VV)(\d{3})(CB|TCU)?/g;
        var cloudMatch;
        while ((cloudMatch = cloudRegex.exec(taf)) !== null) {
            var height = parseInt(cloudMatch[2]) * 100;
            var ceilingStr = cloudMatch[1] + cloudMatch[2] + (cloudMatch[3] || '');

            // Niedrigsten (schlechtesten) Ceiling nehmen
            if (ceilingValue === null || height < parseInt(ceilingValue.match(/\d+/)[0]) * 100) {
                ceilingValue = ceilingStr;
            }
        }

        // Visibility: SM oder Meter
        var visMatchSM = taf.match(/\b(\d+)\s*SM\b/);
        var visMatchM = taf.match(/\s(\d{4})\s/);
        if (visMatchSM) {
            visValue = parseInt(visMatchSM[1]);
        } else if (visMatchM && visMatchM[1] !== '9999') {
            visValue = Math.round(parseInt(visMatchM[1]) / 1609); // Meter to SM
        } else if (visMatchM && visMatchM[1] === '9999') {
            visValue = 10; // CAVOK equivalent
        }

        // Precipitation: RA, SN, DZ, etc.
        var wxMatch = taf.match(/\s([+-]?)(RA|SN|DZ|PL|GR|GS|SG|IC|PE|UP|FG|BR|HZ|TS|SH|FZRA|FZDZ)/);
        if (wxMatch) {
            wxValue = wxMatch[0].trim();
        }

        // Zusammenbauen: Ceiling / Visibility / Precipitation
        if (ceilingValue) {
            result.push(ceilingValue);
        }
        if (visValue !== null) {
            if (visValue >= 10) {
                result.push('>10SM');
            } else {
                result.push(visValue + 'SM');
            }
        }
        if (wxValue) {
            result.push(wxValue);
        }

        if (result.length === 0) {
            return 'CAVOK';
        }

        return result.join(' / ');
    },

    // Wind aus METAR extrahieren
    parseMetarWind: function(metar) {
        var wx = parseMetarForWeatherLog(metar);
        return wx ? wx.wind : null;
    },

    // Icing-Info aus METAR extrahieren
    parseMetarIcing: function(metar) {
        var wx = parseMetarForWeatherLog(metar);
        return wx ? wx.icing : null;
    },

    // Turbulenz-Info aus METAR extrahieren
    parseMetarTurbulence: function(metar) {
        var wx = parseMetarForWeatherLog(metar);
        return wx ? wx.turbulence : null;
    },

    // Position of fronts, Low and Highs aus METAR
    // Diese Information kommt normalerweise aus Wetterkarten, nicht aus METAR/TAF
    // Feld bleibt leer für manuelle Eingabe durch den Piloten
    parseMetarPosition: function() {
        return '-';
    },

    // Datum aus Unix-Timestamp formatieren (DD-MMM-YYYY Format, luftfahrtüblich)
    parseDateFromTimestamp: function(value) {
        if (!value) return null;
        var date = new Date(parseInt(value) * 1000);

        // Tag (2-stellig mit führender 0)
        var day = ('0' + date.getUTCDate()).slice(-2);

        // Monat (3 Buchstaben, Großbuchstaben)
        var months = ['JAN', 'FEB', 'MAR', 'APR', 'MAY', 'JUN',
                      'JUL', 'AUG', 'SEP', 'OCT', 'NOV', 'DEC'];
        var month = months[date.getUTCMonth()];

        // Jahr (4-stellig)
        var year = date.getUTCFullYear();

        return day + '-' + month + '-' + year;
    }
};
