# Gsim Kneeboard

Ein Kneeboard Add-on für Microsoft Flight Simulator 2020 und 2024.

## Beschreibung

Gsim Kneeboard ist eine Toolbar- und EFB-Anwendung (Electronic Flight Bag), die ein interaktives Kneeboard im Simulator anzeigt. Die App verbindet sich mit einem lokalen Kneeboard-Server und überträgt Echtzeit-Flugdaten.

### Features

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

## Voraussetzungen

- Microsoft Flight Simulator 2020 oder 2024
- MSFS SDK
- Node.js
- Laufender Kneeboard-Server auf `http://localhost:815`

## Installation

1. Dependencies installieren:
```bash
cd PackageSources/vendor/Kneeboard
npm install
```

2. Projekt bauen:
```bash
npm run build
```

3. Das Package im MSFS DevMode laden oder über das SDK bauen.

## Entwicklung

### Build-Befehle

| Befehl | Beschreibung |
|--------|--------------|
| `npm run build` | Projekt einmalig bauen |
| `npm run watch` | Watch-Mode für Entwicklung |
| `npm run rebuild` | Dist löschen und neu bauen |
| `npm run clean` | Dist und node_modules löschen |

### Konfiguration

Die `.env` Datei im Kneeboard-Ordner ermöglicht folgende Einstellungen:

- `APP_ID` - App-Identifier (Standard: `Kneeboard`)
- `APP_VIEW_CLASS` - CSS-Klasse der View (Standard: `GsimKneeboard`)
- `API_PROXY_URL` - URL des Kneeboard-Servers (Standard: `http://localhost:815`)
- `TYPECHECKING` - TypeScript-Prüfung aktivieren (`true`/`false`)
- `SOURCE_MAPS` - Source Maps generieren (`true`/`false`)
- `MINIFY` - Output minimieren (`true`/`false`)

## Projektstruktur

```
EFB/
├── PackageDefinitions/          # MSFS Package-Definition
├── Packages/                    # Gebautes Package
├── PackagesMetadata/
├── PackageSources/
│   ├── efb_api/                 # EFB API TypeScript-Definitionen
│   └── vendor/
│       └── Kneeboard/           # Kneeboard Source Code
│           ├── src/             # TypeScript/TSX Quellcode
│           ├── dist/            # Build-Output
│           ├── toolbar/         # Toolbar-Assets
│           ├── build.js         # esbuild Konfiguration
│           └── package.json
└── GsimulationsKneeboardProject.xml
```

## Technologie-Stack

- TypeScript / TSX
- [@microsoft/msfs-sdk](https://www.npmjs.com/package/@microsoft/msfs-sdk)
- EFB API
- esbuild
- SASS

## Lizenz

MIT License - Copyright (c) 2025 Patrick

## Autor

Moses / Gsimulations
