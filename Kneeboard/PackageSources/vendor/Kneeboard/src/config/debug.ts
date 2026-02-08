/**
 * Debug Configuration for GSim Kneeboard EFB Services
 *
 * Set these flags to true to enable verbose logging for specific components.
 * Default: all flags are false (no debug output)
 */

// General EFB debug logging (connection, iframe, etc.)
export const EFB_DEBUG = false;

// Teleport service debug logging (pause, position set, resume)
export const TELEPORT_DEBUG = false;

// Frequency service debug logging (COM1/2, NAV1/2 frequency changes)
export const FREQUENCY_DEBUG = false;
