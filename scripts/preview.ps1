#requires -Version 7
[CmdletBinding()]
param([ValidateRange(2,30)][int]$Seconds=8)
$ErrorActionPreference='Stop'
& (Join-Path $PSScriptRoot 'build.ps1') | Out-Null
& node (Join-Path (Split-Path -Parent $PSScriptRoot) 'desktop.mjs') preview $Seconds
exit $LASTEXITCODE
