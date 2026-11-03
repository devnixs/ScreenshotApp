import Foundation
import Testing
@testable import Snapper

struct AnnotationSaveDefaultsTests {
    let date = Date(timeIntervalSince1970: 1793647084)
    let namer = FileNamer(timeZone: TimeZone(secondsFromGMT: 0)!)
    let base = "Screenshot 2026-11-02 at 19.18.04"
    @Test func captureDateAndCollisions() {
        let directory = URL(fileURLWithPath: "/tmp/annotation-defaults")
        for taken in 0...2 {
            var probes: [URL] = []
            let name = AnnotationSaveDefaults.suggestedFileName(captureDate: date, directory: directory, namer: namer) { url in
                probes.append(url)
                return probes.count <= taken
            }
            #expect(name == "\(base)\(taken == 0 ? "" : " (\(taken + 1))").png")
            #expect(probes.allSatisfy { $0.deletingLastPathComponent().path == directory.path })
        }
    }
    @Test func nilDirectoryNeverProbes() {
        let name = AnnotationSaveDefaults.suggestedFileName(captureDate: date, directory: nil, namer: namer) { _ in
            Issue.record("No directory must never check for collisions")
            return true
        }
        #expect(name == "\(base).png")
    }
}
