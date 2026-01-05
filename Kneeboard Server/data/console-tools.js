var CONSOLE_TOOLS = (function() {
  'use strict';

  function showHelp() {
    console.clear();
    console.log('%c=== KNEEBOARD DEBUG TOOLS ===', 'background: #205d8e; color: white; font-size: 14px; padding: 5px;');
    console.log('\n%c📋 Available Commands:', 'color: #205d8e; font-weight: bold;');
    console.log('%cCONSOLE_TOOLS.config()  %c- Show current debug config', 'color: #4a90e2; font-family: monospace;', 'color: #666;');
    console.log('%cCONSOLE_TOOLS.on(cat)  %c- Enable debug for category (e.g., "RUNWAY", "MAP")', 'color: #4a90e2; font-family: monospace;', 'color: #666;');
    console.log('%cCONSOLE_TOOLS.off(cat) %c- Disable debug for category', 'color: #4a90e2; font-family: monospace;', 'color: #666;');
    console.log('%cCONSOLE_TOOLS.all()    %c- Enable ALL debug logs', 'color: #4a90e2; font-family: monospace;', 'color: #666;');
    console.log('%cCONSOLE_TOOLS.none()   %c- Disable ALL debug logs', 'color: #4a90e2; font-family: monospace;', 'color: #666;');
    console.log('%cCONSOLE_TOOLS.reset()  %c- Reset to default config', 'color: #4a90e2; font-family: monospace;', 'color: #666;');
    console.log('%cCONSOLE_TOOLS.help()   %c- Show this help message', 'color: #4a90e2; font-family: monospace;', 'color: #666;');
    console.log('\n%c📂 Categories (all disabled by default - use CONSOLE_TOOLS.on() to enable):', 'color: #205d8e; font-weight: bold;');
    console.log('MAP, WIND, CZ, FREQ, RUNWAY, SIMCONNECT, WAYPOINTS, TELEPORT, API, WEATHER, NAVLOG, SIMBRIEF, AIRPORTS, NAVLOG_DEBUG, OFP, OFP_DEBUG, OFP_MAPPING, BRIDGE, INIT');
    console.log('\n%cExample: CONSOLE_TOOLS.on("NAVLOG"); // Enable navlog debugging\n', 'color: #666; font-style: italic;');
  }

  function showConfig() {
    console.clear();
    console.log('%c=== DEBUG CONFIG ===', 'background: #205d8e; color: white; font-size: 12px; padding: 3px;');
    var config = DEBUG_MANAGER.getAll();
    Object.keys(config).forEach(function(key) {
      var status = config[key] ? '✅ ON ' : '❌ OFF';
      console.log('  ' + status + '  ' + key.padEnd(15));
    });
    console.log('');
  }

  function setCategory(category, enabled) {
    category = category.toUpperCase();
    DEBUG_MANAGER.set(category, enabled);
    
    var status = enabled ? '✅ ENABLED' : '❌ DISABLED';
    console.log('%c' + status + ' %c' + category, 'color: ' + (enabled ? '#4caf50' : '#f44336') + '; font-weight: bold;', 'color: #666;');
    showConfig();
  }

  function enableAll() {
    DEBUG_MANAGER.toggleAll(true);
    console.log('%c✅ ALL DEBUG LOGS ENABLED', 'color: #4caf50; font-weight: bold; font-size: 12px;');
    showConfig();
  }

  function disableAll() {
    DEBUG_MANAGER.toggleAll(false);
    console.log('%c❌ ALL DEBUG LOGS DISABLED', 'color: #f44336; font-weight: bold; font-size: 12px;');
    showConfig();
  }

  function resetConfig() {
    DEBUG_MANAGER.set('DISABLE_ALL_LOGS', false);
    DEBUG_MANAGER.set('MAP', false);
    DEBUG_MANAGER.set('WIND', false);
    DEBUG_MANAGER.set('CZ', false);
    DEBUG_MANAGER.set('FREQ', false);
    DEBUG_MANAGER.set('RUNWAY', false);
    DEBUG_MANAGER.set('SIMCONNECT', false);
    DEBUG_MANAGER.set('WAYPOINTS', false);
    DEBUG_MANAGER.set('TELEPORT', false);
    DEBUG_MANAGER.set('API', false);
    DEBUG_MANAGER.set('WEATHER', false);
    DEBUG_MANAGER.set('NAVLOG', false);
    DEBUG_MANAGER.set('SIMBRIEF', false);
    DEBUG_MANAGER.set('AIRPORTS', false);
    
    console.log('%c🔄 Config reset to default', 'color: #ff9800; font-weight: bold;');
    showConfig();
  }

  console.log('%c🔧 Kneeboard Debug Tools Ready! Type CONSOLE_TOOLS.help() for commands', 'color: #205d8e; font-weight: bold;');

  return {
    help: showHelp,
    config: showConfig,
    on: function(cat) { setCategory(cat, true); },
    off: function(cat) { setCategory(cat, false); },
    all: enableAll,
    none: disableAll,
    reset: resetConfig
  };
})();
