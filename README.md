# TuringMonitor (Turing Smart Screen Linux)

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![Language: pt-BR](https://img.shields.io/badge/Language-pt--BR-green.svg)](README.pt-BR.md)

**A high-performance .NET 10 System Monitor for 3.5" Turing Smart Screens on Linux (Turing Smart Screen / Revision A). Features Native AOT for ultra-low memory footprint and a fully customizable JSON theme engine.**

![Sample Preview](sample.png)

## 🚀 Key Features

- **Native AOT:** High-performance native binary with ultra-low memory footprint (< 20MB).
- **Theme Engine:** Fully customizable JSON-based layout system with real transparency support.
- **Live Reload:** Calibrate your layout in real-time—save the JSON and see changes on the LCD instantly.
- **Delta Updates:** Intelligent rendering that sends only modified pixels to the hardware for high responsiveness. Overlap-aware redraw ensures dependent elements are refreshed correctly.
- **Full Telemetry:** Real-time data for CPU, GPU (NVIDIA), RAM, Network, and Weather.
- **Auto-Reconnect:** Automatic serial port reconnection with exponential backoff and port re-detection. The LCD recovers on its own after USB disconnects/reconnects.
- **Robust Error Handling:** All failures are logged with appropriate severity (no silent exceptions). The service keeps running even if sensors or the LCD are unavailable.
- **Non-Blocking Weather:** Weather data is fetched in the background (every 30 min) and never stalls the telemetry loop. Falls back gracefully if the API is unreachable.

---

## 🛠️ Quick Installation

### 1. Prerequisites

- **Nobara Linux / Fedora** (Currently developed and tested on Nobara; other distros are not yet tested).
- **.NET 10 SDK** (required for building).
- Dependencies: `libicu`, `libssl`, `libusb`.
- A 3.5" Turing Smart Screen (Revision A).

### 2. Install using Script

We provide a simple installation script that builds the project and sets up a systemd daemon:

```bash
chmod +x install.sh
./install.sh
```

The script will:

1. Build the binary using **Native AOT**.
2. Install the application to `/usr/local/bin/TuringMonitor`.
3. Configure and start a `systemd` service (`turing-monitor.service`).
4. Add your user to the `dialout` group for serial port access.

---

## 🎨 Theme Guide

### Selection

To change the active theme, edit `appsettings.json`:

```json
{
  "Theme": "MyCustomTheme"
}
```

Themes are located in `Assets/Themes/`.

### Creating a Custom Theme

1. Create a new folder in `Assets/Themes/[ThemeName]`.
2. Add a `background.png` (480x320).
3. Create a `theme.json` file.
4. (Optional) Add an `Icons/` folder with weather icons (01d.png, etc).

### Global Configuration (`theme.json`)

| Field | Description |
| :--- | :--- |
| `Background` | Background image file name. |
| `FontPath` | Path to a `.ttf` font file (relative to root). |
| `DebugMode` | If `true`, draws red bounding boxes around elements. |
| `Latitude` / `Longitude` | Geographical coordinates for weather. |
| `WeatherApi` | Weather data provider. Default `openmeteo` (keyless). Optional: `openweather` or `openweathermap` (alias) — requires an API key. |
| `WeatherIconsSource` | Source for weather icons. Default `local` (uses `Icons/{icon}.png` from theme). Optional: `online` — downloads OWM icons to `IconCache/` on first use. Local icons always take precedence and are never overwritten. |
| `OpenWeatherApiKey` | API key for OpenWeather when `WeatherApi=openweather`. Recommended to put the key in `appsettings.json` (`OpenWeatherApiKey`) instead — it has priority over `theme.json`. See [Weather Providers](#weather-providers). |

### Element Types

- `Text`: Renders formatted strings.
- `ProgressBar`: Segmented horizontal bar.
- `Gauge`: 180° segmented arc.
- `Icon`: Dynamic PNG icons based on weather.

---

## 📊 Data Sources

- `CpuName`, `CpuLoad`, `CpuTemp`, `CpuClock`, `CpuPower`
- `GpuModel`, `GpuLoad`, `GpuTemp`, `GpuPower`, `VramString`, `VramPercent`
- `RamString`, `RamPercent`
- `NetInMbps`, `NetOutMbps`, `NetInString`, `NetOutString`
- `WeatherTemp`, `WeatherIcon`
- `DateTime`

### Weather Providers

TuringMonitor supports two weather data providers, selectable per theme via `WeatherApi` in `theme.json`:

| Provider | `WeatherApi` value | API Key | Endpoint |
| :--- | :--- | :--- | :--- |
| **Open-Meteo** (default) | `openmeteo` | none | `api.open-meteo.com/v1/forecast` |
| **OpenWeather** (optional) | `openweather` (or alias `openweathermap`) | required | `api.openweathermap.org/data/2.5/weather` (Current Weather Data 2.5, free tier ~1000 calls/day) |

**API Key resolution**: when `WeatherApi=openweather`, the key is resolved as `appsettings.json:OpenWeatherApiKey` (priority) ?? `theme.json:OpenWeatherApiKey`. To keep your key out of the theme folder (and out of any shared theme download), place it in `appsettings.json` or, preferably, `appsettings.local.json` (gitignored by default — see `.gitignore`):

```json
{
  "OpenWeatherApiKey": "your-api-key-here"
}
```

**Fallback behavior**:

- If the key is missing or invalid (HTTP 401) with `openweather` configured → logs `ERROR`, falls back **permanently** to Open-Meteo for the current session.
- If the OpenWeather call fails transiently (timeout / 5xx / network) → logs `WARNING`, keeps `openweather` as the provider, and uses the last cached value. Does not mix providers within a session.
- If `WeatherApi` is an unknown value → logs `ERROR`, uses `openmeteo`.
- Open-Meteo (`openmeteo`, the default) keeps the existing behavior unchanged.

**Icons**: weather icon codes are native OpenWeather codes (`01d`, `04n`, etc.). When `WeatherIconsSource=online`, icons are downloaded once to `Assets/Themes/[Theme]/IconCache/{icon}.png` and reused from cache. A local icon in `Assets/Themes/[Theme]/Icons/{icon}.png` always takes precedence over the online download and is never overwritten. If the download fails, the local icon is used as fallback; if neither exists, a geometric placeholder is drawn.

---

## ⚖️ License

This project is licensed under the **MIT License**. Created by **bendak**.
