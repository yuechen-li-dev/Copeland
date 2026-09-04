# Machina.Typography.OpenFont

This package is a pinned, source-built distribution of LayoutFarm's
`Typography.OpenFont` library. It exists because the previously available unofficial
NuGet distribution was built from an obsolete source snapshot that aliases every
empty TrueType outline to glyph zero. A caller using the returned glyph identity to
look up metrics can therefore receive the `.notdef` advance for spaces.

The public namespaces remain `Typography.OpenFont` for source compatibility. The
assembly and NuGet package are named `Machina.Typography.OpenFont` to distinguish this
maintained distribution from upstream and from unrelated unofficial packages.

See `UPSTREAM.md` for the exact source revision and `LICENSE.md` for the original
project licensing and component provenance.

The fork keeps changes deliberately reviewable. Behavior corrections and safe
refactors are recorded in `PATCHES.md` and covered by package-level tests.
