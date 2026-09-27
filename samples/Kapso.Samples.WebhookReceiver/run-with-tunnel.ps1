#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Runs the webhook receiver behind a public Cloudflare tunnel.

.DESCRIPTION
    Kapso delivers webhooks over HTTPS to a public address, so a receiver on
    localhost needs a tunnel in front of it. This starts one, prints the URL to
    register in Kapso, and runs the receiver until you stop it with Ctrl+C.

    Requires cloudflared:

        winget install Cloudflare.cloudflared

    Quick tunnels need no Cloudflare account, and the URL is different every run
    — so the webhook has to be re-pointed each time you restart this. That is the
    cost of not owning a domain; a named tunnel keeps a stable hostname.

.PARAMETER Port
    Local port for the receiver. Default 5099.

.EXAMPLE
    pwsh samples/Kapso.Samples.WebhookReceiver/run-with-tunnel.ps1
#>
[CmdletBinding()]
param(
    [int] $Port = 5099
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$projectDir = $PSScriptRoot
$repoRoot = Join-Path $projectDir '..' '..' | Resolve-Path | Select-Object -ExpandProperty Path

if (-not (Get-Command cloudflared -ErrorAction SilentlyContinue)) {
    Write-Host 'cloudflared is not on PATH. Install it with:' -ForegroundColor Red
    Write-Host '  winget install Cloudflare.cloudflared'
    exit 1
}

$tunnelOut = New-TemporaryFile
$tunnel = $null

try {
    Write-Host 'Starting the Cloudflare tunnel…' -ForegroundColor Cyan

    # cloudflared writes the assigned hostname to stderr, so both streams are
    # captured and scanned.
    $tunnel = Start-Process cloudflared `
        -ArgumentList @('tunnel', '--url', "http://127.0.0.1:$Port", '--no-autoupdate') `
        -RedirectStandardError $tunnelOut.FullName `
        -RedirectStandardOutput "$($tunnelOut.FullName).out" `
        -PassThru -WindowStyle Hidden

    $publicUrl = $null
    $deadline = (Get-Date).AddSeconds(45)

    while (-not $publicUrl -and (Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 500

        if ($tunnel.HasExited) {
            Write-Host 'cloudflared exited before reporting a URL:' -ForegroundColor Red
            Get-Content $tunnelOut.FullName -Tail 20 | Write-Host
            exit 1
        }

        $text = Get-Content $tunnelOut.FullName -Raw -ErrorAction SilentlyContinue
        if ($text -match 'https://[a-z0-9-]+\.trycloudflare\.com') {
            $publicUrl = $Matches[0]
        }
    }

    if (-not $publicUrl) {
        Write-Host 'The tunnel did not report a URL within 45 seconds.' -ForegroundColor Red
        Get-Content $tunnelOut.FullName -Tail 20 | Write-Host
        exit 1
    }

    $webhookUrl = "$publicUrl/webhooks/kapso"

    Write-Host ''
    Write-Host ('-' * 72)
    Write-Host "  Tunnel: $webhookUrl" -ForegroundColor Green
    Write-Host ''
    Write-Host '  The receiver repoints your existing webhook at this URL and reads'
    Write-Host '  its secret from the API, so there is nothing to copy by hand and'
    Write-Host '  nothing to redo when the tunnel restarts with a new hostname.'
    Write-Host ''
    Write-Host '  Needs Kapso:ApiKey and Kapso:PhoneNumberId in user secrets. The'
    Write-Host '  webhook itself must already exist: create it once in the dashboard'
    Write-Host '  and subscribe it to the events you want.'
    Write-Host ('-' * 72)
    Write-Host ''

    Write-Host "Receiver listening on http://127.0.0.1:$Port — Ctrl+C to stop." -ForegroundColor Cyan
    Write-Host ''

    $env:ASPNETCORE_URLS = "http://127.0.0.1:$Port"

    # Lets the receiver recognise its own webhook and move it here.
    $env:Kapso__PublicUrl = $publicUrl

    dotnet run --project $projectDir -c Release -- --point-here
}
finally {
    if ($tunnel -and -not $tunnel.HasExited) {
        Write-Host 'Stopping the tunnel…' -ForegroundColor DarkGray
        Stop-Process -Id $tunnel.Id -Force -ErrorAction SilentlyContinue
    }

    Remove-Item $tunnelOut.FullName -ErrorAction SilentlyContinue
    Remove-Item "$($tunnelOut.FullName).out" -ErrorAction SilentlyContinue
}
