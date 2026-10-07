import AppKit
import Combine
import FengYuMuCore

@MainActor
final class AppState: ObservableObject {
    static let version = "3.2.4-mac.1"

    @Published var status = "正在载入词库…"
    @Published var dictionaryCount = 0
    @Published var rangeMode: TranslationRangeMode = .balanced {
        didSet { UserDefaults.standard.set(rangeMode.rawValue, forKey: "TranslationRange") }
    }
    @Published var continuousTranslation = false {
        didSet { updateContinuousLoop() }
    }
    @Published var isWorking = false
    @Published var aiSettings: AISettings
    @Published var chatTranslations: [ChatTranslation] = []
    @Published var aiMonitorRunning = false
    @Published var hotkeyStatus = "F8 翻译开/关 · F9 重新找游戏 · F10 AI 悬浮窗"

    let store = TranslationStore()
    let capture = ScreenCaptureService()
    let ocr = OCRService()
    let overlay = OverlayController()
    let aiClient = AIClient()
    let aiSettingsStore = AISettingsStore()
    private var continuousTask: Task<Void, Never>?
    private(set) var chatMonitor: ChatMonitor!

    var showAIWindow: (() -> Void)?
    var showMainWindow: (() -> Void)?

    init() {
        let saved = UserDefaults.standard.integer(forKey: "TranslationRange")
        rangeMode = TranslationRangeMode(rawValue: saved) ?? .balanced
        aiSettings = aiSettingsStore.load()
        chatMonitor = ChatMonitor(
            capture: capture, ocr: ocr, store: store, client: aiClient,
            settings: { [weak self] in self?.aiSettings ?? AISettings() },
            onTranslation: { [weak self] item in
                self?.chatTranslations.append(item)
                if let count = self?.chatTranslations.count, count > 100 {
                    self?.chatTranslations.removeFirst(count - 80)
                }
            },
            onFatalError: { [weak self] error in
                self?.aiMonitorRunning = false
                self?.status = error.localizedDescription
            }
        )
    }

    func start() {
        do {
            let url = try DictionaryLocator.locate()
            dictionaryCount = try store.load(from: url)
            status = "已就绪 · 词库 \(dictionaryCount) 条"
        } catch {
            status = error.localizedDescription
        }
    }

    func toggleTranslation() {
        if overlay.isVisible || isWorking {
            overlay.hide()
            isWorking = false
            status = "翻译已隐藏"
        } else {
            Task { await translateOnce() }
        }
    }

    func translateOnce(silentWhenEmpty: Bool = false) async {
        guard !isWorking, dictionaryCount > 0 else { return }
        isWorking = true
        if !silentWhenEmpty { overlay.hide() }
        status = "正在识别游戏画面…"
        defer { isWorking = false }
        do {
            let shot = try await capture.captureGameWindow()
            let lines = try await ocr.recognize(shot.image)
            let hitCount = lines.reduce(0) { $0 + store.matches(in: $1.text, mode: rangeMode).count }
            if hitCount == 0 {
                overlay.hide()
                if !silentWhenEmpty { status = "识别完成，但当前画面没有命中词库" }
            } else {
                overlay.show(lines: lines, store: store, mode: rangeMode, over: shot)
                status = "已显示 \(hitCount) 处翻译 · \(shot.applicationName)"
            }
        } catch let error as CaptureError {
            overlay.hide()
            if error == .windowChanged, continuousTranslation {
                continuousTranslation = false
            }
            status = error.localizedDescription
        } catch {
            overlay.hide()
            status = error.localizedDescription
        }
    }

    func realignGameWindow() {
        capture.resetWindowChoice()
        status = "已清除旧窗口位置；点击游戏后再按 F8"
    }

    func requestScreenPermission() {
        if capture.requestPermission() {
            status = "屏幕录制权限已允许；若仍无画面，请重新启动枫语幕"
        } else {
            openScreenPermissionSettings()
            status = "请在系统设置中允许枫语幕录制屏幕，然后重新启动"
        }
    }

    func openScreenPermissionSettings() {
        if let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_ScreenCapture") {
            NSWorkspace.shared.open(url)
        }
    }

    func saveAISettings(_ value: AISettings) throws {
        try aiSettingsStore.save(value)
        aiSettings = value
    }

    func setChatMonitoring(_ enabled: Bool) {
        if enabled {
            guard aiSettings.isReady else {
                status = "请先在 AI 设置中填写 API Key"
                showAIWindow?()
                return
            }
            chatMonitor.start()
            aiMonitorRunning = true
            status = "AI 实时聊天翻译已开启"
        } else {
            chatMonitor.stop()
            aiMonitorRunning = false
            status = "AI 实时聊天翻译已停止"
        }
    }

    func shutdown() {
        continuousTask?.cancel()
        continuousTask = nil
        chatMonitor.stop()
        aiMonitorRunning = false
        overlay.hide()
    }

    private func updateContinuousLoop() {
        continuousTask?.cancel()
        continuousTask = nil
        guard continuousTranslation else { return }
        continuousTask = Task { [weak self] in
            while !Task.isCancelled {
                await self?.translateOnce(silentWhenEmpty: true)
                try? await Task.sleep(for: .seconds(1.2))
            }
        }
    }
}

enum DictionaryLocator {
    static func locate() throws -> URL {
        let manager = FileManager.default
        let candidates: [URL?] = [
            Bundle.main.resourceURL?.appendingPathComponent("枫语幕词库.tsv"),
            Bundle.main.bundleURL.deletingLastPathComponent().appendingPathComponent("枫语幕词库.tsv"),
            URL(fileURLWithPath: manager.currentDirectoryPath).appendingPathComponent("枫语幕词库.tsv"),
            URL(fileURLWithPath: manager.currentDirectoryPath).deletingLastPathComponent()
                .appendingPathComponent("枫语幕词库.tsv")
        ]
        if let url = candidates.compactMap({ $0 }).first(where: { manager.fileExists(atPath: $0.path) }) {
            return url
        }
        throw DictionaryError.missingFile(
            Bundle.main.resourceURL?.appendingPathComponent("枫语幕词库.tsv") ??
            URL(fileURLWithPath: "枫语幕词库.tsv")
        )
    }
}
