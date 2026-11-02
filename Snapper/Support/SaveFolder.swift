import Foundation
import os

enum SaveFolderError: Error, Equatable, Sendable {
    case cannotCreate(URL)
    case notWritable(URL)
    case notADirectory(URL)

    var folderURL: URL {
        switch self {
        case .cannotCreate(let url), .notWritable(let url), .notADirectory(let url): url
        }
    }
}

struct SaveFolder: Sendable {
    private static let logger = Logger(subsystem: "com.thera-engineering.snapper", category: "SaveFolder")

    static var defaultURL: URL {
        URL.documentsDirectory.appending(path: "Screenshots", directoryHint: .isDirectory)
    }

    let url: URL

    init(url: URL) {
        self.url = url.standardizedFileURL
    }

    /// Creates a missing folder on use and checks that it is a writable directory.
    @discardableResult
    func prepare() throws(SaveFolderError) -> URL {
        let fileManager = FileManager.default
        var isDirectory: ObjCBool = false
        if fileManager.fileExists(atPath: url.path, isDirectory: &isDirectory) {
            guard isDirectory.boolValue else {
                throw .notADirectory(url)
            }
        } else {
            do {
                try fileManager.createDirectory(at: url, withIntermediateDirectories: true)
            } catch {
                Self.logger.error("Couldn't create save folder: \(error.localizedDescription, privacy: .public)")
                throw .cannotCreate(url)
            }
        }
        guard fileManager.isWritableFile(atPath: url.path) else {
            throw .notWritable(url)
        }
        return url
    }

    /// Returns an available URL without reserving it. Writers must handle the check-then-write race.
    /// Pass the capture time for screenshots and the recording start time for recordings.
    func uniqueFileURL(
        for kind: CaptureKind,
        date: Date,
        namer: FileNamer = FileNamer()
    ) throws(SaveFolderError) -> URL {
        let folder = try prepare()
        let name = namer.uniqueFileName(for: kind, date: date) { name in
            // attributesOfItem sees the final symlink itself, including dangling symlinks.
            (try? FileManager.default.attributesOfItem(atPath: folder.appending(path: name).path)) != nil
        }
        return folder.appending(path: name, directoryHint: .notDirectory)
    }
}
