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
                    using (Bitmap prepared = PrepareForOcr(bitmap, out scale, false, screen.Width >= 1900 ? 1800.0f : 1700.0f))
                    {
                        OcrResult result = await RecognizeAsync(prepared, cancellation.Token);
                        if (RecognitionWasCancelled(automatic, requestId, generation)) return;
                        recognized = result.Text;
                        Program.LogRuntimeEvent("ocr-stage", "generation=" + generation + " stage=whole-client lines=" + result.Lines.Count + " chars=" + (result.Text ?? "").Length);
                        next = BuildLabels(result, scale, prepared, LabelBuildScope.Discovery);
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
                if (!Program.Benchmark && visibleTranslation)
                {
                    await RetireChangedSceneAsync(true);
                    if (RecognitionWasCancelled(automatic, requestId, generation)) return;
                }
                ShowCurrentTranslations(); if (!Program.Benchmark) Update();
                Program.LogRuntimeEvent("overlay-published", "generation=" + generation + " phase=single elapsedMs=" + visibleRequestWatch.ElapsedMilliseconds);
                Program.LogRuntimeEvent("ocr-complete", "generation=" + generation + " labels=" + labels.Count + " prepared=" + simpleReady.Count + " painted=" + lastPaintedTranslationCount + " elapsedMs=" + clock.ElapsedMilliseconds);
                SetInteractionStatus("词库候选 " + labels.Count + " 条；确认替换 " + simpleReady.Count + " 条；实际显示 " + lastPaintedTranslationCount + " 条。");
                tray.Text = "枫语幕 v4.0（显示 " + lastPaintedTranslationCount + " 条，" + clock.ElapsedMilliseconds + "ms）";
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
                    WriteBenchmarkResult(clock, captureMs, 0, 0, 0, 0, 0, 0, "simple-single-pass", "", recognized);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                if (shuttingDown || IsDisposed || Disposing) return;
                visibleTranslation = false; labels.Clear(); ClearFusionAssets();
                Program.LogRuntimeEvent("ocr-failed", "generation=" + generation + " type=" + error.GetType().Name);
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
    }
}
