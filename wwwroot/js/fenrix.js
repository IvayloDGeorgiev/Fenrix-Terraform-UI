// Fenrix theme + UI interop. See docs/24-visual-design-language.md.
window.fenrix = {
  // theme: "light" | "dark" | "system"
  applyTheme: function (theme) {
    var choice = theme || "system";
    var resolved = choice;
    if (choice === "system") {
      resolved = window.matchMedia && window.matchMedia("(prefers-color-scheme: dark)").matches
        ? "dark" : "light";
    }
    document.documentElement.setAttribute("data-theme", resolved);
    try { localStorage.setItem("fenrix.theme", choice); } catch (e) { /* ignore */ }
    return resolved;
  },

  setReducedMotion: function (on) {
    if (on) {
      document.documentElement.setAttribute("data-reduced-motion", "true");
    } else {
      document.documentElement.removeAttribute("data-reduced-motion");
    }
  },

  prefersDark: function () {
    return !!(window.matchMedia && window.matchMedia("(prefers-color-scheme: dark)").matches);
  }
};

// Apply an initial theme early to avoid a flash; the app overrides with the saved value.
(function () {
  try {
    var t = localStorage.getItem("fenrix.theme") || "dark";
    window.fenrix.applyTheme(t);
  } catch (e) {
    window.fenrix.applyTheme("dark");
  }
})();
