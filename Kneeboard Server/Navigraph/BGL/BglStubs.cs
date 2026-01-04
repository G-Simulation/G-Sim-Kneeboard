using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Kneeboard_Server.Navigraph;

namespace Kneeboard_Server.Navigraph.BGL
{
    /// <summary>
    /// MSFS Version enum (stub for backward compatibility)
    /// </summary>
    public enum MsfsVersion
    {
        MSFS2020,
        MSFS2024
    }

    /// <summary>
    /// Stub for MsfsNavdataService (deprecated - use NavigraphDbService instead)
    /// </summary>
    public class MsfsNavdataService : IDisposable
    {
        public bool IsAvailable => false;
        public bool RequiresSimConnect => false;
        public string NavdataPath => "";
        public int SidCount => 0;
        public int StarCount => 0;
        public int IndexedAirportCount => 0;

        public MsfsNavdataService(MsfsVersion version) { }

        public static List<MsfsVersion> DetectInstalledVersions()
        {
            // Return empty list - we use Navigraph database now
            return new List<MsfsVersion>();
        }

        public void IndexNavdata() { }
        public ProcedureDetail GetProcedureDetail(string airport, string name, string transition, ProcedureType type) => null;
        public List<ProcedureSummary> GetSIDs(string airport) => new List<ProcedureSummary>();
        public List<ProcedureSummary> GetSTARs(string airport) => new List<ProcedureSummary>();
        public string TestAirport(string icao) => null;

        public void Dispose() { }
    }

    /// <summary>
    /// Stub for SimConnectFacilityService (deprecated)
    /// </summary>
    public class SimConnectFacilityService : IDisposable
    {
        public static bool IsFacilityApiAvailable => false;
        public bool IsConnected => false;

        // Nested types for compatibility
        public struct FixId : IEquatable<FixId>
        {
            public string Ident;
            public string Region;
            public int Type;
            public string Airport;

            public FixId(string ident, string region, int type)
            {
                Ident = ident;
                Region = region;
                Type = type;
                Airport = null;
            }

            public bool Equals(FixId other)
            {
                return Ident == other.Ident && Region == other.Region;
            }

            public override bool Equals(object obj)
            {
                return obj is FixId other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = 17;
                    hash = hash * 31 + (Ident?.GetHashCode() ?? 0);
                    hash = hash * 31 + (Region?.GetHashCode() ?? 0);
                    return hash;
                }
            }
        }

        public struct NavaidCoord
        {
            public double Latitude;
            public double Longitude;
            public string Icao;
            public string Region;
            public int Type;
        }

        public SimConnectFacilityService(IntPtr handle) { }

        public bool Connect() => false;
        public void Disconnect() { }
        public Task<List<ProcedureSummary>> GetSIDsAsync(string icao) => Task.FromResult(new List<ProcedureSummary>());
        public Task<List<ProcedureSummary>> GetSTARsAsync(string icao) => Task.FromResult(new List<ProcedureSummary>());
        public Task<ProcedureDetail> GetProcedureDetailAsync(string icao, string name, string transition, bool isSid) => Task.FromResult<ProcedureDetail>(null);
        public Task<ProcedureDetail> GetProcedureDetailAsync(string icao, string name, string transition, ProcedureType type) => Task.FromResult<ProcedureDetail>(null);
        public Task LoadAllAirportsAsync() => Task.CompletedTask;
        public Task LoadAllAirportsAsync(Action<int, int> progress, bool fullLoad) => Task.CompletedTask;
        public Task LoadAllAirportsAsync(NavdataDatabase db, IProgress<(string, int, int)> progress) => Task.CompletedTask;
        public Task LoadDebugAirportsAsync(List<string> icaos) => Task.CompletedTask;
        public Task LoadDebugAirportsAsync(List<string> icaos, Action<int, int> progress) => Task.CompletedTask;
        public Task LoadDebugAirportsAsync(NavdataDatabase db, IProgress<(string, int, int)> progress) => Task.CompletedTask;
        public List<object> GetWaypoints(string ident) => new List<object>();

        public void Dispose() { }
    }

    /// <summary>
    /// Stub for NavdataDatabase (deprecated - use NavigraphDbService instead)
    /// </summary>
    public class NavdataDatabase : IDisposable
    {
        public int ProcedureCount => 0;
        public int AirportCount => 0;

        public NavdataDatabase(string dataFolder) { }

        public void Initialize() { }
        public void RefreshCounts() { }
        public List<ProcedureSummary> GetSIDs(string icao) => new List<ProcedureSummary>();
        public List<ProcedureSummary> GetSTARs(string icao) => new List<ProcedureSummary>();
        public List<ApproachSummary> GetApproaches(string icao) => new List<ApproachSummary>();
        public List<RunwayInfo> GetRunways(string icao) => new List<RunwayInfo>();
        public List<ILSData> GetILS(string icao) => new List<ILSData>();
        public List<ProcedureLeg> GetProcedureLegs(string icao, string name, ProcedureType type) => new List<ProcedureLeg>();

        public void Dispose() { }
    }

    /// <summary>
    /// Procedure info stub (for compatibility) - inherits from ProcedureSummary
    /// </summary>
    public class ProcedureInfo : ProcedureSummary
    {
        public string Airport { get; set; }
        public List<string> Transitions { get; set; } = new List<string>();
    }

    /// <summary>
    /// ILS info stub (for compatibility)
    /// </summary>
    public class ILSInfo
    {
        public string Identifier { get; set; }
        public string Runway { get; set; }
        public double Frequency { get; set; }
        public double Course { get; set; }
    }
}
