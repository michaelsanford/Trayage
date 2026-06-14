// Fill the Download button's version label from the latest GitHub release.
// Progressive enhancement: the button always links to /releases/latest, so it
// works with JavaScript disabled — this only upgrades the label text.
(function () {
  "use strict";
  var label = document.getElementById("dlver");
  if (!label) {
    return;
  }

  fetch("https://api.github.com/repos/michaelsanford/Trayage/releases/latest", {
    headers: { Accept: "application/vnd.github+json" }
  })
    .then(function (response) {
      return response.ok ? response.json() : null;
    })
    .then(function (release) {
      if (release && release.tag_name) {
        label.textContent = " " + release.tag_name;
      }
    })
    .catch(function () {
      // Leave the default "latest" label in place on any failure.
    });
})();
