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

enum CaptureError: LocalizedError, Equatable {
    case permissionDenied
    case noWindow
    case windowChanged
    case captureFailed

    var errorDescription: String? {
        switch self {
        case .permissionDenied: return "请先允许枫语幕使用“屏幕与系统音频录制”权限"
        case .noWindow: return "没有找到可识别的游戏窗口；请先打开并点击游戏"
        case .windowChanged: return "游戏窗口已关闭或更换；请按 F9，点击游戏后再按 F8"
        case .captureFailed: return "游戏窗口截图失败，请重新点击游戏后再试"
        }
    }
}

final class ScreenCaptureService {
    private var lastWindowID: CGWindowID?
    private var lastProcessID: pid_t?
    private let ownPID = ProcessInfo.processInfo.processIdentifier

    var hasPermission: Bool { CGPreflightScreenCaptureAccess() }

    @discardableResult
    func requestPermission() -> Bool {
        CGRequestScreenCaptureAccess()
    }

    func captureGameWindow() async throws -> CapturedGameWindow {
        guard hasPermission else { throw CaptureError.permissionDenied }
        let content = try await SCShareableContent.excludingDesktopWindows(true, onScreenWindowsOnly: true)
        let window = try selectWindow(from: content.windows)

        let filter = SCContentFilter(desktopIndependentWindow: window)
        let configuration = SCStreamConfiguration()
        let scale = max(CGFloat(filter.pointPixelScale), 1)
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
        lastProcessID = window.owningApplication?.processID
        return CapturedGameWindow(
            image: image,
            frame: window.frame,
            windowID: window.windowID,
            applicationName: window.owningApplication?.applicationName ?? "游戏"
        )
    }

    func resetWindowChoice() {
        lastWindowID = nil
        lastProcessID = nil
    }

    private func selectWindow(from windows: [SCWindow]) throws -> SCWindow {
        let usable = windows.filter {
            $0.isOnScreen && $0.frame.width >= 640 && $0.frame.height >= 480 &&
            $0.owningApplication?.processID != ownPID
        }
        let gameWords = [
            "maplestory", "maple story", "maplestory worlds", "冒险岛",
            "artale", "classic world", "mapleland", "maplelegends", "mapleroyals"
        ]
        let gameWindows = usable.filter { window in
            guard let application = window.owningApplication else { return false }
            let identity = (application.applicationName + " " + application.bundleIdentifier).lowercased()
            return gameWords.contains(where: identity.contains)
        }
        if let remembered = lastWindowID, let rememberedPID = lastProcessID,
           let window = gameWindows.first(where: {
               $0.windowID == remembered && $0.owningApplication?.processID == rememberedPID
           }) {
            return window
        }
        if lastWindowID != nil || lastProcessID != nil {
            throw CaptureError.windowChanged
        }
        let frontPID = NSWorkspace.shared.frontmostApplication?.processIdentifier
        if let game = gameWindows.filter({ $0.owningApplication?.processID == frontPID })
            .max(by: { area($0.frame) < area($1.frame) }) {
            return game
        }
        if let game = gameWindows.max(by: { area($0.frame) < area($1.frame) }) {
            return game
        }
        throw CaptureError.noWindow
    }

    private func area(_ rect: CGRect) -> CGFloat { rect.width * rect.height }

    static func cocoaFrame(fromScreenCaptureFrame frame: CGRect) -> CGRect {
        let mainTop = NSScreen.screens.first?.frame.maxY ?? 0
        return CGRect(x: frame.minX, y: mainTop - frame.maxY, width: frame.width, height: frame.height)
    }
}
