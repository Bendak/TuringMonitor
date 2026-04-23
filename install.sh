#!/bin/bash

# TuringMonitor Installation Script
# This script builds and installs TuringMonitor as a systemd service.

set -e

APP_NAME="TuringMonitor"
INSTALL_DIR="/usr/local/bin/$APP_NAME"
CONFIG_DIR="/etc/$APP_NAME"
ASSETS_DIR="/usr/local/share/$APP_NAME"
SERVICE_FILE="/etc/systemd/system/turing-monitor.service"

echo "🚀 Starting installation of $APP_NAME..."

# Check for dotnet 10
if ! dotnet --version | grep -q "^10\."; then
    echo "❌ Error: .NET 10 SDK is required but not found."
    exit 1
fi

# Build the project (Native AOT)
echo "📦 Building $APP_NAME (Native AOT)..."
dotnet publish -c Release -r linux-x64 --self-contained true

# Create directories
echo "📂 Creating installation directories..."
sudo mkdir -p "$INSTALL_DIR"
sudo mkdir -p "$CONFIG_DIR"
sudo mkdir -p "$ASSETS_DIR"

# Copy binary and assets
echo "📝 Copying binary and assets..."
sudo cp bin/Release/net10.0/linux-x64/publish/TuringMonitor "$INSTALL_DIR/"
sudo cp bin/Release/net10.0/linux-x64/publish/*.so "$INSTALL_DIR/" 2>/dev/null || true
sudo cp appsettings.json "$INSTALL_DIR/"
sudo cp -r Assets "$INSTALL_DIR/"

# Create systemd service
echo "⚙️ Creating systemd service..."
sudo bash -c "cat > $SERVICE_FILE" <<EOF
[Unit]
Description=Turing Smart Screen Monitor Service
After=network.target

[Service]
Type=simple
WorkingDirectory=$INSTALL_DIR
ExecStart=$INSTALL_DIR/TuringMonitor
Restart=always
RestartSec=5
# Ensure the service can access the serial port
Group=dialout

[Install]
WantedBy=multi-user.target
EOF

# Fix permissions
echo "🛡️ Setting up permissions..."
sudo usermod -a -G dialout $USER
sudo chmod +x "$INSTALL_DIR/TuringMonitor"

# Reload and enable service
echo "🔄 Enabling and starting service..."
sudo systemctl daemon-reload
sudo systemctl enable turing-monitor.service

echo "✅ Installation complete!"
echo "💡 You can start the service with: sudo systemctl start turing-monitor"
echo "💡 To view logs: journalctl -u turing-monitor -f"
echo "⚠️ Note: You might need to logout and login again for the 'dialout' group changes to take effect."
