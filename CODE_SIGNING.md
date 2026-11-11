# Code signing policy

Windows binaries of Device Check Agent (installer, `DeviceCheck.Agent.exe`, `DeviceCheck.Agent.Cli.exe`) are
built from this repository by GitHub Actions only ([`.github/workflows/release.yml`](.github/workflows/release.yml)),
from a tagged commit of the `main` branch. Nothing is built or signed on a developer machine.

Free code signing is planned via [SignPath.io](https://about.signpath.io/), certificate by
[SignPath Foundation](https://signpath.org/). Until it is in place, the binaries are unsigned; every release publishes
SHA-256 checksums (`SHA256SUMS.txt`) next to the files on GitHub Releases and on https://device-check.umolab.ru/agent.

## Team roles

| Role | Members |
|---|---|
| Committers and reviewers | [@BlkDem](https://github.com/BlkDem) |
| Approvers (release signing) | [@BlkDem](https://github.com/BlkDem) |

Every signing request is approved manually by an approver for a release tag built by the release workflow.

## Privacy policy

This program will not transfer any information to other networked systems unless specifically requested by the user
or the person installing or operating it.

Details: the agent reads hardware information locally (read-only) and shows it in a dashboard served on
`127.0.0.1` only. It has no telemetry, does not collect serial numbers, the computer name or MAC addresses, and never
uploads reports. See https://device-check.umolab.ru/privacy.
