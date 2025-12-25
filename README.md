# G-Sim Kneeboard Server

HTTP Server für das G-Sim Kneeboard Add-on für Microsoft Flight Simulator 2020/2024.

## Beschreibung

Der Kneeboard Server ist eine Windows-Anwendung, die einen lokalen HTTP-Server auf Port 815 bereitstellt. Er versorgt das Kneeboard EFB Add-on mit Kartendaten, Flughafeninformationen, Waypoints und Simbrief-Flugplänen.

### Features

- **HTTP Server** auf `localhost:815`
- **Flughafen-Datenbank** mit weltweiten Flughäfen
- **Waypoint-Verwaltung** für Navigation
- **Simbrief-Integration** für Flugplanung
- **SID/STAR Procedure-Daten** für detaillierte Flugpfad-Darstellung
- **Echtzeit-Kommunikation** mit MSFS via SimConnect
- **Karten-Visualisierung** für das Kneeboard
- **Singleton-Anwendung** (nur eine Instanz gleichzeitig)

## SID/STAR Navdata Integration

Der Kneeboard Server kann detaillierte SID/STAR-Waypoints in der Karte darstellen. Die Waypoints werden farblich unterschieden:

- **Grün**: Departure/SID-Waypoints
- **Blau**: Enroute-Waypoints
- **Gelb**: Arrival/STAR-Waypoints

### Navdata-Datenbank erstellen

Die Navdata werden direkt aus dem MSFS-Installationsordner gelesen. **Wenn du Navigraph in MSFS installiert hast, werden automatisch die aktuellen AIRAC-Daten verwendet!**

**Einrichtung:**

1. Öffne den Kneeboard Server
2. Klicke auf das **Info-Symbol (i)** oben rechts
3. Der **MSFS Packages-Ordner** wird automatisch erkannt
   - Falls nicht: Klicke auf das Textfeld und wähle den Ordner manuell
   - Der Ordner enthält typischerweise `Official` und `Community` Unterordner
4. Klicke auf **"Navdata Datenbank erstellen"**
5. Warte bis die Datenbank erstellt wurde

**AIRAC-Updates:**

Wenn du Navigraph aktualisierst, zeigt der Kneeboard Server automatisch eine Meldung, dass ein AIRAC-Update verfügbar ist. Klicke dann erneut auf "Navdata Datenbank erstellen" um die neuen Daten zu laden.

**Datenquellen (Priorität):**

1. **Navigraph** (wenn in MSFS Community-Ordner installiert) - Aktuelle AIRAC-Daten
2. **Standard MSFS Navdata** - Basis-Navdata des Simulators

### SimBrief Fallback

Ohne erstellte Navdata-Datenbank werden die vereinfachten Waypoints aus dem SimBrief-Flugplan verwendet. Diese werden anhand der `stage`-Information (CLB/CRZ/DSC) farblich markiert.

### API Endpoints

| Endpoint | Beschreibung |
|----------|--------------|
| `GET /api/procedures/status` | Status der Navdata-Verbindung |
| `GET /api/procedures/sids/{icao}` | Liste aller SIDs eines Flughafens |
| `GET /api/procedures/stars/{icao}` | Liste aller STARs eines Flughafens |
| `GET /api/procedures/procedure/{icao}/{name}?type=sid\|star` | Details einer Procedure |

## Systemanforderungen

- Windows 10/11
- .NET Framework 4.8
- Administrator-Rechte (wird automatisch angefordert)
- Microsoft Flight Simulator 2020 oder 2024

## Installation

### Option 1: Installer verwenden

1. Führe das Setup aus `Kneeboard Server Setup\Debug` oder `Release` aus
2. Der Server wird beim Start automatisch mit Admin-Rechten gestartet

### Option 2: Von Source kompilieren

1. Öffne `Kneeboard Server.sln` in Visual Studio 2017 oder neuer
2. Build Configuration: `Debug` oder `Release`
3. Platform: `x64` empfohlen
4. Build Solution

## Verwendung

1. Starte den Kneeboard Server
2. Die Anwendung läuft im Hintergrund
3. Der HTTP-Server ist unter `http://localhost:815` erreichbar
4. Starte MSFS und das Kneeboard EFB Add-on
5. Das Kneeboard verbindet sich automatisch mit dem Server

## Projektstruktur

```
Kneeboard Server/
├── Kneeboard Server/          # Hauptanwendung
│   ├── Program.cs              # Entry Point
│   ├── KneeboardServer.cs      # Server-Logik
│   ├── SimpleHTTPServer.cs     # HTTP Server
│   ├── Airports.cs             # Flughafen-Datenbank
│   ├── Waypoints.cs            # Waypoint-Verwaltung
│   ├── Simbrief.cs             # Simbrief-Integration
│   └── bin/                    # Kompilierte Binaries
└── Kneeboard Server Setup/     # Installer-Projekt
    ├── Debug/                  # Debug Installer
    └── Release/                # Release Installer
```

## Technologie-Stack

- C# / .NET Framework 4.8
- Windows Forms
- HTTP Server (Port 815)
- JSON für Datenaustausch

## Entwicklung

### Build-Konfigurationen

- **Debug|x64**: Entwicklung mit Debug-Symbolen
- **Release|x64**: Optimierte Production-Version

### Voraussetzungen

- Visual Studio 2017 oder neuer
- .NET Framework 4.8 SDK
- Windows SDK

## Port

Der Server verwendet Port **815**. Stelle sicher, dass dieser Port nicht von anderen Anwendungen verwendet wird.

## Version

Aktuelle Version: 2.0.0

## Lizenz

MIT License - Copyright (c) 2021-2025 G-Simulations / Patrick Moses

Siehe [LICENSE](LICENSE) für Details.

## Autor

Moses / Gsimulations
Website: https://www.gsimulations.com

## Hinweise

- Die Anwendung benötigt Admin-Rechte für Netzwerk-Zugriff
- Es kann nur eine Instanz gleichzeitig laufen
- Firewall-Ausnahme wird beim ersten Start abgefragt
