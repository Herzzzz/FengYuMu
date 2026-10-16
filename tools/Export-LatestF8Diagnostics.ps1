param([Parameter(Mandatory=$true)][string]$ExePath)
$ErrorActionPreference='Stop'
$exe=(Resolve-Path -LiteralPath $ExePath).Path
$log=Join-Path (Split-Path -Parent $exe) '运行诊断.log'
if(-not (Test-Path -LiteralPath $log)){throw '实际运行 EXE 旁边没有运行诊断.log；请核对进程路径。'}
$lines=@(Get-Content -LiteralPath $log -Encoding UTF8 -Tail 6000)
$start=-1
for($i=0;$i -lt $lines.Count;$i++){if($lines[$i] -match '^\d{4}-\d{2}-\d{2}T\S+ PID=\d+ (?:ocr-start|picture-test-start) '){$start=$i}}
if($start -lt 0){throw '未找到 OCR 启动记录。先在游戏或本软件图片测试页开始一次识别。'}
Write-Output ('EXE_SHA256='+(Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash)
for($i=$start;$i -lt $lines.Count;$i++){
 # Only numeric/category diagnostics. Do not export exception dumps, settings,
 # provider responses, credentials, OCR text or the full log.
 if($lines[$i] -match '^\d{4}-\d{2}-\d{2}T\S+ PID=\d+ (?:character-field-coverage|chat-capture|chat-channel|skill-overview-coverage|field-coverage|skill-effect-coverage|objective-coverage|quest-journal|quest-journal-page|quest-journal-shortcuts|quest-context|quest-body|ocr-pass|dictionary-pass|composition-pass|replacement-ready|replacement-preserved|overlay-first-paint|primary-ocr|picture-test|picture-test-start|cursor-panel|ocr-engine|ocr-review|overlay-layout-failed|fusion-inpaint-attempt|ocr-start|ocr-stage|ocr-complete|fusion-prepared|fusion-repair-attempt|fusion-fallback|fusion-label-preserved|fusion-label-prepared|fusion-space-attempt|skill-parts|overlay-published|overlay-scene-retired|capture-worker-geometry) '){Write-Output $lines[$i]}
}
