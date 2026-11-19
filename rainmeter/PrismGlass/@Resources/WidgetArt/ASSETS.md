# Widget foregrounds

The fourth experience iteration (2026-11-11) replaces both generated foreground
images with retained, data-driven geometry. `WidgetArt.lua` and
`companion-widgets.js` now draw the vessel, sand, falling grains, branches,
individual leaves, planter, orbits, ribbons, solar arc and calendar cells.
There are no image meters or SVG image nodes in these six components.

`hourglass-shell.png` and `bonsai.png` were project-original transparent images
generated with the built-in image tool on 2026-11-10. They were used in the
previous iteration and are no longer an implementation or acceptance baseline.
Their removal from packaging and the retired endpoint is coordinated with the
managed upgrade; they must not be silently substituted back into the live views.

The existing Moon component retains its separately documented astronomical
surface texture. Its illuminated shape is computed from the actual date using
the existing SunCalc adaptation. It is not a replacement for a live phase model.

Primitives and retained-update methods, adopted and rejected mature widget
implementations, exact source paths and license boundaries are recorded in
[REFERENCES.md](REFERENCES.md). Functional header icons retain the project's
original white line language. Subject motion changes geometry or true state;
there is no whole-image rotation or idle wallpaper animation.
