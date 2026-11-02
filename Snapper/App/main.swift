import AppKit

let app = NSApplication.shared
// NSApplication.delegate is weak; retain the delegate for the process lifetime.
let appDelegate = AppDelegate()
app.delegate = appDelegate
app.run()
