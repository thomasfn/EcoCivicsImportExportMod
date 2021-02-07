#!/bin/bash

docker build . -t ecocivicsimportexportmodserver:latest
docker run --mount "type=bind,src=$PWD/scripts,dst=/app/scripts,ro" --mount "type=bind,src=$PWD/eco-dlls,dst=/app/eco-dlls" ecocivicsimportexportmodserver:latest sh /app/scripts/extract-dlls.sh