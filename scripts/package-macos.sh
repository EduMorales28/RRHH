#!/usr/bin/env bash
set -euo pipefail

RID="${1:-osx-arm64}"
CONFIGURATION="${2:-Release}"
APP_NAME="Barraca RRHH"
PROJECT="src/Barraca.RRHH.App.Mac/Barraca.RRHH.App.Mac.csproj"
EXECUTABLE_NAME="Barraca.RRHH.App.Mac"
OUT_ROOT="dist/macos-${RID}"
PUBLISH_DIR="${OUT_ROOT}/publish"
APP_DIR="${OUT_ROOT}/${APP_NAME}.app"
DMG_PATH="${OUT_ROOT}/${APP_NAME// /-}-${RID}.dmg"

mkdir -p "${OUT_ROOT}"

echo "[1/4] Publicando ${PROJECT} para ${RID}"
dotnet publish "${PROJECT}" -c "${CONFIGURATION}" -r "${RID}" --self-contained true /p:PublishSingleFile=true -o "${PUBLISH_DIR}"

echo "[2/4] Armando bundle .app"
rm -rf "${APP_DIR}"
mkdir -p "${APP_DIR}/Contents/MacOS" "${APP_DIR}/Contents/Resources"
cp -R "${PUBLISH_DIR}/." "${APP_DIR}/Contents/MacOS/"

cat > "${APP_DIR}/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key>
  <string>${APP_NAME}</string>
  <key>CFBundleDisplayName</key>
  <string>${APP_NAME}</string>
  <key>CFBundleIdentifier</key>
  <string>uy.barraca.rrhh</string>
  <key>CFBundleVersion</key>
  <string>1.0.0</string>
  <key>CFBundleShortVersionString</key>
  <string>1.0.0</string>
  <key>CFBundlePackageType</key>
  <string>APPL</string>
  <key>CFBundleExecutable</key>
  <string>${EXECUTABLE_NAME}</string>
  <key>LSMinimumSystemVersion</key>
  <string>12.0</string>
</dict>
</plist>
PLIST

chmod +x "${APP_DIR}/Contents/MacOS/${EXECUTABLE_NAME}" || true

echo "[3/4] Creando DMG"
rm -f "${DMG_PATH}"
hdiutil create -volname "${APP_NAME}" -srcfolder "${APP_DIR}" -ov -format UDZO "${DMG_PATH}"

echo "[4/4] Resultado"
echo "APP: ${APP_DIR}"
echo "DMG: ${DMG_PATH}"
