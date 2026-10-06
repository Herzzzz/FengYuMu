import AppKit
import SwiftUI

@MainActor
final class AIWindowModel: ObservableObject {
    @Published var input = ""
    @Published var result = ""
    @Published var isTranslating = false
    @Published var errorMessage = ""
    let app: AppState

    init(app: AppState) { self.app = app }

    func translateAndCopy() {
        let source = input.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !source.isEmpty, !isTranslating else { return }
        isTranslating = true
        errorMessage = ""
        Task {
            defer { isTranslating = false }
            do {
                let translated = try await app.aiClient.translate(
                    source: source, targetEnglish: true,
                    glossary: app.store.glossary(for: source), settings: app.aiSettings
                )
                result = translated
                NSPasteboard.general.clearContents()
                NSPasteboard.general.setString(translated, forType: .string)
            } catch { errorMessage = error.localizedDescription }
        }
    }
}

struct AIWindowView: View {
    @ObservedObject var app: AppState
    @ObservedObject var model: AIWindowModel

    var body: some View {
        VStack(spacing: 0) {
            HStack {
                VStack(alignment: .leading, spacing: 1) {
                    Text("AI 实时聊天翻译").font(.headline).foregroundStyle(.white)
                    Text(app.aiMonitorRunning ? "正在识别游戏聊天" : "实时识别未开启")
                        .font(.caption).foregroundStyle(.white.opacity(0.8))
                }
                Spacer()
                Button(app.aiMonitorRunning ? "停止" : "开始") {
                    app.setChatMonitoring(!app.aiMonitorRunning)
                }.buttonStyle(.bordered).tint(.white)
            }.padding(12).background(Color(red: 0.10, green: 0.17, blue: 0.24))

            ScrollViewReader { proxy in
                ScrollView {
                    LazyVStack(alignment: .leading, spacing: 9) {
                        if app.chatTranslations.isEmpty {
                            Text("点击“开始”后切回游戏。玩家聊天出现变化时，译文会显示在这里。")
                                .foregroundStyle(.secondary).padding(.top, 18)
                        }
                        ForEach(app.chatTranslations) { item in
                            VStack(alignment: .leading, spacing: 3) {
                                Text(item.source).font(.caption).foregroundStyle(.secondary)
                                Text(item.translation).font(.body.weight(.medium))
                            }
                            .frame(maxWidth: .infinity, alignment: .leading)
                            .padding(8).background(Color.white.opacity(0.06)).clipShape(RoundedRectangle(cornerRadius: 5))
                            .id(item.id)
                        }
                    }.padding(12)
                }
                .onChange(of: app.chatTranslations.count) { _ in
                    if let id = app.chatTranslations.last?.id { withAnimation { proxy.scrollTo(id, anchor: .bottom) } }
                }
            }

            if !model.result.isEmpty {
                Text("已复制：\(model.result)")
                    .font(.caption).foregroundStyle(.green)
                    .frame(maxWidth: .infinity, alignment: .leading).padding(.horizontal, 12)
            }
            if !model.errorMessage.isEmpty {
                Text(model.errorMessage).font(.caption).foregroundStyle(.red)
                    .frame(maxWidth: .infinity, alignment: .leading).padding(.horizontal, 12)
            }
            HStack(alignment: .bottom, spacing: 8) {
                TextField("输入中文，回车翻译为英文并复制", text: $model.input, axis: .vertical)
                    .textFieldStyle(.roundedBorder)
                    .onSubmit { model.translateAndCopy() }
                Button(model.isTranslating ? "翻译中…" : "翻译并复制") { model.translateAndCopy() }
                    .disabled(model.input.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty || model.isTranslating)
            }.padding(12)
        }
        .frame(minWidth: 500, minHeight: 360)
        .background(Color(red: 0.08, green: 0.11, blue: 0.14).opacity(0.96))
        .preferredColorScheme(.dark)
    }
}
