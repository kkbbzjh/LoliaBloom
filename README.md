# 🌸 Lolia Bloom · 洛链绽放

A pink-themed Windows desktop client for managing Lolia FRP tunnels.

[English](README.md) | [简体中文](README.zh-CN.md)

![Lolia Bloom — Windows interface — example tunnels](https://github.com/kkbbzjh/LoliaBloom/releases/download/v0.6.1/LoliaBloom-screenshot.png)

![Create tunnel — example nodes](https://github.com/kkbbzjh/LoliaBloom/releases/download/v0.6.1/LoliaBloom-create.png)

## Features

- **Refined workspace** — custom title bar, searchable node cards, region and online filters, and one-click address copying.

- **Independent FRP updates** — download official releases, verify SHA256, restore running tunnels and roll back on failure.

- **Cloud tunnel creation** — select a node and create TCP, UDP, HTTP or HTTPS tunnels.
- **Saved login** — click Login to authorize with the official website; OAuth PKCE, encrypted credentials and automatic token refresh.
- **Local network rates** — per-process Windows ETW counters, refreshed every second. Excludes loopback and backend traffic; includes FRP transport overhead. Platform totals remain separate.
- **Website check-in** — open the official dashboard to check in and view your daily status.

- **Pink interface** — a clean tunnel list with per-tunnel start and stop buttons.
- **Tunnel details** — view protocol, node, local and remote addresses, and connection status.
- **Background service** — keep tunnel processes running after closing the window.
- **One-click service installation** — install or remove the Windows Service from the app with administrator approval.
- **Start with Windows** — independent capsule switches for each tunnel; settings survive cloud sync and service restarts, with retries when the initial connection fails.
- **Single EXE for Windows x64** — run one file; the included runtime is prepared automatically. A ZIP alternative is also available.

[Download Windows x64](https://github.com/kkbbzjh/LoliaBloom/releases/latest)

Lolia Bloom is an unofficial community client, not affiliated with Lolia FRP.
