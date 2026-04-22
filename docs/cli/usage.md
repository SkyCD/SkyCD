# SkyCD CLI Usage

SkyCD can run in headless CLI mode when command-line switches are provided.
Running with no CLI arguments keeps desktop startup unchanged.

## Commands

```text
skycd open <file> [--format <id>] [--json]
skycd convert --in <file> --out <file> [--in-format <id>] [--format <id>] [--json]
skycd list-formats [--json]
skycd plugins list [--json]
skycd --help
skycd --version
```

## Output Conventions

- Human-readable output is written to stdout by default.
- Machine-readable output can be requested with `--json`.
- Errors are written to stderr and return non-zero exit codes.

## Exit Codes

- `0`: success
- `2`: invalid arguments
- `3`: command execution failure
- `4`: plugin/CLI configuration error (e.g., command collision)
- `130`: cancelled (Ctrl+C)

## Plugin CLI Contributions

Plugins can implement `ICliPluginCapability` to:

- add new commands (`CliContributionType.Command`)
- extend host command pipelines (`CliContributionType.Extension`)

Current extension points:

- `open`
- `convert`

Collision rules:

- Built-in command names are reserved.
- Duplicate plugin command paths are rejected deterministically with a clear error message.
- Extension contributions must target an existing extension point.
