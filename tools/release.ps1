# 打包发布终端版（Windows PowerShell 版，和 release.sh 做同样的事）。结果在 publish/release/ 下。
#
#   .\tools\release.ps1 [-Version 1.2.3] [-WithDotnet]
#
# 默认每个平台打一个包 phi-DOS-<平台>.zip：不带 .NET 运行时，约 1MB，电脑上需要装有 .NET 10 运行时。
# 加 -WithDotnet 时再多打 phi-DOS-<平台>-with-dotnet.zip：自带 .NET 运行时，附上 .NET 的授权文件。
# 版本号会编译进程序，1.2.3 显示为 1.23；不给时显示 X.XX。也可以用环境变量 PHIDOS_VERSION。
# 注意：Compress-Archive 不会保留 Linux 的可执行权限，Linux 包解压后要 chmod +x phidos run-phidos.sh phidisk。
param(
    [string]$Version = $env:PHIDOS_VERSION,
    [switch]$WithDotnet
)
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')

$rids = 'win-x86', 'win-x64', 'win-arm64', 'linux-x64', 'linux-arm64'
$kinds = @('small')
if ($WithDotnet) { $kinds += 'with-dotnet' }
$out = 'publish/release'
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory $out | Out-Null

foreach ($rid in $rids) {
    foreach ($kind in $kinds) {
        if ($kind -eq 'small') {
            $name = "phi-DOS-$rid"
            $mode = @('--self-contained', 'false')
        } else {
            $name = "phi-DOS-$rid-with-dotnet"
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
