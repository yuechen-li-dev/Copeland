# Adapter overview

Machina adapters ask you to learn one layout model: Machina records. The framework adapter only asks for components in that framework.

Layout geometry stays in `LayoutRow[]` + resolved rectangles from core APIs. Adapter differences are host-renderer details (DOM versus React Native style primitives), not separate layout dialects.

## Adapter + text renderer matrix

| Target | Layout adapter | Text renderer | Required peer(s) | Notes |
| --- | --- | --- | --- | --- |
| React DOM | `machinalayout/react` | `machinalayout/text/react` | `react`, `react-dom` | Supports DOM containment/content-visibility and DOM wrapper hooks (`className`/`style`, attrs). |
| React Native | `machinalayout/react-native` | `machinalayout/text/react-native` | `react`, `react-native` | Uses numeric RN styles and RN primitives (`View`/`Text`); no DOM containment/content-visibility. |
| Vue DOM | `machinalayout/vue` | `machinalayout/text/vue` | `vue` | Adapter uses `h()` internally; users keep normal Vue components/templates as payloads. |

## Import guidance

- Subpath imports are preferred for adapters/renderers: `machinalayout/react`, `machinalayout/react-native`, `machinalayout/vue`, `machinalayout/text`, `machinalayout/text/react`, `machinalayout/text/react-native`, and `machinalayout/text/vue`.
- Root imports remain valid during `0.x` compatibility windows.
- Framework peers are adapter-specific: install only peers needed by the subpaths you use.
