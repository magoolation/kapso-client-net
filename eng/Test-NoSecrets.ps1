#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Fails if anything secret-shaped is tracked in the repository.

.DESCRIPTION
    The samples need an API key, a webhook secret and a phone number, and the one
    rule is that none of them ever lands here. GitHub's push protection catches
    credentials from providers it recognises, but Kapso is not one of them, so
    this checks the cases that actually apply to this repository:

      1. files that exist to hold secrets (.env, secrets.json, *.local.json)
      2. private keys
      3. a secret-looking setting assigned a value that is not a placeholder

    Only tracked files are scanned. Anything git ignores cannot be committed.

    Runs in CI on every pull request, and locally before a commit.

.EXAMPLE
    pwsh eng/Test-NoSecrets.ps1
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Join-Path $PSScriptRoot '..' | Resolve-Path | Select-Object -ExpandProperty Path
Push-Location $repoRoot

$findings = [System.Collections.Generic.List[string]]::new()

try {
    # @() so a single result is still an array under StrictMode.
    $tracked = @(git ls-files)

    # ── 1. Files whose whole purpose is holding secrets ──────────────────────
    $forbidden = @('*.env', '.env.*', 'secrets.json', '*.local.json', '*.pfx', '*.p12', 'id_rsa', '*.pem')
    foreach ($file in $tracked) {
        $name = Split-Path $file -Leaf
        foreach ($pattern in $forbidden) {
            if ($name -like $pattern) {
                $findings.Add("$file : a file of this kind should never be tracked ($pattern)")
            }
        }
    }

    # Generated Kiota output is large and machine-written; scanning it wastes time
    # and it cannot contain a credential by construction.
    $scannable = @($tracked | Where-Object {
        $_ -notmatch '^src/Kapso/Generated/' -and
        $_ -notmatch '^specs/' -and
        $_ -ne 'eng/Test-NoSecrets.ps1'
    })

    # ── 2. Private keys ──────────────────────────────────────────────────────
    foreach ($file in $scannable) {
        if (-not (Test-Path $file -PathType Leaf)) { continue }
        $content = Get-Content $file -Raw -ErrorAction SilentlyContinue
        if ($null -eq $content) { continue }

        if ($content -match '-----BEGIN [A-Z ]*PRIVATE KEY-----') {
            $findings.Add("$file : contains a private key")
        }
    }

    # ── 3. A secret-looking setting with a real-looking value ────────────────
    # Matches ApiKey / WebhookSecret / Token / Password / Recipient assigned a
    # quoted literal, in code or in JSON.
    # The "? before the separator matters: in JSON the key itself is quoted, so
    # the text reads "ApiKey": "value" rather than ApiKey = "value". Without it
    # the scanner reads code and misses configuration files, which is where a key
    # is far more likely to be pasted by accident.
    $assignment = '(?i)\b(api[_-]?key|webhook[_-]?secret|secret[_-]?key|access[_-]?token|bearer[_-]?token|password|recipient)\b"?\s*(?:[:=]|=>)\s*"([^"]*)"'

    # Values that are obviously stand-ins rather than credentials.
    $placeholder = '(?i)^\s*$|test|fake|example|placeholder|dummy|sample|not[-_]a[-_]real|your[-_]|<.*>|\{\{|\$\(|\$\{|^\*+$|^x+$|selftest'

    foreach ($file in $scannable) {
        if (-not (Test-Path $file -PathType Leaf)) { continue }
        $lines = @(Get-Content $file -ErrorAction SilentlyContinue)
        if ($lines.Count -eq 0) { continue }

        for ($i = 0; $i -lt $lines.Count; $i++) {
            foreach ($match in [regex]::Matches($lines[$i], $assignment)) {
                $value = $match.Groups[2].Value

                # A short value cannot be a useful credential, and placeholders are
                # what a sample is supposed to contain.
                if ($value.Length -lt 8 -or $value -match $placeholder) { continue }

                $findings.Add("$file`:$($i + 1) : $($match.Groups[1].Value) assigned a literal that does not look like a placeholder")
            }
        }
    }
}
finally {
    Pop-Location
}

if ($findings.Count -gt 0) {
    Write-Host ''
    Write-Host 'Possible secrets tracked in the repository:' -ForegroundColor Red
    foreach ($finding in $findings) { Write-Host "  $finding" -ForegroundColor Red }
    Write-Host ''
    Write-Host 'Secrets belong in user secrets or the environment, never in a tracked file:'
    Write-Host '  dotnet user-secrets set "Kapso:ApiKey" "<value>" --project samples/Kapso.Samples.Quickstart'
    exit 1
}

Write-Host "No secrets found in $((git ls-files | Measure-Object).Count) tracked files." -ForegroundColor Green
exit 0
