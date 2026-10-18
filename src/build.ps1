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
$cscPrefix = @()
if (Test-Path -LiteralPath $vswhere) {
    $csc = @(& $vswhere -latest -products '*' -find 'MSBuild\**\Bin\Roslyn\csc.exe' |
        Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1)[0]
}
if ([string]::IsNullOrWhiteSpace($csc)) {
    # Machines without Visual Studio still ship the same Roslyn compiler inside the
    # .NET SDK. It targets the reference assemblies passed below, so the produced
    # .NET Framework executable is identical in kind; only the compiler host differs.
    $dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    $dotnetSdkRoot = Join-Path $env:ProgramFiles 'dotnet\sdk'
    if ($null -ne $dotnetCommand -and (Test-Path -LiteralPath $dotnetSdkRoot)) {
        $sdkCsc = @(Get-ChildItem -LiteralPath $dotnetSdkRoot -Directory | Sort-Object Name -Descending |
            ForEach-Object { Join-Path $_.FullName 'Roslyn\bincore\csc.dll' } |
            Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1)[0]
        if (-not [string]::IsNullOrWhiteSpace($sdkCsc)) {
            $csc = $dotnetCommand.Source
            $cscPrefix = @($sdkCsc)
        }
    }
}
if ([string]::IsNullOrWhiteSpace($csc)) {
    throw '需要 Visual Studio 2022 / Build Tools 或 .NET SDK 的 Roslyn C# 编译器（.NET 桌面生成工具）'
}
# Pin the same references as the validated cross build. Runtime WinMetadata on
# Server editions can expose unprojected event accessors, so it is not a build SDK.
. (Join-Path $repoRoot 'tools/Restore-WindowsBuildReferences.ps1')
$referenceRoot = Restore-WindowsBuildReferences (Join-Path $repoRoot '.build-references')
$framework = Join-Path $referenceRoot 'microsoft.netframework.referenceassemblies.net48/build/.NETFramework/v4.8'
$refs = @('mscorlib.dll','System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll',
    'System.Web.Extensions.dll','System.Security.dll','System.IO.Compression.dll','System.IO.Compression.FileSystem.dll') |
    ForEach-Object { Join-Path $framework $_ }
$refs += @(Get-ChildItem (Join-Path $framework 'Facades') -Filter '*.dll' | Select-Object -ExpandProperty FullName)
$refs += Join-Path $referenceRoot 'system.runtime.windowsruntime/ref/netstandard2.0/System.Runtime.WindowsRuntime.dll'
$refs += @(Get-ChildItem (Join-Path $referenceRoot 'microsoft.windows.sdk.contracts/ref/netstandard2.0') -Filter '*.winmd' | Select-Object -ExpandProperty FullName)
$responseFile = Join-Path $referenceRoot 'references.rsp'
[IO.File]::WriteAllLines($responseFile, [string[]]@($refs | ForEach-Object { '/reference:"' + $_ + '"' }), [Text.Encoding]::UTF8)

& $csc @cscPrefix /nologo /noconfig /nostdlib+ /langversion:5 /target:winexe /optimize+ /platform:anycpu /win32manifest:"$scriptDir\app.manifest" `
  /win32icon:"$scriptDir\FengYuMu.ico" /out:"$OutputPath" "@$responseFile" `
  "$scriptDir\MapleOverlay.cs" "$scriptDir\SceneRecognition.cs" "$scriptDir\SupplementalOcrReview.cs" "$scriptDir\OcrFrame.cs" "$scriptDir\OcrWindowsAdapter.cs" "$scriptDir\PrimaryOcrClient.cs" "$scriptDir\ImageTestForm.cs" "$scriptDir\PanelRecognitionPolicy.cs" "$scriptDir\QuestDetailGeometry.cs" "$scriptDir\QuestTextScope.cs" "$scriptDir\QuestJournalCatalog.cs" "$scriptDir\NumericTextTemplates.cs" "$scriptDir\SkillEffectVocabulary.cs" "$scriptDir\CharacterFieldOverlay.cs" "$scriptDir\ChatColorEvidence.cs" "$scriptDir\SkillSectionOverlay.cs" "$scriptDir\EquipmentFieldGrammar.cs" "$scriptDir\EquipmentPanelLayout.cs" "$scriptDir\StructuredPanelOverlay.cs" "$scriptDir\QuestObjectiveSurface.cs" "$scriptDir\QuestJournalGeometry.cs" "$scriptDir\QuestJournalOverlay.cs" "$scriptDir\QuestJournalComposition.cs" "$scriptDir\ObservedProseMatch.cs" "$scriptDir\SceneTranslationPolicy.cs" "$scriptDir\ChatFrameContinuity.cs" "$scriptDir\SkillDictionaryMatching.cs" "$scriptDir\ManualTranslationMemory.cs" "$scriptDir\ManualMemoryOverlay.cs" "$scriptDir\TextSurfaceReconstruction.cs" "$scriptDir\GlyphBackgroundReconstruction.cs" "$scriptDir\GlyphMaskInpainting.cs" "$scriptDir\PanelFocusPriority.cs" "$scriptDir\FusionPatchMask.cs" "$scriptDir\FusionRowEvidence.cs" "$scriptDir\SimpleBackgroundFill.cs" "$scriptDir\SimpleFusionComposition.cs" "$scriptDir\SimpleTranslationFlow.cs" "$scriptDir\FusionBitmapRenderer.cs" "$scriptDir\FusionTextSpace.cs" "$scriptDir\FusionOverlay.cs" `
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
$compilerDescription = if ($cscPrefix.Count -gt 0) { "$csc $($cscPrefix[0])" } else { $csc }
$evidence = Write-BuildEvidence $OutputPath $repoRoot $compilerDescription
Write-Host "编译完成: $OutputPath SHA-256=$($evidence.exeSha256)"
