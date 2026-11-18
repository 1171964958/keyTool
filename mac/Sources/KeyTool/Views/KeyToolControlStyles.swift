import SwiftUI

struct KeyToolIconButtonStyle: ButtonStyle {
    var tint: Color = .secondary
    var size: CGFloat = 30

    func makeBody(configuration: Configuration) -> some View {
        KeyToolIconButtonBody(
            configuration: configuration,
            tint: tint,
            size: size
        )
    }

    private struct KeyToolIconButtonBody: View {
        let configuration: Configuration
        let tint: Color
        let size: CGFloat

        @Environment(\.isEnabled) private var isEnabled
        @Environment(\.accessibilityReduceMotion) private var reduceMotion

        var body: some View {
            configuration.label
                .font(.system(size: 12, weight: .semibold))
                .foregroundStyle(tint)
                .frame(width: size, height: size)
                .background(fill, in: RoundedRectangle(cornerRadius: 8))
                .overlay {
                    RoundedRectangle(cornerRadius: 8)
                        .stroke(border, lineWidth: 1)
                }
                .contentShape(RoundedRectangle(cornerRadius: 8))
                .opacity(isEnabled ? 1 : 0.36)
                .scaleEffect(
                    configuration.isPressed && !reduceMotion ? 0.96 : 1
                )
                .animation(
                    reduceMotion ? nil : .easeOut(duration: 0.12),
                    value: configuration.isPressed
                )
        }

        private var fill: Color {
            if !isEnabled {
                return Color.primary.opacity(0.025)
            }
            if configuration.isPressed {
                return tint.opacity(0.16)
            }
            return tint.opacity(0.065)
        }

        private var border: Color {
            guard isEnabled else {
                return Color.primary.opacity(0.05)
            }
            return tint.opacity(0.12)
        }
    }
}

extension View {
    func keyToolInsetPanel(padding: CGFloat = 14) -> some View {
        self
            .padding(padding)
            .background(
                Color.primary.opacity(0.025),
                in: RoundedRectangle(cornerRadius: 12)
            )
            .overlay {
                RoundedRectangle(cornerRadius: 12)
                    .stroke(Color.primary.opacity(0.07))
            }
    }
}
