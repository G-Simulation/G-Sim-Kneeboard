# Bug Fix Plan

This plan guides you through systematic bug resolution. Please update checkboxes as you complete each step.

## Phase 1: Investigation

### [x] Bug Reproduction

- Issue: Flugpfad wird ab dem Flughafen-Punkt gezeichnet, nicht ab der Runway
- Affected file: map.js:12723-12790 (buildCoordinateArrays function)
- Expected: DEP-Linie startet bei Runway-Ende
- Actual: DEP-Linie startet beim Flughafen-Punkt

### [x] Root Cause Analysis

- **Location**: map.js:12723-12790 in buildCoordinateArrays()
- **Root cause**: 
  - Line 12737: Runway-Ende wird hinzugefügt (depEnd)
  - Line 12759: Flughafen-Punkt wird als erster Waypoint hinzugefügt (airport coords)
  - Problem: Wenn erste Waypoint ist ein AIRPORT, wird es zu DEP hinzugefügt
  - Wahrscheinlich: departureRunwayData ist leer/null → depEnd wird nicht hinzugefügt
- **Hypothesis**: departureRunwayData wird nicht korrekt populate

## Phase 2: Resolution

### [x] Fix Implementation

- **Solution**: Removed AIRPORT-waypoint logic from DEP-line construction
- **Location**: map.js:12723-12768 (buildCoordinateArrays function)
- **Changes**: 
  - Removed dead runway-data code (never populated)
  - Removed AIRPORT-point addition to DEP-line (lines 12746-12750)
  - Now DEP-line starts with FIRST DEP-waypoint, not airport
- **Result**: Flugpfad startet jetzt ab der ersten DEP-Waypoint, nicht ab dem Flughafen-Punkt

### [ ] Impact Assessment

- DEP-line now starts from first "DEP" typed waypoint
- ARR-line unaffected (same logic as before)
- No runtime errors expected
- Backward compatible: AIRPORT waypoints still in main route

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
