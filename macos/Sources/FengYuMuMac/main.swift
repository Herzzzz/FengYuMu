import AppKit
import Darwin
import Dispatch

if CommandLine.arguments.contains("--fixture-self-test") {
    Task {
        do {
            try await FixtureSelfTest.run(arguments: CommandLine.arguments)
            exit(EXIT_SUCCESS)
        } catch {
            fputs("macOS fixture self-test failed: \(error.localizedDescription)\n", stderr)
            exit(EXIT_FAILURE)
        }
    }
    dispatchMain()
}

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
