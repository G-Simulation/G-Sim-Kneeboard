using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.FlightSimulator.SimConnect;

namespace Kneeboard_Server
{
    public class SimConnectManager : IDisposable
    {
        private SimConnect simConnect;
        private IntPtr windowHandle;
        private Thread reconnectThread;
        private volatile bool isConnected = false;
        private volatile bool isRunning = true;
        private const int WM_USER_SIMCONNECT = 0x0402;

        // Thread-safe Position Storage
        private readonly object dataLock = new object();
        private AircraftPosition? latestPosition = null;

        // Thread-safe SimConnect access (prevents race condition between UI and reconnect threads)
        private readonly object connectLock = new object();

        // Event für Verbindungsstatus-Änderungen
        public event Action<bool> ConnectionStatusChanged;

        // SimConnect Enums
        private enum DATA_REQUESTS { AIRCRAFT_POSITION, PAUSE_STATE }
        private enum DEFINITIONS { AircraftPosition, PauseState, TeleportPosition }
        private enum NOTIFICATION_GROUPS { GROUP0 }

        // Event IDs for commands
        private enum EVENTS
        {
            PAUSE_TOGGLE,
            SLEW_TOGGLE,
            SLEW_ON,
            SLEW_OFF,
            FREEZE_LATITUDE_LONGITUDE_TOGGLE,
            FREEZE_ALTITUDE_TOGGLE,
            FREEZE_ATTITUDE_TOGGLE,
            SIM_RATE_DECR,
            SIM_RATE_INCR,
            SIM_RATE_SET,
            COM1_RADIO_SET_HZ,
            COM1_RADIO_SWAP,
            COM2_RADIO_SET_HZ,
            COM2_RADIO_SWAP,
            NAV1_RADIO_SET_HZ,
            NAV1_RADIO_SWAP,
            NAV2_RADIO_SET_HZ,
            NAV2_RADIO_SWAP,
            ADF1_RADIO_SET,
            ADF1_RADIO_SWAP,
            ADF2_RADIO_SET,
            ADF2_RADIO_SWAP
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
        public struct AircraftPosition
        {
            public double Latitude;             // PLANE LATITUDE (degrees)
            public double Longitude;            // PLANE LONGITUDE (degrees)
            public double Altitude;             // PLANE ALTITUDE (feet)
            public double Heading;              // PLANE HEADING DEGREES TRUE (degrees)
            public double GroundSpeed;          // GROUND VELOCITY (knots)
            public double IndicatedAirspeed;    // AIRSPEED INDICATED (knots)
            public double WindDirection;        // AMBIENT WIND DIRECTION (degrees)
            public double WindSpeed;            // AMBIENT WIND VELOCITY (knots)
            public double CameraState;          // CAMERA STATE (2-6 = flight active)
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
        public struct PauseState
        {
            public int IsPaused;                // IS PAUSED (0 = not paused, 1 = paused)
        }

        // Separater Struct NUR für Teleport (nur setzbare Felder!)
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
        public struct TeleportPosition
        {
            public double Latitude;             // PLANE LATITUDE (degrees)
            public double Longitude;            // PLANE LONGITUDE (degrees)
            public double Altitude;             // PLANE ALTITUDE (feet)
            public double Heading;              // PLANE HEADING DEGREES TRUE (degrees)
        }

        public SimConnectManager(IntPtr handle)
        {
            windowHandle = handle;
        }

        public void Start()
        {
            isRunning = true;
            reconnectThread = new Thread(ReconnectionLoop);
            reconnectThread.IsBackground = true;
            reconnectThread.Start();
        }

        public void Stop()
        {
            isRunning = false;
            Disconnect();
            reconnectThread?.Join(2000);
        }

        private int reconnectAttempts = 0;

        private void ReconnectionLoop()
        {
            while (isRunning)
            {
                if (!isConnected)
                {
                    reconnectAttempts++;
                    Console.WriteLine($"[SimConnect] Connection attempt #{reconnectAttempts}...");
                    try
                    {
                        Connect();
                        Console.WriteLine($"[SimConnect] Connected successfully after {reconnectAttempts} attempt(s)");
                        reconnectAttempts = 0;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[SimConnect] Connection attempt #{reconnectAttempts} failed: {ex.Message}");
                    }
                }
                Thread.Sleep(5000); // Retry every 5 seconds
            }
        }

        private void Connect()
        {
            lock (connectLock)
            {
                try
                {
                    simConnect = new SimConnect("Kneeboard Server", windowHandle, WM_USER_SIMCONNECT, null, 0);

                    // Event handlers
                    simConnect.OnRecvOpen += OnRecvOpen;
                    simConnect.OnRecvQuit += OnRecvQuit;
                    simConnect.OnRecvException += OnRecvException;
                    simConnect.OnRecvSimobjectData += OnRecvSimobjectData;
                    // Note: OnRecvFacilityData not used - Little Navmap DB is primary source

                    Console.WriteLine("[SimConnect] Connecting to MSFS...");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SimConnect] Connect failed: {ex.Message}");
                    simConnect = null;
                    throw;
                }
            }
        }

        private void OnRecvOpen(SimConnect sender, SIMCONNECT_RECV_OPEN data)
        {
            Console.WriteLine("[SimConnect] Connected to " + data.szApplicationName);
            isConnected = true;
            ConnectionStatusChanged?.Invoke(true);
            SetupDataDefinitions();
        }

        private void SetupDataDefinitions()
        {
            // Define Aircraft Position structure
            simConnect.AddToDataDefinition(DEFINITIONS.AircraftPosition,
                "PLANE LATITUDE", "degrees", SIMCONNECT_DATATYPE.FLOAT64, 0, SimConnect.SIMCONNECT_UNUSED);
            simConnect.AddToDataDefinition(DEFINITIONS.AircraftPosition,
                "PLANE LONGITUDE", "degrees", SIMCONNECT_DATATYPE.FLOAT64, 0, SimConnect.SIMCONNECT_UNUSED);
            simConnect.AddToDataDefinition(DEFINITIONS.AircraftPosition,
                "PLANE ALTITUDE", "feet", SIMCONNECT_DATATYPE.FLOAT64, 0, SimConnect.SIMCONNECT_UNUSED);
            simConnect.AddToDataDefinition(DEFINITIONS.AircraftPosition,
                "PLANE HEADING DEGREES TRUE", "degrees", SIMCONNECT_DATATYPE.FLOAT64, 0, SimConnect.SIMCONNECT_UNUSED);
            simConnect.AddToDataDefinition(DEFINITIONS.AircraftPosition,
                "GROUND VELOCITY", "knots", SIMCONNECT_DATATYPE.FLOAT64, 0, SimConnect.SIMCONNECT_UNUSED);
            simConnect.AddToDataDefinition(DEFINITIONS.AircraftPosition,
                "AIRSPEED INDICATED", "knots", SIMCONNECT_DATATYPE.FLOAT64, 0, SimConnect.SIMCONNECT_UNUSED);
            simConnect.AddToDataDefinition(DEFINITIONS.AircraftPosition,
                "AMBIENT WIND DIRECTION", "degrees", SIMCONNECT_DATATYPE.FLOAT64, 0, SimConnect.SIMCONNECT_UNUSED);
            simConnect.AddToDataDefinition(DEFINITIONS.AircraftPosition,
                "AMBIENT WIND VELOCITY", "knots", SIMCONNECT_DATATYPE.FLOAT64, 0, SimConnect.SIMCONNECT_UNUSED);
            simConnect.AddToDataDefinition(DEFINITIONS.AircraftPosition,
                "CAMERA STATE", "enum", SIMCONNECT_DATATYPE.FLOAT64, 0, SimConnect.SIMCONNECT_UNUSED);

            simConnect.RegisterDataDefineStruct<AircraftPosition>(DEFINITIONS.AircraftPosition);

            // Define Teleport Position structure (nur setzbare Felder!)
            simConnect.AddToDataDefinition(DEFINITIONS.TeleportPosition,
                "PLANE LATITUDE", "degrees", SIMCONNECT_DATATYPE.FLOAT64, 0, SimConnect.SIMCONNECT_UNUSED);
            simConnect.AddToDataDefinition(DEFINITIONS.TeleportPosition,
                "PLANE LONGITUDE", "degrees", SIMCONNECT_DATATYPE.FLOAT64, 0, SimConnect.SIMCONNECT_UNUSED);
            simConnect.AddToDataDefinition(DEFINITIONS.TeleportPosition,
                "PLANE ALTITUDE", "feet", SIMCONNECT_DATATYPE.FLOAT64, 0, SimConnect.SIMCONNECT_UNUSED);
            simConnect.AddToDataDefinition(DEFINITIONS.TeleportPosition,
                "PLANE HEADING DEGREES TRUE", "degrees", SIMCONNECT_DATATYPE.FLOAT64, 0, SimConnect.SIMCONNECT_UNUSED);

            simConnect.RegisterDataDefineStruct<TeleportPosition>(DEFINITIONS.TeleportPosition);

            // Request data every second
            simConnect.RequestDataOnSimObject(DATA_REQUESTS.AIRCRAFT_POSITION,
                DEFINITIONS.AircraftPosition, SimConnect.SIMCONNECT_OBJECT_ID_USER,
                SIMCONNECT_PERIOD.SECOND, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);

            // Map Events for commands
            simConnect.MapClientEventToSimEvent(EVENTS.PAUSE_TOGGLE, "PAUSE_TOGGLE");
            simConnect.MapClientEventToSimEvent(EVENTS.SLEW_TOGGLE, "SLEW_TOGGLE");
            simConnect.MapClientEventToSimEvent(EVENTS.SLEW_ON, "SLEW_ON");
            simConnect.MapClientEventToSimEvent(EVENTS.SLEW_OFF, "SLEW_OFF");
            simConnect.MapClientEventToSimEvent(EVENTS.FREEZE_LATITUDE_LONGITUDE_TOGGLE, "FREEZE_LATITUDE_LONGITUDE_TOGGLE");
            simConnect.MapClientEventToSimEvent(EVENTS.FREEZE_ALTITUDE_TOGGLE, "FREEZE_ALTITUDE_TOGGLE");
            simConnect.MapClientEventToSimEvent(EVENTS.FREEZE_ATTITUDE_TOGGLE, "FREEZE_ATTITUDE_TOGGLE");
            simConnect.MapClientEventToSimEvent(EVENTS.SIM_RATE_DECR, "SIM_RATE_DECR");
            simConnect.MapClientEventToSimEvent(EVENTS.SIM_RATE_INCR, "SIM_RATE_INCR");
            simConnect.MapClientEventToSimEvent(EVENTS.SIM_RATE_SET, "SIM_RATE_SET");
            simConnect.MapClientEventToSimEvent(EVENTS.COM1_RADIO_SET_HZ, "COM_STBY_RADIO_SET_HZ");
            simConnect.MapClientEventToSimEvent(EVENTS.COM1_RADIO_SWAP, "COM_RADIO_SWAP");
            simConnect.MapClientEventToSimEvent(EVENTS.COM2_RADIO_SET_HZ, "COM2_STBY_RADIO_SET_HZ");
            simConnect.MapClientEventToSimEvent(EVENTS.COM2_RADIO_SWAP, "COM2_RADIO_SWAP");
            simConnect.MapClientEventToSimEvent(EVENTS.NAV1_RADIO_SET_HZ, "NAV1_STBY_SET_HZ");
            simConnect.MapClientEventToSimEvent(EVENTS.NAV1_RADIO_SWAP, "NAV1_RADIO_SWAP");
            simConnect.MapClientEventToSimEvent(EVENTS.NAV2_RADIO_SET_HZ, "NAV2_STBY_SET_HZ");
            simConnect.MapClientEventToSimEvent(EVENTS.NAV2_RADIO_SWAP, "NAV2_RADIO_SWAP");
            simConnect.MapClientEventToSimEvent(EVENTS.ADF1_RADIO_SET, "ADF_SET");
            simConnect.MapClientEventToSimEvent(EVENTS.ADF1_RADIO_SWAP, "ADF1_RADIO_SWAP");
            simConnect.MapClientEventToSimEvent(EVENTS.ADF2_RADIO_SET, "ADF2_SET");
            simConnect.MapClientEventToSimEvent(EVENTS.ADF2_RADIO_SWAP, "ADF2_RADIO_SWAP");

            Console.WriteLine("[SimConnect] Data subscription configured (1 Hz)");
        }

        // Public Command Methods

        /// <summary>
        /// Teleportiert das Flugzeug zu den angegebenen Koordinaten (synchroner Wrapper)
        /// </summary>
        public void Teleport(double lat, double lng, double? altitude = null, double? heading = null, double? speed = null)
        {
            // Fire-and-forget: Startet den Teleport asynchron ohne zu blockieren
            _ = TeleportAsync(lat, lng, altitude, heading, speed);
        }

        /// <summary>
        /// Teleportiert das Flugzeug zu den angegebenen Koordinaten (async Implementierung)
        /// </summary>
        private async System.Threading.Tasks.Task TeleportAsync(double lat, double lng, double? altitude = null, double? heading = null, double? speed = null)
        {
            if (!isConnected || simConnect == null) return;

            try
            {
                // Aktuelle Position für Fallback-Werte
                double currentAlt = 0.0;
                double currentHdg = 0.0;
                lock (dataLock)
                {
                    if (latestPosition.HasValue)
                    {
                        currentAlt = latestPosition.Value.Altitude;
                        currentHdg = latestPosition.Value.Heading;
                    }
                }

                // Wenn keine Altitude angegeben, aktuelle behalten
                double targetAlt = altitude ?? currentAlt;
                // Wenn kein Heading angegeben, aktuelles behalten
                double targetHdg = heading ?? currentHdg;

                Console.WriteLine($"[SimConnect] Teleport request: {lat:F6}, {lng:F6}, Alt={targetAlt:F0}ft, Hdg={targetHdg:F0}°");

                // 1. SLEW MODE aktivieren (verhindert Fallen/Physik)
                simConnect.TransmitClientEvent(
                    SimConnect.SIMCONNECT_OBJECT_ID_USER,
                    EVENTS.SLEW_ON,
                    0,
                    NOTIFICATION_GROUPS.GROUP0,
                    SIMCONNECT_EVENT_FLAG.GROUPID_IS_PRIORITY);
                Console.WriteLine("[SimConnect] SLEW_ON sent");

                // 2. Warte kurz damit SLEW aktiv wird
                await System.Threading.Tasks.Task.Delay(100);

                // Null-Check nach dem Delay (Verbindung könnte unterbrochen worden sein)
                if (simConnect == null)
                {
                    Console.WriteLine("[SimConnect] Connection lost during teleport - aborting");
                    return;
                }

                // 3. Position setzen
                simConnect.SetDataOnSimObject(DEFINITIONS.TeleportPosition,
                    SimConnect.SIMCONNECT_OBJECT_ID_USER,
                    SIMCONNECT_DATA_SET_FLAG.DEFAULT,
                    new TeleportPosition
                    {
                        Latitude = lat,
                        Longitude = lng,
                        Altitude = targetAlt,
                        Heading = targetHdg
                    });
                Console.WriteLine($"[SimConnect] Position set: {lat:F6}, {lng:F6}, Alt={targetAlt:F0}ft, Hdg={targetHdg:F0}°");

                // 4. Warte damit Position übernommen wird
                await System.Threading.Tasks.Task.Delay(500);

                // Null-Check nach dem Delay
                if (simConnect == null)
                {
                    Console.WriteLine("[SimConnect] Connection lost during teleport - skipping SLEW_OFF");
                    return;
                }

                // 5. SLEW MODE deaktivieren
                simConnect.TransmitClientEvent(
                    SimConnect.SIMCONNECT_OBJECT_ID_USER,
                    EVENTS.SLEW_OFF,
                    0,
                    NOTIFICATION_GROUPS.GROUP0,
                    SIMCONNECT_EVENT_FLAG.GROUPID_IS_PRIORITY);
                Console.WriteLine("[SimConnect] SLEW_OFF sent");
            }
            catch (ObjectDisposedException)
            {
                Console.WriteLine("[SimConnect] Teleport aborted - connection was closed");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SimConnect] Teleport error: {ex.Message}");
            }
        }

        /// <summary>
        /// Setzt die Radio-Frequenz (synchroner Wrapper)
        /// </summary>
        public void SetRadioFrequency(string radio, uint frequencyHz)
        {
            // Fire-and-forget: Startet die Operation asynchron ohne zu blockieren
            _ = SetRadioFrequencyAsync(radio, frequencyHz);
        }

        /// <summary>
        /// Setzt die Radio-Frequenz (async Implementierung - vermeidet Thread.Sleep)
        /// </summary>
        private async System.Threading.Tasks.Task SetRadioFrequencyAsync(string radio, uint frequencyHz)
        {
            if (!isConnected || simConnect == null) return;

            try
            {
                EVENTS setEvent, swapEvent;

                switch (radio.ToUpper())
                {
                    case "COM1_ACTIVE":
                    case "COM1_STANDBY":
                        setEvent = EVENTS.COM1_RADIO_SET_HZ;
                        swapEvent = EVENTS.COM1_RADIO_SWAP;
                        break;
                    case "COM2_ACTIVE":
                    case "COM2_STANDBY":
                        setEvent = EVENTS.COM2_RADIO_SET_HZ;
                        swapEvent = EVENTS.COM2_RADIO_SWAP;
                        break;
                    case "NAV1_ACTIVE":
                    case "NAV1_STANDBY":
                        setEvent = EVENTS.NAV1_RADIO_SET_HZ;
                        swapEvent = EVENTS.NAV1_RADIO_SWAP;
                        break;
                    case "NAV2_ACTIVE":
                    case "NAV2_STANDBY":
                        setEvent = EVENTS.NAV2_RADIO_SET_HZ;
                        swapEvent = EVENTS.NAV2_RADIO_SWAP;
                        break;
                    case "ADF1_ACTIVE":
                    case "ADF1_STANDBY":
                        setEvent = EVENTS.ADF1_RADIO_SET;
                        swapEvent = EVENTS.ADF1_RADIO_SWAP;
                        break;
                    case "ADF2_ACTIVE":
                    case "ADF2_STANDBY":
                        setEvent = EVENTS.ADF2_RADIO_SET;
                        swapEvent = EVENTS.ADF2_RADIO_SWAP;
                        break;
                    default:
                        Console.WriteLine($"[SimConnect] Unknown radio: {radio}");
                        return;
                }

                // Set standby frequency
                simConnect.TransmitClientEvent(SimConnect.SIMCONNECT_OBJECT_ID_USER,
                    setEvent, frequencyHz,
                    NOTIFICATION_GROUPS.GROUP0,
                    SIMCONNECT_EVENT_FLAG.DEFAULT);

                // Swap to active if needed
                if (radio.EndsWith("_ACTIVE"))
                {
                    // Async delay statt Thread.Sleep - blockiert keinen ThreadPool-Thread
                    await System.Threading.Tasks.Task.Delay(50);

                    // Null-Check nach dem Delay
                    if (simConnect == null) return;

                    simConnect.TransmitClientEvent(SimConnect.SIMCONNECT_OBJECT_ID_USER,
                        swapEvent, 1,
                        NOTIFICATION_GROUPS.GROUP0,
                        SIMCONNECT_EVENT_FLAG.DEFAULT);
                }

                Console.WriteLine($"[SimConnect] Set {radio} to {frequencyHz} Hz");
            }
            catch (ObjectDisposedException)
            {
                Console.WriteLine("[SimConnect] SetRadioFrequency aborted - connection was closed");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SimConnect] SetRadioFrequency error: {ex.Message}");
            }
        }

        private void OnRecvSimobjectData(SimConnect sender, SIMCONNECT_RECV_SIMOBJECT_DATA data)
        {
            if (data.dwRequestID == (uint)DATA_REQUESTS.AIRCRAFT_POSITION)
            {
                var position = (AircraftPosition)data.dwData[0];

                lock (dataLock)
                {
                    latestPosition = position;
                }
            }
        }

        private void OnRecvQuit(SimConnect sender, SIMCONNECT_RECV data)
        {
            Console.WriteLine("[SimConnect] Simulator closed");
            Disconnect();
        }

        private void OnRecvException(SimConnect sender, SIMCONNECT_RECV_EXCEPTION data)
        {
            Console.WriteLine($"[SimConnect] Exception: {(SIMCONNECT_EXCEPTION)data.dwException}");
        }

        private void Disconnect()
        {
            bool wasConnected;
            lock (connectLock)
            {
                if (simConnect != null)
                {
                    // WICHTIG: Event-Handler abmelden VOR Dispose um Handler-Verdopplung bei Reconnects zu verhindern
                    try
                    {
                        simConnect.OnRecvOpen -= OnRecvOpen;
                        simConnect.OnRecvQuit -= OnRecvQuit;
                        simConnect.OnRecvException -= OnRecvException;
                        simConnect.OnRecvSimobjectData -= OnRecvSimobjectData;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[SimConnect] Handler removal warning: {ex.Message}");
                    }

                    simConnect.Dispose();
                    simConnect = null;
                }
                wasConnected = isConnected;
                isConnected = false;
                latestPosition = null;
            }
            // Event außerhalb des Locks aufrufen um Deadlocks zu vermeiden
            if (wasConnected)
            {
                ConnectionStatusChanged?.Invoke(false);
            }
        }

        public void HandleWindowMessage(ref Message m)
        {
            if (m.Msg == WM_USER_SIMCONNECT)
            {
                lock (connectLock)
                {
                    if (simConnect != null)
                    {
                        try
                        {
                            simConnect.ReceiveMessage();
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[SimConnect] Message handling error: {ex.Message}");
                        }
                    }
                }
            }
        }

        // Public API
        public bool IsConnected => isConnected;

        public AircraftPosition? GetLatestPosition()
        {
            lock (dataLock)
            {
                return latestPosition;
            }
        }

        public bool IsFlightLoaded()
        {
            var pos = GetLatestPosition();
            if (!pos.HasValue) return false;
            var state = (int)pos.Value.CameraState;
            // CameraState 2-6 = active flight (cockpit, external, drone, etc.)
            return state >= 2 && state <= 6;
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
