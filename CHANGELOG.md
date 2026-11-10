# Changelog

All notable changes to the WinYandex project are documented in this file.

---

## [0.9.80] — 2026-11-09

### Manual Karaoke Lyrics Timing & Sync
* **Individual Lyrics Timing Adjustment**: added a dedicated "Timing" button in the Now Playing drawer for precision alignment between synced lyrics and audio.
* **Offset Range from -60s to +60s**: quick slider controls and step buttons (±0.5s, ±5s, Reset) easily correct timing for songs with non-standard spoken intros or audio desync.
* **Per-Track Persistent Storage**: configured offsets automatically save to local storage per track and apply immediately on future playback.
* **Dynamic Status Badge**: active timing offsets are reflected right on the badge (e.g. "Timing (+2.5 s)").

### Pinned Items Reordering (Drag & Drop)
* **Drag-and-Drop Sidebar Reordering**: pinned albums and playlists in the left navigation sidebar can now be freely rearranged via intuitive mouse drag-and-drop.
* **Persistent Custom Ordering**: user-defined order of pinned playlists and albums persists reliably across app restarts.

### Fluid Animations & Transitions
* **Choreographed Cascade Animations**: artwork, metadata, and track listings now open together with smooth, unified entrance animations.
* **Instant Responsive Navigation**: enhanced navigation responsiveness between sidebar sections, albums, and playlists.

### Now Playing Drawer Refinements
* **Fluid Badge Row Layout**: tags and quality badges (bitrate, synced lyrics, timing offset, mods) now flow naturally without artificial boundary constraints while preserving control alignment.
* **Refined Glass Title Bar**: now playing header visual background gracefully extends beneath Windows caption controls without blocking interaction.

### Game Overlay & System Polish
* **Hardware-Accelerated Overlay Animations**: game overlay card transitions migrated to native WinUI Storyboards, cutting idle GPU and CPU usage.
* **Resource & Memory Optimization**: album art decoding constrained to exact display dimensions and background visualization timers tuned.
* **Non-Blocking Tray Notifications**: system tray notifications prioritize fast native shell balloons, preventing UI thread pauses during track changes.

---

## [0.9.72.1] — 2026-11-03

### Process Lifecycle & Taskbar Launch
* **Fixed Taskbar Activation & Zombie Process Hang**: resolved an issue where clicking the pinned taskbar shortcut failed to launch or open the app while leaving an unresponsive process in Task Manager.
* **Zombie Process Detection & Auto-Recovery**: added automatic detection and termination of unresponsive or orphaned background instances during startup, ensuring fresh launches always succeed.
* **Guaranteed Clean Process Exit**: fixed window closing logic in unpackaged WinUI 3 mode to cleanly dispose background services (OBS Overlay, Discord RPC, Game HUD, Audio streams) and explicitly terminate the process instead of lingering indefinitely.
* **Reliable Window Foregrounding**: implemented thread-input attachment (AttachThreadInput / BringWindowToTop) to bypass Windows OS foreground restrictions when restoring from tray or taskbar shortcuts.

### UI & Alignment
* **Search Input Vertical Centering**: fixed vertical alignment of search and filter inputs across the app (Liked Tracks capsule, Explore and Playlists in-page filters, and main Search page), eliminating upward text offset caused by WinUI 3 template minimum height constraints.

---
## [0.9.72] — 2026-11-03

### Explore & Playlist View
* **Modern Playlist Detail Redesign**: redesigned the playlist and album view in Explore with unified natural scrolling (`ListView.Header`), placing artwork, description, and action controls in a seamless scrollable layout.
* **Refined Action Controls**: modern rounded action buttons for Play All, Shuffle, Like (Favorite), and Pin.
* **In-Playlist Search Filter**: quickly search and filter tracks within any opened playlist or album by track title, artist, or album name.
* **Clean Creator Branding**: normalized creator titles (replacing technical usernames like `yamusic-daily` with clean "Яндекс Музыка" / "Yandex Music").
* **Aligned Column Headers**: added visual column header strip (`TITLE`, `ALBUM`, duration) precisely aligned with track rows.

### Performance & Responsiveness
* **Volume Slider Smoothing**: eliminated lag and stuttering when rapidly dragging the volume slider by debouncing playback state serialization (1000ms) and breaking the slider event echo loop.
* **Rapid Track Skipping Optimization**: separated playback state updates from full library metadata scans, preventing thousands of redundant service queries and UI locks when quickly skipping tracks.
* **Smooth Now Playing Drawer Exit**: eliminated DWM compositor frame stutter during drawer collapse by keeping title bar reconfiguration stable until the animation finishes; fixed 1-frame opacity snap.
* **Instant Page Navigation & Memory Leak Prevention**: enabled page caching across all primary pages (`ExplorePage`, `PlaylistsPage`, `SearchPage`, `WavePage`, `ProfilePage`), eliminating repeated XAML visual tree reconstructions and memory retention.
* **CPU & Idle Optimizations**: optimized synchronized lyrics tracking (skipping redundant line passes) and suspended background JSON serialization in OBS Overlay when no clients are connected.

---

## [0.9.71.1] — 2026-11-03

### Performance & Window Management
* **Eliminated Window Drag Lag ("Jelly" Effect)**: resolved window movement latency and sluggishness by removing premature activation of the background Game Overlay window and removing full-screen transparent rasterization passes over Mica backdrops.
* **Instant Update Navigation**: clicking the update notification banner now immediately displays the new version details, changelog, and download button in Settings without requiring a manual update check.
* **Streamlined TitleBar Transitions**: removed redundant title bar registrations during Now Playing drawer transitions.

---

## [0.9.71] — 2026-11-02

### AI Music Filtering (Slopless Integration)
* **AI Track Detection**: built-in integration with [Slopless](https://github.com/tslyf/slopless-pulsesync) to identify AI-generated music.
* **Automated Actions**: option to automatically skip or dislike generated tracks during playback in My Wave and playlists.
* **Configurable Sensitivity**: choose between track-level and artist-level detection criteria, with an adjustable probability threshold.
* **Visual Badges**: neat probability badges displayed next to generated track titles across the player, Now Playing drawer, and track lists.
* **Optimized Caching**: fast local caching of detection results to prevent redundant network requests.
* **Attribution**: featured link to the original project [tslyf / slopless-pulsesync](https://github.com/tslyf/slopless-pulsesync) in settings.

### Lyrics
* **Mismatched Lyrics Fix**: resolved an issue where lyrics from unrelated songs could appear on instrumental pieces or tracks with short titles.
* **Instrumental Protection**: tracks without lyrics no longer display false or mismatched text.
* **Stricter Matching**: improved verification of titles, artists, and durations when searching for lyrics.

### Search History
* **Reliable History Saving**: search queries are now saved automatically when selecting a result or pausing typing, not just when pressing Enter.
* **Recent Search Suggestions**: clicking the search bar displays your recent queries for quick access.
* **Individual Item Removal**: added the ability to remove specific queries from search history.

### Window Management
* **Window Dragging in Now Playing**: the window can now be smoothly moved by dragging the top bar inside the Now Playing view.
* **Double-Click to Maximize**: double-clicking empty areas of the Now Playing header maximizes or restores the window.

---

## [0.9.7] — 2026-11-01

### Theming System & Visual Customization
* **New Color Themes**:
  * **OLED Black** — deep absolute black background (#000000) for maximum contrast and battery saving on OLED displays.
  * **Midnight Blue** — elegant deep blue night tone.
  * **Sepia Warm** — soft, warm palette for comfortable late-night listening sessions.
  * **Classic Dark & Light** — familiar dark and light Fluent themes.
* **Curated Nordic Accent Palette**:
  * Distinctive Nordic-inspired accents (Nordic Ice, Berry, Emerald, Mint, Gold, Purple, Coral).
  * **Custom HEX Color Picker**: enter any hex color code (#RRGGBB) to personalize your accent tone.
* **Interface Fonts**: choose between Inter, Segoe UI, and Yandex Sans.
* **Window Backdrops**: toggle seamlessly between Mica, Mica Alt, Acrylic, and Solid backgrounds.

### Categorized Settings (Profile & Preferences)
* **Segmented Navigation Bar**: settings are now neatly organized into dedicated categories (Profile, Audio, Appearance & Modules, Integrations, System, Storage, About, and All).
* **Smooth Category Animations**: tactile slide and fade-in transitions when navigating between settings categories.
* **Scroll & Layout Fixes**: resolved dead-zone strips and bottom padding clipping in settings scroll viewers.

### Modular Interface Customization
* **Sidebar Customization**: independently toggle visibility for Search, My Wave, Liked Tracks, and Playlists & Albums in the sidebar.
* **Player Bar Controls**: customize lower player items, including Lyrics, Equalizer, HQ quality badge, and compact volume slider.
* **Home Page Sections**: independently show or hide Home sections ("Curated for You", "New Releases", "Editorial Playlists", and "Chart").

### Smooth Navigation Transitions
* **Fluid Page Transitions**: integrated `EntranceNavigationTransitionInfo` across the entire application shell, providing fluid entrance animations when switching pages, browsing artists, and opening albums.

### Track Row Redesign
* **Dynamic Active Track Glow**: translucent accent tint (12.5% opacity) highlighting the currently playing track, dynamically adapting to your active accent color.
* **Fluent 2 Hover Effects**: translucent hover and selection overlays replacing flat gray boxes.
* **Refined 6px Thumbnails**: track artwork thumbnails and playing equalizer overlays now feature consistent modern 6px rounded corners across all pages.

### Liked Tracks & Playlists Polish
* **Modern Header Action Pills**: replaced clunky rectangular buttons with sleek Fluent 2 pills ("Play All", "Shuffle") with tactile cursors. Removed redundant refresh button.
* **Redesigned Search Capsule**: transformed the search input into a dedicated capsule featuring a left-aligned search icon, concise "Filter tracks..." placeholder, quick-clear `✕` button, and no underline artifact.
* **Seamless Viewport & Spacing**: removed the bottom dead-band gap so track lists scroll flush against the player bar, and balanced top padding (32px) matching Explore and Search pages.

### Game Overlay & Spotify Import
* **Game Overlay**: foundation service for in-game track notifications and overlay display.
* **Spotify Import**: service for migrating and matching Spotify playlists and tracks into Yandex Music.

---

## [0.9.61] — 2026-11-01

### Seamless Playback with VPN & System Proxy
* **No more VPN interruptions**: if you use a VPN or system proxy (Clash, v2rayN, Outline, etc.) for Discord or browsing, WinYandex now automatically routes Yandex requests directly. This eliminates geographic playback errors and access issues (403 Forbidden).
* **Smart automatic fallback**: if your local ISP blocks Yandex services directly, the app automatically switches to routing through your active proxy.
* **Queue skip protection**: if tracks are unavailable in the connected region, the player no longer silently skips through the entire playlist — it halts cleanly and informs you with a helpful toast notification.
* **Full-speed downloads**: saving tracks to disk now also bypasses the proxy to download at your connection's maximum bandwidth.
* **Customizable in settings**: an "Auto Bypass Proxy for Yandex" toggle has been added to the profile settings (enabled by default).

---

## [0.9.60] — 2026-11-01

### Now Playing Widget (OBS Studio)
* **Music on your screen**: you can now display the currently playing track in OBS Studio or Streamlabs using a standard Browser Source with a transparent background.
* **Three visual themes**:
  * **Glassmorphism Card (Glass)** — sleek translucent card with smooth background blur, dynamic artwork glow, live equalizer, and playback timeline.
  * **Vinyl Record (Vinyl)** — retro vinyl turntable deck featuring a record that slides out from the album sleeve and smoothly spins during playback, pausing neatly when you pause.
  * **Compact Pill (Pill)** — ultra-minimalistic pill overlay, perfectly sized to sit right under a webcam.
* **Smooth animations**: track transitions happen smoothly and cinematically without harsh jumps.
* **Interactive preview page**: visit `http://localhost:21337/` in any browser to test themes against different backgrounds (checkerboard, dark, green screen chroma key), toggle auto-hide on pause, and copy the ready-to-use link into OBS in one click.
* **Profile settings toggle**: easily enable/disable the widget or adjust its port directly from the in-app profile settings.

### Seamless My Wave on Album Completion
* **Music never stops**: when the last track of an album finishes playing, the player no longer fades into silence — it automatically transitions into a personalized My Wave stream matching the album's vibe.

---

## [0.9.52] — 2026-10-31

### Audio Equalizer Fixes & Frequency Editing
* **Fixed Frequency Sliders**: resolved a feedback loop where adjusting any equalizer frequency band caused `ProfileViewModel` to immediately reapply the dummy "Custom" preset, instantly snapping all sliders back to 0 dB.
* **Smart Custom Preset Preservation**: user-configured band gains are now persistently saved in `_customGains` and in `%LocalAppData%/YMusic/equalizer_settings.json`. Switching back to "Custom" from built-in presets (Rock, Pop, etc.) accurately restores the user's custom EQ curve instead of resetting gains.
* **Seamless EQ Auto-Activation**: adjusting any frequency band when the equalizer is toggled off now automatically activates the equalizer, immediately routing processed audio through the AudioGraph filter nodes.
* **Batch Update Event Suppression**: added `_isUpdatingBands` guard during preset application and settings loading to eliminate redundant event storms and prevent intermediate inconsistent states.
* **UI Polish**: formatted 0 dB labels cleanly as `"0 dB"`, tuned vertical sliders with `SmallChange="0.5"` / `LargeChange="2"`, and disabled overlay thumb tooltips to keep band labels clear and legible.

---

## [0.9.51] — 2026-10-29

### Instant Track Switching & Playback Optimization
* **Proactive Queue Preloader**: automatically resolves and background-caches upcoming tracks to the local audio disk cache (`%LocalAppData%/YMusic/Cache`) shortly after playback begins. When selecting the next track or skipping forward, playback starts instantaneously from the local SSD/disk (~10–15 ms) without waiting for network roundtrips.
* **In-Memory Stream Caching**: added a thread-safe cache (`_streamInfoCache`) in `YandexMusicService` with connection pooling (`SocketsHttpHandler`) and HTTP/2 support to eliminate redundant network roundtrips for track URLs.
* **Smart Queue Preservation**: clicking tracks within the active playlist or liked songs no longer clears and reshuffles the queue unnecessarily.
* **Immediate Audio Feedback**: when switching to cold tracks, the previous audio and playback timers stop immediately with generation-based cancellation to eliminate race conditions.

### System & Process Name Fix
* Configured assembly and application metadata (`AssemblyName`, `Product`, `AssemblyTitle`, `Description`, `Company`) so the app is correctly identified as **WinYandex** (`WinYandex.exe`) in Windows Task Manager, Process List, and system volume mixer.
* Updated installer, manifests, and documentation references.

---

## [0.9.5] — 2026-10-28

### Pre-Save Albums (Upcoming Releases)
* Added full support for upcoming and unreleased albums across the entire application.
* **Pre-save album page**: dedicated view featuring cover art, artist credits, release date countdown, and one-click pre-save action.
* **Liked Albums integration**: user pre-saved albums appear directly at the top of "Liked Albums" with high priority placement, clear `PRE-SAVE` status badge, and instant state toggle.
* **Search integration**: upcoming and pre-saved albums can now be found directly via Search, including in the Albums section and as the Best Result.
* **Fluent Design styling**: translucent acrylic violet styling with subtle borders and badges (`#2A8B5CF6` / `#C4B5FD`) designed to blend seamlessly into the UI.

### Instant Live Search in Navigation Bar
* **Search on first keystroke**: typing even a single character in the sidebar search box immediately triggers a search without requiring Enter.
* **Seamless navigation & persistent focus**: automatically routes to the Search page without losing keyboard focus or interrupting typing.
* **Bidirectional synchronization**: search queries stay perfectly synced in real time between the sidebar and the Search page input.
* **Fast response**: reduced search debounce to 240ms with immediate cancellation of stale queries for snappy results.

### Playback & Visual Transitions
* Replaced abrupt strobe track switching in Now Playing drawer with a smooth crossfade and subtle zoom transition.
* Added IDE F5 debugging configuration in `.vscode` (`tasks.json` and `launch.json`).

---

## [0.9.4] — 2026-10-24

### Track Context Menu
* Added a comprehensive track context menu accessible via right-click and "···" button across all views (Playlists, Liked Tracks, Search, Artist Page, Explore, Player Bar, and Now Playing drawer).
* **"My Wave by track"**: launch a personalized My Wave stream tuned to any selected track. If the track is currently playing, playback continues smoothly without interruption while the upcoming queue seamlessly updates to the wave stream.
* **Queue management**: added "Play next" (insert immediately after the current track) and "Add to end of queue".
* **"Don't like" (Dislike)**: added full dislike support synchronized with Yandex Music cloud. Disliked tracks are visually dimmed (semi-transparent) across all lists, and disliking the currently playing track skips it automatically.
* **"Add to playlist"**: dynamic submenu listing all user playlists with instant track addition via API.
* **"Share"**: instant copying of web track links to clipboard.
* **"Download to file"**: direct download of audio tracks to the user's `Downloads` folder with clean tagging (`Artist - Title.m4a` / `mp3`).
* **"Track details"**: modal dialog displaying comprehensive metadata (title, artists, album, year, duration, bitrate, codec, and track ID).
* **FckCensor integration**: quick options to replace censored tracks with local files, remove existing replacements, or report censorship directly from any list.

### UI & Notifications
* Added a global floating toast notification pill at the bottom of the window to confirm user actions (track added to playlist, link copied, file downloaded, added to queue, etc.).
* Visual dimming for disliked tracks across all track lists.
* Complete localization (English and Russian) for all new context menu items, toasts, and dialogs.

---

## [0.9.3] — 2026-10-24

### UI & Update System
* Improved visual styling of the Update Available card in settings: eliminated low-contrast white text on accent background, redesigned as a clean Fluent Card with accent border and dynamic system theme brushes.
* Added smooth scrolling (`ScrollViewer`) for the entire changelog list directly within the update card.
* Fixed update navigation from the main player notification banner: clicking the update button now smoothly scrolls down to the update section in settings.
* Seamless changelog integration: GitHub releases and the in-app update service now publish formatted release notes directly from `CHANGELOG.md` instead of a generic compare link.
* Added automatic cleanup of downloaded update installer files after installation and on app startup to reclaim disk space.

---

## [0.9.2] — 2026-10-24

### System Tray & App Lifecycle (Single Instance)
* Fixed re-opening the application when "hide to tray on close" is enabled: clicking desktop shortcuts or pinned taskbar icons now restores and activates the existing tray instance instead of launching a duplicate process.
* Implemented cross-process activation redirection (Windows AppLifecycle Single Instance) with proper window foreground handoff (`AllowSetForegroundWindow` / `SwitchToThisWindow`).

### Audio Cache & Storage
* Added ability to select custom directories and any drive (e.g. D:, E:) for audio cache storage, removing the restriction to drive C:.
* Added custom directory picker button with native Windows Explorer dialog.
* Added safe automated migration of previously cached tracks to the new location (with user confirmation).
* Added quick reset button to restore the default cache path on the system drive.
* Dynamic cache size calculation now accurately accounts for storage across all connected drives.

---

## [0.9.1] — 2026-10-24

### UI & Animations
* Added smooth entry and exit animations for full-screen Now Playing mode (slide up, fade, and subtle album art scale).
* Eliminated layout shifts and content jumping in the main panel when toggling Now Playing mode.
* Added smooth opening and closing animations for albums and playlists.
* Added crossfade transition when switching between albums.
* Fixed track table flickering during data loading.
* Replaced playback icon near "My Vibe" header with a neutral audio wave icon.
* Increased navigation sidebar width to 250 px to prevent text clipping.
* Added ability to like and unlike (add/remove from favorites) albums and playlists in headers and context menus.
* Added artist picker context menu when clicking tracks with multiple artists (in player bar, Now Playing, and track lists).
* Hid back button when navigating to pinned albums or playlists directly from the sidebar.
* Prevented re-triggering animation and reloading when clicking already opened albums or playlists.
* Fixed back navigation from albums: now returns directly to the originating page (including artist page) without intermediate playlist library display.
* Fixed quality badges and text clipping in Now Playing mode.
* Fixed clipping of Mute, equalizer, and lyrics buttons in the player bar.

### Audio Controls
* Increased volume slider width and added smooth mouse wheel adjustment.
* Added manual volume percentage input by clicking the numeric value.
* Refined speaker icon state for low volume levels.

### Performance
* Added in-memory caching for albums and playlists, eliminating redundant network calls.

### FckCensor v2
* Support for `list_v2.json` database.
* Restored blocked tracks in albums (AlbumSpoof).
* Re-uploaded track injection into artist pages (ArtistInsertion).
* Priority for synchronized LRCLIB lyrics on replaced tracks.
* Automatic inheritance of album art for recovered tracks.
* Dynamic audio stream duration detection and seek bar unlocking for blocked tracks.
* Added attribution links to original FckCensor repository in settings and context menus.

### Authentication & Session
* Added external system browser authentication with clipboard token capture.
* Added SSL security indicator badge to embedded login window and improved text wrapping.
* Clean playback stop, player collapse, and pinned items concealment on account logout.

---

## [0.9.0] — 2026-10-23

### Release
* Native Yandex Music desktop client built with WinUI 3 and .NET 8.
* "My Vibe" personalized stream support with dynamic moods and filters.
* Playback for tracks, playlists, albums, and artist discographies.
* Built-in 10-band equalizer with audio presets.
* Real-time synchronized lyrics display.
* Local disk LRU cache with gapless track preloading.
* Integrated FckCensor module for censored track replacement.
* Full integration with Windows SMTC, system tray, and Discord Rich Presence.
* Light and dark themes with Mica material effects.
* Global hotkeys and media key control.
