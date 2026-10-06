import AppKit

@MainActor
private func runFengYuMu() {
    let application = NSApplication.shared
    let delegate = AppDelegate()
    application.delegate = delegate
    application.run()
}

MainActor.assumeIsolated {
    runFengYuMu()
}
