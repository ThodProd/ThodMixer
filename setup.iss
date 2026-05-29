; Inno Setup Script for ThodMixer

[Setup]
AppName=ThodMixer
AppVersion=1.0
DefaultDirName={autopf}\ThodMixer
DefaultGroupName=ThodMixer
UninstallDisplayIcon={app}\ThodMixer.exe
OutputDir=.
OutputBaseFilename=ThodMixer_Setup
Compression=lzma2/max
SolidCompression=yes
SetupIconFile=Resources\app.ico
WizardStyle=modern

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Files]
Source: "bin\Release\net10.0-windows\win-x64\publish\ThodMixer.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "bin\Release\net10.0-windows\win-x64\publish\Melanchall_DryWetMidi_Native64.dylib"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\ThodMixer"; Filename: "{app}\ThodMixer.exe"
Name: "{commondesktop}\ThodMixer"; Filename: "{app}\ThodMixer.exe"

[Run]
Filename: "{app}\ThodMixer.exe"; Description: "Запустить ThodMixer"; Flags: postinstall nowait skipifsilent
