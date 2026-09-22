#!/usr/bin/env bash
set -euo pipefail

# ==========================================
# Wiremote Linux Setup & Autostart Script
# ==========================================

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SERVICE_NAME="wiremote.service"

echo "=========================================="
echo "          Wiremote Linux Setup            "
echo "=========================================="

# 1. Ellenőrizzük a dotnet meglétét
if ! command -v dotnet &>/dev/null; then
    echo "[-] Hiba: A .NET SDK (10.0+) nincs telepítve vagy nem található a PATH-ban!"
    echo "    Kérlek telepítsd a .NET SDK-t: https://dotnet.microsoft.com/download"
    exit 1
fi

DOTNET_VER="$(dotnet --version)"
echo "[+] .NET SDK észlelve: $DOTNET_VER"

# 2. Ha már fut a szolgáltatás, állítsuk le a buildelés idejére (ETXTBSY / busy binary hiba elkerülése)
if systemctl --user is-active --quiet "$SERVICE_NAME" 2>/dev/null; then
    echo "[*] Korábbi $SERVICE_NAME leállítása a frissítéshez..."
    systemctl --user stop "$SERVICE_NAME" || true
fi

# 3. Release build & publish
echo "[*] Wiremote fordítása és közzététele (Release / Publish)..."
dotnet publish "$SCRIPT_DIR/Wiremote.csproj" -c Release

# 4. Publish könyvtár és bináris felderítése
PUBLISH_DIR="$SCRIPT_DIR/bin/Release/net10.0/linux-x64/publish"
if [ ! -f "$PUBLISH_DIR/Wiremote" ]; then
    FOUND_BIN=$(find "$SCRIPT_DIR/bin/Release" -type f -name "Wiremote" -path "*/publish/*" 2>/dev/null | head -n 1)
    if [ -n "$FOUND_BIN" ]; then
        PUBLISH_DIR="$(dirname "$FOUND_BIN")"
    else
        echo "[-] Hiba: A lefordított Wiremote bináris nem található!"
        exit 1
    fi
fi

chmod +x "$PUBLISH_DIR/Wiremote"
echo "[+] Futtatható állomány készen áll: $PUBLISH_DIR/Wiremote"

# 5. Szimbolikus link a ~/.local/bin mappába
mkdir -p "$HOME/.local/bin"
ln -sf "$PUBLISH_DIR/Wiremote" "$HOME/.local/bin/wiremote"
echo "[+] Parancsikon létrehozva: $HOME/.local/bin/wiremote"

# 6. Systemd user service konfigurálása
SYSTEMD_USER_DIR="$HOME/.config/systemd/user"
mkdir -p "$SYSTEMD_USER_DIR"

cat <<EOF > "$SYSTEMD_USER_DIR/$SERVICE_NAME"
[Unit]
Description=Wiremote Remote Control Service
After=network.target sound.target

[Service]
Type=simple
WorkingDirectory=${PUBLISH_DIR}
ExecStart=${PUBLISH_DIR}/Wiremote
Restart=always
RestartSec=3

[Install]
WantedBy=default.target
EOF

echo "[+] Systemd user service fájl létrehozva: $SYSTEMD_USER_DIR/$SERVICE_NAME"

# 7. Felhasználói linger engedélyezése (gép bekapcsolásakor azonnal elindul)
echo "[*] Felhasználói linger beállítása (rendszerinduláskor automatikus futás)..."
loginctl enable-linger "$USER" 2>/dev/null || loginctl enable-linger 2>/dev/null || true

# 8. Systemd daemon frissítése és service indítása
systemctl --user daemon-reload
systemctl --user enable --now "$SERVICE_NAME"

echo "[+] $SERVICE_NAME sikeresen engedélyezve és elindítva!"

# 9. Tűzfal ellenőrzés és figyelmeztetés
echo ""
echo "[*] Tűzfal (ports 8765, 8766) ellenőrzése..."
if command -v firewall-cmd &>/dev/null && firewall-cmd --state &>/dev/null; then
    PORTS=$(firewall-cmd --list-ports 2>/dev/null || true)
    if [[ "$PORTS" != *"8765"* ]] && [[ "$PORTS" != *"1025-65535"* ]]; then
        echo "  [!] Figyelem: A tűzfalon szükség lehet a portok megnyitására:"
        echo "      sudo firewall-cmd --add-port=8765/tcp --add-port=8766/tcp --permanent && sudo firewall-cmd --reload"
    else
        echo "  [+] A tűzfal engedélyezi a szükséges portokat."
    fi
elif command -v ufw &>/dev/null && ufw status | grep -qw "active"; then
    echo "  [!] Ha nem éred el telefonról, nyisd meg a portokat UFW-ben:"
    echo "      sudo ufw allow 8765/tcp && sudo ufw allow 8766/tcp"
fi

# 10. Kis várakozás az indulásra és URL kiírása
sleep 2

echo ""
echo "=========================================="
echo "          Wiremote Futási Állapot         "
echo "=========================================="
systemctl --user status "$SERVICE_NAME" --no-pager -l || true

echo ""
echo "[*] Legutóbbi naplóbejegyzések (Token és Cím):"
journalctl --user -u "$SERVICE_NAME" -n 12 --no-pager || true

echo ""
echo "[+] Telepítés kész! A Wiremote mostantól automatikusan fut rendszerinduláskor."
echo "    Kezelés:"
echo "      systemctl --user status wiremote"
echo "      systemctl --user restart wiremote"
echo "      systemctl --user stop wiremote"
echo "      journalctl --user -u wiremote -f"
