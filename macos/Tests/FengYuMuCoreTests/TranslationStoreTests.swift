import XCTest
@testable import FengYuMuCore

final class TranslationStoreTests: XCTestCase {
    func testNormalizationRepairsKnownClassicOCRShapes() {
        XCTAssertEqual(TranslationStore.normalize(" OUEST—Helper "), "quest helper")
        XCTAssertEqual(TranslationStore.normalize("Biggs’s Item"), "biggs's item")
    }

    func testLoadsAndMatchesDictionaryWithoutReplacingDynamicCounts() throws {
        let file = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try [
            "Pig's Head\t猪头\t怀旧服-任务物品",
            "Mrs. Ming Ming's Second Worry\t明明女士的第二个担心\t怀旧服-任务#1001",
            "Pig's Head 15 / 10\t错误整行\t怀旧服-聊天"
        ].joined(separator: "\n").write(to: file, atomically: true, encoding: .utf8)
        defer { try? FileManager.default.removeItem(at: file) }

        let store = TranslationStore()
        XCTAssertEqual(try store.load(from: file), 3)
        let matches = store.matches(in: "Pig's Head 15 / 10")
        XCTAssertEqual(matches.map(\.entry.chinese), ["猪头"])
    }

    func testAmbiguousPlainNamesAreNotPaintedWithoutDisambiguation() throws {
        let file = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try [
            "Blue Bandana\t蓝色头巾\t怀旧服-装备#1\t0123456789ABCDEF",
            "Blue Bandana\t蓝色发带\t怀旧服-装备#2\tFEDCBA9876543210"
        ].joined(separator: "\n").write(to: file, atomically: true, encoding: .utf8)
        defer { try? FileManager.default.removeItem(at: file) }
        let store = TranslationStore()
        _ = try store.load(from: file)
        XCTAssertTrue(store.matches(in: "Blue Bandana").isEmpty)
    }
}
