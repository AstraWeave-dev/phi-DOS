#!/usr/bin/env bash
# 打包发布终端版，结果在 publish/release/ 下。
#
#   ./tools/release.sh [版本号] [--with-dotnet]
#
# 默认每个平台打一个包 phi-DOS-<平台>.zip：不带 .NET 运行时，约 1MB，电脑上需要装有 .NET 10 运行时。
# 加 --with-dotnet 时，每个平台再多打一个 phi-DOS-<平台>-with-dotnet.zip：自带 .NET 运行时，
# 包里附上 .NET 的授权文件。
#
# 版本号（例如 1.2.3，也可以用环境变量 PHIDOS_VERSION）会编译进程序，显示为 DOS 风格的两段式 1.23。
# 不给时显示占位符 X.XX。
#
# 平台：win-x86、win-x64、win-arm64、linux-x64、linux-arm64。32 位的 win-arm 从 .NET 7 起不再支持。
set -euo pipefail
cd "$(dirname "$0")/.."

VERSION="${PHIDOS_VERSION:-}"
WITH_DOTNET=false
for arg in "$@"; do
  case "$arg" in
    --with-dotnet) WITH_DOTNET=true ;;
    -*) echo "Unknown option $arg" >&2; exit 2 ;;
    *) VERSION="$arg" ;;
  esac
done
version_arg=()
if [ -n "$VERSION" ]; then version_arg=("-p:PhiDosVersion=$VERSION"); fi

RIDS=(win-x86 win-x64 win-arm64 linux-x64 linux-arm64)
KINDS=(small)
if [ "$WITH_DOTNET" = true ]; then KINDS+=(with-dotnet); fi
OUT=publish/release
rm -rf "$OUT"
mkdir -p "$OUT"

for rid in "${RIDS[@]}"; do
  for kind in "${KINDS[@]}"; do
    if [ "$kind" = small ]; then
      name="phi-DOS-$rid"
      mode=(--self-contained false)
    else
      name="phi-DOS-$rid-with-dotnet"
      # 单文件压缩能让带 .NET 的包小一半
      mode=(--self-contained true -p:EnableCompressionInSingleFile=true)
    fi
    echo "==> $name"
    dotnet publish src/PhiDos.ConsoleHost -c Release -r "$rid" "${mode[@]}" ${version_arg[@]+"${version_arg[@]}"} \
      -p:PublishSingleFile=true -p:DebugType=none \
      -p:CopyOutputSymbolsToPublishDirectory=false -p:AllowedReferenceRelatedFileExtensions=.xml \
      -o "$OUT/$name" -v quiet -nologo
    if [ -f "$OUT/$name/run-phidos.sh" ]; then chmod +x "$OUT/$name/run-phidos.sh" "$OUT/$name/phidisk" "$OUT/$name/phidos"; fi
    (cd "$OUT" && zip -qr "$name.zip" "$name")
  done
done

ls -lh "$OUT"/*.zip
