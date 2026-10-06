import AppKit
import Carbon
import SwiftUI

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    private let state = AppState()
    private var mainWindow: NSWindow!
    private var aiWindow: NSPanel!
    private var statusItem: NSStatusItem!
    private var hotkeys: GlobalHotKeyManager?

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.regular)
        makeMainWindow()
        makeAIWindow()
        makeStatusItem()
        registerHotkeys()
        state.showMainWindow = { [weak self] in self?.showMainWindow() }
        state.showAIWindow = { [weak self] in self?.showAIWindow() }
        state.start()
        showMainWindow()
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { false }

    private func makeMainWindow() {
        let controller = NSHostingController(rootView: MainView(model: state))
        mainWindow = NSWindow(contentViewController: controller)
        mainWindow.title = "枫语幕 v\(AppState.version)"
        mainWindow.styleMask = [.titled, .closable, .miniaturizable]
        mainWindow.isReleasedWhenClosed = false
        mainWindow.center()
    }

    private func makeAIWindow() {
        let viewModel = AIWindowModel(app: state)
        let controller = NSHostingController(rootView: AIWindowView(app: state, model: viewModel))
        aiWindow = NSPanel(contentViewController: controller)
        aiWindow.title = "枫语幕 AI 翻译"
        aiWindow.styleMask = [.titled, .closable, .resizable, .nonactivatingPanel]
        aiWindow.level = .floating
        aiWindow.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        aiWindow.isReleasedWhenClosed = false
        aiWindow.setContentSize(NSSize(width: 580, height: 430))
    }

    private func makeStatusItem() {
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        statusItem.button?.title = "枫"
        statusItem.button?.toolTip = "枫语幕 v\(AppState.version)"
        let menu = NSMenu()
        menu.addItem(withTitle: "打开枫语幕", action: #selector(openMain), keyEquivalent: "")
        menu.addItem(withTitle: "识别/隐藏当前画面（F8）", action: #selector(toggleOverlay), keyEquivalent: "")
        menu.addItem(withTitle: "AI 实时聊天翻译（F10）", action: #selector(openAI), keyEquivalent: "")
        menu.addItem(.separator())
        menu.addItem(withTitle: "退出", action: #selector(quit), keyEquivalent: "q")
        menu.items.forEach { $0.target = self }
        statusItem.menu = menu
    }

    private func registerHotkeys() {
        guard let manager = GlobalHotKeyManager() else {
            state.hotkeyStatus = "全局快捷键初始化失败；仍可使用菜单栏按钮"
            return
        }
        let f8 = manager.register(id: 1, keyCode: UInt32(kVK_F8)) { [weak state] in state?.toggleTranslation() }
        let f9 = manager.register(id: 2, keyCode: UInt32(kVK_F9)) { [weak state] in state?.realignGameWindow() }
        let f10 = manager.register(id: 3, keyCode: UInt32(kVK_F10)) { [weak self] in self?.showAIWindow() }
        if !(f8 && f9 && f10) { state.hotkeyStatus = "部分 F 键被其他软件占用；可用菜单栏操作" }
        hotkeys = manager
    }

    private func showMainWindow() {
        mainWindow.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
    }

    private func showAIWindow() {
        aiWindow.orderFrontRegardless()
    }

    @objc private func openMain() { showMainWindow() }
    @objc private func openAI() { showAIWindow() }
    @objc private func toggleOverlay() { state.toggleTranslation() }
    @objc private func quit() { NSApp.terminate(nil) }
}
