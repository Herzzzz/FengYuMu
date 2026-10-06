import CoreGraphics
import FengYuMuCore
import Foundation
import ImageIO

enum FixtureSelfTestError: LocalizedError {
    case arguments
    case image
    case noText
    case noDictionaryMatch

    var errorDescription: String? {
        switch self {
        case .arguments: return "用法：--fixture-self-test <词库.tsv> <游戏截图.png>"
        case .image: return "无法读取游戏测试截图"
        case .noText: return "Vision 没有从游戏截图识别出英文"
        case .noDictionaryMatch: return "Vision 识别结果没有命中枫语幕词库"
        }
    }
}

enum FixtureSelfTest {
    static func run(arguments: [String]) async throws {
        guard let flag = arguments.firstIndex(of: "--fixture-self-test"),
              arguments.count > flag + 2 else { throw FixtureSelfTestError.arguments }
        let dictionaryURL = URL(fileURLWithPath: arguments[flag + 1])
        let imageURL = URL(fileURLWithPath: arguments[flag + 2])
        guard let source = CGImageSourceCreateWithURL(imageURL as CFURL, nil),
              let image = CGImageSourceCreateImageAtIndex(source, 0, nil) else {
            throw FixtureSelfTestError.image
        }
        let store = TranslationStore()
        let dictionaryCount = try store.load(from: dictionaryURL)
        let lines = try await OCRService().recognize(image)
        guard lines.count >= 3 else { throw FixtureSelfTestError.noText }
        let matches = lines.flatMap { store.matches(in: $0.text, mode: .maximum) }
        guard !matches.isEmpty else { throw FixtureSelfTestError.noDictionaryMatch }
        let sample = matches.prefix(8).map { $0.entry.chinese }.joined(separator: " | ")
        print("macOS fixture self-test passed: dictionary=\(dictionaryCount), OCR lines=\(lines.count), matches=\(matches.count)")
        print("sample=\(sample)")
    }
}
