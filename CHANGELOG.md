# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Runnable samples: a webhook receiver that verifies itself with no Kapso
  account, a read-only tour of both APIs, and a message sender.
- Failed responses are logged with the body the server sent. Kiota keeps only the
  status code when a body matches no error schema in the OpenAPI description, and
  Kapso's errors frequently do not, which left a refused call with nothing to act
  on. Pass an `ILoggerFactory` to the constructor, or use `AddKapso` and get the
  application's logging.
- `eng/Test-NoSecrets.ps1` fails the build if anything secret-shaped is committed.

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
