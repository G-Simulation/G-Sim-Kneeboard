using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using CoordinateSharp;
using CoordinateSharp.Magnetic;

namespace Kneeboard_Server.Navigraph
{
    /// <summary>
    /// Navigraph Database Service for querying DFD v2 SQLite database
    /// </summary>
    public class NavigraphDbService : IDisposable
    {
        private SQLiteConnection _connection;
        private readonly string _databasePath;
        private bool _disposed;

        public bool IsConnected => _connection != null && _connection.State == System.Data.ConnectionState.Open;

        public NavigraphDbService(string databasePath)
        {
            _databasePath = databasePath;
            Connect();
        }

        /// <summary>
        /// Connect to the SQLite database
        /// </summary>
        private void Connect()
        {
            try
            {
                if (!File.Exists(_databasePath))
                {
                    Console.WriteLine($"Navigraph DB: Datei nicht gefunden: {_databasePath}");
                    return;
                }

                var connectionString = $"Data Source={_databasePath};Version=3;Read Only=True;";
                _connection = new SQLiteConnection(connectionString);
                _connection.Open();

                Console.WriteLine($"Navigraph DB: Verbunden mit {Path.GetFileName(_databasePath)}");

                // Debug: Log table schemas for SID/STAR/IAP tables
                LogTableSchema("tbl_pd_sids");
                LogTableSchema("tbl_pe_stars");
                LogTableSchema("tbl_pf_iaps");
                LogTableSchema("tbl_pg_runways");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Navigraph DB: Verbindungsfehler: {ex.Message}");
            }
        }

        /// <summary>
        /// Debug helper: Log table schema to identify correct column names
        /// </summary>
        private void LogTableSchema(string tableName)
        {
            try
            {
                using (var cmd = new SQLiteCommand(_connection))
                {
                    cmd.CommandText = $"PRAGMA table_info({tableName})";
                    Console.WriteLine($"[Navigraph DB] Schema for {tableName}:");
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            Console.WriteLine($"  - {reader["name"]}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Navigraph DB] Error reading schema for {tableName}: {ex.Message}");
            }
        }

        #region Airport Queries

        /// <summary>
        /// Get airport information by ICAO code
        /// </summary>
        public AirportInfo GetAirport(string icao)
        {
            if (!IsConnected) return null;

            try
            {
                using (var cmd = new SQLiteCommand(_connection))
                {
                    cmd.CommandText = @"
                        SELECT
                            airport_identifier,
                            airport_ref_latitude,
                            airport_ref_longitude,
                            airport_name,
                            ata_iata_code,
                            elevation,
                            transition_altitude,
                            transition_level,
                            speed_limit,
                            speed_limit_altitude,
                            ifr_capability
                        FROM tbl_pa_airports
                        WHERE airport_identifier = @icao
                        LIMIT 1";

                    cmd.Parameters.AddWithValue("@icao", icao.ToUpperInvariant());

                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            var airport = new AirportInfo
                            {
                                Icao = reader.GetString(0),
                                Latitude = reader.GetDouble(1),
                                Longitude = reader.GetDouble(2),
                                Name = reader.IsDBNull(3) ? "" : reader.GetString(3),
                                Iata = reader.IsDBNull(4) ? "" : reader.GetString(4),
                                Elevation = reader.IsDBNull(5) ? 0 : reader.GetInt32(5),
                                TransitionAltitude = reader.IsDBNull(6) ? 0 : reader.GetInt32(6),
                                TransitionLevel = reader.IsDBNull(7) ? 0 : reader.GetInt32(7),
                                SpeedLimit = reader.IsDBNull(8) ? "" : reader.GetValue(8).ToString(),
                                SpeedLimitAltitude = reader.IsDBNull(9) ? 0 : reader.GetInt32(9),
                                IfrCapability = reader.IsDBNull(10) ? "" : reader.GetString(10)
                            };

                            // Load runways
                            airport.Runways = GetRunways(icao);
                            airport.Frequencies = GetFrequencies(icao);

                            return airport;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Navigraph DB: Airport Query Fehler: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Get magnetic declination at a position using World Magnetic Model (WMM)
        /// Like Little Navmap does for accurate runway heading calculation
        /// </summary>
        public static double GetMagneticDeclination(double latitude, double longitude)
        {
            try
            {
                // Coordinate(lat, lon, date) - standard geographic order
                var coord = new Coordinate(latitude, longitude, DateTime.UtcNow);
                // Create Magnetic object with WMM2020 model
                var magnetic = new Magnetic(coord, DataModel.WMM2020);
                // Get declination from magnetic field elements
                var declination = magnetic.MagneticFieldElements.Declination;
                Console.WriteLine($"[WMM] Lat={latitude:F4}, Lon={longitude:F4} => Declination={declination:F2}°");
                return declination;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WMM] Error calculating declination: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Get all runways for an airport
        /// </summary>
        public List<RunwayInfo> GetRunways(string icao)
        {
            var runways = new List<RunwayInfo>();
            if (!IsConnected) return runways;

            try
            {
                using (var cmd = new SQLiteCommand(_connection))
                {
                    cmd.CommandText = @"
                        SELECT
                            runway_identifier,
                            runway_latitude,
                            runway_longitude,
                            runway_magnetic_bearing,
                            runway_length,
                            runway_width,
                            landing_threshold_elevation,
                            runway_gradient,
                            displaced_threshold_distance,
                            llz_identifier,
                            runway_true_bearing
                        FROM tbl_pg_runways
                        WHERE UPPER(TRIM(airport_identifier)) = @icao
                        ORDER BY runway_identifier";

                    cmd.Parameters.AddWithValue("@icao", icao.ToUpperInvariant());

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var identifier = reader.IsDBNull(0) ? "" : reader.GetString(0).Trim();
                            var thresholdLat = reader.IsDBNull(1) ? 0 : reader.GetDouble(1);
                            var thresholdLon = reader.IsDBNull(2) ? 0 : reader.GetDouble(2);
                            var magHeading = reader.IsDBNull(3) ? 0 : reader.GetDouble(3);
                            var trueHeadingFromDb = reader.IsDBNull(10) ? (double?)null : reader.GetDouble(10);

                            // Calculate TRUE heading using WMM if not in database
                            double trueHeading;
                            if (trueHeadingFromDb.HasValue && trueHeadingFromDb.Value > 0)
                            {
                                // TRUE bearing available from database
                                trueHeading = trueHeadingFromDb.Value;
                                Console.WriteLine($"[Runway] {identifier}: Using TRUE bearing from DB: {trueHeading:F1}°");
                            }
                            else
                            {
                                // Calculate TRUE from MAGNETIC + Declination (WMM)
                                var declination = GetMagneticDeclination(thresholdLat, thresholdLon);
                                // True = Magnetic - Declination (East declination is positive)
                                trueHeading = magHeading - declination;
                                // Normalize to 0-360
                                if (trueHeading < 0) trueHeading += 360;
                                if (trueHeading >= 360) trueHeading -= 360;
                                Console.WriteLine($"[Runway] {identifier}: Calculated TRUE: {magHeading:F1}° - {declination:F1}° = {trueHeading:F1}°");
                            }

                            var runway = new RunwayInfo
                            {
                                Identifier = identifier,
                                ThresholdLat = thresholdLat,
                                ThresholdLon = thresholdLon,
                                Heading = magHeading,
                                TrueHeading = trueHeading,
                                Length = reader.IsDBNull(4) ? 0 : (int)reader.GetDouble(4),
                                Width = reader.IsDBNull(5) ? 0 : reader.GetInt32(5),
                                ThresholdElevation = reader.IsDBNull(6) ? 0 : reader.GetInt32(6),
                                ThresholdDisplacement = reader.IsDBNull(8) ? 0 : reader.GetInt32(8)
                            };

                            // Calculate runway end coordinates using TRUE heading
                            runway.CalculateEndCoordinates();

                            runways.Add(runway);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Navigraph DB: Runway Query Fehler: {ex.Message}");
            }

            return runways;
        }

        /// <summary>
        /// Get a specific runway
        /// </summary>
        public RunwayInfo GetRunway(string icao, string runwayId)
        {
            if (!IsConnected)
            {
                Console.WriteLine($"[Runway Debug] Not connected to database");
                return null;
            }

            try
            {
                var normalizedId = (runwayId ?? "").Trim().ToUpperInvariant();
                if (!normalizedId.StartsWith("RW"))
                {
                    normalizedId = "RW" + normalizedId;
                }

                Console.WriteLine($"[Runway Debug] GetRunway({icao}, {runwayId}) - normalized: {normalizedId}");

                var runways = GetRunways(icao);
                Console.WriteLine($"[Runway Debug] Found {runways.Count} runways at {icao}");
                foreach (var rwy in runways)
                {
                    Console.WriteLine($"[Runway Debug]   - Runway: {rwy.Identifier}");
                }

                var trimmedRunwayId = (runwayId ?? "").Trim();

                foreach (var rwy in runways)
                {
                    var identifier = (rwy.Identifier ?? "").Trim();
                    if (identifier.Equals(normalizedId, StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine($"[Runway Debug] Found exact match: {identifier}");
                        return rwy;
                    }
                    if (identifier.Equals(trimmedRunwayId, StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine($"[Runway Debug] Found match (original format): {identifier}");
                        return rwy;
                    }
                }

                var idWithoutRW = normalizedId.Replace("RW", "").Trim();
                Console.WriteLine($"[Runway Debug] Trying partial match without RW: {idWithoutRW}");
                foreach (var rwy in runways)
                {
                    var identifier = (rwy.Identifier ?? "").Trim();
                    var rwyIdWithoutRW = identifier.ToUpperInvariant().Replace("RW", "").Trim();
                    if (rwyIdWithoutRW.Equals(idWithoutRW, StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine($"[Runway Debug] Found partial match: {identifier}");
                        return rwy;
                    }
                }

                foreach (var rwy in runways)
                {
                    var identifier = (rwy.Identifier ?? "").Trim();
                    if (!string.IsNullOrEmpty(idWithoutRW) && identifier.ToUpperInvariant().Contains(idWithoutRW))
                    {
                        Console.WriteLine($"[Runway Debug] Found contains match: {identifier}");
                        return rwy;
                    }
                }

                if (runways.Count > 0)
                {
                    Console.WriteLine($"[Runway Debug] No exact match, returning first runway: {runways[0].Identifier}");
                    return runways[0];
                }

                Console.WriteLine($"[Runway Debug] No runway found for {icao} {runwayId}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Runway Debug] Runway Query Error: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Calculate opposite runway ID (e.g., 10L -> 28R, 28R -> 10L)
        /// </summary>
        private string GetOppositeRunwayId(string runwayId)
        {
            // Remove RW prefix if present
            var id = runwayId.Replace("RW", "").Trim();

            // Extract number and designator (L/R/C)
            var numberStr = "";
            var designator = "";
            foreach (var c in id)
            {
                if (char.IsDigit(c))
                    numberStr += c;
                else
                    designator += c;
            }

            if (!int.TryParse(numberStr, out int number))
                return null;

            // Calculate opposite number (add or subtract 18)
            int oppositeNumber = number <= 18 ? number + 18 : number - 18;

            // Flip L<->R, keep C
            string oppositeDesignator;
            if (designator == "L")
                oppositeDesignator = "R";
            else if (designator == "R")
                oppositeDesignator = "L";
            else
                oppositeDesignator = designator;

            return $"RW{oppositeNumber:D2}{oppositeDesignator}";
        }

        /// <summary>
        /// Get runway with end coordinates from opposite runway's threshold
        /// </summary>
        public RunwayInfo GetRunwayWithOppositeEnd(string icao, string runwayId)
        {
            var runway = GetRunway(icao, runwayId);
            if (runway == null) return null;

            // Log all available runways at this airport
            var allRunways = GetRunways(icao);
            Console.WriteLine($"[Runway Debug] All runways at {icao}: {string.Join(", ", allRunways.Select(r => r.Identifier))}");
            Console.WriteLine($"[Runway Debug] Requested runway: {runway.Identifier} at ({runway.ThresholdLat:F6}, {runway.ThresholdLon:F6})");

            // Get opposite runway ID
            var oppositeId = GetOppositeRunwayId(runway.Identifier);
            Console.WriteLine($"[Runway Debug] Calculated opposite runway of {runway.Identifier} is {oppositeId}");

            // ALWAYS calculate end from heading/length for reliability
            runway.CalculateEndCoordinates();
            Console.WriteLine($"[Runway Debug] Calculated end from heading {runway.Heading}° and length {runway.Length}ft: ({runway.EndLat:F6}, {runway.EndLon:F6})");

            return runway;
        }

        /// <summary>
        /// Get ILS/LOC data for an airport
        /// </summary>
        public List<ILSData> GetILS(string icao)
        {
            var ilsList = new List<ILSData>();
            if (!IsConnected) return ilsList;

            try
            {
                using (var cmd = new SQLiteCommand(_connection))
                {
                    cmd.CommandText = @"
                        SELECT
                            llz_identifier,
                            runway_identifier,
                            llz_frequency,
                            llz_bearing,
                            gs_angle,
                            llz_latitude,
                            llz_longitude,
                            ils_mls_gls_category
                        FROM tbl_pi_localizers_glideslopes
                        WHERE airport_identifier = @icao";

                    cmd.Parameters.AddWithValue("@icao", icao.ToUpperInvariant());

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            ilsList.Add(new ILSData
                            {
                                Identifier = reader.IsDBNull(0) ? "" : reader.GetString(0),
                                RunwayIdentifier = reader.IsDBNull(1) ? "" : reader.GetString(1),
                                Frequency = reader.IsDBNull(2) ? 0 : reader.GetDouble(2),
                                LocalizerBearing = reader.IsDBNull(3) ? 0 : reader.GetDouble(3),
                                GlideSlopeAngle = reader.IsDBNull(4) ? 0 : reader.GetDouble(4),
                                Latitude = reader.IsDBNull(5) ? 0 : reader.GetDouble(5),
                                Longitude = reader.IsDBNull(6) ? 0 : reader.GetDouble(6),
                                Category = reader.IsDBNull(7) ? "" : reader.GetString(7)
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Navigraph DB: ILS Query Fehler: {ex.Message}");
            }

            return ilsList;
        }

        /// <summary>
        /// Get frequencies for an airport
        /// </summary>
        public List<FrequencyInfo> GetFrequencies(string icao)
        {
            var frequencies = new List<FrequencyInfo>();
            if (!IsConnected) return frequencies;

            try
            {
                using (var cmd = new SQLiteCommand(_connection))
                {
                    cmd.CommandText = @"
                        SELECT
                            communication_type,
                            communication_frequency,
                            service_indicator
                        FROM tbl_pv_airport_communication
                        WHERE airport_identifier = @icao
                        ORDER BY communication_type";

                    cmd.Parameters.AddWithValue("@icao", icao.ToUpperInvariant());

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            frequencies.Add(new FrequencyInfo
                            {
                                Type = reader.IsDBNull(0) ? "" : reader.GetString(0),
                                Frequency = reader.IsDBNull(1) ? 0 : reader.GetDouble(1),
                                Name = reader.IsDBNull(2) ? "" : reader.GetString(2)
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Navigraph DB: Frequency Query Fehler: {ex.Message}");
            }

            return frequencies;
        }

        #endregion

        #region Procedure Queries (SID/STAR/Approach)

        /// <summary>
        /// Get SID procedures for an airport
        /// </summary>
        public List<ProcedureSummary> GetSIDs(string icao)
        {
            return GetProcedures(icao, "tbl_pd_sids", ProcedureType.SID);
        }

        /// <summary>
        /// Get STAR procedures for an airport
        /// </summary>
        public List<ProcedureSummary> GetSTARs(string icao)
        {
            return GetProcedures(icao, "tbl_pe_stars", ProcedureType.STAR);
        }

        /// <summary>
        /// Get procedures (generic method for SID/STAR)
        /// </summary>
        private List<ProcedureSummary> GetProcedures(string icao, string tableName, ProcedureType type)
        {
            var procedures = new List<ProcedureSummary>();
            if (!IsConnected) return procedures;

            try
            {
                using (var cmd = new SQLiteCommand(_connection))
                {
                    cmd.CommandText = $@"
                        SELECT DISTINCT
                            procedure_identifier,
                            route_type,
                            transition_identifier
                        FROM {tableName}
                        WHERE airport_identifier = @icao
                        ORDER BY procedure_identifier";

                    cmd.Parameters.AddWithValue("@icao", icao.ToUpperInvariant());

                    using (var reader = cmd.ExecuteReader())
                    {
                        int count = 0;
                        while (reader.Read())
                        {
                            var routeType = reader.IsDBNull(1) ? "" : reader.GetString(1);
                            var transitionId = reader.IsDBNull(2) ? "" : reader.GetString(2);

                            // ARINC 424 Route Types:
                            // SID: 1=Runway Transition (conv), 2=Common, 3=Enroute Trans, 4=RNAV Runway Trans, 5=RNAV Common, 6=RNAV Enroute
                            // STAR: 1=Enroute Trans, 2=Common, 3=Runway Trans, 4=RNAV Enroute, 5=RNAV Common, 6=RNAV Runway Trans
                            // For Runway Transitions, transition_identifier IS the runway (e.g., "RW10B")
                            string runway = null;
                            bool isRunwayTransition = (type == ProcedureType.SID && (routeType == "1" || routeType == "4")) ||
                                                      (type == ProcedureType.STAR && (routeType == "3" || routeType == "6"));
                            if (isRunwayTransition && transitionId.StartsWith("RW"))
                            {
                                runway = transitionId;
                            }

                            var proc = new ProcedureSummary
                            {
                                Identifier = reader.GetString(0),
                                RouteType = routeType,
                                TransitionIdentifier = transitionId,
                                Runway = runway,
                                Type = type
                            };
                            procedures.Add(proc);
                            count++;
                            // Debug: Log first 10 entries
                            if (count <= 10)
                            {
                                Console.WriteLine($"[Navigraph DB] {type} Row {count}: ID={proc.Identifier}, RouteType={proc.RouteType}, Transition={proc.TransitionIdentifier}, Runway={proc.Runway}, IsRunwayTransition={isRunwayTransition}");
                            }
                        }
                        Console.WriteLine($"[Navigraph DB] {type} Query returned {count} rows for {icao}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Navigraph DB: Procedure Query Fehler: {ex.Message}");
            }

            return procedures;
        }

        /// <summary>
        /// Get approach procedures for an airport
        /// </summary>
        public List<ApproachSummary> GetApproaches(string icao)
        {
            var approaches = new List<ApproachSummary>();
            if (!IsConnected) return approaches;

            try
            {
                using (var cmd = new SQLiteCommand(_connection))
                {
                    cmd.CommandText = @"
                        SELECT DISTINCT
                            procedure_identifier,
                            route_type,
                            transition_identifier
                        FROM tbl_pf_iaps
                        WHERE airport_identifier = @icao
                        ORDER BY procedure_identifier";

                    cmd.Parameters.AddWithValue("@icao", icao.ToUpperInvariant());

                    using (var reader = cmd.ExecuteReader())
                    {
                        var approachDict = new Dictionary<string, ApproachSummary>();

                        while (reader.Read())
                        {
                            var procId = reader.GetString(0);
                            var routeType = reader.IsDBNull(1) ? "" : reader.GetString(1);
                            var transition = reader.IsDBNull(2) ? "" : reader.GetString(2);

                            // Use only procedure_identifier as key (not route_type)
                            var key = procId;

                            if (!approachDict.ContainsKey(key))
                            {
                                // Parse approach type from identifier (e.g., "I25C" -> ILS RWY 25C)
                                string appType = "RNAV";
                                if (procId.StartsWith("I")) appType = "ILS";
                                else if (procId.StartsWith("L")) appType = "LOC";
                                else if (procId.StartsWith("V")) appType = "VOR";
                                else if (procId.StartsWith("N")) appType = "NDB";
                                else if (procId.StartsWith("R")) appType = "RNAV";
                                else if (procId.StartsWith("D")) appType = "VOR/DME";

                                // Extract runway from procedure identifier (e.g., "I12L" -> "12L", "R30RZ" -> "30R")
                                string runway = "";
                                if (procId.Length > 1)
                                {
                                    // Remove first char (approach type) and trailing suffix (Y, Z, etc.)
                                    runway = procId.Substring(1).TrimEnd('Y', 'Z', 'A', 'B', 'C');
                                }

                                approachDict[key] = new ApproachSummary
                                {
                                    Identifier = procId,
                                    Type = appType,
                                    Runway = runway,
                                    Transitions = new List<string>()
                                };
                            }

                            // Only add transitions from route_type 'A' (Approach Transition legs)
                            if (routeType == "A" && !string.IsNullOrEmpty(transition) &&
                                !approachDict[key].Transitions.Contains(transition))
                            {
                                approachDict[key].Transitions.Add(transition);
                            }
                        }

                        approaches.AddRange(approachDict.Values);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Navigraph DB: Approach Query Fehler: {ex.Message}");
            }

            return approaches;
        }

        /// <summary>
        /// Get procedure legs with route_type filtering for correct path construction
        /// </summary>
        /// <param name="icao">Airport ICAO code</param>
        /// <param name="procedureId">Procedure identifier (e.g., MINNE5, OSMU2A)</param>
        /// <param name="type">Procedure type (SID, STAR, Approach)</param>
        /// <param name="transition">Optional enroute transition identifier (e.g., HISKU)</param>
        /// <param name="runway">Optional runway identifier (e.g., 10L, RW10L)</param>
        /// <returns>List of procedure legs in correct order</returns>
        public List<ProcedureLeg> GetProcedureLegs(string icao, string procedureId, ProcedureType type, string transition = null, string runway = null)
        {
            var legs = new List<ProcedureLeg>();
            if (!IsConnected) return legs;

            string tableName;
            string routeTypeFilter;
            string orderBy;
            string runwayTransition = null;
            string runwayBoth = null;  // For "Both" runways like RW10B

            // Normalize runway to transition format (e.g., "10L" -> "RW10L")
            // Also handle "Both" runways: 10L should match RW10L OR RW10B
            if (!string.IsNullOrEmpty(runway))
            {
                runway = runway.Trim().ToUpperInvariant();
                if (!runway.StartsWith("RW"))
                    runwayTransition = "RW" + runway;
                else
                    runwayTransition = runway;

                // Extract base runway number for "Both" matching (10L -> 10, 28R -> 28)
                var baseRwy = System.Text.RegularExpressions.Regex.Match(runwayTransition, @"RW(\d+)").Groups[1].Value;
                if (!string.IsNullOrEmpty(baseRwy))
                {
                    runwayBoth = $"RW{baseRwy}B";
                }
            }

            // Build route_type filter based on procedure type
            // SID route_types: 4=Runway Transition, 5=Common Route, 6=Enroute Transition
            // STAR route_types: 1=Enroute Transition (conv), 2=Common Route, 3=Runway Transition; 4/5/6 for RNAV
            // Approach route_types: A=Approach Transition, F=Final Approach, Z=Missed Approach
            switch (type)
            {
                case ProcedureType.SID:
                    tableName = "tbl_pd_sids";
                    // SID order: Runway-Transition (4) -> Common (5) -> Enroute-Transition (6)
                    // runwayBoth handles cases like 10L matching RW10B (Both)
                    var sidRwyFilter = runwayBoth != null
                        ? $"(transition_identifier = '{runwayTransition}' OR transition_identifier = '{runwayBoth}')"
                        : $"transition_identifier = '{runwayTransition}'";

                    if (!string.IsNullOrEmpty(runwayTransition) && !string.IsNullOrEmpty(transition))
                    {
                        // Both runway and enroute transition specified
                        routeTypeFilter = $"(route_type = '5' OR (route_type = '4' AND {sidRwyFilter}) OR (route_type = '6' AND transition_identifier = @transition))";
                        orderBy = "route_type ASC, CAST(seqno AS INTEGER) ASC";
                    }
                    else if (!string.IsNullOrEmpty(runwayTransition))
                    {
                        // Only runway specified - get runway transition + common
                        routeTypeFilter = $"(route_type = '5' OR (route_type = '4' AND {sidRwyFilter}))";
                        orderBy = "route_type ASC, CAST(seqno AS INTEGER) ASC";
                    }
                    else if (!string.IsNullOrEmpty(transition))
                    {
                        // Only enroute transition specified - common + enroute transition
                        // FIX: Also include Route 4 (Runway Transition) to ensure connection to airport!
                        routeTypeFilter = "(route_type = '5' OR route_type = '4' OR (route_type = '6' AND transition_identifier = @transition))";
                        orderBy = "route_type ASC, CAST(seqno AS INTEGER) ASC";
                    }
                    else
                    {
                        // Nothing specified - get common route (5) if exists,
                        // OR any single runway transition (4) if no common route exists
                        // This handles SIDs like PTLD2 that only have runway transitions
                        routeTypeFilter = "(route_type = '5' OR route_type = '4')";
                        orderBy = "route_type ASC, transition_identifier ASC, CAST(seqno AS INTEGER) ASC";  // 4 then 5 (Runway -> Common)
                    }
                    break;

                case ProcedureType.STAR:
                    tableName = "tbl_pe_stars";
                    // STAR order: Enroute-Transition (1/4) -> Common (2/5) -> Runway-Transition (3/6)
                    // runwayBoth handles cases like 12R matching RW12B (Both)
                    var starRwyFilter = runwayBoth != null
                        ? $"(transition_identifier = '{runwayTransition}' OR transition_identifier = '{runwayBoth}')"
                        : $"transition_identifier = '{runwayTransition}'";

                    if (!string.IsNullOrEmpty(transition) && !string.IsNullOrEmpty(runwayTransition))
                    {
                        // Both enroute transition and runway specified
                        routeTypeFilter = $"(route_type IN ('2', '5') OR ((route_type = '1' OR route_type = '4') AND transition_identifier = @transition) OR ((route_type = '3' OR route_type = '6') AND {starRwyFilter}))";
                        orderBy = "CASE WHEN route_type IN ('1', '4') THEN 0 WHEN route_type IN ('2', '5') THEN 1 ELSE 2 END, CAST(seqno AS INTEGER) ASC";
                    }
                    else if (!string.IsNullOrEmpty(transition))
                    {
                        // Only enroute transition - enroute + common
                        // FIX: Also include Route 3/6 (Runway Transitions) to ensure connection to airport!
                        routeTypeFilter = "(route_type IN ('2', '5') OR route_type IN ('3', '6') OR ((route_type = '1' OR route_type = '4') AND transition_identifier = @transition))";
                        orderBy = "CASE WHEN route_type IN ('1', '4') THEN 0 ELSE 1 END, CAST(seqno AS INTEGER) ASC";
                    }
                    else if (!string.IsNullOrEmpty(runwayTransition))
                    {
                        // Only runway - common + runway transition
                        routeTypeFilter = $"(route_type IN ('2', '5') OR ((route_type = '3' OR route_type = '6') AND {starRwyFilter}))";
                        orderBy = "CASE WHEN route_type IN ('2', '5') THEN 0 ELSE 1 END, CAST(seqno AS INTEGER) ASC";
                    }
                    else
                    {
                        // Nothing specified - get common route (2/5) if exists,
                        // OR any single enroute/runway transition if no common route exists
                        // This handles STARs that only have transitions
                        routeTypeFilter = "(route_type IN ('1', '2', '3', '4', '5', '6'))";
                        orderBy = "CASE WHEN route_type IN ('1', '4') THEN 0 WHEN route_type IN ('2', '5') THEN 1 ELSE 2 END, transition_identifier ASC, CAST(seqno AS INTEGER) ASC";
                    }
                    break;

                case ProcedureType.Approach:
                    tableName = "tbl_pf_iaps";
                    // Approach: Transition (A) -> Final (F) -> Missed (Z)
                    if (!string.IsNullOrEmpty(transition))
                    {
                        // Transition + Final approach
                        routeTypeFilter = "(route_type = 'F' OR (route_type = 'A' AND transition_identifier = @transition))";
                        orderBy = "CASE WHEN route_type = 'A' THEN 0 ELSE 1 END, CAST(seqno AS INTEGER) ASC";
                    }
                    else
                    {
                        // No transition - Final approach legs (F=Final, I=ILS, J=GNSS, P=GPS, R=RNAV, S=VOR/DME, etc.)
                        routeTypeFilter = "route_type NOT IN ('A', 'Z')";
                        orderBy = "CAST(seqno AS INTEGER) ASC";
                    }
                    break;

                default:
                    return legs;
            }

            try
            {
                using (var cmd = new SQLiteCommand(_connection))
                {
                    cmd.CommandText = $@"
                        SELECT
                            seqno,
                            waypoint_identifier,
                            waypoint_latitude,
                            waypoint_longitude,
                            path_termination,
                            turn_direction,
                            altitude_description,
                            altitude1,
                            altitude2,
                            speed_limit,
                            speed_limit_description,
                            course,
                            route_distance_holding_distance_time,
                            waypoint_description_code,
                            route_type,
                            transition_identifier
                        FROM {tableName}
                        WHERE airport_identifier = @icao
                          AND procedure_identifier = @proc
                          AND {routeTypeFilter}
                        ORDER BY {orderBy}";

                    cmd.Parameters.AddWithValue("@icao", icao.ToUpperInvariant());
                    cmd.Parameters.AddWithValue("@proc", procedureId);
                    if (!string.IsNullOrEmpty(transition))
                    {
                        cmd.Parameters.AddWithValue("@transition", transition.ToUpperInvariant());
                    }

                    int rowCount = 0;
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            rowCount++;
                            try
                            {
                                var seqNo = reader.IsDBNull(0) ? 0 : Convert.ToInt32(reader.GetValue(0));
                                var wpIdent = reader.IsDBNull(1) ? "" : reader.GetValue(1)?.ToString() ?? "";
                                var lat = reader.IsDBNull(2) ? 0.0 : Convert.ToDouble(reader.GetValue(2));
                                var lon = reader.IsDBNull(3) ? 0.0 : Convert.ToDouble(reader.GetValue(3));
                                var pathTerm = reader.IsDBNull(4) ? "" : reader.GetValue(4)?.ToString() ?? "";
                                var altDesc = reader.IsDBNull(6) ? "" : reader.GetValue(6)?.ToString() ?? "";
                                int? alt1 = reader.IsDBNull(7) ? (int?)null : Convert.ToInt32(reader.GetValue(7));
                                int? alt2 = reader.IsDBNull(8) ? (int?)null : Convert.ToInt32(reader.GetValue(8));
                                int? spdLimit = reader.IsDBNull(9) ? (int?)null : Convert.ToInt32(reader.GetValue(9));
                                var spdDesc = reader.IsDBNull(10) ? "" : reader.GetValue(10)?.ToString() ?? "";
                                double? course = reader.IsDBNull(11) ? (double?)null : Convert.ToDouble(reader.GetValue(11));
                                double? distance = reader.IsDBNull(12) ? (double?)null : Convert.ToDouble(reader.GetValue(12));
                                var descCode = reader.IsDBNull(13) ? "" : reader.GetValue(13)?.ToString() ?? "";
                                var routeType = reader.IsDBNull(14) ? "" : reader.GetValue(14)?.ToString() ?? "";
                                var transId = reader.IsDBNull(15) ? "" : reader.GetValue(15)?.ToString() ?? "";

                                var leg = new ProcedureLeg
                                {
                                    SequenceNumber = seqNo,
                                    WaypointIdentifier = wpIdent,
                                    Latitude = lat,
                                    Longitude = lon,
                                    PathTerminator = pathTerm,
                                    AltitudeConstraint = altDesc,
                                    Altitude1 = alt1,
                                    Altitude2 = alt2,
                                    SpeedLimit = spdLimit,
                                    SpeedConstraint = spdDesc,
                                    Course = course,
                                    Distance = distance,
                                    RouteType = routeType,
                                    TransitionIdentifier = transId,
                                    Overfly = descCode.Contains("E")
                                };

                                // RW waypoints durchlassen, auch wenn lat=0 (werden später mit Runway-Koordinaten ergänzt)
                                if (Math.Abs(lat) > 0.001 || Math.Abs(lon) > 0.001 || wpIdent.StartsWith("RW"))
                                {
                                    legs.Add(leg);
                                }
                            }
                            catch (Exception rowEx)
                            {
                                Console.WriteLine($"[Navigraph DB] ERROR row {rowCount}: {rowEx.Message}");
                            }
                        }
                    }

                    Console.WriteLine($"[Navigraph DB] GetProcedureLegs FIX_V4: {icao}/{procedureId} -> {rowCount} rows, {legs.Count} kept");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Navigraph DB: Procedure Legs Query Fehler: {ex.Message}");
            }

            // Ergänze Koordinaten für Runway-Waypoints die lat=0/lon=0 haben
            foreach (var leg in legs)
            {
                if (leg.WaypointIdentifier.StartsWith("RW") && Math.Abs(leg.Latitude) < 0.001 && Math.Abs(leg.Longitude) < 0.001)
                {
                    // Extrahiere Runway-Identifier (z.B. "RW18" -> "18", "RW08L" -> "08L")
                    var rwyIdent = leg.WaypointIdentifier.Substring(2);
                    var rwyInfo = GetRunway(icao, rwyIdent);
                    if (rwyInfo != null)
                    {
                        leg.Latitude = rwyInfo.ThresholdLat;
                        leg.Longitude = rwyInfo.ThresholdLon;
                        Console.WriteLine($"[Navigraph DB] Enriched RW waypoint {leg.WaypointIdentifier} with coordinates: ({leg.Latitude:F6}, {leg.Longitude:F6})");
                    }
                    else
                    {
                        Console.WriteLine($"[Navigraph DB] WARNING: Could not find runway {rwyIdent} at {icao} for waypoint {leg.WaypointIdentifier}");
                    }
                }
            }

            return legs;
        }

        /// <summary>
        /// Diagnostic version of GetProcedureLegs that returns detailed debug info
        /// </summary>
        public (List<ProcedureLeg> Legs, List<string> DebugLog) GetProcedureLegsWithDebug(string icao, string procedureId, ProcedureType type)
        {
            var legs = new List<ProcedureLeg>();
            var debugLog = new List<string>();
            
            debugLog.Add($"DIAG_V3: Starting for {icao}/{procedureId} type={type}");
            
            if (!IsConnected)
            {
                debugLog.Add("ERROR: Not connected to database");
                return (legs, debugLog);
            }

            string tableName = type == ProcedureType.Approach ? "tbl_pf_iaps" : "tbl_pe_stars";
            string routeTypeFilter = "route_type NOT IN ('A', 'Z')";
            string orderBy = "CAST(seqno AS INTEGER) ASC";

            debugLog.Add($"Table: {tableName}, Filter: {routeTypeFilter}");

            try
            {
                using (var cmd = new SQLiteCommand(_connection))
                {
                    cmd.CommandText = $@"
                        SELECT
                            seqno,
                            waypoint_identifier,
                            waypoint_latitude,
                            waypoint_longitude,
                            path_termination,
                            turn_direction,
                            altitude_description,
                            altitude1,
                            altitude2,
                            speed_limit,
                            speed_limit_description,
                            course,
                            route_distance_holding_distance_time,
                            waypoint_description_code,
                            route_type,
                            transition_identifier
                        FROM {tableName}
                        WHERE airport_identifier = @icao
                          AND procedure_identifier = @proc
                          AND {routeTypeFilter}
                        ORDER BY {orderBy}";

                    cmd.Parameters.AddWithValue("@icao", icao.ToUpperInvariant());
                    cmd.Parameters.AddWithValue("@proc", procedureId);

                    debugLog.Add($"SQL: {cmd.CommandText}");
                    debugLog.Add($"Params: icao={icao}, proc={procedureId}");

                    int rowCount = 0;
                    using (var reader = cmd.ExecuteReader())
                    {
                        debugLog.Add($"Reader created, HasRows={reader.HasRows}");
                        
                        while (reader.Read())
                        {
                            rowCount++;
                            try
                            {
                                var seqNo = reader.IsDBNull(0) ? 0 : Convert.ToInt32(reader.GetValue(0));
                                var wpIdent = reader.IsDBNull(1) ? "" : reader.GetValue(1)?.ToString() ?? "";
                                var lat = reader.IsDBNull(2) ? 0.0 : Convert.ToDouble(reader.GetValue(2));
                                var lon = reader.IsDBNull(3) ? 0.0 : Convert.ToDouble(reader.GetValue(3));
                                var pathTerm = reader.IsDBNull(4) ? "" : reader.GetValue(4)?.ToString() ?? "";
                                // Index 5: turn_direction (unused for now)
                                var altDesc = reader.IsDBNull(6) ? "" : reader.GetValue(6)?.ToString() ?? "";
                                int? alt1 = reader.IsDBNull(7) ? (int?)null : Convert.ToInt32(reader.GetValue(7));
                                int? alt2 = reader.IsDBNull(8) ? (int?)null : Convert.ToInt32(reader.GetValue(8));
                                int? spdLimit = reader.IsDBNull(9) ? (int?)null : Convert.ToInt32(reader.GetValue(9));
                                var spdDesc = reader.IsDBNull(10) ? "" : reader.GetValue(10)?.ToString() ?? "";
                                double? course = reader.IsDBNull(11) ? (double?)null : Convert.ToDouble(reader.GetValue(11));
                                double? distance = reader.IsDBNull(12) ? (double?)null : Convert.ToDouble(reader.GetValue(12));
                                var descCode = reader.IsDBNull(13) ? "" : reader.GetValue(13)?.ToString() ?? "";
                                var routeType = reader.IsDBNull(14) ? "" : reader.GetValue(14)?.ToString() ?? "";
                                var transId = reader.IsDBNull(15) ? "" : reader.GetValue(15)?.ToString() ?? "";

                                debugLog.Add($"Row {rowCount}: seq={seqNo} wp='{wpIdent}' path={pathTerm} lat={lat} lon={lon} alt1={alt1} alt2={alt2}");

                                // Stop at missed approach (CA = Course to Altitude after runway)
                                if (pathTerm == "CA" || pathTerm == "HA" || pathTerm == "HF" || pathTerm == "HM")
                                {
                                    debugLog.Add($"  -> STOP: Missed approach detected (path={pathTerm})");
                                    break;
                                }

                                // RW waypoints durchlassen, auch wenn lat=0 (werden später mit Runway-Koordinaten ergänzt)
                                bool hasCoords = Math.Abs(lat) > 0.001 || Math.Abs(lon) > 0.001 || wpIdent.StartsWith("RW");

                                if (hasCoords)
                                {
                                    var leg = new ProcedureLeg
                                    {
                                        SequenceNumber = seqNo,
                                        WaypointIdentifier = wpIdent,
                                        Latitude = lat,
                                        Longitude = lon,
                                        PathTerminator = pathTerm,
                                        AltitudeConstraint = altDesc,
                                        Altitude1 = alt1,
                                        Altitude2 = alt2,
                                        SpeedLimit = spdLimit,
                                        SpeedConstraint = spdDesc,
                                        Course = course,
                                        Distance = distance,
                                        RouteType = routeType,
                                        TransitionIdentifier = transId,
                                        Overfly = descCode.Contains("E")
                                    };
                                    legs.Add(leg);
                                    debugLog.Add($"  -> ADDED");
                                }
                                else
                                {
                                    debugLog.Add($"  -> FILTERED (0,0)");
                                }
                            }
                            catch (Exception rowEx)
                            {
                                debugLog.Add($"Row {rowCount} ERROR: {rowEx.Message}");
                            }
                        }
                    }
                    debugLog.Add($"Reader closed. Total rows: {rowCount}, Kept: {legs.Count}");
                }
            }
            catch (Exception ex)
            {
                debugLog.Add($"QUERY ERROR: {ex.Message}");
            }

            // Ergänze Koordinaten für Runway-Waypoints die lat=0/lon=0 haben
            foreach (var leg in legs)
            {
                if (leg.WaypointIdentifier.StartsWith("RW") && Math.Abs(leg.Latitude) < 0.001 && Math.Abs(leg.Longitude) < 0.001)
                {
                    var rwyIdent = leg.WaypointIdentifier.Substring(2);
                    var rwyInfo = GetRunway(icao, rwyIdent);
                    if (rwyInfo != null)
                    {
                        leg.Latitude = rwyInfo.ThresholdLat;
                        leg.Longitude = rwyInfo.ThresholdLon;
                        debugLog.Add($"Enriched RW waypoint {leg.WaypointIdentifier} with coordinates: ({leg.Latitude:F6}, {leg.Longitude:F6})");
                    }
                    else
                    {
                        debugLog.Add($"WARNING: Could not find runway {rwyIdent} at {icao}");
                    }
                }
            }

            return (legs, debugLog);
        }

        /// <summary>
        /// Test the EXACT SQL query from GetProcedureLegs for Approach
        /// </summary>
        public List<Dictionary<string, object>> TestApproachQuery(string icao, string procedureId)
        {
            var legs = new List<Dictionary<string, object>>();
            if (!IsConnected) return legs;

            try
            {
                using (var cmd = new SQLiteCommand(_connection))
                {
                    cmd.CommandText = @"
                        SELECT
                            seqno,
                            waypoint_identifier,
                            waypoint_latitude,
                            waypoint_longitude,
                            path_termination,
                            turn_direction,
                            altitude_description,
                            altitude1,
                            altitude2,
                            speed_limit,
                            speed_limit_description,
                            course,
                            route_distance_holding_distance_time,
                            waypoint_description_code,
                            route_type,
                            transition_identifier
                        FROM tbl_pf_iaps
                        WHERE airport_identifier = @icao
                          AND procedure_identifier = @proc
                          AND route_type NOT IN ('A', 'Z')
                        ORDER BY CAST(seqno AS INTEGER) ASC";

                    cmd.Parameters.AddWithValue("@icao", icao.ToUpperInvariant());
                    cmd.Parameters.AddWithValue("@proc", procedureId);

                    Console.WriteLine($"[TEST] Executing exact GetProcedureLegs query for {icao}/{procedureId}");

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var leg = new Dictionary<string, object>();
                            for (int i = 0; i < reader.FieldCount; i++)
                            {
                                leg[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                            }
                            legs.Add(leg);
                            Console.WriteLine($"[TEST] Row: seqno={leg["seqno"]} wp={leg["waypoint_identifier"]} route={leg["route_type"]}");
                        }
                    }
                    Console.WriteLine($"[TEST] Total rows: {legs.Count}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TEST] Error: {ex.Message}");
            }

            return legs;
        }

        /// <summary>
        /// Get ALL raw approach legs for debugging (no coordinate filtering)
        /// </summary>
        public List<Dictionary<string, object>> GetRawApproachLegs(string icao, string procedureId)
        {
            var legs = new List<Dictionary<string, object>>();
            if (!IsConnected) return legs;

            try
            {
                using (var cmd = new SQLiteCommand(_connection))
                {
                    cmd.CommandText = @"
                        SELECT 
                            seqno,
                            waypoint_identifier,
                            waypoint_latitude,
                            waypoint_longitude,
                            path_termination,
                            route_type,
                            transition_identifier,
                            course,
                            route_distance_holding_distance_time,
                            altitude1,
                            altitude2,
                            recommended_navaid,
                            recommended_navaid_latitude,
                            recommended_navaid_longitude,
                            turn_direction,
                            waypoint_description_code,
                            procedure_identifier
                        FROM tbl_pf_iaps
                        WHERE airport_identifier = @icao
                          AND procedure_identifier = @proc
                        ORDER BY route_type, seqno ASC";

                    cmd.Parameters.AddWithValue("@icao", icao.ToUpperInvariant());
                    cmd.Parameters.AddWithValue("@proc", procedureId);

                    Console.WriteLine($"Navigraph DB: Querying raw approach legs for {icao}/{procedureId}");

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var leg = new Dictionary<string, object>();
                            for (int i = 0; i < reader.FieldCount; i++)
                            {
                                var name = reader.GetName(i);
                                var value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                                leg[name] = value;
                            }
                            legs.Add(leg);
                        }
                    }
                    Console.WriteLine($"Navigraph DB: Found {legs.Count} raw approach legs");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Navigraph DB: Raw Approach Legs Query Error: {ex.Message}");
                Console.WriteLine($"Navigraph DB: Stack: {ex.StackTrace}");
            }

            return legs;
        }

        #endregion

        #region Waypoint/Navaid Queries

        /// <summary>
        /// Get waypoint by identifier
        /// </summary>
        public WaypointInfo GetWaypoint(string ident, string region = null)
        {
            if (!IsConnected) return null;

            try
            {
                using (var cmd = new SQLiteCommand(_connection))
                {
                    if (!string.IsNullOrEmpty(region))
                    {
                        cmd.CommandText = @"
                            SELECT waypoint_identifier, waypoint_name, icao_code,
                                   waypoint_latitude, waypoint_longitude, waypoint_type
                            FROM tbl_ea_enroute_waypoints
                            WHERE waypoint_identifier = @ident AND icao_code = @region
                            LIMIT 1";
                        cmd.Parameters.AddWithValue("@region", region.ToUpperInvariant());
                    }
                    else
                    {
                        cmd.CommandText = @"
                            SELECT waypoint_identifier, waypoint_name, icao_code,
                                   waypoint_latitude, waypoint_longitude, waypoint_type
                            FROM tbl_ea_enroute_waypoints
                            WHERE waypoint_identifier = @ident
                            LIMIT 1";
                    }

                    cmd.Parameters.AddWithValue("@ident", ident.ToUpperInvariant());

                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            return new WaypointInfo
                            {
                                Identifier = reader.GetString(0),
                                Name = reader.IsDBNull(1) ? "" : reader.GetString(1),
                                Region = reader.IsDBNull(2) ? "" : reader.GetString(2),
                                Latitude = reader.GetDouble(3),
                                Longitude = reader.GetDouble(4),
                                Type = reader.IsDBNull(5) ? "WPT" : reader.GetString(5)
                            };
                        }
                    }
                }

                // Also try terminal waypoints
                using (var cmd = new SQLiteCommand(_connection))
                {
                    cmd.CommandText = @"
                        SELECT waypoint_identifier, waypoint_name, icao_code,
                               waypoint_latitude, waypoint_longitude, waypoint_type
                        FROM tbl_pc_terminal_waypoints
                        WHERE waypoint_identifier = @ident
                        LIMIT 1";

                    cmd.Parameters.AddWithValue("@ident", ident.ToUpperInvariant());

                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            return new WaypointInfo
                            {
                                Identifier = reader.GetString(0),
                                Name = reader.IsDBNull(1) ? "" : reader.GetString(1),
                                Region = reader.IsDBNull(2) ? "" : reader.GetString(2),
                                Latitude = reader.GetDouble(3),
                                Longitude = reader.GetDouble(4),
                                Type = reader.IsDBNull(5) ? "WPT" : reader.GetString(5)
                            };
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Navigraph DB: Waypoint Query Fehler: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Get navaids near a position
        /// </summary>
        public List<NavaidInfo> GetNavaidsNear(double lat, double lon, double radiusNm)
        {
            var navaids = new List<NavaidInfo>();
            if (!IsConnected) return navaids;

            // Convert radius to approximate degree bounds
            double latDelta = radiusNm / 60.0;
            double lonDelta = radiusNm / (60.0 * Math.Cos(lat * Math.PI / 180));

            try
            {
                using (var cmd = new SQLiteCommand(_connection))
                {
                    cmd.CommandText = @"
                        SELECT
                            navaid_identifier, navaid_name, navaid_class,
                            navaid_frequency, navaid_latitude, navaid_longitude,
                            icao_code, dme_elevation, station_declination
                        FROM tbl_d_vhfnavaids
                        WHERE navaid_latitude BETWEEN @minLat AND @maxLat
                          AND navaid_longitude BETWEEN @minLon AND @maxLon";

                    cmd.Parameters.AddWithValue("@minLat", lat - latDelta);
                    cmd.Parameters.AddWithValue("@maxLat", lat + latDelta);
                    cmd.Parameters.AddWithValue("@minLon", lon - lonDelta);
                    cmd.Parameters.AddWithValue("@maxLon", lon + lonDelta);

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            navaids.Add(new NavaidInfo
                            {
                                Identifier = reader.GetString(0),
                                Name = reader.IsDBNull(1) ? "" : reader.GetString(1),
                                Type = reader.IsDBNull(2) ? "VOR" : reader.GetString(2),
                                Frequency = reader.IsDBNull(3) ? 0 : reader.GetDouble(3),
                                Latitude = reader.GetDouble(4),
                                Longitude = reader.GetDouble(5),
                                Region = reader.IsDBNull(6) ? "" : reader.GetString(6),
                                Elevation = reader.IsDBNull(7) ? 0 : reader.GetInt32(7),
                                MagneticVariation = reader.IsDBNull(8) ? 0 : reader.GetDouble(8)
                            });
                        }
                    }
                }

                // Also get NDBs
                using (var cmd = new SQLiteCommand(_connection))
                {
                    cmd.CommandText = @"
                        SELECT
                            navaid_identifier, navaid_name, navaid_class,
                            navaid_frequency, navaid_latitude, navaid_longitude,
                            icao_code
                        FROM tbl_db_enroute_ndbnavaids
                        WHERE navaid_latitude BETWEEN @minLat AND @maxLat
                          AND navaid_longitude BETWEEN @minLon AND @maxLon";

                    cmd.Parameters.AddWithValue("@minLat", lat - latDelta);
                    cmd.Parameters.AddWithValue("@maxLat", lat + latDelta);
                    cmd.Parameters.AddWithValue("@minLon", lon - lonDelta);
                    cmd.Parameters.AddWithValue("@maxLon", lon + lonDelta);

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            navaids.Add(new NavaidInfo
                            {
                                Identifier = reader.GetString(0),
                                Name = reader.IsDBNull(1) ? "" : reader.GetString(1),
                                Type = "NDB",
                                Frequency = reader.IsDBNull(3) ? 0 : reader.GetDouble(3),
                                Latitude = reader.GetDouble(4),
                                Longitude = reader.GetDouble(5),
                                Region = reader.IsDBNull(6) ? "" : reader.GetString(6)
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Navigraph DB: Navaid Query Fehler: {ex.Message}");
            }

            return navaids;
        }

        /// <summary>
        /// Get navaid by identifier
        /// </summary>
        public NavaidInfo GetNavaid(string ident)
        {
            if (!IsConnected) return null;

            try
            {
                // Try VOR first
                using (var cmd = new SQLiteCommand(_connection))
                {
                    cmd.CommandText = @"
                        SELECT
                            navaid_identifier, navaid_name, navaid_class,
                            navaid_frequency, navaid_latitude, navaid_longitude,
                            icao_code, dme_elevation
                        FROM tbl_d_vhfnavaids
                        WHERE navaid_identifier = @ident
                        LIMIT 1";

                    cmd.Parameters.AddWithValue("@ident", ident.ToUpperInvariant());

                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            return new NavaidInfo
                            {
                                Identifier = reader.GetString(0),
                                Name = reader.IsDBNull(1) ? "" : reader.GetString(1),
                                Type = reader.IsDBNull(2) ? "VOR" : reader.GetString(2),
                                Frequency = reader.IsDBNull(3) ? 0 : reader.GetDouble(3),
                                Latitude = reader.GetDouble(4),
                                Longitude = reader.GetDouble(5),
                                Region = reader.IsDBNull(6) ? "" : reader.GetString(6),
                                Elevation = reader.IsDBNull(7) ? 0 : reader.GetInt32(7)
                            };
                        }
                    }
                }

                // Try NDB
                using (var cmd = new SQLiteCommand(_connection))
                {
                    cmd.CommandText = @"
                        SELECT
                            navaid_identifier, navaid_name, navaid_class,
                            navaid_frequency, navaid_latitude, navaid_longitude,
                            icao_code
                        FROM tbl_db_enroute_ndbnavaids
                        WHERE navaid_identifier = @ident
                        LIMIT 1";

                    cmd.Parameters.AddWithValue("@ident", ident.ToUpperInvariant());

                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            return new NavaidInfo
                            {
                                Identifier = reader.GetString(0),
                                Name = reader.IsDBNull(1) ? "" : reader.GetString(1),
                                Type = "NDB",
                                Frequency = reader.IsDBNull(3) ? 0 : reader.GetDouble(3),
                                Latitude = reader.GetDouble(4),
                                Longitude = reader.GetDouble(5),
                                Region = reader.IsDBNull(6) ? "" : reader.GetString(6)
                            };
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Navigraph DB: Navaid Query Fehler: {ex.Message}");
            }

            return null;
        }

        #endregion

        #region Airway Queries

        /// <summary>
        /// Get airway information
        /// </summary>
        public AirwayInfo GetAirway(string ident)
        {
            if (!IsConnected) return null;

            try
            {
                var legs = new List<AirwayLeg>();

                using (var cmd = new SQLiteCommand(_connection))
                {
                    cmd.CommandText = @"
                        SELECT
                            seqno,
                            waypoint_identifier,
                            waypoint_latitude,
                            waypoint_longitude,
                            minimum_altitude1,
                            maximum_altitude,
                            direction_restriction,
                            route_type
                        FROM tbl_er_enroute_airways
                        WHERE route_identifier = @ident
                        ORDER BY CAST(seqno AS INTEGER)";

                    cmd.Parameters.AddWithValue("@ident", ident.ToUpperInvariant());

                    string routeType = "";

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            if (string.IsNullOrEmpty(routeType) && !reader.IsDBNull(7))
                            {
                                routeType = reader.GetString(7);
                            }

                            legs.Add(new AirwayLeg
                            {
                                SequenceNumber = reader.GetInt32(0),
                                WaypointIdentifier = reader.GetString(1),
                                Latitude = reader.GetDouble(2),
                                Longitude = reader.GetDouble(3),
                                MinimumAltitude = reader.IsDBNull(4) ? null : (int?)reader.GetInt32(4),
                                MaximumAltitude = reader.IsDBNull(5) ? null : (int?)reader.GetInt32(5),
                                Direction = reader.IsDBNull(6) ? "" : reader.GetString(6)
                            });
                        }
                    }

                    if (legs.Count > 0)
                    {
                        return new AirwayInfo
                        {
                            Identifier = ident,
                            Type = routeType,
                            Legs = legs
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Navigraph DB: Airway Query Fehler: {ex.Message}");
            }

            return null;
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _connection?.Close();
                    _connection?.Dispose();
                }
                _disposed = true;
            }
        }

        #endregion
    }
}
