import Carbon
import Foundation

final class GlobalHotKeyManager {
    typealias Handler = () -> Void
    private var eventHandler: EventHandlerRef?
    private var refs: [EventHotKeyRef?] = []
    private var handlers: [UInt32: Handler] = [:]
    private let signature: OSType = 0x46594D55 // FYMU

    init?() {
        var spec = EventTypeSpec(eventClass: OSType(kEventClassKeyboard),
                                 eventKind: UInt32(kEventHotKeyPressed))
        let status = InstallEventHandler(
            GetApplicationEventTarget(),
            { _, event, pointer in
                guard let event, let pointer else { return noErr }
                let manager = Unmanaged<GlobalHotKeyManager>.fromOpaque(pointer).takeUnretainedValue()
                var identifier = EventHotKeyID()
                let result = GetEventParameter(
                    event,
                    EventParamName(kEventParamDirectObject),
                    EventParamType(typeEventHotKeyID),
                    nil,
                    MemoryLayout<EventHotKeyID>.size,
                    nil,
                    &identifier
                )
                if result == noErr, let action = manager.handlers[identifier.id] {
                    DispatchQueue.main.async(execute: action)
                }
                return noErr
            },
            1,
            &spec,
            Unmanaged.passUnretained(self).toOpaque(),
            &eventHandler
        )
        guard status == noErr else { return nil }
    }

    deinit {
        refs.forEach { if let ref = $0 { UnregisterEventHotKey(ref) } }
        if let eventHandler { RemoveEventHandler(eventHandler) }
    }

    @discardableResult
    func register(id: UInt32, keyCode: UInt32, modifiers: UInt32 = 0,
                  handler: @escaping Handler) -> Bool {
        let identifier = EventHotKeyID(signature: signature, id: id)
        var ref: EventHotKeyRef?
        let status = RegisterEventHotKey(keyCode, modifiers, identifier,
                                         GetApplicationEventTarget(), 0, &ref)
        guard status == noErr else { return false }
        handlers[id] = handler
        refs.append(ref)
        return true
    }
}
