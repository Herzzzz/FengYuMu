import AppKit
import CoreGraphics
import ScreenCaptureKit

struct CapturedGameWindow {
    let image: CGImage
    /// ScreenCaptureKit uses the Core Graphics global coordinate system (top-left origin).
    let frame: CGRect
    let windowID: CGWindowID
    let applicationName: String
}

enum CaptureError: LocalizedError {
    case permissionDenied
    case noWindow
    case captureFailed

    var errorDescription: String? {
        switch self {
        case .permissionDenied: return "请先允许枫语幕使用“屏幕与系统音频录制”权限"
        case .noWindow: return "没有找到可识别的游戏窗口；请先打开并点击游戏"
        case .captureFailed: return "游戏窗口截图失败，请重新点击游戏后再试"
        }
    }
}

final class ScreenCaptureService {
    private var lastWindowID: CGWindowID?
    private let ownPID = ProcessInfo.processInfo.processIdentifier

    var hasPermission: Bool { CGPreflightScreenCaptureAccess() }

    @discardableResult
    func requestPermission() -> Bool {
        CGRequestScreenCaptureAccess()
    }

    func captureGameWindow() async throws -> CapturedGameWindow {
        guard hasPermission else { throw CaptureError.permissionDenied }
        let content = try await SCShareableContent.excludingDesktopWindows(true, onScreenWindowsOnly: true)
        guard let window = selectWindow(from: content.windows) else { throw CaptureError.noWindow }

        let filter = SCContentFilter(desktopIndependentWindow: window)
        let configuration = SCStreamConfiguration()
        let scale = backingScale(for: window.frame)
        configuration.width = max(1, Int(window.frame.width * scale))
        configuration.height = max(1, Int(window.frame.height * scale))
        configuration.showsCursor = false
        configuration.capturesAudio = false
        configuration.ignoreShadowsSingleWindow = true
        configuration.includeChildWindows = false

        let image = try await SCScreenshotManager.captureImage(
            contentFilter: filter,
            configuration: configuration
        )
        lastWindowID = window.windowID
        return CapturedGameWindow(
            image: image,
            frame: window.frame,
            windowID: window.windowID,
            applicationName: window.owningApplication?.applicationName ?? "游戏"
        )
    }

    func resetWindowChoice() { lastWindowID = nil }

    private func selectWindow(from windows: [SCWindow]) -> SCWindow? {
        let usable = windows.filter {
            $0.isOnScreen && $0.frame.width >= 640 && $0.frame.height >= 480 &&
            $0.owningApplication?.processID != ownPID
        }
        let frontPID = NSWorkspace.shared.frontmostApplication?.processIdentifier
        if let front = usable.filter({ $0.owningApplication?.processID == frontPID })
            .max(by: { area($0.frame) < area($1.frame) }) {
            return front
        }
        if let remembered = lastWindowID,
           let window = usable.first(where: { $0.windowID == remembered }) {
            return window
        }
        let gameWords = ["maplestory", "maple story", "冒险岛", "artale", "classic world"]
        if let game = usable.filter({ window in
            let name = ((window.owningApplication?.applicationName ?? "") + " " + (window.title ?? "")).lowercased()
            return gameWords.contains(where: name.contains)
        }).max(by: { area($0.frame) < area($1.frame) }) {
            return game
        }
        return nil
    }

    private func backingScale(for frame: CGRect) -> CGFloat {
        let cocoaFrame = Self.cocoaFrame(fromScreenCaptureFrame: frame)
        return NSScreen.screens.first(where: { $0.frame.intersects(cocoaFrame) })?.backingScaleFactor ?? 2
    }

    private func area(_ rect: CGRect) -> CGFloat { rect.width * rect.height }

    static func cocoaFrame(fromScreenCaptureFrame frame: CGRect) -> CGRect {
        let mainTop = NSScreen.screens.first?.frame.maxY ?? 0
        return CGRect(x: frame.minX, y: mainTop - frame.maxY, width: frame.width, height: frame.height)
    }
}
