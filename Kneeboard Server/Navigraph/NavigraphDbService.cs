using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;

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
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Navigraph DB: Verbindungsfehler: {ex.Message}");
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
                            llz_identifier
                        FROM tbl_pg_runways
                        WHERE UPPER(TRIM(airport_identifier)) = @icao
                        ORDER BY runway_identifier";

                    cmd.Parameters.AddWithValue("@icao", icao.ToUpperInvariant());

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var runway = new RunwayInfo
                            {
                                Identifier = reader.IsDBNull(0) ? "" : reader.GetString(0).Trim(),
                                ThresholdLat = reader.IsDBNull(1) ? 0 : reader.GetDouble(1),
                                ThresholdLon = reader.IsDBNull(2) ? 0 : reader.GetDouble(2),
                                Heading = reader.IsDBNull(3) ? 0 : reader.GetDouble(3),
                                Length = reader.IsDBNull(4) ? 0 : (int)reader.GetDouble(4),
                                Width = reader.IsDBNull(5) ? 0 : reader.GetInt32(5),
                                ThresholdElevation = reader.IsDBNull(6) ? 0 : reader.GetInt32(6),
                                ThresholdDisplacement = reader.IsDBNull(8) ? 0 : reader.GetInt32(8)
                            };

                            // Calculate runway end coordinates
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
                            runway_transition,
                            transition_identifier
                        FROM {tableName}
                        WHERE airport_identifier = @icao
                        ORDER BY procedure_identifier";

                    cmd.Parameters.AddWithValue("@icao", icao.ToUpperInvariant());

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            procedures.Add(new ProcedureSummary
                            {
                                Identifier = reader.GetString(0),
                                Runway = reader.IsDBNull(1) ? "" : reader.GetString(1),
                                TransitionIdentifier = reader.IsDBNull(2) ? "" : reader.GetString(2),
                                Type = type
                            });
                        }
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
                            runway_identifier,
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
                            var runway = reader.IsDBNull(1) ? "" : reader.GetString(1);
                            var transition = reader.IsDBNull(2) ? "" : reader.GetString(2);

                            var key = $"{procId}_{runway}";

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

                                approachDict[key] = new ApproachSummary
                                {
                                    Identifier = procId,
                                    Type = appType,
                                    Runway = runway,
                                    Transitions = new List<string>()
                                };
                            }

                            if (!string.IsNullOrEmpty(transition) &&
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
        /// Get procedure legs
        /// </summary>
        public List<ProcedureLeg> GetProcedureLegs(string icao, string procedureId, ProcedureType type)
        {
            var legs = new List<ProcedureLeg>();
            if (!IsConnected) return legs;

            string tableName;
            switch (type)
            {
                case ProcedureType.SID:
                    tableName = "tbl_pd_sids";
                    break;
                case ProcedureType.STAR:
                    tableName = "tbl_pe_stars";
                    break;
                case ProcedureType.Approach:
                    tableName = "tbl_pf_iaps";
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
                            waypoint_description_code
                        FROM {tableName}
                        WHERE airport_identifier = @icao
                          AND procedure_identifier = @proc
                        ORDER BY seqno";

                    cmd.Parameters.AddWithValue("@icao", icao.ToUpperInvariant());
                    cmd.Parameters.AddWithValue("@proc", procedureId);

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var leg = new ProcedureLeg
                            {
                                SequenceNumber = reader.GetInt32(0),
                                WaypointIdentifier = reader.IsDBNull(1) ? "" : reader.GetString(1),
                                Latitude = reader.IsDBNull(2) ? 0 : reader.GetDouble(2),
                                Longitude = reader.IsDBNull(3) ? 0 : reader.GetDouble(3),
                                PathTerminator = reader.IsDBNull(4) ? "" : reader.GetString(4),
                                AltitudeConstraint = reader.IsDBNull(6) ? "" : reader.GetString(6),
                                Altitude1 = reader.IsDBNull(7) ? null : (int?)reader.GetInt32(7),
                                Altitude2 = reader.IsDBNull(8) ? null : (int?)reader.GetInt32(8),
                                SpeedLimit = reader.IsDBNull(9) ? null : (int?)reader.GetInt32(9),
                                SpeedConstraint = reader.IsDBNull(10) ? "" : reader.GetString(10),
                                Course = reader.IsDBNull(11) ? null : (double?)reader.GetDouble(11),
                                Distance = reader.IsDBNull(12) ? null : (double?)reader.GetDouble(12)
                            };

                            // Check for overfly (waypoint_description_code contains 'E' for overfly)
                            var descCode = reader.IsDBNull(13) ? "" : reader.GetString(13);
                            leg.Overfly = descCode.Contains("E");

                            legs.Add(leg);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Navigraph DB: Procedure Legs Query Fehler: {ex.Message}");
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
                        ORDER BY seqno";

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
