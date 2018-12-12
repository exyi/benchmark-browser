#!/usr/bin/fish

set -x COMPlus_TieredCompilation 1

cd src/worker
while true
     dotnet run -c Production -- config.json
     sleep 100
end
