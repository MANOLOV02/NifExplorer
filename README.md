Fast and High Quality Nif Explorer

Browse and preview every NIF of Fallout 4 and Skyrim Special Edition, from loose folders and from
BA2/BSA archives, with no setup: you pick the sources and their order, and loose files always win
over archives, like the engine does. The game is taken from the NIF itself. Export any NIF together
with its textures and materials, keeping the game relative paths.

Instructions:

Requires the following libraries/packages:
 - ManoloV02: FO4 Base Library - Licensed under the GPL-3.0 License  (https://github.com/MANOLOV02/FO4_Base_Library)
 - ManoloV02: BSA/BA2 Library - Licensed under the GPL-3.0 License  (https://github.com/MANOLOV02/BSA_BA2_Library_DLL)

Build with MSBuild, platform x64:
 msbuild Nif_Explorer.vbproj -t:Restore,Build -p:Configuration=Publish -p:Platform=x64
