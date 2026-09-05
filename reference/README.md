# Reference Source

Copeland no longer embeds a Dominatus checkout under `reference/`.

Active source-level integrations resolve the standalone sibling repository at
`../Dominatus`, matching the existing workspace model used for Deliverance and
InputMan. Package-only consumers continue to use the centrally pinned Dominatus
packages.

Historical reports and artifacts may mention `reference/dominatus`; those paths
describe the repository layout at the time the evidence was recorded and are not
active build dependencies.
