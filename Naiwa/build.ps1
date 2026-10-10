param([string]$OutputPath = (Join-Path $PSScriptRoot '奶蛙.exe'))
$ErrorActionPreference = 'Stop'
$naiwaRoot = $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw '需要 Windows .NET Framework 4.x 编译工具。' }
$arguments = @('/nologo', '/utf8output', '/target:winexe', '/platform:anycpu', '/optimize+',
    ('/out:' + $OutputPath), '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll',
    '/r:System.Runtime.Serialization.dll', ('/win32icon:' + (Join-Path $naiwaRoot 'assets/naiwa.ico')),
    ('/win32manifest:' + (Join-Path $naiwaRoot 'source/app.manifest')))
Get-ChildItem -LiteralPath (Join-Path $naiwaRoot 'assets') -File | Where-Object { $_.Extension -in '.png', '.ico' } | ForEach-Object {
    $arguments += '/resource:' + $_.FullName + ',Naiwa.assets.' + $_.Name
}
Get-ChildItem -LiteralPath (Join-Path $naiwaRoot 'source') -Filter '*.cs' -File | Where-Object { $_.Name -ne 'AssetPack.cs' } | ForEach-Object { $arguments += $_.FullName }
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw '编译失败。' }
Write-Output $OutputPath
