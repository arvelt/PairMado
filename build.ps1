param([switch]$Verify)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path -LiteralPath (Join-Path $framework 'csc.exe'))) {
    $framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
}
$compiler = Join-Path $framework 'csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework compiler was not found.' }
$destination = Join-Path $projectRoot 'dist'
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$sources = @((Join-Path $projectRoot 'src\PairMado.cs'))
$compilerOptions = @('/nologo', '/target:winexe', '/optimize+',
    ('/out:' + (Join-Path $destination 'PairMado.exe')),
    ('/win32icon:' + (Join-Path $projectRoot 'assets\image-compare.ico')),
    '/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll',
    '/reference:System.Core.dll', '/reference:System.Xaml.dll',
    ('/reference:' + (Join-Path $framework 'WPF\PresentationCore.dll')),
    ('/reference:' + (Join-Path $framework 'WPF\WindowsBase.dll')))
if ($Verify) {
    $compilerOptions += '/define:VERIFY'
    $sources += Join-Path $projectRoot 'tests\Verification.cs'
}
& $compiler @compilerOptions @sources
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'assets\image-compare.ico') -Destination $destination -Force
if ($Verify) {
    $testRoot = Join-Path (Join-Path $projectRoot 'test-output') ([Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
    $testProcess = Start-Process -FilePath (Join-Path $destination 'PairMado.exe') -ArgumentList @('--verify', ('"' + $testRoot + '"')) -WindowStyle Hidden -Wait -PassThru
    Get-Content -LiteralPath (Join-Path $testRoot 'verification.txt')
    if ($testProcess.ExitCode -ne 0) { throw 'Verification failed.' }
    # テスト用の操作入口を配布バイナリに含めないよう、通常版を再ビルドします。
    & $compiler @($compilerOptions | Where-Object { $_ -ne '/define:VERIFY' }) (Join-Path $projectRoot 'src\PairMado.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Release compilation failed.' }
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE'),(Join-Path $projectRoot 'README.md') -Destination $destination -Force
$packageFiles = @('PairMado.exe', 'image-compare.ico', 'LICENSE', 'README.md') | ForEach-Object { Join-Path $destination $_ }
Compress-Archive -LiteralPath $packageFiles -DestinationPath (Join-Path $destination 'PairMado-Windows.zip') -Force
Write-Output ('Built: ' + (Join-Path $destination 'PairMado-Windows.zip'))
