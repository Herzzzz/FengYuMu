using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MapleOverlay
{
    internal sealed partial class OverlayForm
    {
        // One request owns one source frame and one final plan. No early publication,
        // OCR review, AI enrichment or retention of labels from another pass.
        private async Task ShowSimpleTranslationAsync(bool automatic, int requestId)
        {
            if (processing) return;
            int generation = recognitionGeneration;
            processing = true;
            CancelPanelAiRequest();
            CancellationTokenSource cancellation = new CancellationTokenSource();
            panelAiCancellation = cancellation;
            IntPtr sourceWindow = automatic ? GetForegroundWindow() : IntPtr.Zero;
            Stopwatch clock = Stopwatch.StartNew();
            if (automatic || Program.Benchmark || visibleRequestWatch == null) visibleRequestWatch = Stopwatch.StartNew();
            firstPaintRecorded = false;
            lastPaintedTranslationCount = 0;
            visibleTranslation = false; labels.Clear(); ClearFusionAssets();
            displayedScene.Clear(); displayedSceneRevision++;
            captureTextRows.Clear(); captureTextRowsOverflow = false; requestQuestMemoryIds.Clear();
            requestFocusBounds = RectangleF.Empty; requestFocusIsTooltip = false;
            Invalidate(); Update();
            Program.LogRuntimeEvent("ocr-start", "generation=" + generation + " automatic=" + automatic + " pipeline=simple");
            Program.LogRuntimeEvent("ocr-engine", "generation=" + generation + " primary=" + primaryOcr.EngineLabel);
            SetInteractionStatus("正在识别游戏画面…");
            try
            {
                Rectangle screen;
                if (Program.Benchmark && !String.IsNullOrEmpty(Program.BenchmarkImagePath))
                { using (Image image = Image.FromFile(Program.BenchmarkImagePath)) screen = new Rectangle(Point.Empty, image.Size); }
                else if (!ResolveCaptureBounds(out screen)) return;
                captureBounds = gameBounds = screen;
                long captureMs;
                string recognized = "";
                OcrResult benchmarkResult = null;
                using (Bitmap bitmap = new Bitmap(screen.Width, screen.Height, PixelFormat.Format32bppArgb))
                {
                    using (Graphics graphics = Graphics.FromImage(bitmap))
                    {
                        if (Program.Benchmark)
                        {
                            if (!String.IsNullOrEmpty(Program.BenchmarkImagePath))
                            { using (Image image = Image.FromFile(Program.BenchmarkImagePath)) graphics.DrawImageUnscaled(image, 0, 0); }
                            else
                            {
                                graphics.Clear(Color.FromArgb(28, 28, 32));
                                using (Font font = new Font("Arial", 14))
                                    graphics.DrawString((Program.BenchmarkText ?? "Quest Complete").Replace("|", Environment.NewLine), font, Brushes.White, 40, 78);
                            }
                        }
                        else using (WindowCaptureFrame frame = await CaptureSceneFrameWithoutOverlayAsync(cancellation.Token))
                        {
                            if (frame.Bounds != screen) throw new OperationCanceledException();
                            graphics.DrawImageUnscaled(frame.Bitmap, 0, 0);
                        }
                    }
                    if (RecognitionWasCancelled(automatic, requestId, generation)) return;
                    captureMs = clock.ElapsedMilliseconds;
                    if (!automatic && !Program.Benchmark) ShowWindowScanIndicator();
                    float scale; List<OverlayLabel> next;
                    using (Bitmap prepared = PrepareForOcr(bitmap, out scale, false, screen.Width >= 1900 ? 2400.0f : 1700.0f))
                    {
                        OcrResult result = await RecognizeAsync(prepared, cancellation.Token);
                        if (RecognitionWasCancelled(automatic, requestId, generation)) return;
                        // Players run very different UI sizes: a 1280x720 window with the
                        // large UI setting prints the same rows that a 1920 window prints
                        // at twice the height. Judging by the frame width alone therefore
                        // mis-scales one of them. When the first pass reports unusually
                        // small glyphs, read the frame once more at 1.5x and keep the
                        // richer result; the coordinate scale travels with it.
                        float medianHeight = MedianWordHeight(result);
                        if (medianHeight > 0 && medianHeight < 9.0f &&
                            prepared.Width * 1.5f <= 4200.0f)
                        {
                            float secondScale;
                            using (Bitmap enlarged = PrepareForOcr(bitmap, out secondScale, false,
                                prepared.Width * 1.5f))
                            {
                                OcrResult second = await RecognizeAsync(enlarged, cancellation.Token);
                                if (RecognitionWasCancelled(automatic, requestId, generation)) return;
                                if (CharacterCount(second) > CharacterCount(result))
                                {
                                    result = second;
                                    scale = secondScale;
                                    Program.LogRuntimeEvent("ocr-adaptive-scale",
                                        "medianHeight=" + medianHeight.ToString("0.0") +
                                        " scale=" + scale.ToString("0.00"));
                                }
                            }
                        }
                        result = await EnrichTooltipOcrAsync(prepared, result, automatic, cancellation.Token);
                        if (RecognitionWasCancelled(automatic, requestId, generation)) return;
                        recognized = result.Text;
                        benchmarkResult = result;
                        Program.LogRuntimeEvent("ocr-stage", "generation=" + generation + " stage=whole-client lines=" + result.Lines.Count + " chars=" + (result.Text ?? "").Length);
                        next = BuildLabels(result, scale, prepared, LabelBuildScope.Discovery);
                        // Dictionary misses (NPC dialogue, story panels) fall back to the
                        // panel AI translator. Manual request only: continuous mode would
                        // call the provider every few hundred milliseconds.
                        if (!Program.Benchmark && !automatic)
                        {
                            OverlayLabel dialogueAi = await BuildDialogueAiLabelAsync(
                                result, prepared, scale, cancellation.Token);
                            if (dialogueAi != null) next.Add(dialogueAi);
                        }
                    }
                    // The matcher resolves semantic identity; this is the only merge stage.
                    MergeAdjacentLongLabels(next); MergeOverlappingSameTextLabels(next);
                    MergeSameTextLabels(next); RemoveContainedLabels(next);
                    Program.LogRuntimeEvent("ocr-stage", "generation=" + generation + " stage=dictionary-match labels=" + next.Count);
                    if (RecognitionWasCancelled(automatic, requestId, generation)) return;
                    if (!await RememberDisplayedSceneAsync(bitmap, screen, next)) return;
                    if (RecognitionWasCancelled(automatic, requestId, generation)) return;
                    labels.Clear(); labels.AddRange(next);
                }
                if (automatic && (!continuousTranslationEnabled || GetForegroundWindow() != sourceWindow))
                { labels.Clear(); ClearFusionAssets(); return; }
                HideWindowScanIndicator();
                visibleTranslation = labels.Count > 0;
                // Continuous mode rebuilds this frame every cycle, so retiring the overlay
                // here only produced a blank flash between two passes that show the same
                // panel. The periodic retirement guard still covers real panel changes.
                if (!Program.Benchmark && visibleTranslation && !continuousTranslationEnabled)
                {
                    await RetireChangedSceneAsync(true);
                    if (RecognitionWasCancelled(automatic, requestId, generation)) return;
                }
                ShowCurrentTranslations(); if (!Program.Benchmark) Update();
                Program.LogRuntimeEvent("overlay-published", "generation=" + generation + " phase=single elapsedMs=" + visibleRequestWatch.ElapsedMilliseconds);
                Program.LogRuntimeEvent("ocr-complete", "generation=" + generation + " labels=" + labels.Count + " prepared=" + simpleReady.Count + " painted=" + lastPaintedTranslationCount + " elapsedMs=" + clock.ElapsedMilliseconds);
                SetInteractionStatus("词库候选 " + labels.Count + " 条；确认替换 " + simpleReady.Count + " 条；实际显示 " + lastPaintedTranslationCount + " 条。");
                tray.Text = "枫语幕 v4.4.5（显示 " + lastPaintedTranslationCount + " 条，" + clock.ElapsedMilliseconds + "ms）";
                if (automatic)
                {
                    continuousTranslationFailures = 0;
                    continuousTranslationMisses = simpleReady.Count == 0 ? continuousTranslationMisses + 1 : 0;
                    continuousTranslationTimer.Interval = ContinuousTranslationPolicy.NextInterval(simpleReady.Count > 0, continuousTranslationMisses, 0);
                }
                if (Program.Benchmark)
                {
                    benchmarkConvergedMs = clock.ElapsedMilliseconds;
                    benchmarkFirstVisibleMs = 0;
                    // The benchmark must name the surface that produced these labels.
                    // Reporting an empty scene made every scene assertion unprovable and
                    // let a quest-pipeline regression hide behind a blank field.
                    SceneSnapshot benchmarkScene = benchmarkResult == null ? null :
                        SceneClassifier.Analyze(benchmarkResult, translations, false);
                    WriteBenchmarkResult(clock, captureMs, 0, 0, 0, 0, 0, 0, "simple-single-pass",
                        DescribeBenchmarkScene(benchmarkScene), recognized);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                if (shuttingDown || IsDisposed || Disposing) return;
                visibleTranslation = false; labels.Clear(); ClearFusionAssets();
                string trace = (error.StackTrace ?? "").Replace('\n', ' ').Replace('\r', ' ');
                if (trace.Length > 420) trace = trace.Substring(0, 420);
                Program.LogRuntimeEvent("ocr-failed", "generation=" + generation +
                    " type=" + error.GetType().Name + " message=" + error.Message + " stack=" + trace);
                SetInteractionStatus("识别失败：" + error.Message);
                if (automatic)
                {
                    continuousTranslationFailures++;
                    continuousTranslationTimer.Interval = ContinuousTranslationPolicy.NextInterval(false, continuousTranslationMisses, continuousTranslationFailures);
                }
            }
            finally
            {
                if (Object.ReferenceEquals(panelAiCancellation, cancellation)) panelAiCancellation = null;
                cancellation.Dispose(); HideWindowScanIndicator(); processing = false;
            }
        }

        // Scene wording for benchmark reports. The order follows specificity: a quest
        // window also shows an interface frame, so the stronger surface must win.
        private static string DescribeBenchmarkScene(SceneSnapshot scene)
        {
            if (scene == null) return "未识别";
            if (scene.IsStrong(SceneKind.Quest)) return "任务";
            if (scene.IsStrong(SceneKind.Skill)) return "技能";
            if (scene.IsStrong(SceneKind.Item)) return "装备/道具";
            if (scene.IsStrong(SceneKind.Character)) return "人物属性";
            if (scene.IsStrong(SceneKind.Shop)) return "商店";
            if (scene.IsStrong(SceneKind.Inventory)) return "背包";
            if (scene.IsStrong(SceneKind.Interface)) return "普通界面";
            return "普通界面";
        }
    }
}
