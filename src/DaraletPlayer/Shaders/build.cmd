@echo off
rem Compiles the shaders into Resources\shaders (checked in, so a build needs no Windows SDK). Run after changing one.
set FXC="C:\Program Files (x86)\Windows Kits\10\bin\10.0.18362.0\x86\fxc.exe"
cd /d "%~dp0"
for %%s in (fullscreen grass shadow fartree lampshadow farland) do (
    %FXC% /nologo /T vs_3_0 /E main /O3 /Fo ..\Resources\shaders\%%s.vs.cso %%s.vs.hlsl || exit /b 1
)
for %%s in (shading water antialias sharpen grass shadow fartree lamps glowbright glowblur lampshadow farland seethrough shadeblur) do (
    %FXC% /nologo /T ps_3_0 /E main /O3 /Fo ..\Resources\shaders\%%s.ps.cso %%s.ps.hlsl || exit /b 1
)
rem the lamps again with fewer shadow maps, for cards that take fewer instructions (Ui/DepthShading.Lamps.cs)
for %%m in (10 8 6) do (
    %FXC% /nologo /T ps_3_0 /E main /O3 /D MAPS=%%m /Fo ..\Resources\shaders\lamps%%m.ps.cso lamps.ps.hlsl || exit /b 1
)
