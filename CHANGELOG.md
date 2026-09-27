# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.1.0] - 2026-09-27

### Added

- Typed clients for the WhatsApp, Platform, Workflows and Agent APIs, generated
  with Kiota from Kapso's official OpenAPI descriptions.
- `KapsoClient` facade, usable directly or through `services.AddKapso(...)`.
- Resilience that distinguishes a refused request from an ambiguous one: `429`
  is retried for every method, `5xx` and dropped connections only for idempotent
  ones, so a retry cannot send a WhatsApp message twice.
- Rate limit state from the latest response on `KapsoClient.RateLimit`.
- `EnumerateAsync` pagination over both of Kapso's paging schemes.
- Webhook signature verification and typed payloads for the documented events.
- Native AOT and trimming support, verified by publishing a sample in CI.

[Unreleased]: https://github.com/magoolation/kapso-client-net/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/magoolation/kapso-client-net/releases/tag/v0.1.0
