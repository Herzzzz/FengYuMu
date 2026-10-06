import CoreGraphics
import FengYuMuCore

struct ChatTranslation: Identifiable, Equatable {
    let id = UUID()
    let source: String
    let translation: String
}

@MainActor
final class ChatMonitor {
    private let capture: ScreenCaptureService
    private let ocr: OCRService
    private let store: TranslationStore
    private let client: AIClient
    private let settings: () -> AISettings
    private let onTranslation: (ChatTranslation) -> Void
    private var loop: Task<Void, Never>?
    private var seen: [String] = []

    var isRunning: Bool { loop != nil }

    init(capture: ScreenCaptureService, ocr: OCRService, store: TranslationStore,
         client: AIClient, settings: @escaping () -> AISettings,
         onTranslation: @escaping (ChatTranslation) -> Void) {
        self.capture = capture
        self.ocr = ocr
        self.store = store
        self.client = client
        self.settings = settings
        self.onTranslation = onTranslation
    }

    func start() {
        guard loop == nil else { return }
        loop = Task { [weak self] in
            while !Task.isCancelled {
                await self?.scanOnce()
                try? await Task.sleep(for: .milliseconds(650))
            }
        }
    }

    func stop() {
        loop?.cancel()
        loop = nil
    }

    private func scanOnce() async {
        do {
            let window = try await capture.captureGameWindow()
            guard let chat = cropChat(from: window.image) else { return }
            let lines = try await ocr.recognize(chat, fast: true)
            for raw in lines.map(\.text) {
                guard let source = playerMessage(from: raw), remember(source) else { continue }
                if let fixed = store.deterministicChatTranslation(for: source) {
                    onTranslation(ChatTranslation(source: source, translation: fixed))
                    continue
                }
                let value = settings()
                guard value.isReady else { continue }
                let translated = try await client.translate(
                    source: source, targetEnglish: false,
                    glossary: store.glossary(for: source), settings: value
                )
                onTranslation(ChatTranslation(source: source, translation: translated))
            }
        } catch {
            // The next scan retries. Repeated permission/configuration errors are surfaced by the UI.
        }
    }

    private func cropChat(from image: CGImage) -> CGImage? {
        let width = CGFloat(image.width), height = CGFloat(image.height)
        // MapleStory chat is normally in the lower-left. CGImage crop uses top-left coordinates.
        let rect = CGRect(x: width * 0.07, y: height * 0.68,
                          width: width * 0.60, height: height * 0.28).integral
        return image.cropping(to: rect)
    }

    private func playerMessage(from raw: String) -> String? {
        let text = raw.trimmingCharacters(in: .whitespacesAndNewlines)
        guard text.count >= 2 else { return nil }
        let lower = text.lowercased()
        let systemFragments = [
            "you don't have enough mp", "you do not have enough mp", "not enough hp",
            "not enough mp", "low hp warning", "low mp warning", "you have gained exp",
            "you received exp", "channel is now open", "maple tip"
        ]
        guard !systemFragments.contains(where: lower.contains) else { return nil }
        if let colon = text.firstIndex(where: { $0 == ":" || $0 == "：" }) {
            let body = text[text.index(after: colon)...].trimmingCharacters(in: .whitespaces)
            return body.count >= 2 ? body : nil
        }
        // Megaphones sometimes omit a reliably recognized separator. Keep longer lines only.
        return text.count >= 7 ? text : nil
    }

    private func remember(_ value: String) -> Bool {
        let key = TranslationStore.normalize(value)
        guard !key.isEmpty, !seen.contains(key) else { return false }
        seen.append(key)
        if seen.count > 160 { seen.removeFirst(seen.count - 120) }
        return true
    }
}
