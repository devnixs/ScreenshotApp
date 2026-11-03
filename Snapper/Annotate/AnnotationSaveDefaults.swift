import Foundation

enum AnnotationSaveDefaults {
    static func suggestedFileName(captureDate: Date, directory: URL?,
                                  namer: FileNamer = FileNamer(), fileExists: (URL) -> Bool) -> String {
        guard let directory else { return namer.fileName(for: .screenshot, date: captureDate) }
        return namer.uniqueFileName(for: .screenshot, date: captureDate) {
            fileExists(directory.appendingPathComponent($0))
        }
    }
}
