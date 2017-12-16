#!/usr/bin/fish

cd src/worker
while true
     dotnet run -c Production -- config.json
end
