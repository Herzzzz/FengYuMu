#!/bin/zsh
set -euo pipefail

SCRIPT_DIR="${0:A:h}"
MAC_DIR="${SCRIPT_DIR:h}"
REPO_DIR="${MAC_DIR:h}"
BUILD_DIR="${MAC_DIR}/.build"
PRODUCT_DIR="${BUILD_DIR}/product"
APP_DIR="${PRODUCT_DIR}/枫语幕.app"
CONTENTS_DIR="${APP_DIR}/Contents"
MACOS_DIR="${CONTENTS_DIR}/MacOS"
RESOURCES_DIR="${CONTENTS_DIR}/Resources"
VERSION="3.2.2-mac.1"

if [[ "$(uname -s)" != "Darwin" ]]; then
  print -u2 "只能在 Mac 上构建枫语幕 macOS 版。"
  exit 1
fi
if [[ "$(uname -m)" != "arm64" ]]; then
  print -u2 "此版本只支持 Apple 芯片 Mac。"
  exit 1
fi

MAJOR_VERSION="$(sw_vers -productVersion | cut -d. -f1)"
MINIMUM_VERSION="26.6"
CURRENT_VERSION="$(sw_vers -productVersion)"
if [[ "$(printf '%s\n%s\n' "${MINIMUM_VERSION}" "${CURRENT_VERSION}" | sort -V | head -n1)" != "${MINIMUM_VERSION}" ]]; then
  print -u2 "需要 macOS 26.6 或以上版本。"
  exit 1
fi
command -v swift >/dev/null || { print -u2 "找不到 Xcode/Swift，请先安装 Xcode 26。"; exit 1; }

cd "${MAC_DIR}"
swift test
swift build -c release --arch arm64

"${BUILD_DIR}/arm64-apple-macosx/release/FengYuMuMac" --fixture-self-test \
  "${REPO_DIR}/枫语幕词库.tsv" "${REPO_DIR}/tests/fixtures/public-v3/quest-dr-kim.png"

rm -rf "${PRODUCT_DIR}"
mkdir -p "${MACOS_DIR}" "${RESOURCES_DIR}"
cp "${BUILD_DIR}/arm64-apple-macosx/release/FengYuMuMac" "${MACOS_DIR}/FengYuMuMac"
cp "${MAC_DIR}/Info.plist" "${CONTENTS_DIR}/Info.plist"
cp "${REPO_DIR}/枫语幕词库.tsv" "${RESOURCES_DIR}/枫语幕词库.tsv"
cp "${MAC_DIR}/首次安装.command" "${PRODUCT_DIR}/首次安装.command"
cp "${MAC_DIR}/安装说明.txt" "${PRODUCT_DIR}/安装说明.txt"
chmod +x "${MACOS_DIR}/FengYuMuMac" "${PRODUCT_DIR}/首次安装.command"

if [[ -n "${DEVELOPER_ID_APPLICATION:-}" ]]; then
  codesign --force --options runtime --timestamp --deep \
    --sign "${DEVELOPER_ID_APPLICATION}" "${APP_DIR}"
  print "已使用 Developer ID 签名。"
else
  codesign --force --deep --sign - "${APP_DIR}"
  print "未设置 Developer ID，已进行本机临时签名。"
fi
codesign --verify --deep --strict --verbose=2 "${APP_DIR}"

DMG_PATH="${MAC_DIR}/枫语幕_${VERSION}_macOS26_arm64.dmg"
rm -f "${DMG_PATH}"
hdiutil create -volname "枫语幕 ${VERSION}" -srcfolder "${PRODUCT_DIR}" \
  -ov -format UDZO "${DMG_PATH}"
shasum -a 256 "${DMG_PATH}" > "${DMG_PATH}.sha256"
print "完成：${DMG_PATH}"
