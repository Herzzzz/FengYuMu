param([string]$OutputPath)
$ErrorActionPreference = 'Stop'
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptDir
if ([string]::IsNullOrWhiteSpace($OutputPath)) { $OutputPath = Join-Path $repoRoot '枫语幕.exe' }
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $OutputPath) | Out-Null
# The Framework compiler predates WinRT event projections and cannot compile
# Graphics Capture's FrameArrived event. Use the installed Visual Studio Roslyn
# compiler while retaining the .NET Framework runtime/reference contract.
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$csc = $null
if (Test-Path -LiteralPath $vswhere) {
    $csc = @(& $vswhere -latest -products '*' -find 'MSBuild\**\Bin\Roslyn\csc.exe' |
        Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1)[0]
}
if ([string]::IsNullOrWhiteSpace($csc)) {
    throw '需要 Visual Studio 2022 / Build Tools 的 Roslyn C# 编译器（.NET 桌面生成工具）'
}
$framework = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$winmd = 'C:\Windows\System32\WinMetadata'

& $csc /nologo /langversion:5 /target:winexe /optimize+ /platform:anycpu /win32manifest:"$scriptDir\app.manifest" `
  /win32icon:"$scriptDir\FengYuMu.ico" /out:"$OutputPath" `
  /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll `
  /reference:System.Windows.Forms.dll `
  /reference:System.Web.Extensions.dll `
  /reference:System.Security.dll `
  /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll `
  /reference:"$framework\System.Runtime.dll" `
  /reference:"$framework\System.Runtime.WindowsRuntime.dll" `
  /reference:"$winmd\Windows.Foundation.winmd" `
  /reference:"$winmd\Windows.Globalization.winmd" `
  /reference:"$winmd\Windows.Graphics.winmd" `
  /reference:"$winmd\Windows.Media.winmd" `
  /reference:"$winmd\Windows.Storage.winmd" `
  "$scriptDir\MapleOverlay.cs" "$scriptDir\SceneRecognition.cs" "$scriptDir\SupplementalOcrReview.cs" "$scriptDir\OcrFrame.cs" "$scriptDir\OcrWindowsAdapter.cs" "$scriptDir\PrimaryOcrClient.cs" "$scriptDir\ImageTestForm.cs" "$scriptDir\PanelRecognitionPolicy.cs" "$scriptDir\QuestDetailGeometry.cs" "$scriptDir\QuestTextScope.cs" "$scriptDir\QuestJournalCatalog.cs" "$scriptDir\NumericTextTemplates.cs" "$scriptDir\SkillEffectVocabulary.cs" "$scriptDir\CharacterFieldOverlay.cs" "$scriptDir\ChatColorEvidence.cs" "$scriptDir\SkillSectionOverlay.cs" "$scriptDir\EquipmentFieldGrammar.cs" "$scriptDir\StructuredPanelOverlay.cs" "$scriptDir\QuestObjectiveSurface.cs" "$scriptDir\QuestJournalGeometry.cs" "$scriptDir\QuestJournalOverlay.cs" "$scriptDir\QuestJournalComposition.cs" "$scriptDir\ObservedProseMatch.cs" "$scriptDir\SceneTranslationPolicy.cs" "$scriptDir\ChatFrameContinuity.cs" "$scriptDir\SkillDictionaryMatching.cs" "$scriptDir\ManualTranslationMemory.cs" "$scriptDir\ManualMemoryOverlay.cs" "$scriptDir\TextSurfaceReconstruction.cs" "$scriptDir\GlyphBackgroundReconstruction.cs" "$scriptDir\GlyphMaskInpainting.cs" "$scriptDir\PanelFocusPriority.cs" "$scriptDir\FusionPatchMask.cs" "$scriptDir\FusionRowEvidence.cs" "$scriptDir\SimpleBackgroundFill.cs" "$scriptDir\SimpleFusionComposition.cs" "$scriptDir\SimpleTranslationFlow.cs" "$scriptDir\FusionBitmapRenderer.cs" "$scriptDir\FusionTextSpace.cs" "$scriptDir\FusionOverlay.cs" `
  "$scriptDir\MapleWindowTracker.cs" "$scriptDir\TranslationTerminology.cs" `
  "$scriptDir\EmbeddedFilePicker.cs" "$scriptDir\OnlineAiProtocol.cs" "$scriptDir\OnlineAiResponse.cs" "$scriptDir\UserSettings.cs" "$scriptDir\CaptureProcessSession.cs" "$scriptDir\WindowCaptureClient.cs" "$scriptDir\GraphicsWindowCapture.cs" "$scriptDir\CaptureFramePolicy.cs" "$scriptDir\ChatBoundaryPolicy.cs" "$scriptDir\FusionInkWeight.cs" `
  "$scriptDir\StrongOcrClient.cs" "$scriptDir\StrongOcrComponent.cs" "$scriptDir\StrongOcrValidationCache.cs" `
  "$scriptDir\SimpleForms.cs" "$scriptDir\OutboundChatIntent.cs" "$scriptDir\OfflineChat.cs" `
  "$scriptDir\ApplicationUpdater.cs" "$scriptDir\UpdateFileTransaction.cs" "$scriptDir\GamepadProbe.cs"

if ($LASTEXITCODE -ne 0) { throw "编译失败，退出码 $LASTEXITCODE" }
$outputDirectory = Split-Path -Parent $OutputPath
if ([IO.Path]::GetFullPath($outputDirectory) -ne [IO.Path]::GetFullPath($repoRoot)) {
    foreach ($asset in @('枫语幕词库.tsv','基础补充词库.tsv','任务正文.tsv','使用说明.txt')) {
        Copy-Item -LiteralPath (Join-Path $repoRoot $asset) -Destination (Join-Path $outputDirectory $asset)
    }
}
. (Join-Path $repoRoot 'tools/Build-Evidence.ps1')
$evidence = Write-BuildEvidence $OutputPath $repoRoot $csc
Write-Host "编译完成: $OutputPath SHA-256=$($evidence.exeSha256)"
