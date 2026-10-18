#!/bin/sh
set -eu
command -v docker >/dev/null 2>&1 || { echo '请先安装 Docker Engine。'; exit 1; }
docker compose version >/dev/null 2>&1 || { echo '请先安装 Docker Compose v2。'; exit 1; }
command -v curl >/dev/null 2>&1 || { echo '请先安装 curl。'; exit 1; }
umask 077
install_dir=${LOLIABLOOM_INSTALL_DIR:-"$HOME/loliabloom"}
mkdir -p "$install_dir"
cd "$install_dir"
curl -fsSL https://raw.githubusercontent.com/kkbbzjh/LoliaBloom/main/compose.yaml -o compose.yaml.new
mv compose.yaml.new compose.yaml
if [ ! -f .env ]; then
  password=$(od -An -N24 -tx1 /dev/urandom | tr -d ' \n')
  host_ip=$(hostname -I 2>/dev/null | awk '{print $1}')
  host_ip=${host_ip:-localhost}
  public_url=${LOLIABLOOM_PUBLIC_URL:-"http://$host_ip:8080"}
  printf 'LOLIABLOOM_ADMIN_PASSWORD=%s\nLOLIABLOOM_PUBLIC_URL=%s\n' "$password" "$public_url" > .env
fi
docker compose pull
docker compose up -d
ready=false
for attempt in $(seq 1 30); do
  status=$(docker inspect --format '{{.State.Health.Status}}' loliabloom 2>/dev/null || true)
  if [ "$status" = healthy ]; then ready=true; break; fi
  sleep 2
done
if [ "$ready" != true ]; then docker compose logs --tail=80; echo '启动检查未通过，请检查上述日志。'; exit 1; fi
printf '\n洛链绽放已启动，配置目录：%s\n' "$install_dir"
sed -n 's/^LOLIABLOOM_PUBLIC_URL=/访问地址：/p; s/^LOLIABLOOM_ADMIN_PASSWORD=/管理密码：/p' .env
printf '更新：重新执行本安装命令；密码与数据会保留。\n'
