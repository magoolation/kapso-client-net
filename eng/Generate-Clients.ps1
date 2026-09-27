#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Regenerates every Kiota client from the vendored OpenAPI documents.

.DESCRIPTION
    Running this on a clean checkout must reproduce exactly what is committed
    under src/Kapso/Generated. CI asserts that with `git diff --exit-code`, so
    everything the output depends on is pinned: the Kiota version comes from
    .config/dotnet-tools.json, and the specs come from specs/ rather than the
    network.

    Steps:
      1. split the WhatsApp document (see eng/Kapso.SpecTool for why)
      2. fail on any Kiota path-signature collision
      3. generate the seven clients

.PARAMETER SkipSplit
    Reuse the existing specs/whatsapp/*.yaml instead of regenerating them.

.EXAMPLE
    pwsh eng/Generate-Clients.ps1
#>
[CmdletBinding()]
param(
    [switch] $SkipSplit
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Join-Path $PSScriptRoot '..' | Resolve-Path | Select-Object -ExpandProperty Path
$specsDir = Join-Path $repoRoot 'specs'
$splitDir = Join-Path $specsDir 'whatsapp'
$outputRoot = Join-Path $repoRoot 'src' 'Kapso' 'Generated'
$specTool = Join-Path $repoRoot 'eng' 'Kapso.SpecTool'

function Invoke-Step {
    param([string] $Description, [scriptblock] $Action)

    Write-Host ''
    Write-Host $Description -ForegroundColor Cyan
    & $Action
    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed with exit code $LASTEXITCODE"
    }
}

# The seven clients.
#
# WhatsApp is four rather than one because Kiota collapses paths that differ only
# in a parameter name, which would silently drop six of its operations. The other
# three are separate APIs in Kapso's own documentation; Platform and Workflows
# share a base URL but are documented apart, and keeping that split makes the
# facade read the way the docs do.
$clients = @(
    @{ Spec = 'whatsapp/whatsapp-phone.yaml'; Output = 'WhatsApp/PhoneNumbers';    Class = 'WhatsAppPhoneNumbersClient';    Namespace = 'Kapso.Generated.WhatsApp.PhoneNumbers' }
    @{ Spec = 'whatsapp/whatsapp-waba.yaml';  Output = 'WhatsApp/BusinessAccounts'; Class = 'WhatsAppBusinessAccountsClient'; Namespace = 'Kapso.Generated.WhatsApp.BusinessAccounts' }
    @{ Spec = 'whatsapp/whatsapp-flow.yaml';  Output = 'WhatsApp/Flows';            Class = 'WhatsAppFlowsClient';            Namespace = 'Kapso.Generated.WhatsApp.Flows' }
    @{ Spec = 'whatsapp/whatsapp-media.yaml'; Output = 'WhatsApp/Media';            Class = 'WhatsAppMediaClient';            Namespace = 'Kapso.Generated.WhatsApp.Media' }
    @{ Spec = 'openapi-platform.yaml';        Output = 'Platform';                  Class = 'PlatformClient';                 Namespace = 'Kapso.Generated.Platform' }
    @{ Spec = 'openapi-workflows.yaml';       Output = 'Workflows';                 Class = 'WorkflowsClient';                Namespace = 'Kapso.Generated.Workflows' }
    @{ Spec = 'openapi-kapso-agent.yaml';     Output = 'Agent';                     Class = 'AgentClient';                    Namespace = 'Kapso.Generated.Agent' }
)

Push-Location $repoRoot
try {
    Invoke-Step 'Restoring pinned tools' { dotnet tool restore }

    Invoke-Step 'Building the spec tool' { dotnet build $specTool -v q --nologo }

    if (-not $SkipSplit) {
        Invoke-Step 'Splitting the WhatsApp document' {
            dotnet run --project $specTool --no-build -- split (Join-Path $specsDir 'openapi-whatsapp.yaml') $splitDir
        }
    }

    Invoke-Step 'Checking every document Kiota will read' {
        $toCheck = @(
            Get-ChildItem $splitDir -Filter '*.yaml' | Select-Object -ExpandProperty FullName
            Join-Path $specsDir 'openapi-platform.yaml'
            Join-Path $specsDir 'openapi-workflows.yaml'
            Join-Path $specsDir 'openapi-kapso-agent.yaml'
        )
        dotnet run --project $specTool --no-build -- check @toCheck
    }

    Write-Host ''
    Write-Host 'Generating clients' -ForegroundColor Cyan

    foreach ($client in $clients) {
        $spec = Join-Path $specsDir $client.Spec
        $output = Join-Path $outputRoot $client.Output

        Write-Host "  $($client.Class)" -NoNewline

        # --ebc drops the obsolete overloads Kiota keeps for clients that predate
        # its current shape. This client has no such history.
        $log = dotnet kiota generate `
            --language CSharp `
            --openapi $spec `
            --output $output `
            --class-name $client.Class `
            --namespace-name $client.Namespace `
            --exclude-backward-compatible `
            --clean-output `
            --log-level Error 2>&1

        if ($LASTEXITCODE -ne 0) {
            Write-Host ''
            $log | Write-Host
            throw "kiota failed for $($client.Class)"
        }

        $errors = $log | Where-Object { $_ -match '^\s*(fail|error)' }
        if ($errors) {
            Write-Host '  FAILED' -ForegroundColor Red
            $errors | Write-Host
            throw "kiota reported errors for $($client.Class)"
        }

        $fileCount = (Get-ChildItem $output -Recurse -Filter '*.cs').Count
        Write-Host "  $fileCount files" -ForegroundColor DarkGray
    }

    Write-Host ''
    Write-Host 'Done. Review the diff under src/Kapso/Generated before committing.' -ForegroundColor Green
}
finally {
    Pop-Location
}
