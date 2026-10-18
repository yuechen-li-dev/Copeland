# Shared Copeland application libraries

| Library | Owner and consumers |
| --- | --- |
| [Copeland.Editing](Copeland.Editing/README.md) | Typed drafts and explicit parsing; reused by the café editors |
| [Copeland React controls](Copeland.Browser.React/README.md) | React raw-text/form adapters and their component contract; reused by both current café editors |

Promote repeated runtime behavior here with its public contract and real consumer
qualification. Test runners belong under `tools`; sample-specific business policy
stays with the application. The compiler owns language semantics and TSPack owns
the canonical browser frame/lifecycle executor.
