/**
 * Kneeboard Shared Types
 * Used by both EFB and Toolbar versions
 */

/**
 * Teleport request data from map iframe
 */
export interface TeleportData {
  lat: number;
  lng: number;
  altitude?: number;
  heading?: number;
  speed?: number;
}

/**
 * Aircraft position data for updates
 */
export interface PositionData {
  lat: number;
  lng: number;
  altitude: number;
  heading: number;
  speed: number;
  windDirection: number;
  windSpeed: number;
}

/**
 * Radio frequency setting data
 */
export interface FrequencyData {
  radio: RadioType;
  frequencyHz: number;
  frequencyMHz?: number;
}

/**
 * Supported radio types for frequency setting
 * COM: Communication radios
 * NAV: VOR/ILS navigation radios
 * ADF: NDB/ADF navigation radios
 */
export type RadioType =
  | 'COM1_STBY' | 'COM1_ACTIVE' | 'COM2_STBY' | 'COM2_ACTIVE'
  | 'NAV1_STBY' | 'NAV1_ACTIVE' | 'NAV2_STBY' | 'NAV2_ACTIVE'
  | 'ADF1_STBY' | 'ADF1_ACTIVE' | 'ADF2_STBY' | 'ADF2_ACTIVE';

/**
 * Inbound message types from map iframe
 */
export type KneeboardMessageType =
  | 'teleportPause'
  | 'map'
  | 'unpauseTeleport'
  | 'setFrequency';

/**
 * Connection configuration for server probing
 */
export interface ConnectionConfig {
  kneeboardUrl: string;
  probeRateMs: number;
  probeTimeoutMs: number;
  reconnectDelayMs: number;
}

/**
 * Result of teleport operation
 */
export interface TeleportResult {
  success: boolean;
  error?: string;
}

/**
 * Message handlers for MessageRouter
 */
export interface MessageHandlers {
  onPauseConfirmed?: (result: TeleportResult) => void;
}
