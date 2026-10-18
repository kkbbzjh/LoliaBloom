# 🌸 Lolia Bloom（洛链绽放）

采用粉色主题的 洛链映射（Lolia FRP）Windows 桌面隧道管理客户端。

[English](README.md) | [简体中文](README.zh-CN.md)

![Lolia Bloom Windows 界面（示例隧道）](https://github.com/kkbbzjh/LoliaBloom/releases/download/v0.6.1/LoliaBloom-screenshot.png)

![创建隧道界面（示例节点）](https://github.com/kkbbzjh/LoliaBloom/releases/download/v0.6.1/LoliaBloom-create.png)

## 功能特色

- **精致工作台**：自定义标题栏、节点卡片、名称与地区筛选、在线状态筛选，以及访问地址一键复制。

- **FRP 独立更新**：获取官方版本、校验 SHA256、恢复运行中的隧道，失败自动回退。

- **云端创建隧道**：选择节点，创建 TCP、UDP、HTTP、HTTPS 隧道。
- **记住登录**：点击登录即可跳转官方授权，支持 OAuth PKCE、Windows 加密保存与自动刷新令牌。
- **本地实时速率**：Windows ETW 按 FRP 进程统计，每秒刷新；排除回环及业务端流量，包含协议开销。平台累计流量单独展示。
- **官网签到**：一键打开官方控制台签到，查看今日签到状态。

- **粉色管理界面**：清晰展示隧道列表，直接启动或停止单条隧道。
- **隧道详情**：查看协议、节点、本地地址、远程地址和连接状态。
- **独立后台服务**：关闭程序窗口后，隧道进程仍可继续运行。
- **一键安装服务**：在程序内申请管理员权限，安装或卸载 Windows Service。
- **开机自动连接**：每条隧道独立胶囊开关，同步和重启后保留设置；开机网络未就绪时重试，手动停止后取消本轮重试。
- **Windows x64 单文件版**：双击一个 EXE，自动准备运行环境；同时提供 ZIP 版。

[下载 Windows x64 绿色版](https://github.com/kkbbzjh/LoliaBloom/releases/latest)

本项目为社区第三方客户端，与 洛链映射官方无隶属关系。
