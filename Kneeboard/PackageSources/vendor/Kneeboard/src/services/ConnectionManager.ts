import { ConnectionConfig } from '../types/KneeboardTypes';

/**
 * Connection Manager Service - Handles server connection state
 * Manages probing, reconnection, and state change notifications
 */
export class ConnectionManager {
  private isConnected = false;
  private reconnectTimerId?: number;
  private probeIntervalId?: number;
  private onStateChange?: (connected: boolean) => void;

  constructor(private config: ConnectionConfig) {}

  /**
   * Sets the handler for connection state changes
   */
  setStateChangeHandler(handler: (connected: boolean) => void): void {
    this.onStateChange = handler;
  }

  /**
   * Gets current connection state
   */
  getConnectionState(): boolean {
    return this.isConnected;
  }

  /**
   * Updates connection state and notifies handler
   * @param connected New connection state
   */
  setConnected(connected: boolean): void {
    if (this.isConnected !== connected) {
      this.isConnected = connected;
      this.onStateChange?.(connected);
    }
  }

  /**
   * Starts periodic server probing
   */
  startProbing(): void {
    if (this.probeIntervalId !== undefined) {
      return; // Already running
    }
    this.probeIntervalId = window.setInterval(
      () => this.probe(),
      this.config.probeRateMs
    );
  }

  /**
   * Stops periodic server probing
   */
  stopProbing(): void {
    if (this.probeIntervalId !== undefined) {
      window.clearInterval(this.probeIntervalId);
      this.probeIntervalId = undefined;
    }
  }

  /**
   * Schedules a reconnection attempt
   * @param reloadFn Function to call for reload (e.g., iframe reload)
   */
  scheduleReconnect(reloadFn: () => void): void {
    if (this.isConnected || this.reconnectTimerId !== undefined) {
      return; // Already connected or reconnect pending
    }
    this.reconnectTimerId = window.setTimeout(() => {
      this.reconnectTimerId = undefined;
      reloadFn();
    }, this.config.reconnectDelayMs);
  }

  /**
   * Clears any pending reconnection timer
   */
  clearReconnectTimer(): void {
    if (this.reconnectTimerId !== undefined) {
      window.clearTimeout(this.reconnectTimerId);
      this.reconnectTimerId = undefined;
    }
  }

  /**
   * Probes server reachability with timeout
   */
  async probe(): Promise<void> {
    try {
      const response = await this.fetchWithTimeout(
        `${this.config.kneeboardUrl}?ping=${Date.now()}`,
        this.config.probeTimeoutMs,
        { method: 'GET', cache: 'no-store' }
      );

      if (response.ok) {
        this.setConnected(true);
        this.clearReconnectTimer();
      } else {
        throw new Error(`HTTP ${response.status}`);
      }
    } catch (err) {
      console.warn('Kneeboard server probe failed:', err);
      this.setConnected(false);
    }
  }

  /**
   * Fetch with configurable timeout
   */
  private fetchWithTimeout(
    url: string,
    timeoutMs: number,
    init?: RequestInit
  ): Promise<Response> {
    const requestInit: RequestInit = {
      method: 'GET',
      cache: 'no-store',
      ...init,
    };

    // Check for AbortController support
    if (typeof AbortController === 'undefined') {
      return fetch(url, requestInit).then((response) => {
        if (!response.ok) {
          throw new Error(`HTTP ${response.status}`);
        }
        return response;
      });
    }

    const controller = new AbortController();
    const timeoutId = window.setTimeout(() => controller.abort(), timeoutMs);

    return fetch(url, {
      ...requestInit,
      signal: controller.signal,
    })
      .then((response) => {
        if (!response.ok) {
          throw new Error(`HTTP ${response.status}`);
        }
        return response;
      })
      .finally(() => {
        window.clearTimeout(timeoutId);
      });
  }

  /**
   * Cleanup - stops all timers
   */
  destroy(): void {
    this.stopProbing();
    this.clearReconnectTimer();
  }
}
