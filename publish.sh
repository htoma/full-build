#! /bin/bash

function failure
{
  exit 5
}

function publish
{
  mono src/fullbuild/bin/fullbuild.exe publish * || failure
  
  rm -f apps/full-build/*
  cp apps/full-build/* apps/full-build
}


publish
