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
- Navigraph DFD v2 Integration mit aktuellem AIRAC
- SID/STAR/Approach Procedure-Daten für detaillierte Flugpfad-Darstellung
- ILS-Daten und Runway-Informationen
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

1. Öffne `Kneeboard Server.sln` in Visual Studio 2022
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

Der Kneeboard Server kann detaillierte SID/STAR/Approach-Waypoints in der Karte darstellen. Die Waypoints werden farblich unterschieden:

- **Rot**: SID/Departure-Waypoints
- **Lila**: Route/Enroute-Waypoints
- **Grün**: Arrival/STAR-Waypoints
- **Orange**: Approach-Waypoints
- **Blau**: Alternate-Waypoints

### Navigraph Integration

Die Navdata werden über die **Navigraph DFD v2 API** bezogen. Dafür ist ein Navigraph-Account mit aktiver Subscription erforderlich.

**Einrichtung:**

1. Öffne den Kneeboard Server
2. Klicke auf das **Info-Symbol (i)** oben rechts
3. Klicke auf **"Login"** im Navigraph-Bereich
4. Ein Autorisierungscode wird angezeigt - gib diesen auf der Navigraph-Website ein
5. Nach erfolgreicher Autorisierung werden die aktuellen AIRAC-Daten automatisch heruntergeladen

**AIRAC-Updates:**

Der Kneeboard Server prüft beim Start automatisch auf neue AIRAC-Zyklen und lädt Updates herunter.

**Fallback:**

Ohne Navigraph-Login wird eine gebundelte Navigraph-Datenbank (AIRAC 2403) als Fallback verwendet.

### SimBrief Fallback

Ohne Navdata-Datenbank werden die vereinfachten Waypoints aus dem SimBrief-Flugplan verwendet. Diese werden anhand der `stage`-Information (CLB/CRZ/DSC) farblich markiert.

### API Endpoints

| Endpoint | Beschreibung |
|----------|--------------|
| `GET /api/navigraph/status` | Status der Navigraph-Verbindung |
| `GET /api/procedures/sids/{icao}` | Liste aller SIDs eines Flughafens |
| `GET /api/procedures/stars/{icao}` | Liste aller STARs eines Flughafens |
| `GET /api/procedures/approaches/{icao}` | Liste aller Approaches eines Flughafens |
| `GET /api/procedures/procedure/{icao}/{name}?type=SID\|STAR\|Approach&transition=xxx` | Details einer Procedure |
| `GET /api/ils/{icao}` | ILS-Daten eines Flughafens |
| `GET /api/navigraph/runway/{icao}/{runway}` | Runway-Daten |

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
│   ├── Navigraph/                 # Navigraph DFD v2 Integration
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

- Visual Studio 2022
- .NET Framework 4.8 SDK
- Windows SDK
- Node.js (für Panel-Entwicklung)
- MSFS SDK (für Panel-Entwicklung)

## Port

Der Server verwendet Port **815**. Stelle sicher, dass dieser Port nicht von anderen Anwendungen verwendet wird.

## Version

Aktuelle Version: 2.0.3

## Lizenz

- **Source Code:** [GNU General Public License v3.0 (GPLv3)](https://www.gnu.org/licenses/gpl-3.0.html)
- **Grafische Assets:** [Creative Commons BY-NC 4.0](https://creativecommons.org/licenses/by-nc/4.0/)

Copyright (c) 2021-2026 G-Simulations / Patrick Moses

Siehe [LICENSE](LICENSE) für Details und Drittanbieter-Lizenzen.

## Unterstützung

G-Sim Kneeboard ist ein kostenloses Projekt, das mit Leidenschaft in meiner Freizeit entwickelt wird.

Server-Hosting, die Website und die laufende Weiterentwicklung verursachen jedoch reale Kosten. Deine Spende hilft, dieses Projekt am Leben zu halten und neue Features zu ermöglichen. Jeder Beitrag – egal wie klein – fließt direkt in das Projekt und wird sehr geschätzt.

Vielen Dank für deine Unterstützung!

[![PayPal Spenden](https://img.shields.io/badge/PayPal-Spenden-blue?logo=paypal)](https://www.paypal.com/donate/?hosted_button_id=AJAFN6YQACS3S)

## Unterstützer

Vielen Dank an die folgenden Unterstützer des Projekts:

- [Crew Kingfisher VA](https://crew-kingfisher-va.de/)

## Autor

Moses / Gsimulations
Website: https://www.gsimulations.com

## Hinweise

- **Diese Software dient ausschließlich zur Verwendung mit dem Microsoft Flight Simulator 2020/2024. Sie darf NICHT für reale Luftfahrt oder Navigation verwendet werden.**
- Die Anwendung benötigt Admin-Rechte für Netzwerk-Zugriff
- Es kann nur eine Instanz gleichzeitig laufen
- Firewall-Ausnahme wird beim ersten Start abgefragt
