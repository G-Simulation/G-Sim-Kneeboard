# Bug Fix Plan

This plan guides you through systematic bug resolution. Please update checkboxes as you complete each step.

## Phase 1: Investigation

### [x] Bug Reproduction

- Understand the reported issue and expected behavior
- Reproduce the bug in a controlled environment
- Document steps to reproduce consistently
- Identify affected components and versions

**Findings:**
- Problem: Flugpfad und Transition werden nach dem Laden nicht korrekt gezeichnet
- SID-Linien (grün) zeigen Zickzack-Muster statt korrekter Verbindungen
- Route: KPDX -> KSJC mit SID MINNE5 und STAR BRIXX4

### [x] Root Cause Analysis

- Debug and trace the issue to its source
- Identify the root cause of the problem
- Understand why the bug occurs
- Check for similar issues in related code

**Root Cause:**
Das Problem liegt in der Zuweisung der SID/STAR Waypoints zum DEP/ARR Type:

1. In `kneeboard.js:convertSimbriefFlightplan()` werden Waypoints nur als "DEP" markiert, wenn sie ein `DepartureFP` Flag haben
2. SimBrief PLN setzt `DepartureFP` nur auf bestimmten Waypoints (meist nur ersten Waypoints)
3. Die eigentlichen SID-Waypoints haben kein `DepartureFP` Flag und werden daher NICHT als "DEP" markiert
4. In `map.js:buildCoordinateArrays()` werden nur Waypoints mit `type.startsWith("DEP")` in `coordsArrayDEP` aufgenommen
5. **Resultat:** Die grüne DEP-Linie enthält nur wenige Waypoints, was zu den falschen Verbindungen führt

**Betroffene Dateien:**
- `kneeboard.js` (Zeile 305-343): Waypoint Type-Zuweisung
- `map.js` (Zeile 12954-12962): `buildCoordinateArrays()` DEP/ARR Filterung

## Phase 2: Resolution

### [x] Fix Implementation

- Develop a solution that addresses the root cause
- Ensure the fix doesn't introduce new issues
- Consider edge cases and boundary conditions
- Follow coding standards and best practices

**Implementierte Lösung:**

1. **Server-seitige Anreicherung (KneeboardServer.cs):**
   - `EnrichFlightplanWithProceduresAsync()` extrahiert nun Transition und Runway aus OFP-Daten
   - `GetProcedureWaypointsAsync()` implementiert: ruft Navigraph DB mit korrekten Parametern auf
   - Transitions werden aus `General.Sid_trans` / `General.Star_trans` extrahiert
   - Runways werden aus `Origin.Plan_rwy` / `Destination.Plan_rwy` extrahiert
   - Enriched Data enthält nun: `{pln, ofp, procedures: {sid: {name, transition, runway, waypoints}, star: {...}}}`

2. **Client-seitige Verarbeitung (kneeboard.js):**
   - `convertSimbriefFlightplan()` akzeptiert jetzt `procedures` als dritten Parameter
   - SID/STAR Waypoint-Namen werden aus Navigraph-Daten extrahiert
   - PLN-Waypoints werden anhand ihrer Namen mit SID/STAR Waypoints abgeglichen
   - Erkannte SID-Waypoints erhalten `waypointType: "DEP SID_NAME"`
   - Erkannte STAR-Waypoints erhalten `waypointType: "ARR STAR_NAME"`
   - Beide Call-Sites aktualisiert um `proceduresData` weiterzugeben

3. **Timing/Animation:**
   - Server-seitige Anreicherung erfolgt synchron VOR dem Senden an Client
   - Keine Animation beginnt bevor alle Daten vollständig sind

### [x] Impact Assessment

- Identify areas affected by the change
- Check for potential side effects
- Ensure backward compatibility if needed
- Document any breaking changes

**Betroffene Komponenten:**
- `KneeboardServer.cs`: Neue Navigraph-Integration, OFP-Parsing erweitert
- `kneeboard.js`: Waypoint-Type-Zuweisung verbessert
- Rückwärtskompatibel: `procedures` Parameter ist optional, ohne Navigraph-Daten funktioniert alte Logik

## Phase 3: Verification

### [ ] Testing & Verification

- Verify the bug is fixed with the original reproduction steps
- Write regression tests to prevent recurrence
- Test related functionality for side effects
- Perform integration testing if applicable

### [ ] Documentation & Cleanup

- Update relevant documentation
- Add comments explaining the fix
- Clean up any debug code
- Prepare clear commit message

## Notes

- Update this plan as you discover more about the issue
- Check off completed items using [x]
- Add new steps if the bug requires additional investigation
