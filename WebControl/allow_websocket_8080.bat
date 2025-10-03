@echo off
REM Adds a Windows Firewall rule to allow inbound TCP traffic on port 8080 for Unity WebSocket

netsh advfirewall firewall add rule name="Unity WebSocket 8080" dir=in action=allow protocol=TCP localport=8080 profile=any
netsh advfirewall firewall add rule name="Unity WebSocket 8080" dir=out action=allow protocol=TCP localport=8080 profile=any

echo Firewall rule added for port 8080.
pause
