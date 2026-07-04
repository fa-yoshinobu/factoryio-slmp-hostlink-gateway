# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## Unreleased

### Added
- Added the `Auto reconnect PLC` setting. When enabled, PLC disconnects retry with 1-second exponential backoff up to 30 seconds while the Modbus TCP server stays running.
- Added unit and smoke coverage for PLC auto reconnect settings compatibility and UI state.

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
