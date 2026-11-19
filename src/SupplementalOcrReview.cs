using System;
using System.Threading.Tasks;

namespace MapleOverlay
{
    // A manual screenshot gets a bounded opportunity to recover missing text after
    // the initial result. These are start gates, not a promise to interrupt WinRT
    // OCR mid-call or to finish the entire request within this duration.
    internal static class SupplementalOcrReview
    {
        // Start gates on the original request clock. This does not interrupt an
        // OCR call already in flight, extend its timeout, or add a fresh window.
        internal const int ManualMinimumStartMilliseconds = 1500;
        internal static bool CanStart(bool automatic, long requestElapsedMilliseconds,
            int automaticDeadlineMilliseconds)
        {
            if (requestElapsedMilliseconds < 0 || automaticDeadlineMilliseconds <= 0) return false;
            int deadline = automatic ? automaticDeadlineMilliseconds :
                Math.Max(ManualMinimumStartMilliseconds, automaticDeadlineMilliseconds);
            return requestElapsedMilliseconds < deadline;
        }

        internal static bool NeedsWholeFrameReview(bool automatic, bool useHoverPass,
            int recognizedLines, int labelCount)
        {
            if (recognizedLines < 0 || labelCount < 0) return false;
            // Enabling a cursor inspection does not prove the rest of the screen
            // was recognized. Keep the existing automatic-mode eligibility.
            return (!automatic || !useHoverPass) &&
                (labelCount < 3 || (long)recognizedLines > (long)labelCount + 1);
        }

        internal static async Task<bool> RunWholeFrameAsync(bool automatic,
            Func<long> requestElapsed, Func<bool> needsReview,
            Func<bool> cancelled, Func<Task> contrastPass, Func<Task> enlargedPass,
            Action<string, string> decision)
        {
            if (requestElapsed == null || needsReview == null ||
                cancelled == null || contrastPass == null || enlargedPass == null || decision == null)
                throw new ArgumentNullException("review callback");
            Func<Task>[] passes = { contrastPass, enlargedPass };
            string[] stages = { "whole-contrast", "whole-enlarged" };
            int[] automaticDeadlines = { 450, 650 };
            for (int index = 0; index < passes.Length; index++)
            {
                if (cancelled()) return false;
                if (!needsReview()) { decision(stages[index], "not-needed"); continue; }
                if (!CanStart(automatic, requestElapsed(), automaticDeadlines[index]))
                { decision(stages[index], "budget"); continue; }
                decision(stages[index], "started");
                await passes[index]();
                if (cancelled()) return false;
                decision(stages[index], "completed");
            }
            return !cancelled();
        }
    }
}
