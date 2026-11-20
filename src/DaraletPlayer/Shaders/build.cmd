@echo off
rem Compiles the shaders into Resources\shaders (checked in, so a build needs no Windows SDK). Run after changing one.
set FXC="C:\Program Files (x86)\Windows Kits\10\bin\10.0.18362.0\x86\fxc.exe"
cd /d "%~dp0"
for %%s in (fullscreen grass shadow fartree lampshadow farland) do (
    %FXC% /nologo /T vs_3_0 /E main /O3 /Fo ..\Resources\shaders\%%s.vs.cso %%s.vs.hlsl || exit /b 1
)
for %%s in (shading water antialias sharpen grass shadow fartree lamps glowbright glowblur lampshadow farland) do (
    %FXC% /nologo /T ps_3_0 /E main /O3 /Fo ..\Resources\shaders\%%s.ps.cso %%s.ps.hlsl || exit /b 1
)
