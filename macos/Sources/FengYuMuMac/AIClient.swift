import Foundation
import Security

struct AISettings: Equatable {
    var provider = "豆包 2.0 Lite（推荐）"
    var endpoint = "https://ark.cn-beijing.volces.com/api/v3/responses"
    var model = "doubao-seed-2-0-lite-260215"
    var apiKey = ""

    var isReady: Bool {
        endpoint.lowercased().hasPrefix("https://") && !model.isEmpty && !apiKey.isEmpty
    }

    static let presets: [(name: String, endpoint: String, model: String)] = [
        ("豆包 2.0 Lite（推荐）", "https://ark.cn-beijing.volces.com/api/v3/responses", "doubao-seed-2-0-lite-260215"),
        ("DeepSeek V4 Flash（快速）", "https://api.deepseek.com/v1/chat/completions", "deepseek-v4-flash"),
        ("智谱 GLM-4-Flash（备用）", "https://open.bigmodel.cn/api/paas/v4/chat/completions", "glm-4-flash-250414"),
        ("自定义兼容接口", "https://", "")
    ]
}

final class AISettingsStore {
    private let defaults = UserDefaults.standard
    private let keychain = KeychainStore(service: "cn.fengyumu.macos")

    func load() -> AISettings {
        var value = AISettings()
        value.provider = defaults.string(forKey: "AIProvider") ?? value.provider
        value.endpoint = defaults.string(forKey: "AIEndpoint") ?? value.endpoint
        value.model = defaults.string(forKey: "AIModel") ?? value.model
        value.apiKey = keychain.read(account: "online-ai-key") ?? ""
        return value
    }

    func save(_ value: AISettings) throws {
        defaults.set(value.provider, forKey: "AIProvider")
        defaults.set(value.endpoint, forKey: "AIEndpoint")
        defaults.set(value.model, forKey: "AIModel")
        try keychain.write(value.apiKey, account: "online-ai-key")
    }
}

enum AIClientError: LocalizedError {
    case notConfigured
    case badResponse
    case service(Int, String)

    var errorDescription: String? {
        switch self {
        case .notConfigured: return "请先填写联网 AI 的接口、模型和 API Key"
        case .badResponse: return "联网 AI 没有返回有效译文"
        case .service(let code, let body):
            if code == 401 { return "API Key 不正确或复制不完整" }
            if code == 403 { return "模型未开通、没有权限或余额不足" }
            if code == 404 { return "接口地址或模型名不存在" }
            if code == 429 { return "调用过于频繁、额度或余额不足" }
            if code >= 500 { return "AI 服务暂时故障，请稍后再试" }
            return "AI 服务返回错误（HTTP \(code)）：\(body.prefix(100))"
        }
    }
}

final class AIClient {
    func translate(source: String, targetEnglish: Bool, glossary: String,
                   settings: AISettings) async throws -> String {
        guard settings.isReady else { throw AIClientError.notConfigured }
        let system = Self.systemPrompt(targetEnglish: targetEnglish)
        let user = "只翻译下面这一条玩家聊天：\n\(source)" +
            (glossary.isEmpty ? "" : "\n这句话的强制术语：\n\(glossary)")
        let responsesAPI = settings.endpoint.trimmingCharacters(in: CharacterSet(charactersIn: "/"))
            .hasSuffix("responses")
        var body: [String: Any] = ["model": settings.model, "temperature": 0.1]
        if responsesAPI {
            body["instructions"] = system
            body["input"] = user
            body["max_output_tokens"] = 96
            body["thinking"] = ["type": "disabled"]
        } else {
            body["max_tokens"] = 96
            body["messages"] = [
                ["role": "system", "content": system],
                ["role": "user", "content": user]
            ]
            if settings.model.hasPrefix("deepseek-") { body["thinking"] = ["type": "disabled"] }
            if settings.model.hasPrefix("glm-") { body["do_sample"] = false }
        }
        guard let url = URL(string: settings.endpoint) else { throw AIClientError.notConfigured }
        var request = URLRequest(url: url, timeoutInterval: 12)
        request.httpMethod = "POST"
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.setValue("Bearer \(settings.apiKey)", forHTTPHeaderField: "Authorization")
        request.httpBody = try JSONSerialization.data(withJSONObject: body)
        let (data, response) = try await URLSession.shared.data(for: request)
        guard let http = response as? HTTPURLResponse else { throw AIClientError.badResponse }
        guard (200..<300).contains(http.statusCode) else {
            throw AIClientError.service(http.statusCode, String(decoding: data, as: UTF8.self))
        }
        let root = try JSONSerialization.jsonObject(with: data)
        guard let result = Self.extractText(root)?.cleanAIOutput(), !result.isEmpty else {
            throw AIClientError.badResponse
        }
        return result
    }

    private static func extractText(_ object: Any) -> String? {
        guard let root = object as? [String: Any] else { return nil }
        if let text = root["output_text"] as? String, !text.isEmpty { return text }
        if let choices = root["choices"] as? [[String: Any]],
           let message = choices.first?["message"] as? [String: Any],
           let content = message["content"] as? String { return content }
        if let output = root["output"] as? [[String: Any]] {
            for item in output {
                guard let contents = item["content"] as? [[String: Any]] else { continue }
                for content in contents {
                    if let text = content["text"] as? String, !text.isEmpty { return text }
                }
            }
        }
        return nil
    }

    private static func systemPrompt(targetEnglish: Bool) -> String {
        let shared = "你是冒险岛怀旧服国际服老玩家，熟悉MapleStory Classic/Global。每次只译一条消息，按整句理解，禁止逐词硬译。熟悉地图、职业、装备、怪物、技能、任务、PQ、交易和玩家黑话。普通日常聊天按原意，禁止强行套用游戏黑话。保留数字、频道、价格、表情和语气。"
        if targetEnglish {
            return shared + "把中文改写成简短自然的英语玩家聊天；本人求组用J>/LFG/LFP，自己的队伍招募用R>/LFM/LF1，买入用B>/WTB，卖出用S>/WTS。禁止擅自添加PQ。只输出一行最终译文。"
        }
        return shared + "把外语翻成自然简短的简体中文玩家口语，专名优先采用给定术语。只输出一行最终译文。"
    }
}

private final class KeychainStore {
    let service: String
    init(service: String) { self.service = service }

    func read(account: String) -> String? {
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: account,
            kSecReturnData as String: true,
            kSecMatchLimit as String: kSecMatchLimitOne
        ]
        var item: CFTypeRef?
        guard SecItemCopyMatching(query as CFDictionary, &item) == errSecSuccess,
              let data = item as? Data else { return nil }
        return String(data: data, encoding: .utf8)
    }

    func write(_ value: String, account: String) throws {
        let base: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: account
        ]
        SecItemDelete(base as CFDictionary)
        guard !value.isEmpty else { return }
        var item = base
        item[kSecValueData as String] = Data(value.utf8)
        let status = SecItemAdd(item as CFDictionary, nil)
        guard status == errSecSuccess else {
            throw NSError(domain: NSOSStatusErrorDomain, code: Int(status))
        }
    }
}

private extension String {
    func cleanAIOutput() -> String {
        var value = replacingOccurrences(of: "\\s*<think>[\\s\\S]*?</think>\\s*", with: "",
                                         options: [.regularExpression, .caseInsensitive])
        value = value.replacingOccurrences(of: "^(最终译文|译文|中文|English)\\s*[:：]\\s*", with: "",
                                           options: [.regularExpression, .caseInsensitive])
        return value.replacingOccurrences(of: "[\\r\\n]+", with: " ", options: .regularExpression)
            .trimmingCharacters(in: CharacterSet.whitespacesAndNewlines.union(CharacterSet(charactersIn: "`\"")))
    }
}
