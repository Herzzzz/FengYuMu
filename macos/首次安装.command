#!/bin/zsh
set -euo pipefail

SCRIPT_DIR="${0:A:h}"
SOURCE_APP="${SCRIPT_DIR}/枫语幕.app"
TARGET_ROOT="${HOME}/Applications"
TARGET_APP="${TARGET_ROOT}/枫语幕.app"
STAGE_ROOT=""
BACKUP_ROOT=""
STAGED_APP=""
BACKUP_APP=""
HAD_OLD=0
COMMITTED=0

osascript -e 'display dialog "枫语幕将安全复制到当前用户的“应用程序”目录。安装器不会关闭 macOS 安全检查，也不会修改应用签名。" buttons {"取消", "继续安装"} default button "继续安装" with title "枫语幕首次安装"'

if [[ ! -d "${SOURCE_APP}" ]]; then
  osascript -e 'display alert "安装包不完整" message "没有找到枫语幕.app，请重新下载完整安装包。" as critical'
  exit 1
fi

mkdir -p "${TARGET_ROOT}"
if [[ "${TARGET_APP}" != "${HOME}/Applications/枫语幕.app" ]]; then
  osascript -e 'display alert "安装位置校验失败" message "为保护文件，安装已经停止。" as critical'
  exit 1
fi

cleanup() {
  if [[ "${COMMITTED}" != "1" && "${HAD_OLD}" == "1" &&
        ! -e "${TARGET_APP}" && -n "${BACKUP_APP}" && -e "${BACKUP_APP}" ]]; then
    /bin/mv "${BACKUP_APP}" "${TARGET_APP}" || true
  fi
  if [[ -n "${STAGE_ROOT}" && "${STAGE_ROOT}" == "${TARGET_ROOT}"/.fengyumu-stage.* ]]; then
    /bin/rm -R "${STAGE_ROOT}" 2>/dev/null || true
  fi
  if [[ -n "${BACKUP_ROOT}" && "${BACKUP_ROOT}" == "${TARGET_ROOT}"/.fengyumu-rollback.* ]]; then
    /bin/rm -R "${BACKUP_ROOT}" 2>/dev/null || true
  fi
}
trap cleanup EXIT

STAGE_ROOT="$(mktemp -d "${TARGET_ROOT}/.fengyumu-stage.XXXXXX")"
BACKUP_ROOT="$(mktemp -d "${TARGET_ROOT}/.fengyumu-rollback.XXXXXX")"
STAGED_APP="${STAGE_ROOT}/枫语幕.app"
BACKUP_APP="${BACKUP_ROOT}/枫语幕.app"

ditto "${SOURCE_APP}" "${STAGED_APP}"
BUNDLE_ID="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleIdentifier' "${STAGED_APP}/Contents/Info.plist" 2>/dev/null || true)"
if [[ "${BUNDLE_ID}" != "cn.fengyumu.macos" || ! -x "${STAGED_APP}/Contents/MacOS/FengYuMuMac" ]] ||
   ! codesign --verify --deep --strict --verbose=2 "${STAGED_APP}"; then
  osascript -e 'display alert "安装包校验失败" message "应用签名结构不完整，安装已经停止。请从官方发布页重新下载，并核对 SHA-256。" as critical'
  exit 1
fi

if [[ -e "${TARGET_APP}" || -L "${TARGET_APP}" ]]; then
  /bin/mv "${TARGET_APP}" "${BACKUP_APP}"
  HAD_OLD=1
fi
if ! /bin/mv "${STAGED_APP}" "${TARGET_APP}"; then
  osascript -e 'display alert "安装失败" message "旧版本已经保留，请关闭占用枫语幕的程序后再试。" as critical'
  exit 1
fi
COMMITTED=1
cleanup
trap - EXIT

open -R "${TARGET_APP}"
osascript -e 'display dialog "安装完成，Finder 已经选中枫语幕。首次运行请对“枫语幕”点右键，选择“打开”，再确认一次“打开”。这是 macOS 对未公证内测软件的安全确认；安装器不会绕过它。随后请允许屏幕录制权限，并重新打开一次枫语幕。" buttons {"知道了"} default button "知道了" with title "枫语幕"'
