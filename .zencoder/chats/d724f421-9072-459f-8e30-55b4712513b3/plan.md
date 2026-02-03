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

**Root Cause Found:** In `loadArrivalProcedures()` (map.js:19493-19497), die Approach Transition wird als `null` übergeben, während die STAR Transition korrekt extrahiert wird. Es fehlt die Logik zum Extrahieren und Verwenden der Approach Transition.

## Phase 2: Resolution

### [x] Fix Implementation

- Develop a solution that addresses the root cause
- Ensure the fix doesn't introduce new issues
- Consider edge cases and boundary conditions
- Follow coding standards and best practices

**Änderungen:**
1. `map.html`: Neues Dropdown `arrApproachTransitionSelect` (A-TRANS) hinzugefügt
2. `map.js`: 
   - `flightplanPanelState.arrival.selectedApproachTransition` hinzugefügt
   - `getAvailableApproachTransitions()` - lädt verfügbare Approach Transitions
   - `populateApproachTransitionDropdown()` - befüllt das Dropdown
   - `extractApproachTransitionFromNavlog()` - extrahiert Transition aus OFP
   - `onArrApproachChange()` - lädt nun Transitions beim Approach-Wechsel
   - `onArrApproachTransitionChange()` - Handler für Transition-Dropdown
   - `loadArrivalProcedures()` - initialisiert Transition beim Laden

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
