@echo off
rem Read app branding configuration
for /f "delims=" %%i in ('pwsh -NoProfile -Command "[xml]$x = Get-Content 'src\AppBranding.props'; $x.Project.PropertyGroup.AppExecutableName"') do set AppExecutableName=%%i

rmdir /q /s src\UniGetUI\bin\x64\Release\net8.0-windows10.0.26100.0\win-x64\publish\
dotnet publish src/UniGetUI/UniGetUI.csproj /noLogo /property:Configuration=Release /property:Platform=x64 -v m
%signcommand% "src\UniGetUI\bin\x64\Release\net8.0-windows10.0.26100.0\win-x64\publish\%AppExecutableName%.exe"
python3 scripts\generate_integrity_tree.py %cd%\src\UniGetUI\bin\x64\Release\net8.0-windows10.0.26100.0\win-x64\publish\
echo %cd%\src\UniGetUI\bin\x64\Release\net8.0-windows10.0.26100.0\win-x64\publish\
src\UniGetUI\bin\x64\Release\net8.0-windows10.0.26100.0\win-x64\publish\%AppExecutableName%.exe
pause