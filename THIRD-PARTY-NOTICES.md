# Komponenten und Referenzen

SetupPulse enthält keine MultiMonitorTool-, MonitorProfileSwitcher- oder AudioSwitcher-EXE/DLL. Die native Steuerung ist eine eigene Implementierung. Die bestehenden Tools wurden als Funktions- und Verhaltensreferenz untersucht.

| Bestandteil | Lizenz / Quelle |
| --- | --- |
| Eigener Host, Core, Oberfläche | MIT, siehe LICENSE |
| shadcn/ui generierte Komponenten | MIT, https://github.com/shadcn-ui/ui |
| React, Radix, Tailwind, Lucide, Sonner und weitere npm-Abhängigkeiten | MIT/ISC/weitere, vollständige Texte in THIRD-PARTY-LICENSES.txt |
| NAudio.Core / NAudio.Wasapi | MIT, https://github.com/naudio/NAudio |
| MQTTnet | MIT, siehe [Lizenztext](licenses/MQTTnet-MIT.txt) und https://github.com/dotnet/MQTTnet |
| EarTrumpet: Referenz und adaptierte Windows-App-Audio-Routing-ABI | MIT, Copyright (c) 2015, siehe [Lizenztext](licenses/EarTrumpet-MIT.txt) und https://github.com/File-New-Project/EarTrumpet/tree/2.3.0.0 |
| Microsoft .NET Runtime | MIT und Third-party notices, https://github.com/dotnet/runtime |
| Microsoft WebView2 SDK | Microsoft-Lizenz, siehe THIRD-PARTY-LICENSES.txt |
| System-WebView2 Runtime | Separat installiert; Microsoft-Nutzungsbedingungen |

Protokoll- und Verhaltensreferenzen:

- [Microsoft CCD-Dokumentation](https://learn.microsoft.com/windows/win32/api/wingdi/nf-wingdi-setdisplayconfig).
- [DPI-Sample](https://github.com/lihas/windows-DPI-scaling-sample), Unlicense/public domain. Referenz für die kompatible -3/-4-Paketstruktur und relative DPI-Indizes; eigene Implementierung mit exakter Bereichsprüfung.
- [AudioSwitcher](https://github.com/xenolightning/AudioSwitcher), MS-PL. Funktionsreferenz und Prüfung der PolicyConfig-Schnittstelle; kein übernommener Implementierungscode. Windows-COM-ABI wird in einem eigenen kleinen Adapter aufgerufen.
- [EarTrumpet 2.3.0.0](https://github.com/File-New-Project/EarTrumpet/tree/2.3.0.0), MIT. Referenz für Mixer-Verhalten, App-Gruppierung und App-Ausgabezuordnung; Schnittstellenkennungen, VTable-Reihenfolge und Gerätepfadformat wurden für den isolierten Windows-Routing-Adapter adaptiert. EarTrumpet selbst und seine Markenassets werden nicht mitgeliefert oder zur Laufzeit benötigt.
- [MonitorProfileSwitcher](https://sourceforge.net/projects/monitorswitcher/), Martin Krämer, MPL-2.0. Funktions-/API-Referenz, kein übernommener Code.
- [MultiMonitorTool](https://www.nirsoft.net/utils/multi_monitor_tool.html), NirSoft, proprietäres Freeware-Tool. Kein Bestandteil oder Laufzeit-Dependency.

Die gewählte Farbpalette orientiert sich am [shadcn-Preset bzp5iTzge](https://ui.shadcn.com/create?preset=bzp5iTzge). Runtime-Lizenzen werden bei der Distribution zusammen mit der EXE bereitgestellt; für den eigenen Betrieb reicht die EXE.
