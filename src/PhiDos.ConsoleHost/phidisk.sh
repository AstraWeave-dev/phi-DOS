#!/bin/sh
# phi-DOS disk image tool, see: phidisk --help
exec "$(dirname "$0")/phidos" --phidisk "$@"
