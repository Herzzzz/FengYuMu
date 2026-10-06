import AppKit
import FengYuMuCore

struct OverlayLabel {
    let text: String
    let frame: CGRect
    let sourceHeight: CGFloat
    let prominent: Bool
}

final class OverlayController {
    private var panel: NSPanel?
    private var hideTask: Task<Void, Never>?

    var isVisible: Bool { panel?.isVisible == true }

    func show(lines: [OCRLine], store: TranslationStore,
              mode: TranslationRangeMode, over capture: CapturedGameWindow) {
        let labels = makeLabels(lines: lines, store: store, mode: mode,
                                windowSize: capture.frame.size)
        guard !labels.isEmpty else { hide(); return }
        let targetFrame = ScreenCaptureService.cocoaFrame(fromScreenCaptureFrame: capture.frame)
        let overlay = panel ?? makePanel()
        let canvas = (overlay.contentView as? OverlayCanvasView) ?? OverlayCanvasView(frame: .zero)
        overlay.contentView = canvas
        canvas.labels = labels
        overlay.setFrame(targetFrame, display: true)
        overlay.orderFrontRegardless()
        panel = overlay
    }

    func hide() {
        hideTask?.cancel()
        panel?.orderOut(nil)
    }

    private func makePanel() -> NSPanel {
        let panel = NSPanel(
            contentRect: .zero,
            styleMask: [.borderless, .nonactivatingPanel],
            backing: .buffered,
            defer: false
        )
        panel.isOpaque = false
        panel.backgroundColor = .clear
        panel.hasShadow = false
        panel.ignoresMouseEvents = true
        panel.level = .screenSaver
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .ignoresCycle]
        panel.hidesOnDeactivate = false
        panel.contentView = OverlayCanvasView(frame: .zero)
        return panel
    }

    private func makeLabels(lines: [OCRLine], store: TranslationStore,
                            mode: TranslationRangeMode, windowSize: CGSize) -> [OverlayLabel] {
        var result: [OverlayLabel] = []
        for line in lines where line.confidence >= 0.25 {
            let matches = store.matches(in: line.text, mode: mode)
            guard !matches.isEmpty else { continue }
            let normalizedCount = max(1, TranslationStore.normalize(line.text).count)
            for match in matches {
                let start = match.normalizedStart
                let length = match.normalizedLength
                let xRatio = CGFloat(start) / CGFloat(normalizedCount)
                let widthRatio = CGFloat(length) / CGFloat(normalizedCount)
                let lineFrame = CGRect(
                    x: line.bounds.minX * windowSize.width,
                    y: line.bounds.minY * windowSize.height,
                    width: line.bounds.width * windowSize.width,
                    height: line.bounds.height * windowSize.height
                )
                let sourceHeight = max(11, lineFrame.height)
                let estimatedWidth = max(36, lineFrame.width * widthRatio)
                let chineseWidth = CGFloat(match.entry.chinese.count) * sourceHeight * 1.05 + 12
                let labelFrame = CGRect(
                    x: lineFrame.minX + lineFrame.width * xRatio,
                    y: lineFrame.minY - 2,
                    width: min(windowSize.width - lineFrame.minX, max(estimatedWidth, chineseWidth)),
                    height: max(sourceHeight + 5, match.entry.chinese.contains("\n") ? sourceHeight * 2.2 : sourceHeight + 5)
                )
                result.append(OverlayLabel(
                    text: match.entry.chinese,
                    frame: labelFrame.integral,
                    sourceHeight: sourceHeight,
                    prominent: match.entry.isLongText || match.entry.isTaskName
                ))
            }
        }
        return result
    }
}

private final class OverlayCanvasView: NSView {
    var labels: [OverlayLabel] = [] { didSet { needsDisplay = true } }
    override var isFlipped: Bool { false }

    override func draw(_ dirtyRect: NSRect) {
        super.draw(dirtyRect)
        for label in labels where label.frame.intersects(dirtyRect) {
            let path = NSBezierPath(roundedRect: label.frame, xRadius: 3, yRadius: 3)
            NSColor(calibratedRed: 0.10, green: 0.055, blue: 0.12, alpha: 0.94).setFill()
            path.fill()
            NSColor(calibratedRed: 1.0, green: 0.18, blue: 0.52, alpha: 0.96).setStroke()
            path.lineWidth = label.prominent ? 1.4 : 0.9
            path.stroke()
            let fontSize = min(22, max(10, label.sourceHeight * 0.72))
            let paragraph = NSMutableParagraphStyle()
            paragraph.lineBreakMode = .byTruncatingTail
            let attributes: [NSAttributedString.Key: Any] = [
                .font: NSFont.systemFont(ofSize: fontSize, weight: label.prominent ? .semibold : .medium),
                .foregroundColor: NSColor.white,
                .paragraphStyle: paragraph
            ]
            NSString(string: label.text).draw(
                in: label.frame.insetBy(dx: 5, dy: 2),
                withAttributes: attributes
            )
        }
    }
}
