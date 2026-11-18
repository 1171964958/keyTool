import SwiftUI

@MainActor
final class TaskStore: ObservableObject {
    @Published private(set) var tasks: [MacroTask] = []
    @Published var selectedTaskID: UUID?

    private let storageURL: URL?

    init(storageURL: URL? = TaskStore.defaultStorageURL()) {
        self.storageURL = storageURL
        load()
    }

    var selectedTask: MacroTask? {
        guard let selectedTaskID else { return nil }
        return tasks.first { $0.id == selectedTaskID }
    }

    func createTask() {
        let task = MacroTask.newTask()
        tasks.append(task)
        selectedTaskID = task.id
        save()
    }

    func duplicateTask(_ task: MacroTask) {
        var copy = task
        copy.id = UUID()
        copy.name = "\(task.name) 副本"
        copy.updatedAt = Date()
        copy.actions = task.actions.map(Self.regenerateIdentifiers)
        tasks.append(copy)
        selectedTaskID = copy.id
        save()
    }

    func deleteTask(_ task: MacroTask) {
        guard let index = tasks.firstIndex(where: { $0.id == task.id }) else {
            return
        }
        tasks.remove(at: index)
        if selectedTaskID == task.id {
            selectedTaskID = tasks.indices.contains(index) ? tasks[index].id : tasks.last?.id
        }
        save()
    }

    func updateTask(_ task: MacroTask) {
        guard let index = tasks.firstIndex(where: { $0.id == task.id }) else {
            return
        }
        var updated = task
        updated.updatedAt = Date()
        tasks[index] = updated
        save()
    }

    func binding(for taskID: UUID) -> Binding<MacroTask>? {
        guard tasks.contains(where: { $0.id == taskID }) else {
            return nil
        }
        return Binding(
            get: { [weak self] in
                self?.tasks.first(where: { $0.id == taskID }) ?? MacroTask.newTask()
            },
            set: { [weak self] task in
                self?.updateTask(task)
            }
        )
    }

    private func load() {
        guard
            let storageURL,
            let data = try? Data(contentsOf: storageURL),
            let storedTasks = try? JSONDecoder.keyTool.decode([MacroTask].self, from: data)
        else {
            return
        }

        tasks = storedTasks
        selectedTaskID = storedTasks.first?.id
    }

    private func save() {
        guard
            let storageURL,
            let data = try? JSONEncoder.keyTool.encode(tasks)
        else {
            return
        }

        do {
            try FileManager.default.createDirectory(
                at: storageURL.deletingLastPathComponent(),
                withIntermediateDirectories: true
            )
            try data.write(to: storageURL, options: .atomic)
        } catch {
            NSLog("KeyTool: Unable to save tasks: \(error.localizedDescription)")
        }
    }

    private static func regenerateIdentifiers(_ action: MacroAction) -> MacroAction {
        switch action {
        case .keyboard(var action):
            action.id = UUID()
            return .keyboard(action)
        case .mouseClick(var action):
            action.id = UUID()
            return .mouseClick(action)
        case .mouseMove(var action):
            action.id = UUID()
            return .mouseMove(action)
        case .scroll(var action):
            action.id = UUID()
            return .scroll(action)
        case .delay(var action):
            action.id = UUID()
            return .delay(action)
        }
    }

    nonisolated private static func defaultStorageURL() -> URL? {
        FileManager.default
            .urls(for: .applicationSupportDirectory, in: .userDomainMask)
            .first?
            .appendingPathComponent("KeyTool", isDirectory: true)
            .appendingPathComponent("tasks.json")
    }
}

private extension JSONEncoder {
    static var keyTool: JSONEncoder {
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        encoder.dateEncodingStrategy = .iso8601
        return encoder
    }
}

private extension JSONDecoder {
    static var keyTool: JSONDecoder {
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .iso8601
        return decoder
    }
}
