#requires -Version 7
[CmdletBinding()]
param([switch]$Force)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
$compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Windows .NET Framework C# compiler is unavailable' }
$sources=@(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'native') -Filter '*.cs' | Sort-Object Name)
$output=Join-Path $projectRoot 'bin/DshDesktop.exe'
if (-not $Force -and (Test-Path -LiteralPath $output)) {
  $latest=($sources | Measure-Object LastWriteTime -Maximum).Maximum
  if ((Get-Item -LiteralPath $output).LastWriteTime -ge $latest) { Write-Output $output; return }
}
New-Item -ItemType Directory -Force (Split-Path -Parent $output) | Out-Null
$framework=Split-Path -Parent $compiler
$references=@('System.Windows.Forms.dll','System.Drawing.dll','System.Web.Extensions.dll','System.Core.dll')
$compilerArgs=@('/nologo','/target:exe','/optimize+','/utf8output',('/out:'+$output))
foreach($reference in $references){$compilerArgs+=('/reference:'+ (Join-Path $framework $reference))}
foreach($reference in @('UIAutomationClient.dll','UIAutomationTypes.dll','WindowsBase.dll')){$compilerArgs+=('/reference:'+(Join-Path $framework ('WPF/'+$reference)))}
$compilerArgs+=@($sources.FullName)
$messages=& $compiler @compilerArgs 2>&1
if ($LASTEXITCODE -ne 0){throw ($messages -join [Environment]::NewLine)}
Write-Output $output
