# 打包发布终端版（Windows PowerShell 版，和 release.sh 做同样的事）。结果在 publish/release/ 下。
#   phi-DOS-<平台>.zip        完整版，自带 .NET 运行时
#   phi-DOS-<平台>-small.zip  小体积版，需要电脑上装有 .NET 10 运行时
# 注意：Compress-Archive 不会保留 Linux 的可执行权限，Linux 包解压后要 chmod +x phidos run-phidos.sh phidisk。
# 版本号：.\tools\release.ps1 1.2.3（或者设置环境变量 PHIDOS_VERSION）。程序里显示为 1.23，不给时显示 X.XX。
param([string]$Version = $env:PHIDOS_VERSION)
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')

$rids = 'win-x86', 'win-x64', 'win-arm64', 'linux-x64', 'linux-arm64'
$out = 'publish/release'
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory $out | Out-Null

foreach ($rid in $rids) {
    foreach ($kind in 'full', 'small') {
        $name = "phi-DOS-$rid"
        if ($kind -eq 'small') {
            $name += '-small'
            $mode = @('--self-contained', 'false')
        } else {
            $mode = @('--self-contained', 'true', '-p:EnableCompressionInSingleFile=true')
        }
        Write-Host "==> $name"
        $dir = Join-Path $out $name
        $dotnetArgs = @('publish', 'src/PhiDos.ConsoleHost', '-c', 'Release', '-r', $rid) + $mode + @(
            '-p:PublishSingleFile=true', '-p:DebugType=none',
            '-p:CopyOutputSymbolsToPublishDirectory=false', '-p:AllowedReferenceRelatedFileExtensions=.xml',
            '-o', $dir, '-v', 'quiet', '-nologo')
        if ($Version) { $dotnetArgs += "-p:PhiDosVersion=$Version" }
        & dotnet @dotnetArgs
        if ($LASTEXITCODE -ne 0) { throw "publish failed: $name" }
        Compress-Archive -Path $dir -DestinationPath "$dir.zip"
    }
}

Get-ChildItem "$out/*.zip" | Format-Table Name, Length
