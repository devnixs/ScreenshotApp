import Foundation
import Testing
@testable import Snapper

struct ScreenshotFileWriterTests {
    @Test func createsFolderAndPreservesCollidingFiles() throws {
        let root = FileManager.default.temporaryDirectory.appending(path: UUID().uuidString)
        defer { try? FileManager.default.removeItem(at: root) }
        let folder = root.appending(path: "nested/shots")
        let date = Date(timeIntervalSince1970: 1234567890)
        let first = try ScreenshotFileWriter.save(Data([1, 2]), to: folder, captureDate: date)
        let second = try ScreenshotFileWriter.save(Data([3, 4]), to: folder, captureDate: date)
        #expect(first.lastPathComponent == FileNamer().fileName(for: .screenshot, date: date))
        #expect(second.deletingPathExtension().lastPathComponent == first.deletingPathExtension().lastPathComponent + " (2)")
        #expect(try Data(contentsOf: first) == Data([1, 2]))
        #expect(try Data(contentsOf: second) == Data([3, 4]))
    }
    @Test func folderCreationPreservesSystemDetails() throws {
        let file = FileManager.default.temporaryDirectory.appending(path: UUID().uuidString)
        defer { try? FileManager.default.removeItem(at: file) }
        try Data().write(to: file)
        let folder = file.appending(path: "shots")
        do {
            _ = try ScreenshotFileWriter.save(Data([1]), to: folder, captureDate: Date())
            Issue.record("A child folder cannot be created under a file")
        } catch {
            #expect(ScreenshotOutputPlan.saveFailureAlert(for: error) == .saveFolder)
            let folderError = try #require(error as? SaveFolderError)
            guard case .creationFailed(let url, let details) = folderError else {
                Issue.record("Expected a folder creation error with details")
                return
            }
            #expect(url == folder.standardizedFileURL)
            #expect(!details.isEmpty)
            #expect(error.localizedDescription == details)
        }
    }

    @Test func fileInPlaceOfFolderIsClassified() throws {
        let path = FileManager.default.temporaryDirectory.appending(path: UUID().uuidString)
        defer { try? FileManager.default.removeItem(at: path) }
        try Data().write(to: path)
        do {
            _ = try ScreenshotFileWriter.save(Data([1]), to: path, captureDate: Date())
            Issue.record("Saving into a file should fail")
        } catch { #expect(ScreenshotOutputPlan.saveFailureAlert(for: error) == .saveFolder) }
    }
}
