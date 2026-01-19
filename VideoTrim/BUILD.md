# VideoTrim (C# WinForms)

## Build EXE fara Python (cat mai mic)

1) Instaleaza .NET SDK 8 (o singura data, doar pe PC-ul de build).
2) Ruleaza:

```
dotnet publish .\VideoTrim\VideoTrim.csproj -c Release -r win-x64 ^
  --self-contained false ^
  /p:PublishSingleFile=true
```

Rezultat: `.\VideoTrim\bin\Release\net8.0-windows\win-x64\publish\VideoTrim.exe`

## Pentru utilizatorul final

Trimiti doar `VideoTrim.exe`.

La prima rulare, aplicatia poate descarca automat ffmpeg/ffprobe daca lipsesc.
Pentru rulare este necesar **.NET Desktop Runtime** instalat pe PC.

## Installer (cu download automat runtime)

1) Instaleaza Inno Setup 6+
2) Asigura-te ca ai rulat `dotnet publish` (vezi mai sus)
3) Deschide `installer\VideoTrim.iss` in Inno Setup si apasa **Compile**

Installerul rezultat: `installer\VideoTrim-Setup.exe`
