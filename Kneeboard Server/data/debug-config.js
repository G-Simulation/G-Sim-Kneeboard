var DEBUG_MANAGER = (function() {
  'use strict';
  
  var DEFAULT_CONFIG = {
    DISABLE_ALL_LOGS: false,
    MAP: false,
    WIND: false,
    CZ: false,
    FREQ: false,
    RUNWAY: false,
    SIMCONNECT: false,
    WAYPOINTS: false,
    TELEPORT: false,
    API: false,
    WEATHER: false,
    NAVLOG: false,
    SIMBRIEF: false,
    AIRPORTS: false,
    DOCUMENTS: false,
    FORMULAS: false
  };

  var localConfig = null;
  var serverUrl = null;

  function getServerUrl() {
    if (serverUrl) return serverUrl;
    
    if (window.location.protocol === 'http:' || window.location.protocol === 'https:') {
      serverUrl = window.location.protocol + '//' + window.location.host;
    } else {
      serverUrl = 'http://localhost:8000';
    }
    return serverUrl;
  }

  function initFromLocalStorage() {
    try {
      var saved = localStorage.getItem('debugConfig');
      if (saved) {
        localConfig = JSON.parse(saved);
        return true;
      }
    } catch(e) {
      // Silent fail - localStorage nicht verfügbar
    }
    return false;
  }

  function saveToLocalStorage() {
    try {
      localStorage.setItem('debugConfig', JSON.stringify(localConfig));
    } catch(e) {
      // Silent fail - localStorage nicht verfügbar
    }
  }

  function loadFromServer() {
    return fetch(getServerUrl() + '/api/debug/config')
      .then(function(response) {
        if (!response.ok) throw new Error('HTTP ' + response.status);
        return response.json();
      })
      .then(function(config) {
        localConfig = config;
        saveToLocalStorage();
        return true;
      })
      .catch(function(err) {
        // Silent fail - Server nicht erreichbar
        return false;
      });
  }

  function applyConfig() {
    if (!localConfig) {
      localConfig = DEFAULT_CONFIG;
    }
    
    if (typeof DEBUG_CONFIG !== 'undefined') {
      Object.assign(DEBUG_CONFIG, localConfig);
    }
  }

  function init() {
    var hasLocal = initFromLocalStorage();
    applyConfig();
    
    if (!hasLocal) {
      loadFromServer().then(function() {
        applyConfig();
      });
    }
  }

  function set(category, enabled) {
    if (!localConfig) localConfig = DEFAULT_CONFIG;
    
    localConfig[category] = enabled;
    if (typeof DEBUG_CONFIG !== 'undefined') {
      DEBUG_CONFIG[category] = enabled;
    }
    saveToLocalStorage();
    
    fetch(getServerUrl() + '/api/debug/config', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ [category]: enabled })
    }).catch(function(err) {
      // Silent fail
    });
  }

  function get(category) {
    if (!localConfig) localConfig = DEFAULT_CONFIG;
    return localConfig[category] !== false;
  }

  function getAll() {
    if (!localConfig) localConfig = DEFAULT_CONFIG;
    return Object.assign({}, localConfig);
  }

  function toggleAll(enabled) {
    var newConfig = {};
    Object.keys(localConfig || DEFAULT_CONFIG).forEach(function(key) {
      newConfig[key] = enabled;
    });
    localConfig = newConfig;
    applyConfig();
    saveToLocalStorage();
    
    fetch(getServerUrl() + '/api/debug/config', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(newConfig)
    }).catch(function() {
      // Silent fail
    });
  }

  return {
    init: init,
    set: set,
    get: get,
    getAll: getAll,
    toggleAll: toggleAll,
    loadFromServer: loadFromServer
  };
})();

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', function() {
    DEBUG_MANAGER.init();
  });
} else {
  DEBUG_MANAGER.init();
}
