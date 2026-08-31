# Graph B task

Question: which operations does `RealizeNativeDiagramCard` coordinate directly, and which concerns stay outside that orchestration boundary?

The graph should make the direct fan-out, repeated cache validation, and separation between semantic projection, layout, SVG emission, provenance, and Canvas hosting visible in one half-height slot. Code search finds each name, but does not show the bounded direct ownership set or repeated call count as one stable artifact.
