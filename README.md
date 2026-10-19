# 🌸 Lolia Bloom · 洛链绽放

A pink-themed community client for Lolia FRP, with a Windows desktop app and a Linux Docker web manager.

[English](README.md) | [简体中文](README.zh-CN.md)

**Live traffic dashboard**

![Live upload and download charts with per-tunnel startup switches](docs/screenshots/main-v0.7.4.png)

**Connection details**

![Tunnel details with live rates and one-click address copying](docs/screenshots/details-v0.7.4.png)

**Create a tunnel**

![Node filters and tunnel creation](docs/screenshots/create-v0.7.4.png)

Screenshots are rendered by the Windows client using example tunnels and traffic data.

## Features

- **In-app updates (Windows)**: automatically check official GitHub Releases; download, verify SHA256 and restart on click, with progress and cancellation; the new EXE is saved beside the running portable EXE. The old window stays open until the new version confirms startup.

- **Refined workspace** — custom title bar, searchable node cards, region and online filters, and one-click address copying.

- **Independent FRP updates** — download official releases, verify SHA256, restore running tunnels and roll back on failure.

- **Cloud tunnel creation** — select a node and create TCP, UDP, HTTP or HTTPS tunnels.
- **Saved login** — click Login to authorize with the official website; OAuth PKCE, encrypted credentials and automatic token refresh.
- **Local network rates** — live 60-second upload/download charts, refreshed every second with per-process Windows ETW counters. Excludes loopback and backend traffic; includes FRP transport overhead. Platform totals remain separate.
- **Automatic website check-in (Windows)**: sign in once in a separate local browser, then enable background check-in with a persistent 24-hour countdown, Windows sign-in startup and copyable diagnostics. Login expiry or verification needs user attention. Linux retains the official website link.

- **Pink interface** — a clean tunnel list with per-tunnel start and stop buttons.
- **Tunnel details** — a live card view of rates, traffic trends, platform totals, startup state and copyable access addresses.
- **Background service** — keep tunnel processes running after closing the window.
- **One-click service installation** — install or remove the Windows Service from the app with administrator approval.
- **Start with Windows** — independent capsule switches for each tunnel; settings survive cloud sync and service restarts, with retries when the initial connection fails.
- **Single EXE for Windows x64** — run one file; the included runtime is prepared automatically. A ZIP alternative is also available.
- **Linux web manager** — deploy with Docker Compose, sign in to the management page, sync cloud tunnels, start or stop tunnels individually, set per-tunnel startup behavior, and install verified FRP updates.
- **Docker traffic charts** — show the last 60 seconds of container-interface traffic, including web management traffic; official cumulative tunnel traffic is displayed separately.

[Download Windows x64](https://github.com/kkbbzjh/LoliaBloom/releases/latest)

## Installation

### Windows x64

#### Single EXE (recommended)

1. Open [GitHub Releases](https://github.com/kkbbzjh/LoliaBloom/releases/latest) and download `LoliaBloom-v0.7.14-win-x64.exe`.
2. Double-click to run. No separate .NET installation is required.

#### ZIP version

Alternatively, download `LoliaBloom-v0.7.14-win-x64.zip`, extract it, and run `Lolia.App.exe`.

#### Install the background service

To keep tunnels running after closing the window, choose **Install background service** in the app and approve the Windows administrator prompt.

### Automatic website check-in on Windows

Open “自动签到 / 官网登录” at the bottom left, sign in on the embedded official website, then enable the switch. This website session is separate from the app’s OAuth login and stays on your Windows account. Microsoft Edge WebView2 Runtime is required.

A confirmed success starts a saved **24-hour + 30-second** timer. Closing either window keeps the enabled worker running; it also starts at Windows sign-in. Shutdown or sign-out pauses execution until next sign-in. If the website already shows checked-in but its timestamp is unknown, the worker checks availability every 30 minutes. Use “关闭自动签到并退出” to stop the worker and remove startup registration. Expired login, verification, network failures and website changes are shown in copyable diagnostics.

### Linux / NAS (Docker Compose)

#### One-command installation

Requires Docker Engine and Compose v2.

```bash
curl -fsSL https://raw.githubusercontent.com/kkbbzjh/LoliaBloom/main/scripts/install.sh | sh
```

Generates and saves an administrator password, pulls the image, starts and checks the service, then prints the address and password. Run again to update while keeping configuration and data. The GHCR package must be public for anonymous pulls; cloud login still requires an allowed OAuth callback.

#### Manual deployment

Use a Linux host or NAS with Docker Engine and Docker Compose v2. Replace `192.168.1.20` below with its LAN address. After the first image is published, open [GHCR package settings](https://github.com/users/kkbbzjh/packages/container/package/loliabloom) and set visibility to **Public** so Docker can pull anonymously.

```bash
mkdir -p ~/loliabloom && cd ~/loliabloom
curl -fsSLO https://raw.githubusercontent.com/kkbbzjh/LoliaBloom/main/compose.yaml
curl -fsSLO https://raw.githubusercontent.com/kkbbzjh/LoliaBloom/main/.env.example
cp .env.example .env
openssl rand -base64 32
```

Put the generated random value in `.env` as `LOLIABLOOM_ADMIN_PASSWORD`, set `LOLIABLOOM_PUBLIC_URL=http://192.168.1.20:8080`, save the file, then run:

```bash
docker compose pull
docker compose up -d
```

Open `http://192.168.1.20:8080` and enter the admin password. For internet access, configure an HTTPS reverse proxy and set `LOLIABLOOM_PUBLIC_URL` to its HTTPS address.

Stop the service with `docker compose down`. Update to the latest image with `docker compose pull && docker compose up -d`. Tunnel and login data remain in the `loliabloom-data` named volume when the container is removed.

Cloud authorization requires the OAuth app to allow the exact callback `{LOLIABLOOM_PUBLIC_URL}/auth/callback`. If the default public Client ID does not allow that address, authorization will be rejected; configure a Client ID with that callback registered in `LOLIA_OAUTH_CLIENT_ID`. `LOLIABLOOM_OAUTH_REDIRECT_URL` can override the full callback URL.

View logs with `docker compose logs -f`.

Lolia Bloom is an unofficial community client, not affiliated with Lolia FRP.

## License and commercial authorization

[![License: Noncommercial](https://img.shields.io/badge/License-Noncommercial-D74787)](LICENSE)

Lolia Bloom's original code and assets are source-available under the [Lolia Bloom Noncommercial License 1.0](LICENSE). Noncommercial use, modification and distribution are permitted under its terms. **All commercial purposes, including internal business use, require prior separate written authorization from the copyright holder.** Free access to the program does not grant commercial rights. This is not an OSI-approved open-source license.

For commercial authorization, [contact kkbbzjh through a GitHub issue](https://github.com/kkbbzjh/LoliaBloom/issues/new?title=Commercial%20authorization%20request). Keep credentials and confidential information out of public requests. The English LICENSE text governs; this paragraph is a summary.

Third-party components retain their own licenses. In particular, the bundled FRP engine remains under Apache License 2.0; see [third-party notices](THIRD-PARTY-NOTICES.md).
