#!/usr/bin/env bash
set -euo pipefail

RID="${1:-osx-arm64}"
CONFIGURATION="${2:-Release}"
CREATE_DMG="${3:-false}"
APP_NAME="Barraca RRHH"
PROJECT="src/Barraca.RRHH.App.Mac/Barraca.RRHH.App.Mac.csproj"
EXECUTABLE_NAME="Barraca.RRHH.App.Mac"
OUT_ROOT="dist/macos-${RID}"
PUBLISH_DIR="${OUT_ROOT}/publish"
APP_DIR="${OUT_ROOT}/${APP_NAME}.app"
DMG_PATH="${OUT_ROOT}/${APP_NAME// /-}-${RID}.dmg"
ZIP_PATH="${OUT_ROOT}/${APP_NAME// /-}-${RID}.zip"
ICON_SOURCE="src/Barraca.RRHH.App.Mac/Assets/app-icon.png"
ICONSET_DIR="${OUT_ROOT}/AppIcon.iconset"
ICON_NAME="AppIcon.icns"
ICON_PATH="${APP_DIR}/Contents/Resources/${ICON_NAME}"
MACOS_CODESIGN_IDENTITY="${MACOS_CODESIGN_IDENTITY:-}"
MACOS_NOTARY_APPLE_ID="${MACOS_NOTARY_APPLE_ID:-}"
MACOS_NOTARY_PASSWORD="${MACOS_NOTARY_PASSWORD:-}"
MACOS_NOTARY_TEAM_ID="${MACOS_NOTARY_TEAM_ID:-}"

mkdir -p "${OUT_ROOT}"

echo "[1/5] Restore ${PROJECT} para ${RID}"
dotnet restore "${PROJECT}" -r "${RID}"

echo "[2/5] Publicando ${PROJECT} para ${RID}"
dotnet publish "${PROJECT}" --no-restore -c "${CONFIGURATION}" -r "${RID}" --self-contained true /p:PublishSingleFile=true -o "${PUBLISH_DIR}"

echo "[3/5] Armando bundle .app"
rm -rf "${APP_DIR}"
mkdir -p "${APP_DIR}/Contents/MacOS" "${APP_DIR}/Contents/Resources"
cp -R "${PUBLISH_DIR}/." "${APP_DIR}/Contents/MacOS/"

if [[ -f "${ICON_SOURCE}" ]]; then
  rm -rf "${ICONSET_DIR}"
  mkdir -p "${ICONSET_DIR}"

  sips -z 16 16     "${ICON_SOURCE}" --out "${ICONSET_DIR}/icon_16x16.png" >/dev/null
  sips -z 32 32     "${ICON_SOURCE}" --out "${ICONSET_DIR}/icon_16x16@2x.png" >/dev/null
  sips -z 32 32     "${ICON_SOURCE}" --out "${ICONSET_DIR}/icon_32x32.png" >/dev/null
  sips -z 64 64     "${ICON_SOURCE}" --out "${ICONSET_DIR}/icon_32x32@2x.png" >/dev/null
  sips -z 128 128   "${ICON_SOURCE}" --out "${ICONSET_DIR}/icon_128x128.png" >/dev/null
  sips -z 256 256   "${ICON_SOURCE}" --out "${ICONSET_DIR}/icon_128x128@2x.png" >/dev/null
  sips -z 256 256   "${ICON_SOURCE}" --out "${ICONSET_DIR}/icon_256x256.png" >/dev/null
  sips -z 512 512   "${ICON_SOURCE}" --out "${ICONSET_DIR}/icon_256x256@2x.png" >/dev/null
  sips -z 512 512   "${ICON_SOURCE}" --out "${ICONSET_DIR}/icon_512x512.png" >/dev/null
  cp "${ICON_SOURCE}" "${ICONSET_DIR}/icon_512x512@2x.png"

  iconutil -c icns "${ICONSET_DIR}" -o "${ICON_PATH}"
fi

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
  <key>CFBundleIconFile</key>
  <string>${ICON_NAME}</string>
  <key>LSMinimumSystemVersion</key>
  <string>12.0</string>
</dict>
</plist>
PLIST

chmod +x "${APP_DIR}/Contents/MacOS/${EXECUTABLE_NAME}" || true

if [[ -n "${MACOS_CODESIGN_IDENTITY}" ]]; then
  echo "[4/7] Firmando app (Developer ID): ${MACOS_CODESIGN_IDENTITY}"
  codesign --force --deep --options runtime --timestamp --sign "${MACOS_CODESIGN_IDENTITY}" "${APP_DIR}"
else
  echo "[4/7] Firmando app (ad-hoc, no apto Gatekeeper)"
  codesign --force --deep --sign - "${APP_DIR}"
fi

echo "[5/7] Creando ZIP"
rm -f "${ZIP_PATH}"
ditto -c -k --sequesterRsrc --keepParent "${APP_DIR}" "${ZIP_PATH}"

if [[ -n "${MACOS_CODESIGN_IDENTITY}" && -n "${MACOS_NOTARY_APPLE_ID}" && -n "${MACOS_NOTARY_PASSWORD}" && -n "${MACOS_NOTARY_TEAM_ID}" ]]; then
  echo "[6/8] Notarizando ZIP"
  xcrun notarytool submit "${ZIP_PATH}" \
    --apple-id "${MACOS_NOTARY_APPLE_ID}" \
    --password "${MACOS_NOTARY_PASSWORD}" \
    --team-id "${MACOS_NOTARY_TEAM_ID}" \
    --wait

  echo "[7/8] Staple ticket sobre app"
  xcrun stapler staple "${APP_DIR}"

  echo "[8/8] Reempacando ZIP con ticket"
  rm -f "${ZIP_PATH}"
  ditto -c -k --sequesterRsrc --keepParent "${APP_DIR}" "${ZIP_PATH}"
elif [[ -n "${MACOS_CODESIGN_IDENTITY}" ]]; then
  echo "[6/7] Firma activa, notarizacion omitida (faltan variables MACOS_NOTARY_*)"
else
  echo "[6/7] Sin firma Developer ID ni notarizacion: Gatekeeper mostrara advertencia"
fi

if [[ "${CREATE_DMG}" == "true" ]]; then
  echo "[7/8] Creando DMG"
  rm -f "${DMG_PATH}"
  hdiutil create -volname "${APP_NAME}" -srcfolder "${APP_DIR}" -ov -format UDZO "${DMG_PATH}"

  if [[ -n "${MACOS_CODESIGN_IDENTITY}" ]]; then
    echo "[8/8] Firmando DMG"
    codesign --force --timestamp --sign "${MACOS_CODESIGN_IDENTITY}" "${DMG_PATH}"
  fi

  echo "[final] Resultado"
else
  echo "[7/7] Resultado"
fi

echo "APP: ${APP_DIR}"
echo "ZIP: ${ZIP_PATH}"
if [[ "${CREATE_DMG}" == "true" ]]; then
  echo "DMG: ${DMG_PATH}"
fi
