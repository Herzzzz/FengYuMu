import Foundation

public struct TranslationEntry: Hashable, Sendable {
    public let english: String
    public let chinese: String
    public let category: String
    public let normalized: String
    public let source: String
    public let region: String

    public var taskID: String? {
        guard isTaskName || isTaskText, let marker = category.lastIndex(of: "#") else { return nil }
        return String(category[category.index(after: marker)...])
    }

    public var isTaskName: Bool { category.hasPrefix("怀旧服-任务#") }
    public var isTaskText: Bool {
        category.hasPrefix("怀旧服-任务说明#") || category.hasPrefix("怀旧服-任务对白#")
    }
    public var isSkillText: Bool { category.hasPrefix("怀旧服-技能说明#") }
    public var isItemText: Bool {
        category.hasPrefix("怀旧服-装备说明#") || category.hasPrefix("怀旧服-物品说明#")
    }
    public var isInterfaceText: Bool { category.hasPrefix("怀旧服-界面长句") }
    public var isSettingsText: Bool { category.hasPrefix("怀旧服-设置") }
    public var isNPCDialogue: Bool { category.hasPrefix("怀旧服-NPC对白#") }
    public var isPlayerChat: Bool { category.hasPrefix("怀旧服-聊天") }
    public var isLongText: Bool {
        isTaskText || isSkillText || isItemText || isInterfaceText || isSettingsText || isNPCDialogue
    }
}

public struct TranslationMatch: Hashable, Sendable {
    public let entry: TranslationEntry
    public let normalizedStart: Int
    public let normalizedLength: Int
}

public enum TranslationRangeMode: Int, CaseIterable, Hashable, Sendable {
    case maximum = 1
    case balanced = 2
    case minimum = 3

    public var title: String {
        switch self {
        case .maximum: return "翻译更多"
        case .balanced: return "日常推荐"
        case .minimum: return "只翻重点"
        }
    }
}

public enum DictionaryError: LocalizedError {
    case missingFile(URL)
    case unreadableFile(URL)

    public var errorDescription: String? {
        switch self {
        case .missingFile(let url): return "找不到词库：\(url.path)"
        case .unreadableFile(let url): return "无法读取词库：\(url.path)"
        }
    }
}

public final class TranslationStore: @unchecked Sendable {
    public private(set) var entries: [TranslationEntry] = []
    public private(set) var loadedURL: URL?
    private var buckets: [Character: [TranslationEntry]] = [:]
    private var taskBuckets: [Character: [TranslationEntry]] = [:]
    private var chatEntries: [TranslationEntry] = []

    public init() {}

    @discardableResult
    public func load(from url: URL) throws -> Int {
        guard FileManager.default.fileExists(atPath: url.path) else {
            throw DictionaryError.missingFile(url)
        }
        guard let reader = LineReader(url: url) else { throw DictionaryError.unreadableFile(url) }

        var parsed: [TranslationEntry] = []
        var seen = Set<String>()
        for raw in reader {
            let trimmed = raw.trimmingCharacters(in: .whitespacesAndNewlines)
            if trimmed.isEmpty || trimmed.hasPrefix("#") { continue }
            let parts = raw.split(separator: "\t", omittingEmptySubsequences: false).map(String.init)
            guard parts.count >= 2 else { continue }
            let english = parts[0].trimmingCharacters(in: .whitespacesAndNewlines)
            let chinese = Self.polishChinese(parts[1].trimmingCharacters(in: .whitespacesAndNewlines))
            let category = parts.count > 2 ? parts[2].trimmingCharacters(in: .whitespacesAndNewlines) : ""
            let source = parts.count > 3 ? parts[3].trimmingCharacters(in: .whitespacesAndNewlines) : ""
            let region = parts.count > 4 ? parts[4].trimmingCharacters(in: .whitespacesAndNewlines) : ""
            let normalized = Self.normalize(english)
            guard !normalized.isEmpty, !chinese.isEmpty else { continue }

            let temporary = TranslationEntry(
                english: english, chinese: chinese, category: category,
                normalized: normalized, source: source, region: region
            )
            let scope = temporary.taskID ?? (temporary.isLongText ? category : "general")
            guard seen.insert("\(normalized)\t\(scope)").inserted else { continue }
            parsed.append(temporary)
        }

        let ambiguous = Dictionary(grouping: parsed.filter { !$0.isLongText && !$0.isTaskName }) {
            $0.normalized
        }.filter { Set($0.value.map(\.chinese)).count > 1 }.keys
        let ambiguousSet = Set(ambiguous)

        var regular: [Character: [TranslationEntry]] = [:]
        var tasks: [Character: [TranslationEntry]] = [:]
        var chat: [TranslationEntry] = []
        for entry in parsed {
            guard let key = entry.normalized.first else { continue }
            if entry.isPlayerChat {
                chat.append(entry)
            } else if entry.isTaskName || entry.isTaskText {
                tasks[key, default: []].append(entry)
            } else if entry.isLongText || !ambiguousSet.contains(entry.normalized) {
                regular[key, default: []].append(entry)
            }
        }
        for key in regular.keys { regular[key]?.sort { $0.normalized.count > $1.normalized.count } }
        for key in tasks.keys { tasks[key]?.sort { $0.normalized.count > $1.normalized.count } }

        entries = parsed
        buckets = regular
        taskBuckets = tasks
        chatEntries = chat
        loadedURL = url
        return parsed.count
    }

    public func matches(in text: String, mode: TranslationRangeMode = .balanced) -> [TranslationMatch] {
        let normalized = Self.normalize(text)
        guard !normalized.isEmpty else { return [] }
        let sourceBuckets = buckets.merging(taskBuckets) { $0 + $1 }
        var results: [TranslationMatch] = []
        var occupied = Set<Int>()

        for index in normalized.indices {
            guard let candidates = sourceBuckets[normalized[index]] else { continue }
            let offset = normalized.distance(from: normalized.startIndex, to: index)
            for entry in candidates where isAllowed(entry, mode: mode) {
                guard normalized[index...].hasPrefix(entry.normalized) else { continue }
                let end = normalized.index(index, offsetBy: entry.normalized.count)
                guard Self.isBoundary(normalized, before: index), Self.isBoundary(normalized, after: end) else { continue }
                let range = offset..<(offset + entry.normalized.count)
                guard range.allSatisfy({ !occupied.contains($0) }) else { continue }
                occupied.formUnion(range)
                results.append(TranslationMatch(entry: entry, normalizedStart: offset,
                                                normalizedLength: entry.normalized.count))
                break
            }
        }
        return results.sorted {
            $0.normalizedStart < $1.normalizedStart
        }
    }

    public func glossary(for text: String, limit: Int = 10) -> String {
        let normalized = Self.normalize(text)
        guard !normalized.isEmpty else { return "" }
        let candidates = entries.filter {
            !$0.isLongText && !$0.isTaskText && normalized.contains($0.normalized) && $0.normalized.count >= 3
        }.sorted { $0.normalized.count > $1.normalized.count }
        var seen = Set<String>()
        return candidates.compactMap { entry -> String? in
            guard seen.insert(entry.normalized).inserted else { return nil }
            return "\(entry.english)=\(entry.chinese)"
        }.prefix(limit).joined(separator: "；")
    }

    public func deterministicChatTranslation(for text: String) -> String? {
        let normalized = Self.normalize(text)
        return chatEntries.first(where: { $0.normalized == normalized })?.chinese
    }

    private func isAllowed(_ entry: TranslationEntry, mode: TranslationRangeMode) -> Bool {
        switch mode {
        case .maximum: return true
        case .balanced:
            return entry.normalized.count >= 3 || entry.isLongText || entry.isTaskName
        case .minimum:
            return entry.normalized.count >= 5 || entry.isLongText || entry.isTaskName
        }
    }

    public static func normalize(_ value: String) -> String {
        let corrected: [String: String] = [
            "ouest": "quest", "ojest": "quest", "lerve": "leave", "pethils": "details",
            "reo": "req", "aeq": "req", "attacx": "attack", "torget": "forget",
            "tun": "fun", "wont": "won't", "cant": "can't", "dont": "don't",
            "ive": "i've", "thankyou": "thank you"
        ]
        var tokens: [String] = []
        var current = ""
        for scalar in value.lowercased().unicodeScalars {
            let scalarText = (scalar.value == 0x2019 || scalar.value == 0x2018) ? "'" : String(scalar)
            let character = Character(scalarText)
            if CharacterSet.alphanumerics.contains(scalar) || "'+%#".contains(character) {
                current.append(character)
            } else if !current.isEmpty {
                let clean = current.trimmingCharacters(in: CharacterSet(charactersIn: "'"))
                if !clean.isEmpty { tokens.append(corrected[clean] ?? clean) }
                current = ""
            }
        }
        if !current.isEmpty {
            let clean = current.trimmingCharacters(in: CharacterSet(charactersIn: "'"))
            if !clean.isEmpty { tokens.append(corrected[clean] ?? clean) }
        }
        return tokens.joined(separator: " ")
            .replacingOccurrences(of: "jest helper", with: "quest helper")
            .replacingOccurrences(of: "uest helper", with: "quest helper")
    }

    private static func polishChinese(_ value: String) -> String {
        value.replacingOccurrences(of: "<br>", with: "\n")
            .replacingOccurrences(of: "Meso", with: "金币")
            .replacingOccurrences(of: "meso", with: "金币")
            .replacingOccurrences(of: "DEX", with: "敏捷")
            .replacingOccurrences(of: "STR", with: "力量")
            .replacingOccurrences(of: "INT", with: "智力")
            .replacingOccurrences(of: "LUK", with: "运气")
            .replacingOccurrences(of: "8x伤害", with: "8倍伤害")
    }

    private static func isBoundary(_ text: String, before index: String.Index) -> Bool {
        guard index > text.startIndex else { return true }
        let character = text[text.index(before: index)]
        return !character.isLetter && !character.isNumber && character != "'"
    }

    private static func isBoundary(_ text: String, after index: String.Index) -> Bool {
        guard index < text.endIndex else { return true }
        let character = text[index]
        return !character.isLetter && !character.isNumber && character != "'"
    }
}

private final class LineReader: Sequence, IteratorProtocol {
    private let handle: FileHandle
    private var buffer = Data()
    private var reachedEOF = false

    init?(url: URL) {
        guard let handle = try? FileHandle(forReadingFrom: url) else { return nil }
        self.handle = handle
    }

    deinit { try? handle.close() }

    func next() -> String? {
        while true {
            if let newline = buffer.firstIndex(of: 0x0A) {
                let line = buffer.prefix(upTo: newline)
                buffer.removeSubrange(...newline)
                return String(decoding: line, as: UTF8.self).trimmingCharacters(in: .newlines)
            }
            if reachedEOF {
                guard !buffer.isEmpty else { return nil }
                defer { buffer.removeAll() }
                return String(decoding: buffer, as: UTF8.self).trimmingCharacters(in: .newlines)
            }
            do {
                let data = try handle.read(upToCount: 64 * 1024) ?? Data()
                if data.isEmpty { reachedEOF = true } else { buffer.append(data) }
            } catch {
                reachedEOF = true
            }
        }
    }
}
