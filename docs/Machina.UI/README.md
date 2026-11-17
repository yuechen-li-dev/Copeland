# Machina.UI documentation

Machina.UI is the repository's C# UI declaration and presentation stack. Start with the [authoring guide](authoring-guide.md) for the current model and API, then follow the source and tests linked there when changing behavior.

## Learning path

See [native pointer feedback and window chrome](native-pointer-feedback.md) for
cursor intent, hover/pressed surfaces, legacy-control adoption and Windows captions.

For reusable interactive controls, start with [native controlled widgets](native-widgets.md).
It includes component coverage, host ownership, Base UI reference attribution and
the Vulkan gallery validation. Existing StandardUI drawing APIs remain supported.

1. [Authoring guide](authoring-guide.md) — choose between flat screen rows and local component trees; understand the Standard component surface, layout, and frame preparation path.
2. [Architecture index](architecture/README.md) — current ownership and contracts by subsystem.
3. [Reference contracts](reference/) — detailed renderer, component, layout, and artifact contracts.
4. [Historical milestone records](history/) — implementation evidence and decisions in milestone order. These records are not the current API index.

## Solutions and related guidance

- [Machina.UI solution](../../Machina.UI.slnx)
- [Slow diagnostic, sample, and font/tooling solution](../../Machina.UI.Slow.slnx)
- [Repository test-lane doctrine](../architecture/jtf-test-lane-doctrine.md)
- [JTF-M0 topology and ownership](../architecture/jtf-m0-topology-and-ownership.md)

The JTF-M0 naming work keeps the existing assemblies and `Machina.*` namespaces. See the topology doctrine for temporary semantic exceptions and ownership; it does not relocate renderer implementations.
