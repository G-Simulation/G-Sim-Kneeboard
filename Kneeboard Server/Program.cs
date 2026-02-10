using System;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Kneeboard_Server.Logging;

namespace Kneeboard_Server
{
    static class Program
    {
        private static Mutex m_Mutex;
        /// <summary>
        /// Der Haupteinstiegspunkt für die Anwendung.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            KneeboardLogger.Startup("KNEEBOARD SERVER - NEW CODE VERSION ACTIVE (SEQNO FIX) - " + DateTime.Now.ToString());
            // Check database contents
            if (args.Length >= 1 && args[0] == "--check-db")
            {
                CheckDatabase();
                return;
            }

            // Apply saved language preference
            string lang = Properties.Settings.Default.language;
            if (!string.IsNullOrEmpty(lang))
            {
                Thread.CurrentThread.CurrentUICulture = new CultureInfo(lang);
            }

            bool debug = false;
            if (debug == false)
            {
                int milliseconds = 1000;
                Thread.Sleep(milliseconds);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                bool createdNew;
                m_Mutex = new Mutex(true, "KneeboardServerMutex", out createdNew);
                if (createdNew)
                {
                        Application.Run(new Kneeboard_Server());
                }
                else
                {
                    // MessageBox.Show("The application is already running.", Application.ProductName,
                    // MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                }
            }
            else
            {
                int milliseconds = 1000;
                Thread.Sleep(milliseconds);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new Kneeboard_Server());
            }
        }

        /// <summary>
        /// Check database contents - counts and sample data
        /// </summary>
        private static void CheckDatabase()
        {
            KneeboardLogger.Initialize(consoleOutput: true, fileOutput: true, minLevel: KneeboardLogger.Level.DEBUG);

            string dbPath = System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
                "data", "msfs_navdata.sqlite");

            KneeboardLogger.Info("Database", "=== Navdata Database Check ===");
            KneeboardLogger.Debug("Database", $"Database: {dbPath}");
            KneeboardLogger.Debug("Database", $"Exists: {System.IO.File.Exists(dbPath)}");

            if (!System.IO.File.Exists(dbPath))
            {
                KneeboardLogger.Error("Database", "Database not found!");
                return;
            }

            try
            {
                using (var conn = new System.Data.SQLite.SQLiteConnection($"Data Source={dbPath};Version=3;"))
                {
                    conn.Open();

                    // List all tables first
                    KneeboardLogger.Info("Database", "=== ALL TABLES IN DATABASE ===");
                    using (var cmd = new System.Data.SQLite.SQLiteCommand(
                        "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name", conn))
                    {
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                KneeboardLogger.Debug("Database", $"  {reader["name"]}");
                            }
                        }
                    }

                    // Count tables
                    KneeboardLogger.Info("Database", "=== TABLE COUNTS ===");
                    string[] tables = { "airport", "runway", "sid", "star", "approach", "transition", "procedure_leg", "waypoint", "vor", "ndb" };
                    foreach (var table in tables)
                    {
                        try
                        {
                            using (var cmd = new System.Data.SQLite.SQLiteCommand($"SELECT COUNT(*) FROM {table}", conn))
                            {
                                var count = cmd.ExecuteScalar();
                                KneeboardLogger.Debug("Database", $"  {table}: {count}");
                            }
                        }
                        catch
                        {
                            KneeboardLogger.Debug("Database", $"  {table}: (table not found)");
                        }
                    }

                    // Sample waypoints
                    KneeboardLogger.Info("Database", "=== SAMPLE WAYPOINTS (first 10) ===");
                    using (var cmd = new System.Data.SQLite.SQLiteCommand(
                        "SELECT ident, region, latitude, longitude FROM waypoint LIMIT 10", conn))
                    {
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                KneeboardLogger.Debug("Database", $"  {reader["ident"],-7} {reader["region"],-4} lat={reader["latitude"],-12} lon={reader["longitude"]}");
                            }
                        }
                    }

                    // Sample VORs
                    KneeboardLogger.Info("Database", "=== SAMPLE VORs (first 10) ===");
                    using (var cmd = new System.Data.SQLite.SQLiteCommand(
                        "SELECT ident, region, latitude, longitude, frequency FROM vor LIMIT 10", conn))
                    {
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                KneeboardLogger.Debug("Database", $"  {reader["ident"],-7} {reader["region"],-4} lat={reader["latitude"],-12} lon={reader["longitude"],-12} freq={reader["frequency"]}");
                            }
                        }
                    }

                    // Sample NDBs
                    KneeboardLogger.Info("Database", "=== SAMPLE NDBs (first 10) ===");
                    using (var cmd = new System.Data.SQLite.SQLiteCommand(
                        "SELECT ident, region, latitude, longitude, frequency FROM ndb LIMIT 10", conn))
                    {
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                KneeboardLogger.Debug("Database", $"  {reader["ident"],-7} {reader["region"],-4} lat={reader["latitude"],-12} lon={reader["longitude"],-12} freq={reader["frequency"]}");
                            }
                        }
                    }

                    // Show approach_leg table structure
                    KneeboardLogger.Info("Database", "=== APPROACH_LEG TABLE STRUCTURE ===");
                    using (var cmd = new System.Data.SQLite.SQLiteCommand(
                        "PRAGMA table_info(approach_leg)", conn))
                    {
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                KneeboardLogger.Debug("Database", $"  {reader["name"]} ({reader["type"]})");
                            }
                        }
                    }

                    // Check approach legs with fix references
                    KneeboardLogger.Info("Database", "=== APPROACH LEGS WITH FIXES (first 20) ===");
                    using (var cmd = new System.Data.SQLite.SQLiteCommand(
                        @"SELECT al.fix_ident, al.fix_region, al.fix_type,
                                 w.latitude as wpt_lat, w.longitude as wpt_lon,
                                 v.latitude as vor_lat, v.longitude as vor_lon,
                                 n.latitude as ndb_lat, n.longitude as ndb_lon
                          FROM approach_leg al
                          LEFT JOIN waypoint w ON al.fix_ident = w.ident AND al.fix_region = w.region
                          LEFT JOIN vor v ON al.fix_ident = v.ident AND al.fix_region = v.region
                          LEFT JOIN ndb n ON al.fix_ident = n.ident AND al.fix_region = n.region
                          WHERE al.fix_ident IS NOT NULL AND al.fix_ident != ''
                          LIMIT 20", conn))
                    {
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                string fixIdent = reader["fix_ident"]?.ToString() ?? "";
                                string fixRegion = reader["fix_region"]?.ToString() ?? "";
                                int fixType = 0;
                                if (reader["fix_type"] != DBNull.Value)
                                    int.TryParse(reader["fix_type"].ToString(), out fixType);

                                double? lat = null, lon = null;
                                string source = "NONE";

                                // Check waypoint first
                                if (reader["wpt_lat"] != DBNull.Value)
                                {
                                    lat = Convert.ToDouble(reader["wpt_lat"]);
                                    lon = Convert.ToDouble(reader["wpt_lon"]);
                                    source = "WPT";
                                }
                                else if (reader["vor_lat"] != DBNull.Value)
                                {
                                    lat = Convert.ToDouble(reader["vor_lat"]);
                                    lon = Convert.ToDouble(reader["vor_lon"]);
                                    source = "VOR";
                                }
                                else if (reader["ndb_lat"] != DBNull.Value)
                                {
                                    lat = Convert.ToDouble(reader["ndb_lat"]);
                                    lon = Convert.ToDouble(reader["ndb_lon"]);
                                    source = "NDB";
                                }

                                if (lat.HasValue)
                                {
                                    KneeboardLogger.Debug("Database", $"  fix={fixIdent,-7} [{source}] lat={lat:F6} lon={lon:F6}");
                                }
                                else
                                {
                                    KneeboardLogger.Debug("Database", $"  fix={fixIdent,-7} [NO NAVAID] type={fixType}");
                                }
                            }
                        }
                    }

                    // Count how many approach legs have resolvable coordinates
                    KneeboardLogger.Info("Database", "=== COORDINATE RESOLUTION STATS ===");
                    using (var cmd = new System.Data.SQLite.SQLiteCommand(
                        @"SELECT
                            COUNT(*) as total_legs,
                            SUM(CASE WHEN fix_ident IS NOT NULL AND fix_ident != '' THEN 1 ELSE 0 END) as legs_with_fix,
                            (SELECT COUNT(*) FROM approach_leg al
                             WHERE EXISTS (SELECT 1 FROM waypoint w WHERE al.fix_ident = w.ident AND al.fix_region = w.region)) as matched_waypoint,
                            (SELECT COUNT(*) FROM approach_leg al
                             WHERE EXISTS (SELECT 1 FROM vor v WHERE al.fix_ident = v.ident AND al.fix_region = v.region)) as matched_vor,
                            (SELECT COUNT(*) FROM approach_leg al
                             WHERE EXISTS (SELECT 1 FROM ndb n WHERE al.fix_ident = n.ident AND al.fix_region = n.region)) as matched_ndb
                          FROM approach_leg", conn))
                    {
                        using (var reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                KneeboardLogger.Info("Database", $"  Total approach legs: {reader["total_legs"]}");
                                KneeboardLogger.Info("Database", $"  Legs with fix_ident: {reader["legs_with_fix"]}");
                                KneeboardLogger.Info("Database", $"  Matched to waypoint: {reader["matched_waypoint"]}");
                                KneeboardLogger.Info("Database", $"  Matched to VOR: {reader["matched_vor"]}");
                                KneeboardLogger.Info("Database", $"  Matched to NDB: {reader["matched_ndb"]}");
                            }
                        }
                    }

                    // Show sample raw approach legs
                    KneeboardLogger.Info("Database", "=== SAMPLE APPROACH LEGS (first 10) ===");
                    using (var cmd = new System.Data.SQLite.SQLiteCommand(
                        "SELECT * FROM approach_leg LIMIT 10", conn))
                    {
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                var cols = new System.Collections.Generic.List<string>();
                                for (int i = 0; i < reader.FieldCount; i++)
                                {
                                    cols.Add($"{reader.GetName(i)}={reader[i]}");
                                }
                                KneeboardLogger.Debug("Database", $"  {string.Join(", ", cols)}");
                            }
                        }
                    }

                    KneeboardLogger.Info("Database", "=== DONE ===");
                }
            }
            catch (Exception ex)
            {
                KneeboardLogger.Error("Database", ex);
            }
        }


    }
}
