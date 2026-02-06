using System;
using System.Collections.Generic;
using Kneeboard_Server.Logging;

namespace Kneeboard_Server.Navigraph
{
    /// <summary>
    /// Device Authorization Response from Navigraph OAuth
    /// </summary>
    public class DeviceCodeResponse
    {
        public string DeviceCode { get; set; }
        public string UserCode { get; set; }
        public string VerificationUri { get; set; }
        public string VerificationUriComplete { get; set; }
        public int ExpiresIn { get; set; }
        public int Interval { get; set; }
    }

    /// <summary>
    /// Token Response from Navigraph OAuth
    /// </summary>
    public class TokenResponse
    {
        public string AccessToken { get; set; }
        public string RefreshToken { get; set; }
        public string TokenType { get; set; }
        public int ExpiresIn { get; set; }
        public string Scope { get; set; }
    }

    /// <summary>
    /// Navigraph Subscription Info
    /// </summary>
    public class SubscriptionInfo
    {
        public bool HasFmsData { get; set; }
        public bool HasCharts { get; set; }
        public string SubscriptionType { get; set; }
        public DateTime? ExpiryDate { get; set; }
    }

    /// <summary>
    /// Navdata Package Info from Navigraph API
    /// </summary>
    public class NavdataPackage
    {
        public string PackageId { get; set; }
        public string Cycle { get; set; }
        public string Revision { get; set; }
        public string Format { get; set; }
        public DateTime EffectiveDate { get; set; }
        public DateTime ExpirationDate { get; set; }
        public string DownloadUrl { get; set; }
        public long FileSize { get; set; }
        public string Hash { get; set; }
        public string PackageStatus { get; set; }
    }

    /// <summary>
    /// Airport Information from DFD v2
    /// </summary>
    public class AirportInfo
    {
        public string Icao { get; set; }
        public string Iata { get; set; }
        public string Name { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public int Elevation { get; set; }
        public int TransitionAltitude { get; set; }
        public int TransitionLevel { get; set; }
        public string SpeedLimit { get; set; }
        public int SpeedLimitAltitude { get; set; }
        public string IfrCapability { get; set; }
        public string LongestRunway { get; set; }
        public List<RunwayInfo> Runways { get; set; }
        public List<FrequencyInfo> Frequencies { get; set; }
    }

    /// <summary>
    /// Runway Information from DFD v2
    /// </summary>
    public class RunwayInfo
    {
        public string Identifier { get; set; }          // "25C"
        public double ThresholdLat { get; set; }        // Threshold Latitude
        public double ThresholdLon { get; set; }        // Threshold Longitude
        public double Heading { get; set; }             // Magnetic Bearing
        public double TrueHeading { get; set; }         // True Bearing (for calculations)
        public int Length { get; set; }                 // Length in feet
        public int Width { get; set; }                  // Width in feet
        public string Surface { get; set; }             // Surface type
        public int ThresholdElevation { get; set; }     // Elevation in feet
        public double GlidePathAngle { get; set; }      // ILS glidepath angle
        public int ThresholdDisplacement { get; set; } // Displaced threshold in feet

        // Calculated end coordinates
        public double EndLat { get; set; }
        public double EndLon { get; set; }

        /// <summary>
        /// Calculate the runway end coordinates from threshold and TRUE heading
        /// </summary>
        public void CalculateEndCoordinates()
        {
            // Convert length from feet to nautical miles
            double lengthNm = Length / 6076.12;

            // Use TRUE heading for calculation (not magnetic!)
            double headingToUse = TrueHeading > 0 ? TrueHeading : Heading;

            KneeboardLogger.NavigraphDebug($"{Identifier}: Threshold=({ThresholdLat:F6},{ThresholdLon:F6}), MagHdg={Heading:F1}, TrueHdg={TrueHeading:F1}, Using={headingToUse:F1}, Length={Length}ft");

            // Calculate end point using TRUE heading
            var endPoint = CalculateDestination(ThresholdLat, ThresholdLon, headingToUse, lengthNm);
            EndLat = endPoint.Item1;
            EndLon = endPoint.Item2;

            KneeboardLogger.NavigraphDebug($"{Identifier}: Calculated End=({EndLat:F6},{EndLon:F6})");
        }

        private static (double, double) CalculateDestination(double lat, double lon, double bearing, double distanceNm)
        {
            const double EarthRadiusNm = 3440.065; // Earth radius in nautical miles

            double latRad = lat * Math.PI / 180;
            double lonRad = lon * Math.PI / 180;
            double bearingRad = bearing * Math.PI / 180;
            double distRatio = distanceNm / EarthRadiusNm;

            double newLatRad = Math.Asin(
                Math.Sin(latRad) * Math.Cos(distRatio) +
                Math.Cos(latRad) * Math.Sin(distRatio) * Math.Cos(bearingRad)
            );

            double newLonRad = lonRad + Math.Atan2(
                Math.Sin(bearingRad) * Math.Sin(distRatio) * Math.Cos(latRad),
                Math.Cos(distRatio) - Math.Sin(latRad) * Math.Sin(newLatRad)
            );

            return (newLatRad * 180 / Math.PI, newLonRad * 180 / Math.PI);
        }
    }

    /// <summary>
    /// ILS/LOC/GS Data from DFD v2
    /// </summary>
    public class ILSData
    {
        public string Identifier { get; set; }
        public string RunwayIdentifier { get; set; }
        public double Frequency { get; set; }
        public double LocalizerBearing { get; set; }
        public double GlideSlopeAngle { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string Category { get; set; }
    }

    /// <summary>
    /// Frequency Information (Tower, Ground, ATIS, etc.)
    /// </summary>
    public class FrequencyInfo
    {
        public string Type { get; set; }
        public string Name { get; set; }
        public double Frequency { get; set; }
    }

    /// <summary>
    /// Waypoint Information from DFD v2
    /// </summary>
    public class WaypointInfo
    {
        public string Identifier { get; set; }
        public string Name { get; set; }
        public string Region { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string Type { get; set; }
    }

    /// <summary>
    /// Navaid (VOR, NDB, DME) Information
    /// </summary>
    public class NavaidInfo
    {
        public string Identifier { get; set; }
        public string Name { get; set; }
        public string Type { get; set; }
        public double Frequency { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string Region { get; set; }
        public int Elevation { get; set; }
        public double MagneticVariation { get; set; }
    }

    /// <summary>
    /// Procedure Summary (SID/STAR)
    /// </summary>
    public class ProcedureSummary
    {
        public string Identifier { get; set; }
        public string Name { get; set; }
        public string Runway { get; set; }
        public string RouteType { get; set; }  // ARINC 424 route_type (4,5,6 for SID; 1-6 for STAR)
        public string TransitionIdentifier { get; set; }
        public ProcedureType Type { get; set; }
    }

    /// <summary>
    /// Approach Summary
    /// </summary>
    public class ApproachSummary
    {
        public string Identifier { get; set; }
        public string Type { get; set; }          // ILS, RNAV, VOR, etc.
        public string Runway { get; set; }
        public string Suffix { get; set; }        // Y, Z, etc.
        public List<string> Transitions { get; set; }
    }

    /// <summary>
    /// Procedure Leg from DFD v2
    /// </summary>
    public class ProcedureLeg
    {
        public int SequenceNumber { get; set; }
        public string WaypointIdentifier { get; set; }
        public string Identifier { get => WaypointIdentifier; set => WaypointIdentifier = value; } // Alias
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string PathTerminator { get; set; }  // TF, DF, CF, RF, etc.
        public string PathTermination { get => PathTerminator; set => PathTerminator = value; } // Alias
        public double? TurnDirection { get; set; }
        public string AltitudeConstraint { get; set; }  // AT, ABOVE, BELOW
        public string AltitudeDescription { get => AltitudeConstraint; set => AltitudeConstraint = value; } // Alias
        public int? Altitude1 { get; set; }
        public int? Altitude2 { get; set; }
        public int? SpeedLimit { get; set; }
        public string SpeedConstraint { get; set; }
        public double? Course { get; set; }
        public double? MagneticCourse { get => Course; set => Course = value; } // Alias
        public double? Distance { get; set; }
        public double? RouteDistance { get => Distance; set => Distance = value; } // Alias
        public bool Overfly { get; set; }
        public bool IsFlyOver { get => Overfly; set => Overfly = value; } // Alias
        
        // ARINC 424 fields for transition filtering
        public string RouteType { get; set; }  // ARINC 424 route_type (1-6)
        public string TransitionIdentifier { get; set; }  // transition_identifier from database
    }

    /// <summary>
    /// Airway Information
    /// </summary>
    public class AirwayInfo
    {
        public string Identifier { get; set; }
        public string Type { get; set; }          // HIGH, LOW, BOTH
        public List<AirwayLeg> Legs { get; set; }
    }

    /// <summary>
    /// Airway Leg
    /// </summary>
    public class AirwayLeg
    {
        public int SequenceNumber { get; set; }
        public string WaypointIdentifier { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public int? MinimumAltitude { get; set; }
        public int? MaximumAltitude { get; set; }
        public string Direction { get; set; }
    }

    /// <summary>
    /// Procedure Type Enum
    /// </summary>
    public enum ProcedureType
    {
        SID,
        STAR,
        Approach
    }

    /// <summary>
    /// Navigraph Authentication Status
    /// </summary>
    public class NavigraphStatus
    {
        public bool IsAuthenticated { get; set; }
        public bool Authenticated { get => IsAuthenticated; set => IsAuthenticated = value; } // Alias for backward compatibility
        public string Username { get; set; }
        public bool HasFmsDataSubscription { get; set; }
        public string CurrentAiracCycle { get; set; }
        public DateTime? DatabaseDate { get; set; }
        public bool IsUsingBundledDatabase { get; set; }
        public string DatabasePath { get; set; }
        /// <summary>
        /// Whether navigation data is available (bundled or subscription)
        /// </summary>
        public bool IsDataAvailable { get; set; }
    }

    /// <summary>
    /// Procedure Detail with waypoints (for SID/STAR/Approach)
    /// </summary>
    public class ProcedureDetail
    {
        public string Identifier { get; set; }
        public string Airport { get; set; }
        public string Runway { get; set; }
        public string Transition { get; set; }
        public ProcedureType Type { get; set; }
        public string AiracCycle { get; set; }
        public List<ProcedureWaypoint> Waypoints { get; set; } = new List<ProcedureWaypoint>();
    }

    /// <summary>
    /// Waypoint in a procedure
    /// </summary>
    public class ProcedureWaypoint
    {
        public string Identifier { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string PathTerminator { get; set; }
        public string PathTermination { get => PathTerminator; set => PathTerminator = value; } // Alias
        public string AltitudeConstraint { get; set; }
        public string AltitudeDescription { get => AltitudeConstraint; set => AltitudeConstraint = value; } // Alias
        public int? Altitude1 { get; set; }
        public int? Altitude2 { get; set; }
        public int? SpeedLimit { get; set; }
        public double? Course { get; set; }
        public double? MagneticCourse { get => Course; set => Course = value; } // Alias
        public double? Distance { get; set; }
        public double? RouteDistance { get => Distance; set => Distance = value; } // Alias
        public double? TurnDirection { get; set; }
        public bool Overfly { get; set; }
        public bool IsFlyOver { get => Overfly; set => Overfly = value; } // Alias
        public int SequenceNumber { get; set; }
        public string RouteType { get; set; }
        public string TransitionIdentifier { get; set; }
    }

    /// <summary>
    /// SID List Response
    /// </summary>
    public class SIDListResponse
    {
        public string Airport { get; set; }
        public List<ProcedureSummary> Sids { get; set; }
        public string AiracCycle { get; set; }
        public string Source { get; set; }
    }

    /// <summary>
    /// STAR List Response
    /// </summary>
    public class STARListResponse
    {
        public string Airport { get; set; }
        public List<ProcedureSummary> Stars { get; set; }
        public string AiracCycle { get; set; }
        public string Source { get; set; }
    }

    /// <summary>
    /// Approach List Response
    /// </summary>
    public class ApproachListResponse
    {
        public string Airport { get; set; }
        public List<ApproachSummary> Approaches { get; set; }
        public string AiracCycle { get; set; }
        public string Source { get; set; }
    }
}
