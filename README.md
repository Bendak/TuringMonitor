# System Monitor LCD Daemon (.NET 10)

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![Language: pt-BR](https://img.shields.io/badge/Language-pt--BR-green.svg)](README.pt-BR.md)

An ultra-lightweight system monitor for Linux, built with **.NET 10** and **Native AOT**, specifically designed for 3.5" Chinese USB LCD displays (Turing Smart Screen / Revision A).

## 🚀 Highlights
- **Native AOT:** High-performance native binary with ultra-low memory footprint (< 20MB).
- **Pro Theme Engine:** Fully customizable JSON-based theme system with real transparency support (Alpha Blending).
- **Live Reload:** Calibrate your layout in real-time—save the JSON and see changes on the LCD instantly without restarting.
- **Delta Updates:** Intelligent rendering that sends only modified pixels to the hardware, ensuring high responsiveness.
- **Deep Telemetry:** Real-time data for CPU, GPU (NVIDIA), RAM, Network, and Weather.

---

## 🎨 Theme Guide (theme.json)

The theme file is located at `Assets/Themes/[Name]/theme.json`.

### Global Configuration
| Field | Description |
| :--- | :--- |
| `Background` | Background image file name (480x320). |
| `FontPath` | Path to a system `.ttf` font file. |
| `DebugMode` | If `true`, draws red bounding boxes around elements for calibration. |
| `Latitude` / `Longitude` | Geographical coordinates for weather forecasting. |

### Element Types
- **`Text`**: Renders formatted strings.
- **`ProgressBar`**: Segmented progress bar.
- **`Gauge`**: 180° segmented arc (speedometer style).
- **`Icon`**: Loads dynamic PNGs from the `Icons/` folder based on sensor data.

### Element Properties
| Property | Description |
| :--- | :--- |
| `Source` | Data source name (see list below). |
| `X`, `Y` | Position on the display (0-479, 0-319). |
| `Width`, `Height` | Dimensions of the update area. |
| `Color` | Element color (Hex: `#00ffff` or name: `cyan`). |
| `OffColor` | Color of inactive blocks (or `transparent`). |
| `Alignment` | Text alignment (`Left`, `Center`, `Right`). |
| `Multiplier` | Multiplier for raw values (e.g., `0.001` to convert MHz to GHz). |
| `Format` | C# format mask (e.g., `{0:F1} GHz` or `{0:HH:mm}`). |
| `Blocks` | Number of segments for bars and gauges. |
| `ShowPercentage` | (ProgressBar) Displays the `%` text aligned to the right. |

---

## 📊 Data Sources (Sources)

### CPU
- `CpuName`: Friendly processor name.
- `CpuLoad`: Total CPU usage percentage.
- `CpuTemp`: Real-time temperature (Tctl/Package).
- `CpuClock`: Current maximum frequency (MHz).
- `CpuPower`: Power consumption in Watts (via RAPL).

### GPU (NVIDIA)
- `GpuModel`: Short model name (e.g., RTX 4090).
- `GpuLoad`: Core utilization percentage.
- `GpuTemp`: GPU temperature.
- `GpuPower`: Power draw in Watts.
- `VramString`: Formatted "Used / Total GB" string.
- `VramPercent`: VRAM usage percentage.

### Others
- `RamString`: Formatted "Used / Total GB" string.
- `RamPercent`: RAM usage percentage.
- `NetInString` / `NetOutString`: Real-time network speed (e.g., "500 Mbps").
- `WeatherTemp`: Current outside temperature.
- `WeatherIcon`: Weather condition ID (Day/Night compatible).
- `DateTime`: Full timestamp object for custom date/time formatting.

---

## 🏗️ Getting Started

1.  **Permissions:** Grant access to the serial port: `sudo chmod 666 /dev/ttyACM0`
2.  **Run:** Execute `dotnet run` inside the `LcdDisplay` folder.
3.  **Production:** Publish as a native binary: `dotnet publish -c Release`

## ⚖️ License
This project is licensed under the **MIT License**.
