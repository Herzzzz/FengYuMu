import SwiftUI
import FengYuMuCore

struct MainView: View {
    @ObservedObject var model: AppState
    @State private var showingAISettings = false
    @State private var showingCredits = false

    var body: some View {
        VStack(spacing: 14) {
            HStack {
                VStack(alignment: .leading, spacing: 2) {
                    Text("枫语幕 v\(AppState.version)")
                        .font(.system(size: 31, weight: .bold, design: .rounded))
                    Text("冒险岛国际怀旧服 · macOS 原生版")
                        .font(.subheadline).foregroundStyle(.secondary)
                }
                Spacer()
                Circle().fill(model.capture.hasPermission ? Color.green : Color.orange)
                    .frame(width: 10, height: 10)
                Text(model.capture.hasPermission ? "屏幕权限正常" : "需要屏幕权限")
                    .font(.caption)
            }

            GroupBox {
                VStack(alignment: .leading, spacing: 11) {
                    Text(model.status).font(.headline).foregroundStyle(statusColor)
                        .frame(maxWidth: .infinity, alignment: .leading)
                    Picker("翻译范围", selection: $model.rangeMode) {
                        ForEach(TranslationRangeMode.allCases, id: \.rawValue) { mode in
                            Text(mode.title).tag(mode)
                        }
                    }.pickerStyle(.segmented)
                    Toggle("持续自动翻译（检测到游戏界面时更新）",
                           isOn: $model.continuousTranslation)
                    Text("建议日常保持关闭；需要连续查看面板时再开启。")
                        .font(.caption).foregroundStyle(.secondary)
                }.padding(4)
            }

            Button {
                model.toggleTranslation()
            } label: {
                Text(model.overlay.isVisible ? "隐藏当前翻译" : "识别当前游戏画面")
                    .frame(maxWidth: .infinity).padding(.vertical, 7)
            }
            .buttonStyle(.borderedProminent)
            .controlSize(.large)
            .disabled(model.isWorking || model.dictionaryCount == 0)

            HStack(spacing: 9) {
                Button("屏幕权限") { model.requestScreenPermission() }
                Button("AI 翻译") { model.showAIWindow?() }
                Button("联网 AI 设置") { showingAISettings = true }
                Button("鸣谢与声明") { showingCredits = true }
            }
            .buttonStyle(.bordered)

            Divider()
            HStack {
                Text(model.hotkeyStatus).font(.caption).foregroundStyle(.secondary)
                Spacer()
                Text("不读取内存 · 不注入游戏").font(.caption).foregroundStyle(.secondary)
            }
        }
        .padding(20)
        .frame(width: 560)
        .sheet(isPresented: $showingAISettings) { AISettingsView(model: model) }
        .sheet(isPresented: $showingCredits) { CreditsView() }
    }

    private var statusColor: Color {
        if model.status.contains("失败") || model.status.contains("找不到") || model.status.contains("请先") {
            return .red
        }
        return model.dictionaryCount > 0 ? .green : .primary
    }
}

private struct AISettingsView: View {
    @ObservedObject var model: AppState
    @Environment(\.dismiss) private var dismiss
    @State private var draft = AISettings()
    @State private var message = ""

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("联网 AI 设置").font(.title2.bold())
            Text("API Key 只保存在这台 Mac 的系统钥匙串中。")
                .font(.caption).foregroundStyle(.secondary)
            Picker("服务商", selection: $draft.provider) {
                ForEach(AISettings.presets, id: \.name) { preset in Text(preset.name).tag(preset.name) }
            }
            .onChange(of: draft.provider) { _, selected in
                if let preset = AISettings.presets.first(where: { $0.name == selected }) {
                    draft.endpoint = preset.endpoint; draft.model = preset.model
                }
            }
            TextField("接口地址", text: $draft.endpoint)
            TextField("模型名", text: $draft.model)
            SecureField("API Key", text: $draft.apiKey)
            if !message.isEmpty { Text(message).font(.caption).foregroundStyle(.red) }
            HStack {
                Spacer()
                Button("取消") { dismiss() }
                Button("保存") {
                    do { try model.saveAISettings(draft); dismiss() }
                    catch { message = error.localizedDescription }
                }.buttonStyle(.borderedProminent).disabled(!draft.isReady)
            }
        }
        .textFieldStyle(.roundedBorder)
        .padding(20)
        .frame(width: 520)
        .onAppear { draft = model.aiSettings }
    }
}

private struct CreditsView: View {
    @Environment(\.dismiss) private var dismiss
    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 10) {
                Text("鸣谢与声明").font(.title2.bold())
                Text("特别感谢 B站 Up 主“奇怪小鸭”及其参考网站；感谢群友“四水年华”提供专用简写翻译思路。")
                Text("翻译与资料还参考 GCW、Henesys.gg、MSCW Guidebook、MeowDB、Hidden Street 等公开资料站及其作者和维护者。")
                Text("枫语幕是玩家制作的非官方、非商业翻译辅助工具，仅免费提供给热爱《冒险岛》的冒险家，不代表 NEXON 或任何资料站的官方立场，也不暗示授权、隶属或合作关系。")
                Text("联网 AI 会把当前短句、命中术语和固定提示词发送给你自行选择的服务商，请勿输入账号、密码等敏感信息。")
                HStack { Spacer(); Button("知道了") { dismiss() } }
            }.padding(20)
        }.frame(width: 520, height: 330)
    }
}
