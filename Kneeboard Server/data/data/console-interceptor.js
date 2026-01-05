var CONSOLE_INTERCEPTOR = (function() {
  'use strict';

  var originalLog = console.log;
  var originalWarn = console.warn;
  var originalError = console.error;
  var isInitialized = false;

  function extractCategory(firstArg) {
    if (typeof firstArg !== 'string') return null;
    var match = firstArg.match(/^\[([^\]]+)\]/);
    return match ? match[1].toUpperCase().replace(/\s+/g, '_') : null;
  }

  function shouldLog(category) {
    if (typeof DEBUG_CONFIG === 'undefined') return true;
    if (DEBUG_CONFIG.DISABLE_ALL_LOGS) return false;
    
    if (!category) return true;
    
    return DEBUG_CONFIG[category] || DEBUG_CONFIG[category.toUpperCase()] || false;
  }

  function init() {
    if (isInitialized) return;
    isInitialized = true;

    console.log = function() {
      var args = Array.prototype.slice.call(arguments);
      if (args.length === 0) return originalLog.apply(console, args);
      
      var category = extractCategory(args[0]);
      if (category && !shouldLog(category)) return;
      
      originalLog.apply(console, args);
    };

    console.warn = function() {
      var args = Array.prototype.slice.call(arguments);
      if (args.length === 0) return originalWarn.apply(console, args);
      
      var category = extractCategory(args[0]);
      if (category && !shouldLog(category)) return;
      
      originalWarn.apply(console, args);
    };

    console.error = function() {
      var args = Array.prototype.slice.call(arguments);
      if (args.length === 0) return originalError.apply(console, args);
      
      var category = extractCategory(args[0]);
      if (category && !shouldLog(category)) return;
      
      originalError.apply(console, args);
    };
  }

  return {
    init: init,
    shouldLog: shouldLog
  };
})();

CONSOLE_INTERCEPTOR.init();

document.addEventListener('DOMContentLoaded', function() {
  setTimeout(function() {
    CONSOLE_INTERCEPTOR.init();
  }, 0);
});
