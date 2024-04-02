#!/bin/bash

MODKIT_FILENAME="EcoModKit_v$MODKIT_VERSION.zip"

mkdir -p ./eco-dlls
wget "https://eco-releases-$ECO_BRANCH-eu.s3.eu-central-1.amazonaws.com/$MODKIT_FILENAME"
unzip -o $MODKIT_FILENAME -d ./tmp
cp ./tmp/ReferenceAssemblies/*.dll ./eco-dlls
rm -r ./tmp
rm $MODKIT_FILENAME