import AppKit

enum RunningApplicationCatalog {
    static func current() -> [TargetApplication] {
        let ownProcessID = ProcessInfo.processInfo.processIdentifier
        return NSWorkspace.shared.runningApplications
            .filter {
                !$0.isTerminated
                    && $0.activationPolicy == .regular
                    && $0.processIdentifier != ownProcessID
            }
            .compactMap { application in
                guard let name = application.localizedName else { return nil }
                return TargetApplication(
                    name: name,
                    bundleIdentifier: application.bundleIdentifier,
                    processID: application.processIdentifier
                )
            }
            .sorted {
                $0.name.localizedStandardCompare($1.name) == .orderedAscending
            }
    }

    static func frontmost(excluding processID: pid_t? = nil) -> TargetApplication? {
        guard
            let application = NSWorkspace.shared.frontmostApplication,
            application.processIdentifier != processID,
            let name = application.localizedName
        else {
            return nil
        }
        return TargetApplication(
            name: name,
            bundleIdentifier: application.bundleIdentifier,
            processID: application.processIdentifier
        )
    }
}
