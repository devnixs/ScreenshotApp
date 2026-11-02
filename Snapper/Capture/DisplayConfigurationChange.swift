enum DisplayConfigurationChange {
    static func requiresCancel(old: [DisplayInfo], new: [DisplayInfo]) -> Bool {
        Set(old) != Set(new)
    }
}
