mergeInto(LibraryManager.library, {
  // kind: 1 play-by-play commentator, 2 team radio, 3 second commentator
  ApexSpeak: function (textPtr, kind) {
    try {
      if (typeof window === 'undefined' || !window.speechSynthesis) return;
      var text = UTF8ToString(textPtr);
      if (!text) return;
      var synth = window.speechSynthesis;
      var all = synth.getVoices() || [];
      var en = all.filter(function (v) { return /^en/i.test(v.lang); });
      function pick(res) {
        for (var i = 0; i < res.length; i++) {
          for (var j = 0; j < en.length; j++) if (res[i].test(en[j].name) || res[i].test(en[j].lang)) return en[j];
        }
        return en.length ? en[0] : null;
      }
      var voice;
      if (kind === 2) voice = pick([/david|mark|guy|ryan|google us/i, /en-US/i]);
      else if (kind === 3) voice = pick([/hazel|susan|libby|sonia|female|zira|google uk english female/i, /en-GB/i]);
      else voice = pick([/daniel|george|ryan|google uk english male/i, /en-GB/i]);
      var u = new SpeechSynthesisUtterance(text);
      if (voice) u.voice = voice;
      u.rate = kind === 2 ? 1.12 : 1.05;
      u.pitch = kind === 3 ? 1.1 : (kind === 2 ? 0.9 : 0.95);
      u.volume = kind === 2 ? 0.85 : 1.0;
      if (kind === 2) synth.cancel();          // the radio cuts across
      synth.speak(u);
    } catch (e) { }
  },
  ApexSpeakStop: function () {
    try { if (window.speechSynthesis) window.speechSynthesis.cancel(); } catch (e) { }
  },
  // render the canvas at a fraction of its size (the page stretches it): a cheap way to speed up weak graphics cards
  ApexSetScale: function (scale) {
    try {
      var m = (typeof Module !== 'undefined') ? Module : null;
      if (m) { m.devicePixelRatio = Math.max(0.3, (window.devicePixelRatio || 1) * scale); }
      window.dispatchEvent(new Event('resize'));
    } catch (e) { }
  }
});
