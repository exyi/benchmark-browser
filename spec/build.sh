#!/bin/bash

if [ $# -eq 1 ]
  then
    cd $@
fi

rm *-crop.pdf
rm spec.pdf
rm spec.html

for i in *.pdf; do
    pdfcrop $i
done

pandoc --smart spec.md -o spec.html
pandoc --smart spec.md -o spec.pdf

