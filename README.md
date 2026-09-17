# CyberPunkNetRadar 📡

<img width="517" height="612" alt="Screenshot 2026-09-16 185730" src="https://github.com/user-attachments/assets/7fa1157d-ea31-477f-9819-4f66cb6ef50e" />


<p align="center">
  <strong>A futuristic cyberpunk round network radar widget for Windows desktop.</strong><br>
  Built with <strong>C# (.NET 10)</strong>, <strong>WPF</strong>, and <strong>SkiaSharp</strong> for real-time 60 FPS hardware-accelerated rendering.
</p>

<p align="center">
  <img src="https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011%20(x64)-00F0FF?style=flat-square&logo=windows" alt="Platform Windows" />
  <img src="https://img.shields.io/badge/Framework-.NET%2010%20%7C%20WPF-00FF66?style=flat-square&logo=dotnet" alt=".NET 10" />
  <img src="https://img.shields.io/badge/Graphics-SkiaSharp%202.88.9-FF007F?style=flat-square" alt="SkiaSharp" />
  <img src="https://img.shields.io/badge/License-MIT-FFB000?style=flat-square" alt="License" />
</p>

---

## ⚡ Overview

**CyberPunkNetRadar** monitors live local and remote network connections across your system, measures socket endpoint latency via asynchronous ping probing, and visualizes them on a 60 FPS circular radar screen with phosphor blip decay, rotating sweep beams, on-hover lock-on targeting reticles, and a full retro CRT visual effects suite.

---

## 🌟 Key Features

### 📡 60 FPS Hardware-Accelerated Radar
* **Vector Radar Scope:** Hardware-accelerated SkiaSharp 2D vector rendering with neon bloom shaders, crosshairs, azimuth cardinal ticks ($000^\circ, 090^\circ, 180^\circ, 270^\circ$), and angular degree markings every $15^\circ$.
* **5ms Ultra-Precision Mode:** Concentric distance rings spaced at **1ms, 2ms, 3ms, 4ms, and 5ms** ($1\text{ms}$ step) providing 6x magnification for sub-2ms network sockets (local gateway, router hops, LAN services, and internal APIs).
* **Standard & Extended Ranges:** 30ms Standard (5ms rings), 60ms Extended (10ms rings), 100ms Long Range (20ms rings), 300ms Global Range (50ms rings), and Dynamic Auto-Scaling.

### 💫 Sweep Physics & Phosphor Decay
* **Sweep Beam Duration:** Rotating sweep ray with a bright white-hot core and trailing 55° cone fan with smooth angular gradient decay. Rotation duration is user-adjustable from **10 seconds down to 3 seconds** (with instant presets: 3s, 5s, 8s, 10s).
* **5-Second Phosphor Blip Decay:** Active endpoints illuminate with high bloom intensity upon sweep contact and decay smoothly over 5 seconds based on exponential CRT phosphor persistence curves.

### 🔍 Live Network Telemetry & Process Mapping
* **Socket Discovery:** Native Windows IP Helper API (`iphlpapi.dll`) capturing all active IPv4/IPv6 TCP established sockets and UDP endpoints.
* **Process Attribution:** Automatically resolves owning Process IDs (PIDs) to application names (e.g. `chrome.exe`, `discord.exe`, `steam.exe`).
* **Deterministic Bearing:** FNV-1a hash algorithm deterministically maps each remote IP address to a consistent compass bearing ($0^\circ - 360^\circ$) so connections remain pinned to stable sectors.
* **Latency Probing:** Asynchronous ICMP ping probing engine with caching, concurrency throttling, and synthetic fallback distance for hosts that drop ICMP echo requests.

### 🎯 On-Hover Lock-On Reticle & Target HUD
* Hovering the mouse near any blip renders a rotating cyber lock-on reticle `[ + ]` with a leader line connecting to a HUD metadata card displaying:
  * **IP & Port:** Remote socket address and destination port.
  * **Process Name:** Executable name and Process ID (PID).
  * **Latency:** Real-time roundtrip ping time in milliseconds + Ping Type (`[ICMP]` or `[EST]`).
  * **Protocol & Classification:** TCP/UDP, LAN vs WAN.
  * **Bearing Angle:** Precise compass heading in degrees ($000^\circ - 359^\circ$).

### 📺 Circularly-Clipped Visual FX Suite
* 🌈 **Circular Cyberpunk Rainbow Border:** Rotating multi-stop neon spectrum halo (`Ellipse`) that hugs the outer bezel of the radar screen with adjustable width.
* 📺 **Vintage CRT Scanlines:** Tiled scanlines strictly masked to the circular radar screen via `EllipseGeometry`.
* ⚡ **CRT Glitch & Slices:** Randomized horizontal scan drops, slice tearing, and chromatic aberration bounded inside the circular scope.
* ❄️ **CRT Snow Static:** Animated multi-frame phosphor static noise overlay with adjustable intensity.

### 🎨 Cyberpunk Theme Palettes
* **Electric Cyan** (`#00F0FF`)
* **Matrix Green** (`#00FF66`)
* **Synthwave Magenta** (`#FF007F`)
* **Solar Amber** (`#FFB000`)
* **Phosphor White** (`#F0F0F0`)
* **Glitch Red** (`#FF2244`)
* **Night City Violet** (`#A040FF`)
* 🎨 **Interactive Custom RGB / Hex Color Picker** dialog with live swatch preview.
* 🌈 **RGB Spectrum Cycling** mode with adjustable cycle speed multiplier ($0.2\times - 3.0\times$).

---

## 🚀 Installation & Downloads

### Download Pre-Built Releases
Grab the latest zip package from the [Releases](https://github.com/TheZenHippie/CyberPunkNetRadar/releases) tab:

1. **Standalone Release (`win-x64-standalone.zip`):**
   * Single standalone `.exe` containing the complete .NET runtime.
   * No prerequisites needed — extract and run immediately.
2. **Framework-Dependent Release (`win-x64-framework-dependent.zip`):**
   * Ultra-compact lightweight package (~7.6 MB).
   * Requires [.NET 10.0 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0).

---

## 🕹️ Controls & Navigation

| Action | Control |
| :--- | :--- |
| **Move Radar** | Click and drag anywhere on the radar with the **Left Mouse Button**. |
| **Resize Radar** | Drag the bottom-right resize grip. |
| **Inspect Blip** | Hover mouse cursor over any glowing blip to lock on and view telemetry card. |
| **Open Settings Menu** | **Right-click** anywhere on the radar. |
| **Adjust Sliders** | Drag slider thumb or use the **Mouse Scroll Wheel** over any slider. |

---

## ⚙️ Context Menu Options

Right-click the radar widget to access the menu:
* **Sweep Speed (Duration):** Continuous slider (3.0s – 10.0s) + Presets (3s, 5s, 8s, 10s).
* **Radar Range Mode:**
  * `5 ms Ultra-Precision (1ms Rings)`
  * `30 ms Standard Range (5ms Rings)`
  * `60 ms Extended Range (10ms Rings)`
  * `100 ms Long Range (20ms Rings)`
  * `300 ms Global Range (50ms Rings)`
  * `Auto-Scale (Dynamic)`
* **Phosphor Blip Fade:** Slider (2.0s – 10.0s).
* **HUD Accent Color:** Palette presets + Custom RGB Hex Dialog.
* **RGB Spectrum Cycling:** Toggle + Speed multiplier slider (0.2x – 3.0x).
* **Visual FX Suite:** Circular Rainbow Border, Vintage CRT Scanlines, CRT Glitch, CRT Snow Static.
* **Display Options:** Show/hide process labels, Show/hide digital corner HUD telemetry.
* **Glass Opacity & HUD Brightness:** Sliders (30%–100% and 10%–100%).
* **Window Controls:** Always on Top, Window Shadow, Reset Size (460x460), Close.

All settings, window dimensions, and positions automatically persist to `%APPDATA%\CyberPunkNetRadar\settings.json`.

---

## 🛠️ Building from Source

### Prerequisites
* Windows 10 / 11 (x64)
* [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) or Visual Studio 2026 / Visual Studio Code

### Build & Run
```bash
# Clone repository
git clone https://github.com/TheZenHippie/CyberPunkNetRadar.git
cd CyberPunkNetRadar

# Build and run
dotnet build
dotnet run

# Publish standalone single-file binary
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish/standalone
```

---

## ⚡ Performance & Zero-Allocation Architecture

* **Zero Per-Frame Garbage Collection:** All SkiaSharp paints, shaders, blur mask filters, brushes, and drawing paths (`_segPath`, `_diamondPath`) are pre-allocated and cached in `RadarRenderer`.
* **Hardware Accelerated:** Uses Direct3D hardware-accelerated SkiaSharp rendering via `SkiaSharp.Views.WPF.SKElement` and WPF's `CompositionTarget.Rendering` loop at display refresh rate (60Hz / 120Hz / 144Hz+).
* **Low CPU/Network Overhead:** Background network polling runs on a 1.2s worker thread throttled by a 8-worker semaphore to prevent network socket congestion.

---

## 📄 License

This project is licensed under the **MIT License**.

