# 🌸 Lolia Bloom

**A community-developed Windows desktop client for Lolia FRP.**

[English](README.md) | [简体中文](README.zh-CN.md)

> **Development status:** Early prototype. The Windows build and live Lolia API integration have not yet been verified. Do not rely on this project for production connectivity.

Lolia Bloom aims to make tunnel management simpler on Windows, with a pink-themed interface and a Windows background service that can keep tunnels running when the UI is closed.

## Planned features

- OAuth account sign-in and cloud tunnel synchronization
- TCP, UDP, HTTP and HTTPS tunnel management (subject to official API capabilities)
- Per-tunnel start/stop controls directly in the list
- Upload/download rates, traffic totals and manual refresh (only when supported by real data sources)
- Detailed tunnel information and logs
- One-click Windows Service registration from a portable distribution
- Check-in dashboard; automatic daily check-in **only if officially supported and permitted**

## Installation and builds

The V0.4 source is included. GitHub Actions builds self-contained Windows x64 binaries and verifies Windows Service installation and named-pipe communication.

Check [Releases](https://github.com/kkbbzjh/LoliaBloom/releases) for portable ZIP packages. Service installation will require Windows administrator approval.

## Contributing

Issues, feedback and pull requests are welcome. Please avoid including access tokens, tunnel secrets, or private logs in reports.

## Disclaimer

Unofficial community project; not affiliated with or endorsed by Lolia FRP. Lolia names and trademarks belong to their respective owners. No open-source license has been selected yet; check repository licensing before reuse or redistribution.

## Portable setup

Extract the entire ZIP to a folder and run `Lolia.App.exe`. Click the service installation button and approve Windows UAC. Keep the `Service` folder beside the app. Service files are copied to Program Files; data is stored under `%ProgramData%\LoliaBloom`.

Download the official `frpc.exe` separately and place it beside `install-service.cmd` before installation. Register an OAuth public client with redirect URI `http://localhost:1145` and write its client ID to `%ProgramData%\LoliaBloom\client-id.txt`. Per-tunnel credentials are read from `keys/<id>.txt` under that data directory. Check-in, automatic check-in, cloud creation and traffic metrics are not implemented integrations in this prototype.
