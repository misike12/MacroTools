# r6-dissect (vendored parser)

`r6-dissect.exe` turns the game's own `.rec` match-replay files into the JSON
snapshots this plugin maps. It is a build of the MIT-licensed
[lumina-r6/siege-dissect](https://github.com/lumina-r6/siege-dissect) project
(a maintained fork of `redraskal/r6-dissect`), whose license travels beside it
as `LICENSE-r6-dissect`. Copyright (c) 2022 Benjamin Ryan and contributors.

Build provenance: commit `ea662d31f4dc2a66576088c49ab8d3174b0b3989`,
built with Go 1.27 (`go build -o r6-dissect.exe .`) for windows-amd64.

Invocation contract the plugin relies on:

- `r6-dissect.exe <match-folder-or-rec>` prints one JSON document to stdout
  and exits 0. Logs go to stderr, never stdout.
- A corrupt or version-skewed file exits non-zero with no JSON on stdout.

When Ubisoft changes the replay format, rebuild this binary from a newer
upstream commit and re-verify with `tests/R6Md.Tests` fixtures. Nothing in
`src/` parses the binary format itself, deliberately: the format is
reverse-engineered and drifts every season, and that drift belongs in one
replaceable executable rather than spread across the plugin.
