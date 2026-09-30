$ErrorActionPreference = 'Stop'
$testExe = Join-Path $env:TEMP ('2ShipPresenceTests-' + [Guid]::NewGuid().ToString('N') + '.exe')
try {
    & "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /platform:x64 /target:exe /main:PresenceTests /r:System.Windows.Forms.dll /r:System.Drawing.dll "/out:$testExe" "$PSScriptRoot\Program.cs" "$PSScriptRoot\GameMemoryReader.cs" "$PSScriptRoot\Tests\PresenceTests.cs"
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
    & $testExe
    if ($LASTEXITCODE -ne 0) { throw 'Presence tests failed.' }
} finally {
    if (Test-Path -LiteralPath $testExe) { Remove-Item -LiteralPath $testExe }
}
