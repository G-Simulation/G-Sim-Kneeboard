# G-Sim Kneeboard

Interaktives Kneeboard Add-on für Microsoft Flight Simulator 2020/2024 mit lokalem HTTP-Server.

## Beschreibung

G-Sim Kneeboard besteht aus zwei Komponenten:

- **Kneeboard Server** - Eine Windows-Anwendung, die einen lokalen HTTP-Server auf Port 815 bereitstellt. Er versorgt das Kneeboard Add-on mit Kartendaten, Flughafeninformationen, Waypoints und Simbrief-Flugplänen.
- **Kneeboard Panel** - Eine Toolbar- und EFB-Anwendung (Electronic Flight Bag) für MSFS, die ein interaktives Kneeboard im Simulator anzeigt und Echtzeit-Flugdaten überträgt.

### Features

**Server:**
- HTTP Server auf `localhost:815`
- Flughafen-Datenbank mit weltweiten Flughäfen
- Waypoint-Verwaltung für Navigation
- Simbrief-Integration für Flugplanung
- SID/STAR Procedure-Daten für detaillierte Flugpfad-Darstellung
- Echtzeit-Kommunikation mit MSFS via SimConnect
- Karten-Visualisierung
- Singleton-Anwendung (nur eine Instanz gleichzeitig)

**Panel:**
- Kompatibel mit MSFS 2020 und MSFS 2024
- Verfügbar als Toolbar-Panel und EFB-App (MSFS 2024)
- Echtzeit-Übertragung von Flugzeugdaten:
  - Position (Latitude/Longitude)
  - Höhe (Altitude)
  - Kurs (Heading)
  - Geschwindigkeit (Airspeed)
  - Windrichtung und -geschwindigkeit
- Teleport-Funktion über die Karte
- Automatische Reconnection bei Verbindungsabbruch

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
4. Starte MSFS und das Kneeboard Add-on
5. Das Kneeboard verbindet sich automatisch mit dem Server

## SID/STAR Navdata Integration

Der Kneeboard Server kann detaillierte SID/STAR-Waypoints in der Karte darstellen. Die Waypoints werden farblich unterschieden:

- **Rot**: SID/Departure-Waypoints
- **Lila**: Route/Enroute-Waypoints
- **Grün**: Arrival/STAR-Waypoints
- **Orange**: Approach-Waypoints
- **Blau**: Alternate-Waypoints

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

## Kneeboard Panel Entwicklung

### Build-Befehle

```bash
cd Kneeboard/PackageSources/vendor/Kneeboard
npm install
npm run build
```

| Befehl | Beschreibung |
|--------|--------------|
| `npm run build` | Projekt einmalig bauen |
| `npm run watch` | Watch-Mode für Entwicklung |
| `npm run rebuild` | Dist löschen und neu bauen |
| `npm run clean` | Dist und node_modules löschen |

### Panel-Konfiguration

Die `.env` Datei im Kneeboard-Ordner ermöglicht folgende Einstellungen:

- `APP_ID` - App-Identifier (Standard: `Kneeboard`)
- `APP_VIEW_CLASS` - CSS-Klasse der View (Standard: `GsimKneeboard`)
- `API_PROXY_URL` - URL des Kneeboard-Servers (Standard: `http://localhost:815`)
- `TYPECHECKING` - TypeScript-Prüfung aktivieren (`true`/`false`)
- `SOURCE_MAPS` - Source Maps generieren (`true`/`false`)
- `MINIFY` - Output minimieren (`true`/`false`)

## Projektstruktur

```
G-Sim Kneeboard/
├── Kneeboard Server/              # Server-Anwendung
│   ├── Program.cs                  # Entry Point
│   ├── KneeboardServer.cs         # Server-Logik
│   ├── SimpleHTTPServer.cs        # HTTP Server
│   ├── Airports.cs                # Flughafen-Datenbank
│   ├── Waypoints.cs               # Waypoint-Verwaltung
│   ├── Simbrief.cs                # Simbrief-Integration
│   ├── PanelDeploymentService.cs  # Panel-Installation
│   ├── MsfsPathDetector.cs        # MSFS-Pfad-Erkennung
│   └── data/                      # Web-Frontend (HTML/JS/CSS)
├── Kneeboard Server Setup/        # Installer-Projekt
├── Kneeboard/                     # MSFS Panel Add-on
│   ├── PackageDefinitions/         # MSFS Package-Definition
│   ├── Packages/                   # Gebautes Package
│   └── PackageSources/
│       ├── efb_api/                # EFB API TypeScript-Definitionen
│       └── vendor/
│           └── Kneeboard/          # Panel Source Code (TypeScript/TSX)
└── Kneeboard Server.sln
```

## Technologie-Stack

**Server:**
- C# / .NET Framework 4.8
- Windows Forms
- EmbedIO HTTP Server (Port 815)
- SimConnect
- JSON für Datenaustausch

**Panel:**
- TypeScript / TSX
- @microsoft/msfs-sdk
- EFB API
- esbuild
- SASS

## Entwicklung

### Server Build-Konfigurationen

- **Debug|x64**: Entwicklung mit Debug-Symbolen
- **Release|x64**: Optimierte Production-Version

### Voraussetzungen

- Visual Studio 2017 oder neuer
- .NET Framework 4.8 SDK
- Windows SDK
- Node.js (für Panel-Entwicklung)
- MSFS SDK (für Panel-Entwicklung)

## Port

Der Server verwendet Port **815**. Stelle sicher, dass dieser Port nicht von anderen Anwendungen verwendet wird.

## Version

Aktuelle Version: 2.0.0

## Lizenz

- **Source Code:** [GNU General Public License v3.0 (GPLv3)](https://www.gnu.org/licenses/gpl-3.0.html)
- **Grafische Assets:** [Creative Commons BY-NC 4.0](https://creativecommons.org/licenses/by-nc/4.0/)

Copyright (c) 2021-2026 G-Simulations / Patrick Moses

Siehe [LICENSE](LICENSE) für Details und Drittanbieter-Lizenzen.

## Autor

Moses / Gsimulations
Website: https://www.gsimulations.com

## Hinweise

- **Diese Software dient ausschließlich zur Verwendung mit dem Microsoft Flight Simulator 2020/2024. Sie darf NICHT für reale Luftfahrt oder Navigation verwendet werden.**
- Die Anwendung benötigt Admin-Rechte für Netzwerk-Zugriff
- Es kann nur eine Instanz gleichzeitig laufen
- Firewall-Ausnahme wird beim ersten Start abgefragt
