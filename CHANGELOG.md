# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## Unreleased

### Added
- Added the `Auto reconnect PLC` setting. When enabled, PLC disconnects retry with 1-second exponential backoff up to 30 seconds while the Modbus TCP server stays running.
- Added unit and smoke coverage for PLC auto reconnect settings compatibility and UI state.
- Added a confirmation prompt before generating very large Modbus mapping ranges when any max address exceeds 4096.

### Changed
- Rotated `gateway.log` and `error.log` at 10 MB so long-running sessions do not grow log files without bound.
- Switched PLC communication references to local `PlcComm.Slmp` and `PlcComm.KvHostLink` project sources for workspace builds.
- Updated SLMP and KV Host Link profile selectors to use canonical communication-library `display_name` labels while preserving saved canonical profile IDs.
- Updated the SLMP PLC profile selector with Ethernet unit profiles for iQ-R/RJ71EN71, LCPU/LJ71E71-100, QnU/QJ71E71-100, QnUDV/QJ71E71-100, and QCPU/QJ71E71-100.
- Updated the KV Host Link PLC profile selector labels with local profile families for KV-5500, KV-7300, KV-7500, KV-8000A, and KV-X310/X520/X530/X550 models.
- Removed base-only `melsec:qcpu` from the selectable SLMP connection profile list.
- Updated GitHub Actions checkout behavior so local `PlcComm` source project references resolve during CI and release builds.

## [1.0.1] - 2026-06-29

### Changed
- Updated PLC communication package references to `PlcComm.Slmp` and `PlcComm.KvHostLink` `1.1.1`.
- Updated Host Link bit read/write handling for the latest `PlcComm.KvHostLink` API.
- Kept existing PLC address entries such as `X4`, `Y0`, and `D0` compatible while adapting the internal communication calls to the updated libraries.
- Updated README mapping notes to keep the user-facing PLC address format focused on the existing UI behavior.

## [1.0.0] - 2026-06-24

### Changed
- Updated PLC communication package references to `PlcComm.Slmp` and `PlcComm.KvHostLink` `1.0.0`.
- Added top-level changelog and TODO tracking files for release maintenance.
- Updated README library versions and documentation links.
