import CoreGraphics
import Vision

struct OCRLine: Sendable {
    let text: String
    /// Vision-normalized coordinates, bottom-left origin.
    let bounds: CGRect
    let confidence: Float
}

enum OCRError: LocalizedError {
    case unavailable

    var errorDescription: String? { "系统文字识别没有返回结果" }
}

final class OCRService {
    func recognize(_ image: CGImage, fast: Bool = false) async throws -> [OCRLine] {
        try await Task.detached(priority: fast ? .utility : .userInitiated) {
            let request = VNRecognizeTextRequest()
            request.recognitionLevel = fast ? .fast : .accurate
            request.recognitionLanguages = ["en-US"]
            request.usesLanguageCorrection = true
            request.minimumTextHeight = fast ? 0.008 : 0.005
            let handler = VNImageRequestHandler(cgImage: image, orientation: .up)
            try handler.perform([request])
            let observations = request.results ?? []
            return observations.compactMap { observation in
                guard let candidate = observation.topCandidates(1).first,
                      !candidate.string.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { return nil }
                return OCRLine(text: candidate.string, bounds: observation.boundingBox,
                               confidence: candidate.confidence)
            }.sorted {
                if abs($0.bounds.maxY - $1.bounds.maxY) > 0.012 { return $0.bounds.maxY > $1.bounds.maxY }
                return $0.bounds.minX < $1.bounds.minX
            }
        }.value
    }
}
