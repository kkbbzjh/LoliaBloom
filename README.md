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

This repository currently contains a GitHub Actions workflow for Windows x64. The application source code still needs to be uploaded and validated before it can produce a usable build.

Once source and builds are available, check [Releases](https://github.com/kkbbzjh/LoliaBloom/releases) for portable ZIP packages. Service installation will require Windows administrator approval.

## Contributing

Issues, feedback and pull requests are welcome. Please avoid including access tokens, tunnel secrets, or private logs in reports.

## Disclaimer

Unofficial community project; not affiliated with or endorsed by Lolia FRP. Lolia names and trademarks belong to their respective owners. No open-source license has been selected yet; check repository licensing before reuse or redistribution.
