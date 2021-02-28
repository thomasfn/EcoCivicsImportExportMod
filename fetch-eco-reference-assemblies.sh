#!/bin/bash

MODKIT_VERSION="0.9.2.4-beta"
MODKIT_FILENAME="EcoModKit_v$MODKIT_VERSION.zip"

mkdir -p ./eco-dlls
wget "https://s3-us-west-2.amazonaws.com/eco-releases/$MODKIT_FILENAME"
unzip -o $MODKIT_FILENAME -d ./tmp
cp ./tmp/ReferenceAssemblies/*.dll ./eco-dlls
rm -r ./tmp
rm $MODKIT_FILENAME