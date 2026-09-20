"""LocalAdmin 面板端到端冒烟测试。

配合 FakeGame/（一个会说官方控制台协议的假游戏进程），验证：
  帧编解码 / 颜色码映射 / 多行负载 / 心跳状态机 / 退出动作协商 /
  重启限流 / 控制台命令往返 / 审计记录

用法（通常由 run-smoke.sh 调用）：
    SMOKE_BASE=http://127.0.0.1:8099 SMOKE_LOG=/path/bot.log python smoke-test.py

密码既可显式给 SMOKE_PASSWORD，也可只给 SMOKE_LOG 由脚本自行从启动日志里解析
（避免中文经过命令行参数，规避 Windows 上的 argv 编码问题）。
"""
import json
import os
import re
import sys
import time
import urllib.error
import urllib.request

BASE = os.environ.get("SMOKE_BASE", "http://127.0.0.1:8099")
USER = os.environ.get("SMOKE_USER", "admin")
PWD = os.environ.get("SMOKE_PASSWORD", "")
LOG = os.environ.get("SMOKE_LOG", "")
SID = os.environ.get("SMOKE_ID", "demo")

TOKEN = None
failures = []
checks = 0


def password_from_log(path):
    """从机器人启动日志里解析内置管理员密码。

    注意：.NET 控制台重定向到文件时用的是系统 ANSI 代码页（中文 Windows 上是 GBK），
    并非 UTF-8，所以这里在**字节层面**做纯 ASCII 匹配，避开编码差异。
    公告板形如：
        用户名   : admin
        密　码   : xxxxxxxxxxxxxxxx
    """
    if not path or not os.path.exists(path):
        return ""

    raw = open(path, "rb").read()

    m = re.search(rb":\s*admin\s*\r?\n[^\r\n]*?:\s*([A-Za-z0-9]{8,32})", raw)
    if m:
        return m.group(1).decode("ascii")

    m = re.search(rb"admin\s*\r?\n[^\r\n]*?([A-Za-z0-9]{16})\s*\r?\n", raw)
    return m.group(1).decode("ascii") if m else ""


if not PWD:
    PWD = password_from_log(LOG)


def call(method, path, body=None):
    req = urllib.request.Request(
        BASE + path,
        data=json.dumps(body).encode() if body is not None else None,
        method=method,
    )
    req.add_header("Content-Type", "application/json")
    if TOKEN:
        req.add_header("Authorization", "Bearer " + TOKEN)
    try:
        with urllib.request.urlopen(req, timeout=30) as r:
            text = r.read().decode("utf-8")
            return r.status, (json.loads(text) if text else None)
    except urllib.error.HTTPError as e:
        text = e.read().decode("utf-8")
        return e.code, (json.loads(text) if text else None)


def check(label, ok, detail=""):
    global checks
    checks += 1
    print(f"  [{'PASS' if ok else 'FAIL'}] {label}" + (f"  -> {detail}" if detail else ""))
    if not ok:
        failures.append(label)
    return ok


def snapshot(limit=1000):
    """取一份包含全部缓冲的完整快照（每次调用都重新拉取，避免用到旧数据）。"""
    _, d = call("GET", f"/api/local/servers/{SID}/poll?after=0&limit={limit}")
    return d


def status():
    return snapshot()["status"]


def find(lines, sub, kind=None):
    for l in lines:
        if sub in l["text"] and (kind is None or l["kind"] == kind):
            return l
    return None


def wait_for(predicate, timeout=40, interval=0.5, desc=""):
    deadline = time.time() + timeout
    last = None
    while time.time() < deadline:
        last = snapshot()
        if predicate(last):
            return last
        time.sleep(interval)
    print(f"        （等待超时：{desc}）最后状态={json.dumps(last.get('status') if last else None, ensure_ascii=False)}")
    return last


def start_and_wait(desc="进程运行且控制台已连接", timeout=40):
    call("POST", f"/api/local/servers/{SID}/start", {})
    return wait_for(lambda d: d["status"]["running"] and d["status"]["consoleConnected"], timeout, 0.5, desc)


print("=" * 70)
print("1) 登录与能力探针")
print("=" * 70)
st, d = call("POST", "/api/auth/login", {"username": USER, "password": PWD})
if not check("登录成功", st == 200 and bool(d and d.get("token")), f"status={st}"):
    print("\n无法登录，测试中止。请确认 SMOKE_PASSWORD 或 SMOKE_LOG 是否正确。")
    sys.exit(2)
TOKEN = d["token"]

_, meta = call("GET", "/api/meta")
caps = (meta or {}).get("capabilities", {})
check("capabilities.localAdminGateway = true", caps.get("localAdminGateway") is True, str(caps))
check("capabilities.localServerCount = 1", caps.get("localServerCount") == 1, str(caps))

st, lst = call("GET", "/api/local/servers")
check("列出托管服务器 = 1 台", st == 200 and lst.get("total") == 1, str(lst.get("total")))
srv = (lst.get("servers") or [{}])[0]
check("初始状态为未运行", srv.get("running") is False, str(srv.get("running")))
check("实例名与游戏端口正确", srv.get("name") == "SmokeTest" and srv.get("gamePort") == 7799,
      f"{srv.get('name')}/{srv.get('gamePort')}")

print()
print("=" * 70)
print("2) 启动进程 + 上行帧解析（颜色码 / 多行 / 控制码 / stdout）")
print("=" * 70)
st, r = call("POST", f"/api/local/servers/{SID}/start", {})
check("启动指令返回成功", st == 200 and r.get("success") is True, json.dumps(r, ensure_ascii=False))

res = wait_for(lambda d: d["status"]["running"] and d["status"]["consoleConnected"], 40, 0.5, "进程运行且控制台已连接")
stt = res["status"]
check("进程已运行", stt["running"] is True)
check("控制台通道已连接", stt["consoleConnected"] is True)
check("拿到 PID", isinstance(stt["processId"], int) and stt["processId"] > 0, str(stt["processId"]))
check("监听到回环控制台端口", isinstance(stt["consolePort"], int) and stt["consolePort"] > 0, str(stt["consolePort"]))
check("游戏端口透传正确", stt["gamePort"] == 7799)

lines = snapshot()["lines"]

w = find(lines, "Welcome to SCP", "output")
check("code 15 → 白 (#e5e7eb)", w is not None and w["colorHex"] == "#e5e7eb", str(w and w["colorHex"]))
i = find(lines, "游戏端口 7799 |", "output")
check("code 11 → 青 (#22d3ee)", i is not None and i["colorHex"] == "#22d3ee", str(i and i["colorHex"]))
wa = find(lines, "这是一条测试警告", "output")
check("code 14 → 黄 (#f1c40f)", wa is not None and wa["colorHex"] == "#f1c40f", str(wa and wa["colorHex"]))
er = find(lines, "这是一条测试错误", "output")
check("code 12 → 红 (#e74c3c)", er is not None and er["colorHex"] == "#e74c3c", str(er and er["colorHex"]))

check("多行负载被正确拆成多行",
      all(find(lines, x, "output") for x in ("多行测试：第一行", "第二行", "第三行")))
check("控制码 0x10 RoundRestart 已解析并提示", find(lines, "回合重启", "system") is not None)
check("游戏 stdout 已重定向到面板", find(lines, "[STDOUT]", "stdout") is not None)

print()
print("=" * 70)
print("3) 下行帧往返 + 心跳状态机")
print("=" * 70)
st, r = call("POST", f"/api/local/servers/{SID}/console", {"command": "hello world"})
check("下发控制台命令成功", st == 200 and r.get("success") is True, json.dumps(r, ensure_ascii=False))

wait_for(lambda d: find(d["lines"], "[ECHO] hello world", "output") is not None, 12, 0.4, "回显出现")
lines = snapshot()["lines"]
check("命令被游戏接收并回显（下行 4 字节长度前缀生效）",
      find(lines, "[ECHO] hello world", "output") is not None)
check("下发内容已作为 input 行记录", find(lines, "> hello world", "input") is not None)

res = wait_for(lambda d: d["status"]["heartbeatStatus"] == "active", 15, 0.4, "心跳进入 active")
check("收到首个心跳后状态转为 active", res["status"]["heartbeatStatus"] == "active",
      res["status"]["heartbeatStatus"])
check("最近心跳时间已刷新", res["status"]["lastHeartbeatAt"] is not None)

call("POST", f"/api/local/servers/{SID}/console", {"command": "hbctrl status"})
time.sleep(1)
check("内置命令 hbctrl status 生效", find(snapshot()["lines"], "心跳状态：") is not None)

call("POST", f"/api/local/servers/{SID}/heartbeat", {"enabled": False})
time.sleep(0.8)
check("可关闭静默崩溃检测", status()["heartbeatStatus"] == "disabled", status()["heartbeatStatus"])
call("POST", f"/api/local/servers/{SID}/heartbeat", {"enabled": True})
time.sleep(0.8)
check("可重新开启（回到等待首个心跳）", status()["heartbeatStatus"] in ("awaiting", "active"),
      status()["heartbeatStatus"])

print()
print("=" * 70)
print("4) 退出动作协商 0x13：崩溃 → 自动重启（计入限流）")
print("=" * 70)
before = status()["restartsInWindow"]
r = call("POST", f"/api/local/servers/{SID}/console", {"command": "crash"})[1]
check("下发 crash 指令", r.get("success") is True)
res = wait_for(lambda d: d["status"]["running"] and d["status"]["restartsInWindow"] > before, 45, 0.5, "崩溃后自动重启")
check("崩溃后进程被自动拉起", res["status"]["running"] is True)
check("重启次数 +1（崩溃计入限流）", res["status"]["restartsInWindow"] == before + 1,
      f"{before} -> {res['status']['restartsInWindow']}")

print()
print("=" * 70)
print("5) 面板主动重启不计入限流")
print("=" * 70)
before = status()["restartsInWindow"]
st, r = call("POST", f"/api/local/servers/{SID}/restart", {"force": True})
check("强制重启指令下发", st == 200 and r.get("success") is True, json.dumps(r, ensure_ascii=False))
res = wait_for(lambda d: d["status"]["running"] and d["status"]["consoleConnected"], 45, 0.5, "重启完成")
check("重启后进程恢复运行", res["status"]["running"] is True)
check("面板主动重启不计入限流", res["status"]["restartsInWindow"] == before,
      f"{before} -> {res['status']['restartsInWindow']}")

print()
print("=" * 70)
print("6) 用户主动关服（exit）不被误判为崩溃")
print("=" * 70)
before = status()["restartsInWindow"]
r = call("POST", f"/api/local/servers/{SID}/console", {"command": "exit"})[1]
check("下发 exit", r.get("success") is True)
time.sleep(7)
d2 = status()
check("进程已退出", d2["running"] is False, str(d2["running"]))
check("未触发自动重启（退出信号被正确抑制）", d2["restartsInWindow"] == before,
      f"{before} -> {d2['restartsInWindow']}")

print()
print("=" * 70)
print("7) 崩溃循环保护：重启次数达到上限后停止自动拉起")
print("=" * 70)
start_and_wait()
exhausted = False
for n in range(8):
    d = status()
    if d["restartBudgetExhausted"]:
        exhausted = True
        break
    if not d["running"]:
        call("POST", f"/api/local/servers/{SID}/start", {})
        time.sleep(3)
    call("POST", f"/api/local/servers/{SID}/console", {"command": "crash"})
    time.sleep(2)
    res = wait_for(lambda x: x["status"]["restartBudgetExhausted"] or x["status"]["running"],
                   30, 0.5, f"第 {n + 1} 次崩溃后的状态")
    if res["status"].get("restartBudgetExhausted"):
        exhausted = True
        break
    time.sleep(1)

final = status()
check("达到上限后置位 restartBudgetExhausted", exhausted, str(final.get("restartBudgetExhausted")))
check("达到上限后不再运行（拒绝继续重启循环）", final["running"] is False, str(final["running"]))
check("重启计数已满", final["restartsInWindow"] >= final["restartLimit"],
      f"{final['restartsInWindow']}/{final['restartLimit']}")

print()
print("=" * 70)
print("8) 优雅停止 + 审计日志")
print("=" * 70)
start_and_wait()
st, r = call("POST", f"/api/local/servers/{SID}/stop", {"force": False})
check("优雅停止指令下发", st == 200 and r.get("success") is True, json.dumps(r, ensure_ascii=False))
res = wait_for(lambda d: not d["status"]["running"], 30, 0.5, "进程退出")
check("进程已停止", res["status"]["running"] is False)
check("停止后未自动重启", res["status"]["restartsInWindow"] == status()["restartsInWindow"])

audit = call("GET", "/api/audit?limit=300")[1] or []
actions = [e["action"] for e in audit]
for want in ("localadmin.start", "localadmin.stop", "localadmin.restart",
             "localadmin.console", "localadmin.heartbeat"):
    check(f"审计含 {want}", want in actions)

print()
print("=" * 70)
print(f"结果：{checks - len(failures)}/{checks} 通过")
if failures:
    print("失败项：")
    for f in failures:
        print("   -", f)
print("=" * 70)
sys.exit(1 if failures else 0)
