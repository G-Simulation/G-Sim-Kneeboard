# Auto-Update Anleitung für G-Sim Kneeboard

Diese Anleitung beschreibt, wie das automatische Update-System und der Release-Prozess funktionieren.

## GitHub Release-Prozess (Automatisch)

Der Release-Prozess ist vollautomatisch über GitHub Actions. Ein neues Release wird erstellt, sobald die Version in `AssemblyInfo.cs` geändert und auf `main` gepusht wird.

### Voraussetzungen

- MSI-Installer und setup.exe müssen im Ordner `Kneeboard Server Setup/Debug/` vorhanden sein
- Git Repository ist mit GitHub verbunden
- GitHub Actions Workflow ist konfiguriert (`.github/workflows/release.yml`)

### Schritt 1: Version erhöhen

In `Properties/AssemblyInfo.cs` die Version erhöhen:

```csharp
[assembly: AssemblyVersion("2.1.0.0")]
[assembly: AssemblyFileVersion("2.1.0.0")]
```

### Schritt 2: Installer erstellen

In Visual Studio das Setup-Projekt kompilieren, sodass die Dateien im `Kneeboard Server Setup/Debug/` Ordner aktualisiert werden:
- `Kneeboard Server.msi`
- `setup.exe`

### Schritt 3: Änderungen committen und pushen

```bash
git add .
git commit -m "Release v2.1.0"
git push origin main
```

### Was passiert automatisch

Nach dem Push auf `main` erkennt GitHub Actions die Änderung in `AssemblyInfo.cs` und führt folgende Schritte aus:

1. **Version extrahieren** - Version wird aus `AssemblyVersion("x.y.z.0")` gelesen
2. **Duplikat-Prüfung** - Falls Tag `vx.y.z` schon existiert, wird abgebrochen
3. **Panel-Versionen synchronisieren** - Folgende Dateien werden auf die Server-Version aktualisiert:
   - `Kneeboard/Packages/gsimulations-kneeboard/manifest.json`
   - `Kneeboard/PackageDefinitions/gsimulations-kneeboard/ContentInfo/manifest.json`
   - `Kneeboard/PackageSources/vendor/Kneeboard/package.json`
4. **README aktualisieren** - Versionszeile wird angepasst
5. **Commit + Push** - Versions-Änderungen werden committed (löst keinen neuen Workflow aus)
6. **Git-Tag erstellen** - `vx.y.z` wird automatisch getaggt und gepusht
7. **ZIP erstellen** - `Kneeboard-vx.y.z-Setup.zip` wird aus MSI + setup.exe erstellt
8. **Update-XML generieren** - `Kneeboard_version.xml` wird automatisch generiert
9. **GitHub Release erstellen** - Release "Kneeboard vx.y.z" mit ZIP und XML wird veröffentlicht

### URL-Schema

Nach erfolgreichem Release sind die Dateien unter folgenden URLs verfügbar:

- **Download:** `https://github.com/G-Simulation/G-Sim-Kneeboard/releases/download/vx.y.z/Kneeboard-vx.y.z-Setup.zip`
- **Changelog:** `https://github.com/G-Simulation/G-Sim-Kneeboard/releases/tag/vx.y.z`

---

## Server-Konfiguration (Auto-Update)

### XML-Versionsdatei

Die Datei `Kneeboard_version.xml` wird automatisch bei jedem Release generiert und als Asset angehängt. Sie kann auch manuell unter folgender URL bereitgestellt werden:
```
https://gsimulations.com/Kneeboard_version.xml
```

**Format der XML-Datei:**

```xml
<?xml version="1.0" encoding="UTF-8"?>
<item>
    <version>2.1.0</version>
    <url>https://github.com/G-Simulation/G-Sim-Kneeboard/releases/download/v2.1.0/Kneeboard-v2.1.0-Setup.zip</url>
    <changelog>https://github.com/G-Simulation/G-Sim-Kneeboard/releases/tag/v2.1.0</changelog>
    <mandatory>false</mandatory>
    <executable>Kneeboard Server.msi</executable>
</item>
```

### Felder-Erklärung

| Feld | Beschreibung | Erforderlich |
|------|-------------|--------------|
| `version` | Die neue Versionsnummer (muss höher sein als die installierte Version) | Ja |
| `url` | Download-Link zur ZIP-Datei | Ja |
| `changelog` | Link zur Changelog-Seite (wird dem Benutzer angezeigt) | Nein |
| `mandatory` | `true` = Pflicht-Update, `false` = Optional | Nein |
| `executable` | Name der Installer-Datei innerhalb der ZIP | Nein |
| `checksum` | MD5/SHA256 Prüfsumme der Datei | Nein |

---

## Erweiterte Optionen

### Prüfsumme hinzufügen

Für zusätzliche Sicherheit kann eine Prüfsumme hinzugefügt werden:

```xml
<?xml version="1.0" encoding="UTF-8"?>
<item>
    <version>2.1.0</version>
    <url>https://github.com/G-Simulation/G-Sim-Kneeboard/releases/download/v2.1.0/Kneeboard-v2.1.0-Setup.zip</url>
    <checksum algorithm="MD5">dein_md5_hash_hier</checksum>
    <mandatory>false</mandatory>
</item>
```

### Pflicht-Update

Für kritische Updates kann `mandatory` auf `true` gesetzt werden:

```xml
<mandatory>true</mandatory>
```

Bei Pflicht-Updates wird der Benutzer gezwungen, das Update zu installieren.

---

## Funktionsweise

1. Beim Start der Anwendung wird `Kneeboard_version.xml` abgerufen
2. Die Version in der XML wird mit der installierten Version verglichen
3. Wenn eine neue Version verfügbar ist:
   - Update wird automatisch heruntergeladen
   - Nach dem Download wird die Installation gestartet
   - Die Anwendung wird beendet und neu gestartet

## Fehlerbehebung

### Update wird nicht erkannt
- Prüfen ob die Version in der XML höher ist als in `AssemblyInfo.cs`
- XML-Datei im Browser aufrufen um Erreichbarkeit zu testen

### Download schlägt fehl
- URL in der XML prüfen (Leerzeichen mit `%20` kodieren)
- ZIP-Datei im Browser testen

### Installation schlägt fehl
- Anwendung benötigt Admin-Rechte
- Antivirus-Software kann Installation blockieren

### GitHub Action schlägt fehl
- Prüfen ob MSI und setup.exe in `Kneeboard Server Setup/Debug/` vorhanden sind
- Prüfen ob der Tag `vx.y.z` noch nicht existiert
- GitHub Actions Logs unter `Actions` Tab im Repository prüfen

---

## Release-Historie

### v2.0.3
- Fix: data-Ordner wurde nicht ins Installationsverzeichnis kopiert (Kneeboard im Browser nicht erreichbar nach Installation)
- Fix: Debug-Build Pfadlogik mit Fallback - im Debug aus VS wird der Projekt-Ordner genutzt, bei installierter Version das exe-Verzeichnis

### v2.0.2
- QR Designer Fix
- Autoupdater URL Fix
- Event Handler Leak Fix
- MSI Upgrade Fix
