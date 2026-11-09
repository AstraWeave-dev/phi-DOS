#!/usr/bin/env bash
# 打包发布终端版：每个平台两种包，结果在 publish/release/ 下。
#   phi-DOS-<平台>.zip        完整版，自带 .NET 运行时
#   phi-DOS-<平台>-small.zip  小体积版，需要电脑上装有 .NET 10 运行时
# 32 位的 win-arm 从 .NET 7 起不再支持，所以没有。
set -euo pipefail
cd "$(dirname "$0")/.."

RIDS=(win-x86 win-x64 win-arm64 linux-x64 linux-arm64)
OUT=publish/release
rm -rf "$OUT"
mkdir -p "$OUT"

for rid in "${RIDS[@]}"; do
  for kind in full small; do
    name="phi-DOS-$rid"
    if [ "$kind" = small ]; then
      name="$name-small"
      mode=(--self-contained false)
    else
      # 单文件压缩能让完整版小一半
      mode=(--self-contained true -p:EnableCompressionInSingleFile=true)
    fi
    echo "==> $name"
    dotnet publish src/PhiDos.ConsoleHost -c Release -r "$rid" "${mode[@]}" \
      -p:PublishSingleFile=true -p:DebugType=none \
      -p:CopyOutputSymbolsToPublishDirectory=false -p:AllowedReferenceRelatedFileExtensions=.xml \
      -o "$OUT/$name" -v quiet -nologo
    if [ -f "$OUT/$name/run-phidos.sh" ]; then chmod +x "$OUT/$name/run-phidos.sh" "$OUT/$name/phidos"; fi
    (cd "$OUT" && zip -qr "$name.zip" "$name")
  done
done

ls -lh "$OUT"/*.zip
