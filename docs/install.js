// Shows an "Install app" button and registers the service worker. Any element with a data-install attribute becomes the button.
(function () {
  var deferred = null;
  var base = document.currentScript ? document.currentScript.src.replace(/install\.js.*$/, "") : "";
  if ("serviceWorker" in navigator) navigator.serviceWorker.register(base + "sw.js").catch(function () {});

  function installed() {
    return (window.matchMedia && (matchMedia("(display-mode: standalone)").matches || matchMedia("(display-mode: fullscreen)").matches)) || navigator.standalone === true;
  }
  function setHidden(h) { document.querySelectorAll("[data-install]").forEach(function (b) { b.hidden = h; }); }

  window.addEventListener("beforeinstallprompt", function (e) { e.preventDefault(); deferred = e; });
  window.addEventListener("appinstalled", function () { deferred = null; setHidden(true); });

  document.addEventListener("click", function (ev) {
    var b = ev.target.closest && ev.target.closest("[data-install]");
    if (!b) return;
    ev.preventDefault();
    if (deferred) {
      deferred.prompt();
      deferred.userChoice.then(function () { deferred = null; });
    } else {
      alert("To install Apex Drive:\n\nChrome or Edge on a computer: click the install icon at the right end of the address bar (or the menu, then Install Apex Drive).\n\niPhone or iPad: tap Share, then Add to Home Screen.\n\nOr download the Windows app from this page.");
    }
  });

  function init() { setHidden(installed()); }
  if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", init); else init();
})();
