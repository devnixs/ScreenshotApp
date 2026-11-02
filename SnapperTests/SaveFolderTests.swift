import Darwin
import Foundation
import Testing
@testable import Snapper

struct SaveFolderTests {
    private func withRoot(_ body: (URL) throws -> Void) throws {
        let root = FileManager.default.temporaryDirectory.appending(path: UUID().uuidString, directoryHint: .isDirectory)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: root) }
        try body(root)
    }

    private let date = Date(timeIntervalSince1970: 1_793_640_000)
    private let namer = FileNamer(timeZone: TimeZone(identifier: "UTC")!)

    @Test func defaultURLIsDocumentsScreenshots() {
        #expect(SaveFolder.defaultURL.lastPathComponent == "Screenshots")
        #expect(SaveFolder.defaultURL.deletingLastPathComponent().standardizedFileURL.path ==
                URL.documentsDirectory.standardizedFileURL.path)
    }

    @Test func createsMissingNestedFolder() throws {
        try withRoot { root in
            let folder = root.appending(path: "a/b/Screenshots")
            let prepared = try SaveFolder(url: folder).prepare()
            #expect(prepared.path == folder.standardizedFileURL.path)
            var isDirectory: ObjCBool = false
            #expect(FileManager.default.fileExists(atPath: folder.path, isDirectory: &isDirectory))
            #expect(isDirectory.boolValue)
        }
    }

    @Test func existingFolderIsOK() throws {
        try withRoot { root in
            let sentinel = root.appending(path: "sentinel")
            let contents = Data("keep me".utf8)
            try contents.write(to: sentinel)
            let first = try SaveFolder(url: root).prepare()
            let second = try SaveFolder(url: root).prepare()
            #expect(first == root.standardizedFileURL)
            #expect(second == root.standardizedFileURL)
            #expect(try Data(contentsOf: sentinel) == contents)
        }
    }

    @Test func pathIsAFileThrowsNotADirectory() throws {
        try withRoot { root in
            let file = root.appending(path: "Screenshots")
            try Data().write(to: file)
            #expect(throws: SaveFolderError.notADirectory(file)) { try SaveFolder(url: file).prepare() }
        }
    }

    @Test(.enabled(if: geteuid() != 0, "Root bypasses POSIX write permissions"))
    func readOnlyFolderThrowsNotWritable() throws {
        try withRoot { root in
            try FileManager.default.setAttributes([.posixPermissions: 0o555], ofItemAtPath: root.path)
            defer { try? FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: root.path) }
            #expect(throws: SaveFolderError.notWritable(root)) { try SaveFolder(url: root).prepare() }
        }
    }

    @Test(.enabled(if: geteuid() != 0, "Root bypasses POSIX write permissions"))
    func readOnlyParentThrowsCannotCreate() throws {
        try withRoot { root in
            try FileManager.default.setAttributes([.posixPermissions: 0o555], ofItemAtPath: root.path)
            defer { try? FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: root.path) }
            let folder = root.appending(path: "Screenshots")
            #expect(throws: SaveFolderError.cannotCreate(folder)) { try SaveFolder(url: folder).prepare() }
        }
    }

    @Test func uniqueFileURLInEmptyFolder() throws {
        try withRoot { root in
            let folder = root.appending(path: "Screenshots")
            let result = try SaveFolder(url: folder).uniqueFileURL(for: .screenshot, date: date, namer: namer)
            #expect(result.path == folder.appending(path: namer.fileName(for: .screenshot, date: date)).path)
            #expect(!FileManager.default.fileExists(atPath: result.path))
            #expect(FileManager.default.fileExists(atPath: folder.path))
        }
    }

    @Test func uniqueFileURLSkipsExistingFiles() throws {
        try withRoot { root in
            let base = namer.baseName(for: .screenshot, date: date)
            try Data().write(to: root.appending(path: "\(base).png"))
            try Data().write(to: root.appending(path: "\(base) (2).png"))
            let result = try SaveFolder(url: root).uniqueFileURL(for: .screenshot, date: date, namer: namer)
            #expect(result.lastPathComponent == "\(base) (3).png")
            #expect(!FileManager.default.fileExists(atPath: result.path))
        }
    }

    @Test func uniqueFileURLPropagatesFolderErrors() throws {
        try withRoot { root in
            let file = root.appending(path: "Screenshots")
            try Data().write(to: file)
            #expect(throws: SaveFolderError.notADirectory(file)) {
                try SaveFolder(url: file).uniqueFileURL(for: .screenshot, date: date, namer: namer)
            }
        }
    }

    @Test func errorExposesFolderURL() {
        let folder = URL(filePath: "/tmp/Screenshots")
        for error in [SaveFolderError.cannotCreate(folder), .notWritable(folder), .notADirectory(folder)] {
            #expect(error.folderURL == folder)
        }
    }

    @Test func standardizesFolderURL() throws {
        try withRoot { root in
            let folder = root.appending(path: "a/../Screenshots/")
            let prepared = try SaveFolder(url: folder).prepare()
            #expect(prepared.path == root.appending(path: "Screenshots").path)
        }
    }

    @Test func directorySymlinkIsAccepted() throws {
        try withRoot { root in
            let link = root.appending(path: "alias")
            try FileManager.default.createSymbolicLink(at: link, withDestinationURL: root)
            let prepared = try SaveFolder(url: link).prepare()
            #expect(prepared.path == link.path)
        }
    }

    @Test func danglingSymlinkIsACollision() throws {
        try withRoot { root in
            let plainName = namer.fileName(for: .screenshot, date: date)
            try FileManager.default.createSymbolicLink(
                at: root.appending(path: plainName), withDestinationURL: root.appending(path: "missing")
            )
            let result = try SaveFolder(url: root).uniqueFileURL(for: .screenshot, date: date, namer: namer)
            #expect(result.lastPathComponent == "\(namer.baseName(for: .screenshot, date: date)) (2).png")
        }
    }
}
