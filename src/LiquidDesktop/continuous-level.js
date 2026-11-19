// Shared by desktop WebView2 and the companion browser. No device IO runs here.
(function (root) {
  'use strict';
  root.PrismContinuousLevel = function (send, interval) {
    var latest = null, timer = 0, inFlight = false, lastAt = 0, generation = 0;
    function flush() {
      clearTimeout(timer); timer = 0;
      if (inFlight || latest === null) return;
      var value = latest, epoch = generation; latest = null; inFlight = true; lastAt = Date.now();
      Promise.resolve().then(function () { if (epoch === generation) return send(value); }).catch(function () {}).then(function () {
        inFlight = false; if (latest !== null) schedule();
      });
    }
    // Release/change events also obey the interval: held keyboard arrows can
    // produce many change events, and their final value must not flood the API.
    function schedule() { if (inFlight) return; clearTimeout(timer); timer = setTimeout(flush, Math.max(0, interval - (Date.now() - lastAt))); }
    return {
      set: function (value) { latest = value; schedule(); },
      cancel: function () { generation++; latest = null; clearTimeout(timer); timer = 0; },
      busy: function () { return inFlight || latest !== null; }
    };
  };
})(window);
