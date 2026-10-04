# Third-party components and references

- **Microsoft WebView2 SDK / Runtime**: Microsoft components, governed by the license included in the NuGet package and the Runtime terms. Build downloads the SDK and carries its supplied license/notice files into the local bundle. DLLs and browser user data are not committed here.
- **Rainmeter**: external prerequisite, not bundled or relicensed by this repository. See the [upstream project](https://github.com/rainmeter/rainmeter). The included skin templates and Lua integration are part of this desktop project.
- **Open-Meteo**: weather data/service. See [Open-Meteo](https://open-meteo.com/) for attribution and hosted API terms; this project's MIT license does not grant service access rights.
- **apple-flare-halo-shader** by pancao: visual/shader reference used during the desktop's development. The upstream MIT notice is retained in `licenses/apple-flare-halo-shader-MIT.txt`. See [upstream](https://github.com/pancao/apple-flare-halo-shader).
- **SydeDock visual reference**: interaction direction was inspired by a public demonstration. No endorsement, official affiliation or right to redistribute the reference video's assets is claimed.
- Application icons are generated from programs installed by the end user. No third-party game icons, artwork, music covers, branded wallpapers or reference videos are shipped in this repository.

Legacy Rainmeter panels refer to EasyBlur for optional background effects. That plugin is not bundled; the main WebGL components do not require it. PowerPlugin and Win7AudioPlugin references rely on the corresponding Rainmeter installation. The public package should be tested on the target installation before enabling optional legacy panels.
