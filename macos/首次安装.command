#!/bin/zsh
set -euo pipefail

SCRIPT_DIR="${0:A:h}"
SOURCE_APP="${SCRIPT_DIR}/枫语幕.app"
TARGET_ROOT="${HOME}/Applications"
TARGET_APP="${TARGET_ROOT}/枫语幕.app"

osascript -e 'display dialog "枫语幕将安装到当前用户的“应用程序”目录。首次运行还需要允许屏幕录制权限。" buttons {"取消", "继续安装"} default button "继续安装" with title "枫语幕首次安装"'

if [[ ! -d "${SOURCE_APP}" ]]; then
  osascript -e 'display alert "安装包不完整" message "没有找到枫语幕.app，请重新下载完整安装包。" as critical'
  exit 1
fi

mkdir -p "${TARGET_ROOT}"
rm -rf "${TARGET_APP}"
ditto "${SOURCE_APP}" "${TARGET_APP}"
xattr -dr com.apple.quarantine "${TARGET_APP}" 2>/dev/null || true
codesign --force --deep --sign - "${TARGET_APP}"
codesign --verify --deep --strict "${TARGET_APP}"

open "${TARGET_APP}"
osascript -e 'display dialog "安装完成。请在弹出的系统提示中允许枫语幕录制屏幕；如果没有弹出，请点击软件里的“屏幕权限”。授权后重新打开一次枫语幕即可。" buttons {"知道了"} default button "知道了" with title "枫语幕"'
