#!/bin/sh
# Boot phi-DOS with the A folder next to this file as drive A:
cd "$(dirname "$0")" && exec ./phidos --no-date -a ./A
