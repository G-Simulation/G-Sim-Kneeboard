# Bug Fix Plan

This plan guides you through systematic bug resolution. Please update checkboxes as you complete each step.

## Phase 1: Investigation

### [x] Bug Reproduction

- Understand the reported issue and expected behavior
- Reproduce the bug in a controlled environment
- Document steps to reproduce consistently
- Identify affected components and versions

### [x] Root Cause Analysis

- Debug and trace the issue to its source
- Identify the root cause of the problem
- Understand why the bug occurs
- Check for similar issues in related code

**ROOT CAUSE GEFUNDEN:**
1. Server sucht `approachName` in `general.Approach_name` (KneeboardServer.cs:2954-2956)
2. SimBrief liefert dieses Feld NICHT im OFP `General`-Objekt
3. Navlog ist `null`, daher kann Approach auch nicht aus Navlog extrahiert werden
4. `approachWaypoints` bleibt leer → Approach wird NICHT zum Flugpfad hinzugefügt
5. Client wählt später automatisch Approach aus, aber nur als Preview-Layer (gestrichelt)

## Phase 2: Resolution

### [ ] Fix Implementation

**Geplante Lösung:**
Wenn `approachName` nicht im OFP gefunden wird, soll der Server automatisch einen Approach basierend auf der Arrival-Runway aus der Navigraph-DB holen (analog zu `autoSelectApproach` im Client).

**Änderungen in `KneeboardServer.cs`:**
1. In `EnrichFlightplanWithProceduresAsync` (Zeile ~2973): Wenn `approachName` null ist:
   - Hole alle Approaches für den Arrival-Airport
   - Wähle automatisch einen ILS/RNAV-Approach für die Arrival-Runway
   - Füge die Approach-Waypoints zum Flugpfad hinzu

2. Neue Hilfsfunktion `GetDefaultApproachForRunway(icao, runway)` erstellen

### [ ] Impact Assessment

- Identify areas affected by the change
- Check for potential side effects
- Ensure backward compatibility if needed
- Document any breaking changes

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
