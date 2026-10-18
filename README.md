# 🌸 Lolia Bloom · 洛链绽放

A pink-themed Windows desktop client for managing Lolia FRP tunnels.

[English](README.md) | [简体中文](README.zh-CN.md)

**Live traffic dashboard**

![Live upload and download charts with per-tunnel startup switches](docs/screenshots/main-v0.6.3.png)

**Connection details**

![Tunnel details with live rates and one-click address copying](docs/screenshots/details-v0.6.3.png)

**Create a tunnel**

![Node filters and tunnel creation](docs/screenshots/create-v0.6.3.png)

Screenshots are rendered by the Windows client using example tunnels and traffic data.

## Features

- **Refined workspace** — custom title bar, searchable node cards, region and online filters, and one-click address copying.

- **Independent FRP updates** — download official releases, verify SHA256, restore running tunnels and roll back on failure.

- **Cloud tunnel creation** — select a node and create TCP, UDP, HTTP or HTTPS tunnels.
- **Saved login** — click Login to authorize with the official website; OAuth PKCE, encrypted credentials and automatic token refresh.
- **Local network rates** — live 60-second upload/download charts, refreshed every second with per-process Windows ETW counters. Excludes loopback and backend traffic; includes FRP transport overhead. Platform totals remain separate.
- **Website check-in** — open the official dashboard to check in and view your check-in status.

- **Pink interface** — a clean tunnel list with per-tunnel start and stop buttons.
- **Tunnel details** — a live card view of rates, traffic trends, platform totals, startup state and copyable access addresses.
- **Background service** — keep tunnel processes running after closing the window.
- **One-click service installation** — install or remove the Windows Service from the app with administrator approval.
- **Start with Windows** — independent capsule switches for each tunnel; settings survive cloud sync and service restarts, with retries when the initial connection fails.
- **Single EXE for Windows x64** — run one file; the included runtime is prepared automatically. A ZIP alternative is also available.

[Download Windows x64](https://github.com/kkbbzjh/LoliaBloom/releases/latest)

Lolia Bloom is an unofficial community client, not affiliated with Lolia FRP.
