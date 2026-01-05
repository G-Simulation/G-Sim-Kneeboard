# Map Layer Konfiguration

## Übersicht

Die Map-Layer und Einstellungen können über die Datei `map-layers.json` konfiguriert werden. Dies ermöglicht es, neue Karten-Layer hinzuzufügen und das Verhalten der Karte anzupassen, ohne den JavaScript-Code zu ändern.

## Konfigurationsdatei

Die Datei `map-layers.json` enthält folgende Hauptabschnitte:

### 0. Globale Einstellungen

```json
{
  "mapSettings": {
    "defaultZoom": 9,
    "smoothSensitivity": 1.5
  },
  "defaultBaseLayer": "Streets",
  "defaultOverlays": {
    "OpenAip": "On",
    "Aircraft": "On",
    "Flightpath": "Off",
    "Sectionals": "Off",
    "Wind": "OpenWeatherMap",
    "Weather": "Off",
    "Elevation Profile": "Off",
    "ControlZones": "Off"
  },
  "aircraftScale": 1.0,
  "windSettings": {
    "gridSize": 5,
    "maxSpeedForColor": 50,
    "arrowSize": 24
  },
  "owmApiKey": "9de243494c0b295cca9337e1e96b00e2",
  "scaleControl": {
    "metric": true,
    "imperial": false,
    "nautic": true
  },
  "controlZoneSettings": {
    "opacity": 0.3,
    "colors": {
      "CTR": "#3388ff",
      "APP": "#33ff88",
      "TWR": "#ff8833",
      "GND": "#ff3388",
      "DEL": "#8833ff",
      "FSS": "#3388ff"
    }
  }
}
```

#### mapSettings
- **defaultZoom**: Standard-Zoomstufe beim Start (1-18, Standard: 9)
- **smoothSensitivity**: Empfindlichkeit des Scroll-Zooms (0.5-3.0, Standard: 1.5)

#### defaultBaseLayer
Name der Standard-Basiskarte beim Start. Muss einem Namen aus `baseLayers` entsprechen.
- Mögliche Werte: "Streets", "Hybrid", "Satellite", "Terrain", etc.

#### defaultOverlays
Definiert welche Overlay-Layer beim Start aktiv sind. Der Key ist der Gruppenname, der Value der Layer-Name.
- **OpenAip**: "On" oder "Off"
- **Aircraft**: "On" oder "Off"
- **Flightpath**: "On", "Off" oder "Reset"
- **Sectionals**: "Off" oder "DFS"
- **Wind**: "OpenWeatherMap" oder "Off"
- **Weather**: "Off", "Clouds", "Precipitation", "Temperature" oder "Pressure"
- **Elevation Profile**: "On" oder "Off"
- **ControlZones**: "On" oder "Off" - FIR-Kontrollzonen von VATSIM/IVAO

#### aircraftScale
Skalierungsfaktor für das Flugzeug-Icon (0.1-5.0, Standard: 1.0)

#### windSettings
Einstellungen für die Wind-Anzeige:
- **gridSize**: Anzahl der Windpfeile pro Achse (3-10, Standard: 5). Bei 5 werden 5x5=25 Windpfeile angezeigt.
- **maxSpeedForColor**: Windgeschwindigkeit in Knoten, ab der die Farbe vollständig rot ist (Standard: 50)
- **arrowSize**: Größe der Windpfeile in Pixel (Standard: 24)

#### owmApiKey
API-Schlüssel für OpenWeatherMap. Wird für Weather-Layer (owmLayer) benötigt.
- Kann bei [OpenWeatherMap](https://openweathermap.org/api) kostenlos erstellt werden
- Wird automatisch in die owmLayer-URLs eingebunden

#### controlZoneSettings
Einstellungen für die VATSIM/IVAO Kontrollzonen-Anzeige:
- **opacity**: Transparenz der Zonen-Polygone (0.0-1.0, Standard: 0.3)
- **colors**: Farben für verschiedene Controller-Typen:
  - **CTR**: Center/Radar (Standard: #3388ff - Blau)
  - **APP**: Approach (Standard: #33ff88 - Grün)
  - **TWR**: Tower (Standard: #ff8833 - Orange)
  - **GND**: Ground (Standard: #ff3388 - Pink)
  - **DEL**: Delivery (Standard: #8833ff - Lila)
  - **FSS**: Flight Service Station (Standard: #3388ff - Blau)

**Hinweis**: Die Control Zones zeigen nur aktive FIR-Zonen an, in denen aktuell Controller online sind. Das Netzwerk (VATSIM/IVAO) wird über den RADIO/IVAO/VATSIM-Button gesteuert.

#### scaleControl
Einstellungen für die Maßstabsanzeige:
- **metric**: Metrische Einheiten (km) anzeigen (true/false)
- **imperial**: Imperiale Einheiten (mi) anzeigen (true/false)
- **nautic**: Nautische Einheiten (nm) anzeigen (true/false)

### 1. Base Layers (Basiskarten)

Base Layers sind die Grundkarten, von denen immer genau eine aktiv ist. Beispiele: Streets, Satellite, Terrain.

#### Layer-Typ: tileLayer

```json
{
  "baseLayers": [
    {
      "name": "Streets",
      "type": "tileLayer",
      "url": "https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png",
      "options": {
        "attribution": "&copy; OpenStreetMap contributors",
        "maxZoom": 19,
        "minZoom": 0,
        "subdomains": ["a", "b", "c"]
      }
    }
  ]
}
```

### 2. Overlay Groups (Überlagerungen)

Overlay Groups sind optionale Layer, die über die Basiskarte gelegt werden können. Jede Gruppe kann mehrere Layer enthalten.

#### Layer-Typ: tileLayer

Standard-Tile-Layer für alle gängigen Karten-Overlays:

```json
{
  "overlayGroups": [
    {
      "groupName": "Sectionals",
      "layers": [
        {
          "name": "DFS",
          "type": "tileLayer",
          "url": "https://secais.dfs.de/static-maps/icao500/tiles/{z}/{x}/{y}.png",
          "options": {
            "attribution": "&copy; DFS Deutsche Flugsicherung GmbH",
            "maxZoom": 13,
            "minZoom": 7
          }
        },
        {
          "name": "Off",
          "type": "tileLayer",
          "url": "http://",
          "options": {}
        }
      ]
    }
  ]
}
```

#### Layer-Typ: owmLayer

Spezielle Layer für OpenWeatherMap-Wetterdaten. Diese Layer nutzen den `owmApiKey` aus der globalen Konfiguration:

```json
{
  "overlayGroups": [
    {
      "groupName": "Weather",
      "layers": [
        {
          "name": "Clouds",
          "type": "owmLayer",
          "owmType": "clouds",
          "options": {
            "opacity": 0.5,
            "showLegend": true,
            "legendPosition": "bottomright"
          }
        },
        {
          "name": "Precipitation",
          "type": "owmLayer",
          "owmType": "precipitation",
          "options": {
            "opacity": 0.6,
            "showLegend": true,
            "legendPosition": "bottomright"
          }
        },
        {
          "name": "Off",
          "type": "tileLayer",
          "url": "http://",
          "options": {}
        }
      ]
    }
  ]
}
```

**owmLayer Optionen:**
- **owmType**: Art der Wetterdaten
  - `clouds` - Wolkenbedeckung
  - `precipitation` - Niederschlag
  - `temperature` - Temperatur
  - `pressure` - Luftdruck
- **options.opacity**: Transparenz der Layer (0.0-1.0)
- **options.showLegend**: Legende anzeigen (true/false)
- **options.legendPosition**: Position der Legende
  - `topleft`, `topright`, `bottomleft`, `bottomright`

#### Layer-Typ: controlZones

Spezieller Layer für VATSIM/IVAO FIR-Kontrollzonen. Zeigt automatisch aktive Controller-Zonen auf der Karte an:

```json
{
  "overlayGroups": [
    {
      "groupName": "Control Zones",
      "layers": [
        {
          "name": "On",
          "type": "controlZones",
          "options": {}
        },
        {
          "name": "Off",
          "type": "tileLayer",
          "url": "http://",
          "options": {}
        }
      ]
    }
  ]
}
```

**Funktionen:**
- Zeigt FIR-Polygone für aktive Controller
- Farbcodierung nach Controller-Typ (CTR, APP, TWR, GND, DEL)
- Klick auf Zone zeigt Popup mit:
  - Controller-Callsign und Name
  - Frequenz mit COM1/COM2 Buttons
  - ATIS-Text (falls verfügbar)
- Netzwerk (VATSIM/IVAO) wird über den Radio-Button gesteuert
- Zonen werden automatisch bei Controller-Updates aktualisiert

**Datenquellen:**
- VATSIM: VAT-Spy Data Project (CC BY-SA 4.0)
- IVAO: Little Navmap Airspace Boundaries

## Neue Layer hinzufügen

### Beispiel: OpenAeroMap hinzufügen

Um einen neuen Base Layer (z.B. OpenAeroMap) hinzuzufügen:

1. Öffne `map-layers.json`
2. Füge im `baseLayers` Array einen neuen Eintrag hinzu:

```json
{
  "name": "OpenAero",
  "type": "tileLayer",
  "url": "https://{s}.tile.opentopomap.org/{z}/{x}/{y}.png",
  "options": {
    "attribution": "&copy; OpenAeroMap contributors",
    "maxZoom": 17,
    "minZoom": 0,
    "subdomains": ["a", "b", "c"]
  }
}
```

### Beispiel: Neue Overlay-Gruppe hinzufügen (tileLayer)

Um eine neue Overlay-Gruppe mit tileLayer hinzuzufügen:

```json
{
  "groupName": "Custom",
  "layers": [
    {
      "name": "MyLayer",
      "type": "tileLayer",
      "url": "https://example.com/tiles/{z}/{x}/{y}.png",
      "options": {
        "attribution": "&copy; My Provider",
        "maxZoom": 19,
        "opacity": 0.5
      }
    },
    {
      "name": "Off",
      "type": "tileLayer",
      "url": "http://",
      "options": {}
    }
  ]
}
```

### Beispiel: Weather-Layer mit owmLayer

Die Weather-Layer verwenden den speziellen `owmLayer`-Typ. Stelle sicher, dass `owmApiKey` in der globalen Konfiguration gesetzt ist:

```json
{
  "groupName": "Weather",
  "layers": [
    {
      "name": "Clouds",
      "type": "owmLayer",
      "owmType": "clouds",
      "options": {
        "opacity": 0.5,
        "showLegend": true,
        "legendPosition": "bottomright"
      }
    },
    {
      "name": "Temperature",
      "type": "owmLayer",
      "owmType": "temperature",
      "options": {
        "opacity": 0.6,
        "showLegend": true,
        "legendPosition": "bottomright"
      }
    },
    {
      "name": "Off",
      "type": "tileLayer",
      "url": "http://",
      "options": {}
    }
  ]
}
```

## Verfügbare Optionen

Alle Standard-Leaflet TileLayer-Optionen werden unterstützt:

- **attribution**: Copyright-Hinweis
- **maxZoom**: Maximaler Zoom-Level
- **minZoom**: Minimaler Zoom-Level
- **maxNativeZoom**: Maximaler nativer Zoom des Tile-Servers (wichtig!)
- **subdomains**: Array von Subdomains für parallele Anfragen
- **opacity**: Transparenz (0.0 - 1.0)
- **crossOrigin**: CORS-Einstellung (meistens "anonymous")
- **keepBuffer**: Anzahl zu behaltender Tiles (empfohlen: 4)
- **updateWhenIdle**: Update nur wenn Karte idle ist (empfohlen: true)
- **updateWhenZooming**: Update während Zoom (empfohlen: true)
- **edgeBufferTiles**: Anzahl zusätzlicher Tiles am Rand (optional)
- **updateInterval**: Update-Intervall in ms (optional)

## Platzhalter

Die URL kann folgende Platzhalter verwenden:

- `{z}`: Zoom-Level
- `{x}`: X-Koordinate der Tile
- `{y}`: Y-Koordinate der Tile
- `{s}`: Subdomain (falls `subdomains` definiert)
- `{{OPENAIP_PROXY}}`: Wird automatisch durch die OpenAIP-Proxy-URL ersetzt
- `{{DFS_PROXY}}`: Wird automatisch durch die DFS-Proxy-URL ersetzt

## Beispiele für beliebte Tile-Provider

### OpenStreetMap (Deutschland)
```
https://{s}.tile.openstreetmap.de/{z}/{x}/{y}.png
subdomains: ["a", "b", "c"]
maxNativeZoom: 18
```

### Google Maps Satellite
```
https://{s}.google.com/vt/lyrs=s&x={x}&y={y}&z={z}
subdomains: ["mt0", "mt1", "mt2", "mt3"]
maxNativeZoom: 21
```

### Google Maps Hybrid (Satellite + Labels)
```
https://{s}.google.com/vt/lyrs=s,h&x={x}&y={y}&z={z}
subdomains: ["mt0", "mt1", "mt2", "mt3"]
maxNativeZoom: 21
```

### OpenTopoMap
```
https://{s}.tile.opentopomap.org/{z}/{x}/{y}.png
subdomains: ["a", "b", "c"]
maxNativeZoom: 17
```

### Stadia Terrain (ehemals Stamen)
⚠️ **Hinweis**: Stamen Terrain wurde Oktober 2023 eingestellt. Nutze stattdessen:
```
https://tiles.stadiamaps.com/tiles/stamen_terrain/{z}/{x}/{y}.jpg
maxNativeZoom: 18
```

### CartoDB Dark Matter
```
https://{s}.basemaps.cartocdn.com/dark_all/{z}/{x}/{y}.png
subdomains: ["a", "b", "c", "d"]
maxNativeZoom: 19
```

### ESRI World Imagery
```
https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}
maxNativeZoom: 18
```

### OpenWeatherMap Wind
```
https://tile.openweathermap.org/map/wind_new/{z}/{x}/{y}.png?appid=YOUR_API_KEY
opacity: 0.6
```

## Tipps

1. **Off-Layer**: Jede Overlay-Gruppe sollte einen "Off"-Layer haben, damit Benutzer die Überlagerung deaktivieren können.

2. **Attribution**: Stelle sicher, dass du die korrekten Copyright-Hinweise für jeden Tile-Provider verwendest.

3. **Zoom-Level**: Nicht alle Provider unterstützen alle Zoom-Level. Prüfe die Dokumentation des Providers.

4. **maxNativeZoom**: **WICHTIG!** Setze immer `maxNativeZoom` auf den maximalen Zoom-Level des Tile-Servers. Dies verhindert unnötige Anfragen an den Server für nicht existierende Tiles.

5. **Performance-Optionen**: Für beste Performance nutze:
   - `updateWhenIdle: true` - Update nur wenn Karte ruht
   - `updateWhenZooming: true` - Erlaubt Updates während Zoom
   - `keepBuffer: 4` - Hält 4 Tiles im Puffer (Balance zwischen Speicher und Performance)

6. **Performance**: Zu viele gleichzeitig aktive Layer können die Performance beeinträchtigen.

7. **API-Keys**: Einige Provider erfordern API-Keys in der URL.

8. **Proxy-URLs**: Nutze `{{OPENAIP_PROXY}}` und `{{DFS_PROXY}}` für interne Layer, um CORS-Probleme zu vermeiden.

9. **Default Overlays**: Mit `defaultOverlays` kannst du festlegen, welche Layer beim Start aktiv sind.

10. **Wind-Einstellungen**: Mit `windSettings` kannst du die Dichte und Größe der Windpfeile anpassen.

## Fehlerbehebung

Falls die Karte nicht lädt:

1. Überprüfe die Browser-Konsole auf Fehler
2. Stelle sicher, dass die JSON-Syntax korrekt ist (verwende einen JSON-Validator)
3. Prüfe, ob die Tile-URLs erreichbar sind
4. Überprüfe CORS-Einstellungen bei Cross-Origin-Requests

## Server-Neustart

Nach Änderungen an `map-layers.json`:

1. Speichere die Datei
2. Lade die Seite im Browser neu (Strg+F5 für harten Reload)
3. Ein Server-Neustart ist **nicht** erforderlich

## Support

Bei Problemen oder Fragen:
- GitHub Issues: https://github.com/G-Simulation/G-Sim-Kneeboard-Server/issues
- Website: https://www.gsimulations.com
