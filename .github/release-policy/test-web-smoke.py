#!/usr/bin/env python3
"""Tests for web-smoke.py, web mode's release smoke test (v0.3.0's PR 12, D102). Linux only; standard library only.

  python3 -I .github/release-policy/test-web-smoke.py            (test-release-policy.sh runs it)
  python3 -I .github/release-policy/test-web-smoke.py --write-fake-app <folder>     (a fake release folder and a stub docker
  python3 -I .github/release-policy/test-web-smoke.py --write-fake-docker <folder>   for the step tests of test-release-policy.sh)

The smoke test runs against a fake CampertronConsole: a small Python program that behaves as web mode does (its lines, its
answers, SIGTERM) unless FAKE_WEB names faults, one per test. Container mode runs against a stub docker that starts the same
fake. Nothing here leaves this computer: every listener is on 127.0.0.1.
"""

import contextlib
import hashlib
import importlib.util
import io
import json
import os
import shutil
import signal
import socket
import stat
import subprocess
import sys
import tempfile
import textwrap
import time
import unittest
from unittest import mock

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
ICON = os.path.join(ROOT, "assets", "icon", "campertron.ico")
CODE = "WXYZ-2345-6789"
VERSION = "0.0.0-dryrun.7"


def load_harness():
    spec = importlib.util.spec_from_file_location("web_smoke", os.path.join(HERE, "web-smoke.py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


smoke = load_harness()

FAKE_APP = r'''
import hashlib, http.server, json, os, signal, socket, subprocess, sys, threading, time, urllib.request

faults = set(filter(None, os.environ.get("FAKE_WEB", "").split(",")))
state = os.environ.get("FAKE_STATE", "")
icon = open(os.environ["FAKE_ICON"], "rb").read()
css = b"body { color: black; }\n"
container = os.environ.get("FAKE_CONTAINER") == "1"
version = "9.9.9" if "wrong-version" in faults else os.environ.get("FAKE_VERSION", "0.0.0")
CSP = "default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self'; font-src 'self'; connect-src 'self'; form-action 'self'; base-uri 'none'; frame-ancestors 'none'"
LISTEN_PROBLEM = "Error: --listen needs one IP address, such as 127.0.0.1 (this computer only) or 0.0.0.0 (every network interface), or the word localhost."
USAGE = "Usage: CampertronConsole --web [--listen <address>] [--port <port>]   (the web UI; --healthcheck with the same options asks a running one)"

def note(name, value):
    if state:
        with open(os.path.join(state, name), "w") as f:
            f.write(str(value))

def line(text):
    sys.stdout.write(time.strftime("%Y-%m-%d %H:%M:%S") + "  " + text + "\n")
    sys.stdout.flush()

args = sys.argv[1:]
def value(name):
    return args[args.index(name) + 1] if name in args else None

listen, port = value("--listen"), int(value("--port") or 8790)
if listen is not None and listen not in ("127.0.0.1", "::1", "0.0.0.0"):
    if "create-on-refusal" in faults:
        os.makedirs(os.path.join(os.environ["XDG_DATA_HOME"], "CampertronConfig"))
    if "accept-bad-listen" in faults:
        sys.exit(0)
    sys.stderr.write(LISTEN_PROBLEM + "\n  " + USAGE + "\n")
    sys.exit(1)
if "--healthcheck" in args:
    try:
        host = "[%s]" % listen if listen and ":" in listen else (listen or "127.0.0.1")
        with urllib.request.build_opener(urllib.request.ProxyHandler({})).open("http://%s:%d/healthz" % (host, port), timeout=5) as r:
            ok = r.status == 200 and r.read() == b"ok" and "healthcheck-broken" not in faults
    except Exception:
        ok = False
    print("Campertron's web UI answers." if ok else "Campertron's web UI does not answer.")
    sys.exit(0 if ok else 1)

note("pid", os.getpid())
config = os.environ["FAKE_CONFIG"] if container else os.path.join(os.environ["XDG_DATA_HOME"], "CampertronConfig")
cache = os.environ["FAKE_CACHE"] if container else os.path.join(os.environ["XDG_DATA_HOME"], "CampertronCache")
os.makedirs(config, exist_ok=True)
os.makedirs(cache, exist_ok=True)
shown = "/config" if container else ("/elsewhere/CampertronConfig" if "other-settings" in faults else config)
line("CampertronConsole %s+abc123 | .NET 11.0.0 (X64) | settings %s | web UI" % (version, shown))
if "exit-early" in faults:
    sys.exit(3)
if "bind-fail" in faults or ("bind-fail-once" in faults and not os.path.exists(os.path.join(state, "bound-once"))):
    note("bound-once", 1)
    sys.stderr.write("Error: web mode can't start: it can't listen on port %d at the address it was given.\n" % port)
    sys.exit(1)
keys = os.path.join(config, "Web.keys")
os.makedirs(keys, mode=0o700, exist_ok=True)
for name in ("instance", "key-1.xml"):
    with open(os.path.join(keys, name), "w") as f:
        f.write("x")
    os.chmod(os.path.join(keys, name), 0o600)
if "extra-file" in faults:
    open(os.path.join(config, "General.yaml"), "w").close()
if "home-file" in faults:
    os.makedirs(os.path.join(os.environ["HOME"], ".aspnet"))
if "never-ready" in faults:
    signal.signal(signal.SIGTERM, lambda *a: sys.exit(0))
    time.sleep(600)

version_of = lambda data: hashlib.sha256(data).hexdigest()[:16]
served_icon = b"\x00\x00\x01\x00\x01\x00other" if "favicon-other" in faults else (b"" if "favicon-empty" in faults else icon)
page = ('<!DOCTYPE html>\n<html lang="en">\n<head>\n<title>%s</title>\n<link rel="stylesheet" href="/assets/site.css?v=%s" />\n'
        '%s<link rel="icon" href="/assets/campertron.ico?v=%s" />\n</head>\n<body><a href="#main">Skip</a>\n'
        '<p>12 letters and digits, such as ABCD-EFGH-JKMN.</p>%s\n<form method="post" action="/setup"></form>\n</body>\n</html>\n') % (
    "Something else" if "not-compiled" in faults else "Set the password – Campertron", version_of(css),
    '<script src="https://cdn.example.net/x.js"></script>\n' if "external-asset" in faults else "",
    version_of(icon), ("<p>" + os.environ["FAKE_CODE"] + "</p>") if "page-shows-code" in faults else "")

class Handler(http.server.BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"
    def log_message(self, *a):
        pass
    def send_response(self, code, message=None):
        self.send_response_only(code, message)
        self.send_header("Date", self.date_time_string())
        if "server-header" in faults:
            self.send_header("Server", "Kestrel")
    def answer(self, code, body=b"", kind=None, headers=(), cache="no-store"):
        self.send_response(code)
        if "no-csp" not in faults:
            self.send_header("Content-Security-Policy", CSP.replace("'none'", "*", 1) if "weak-csp" in faults else CSP)
        if "no-nosniff" not in faults:
            self.send_header("X-Content-Type-Options", "nosniff")
        self.send_header("X-Frame-Options", "DENY")
        self.send_header("Referrer-Policy", "same-origin")
        if cache:
            self.send_header("Cache-Control", cache)
        if kind:
            self.send_header("Content-Type", kind)
        for k, v in headers:
            self.send_header(k, v)
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Connection", "close")
        self.end_headers()
        if self.command != "HEAD":
            self.wfile.write(body)
    def route(self):
        host = self.headers.get("Host", "")
        if "no-host-check" not in faults and not (host.startswith("127.0.0.1:") or host.startswith("[::1]:")):
            return self.answer(400, b"<p>host</p>", "text/html; charset=utf-8")
        path = self.path
        if self.command == "POST":
            if self.headers.get("Origin") != "http://" + host:
                return self.answer(403, b"<p>origin</p>", "text/html; charset=utf-8")
            if "post-exposed" in faults:
                return self.answer(200, b"<p>started</p>", "text/html; charset=utf-8")
            return self.answer(302, headers=[("Location", "/setup")])
        if path == "/healthz":
            if "health-hang" in faults:
                time.sleep(30)
            return self.answer(200, b"nope" if "health-wrong" in faults else b"ok", "text/html" if "health-type" in faults else "text/plain; charset=utf-8")
        if path == "/favicon.ico" or path == "/assets/campertron.ico?v=" + version_of(icon):
            if "no-favicon" in faults and path == "/favicon.ico":
                return self.answer(404, b"<p>no</p>", "text/html; charset=utf-8")
            kind = "image/png" if "favicon-type" in faults else "image/x-icon"
            return self.answer(200, served_icon, kind, cache="no-cache" if path == "/favicon.ico" else "public, max-age=31536000, immutable")
        if path == "/assets/site.css?v=" + version_of(css):
            return self.answer(200, css, "text/css; charset=utf-8", cache="public, max-age=31536000, immutable")
        if path.startswith("/assets/"):
            if "file-served" in faults and path == "/assets/Web.password":
                return self.answer(200, b"$pbkdf2", "application/octet-stream")
            return self.answer(404, b"<p>no</p>", "text/html; charset=utf-8")
        if path == "/setup":
            if "page-hang" in faults:
                time.sleep(30)
            if "dies-during-checks" in faults:
                os._exit(5)
            return self.answer(200, page.encode(), "text/html; charset=utf-8", cache="no-cache, no-store")
        if path == "/" and "exposed" in faults:
            return self.answer(200, b"<h1>Start</h1>", "text/html; charset=utf-8")
        return self.answer(302, headers=[("Location", "/setup")])
    do_GET = do_HEAD = do_POST = route
    def do_PUT(self):
        self.answer(405, b"<p>method</p>", "text/html; charset=utf-8", headers=[("Allow", "GET, HEAD, POST")])
    do_DELETE = do_PUT

class Server(http.server.ThreadingHTTPServer):
    address_family = socket.AF_INET6 if listen == "::1" else socket.AF_INET
    daemon_threads = True

bind = os.environ.get("FAKE_BIND_PORT")
server = Server((listen or "127.0.0.1", int(bind) if bind else port), Handler)
threading.Thread(target=server.serve_forever, daemon=True).start()
if "listen-any" in faults:
    line("Web UI: listening on http://0.0.0.0:%d (every network interface of this computer); open http://<this computer's address>:%d/ in a browser. A password is required." % (port, port))
elif container:
    line("Web UI: listening on http://[::]:8790 (every network interface of this computer); open http://<this computer's address>:8790/ in a browser. A password is required.")
else:
    shown_listen = "[::1]" if listen == "::1" else listen
    line("Web UI: listening on http://%s:%d (this computer only); open http://%s:%d/ in a browser. A password is required." % (shown_listen, port, shown_listen, port))
if "aspnet-log" in faults:
    sys.stdout.write("info: Microsoft.Hosting.Lifetime[14]\n      Now listening on: http://127.0.0.1:%d\n" % port)
if "stderr-warning" in faults:
    sys.stderr.write("Warning: something\n")
if "stack-trace" in faults:
    sys.stderr.write("Unhandled exception. System.InvalidOperationException: x\n   at Campertron.Web.Run()\n")
if "chatty" in faults:
    for i in range(200000):
        sys.stdout.write("chatter %d ..........................\n" % i)
if "no-setup-code" not in faults:
    line("Web UI: no password is set yet. Enter this one-time setup code in the browser to set it (valid for 30 minutes, for one use):")
    line("Web UI setup code: >>> %s <<<" % os.environ["FAKE_CODE"])
if "child" in faults:
    child = subprocess.Popen([sys.executable, "-c", "import signal,time; signal.signal(signal.SIGTERM, signal.SIG_IGN); time.sleep(120)"])
    note("child", child.pid)

stopping = threading.Event()
def stop(*_):
    stopping.set()
if "ignore-term" in faults:
    signal.signal(signal.SIGTERM, signal.SIG_IGN)
else:
    signal.signal(signal.SIGTERM, stop)
while not stopping.wait(0.1):
    pass
if "child-holds-port" in faults or "detached-holds-port" in faults:
    holder = subprocess.Popen([sys.executable, "-c", "import time; time.sleep(120)"], pass_fds=[server.socket.fileno()],
                              start_new_session="detached-holds-port" in faults)
    note("child" if "child-holds-port" in faults else "detached", holder.pid)
server.shutdown()
if "extra-stop-line" in faults:
    line("Web UI: something else.")
if "no-stopped-line" not in faults:
    line("Web UI: stopped.")
sys.exit(3 if "stop-exit-3" in faults else 0)
'''

FAKE_DOCKER = r'''
import json, os, signal, socket, subprocess, sys, time
state = os.environ["FAKE_DOCKER_STATE"]
faults = set(filter(None, os.environ.get("FAKE_DOCKER", "").split(",")))
with open(os.path.join(state, "calls.jsonl"), "a") as log:
    log.write(json.dumps(sys.argv[1:]) + "\n")
args = sys.argv[1:]
def path(name):
    return os.path.join(state, name + ".json")
def load(name):
    try:
        return json.load(open(path(name)))
    except FileNotFoundError:
        return None
def alive(pid):
    try:
        os.kill(pid, 0)
        return subprocess.run(["ps", "-o", "stat=", "-p", str(pid)], capture_output=True, text=True).stdout.strip()[:1] not in ("Z", "")
    except ProcessLookupError:
        return False
if args[:2] == ["image", "inspect"]:
    if args[-1] != os.environ.get("FAKE_DOCKER_IMAGE", "campertron-smoke:local"):
        sys.exit(1)
    print("sha256:" + "0" * 64)
elif args[0] == "run" and "--detach" in args:
    name = args[args.index("--name") + 1]
    mounts = [args[i + 1] for i, a in enumerate(args) if a == "--mount"]
    source = {m.split(",")[2].split("=")[1]: m.split(",")[1].split("=")[1] for m in mounts}
    with socket.socket() as s:
        s.bind(("127.0.0.1", 0))
        port = s.getsockname()[1]
    out, err = os.path.join(state, name + ".out"), os.path.join(state, name + ".err")
    env = dict(os.environ, FAKE_CONTAINER="1", FAKE_CONFIG=source["/config"], FAKE_CACHE=source["/cache"], FAKE_BIND_PORT=str(port))
    with open(out, "wb") as o, open(err, "wb") as e:
        p = subprocess.Popen([os.environ["FAKE_APP"], "--web"], stdout=o, stderr=e, env=env, start_new_session=True)
    json.dump({"pid": p.pid, "port": port, "out": out, "err": err, "exit": None, "publish": args[args.index("--publish") + 1]}, open(path(name), "w"))
    print("f" * 64)
elif args[0] == "run" and "--rm" in args:
    mounts = [args[i + 1] for i, a in enumerate(args) if a == "--mount"]
    for m in mounts:
        source = m.split(",")[1].split("=")[1]
        for entry in os.listdir(source):
            full = os.path.join(source, entry)
            subprocess.run(["rm", "-rf", "--", full])
elif args[0] == "port":
    c = load(args[1])
    print(("0.0.0.0:%d\n[::]:%d" if "publish-all" in faults else "127.0.0.1:%d") % ((c["port"],) * (2 if "publish-all" in faults else 1)))
elif args[:2] == ["container", "inspect"]:
    c = load(args[-1])
    if c is None:
        sys.exit(1)
    fmt = args[args.index("--format") + 1] if "--format" in args else None
    running = alive(c["pid"])
    if fmt is None:
        print("[{}]")
    elif "PortBindings" in fmt:
        print(json.dumps({"8790/tcp": [{"HostIp": "0.0.0.0" if "publish-all" in faults else "127.0.0.1", "HostPort": str(c["port"])}]}))
    elif "ExitCode" in fmt:
        print("%s %s false" % ("true" if running else "false", c["exit"] if c["exit"] is not None else 0))
    else:
        print("true" if running else "false")
elif args[0] == "logs":
    c = load(args[1])
    sys.stdout.write(open(c["out"]).read())
    sys.stderr.write(open(c["err"]).read())
elif args[0] == "exec":
    c = load(args[1])
    if "health-broken" in faults:
        print("Campertron's web UI does not answer.")
        sys.exit(1)
    sys.exit(subprocess.run([os.environ["FAKE_APP"], "--healthcheck", "--listen", "127.0.0.1", "--port", str(c["port"])]).returncode)
elif args[0] == "stop":
    name = args[-1]
    c = load(name)
    os.kill(c["pid"], signal.SIGTERM)
    deadline = time.time() + int(args[args.index("--time") + 1])
    while time.time() < deadline and alive(c["pid"]):
        time.sleep(0.05)
    if alive(c["pid"]):
        os.killpg(c["pid"], signal.SIGKILL)
        c["exit"] = 137
    else:
        try:
            c["exit"] = os.waitpid(c["pid"], 0)[1] >> 8
        except ChildProcessError:
            c["exit"] = 3 if "stop-exit-3" in os.environ.get("FAKE_WEB", "") else 0
    json.dump(c, open(path(name), "w"))
elif args[0] == "rm":
    name = args[-1]
    c = load(name)
    if c is not None:
        try:
            os.killpg(c["pid"], signal.SIGKILL)
        except ProcessLookupError:
            pass
        os.remove(path(name))
else:
    sys.exit(99)
'''


def write_executable(path, source):
    with open(path, "w") as file:
        file.write("#!" + sys.executable + " -I\n" + source)
    os.chmod(path, 0o755)


def write_fake_release(folder):
    """A folder like an extracted console archive: CampertronConsole (the fake) and the files the smoke test looks for."""
    os.makedirs(folder, exist_ok=True)
    app = os.path.join(folder, "CampertronConsole")
    write_executable(app, FAKE_APP)
    for name in ("CampertronWeb.dll", "CampertronLibrary.dll"):
        open(os.path.join(folder, name), "wb").close()
    return app


def alive(pid):
    try:
        os.kill(pid, 0)
    except ProcessLookupError:
        return False
    status = subprocess.run(["ps", "-o", "stat=", "-p", str(pid)], capture_output=True, text=True).stdout.strip()
    return bool(status) and not status.startswith("Z")


class Base(unittest.TestCase):
    def setUp(self):
        self.root = tempfile.mkdtemp(prefix="test-web-smoke-")
        self.addCleanup(shutil.rmtree, self.root, ignore_errors=True)
        self.temp = os.path.join(self.root, "tmp")
        self.state = os.path.join(self.root, "state")
        os.makedirs(self.temp)
        os.makedirs(self.state)
        self.app = write_fake_release(os.path.join(self.root, "release folder; touch INJECTED $(touch INJECTED2)"))
        patcher = mock.patch.object(tempfile, "tempdir", self.temp)
        patcher.start()
        self.addCleanup(patcher.stop)
        self.env = {"FAKE_ICON": ICON, "FAKE_STATE": self.state, "FAKE_CODE": CODE, "FAKE_VERSION": VERSION, "FAKE_WEB": ""}

    def run_smoke(self, *argv, faults="", env=None, start_timeout="20", stop_timeout="5"):
        """The smoke test in this process: its exit code and output."""
        variables = dict(self.env, FAKE_WEB=faults, **(env or {}))
        if argv and argv[0] in ("native", "container"):
            for option, value in (("--start-timeout", start_timeout), ("--stop-timeout", stop_timeout)):
                if option not in argv:
                    argv = argv + (option, value)
        out, err = io.StringIO(), io.StringIO()
        with mock.patch.dict(os.environ, variables), contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
            code = smoke.main(list(argv), signals=False)
        self.output = out.getvalue() + err.getvalue()
        return code

    def native(self, faults="", **options):
        return self.run_smoke("native", "--app", self.app, "--version", VERSION, "--icon", ICON, faults=faults, **options)

    def assertFailsWith(self, faults, text, **options):
        code = self.native(faults, **options)
        self.assertEqual(code, smoke.EXIT_FAILED, self.output)
        self.assertIn(text, self.output)
        self.assertCleanedUp()

    def pid(self, name="pid"):
        with open(os.path.join(self.state, name)) as file:
            return int(file.read())

    def assertCleanedUp(self):
        """No process of the fake left, no temporary folder left, the setup code nowhere in the output."""
        for name in ("pid", "child"):
            if os.path.exists(os.path.join(self.state, name)):
                self.assertFalse(alive(self.pid(name)), name + " is still running")
        self.assertEqual(os.listdir(self.temp), [], "the smoke test left a temporary folder")
        self.assertNotIn(CODE, self.output)
        self.assertFalse(os.path.exists(os.path.join(self.root, "INJECTED")))
        self.assertFalse(os.path.exists("INJECTED2"))


class InputSafety(Base):
    def refused(self, *argv):
        code = self.run_smoke(*argv)
        self.assertEqual(code, smoke.EXIT_REFUSED, self.output)
        self.assertEqual(os.listdir(self.temp), [])
        self.assertFalse(os.path.exists(os.path.join(self.state, "pid")), "the program was started")

    def test_a_missing_app_is_refused(self):
        self.refused("native", "--app", os.path.join(self.root, "nothing", "CampertronConsole"), "--version", VERSION, "--icon", ICON)

    def test_a_relative_app_is_refused(self):
        self.refused("native", "--app", "CampertronConsole", "--version", VERSION, "--icon", ICON)

    def test_another_program_is_refused(self):
        other = os.path.join(os.path.dirname(self.app), "CampertronGUI")
        shutil.copy(self.app, other)
        self.refused("native", "--app", other, "--version", VERSION, "--icon", ICON)

    def test_a_windows_executable_is_not_run_on_linux(self):
        exe = self.app + ".exe"
        shutil.copy(self.app, exe)
        self.refused("native", "--app", exe, "--version", VERSION, "--icon", ICON)

    def test_an_app_that_cant_be_executed_is_refused(self):
        os.chmod(self.app, 0o644)
        self.refused("native", "--app", self.app, "--version", VERSION, "--icon", ICON)

    def test_a_folder_without_the_web_ui_is_refused(self):
        os.remove(os.path.join(os.path.dirname(self.app), "CampertronWeb.dll"))
        self.refused("native", "--app", self.app, "--version", VERSION, "--icon", ICON)

    def test_an_address_beyond_loopback_is_refused(self):
        for address in ("0.0.0.0", "::", "localhost", "192.168.1.2", "example.com", "127.0.0.1 --listen 0.0.0.0"):
            with self.subTest(address=address):
                self.refused("native", "--app", self.app, "--version", VERSION, "--icon", ICON, "--listen", address)

    def test_an_invalid_port_is_refused(self):
        for port in ("-1", "80", "1023", "65536", "x", "8790;id"):
            with self.subTest(port=port):
                self.refused("native", "--app", self.app, "--version", VERSION, "--icon", ICON, "--port", port)

    def test_extra_or_injected_arguments_are_refused(self):
        self.refused("native", "--app", self.app, "--version", VERSION, "--icon", ICON, "--", "--listen", "0.0.0.0")
        self.refused("native", "--app", self.app, "--version", VERSION, "--icon", ICON, "--extra")
        self.refused("native", "--app", self.app, "--version", VERSION, "--icon", ICON, "--lis", "0.0.0.0")
        self.refused("native", "--app", self.app, "--version", "1.0; rm -rf /", "--icon", ICON)
        self.refused("native", "--app", "--web", "--version", VERSION, "--icon", ICON)

    def test_unbounded_timeouts_are_refused(self):
        for value in ("0", "-5", "601", "inf", "nan"):
            with self.subTest(value=value):
                self.refused("native", "--app", self.app, "--version", VERSION, "--icon", ICON, "--start-timeout", value)
                self.refused("native", "--app", self.app, "--version", VERSION, "--icon", ICON, "--stop-timeout", value)

    def test_an_icon_that_is_not_an_icon_is_refused(self):
        other = os.path.join(self.root, "x.ico")
        with open(other, "wb") as file:
            file.write(b"\x89PNG....")
        self.refused("native", "--app", self.app, "--version", VERSION, "--icon", other)

    def test_an_unsupported_platform_is_refused(self):
        with mock.patch.object(smoke, "PLATFORM", "freebsd14"):
            self.refused("native", "--app", self.app, "--version", VERSION, "--icon", ICON)

    def test_windows_and_macos_run_only_on_a_fresh_hosted_runner(self):
        home = os.path.join(self.root, "home")
        support = os.path.join(home, "Library", "Application Support")
        os.makedirs(support)
        with mock.patch.object(smoke, "PLATFORM", "darwin"), mock.patch.dict(os.environ, {"HOME": home}):
            with mock.patch.dict(os.environ, {"GITHUB_ACTIONS": "", "RUNNER_ENVIRONMENT": ""}):
                self.refused("native", "--app", self.app, "--version", VERSION, "--icon", ICON)
            with mock.patch.dict(os.environ, {"GITHUB_ACTIONS": "true", "RUNNER_ENVIRONMENT": "self-hosted"}):
                self.refused("native", "--app", self.app, "--version", VERSION, "--icon", ICON)
            os.makedirs(os.path.join(support, "CampertronConfig"))
            with mock.patch.dict(os.environ, {"GITHUB_ACTIONS": "true", "RUNNER_ENVIRONMENT": "github-hosted"}):
                self.refused("native", "--app", self.app, "--version", VERSION, "--icon", ICON)
            self.assertEqual(os.listdir(support), ["CampertronConfig"], "a folder that existed before was touched")

    def test_container_mode_refuses_an_image_name_that_is_an_option(self):
        self.refused("container", "--image", "--privileged", "--version", VERSION, "--icon", ICON)
        self.refused("container", "--image", "Image With Spaces", "--version", VERSION, "--icon", ICON)


class Startup(Base):
    def test_a_good_web_mode_passes_and_leaves_nothing_behind(self):
        unrelated_file = os.path.join(self.temp, "..", "unrelated.txt")
        with open(unrelated_file, "w") as file:
            file.write("keep")
        unrelated = subprocess.Popen([sys.executable, "-c", "import time; time.sleep(60)"])
        self.addCleanup(unrelated.kill)
        code = self.native()
        self.assertEqual(code, smoke.EXIT_PASSED, self.output)
        for check in ("command line: ok", "start: ok", "--healthcheck: ok", "health answer: ok", "request checks: ok",
                      "password boundary: ok", "setup page: ok", "icon: ok", "stop: ok", "passed"):
            self.assertIn("web-smoke: " + check, self.output)
        self.assertCleanedUp()
        self.assertTrue(os.path.exists(unrelated_file))
        self.assertIsNone(unrelated.poll(), "an unrelated process was stopped")

    def test_the_ipv6_loopback_address_works_too(self):
        if not socket.has_ipv6:
            self.skipTest("no IPv6")
        code = self.run_smoke("native", "--app", self.app, "--version", VERSION, "--icon", ICON, "--listen", "::1")
        self.assertEqual(code, smoke.EXIT_PASSED, self.output)

    def test_a_program_that_ends_before_it_listens_fails(self):
        self.assertFailsWith("exit-early", "ended with exit code 3 before it listened")

    def test_a_taken_port_given_by_the_caller_fails_as_a_bind_failure(self):
        code = self.run_smoke("native", "--app", self.app, "--version", VERSION, "--icon", ICON, "--port", str(smoke.free_port("127.0.0.1")), faults="bind-fail")
        self.assertEqual(code, smoke.EXIT_FAILED, self.output)
        self.assertIn("bind failed", self.output)
        self.assertNotIn("trying another", self.output)
        self.assertCleanedUp()

    def test_a_port_taken_between_choice_and_start_is_chosen_again(self):
        code = self.native("bind-fail-once")
        self.assertEqual(code, smoke.EXIT_PASSED, self.output)
        self.assertIn("trying another", self.output)

    def test_a_port_that_stays_taken_fails_after_three_attempts(self):
        code = self.native("bind-fail")
        self.assertEqual(code, smoke.EXIT_FAILED, self.output)
        self.assertEqual(self.output.count("trying another"), 2)
        self.assertIn("bind failed", self.output)

    def test_a_start_that_never_says_where_it_listens_times_out(self):
        began = time.monotonic()
        self.assertFailsWith("never-ready", "did not say where it listens within 2 seconds", start_timeout="2")
        self.assertLess(time.monotonic() - began, 20)

    def test_no_setup_code_times_out(self):
        self.assertFailsWith("no-setup-code", "did not say where it listens within 2 seconds", start_timeout="2")

    def test_another_version_fails(self):
        self.assertFailsWith("wrong-version", "is not web mode's start line for CampertronConsole " + VERSION)

    def test_another_settings_folder_fails(self):
        self.assertFailsWith("other-settings", "did not use the smoke test's own settings folder")

    def test_listening_on_every_interface_fails(self):
        self.assertFailsWith("listen-any", "does not say that it listens where it was told to")

    def test_aspnet_core_logging_fails(self):
        self.assertFailsWith("aspnet-log", "ASP.NET Core wrote to the console")

    def test_standard_error_fails(self):
        self.assertFailsWith("stderr-warning", "wrote to standard error")

    def test_a_stack_trace_fails(self):
        self.assertFailsWith("stack-trace", "exception or a stack trace")

    def test_much_output_does_not_block_and_fails_at_once(self):
        began = time.monotonic()
        self.assertFailsWith("chatty", "wrote more than 256 KB to its console")
        self.assertLess(time.monotonic() - began, 15)

    def test_a_wrong_listen_that_is_accepted_fails(self):
        self.assertFailsWith("accept-bad-listen", "a wrong --listen was not refused")

    def test_a_refusal_that_creates_folders_fails(self):
        self.assertFailsWith("create-on-refusal", "created Campertron's folders")

    def test_the_setup_code_is_never_shown(self):
        code = self.native("no-favicon")
        self.assertEqual(code, smoke.EXIT_FAILED)
        self.assertIn(">>> [setup code removed] <<<", self.output)
        self.assertNotIn(CODE, self.output)
        self.assertNotIn(self.temp, self.output, "a temporary folder's path was shown")

    def test_redact_removes_codes_paths_and_control_characters(self):
        text = smoke.redact("a >>> ABCD-EFGH-JKMN <<< b QRST-VWXY-Z234 /tmp/x/y \x1b[31m", ["/tmp/x"])
        self.assertEqual(text, "a >>> [setup code removed] <<< b [setup code removed] <smoke folder>/y \\x1b[31m")


class HttpChecks(Base):
    def test_a_wrong_health_answer_fails(self):
        self.assertFailsWith("health-wrong", "--healthcheck against the running web UI did not succeed")

    def test_a_health_answer_of_another_type_fails(self):
        self.assertFailsWith("health-type", "GET /healthz is not text/plain")

    def test_a_health_check_that_fails_against_a_running_web_ui_fails(self):
        self.assertFailsWith("healthcheck-broken", "--healthcheck against the running web UI did not succeed")

    def test_a_health_answer_that_hangs_fails_the_health_check(self):
        self.assertFailsWith("health-hang", "--healthcheck against the running web UI did not succeed")

    def test_a_request_that_hangs_times_out(self):
        with mock.patch.object(smoke, "REQUEST_TIMEOUT", 1.0):
            self.assertFailsWith("page-hang", "GET /setup got no answer")

    def test_a_missing_favicon_fails(self):
        self.assertFailsWith("no-favicon", "GET /favicon.ico did not answer 200")

    def test_a_favicon_of_another_type_fails(self):
        self.assertFailsWith("favicon-type", "is not image/x-icon")

    def test_an_empty_favicon_fails(self):
        self.assertFailsWith("favicon-empty", "is empty")

    def test_another_icon_fails(self):
        self.assertFailsWith("favicon-other", "is not the approved campertron.ico")

    def test_a_missing_content_security_policy_fails(self):
        self.assertFailsWith("no-csp", "lacks the web UI's Content-Security-Policy")

    def test_a_weaker_content_security_policy_fails(self):
        self.assertFailsWith("weak-csp", "lacks the web UI's Content-Security-Policy")

    def test_a_missing_nosniff_fails(self):
        self.assertFailsWith("no-nosniff", "lacks X-Content-Type-Options")

    def test_a_server_header_fails(self):
        self.assertFailsWith("server-header", "names its server")

    def test_a_page_without_a_password_fails(self):
        self.assertFailsWith("exposed", "GET / without a password did not lead to the setup")

    def test_a_post_without_a_password_that_succeeds_fails(self):
        self.assertFailsWith("post-exposed", "a POST without a password was not led to the setup")

    def test_a_missing_host_check_fails(self):
        self.assertFailsWith("no-host-check", "another host name was not refused")

    def test_a_served_settings_file_fails(self):
        self.assertFailsWith("file-served", "GET /assets/Web.password was not refused")

    def test_an_external_address_fails(self):
        self.assertFailsWith("external-asset", "names an address outside the web UI")

    def test_a_page_that_is_not_the_compiled_page_fails(self):
        self.assertFailsWith("not-compiled", "is not the web UI's compiled page")

    def test_a_page_that_shows_the_setup_code_fails(self):
        self.assertFailsWith("page-shows-code", "the setup page shows the setup code")

    def test_a_program_that_dies_during_the_checks_fails(self):
        self.assertFailsWith("dies-during-checks", "got no answer")


class Cleanup(Base):
    def test_a_program_that_ignores_sigterm_is_stopped_by_force_and_fails(self):
        self.assertFailsWith("ignore-term", "did not stop within 2 seconds of SIGTERM", stop_timeout="2")
        self.assertIn("stopped by force", self.output)

    def test_a_remaining_child_process_fails_and_is_stopped(self):
        self.assertFailsWith("child", "a process of the web UI remained")

    def test_a_port_kept_open_by_a_child_fails_and_is_closed(self):
        self.assertFailsWith("child-holds-port", "a process of the web UI remained")

    def test_a_port_left_listening_by_another_process_fails(self):
        # a process outside the web UI's process group keeps its socket: the port check, not the process check, finds it
        try:
            self.assertFailsWith("detached-holds-port", "still listens after the web UI stopped")
        finally:
            os.kill(self.pid("detached"), signal.SIGKILL)

    def test_the_smoke_test_asks_no_other_computer(self):
        for address in ("192.0.2.1", "example.com", "0.0.0.0"):
            with self.subTest(address=address):
                with self.assertRaises(smoke.Failed):
                    smoke.request(address, 80, "GET", "/")
                with self.assertRaises(smoke.Failed):
                    smoke.port_open(address, 80)

    def test_another_exit_code_fails(self):
        self.assertFailsWith("stop-exit-3", "stopped with exit code 3, not 0")

    def test_a_missing_stopped_line_fails(self):
        self.assertFailsWith("no-stopped-line", "did not end with exactly one more line")

    def test_another_line_at_the_stop_fails(self):
        self.assertFailsWith("extra-stop-line", "did not end with exactly one more line")

    def test_a_settings_file_written_by_web_mode_fails(self):
        self.assertFailsWith("extra-file", "holds other files than the web UI's keys")

    def test_a_file_in_the_home_folder_fails(self):
        self.assertFailsWith("home-file", "wrote into its home folder")

    def test_a_cleanup_that_fails_fails_the_smoke_test(self):
        real = smoke.remove_tree

        def removes_then_fails(path):
            real(path)
            raise smoke.Failed("could not remove")
        with mock.patch.object(smoke, "remove_tree", removes_then_fails):
            code = self.native()
        self.assertEqual(code, smoke.EXIT_FAILED, self.output)
        self.assertIn("the cleanup failed", self.output)

    def test_a_canceled_smoke_test_stops_the_web_ui_and_removes_its_folders(self):
        env = dict(os.environ, TMPDIR=self.temp, **dict(self.env, FAKE_WEB="page-hang"))
        smoke_test = subprocess.Popen([sys.executable, "-I", os.path.join(HERE, "web-smoke.py"), "native", "--app", self.app,
                                       "--version", VERSION, "--icon", ICON], stdout=subprocess.PIPE, stderr=subprocess.STDOUT, env=env, text=True)
        lines = []
        for line in smoke_test.stdout:
            lines.append(line)
            if "password boundary: ok" in line:
                break
        smoke_test.send_signal(signal.SIGTERM)
        rest, _ = smoke_test.communicate(timeout=60)
        self.output = "".join(lines) + rest
        self.assertEqual(smoke_test.returncode, smoke.EXIT_FAILED, self.output)
        self.assertIn("was interrupted", self.output)
        self.assertCleanedUp()


class PlatformWrappers(Base):
    def test_windows_starts_a_process_group_and_sends_ctrl_break(self):
        started = {}

        class FakeProcess:
            pid, _handle, returncode = 4242, 77, None

            def poll(self):
                return self.returncode

        def popen(argv, **options):
            started.update(argv=argv, **options)
            return FakeProcess()
        folder = tempfile.mkdtemp(dir=self.root)
        with mock.patch.object(smoke, "WINDOWS", True), mock.patch.object(smoke, "PLATFORM", "win32"), \
                mock.patch.object(smoke.subprocess, "CREATE_NEW_PROCESS_GROUP", 0x200, create=True), \
                mock.patch.object(smoke.signal, "CTRL_BREAK_EVENT", 1, create=True), \
                mock.patch.object(smoke.subprocess, "Popen", popen), mock.patch.object(smoke, "WindowsJob") as job, \
                mock.patch.object(smoke.os, "kill") as kill:
            program = smoke.Program(["C:\\a b\\CampertronConsole.exe", "--web", "--listen", "127.0.0.1", "--port", "5000"], {}, folder)
            program.stop_gracefully()
        self.assertEqual(started["argv"], ["C:\\a b\\CampertronConsole.exe", "--web", "--listen", "127.0.0.1", "--port", "5000"])
        self.assertEqual(started["creationflags"], 0x200)
        self.assertNotIn("shell", started)
        self.assertNotIn("start_new_session", started)
        job.assert_called_once()
        kill.assert_called_once_with(4242, 1)

    def test_unix_starts_a_session_of_its_own_without_a_shell(self):
        started = {}

        class FakeProcess:
            pid, returncode = 4242, None

            def poll(self):
                return None

            def send_signal(self, number):
                started["signal"] = number

        def popen(argv, **options):
            started.update(argv=argv, **options)
            return FakeProcess()
        folder = tempfile.mkdtemp(dir=self.root)
        with mock.patch.object(smoke.subprocess, "Popen", popen):
            program = smoke.Program(["/a b/CampertronConsole", "--web"], {}, folder)
            program.stop_gracefully()
        self.assertTrue(started["start_new_session"])
        self.assertNotIn("shell", started)
        self.assertEqual(started["stdin"], subprocess.DEVNULL)
        self.assertEqual(started["signal"], signal.SIGTERM)

    def test_the_web_ui_gets_the_isolated_folders_and_no_container_variable(self):
        with mock.patch.dict(os.environ, {"DOTNET_RUNNING_IN_CONTAINER": "true", "XDG_DATA_HOME": "/real", "HOME": "/real-home"}):
            isolation = smoke.Isolation(tempfile.mkdtemp(dir=self.root))
            env = isolation.environment()
        self.assertNotIn("DOTNET_RUNNING_IN_CONTAINER", env)
        self.assertEqual(env["XDG_DATA_HOME"], isolation.data)
        self.assertEqual(env["HOME"], isolation.home)
        self.assertTrue(isolation.data.startswith(self.root))


class Container(Base):
    def setUp(self):
        super().setUp()
        self.bin = os.path.join(self.root, "bin")
        self.docker_state = os.path.join(self.root, "docker")
        os.makedirs(self.bin)
        os.makedirs(self.docker_state)
        write_executable(os.path.join(self.bin, "docker"), FAKE_DOCKER)
        self.env.update(PATH=self.bin + os.pathsep + os.environ["PATH"], FAKE_DOCKER_STATE=self.docker_state, FAKE_APP=self.app)

    def container(self, faults="", docker_faults="", image="campertron-smoke:local", **options):
        return self.run_smoke("container", "--image", image, "--version", VERSION, "--icon", ICON, faults=faults,
                              env={"FAKE_DOCKER": docker_faults}, **options)

    def calls(self):
        with open(os.path.join(self.docker_state, "calls.jsonl")) as log:
            return [json.loads(line) for line in log]

    def assertContainerCleanedUp(self):
        self.assertEqual([f for f in os.listdir(self.docker_state) if f.endswith(".json")], [], "a container remained")
        self.assertEqual(os.listdir(self.temp), [], "the smoke test left a temporary folder")
        self.assertNotIn(CODE, self.output)
        for call in self.calls():
            self.assertNotIn(call[0], ("push", "pull", "login", "logout", "tag", "rmi", "commit", "save", "load", "build", "buildx", "system", "network", "volume"), call)
            if call[0] == "image":
                self.assertEqual(call[:2], ["image", "inspect"], call)
            if call[0] in ("rm", "stop", "exec", "logs", "port"):
                self.assertTrue(call[-1].startswith("campertron-web-smoke-") or call[1].startswith("campertron-web-smoke-"), call)

    def test_a_good_container_passes(self):
        code = self.container()
        self.assertEqual(code, smoke.EXIT_PASSED, self.output)
        run = next(c for c in self.calls() if c[0] == "run" and "--detach" in c)
        self.assertEqual(run[run.index("--pull") + 1], "never")
        self.assertEqual(run[run.index("--publish") + 1], "127.0.0.1::8790")
        self.assertEqual(run[-2:], ["campertron-smoke:local", "--web"])
        mounts = [run[i + 1] for i, a in enumerate(run) if a == "--mount"]
        self.assertEqual([m.split(",")[2] for m in mounts], ["target=/config", "target=/cache"])
        for mount in mounts:
            self.assertTrue(mount.split(",")[1].startswith("source=" + self.temp), mount)
        cleanup = next(c for c in self.calls() if c[0] == "run" and "--rm" in c)
        self.assertEqual(cleanup[cleanup.index("--network") + 1], "none")
        self.assertIn(["stop", "--time", "5", run[run.index("--name") + 1]], self.calls())
        self.assertTrue(any(c[0] == "rm" for c in self.calls()))
        self.assertContainerCleanedUp()

    def test_an_image_that_is_not_local_is_refused_and_never_pulled(self):
        code = self.container(image="campertron-other:local")
        self.assertEqual(code, smoke.EXIT_REFUSED, self.output)
        self.assertEqual([c[0] for c in self.calls()], ["image"])

    def test_a_port_published_on_every_interface_fails(self):
        code = self.container(docker_faults="publish-all")
        self.assertEqual(code, smoke.EXIT_FAILED, self.output)
        self.assertIn("published beyond this computer's loopback address", self.output)
        self.assertContainerCleanedUp()

    def test_a_health_check_that_fails_in_the_container_fails(self):
        code = self.container(docker_faults="health-broken")
        self.assertEqual(code, smoke.EXIT_FAILED, self.output)
        self.assertIn("--healthcheck in the container did not succeed", self.output)
        self.assertContainerCleanedUp()

    def test_a_missing_favicon_in_the_container_fails(self):
        code = self.container("no-favicon")
        self.assertEqual(code, smoke.EXIT_FAILED, self.output)
        self.assertIn("GET /favicon.ico did not answer 200", self.output)
        self.assertContainerCleanedUp()

    def test_a_container_that_does_not_stop_cleanly_fails(self):
        code = self.container("ignore-term", stop_timeout="2")
        self.assertEqual(code, smoke.EXIT_FAILED, self.output)
        self.assertIn("did not stop with exit code 0", self.output)
        self.assertContainerCleanedUp()

    def test_logging_in_the_container_fails(self):
        code = self.container("aspnet-log")
        self.assertEqual(code, smoke.EXIT_FAILED, self.output)
        self.assertIn("ASP.NET Core wrote to the console", self.output)
        self.assertContainerCleanedUp()


if __name__ == "__main__":
    if sys.argv[1:2] == ["--write-fake-app"]:
        print(write_fake_release(sys.argv[2]))
        sys.exit(0)
    if sys.argv[1:2] == ["--write-fake-docker"]:
        os.makedirs(sys.argv[2], exist_ok=True)
        write_executable(os.path.join(sys.argv[2], "docker"), FAKE_DOCKER)
        print(os.path.join(sys.argv[2], "docker"))
        sys.exit(0)
    if not sys.platform.startswith("linux"):
        print("test-web-smoke.py runs on Linux only")
        sys.exit(1)
    unittest.main(verbosity=2)
