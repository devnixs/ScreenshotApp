import Foundation

enum ScreenshotFileWriter {
    /// Keeps the capture date and bytes unchanged across naming collisions and folder retries.
    static func save(_ png: Data, to folder: URL, captureDate: Date) throws -> URL {
        let folder = try SaveFolder(url: folder).prepareForWriting()
        for attempt in 0...1 {
            let name = FileNamer().uniqueFileName(for: .screenshot, date: captureDate) {
                (try? FileManager.default.attributesOfItem(atPath: folder.appending(path: $0).path)) != nil
            }
            let url = folder.appending(path: name)
            do {
                try png.write(to: url, options: [.withoutOverwriting])
                return url
            } catch {
                let cocoa = error as NSError
                guard attempt == 0, cocoa.domain == NSCocoaErrorDomain,
                      cocoa.code == CocoaError.fileWriteFileExists.rawValue else { throw error }
            }
        }
        // Both attempts either return or throw above.
        throw CocoaError(.fileWriteFileExists)
    }
}
