# Stack mutation transaction boundary

M19k treats a Page as an ordered stack of Cards. A push captures external Markdown into vault-owned content, writes typed Card metadata, appends the Card ID, validates the complete candidate vault, and then commits the bounded file set. A pop validates the candidate without the top Card before removing canonical metadata and uniquely owned content.

The practical question for dogfood is whether top-only mutation is sufficient for ordinary bounded notebook authoring without adding generic Card CRUD.

This sentence was added after the push to prove that the imported vault copy is independent from its external source.
