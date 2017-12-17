#!/usr/bin/fish

# build the fable client-side
cd src/fableweb/src
yarn install
dotnet restore
dotnet fable webpack -- -p

#run the API server
cd ../../api
dotnet run --configuration Release

