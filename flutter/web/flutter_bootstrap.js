{{flutter_js}}
{{flutter_build_config}}

// Fallback fonts (for characters the bundled faces lack) are looked up on this server, never a
// font CDN: a self-hosted page loads nothing from another origin.
_flutter.loader.load({
  config: {
    fontFallbackBaseUrl: "assets/fallback-fonts/",
  },
});
