/**
 * Kneeboard Centralized Logging System
 * Version: 1.0.0
 *
 * Features:
 * - Log-Levels: DEBUG, INFO, WARN, ERROR
 * - Timestamps und Stack-Traces bei Errors
 * - Modul-spezifische Logger
 * - Globale Error-Handler
 * - Coherent GT kompatibel
 */

(function(global) {
    'use strict';

    // ========================================================================
    // CONFIGURATION
    // ========================================================================
    var CONFIG = {
        CONSOLE_OUTPUT: true,
        TIMESTAMP_FORMAT: 'SHORT',  // 'ISO' oder 'SHORT'
        MAX_STACK_DEPTH: 8,
        VERSION: '1.0.0'
    };

    // ========================================================================
    // LOG LEVELS
    // ========================================================================
    var LOG_LEVELS = {
        DEBUG: { value: 0, console: 'log' },
        INFO:  { value: 1, console: 'info' },
        WARN:  { value: 2, console: 'warn' },
        ERROR: { value: 3, console: 'error' }
    };

    var moduleSettings = {};

    // ========================================================================
    // UTILITY FUNCTIONS
    // ========================================================================

    function getTimestamp() {
        var now = new Date();
        if (CONFIG.TIMESTAMP_FORMAT === 'SHORT') {
            var h = String(now.getHours()).padStart(2, '0');
            var m = String(now.getMinutes()).padStart(2, '0');
            var s = String(now.getSeconds()).padStart(2, '0');
            var ms = String(now.getMilliseconds()).padStart(3, '0');
            return h + ':' + m + ':' + s + '.' + ms;
        }
        return now.toISOString();
    }

    function getStackTrace() {
        try {
            throw new Error('');
        } catch (e) {
            if (!e.stack) return null;
            var lines = e.stack.split('\n');
            var relevantLines = lines.slice(4, 4 + CONFIG.MAX_STACK_DEPTH);
            return relevantLines
                .map(function(line) { return line.trim(); })
                .filter(function(line) { return line.length > 0; })
                .join('\n');
        }
    }

    // ========================================================================
    // CORE LOGGING FUNCTION
    // ========================================================================

    function log(level, module, args) {
        var levelConfig = LOG_LEVELS[level];
        if (!levelConfig) return;

        // Prüfe Debug-Level pro Modul
        var moduleConfig = moduleSettings[module] || { enabled: true, minLevel: 'INFO' };
        if (!moduleConfig.enabled) return;
        if (levelConfig.value < LOG_LEVELS[moduleConfig.minLevel].value) return;

        if (!CONFIG.CONSOLE_OUTPUT) return;

        var timestamp = getTimestamp();
        var prefix = '[' + timestamp + '] [' + module + ']';
        var consoleMethod = levelConfig.console;

        // Console Output
        var consoleArgs = [prefix].concat(Array.prototype.slice.call(args));

        try {
            if (console[consoleMethod]) {
                console[consoleMethod].apply(console, consoleArgs);
            } else {
                console.log.apply(console, consoleArgs);
            }

            // Stack-Trace bei Errors
            if (level === 'ERROR') {
                var stack = getStackTrace();
                if (stack) {
                    if (console.groupCollapsed) {
                        console.groupCollapsed('Stack Trace');
                        console.log(stack);
                        console.groupEnd();
                    } else {
                        console.log('Stack Trace:\n' + stack);
                    }
                }
            }
        } catch (e) {
            // Silent fail für Coherent GT Kompatibilität
        }
    }

    // ========================================================================
    // GLOBAL ERROR HANDLER
    // ========================================================================

    function setupGlobalErrorHandler() {
        // Uncaught Exceptions
        if (typeof window !== 'undefined') {
            window.addEventListener('error', function(event) {
                log('ERROR', 'Global', [
                    'Uncaught Error:', event.message,
                    '\nFile:', event.filename + ':' + event.lineno + ':' + event.colno
                ]);
            });

            // Unhandled Promise Rejections
            window.addEventListener('unhandledrejection', function(event) {
                var reason = event.reason;
                var message = (reason instanceof Error) ? reason.message : String(reason);
                log('ERROR', 'Global', ['Unhandled Promise Rejection:', message]);
            });
        }
    }

    // ========================================================================
    // MODULE FACTORY
    // ========================================================================

    function createModuleLogger(moduleName, options) {
        options = options || {};
        var config = {
            enabled: options.enabled !== false,
            minLevel: options.minLevel || 'INFO',
            prefix: options.prefix || moduleName
        };

        moduleSettings[moduleName] = config;

        return {
            debug: function() { log('DEBUG', config.prefix, arguments); },
            info: function() { log('INFO', config.prefix, arguments); },
            warn: function() { log('WARN', config.prefix, arguments); },
            error: function() { log('ERROR', config.prefix, arguments); },

            setEnabled: function(enabled) { config.enabled = enabled; },
            setMinLevel: function(level) { config.minLevel = level; }
        };
    }

    // ========================================================================
    // PUBLIC API
    // ========================================================================

    // ========================================================================
    // PERFORMANCE LOGGING SYSTEM
    // ========================================================================
    var performanceMetrics = {
        renders: [],
        networkRequests: [],
        cacheStats: { hits: 0, misses: 0 }
    };

    function createPerformanceLogger(moduleName) {
        return {
            startTimer: function(op) {
                return { name: op, start: performance.now(), module: moduleName };
            },
            endTimer: function(timer, meta) {
                var duration = performance.now() - timer.start;
                performanceMetrics.renders.push({
                    module: timer.module,
                    operation: timer.name,
                    duration: duration,
                    timestamp: Date.now(),
                    meta: meta
                });
                // Nur bei langen Operationen warnen (>200ms)
                if (duration > 200) {
                    console.warn('[PERF] Slow:', timer.module + '/' + timer.name, Math.round(duration) + 'ms');
                }
                // Halte das Array klein (max 100 Einträge)
                if (performanceMetrics.renders.length > 100) {
                    performanceMetrics.renders.shift();
                }
                return duration;
            },
            cacheHit: function() { performanceMetrics.cacheStats.hits++; },
            cacheMiss: function() { performanceMetrics.cacheStats.misses++; },
            getReport: function() { return performanceMetrics; }
        };
    }

    // ========================================================================
    // PUBLIC API
    // ========================================================================

    var KneeboardLogger = {
        // Core logging (für allgemeine Nutzung)
        debug: function() { log('DEBUG', 'Kneeboard', arguments); },
        info: function() { log('INFO', 'Kneeboard', arguments); },
        warn: function() { log('WARN', 'Kneeboard', arguments); },
        error: function() { log('ERROR', 'Kneeboard', arguments); },

        // Module factory
        createLogger: createModuleLogger,

        // Performance logging
        createPerformanceLogger: createPerformanceLogger,
        getPerformanceReport: function() {
            console.table(performanceMetrics.renders.slice(-20));
            console.log('Cache Stats:', performanceMetrics.cacheStats);
            return performanceMetrics;
        },

        // Configuration
        configure: function(options) {
            if (options) {
                for (var key in options) {
                    if (options.hasOwnProperty(key) && CONFIG.hasOwnProperty(key)) {
                        CONFIG[key] = options[key];
                    }
                }
            }
        },

        // Version
        VERSION: CONFIG.VERSION,

        // Remote logging to server
        remoteLog: function(level, module, message) {
            try {
                var serverUrl = window.SERVER_URL || 'http://localhost:815';
                fetch(serverUrl + '/api/log', {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/json'
                    },
                    body: JSON.stringify({
                        level: level,
                        module: module,
                        message: message
                    })
                }).catch(function(err) {
                    // Silent fail for remote logging
                });
            } catch (e) {
                // Silent fail
            }
        },

        // Log locally and remotely
        remoteInfo: function(module, message) {
            log('INFO', module, [message]);
            KneeboardLogger.remoteLog('INFO', module, message);
        },
        remoteWarn: function(module, message) {
            log('WARN', module, [message]);
            KneeboardLogger.remoteLog('WARN', module, message);
        },
        remoteError: function(module, message) {
            log('ERROR', module, [message]);
            KneeboardLogger.remoteLog('ERROR', module, message);
        }
    };

    // Initialize
    setupGlobalErrorHandler();

    // Export to global
    global.KneeboardLogger = KneeboardLogger;

    // Backward compatibility: Erstelle globalen logger falls noch nicht vorhanden
    if (!global.logger) {
        global.logger = {
            info: function() { log('INFO', 'Kneeboard', arguments); },
            warn: function() { log('WARN', 'Kneeboard', arguments); },
            error: function() { log('ERROR', 'Kneeboard', arguments); }
        };
    }

})(typeof window !== 'undefined' ? window : this);
