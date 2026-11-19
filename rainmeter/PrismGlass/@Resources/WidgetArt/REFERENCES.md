# Fourth iteration: mature widget implementation references

Read directly from the original repositories and official documentation on
2026-11-11. This is a method adaptation into the existing Rainmeter and SVG
implementation, not an installation or claim that those projects run inside
PrismGlass. No third-party image, icon font or remote script is loaded.

## Adopted: JaxCore ModularClocks (MIT)

- [Original project and showcase](https://github.com/Jax-Core/ModularClocks)
- [Main/Main.ini](https://github.com/Jax-Core/ModularClocks/blob/main/Main/Main.ini)
- [Main/Styles/Arc.inc](https://github.com/Jax-Core/ModularClocks/blob/main/Main/Styles/Arc.inc)
- [Main/Styles/CircTech.inc](https://github.com/Jax-Core/ModularClocks/blob/main/Main/Styles/CircTech.inc)
- [License](https://github.com/Jax-Core/ModularClocks/blob/main/LICENSE)

Arc separates static gradient Shape paths from readable time and date. CircTech
keeps the static default update divider disabled and advances time-driven arc
geometry independently of the once-per-second text. The fourth iteration uses
this concrete layering/update pattern for Clock, Hourglass, Tide and Orrery:
fixed geometry is retained, data labels update at their own cadence, and only
running visual layers request frames. The SVG adaptation retains nodes and
changes geometry attributes instead of clearing the tree each second.

We do not copy its AverageSize-based second smoothing: a wrapped second value
can interpolate across the minute boundary incorrectly. Clock interpolates
within a real civil second and resynchronizes every second. We also do not add
the JaxCore installer, Core dependencies, custom fonts or a second settings host.
Attribution is retained in `licenses/JaxCore-MIT.txt` at the project root.

## Adopted: eww official metric example (MIT)

- [Project and original examples](https://github.com/elkowar/eww)
- [examples/eww-bar/eww.yuck](https://github.com/elkowar/eww/blob/master/examples/eww-bar/eww.yuck)
- [License](https://github.com/elkowar/eww/blob/master/LICENSE)

The official `metric` component keeps the live value, action and active state
separate; read-only metrics are inactive rather than pretending to accept a
change. Its example distinguishes a listened-to music value from periodic time
polling. This is applied to shared task/focus summaries, their existing scoped
actions and disabled/unavailable states. Browser frame work stops on hidden
cards/pages or reduced motion; tasks animate briefly only after a real revision.
The Windows implementation retains existing data providers and permissions.
GTK/Rust/Linux command dependencies and the example's system commands are not
included. Attribution is in `licenses/eww-MIT.txt`.

## Evaluated, not copied

- [LuaCalendar](https://github.com/smurfier/LuaCalendar/blob/master/LuaCalendar.ini)
  and [Enigma Calendar](https://github.com/Kaelri/Enigma/blob/master/Skins/Enigma/Sidebar/Calendar/Calendar.ini)
  provide established readable calendar grids. Their noncommercial share-alike
  terms do not match this project's permissive distribution, so their code and
  artwork are not copied. YearDots reuses this project's existing civil-calendar
  calculation and makes day cells readable instead of decorative dots.
- [JaxCore ModularPlayers](https://github.com/Jax-Core/ModularPlayers) is useful
  for an actual media-player module, but it does not solve these timer/task/date
  components. Adding another player stack would duplicate current providers.
- [eww](https://github.com/elkowar/eww) as an installed renderer is not appropriate
  here: these components already have Rainmeter/Direct2D and browser SVG hosts.

## Native primitives and precision boundary

- [Rainmeter Shape](https://docs.rainmeter.net/manual/meters/shape/): existing
  hardware-rendered path, ellipse and gradient primitives; no new renderer.
- [Rainmeter Time](https://docs.rainmeter.net/manual/measures/time/) and
  [MeasureTime.cpp](https://github.com/rainmeter/rainmeter/blob/master/Library/MeasureTime.cpp):
  the numeric default is integer seconds. Merely reducing `Update` does not
  create continuous hands; the new Clock visual layer explicitly interpolates.
- Native active visual updates request a 16 ms cadence. This is not measured
  display FPS and does not satisfy the plan's 120 FPS target by itself.

The calendar and solar position remain still when their real data is unchanged;
the moon does not spin for decoration. Task leaves map current completion ratio,
not a fabricated productivity history. Orbit periods represent elapsed focus
seconds, five minutes and the current session, not astronomical planet positions.
