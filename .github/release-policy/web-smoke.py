#!/usr/bin/env python3
"""Web mode's release smoke test (v0.3.0's PR 12, decision D102; docs/ai-context/release-process.md §Release workflow).

Starts a released CampertronConsole in web mode (--web, D92) and checks what only a release artifact can show: that the
published program contains the web UI (CampertronWeb, its compiled pages and embedded files) and starts it on this computer
only, answers its health check, keeps its pages behind the password, serves the approved icon, writes only its own fixed
lines and stops cleanly. It is a smoke test: CampertronWebTests tests the web UI itself.

  python3 -I web-smoke.py native --app <the archive's CampertronConsole executable> --version <version> --icon <campertron.ico>
  python3 -I web-smoke.py container --image <a local image> --version <version> --icon <campertron.ico>

native runs the executable of an extracted release archive. On Linux its data folder is a new temporary XDG_DATA_HOME (and
an empty HOME that must stay empty). On Windows and macOS Campertron's data folder can't be redirected, so it runs only on a
fresh GitHub-hosted runner, as the release workflow's other native smoke tests do, where CampertronConfig and
CampertronCache must not exist before it starts; it removes the two folders afterwards. container runs a local image with
new temporary /config and /cache folders and its port published on 127.0.0.1 only; it never pulls or pushes an image.

The web UI listens on a loopback address only (127.0.0.1 unless --listen names ::1; never another address) on a free port
this script picks. Nothing contacts another computer: no search, refresh or monitoring runs, and no password is set (the
one-time setup code is never used, kept or shown: it is removed from every diagnostic). The program is stopped as a user's
service manager stops it (SIGTERM on Linux and macOS, CTRL_BREAK to its own process group on Windows; docker stop), and it
must end with exit code 0 and "Web UI: stopped.". Then its port must be closed, no process of it may remain and the
temporary folders are removed. A program that does not stop is stopped by force, and the smoke test fails.

Python 3.9 or later, the standard library only. Exit code 0 when every check passed, 1 when one failed (or the cleanup
failed), 2 when the command line was refused. The output names each check; a failure adds a GitHub error annotation and the
program's last lines with the setup code and the temporary folders' paths removed. No log or file is left behind.
"""

import argparse
import http.client
import json
import os
import re
import secrets
import shutil
import signal
import socket
import stat
import subprocess
import sys
import tempfile
import time

EXIT_PASSED, EXIT_FAILED, EXIT_REFUSED = 0, 1, 2

# ---- what the released program says and serves (CampertronWeb). WebSmokeContractTests (CampertronWebTests) checks these
# against the code, so a change of the web UI that this smoke test would refuse fails the normal CI first.
HEALTH_PATH = "/healthz"
HEALTH_TEXT = "ok"
HEALTHY_LINE = "Campertron's web UI answers."
UNHEALTHY_LINE = "Campertron's web UI does not answer."
CONTENT_SECURITY_POLICY = "default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self'; font-src 'self'; connect-src 'self'; form-action 'self'; base-uri 'none'; frame-ancestors 'none'"
LISTENING_LOOPBACK = "Web UI: listening on http://{address}:{port} (this computer only); open http://{address}:{port}/ in a browser. A password is required."
LISTENING_EVERY_INTERFACE = "Web UI: listening on http://{address}:{port} (every network interface of this computer); open http://<this computer's address>:{port}/ in a browser. A password is required."
SETUP_CODE_INTRO = "Web UI: no password is set yet. Enter this one-time setup code in the browser to set it (valid for 30 minutes, for one use):"
SETUP_CODE_LINE = "Web UI setup code: >>> {code} <<<"
SETUP_CODE_ALPHABET = "ABCDEFGHJKMNPQRSTVWXYZ23456789"
STOPPED_LINE = "Web UI: stopped."
LISTEN_PROBLEM = "Error: --listen needs one IP address, such as 127.0.0.1 (this computer only) or 0.0.0.0 (every network interface), or the word localhost."
USAGE_LINE = "Usage: CampertronConsole --web [--listen <address>] [--port <port>]"
SETUP_PAGE_TITLE = "<title>Set the password – Campertron</title>"
CONTAINER_PORT = 8790
# the files web mode itself may create in a new settings folder: its keys (WebKeyRepository), nothing else (no settings
# file, no password, no campground data, no Camp.History, no monitoring lock)
OWN_FILES = ("Web.keys",)

LOOPBACK_ADDRESSES = ("127.0.0.1", "::1")
TIMESTAMP = r"\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}  "
CODE = "[" + SETUP_CODE_ALPHABET + "]{4}-[" + SETUP_CODE_ALPHABET + "]{4}-[" + SETUP_CODE_ALPHABET + "]{4}"
VERSION_PATTERN = re.compile(r"^[0-9A-Za-z][0-9A-Za-z.+-]{0,63}$")
IMAGE_PATTERN = re.compile(r"^[a-z0-9][a-z0-9._/-]{0,127}(:[A-Za-z0-9_][A-Za-z0-9._-]{0,127})?$")
# ASP.NET Core's console logger and its host's status messages, which web mode turns off (D81)
FRAMEWORK_LOG = re.compile(r"^(info|warn|fail|crit|dbug|trce): |Microsoft\.(AspNetCore|Hosting|Extensions)|Now listening on|Application started|Hosting environment|Content root path|Application is shutting down")
STACK_TRACE = re.compile(r"^\s+at \S|Exception\b|^Unhandled ")

START_TIMEOUT_DEFAULT, STOP_TIMEOUT_DEFAULT, REQUEST_TIMEOUT = 90.0, 30.0, 10.0
TIMEOUT_LIMIT = 600.0
POLL = 0.1
MAX_BODY = 1024 * 1024
# web mode writes about 500 bytes when it starts; far more is a logger that was turned on
OUTPUT_LIMIT = 256 * 1024
# the platform this runs on (the tests replace it to check the refusals of the platforms they can't run)
PLATFORM = sys.platform
WINDOWS = PLATFORM == "win32"


class Refused(Exception):
    """The command line or the environment can't be used: nothing was started (exit code 2)."""


class Failed(Exception):
    """A check failed (exit code 1)."""


def say(text):
    print("web-smoke: " + text, flush=True)


# ---- what may be shown ----

def redact(text, paths=()):
    """The text without the setup code and without the temporary folders' paths, with control characters escaped."""
    text = re.sub(r">>>.*?<<<", ">>> [setup code removed] <<<", text)
    text = re.sub(CODE, "[setup code removed]", text)
    for path in sorted((p for p in paths if p), key=len, reverse=True):
        text = text.replace(path, "<smoke folder>")
    return "".join(c if c == "\n" or (c.isprintable() and c != "\x1b") else "\\x%02x" % ord(c) for c in text)


def tail(text, paths, lines=40, width=300):
    shown = [line[:width] for line in redact(text, paths).splitlines()[-lines:]]
    return "\n".join("    | " + line for line in shown) if shown else "    | (nothing)"


# ---- the command line ----

def parse(argv):
    parser = argparse.ArgumentParser(prog="web-smoke.py", description="Web mode's release smoke test.", allow_abbrev=False)
    modes = parser.add_subparsers(dest="mode", required=True)
    for name in ("native", "container"):
        mode = modes.add_parser(name, allow_abbrev=False)
        if name == "native":
            mode.add_argument("--app", required=True, help="the extracted release archive's CampertronConsole executable")
            mode.add_argument("--listen", default="127.0.0.1", help="127.0.0.1 (the default) or ::1; never another address")
            mode.add_argument("--port", type=int, default=0, help="a free port from 1024 to 65535; by default one is picked")
        else:
            mode.add_argument("--image", required=True, help="a local image (never pulled)")
        mode.add_argument("--version", required=True, help="the version the program must report")
        mode.add_argument("--icon", required=True, help="the approved campertron.ico the web UI must serve")
        mode.add_argument("--start-timeout", type=float, default=START_TIMEOUT_DEFAULT)
        mode.add_argument("--stop-timeout", type=float, default=STOP_TIMEOUT_DEFAULT)
    try:
        return parser.parse_args(argv)
    except SystemExit as exit_:
        raise Refused("the command line was refused (see above)") from exit_


def check_inputs(args):
    if sys.version_info < (3, 9):
        raise Refused("Python 3.9 or later is needed")
    if not VERSION_PATTERN.match(args.version):
        raise Refused("--version is not a version")
    for name in ("start_timeout", "stop_timeout"):
        value = getattr(args, name)
        if not (0 < value <= TIMEOUT_LIMIT):
            raise Refused("--%s must be more than 0 and at most %d seconds" % (name.replace("_", "-"), TIMEOUT_LIMIT))
    if not os.path.isfile(args.icon) or os.path.getsize(args.icon) == 0:
        raise Refused("--icon is not a file")
    if args.mode == "native":
        if args.listen not in LOOPBACK_ADDRESSES:
            raise Refused("--listen must be a loopback address (127.0.0.1 or ::1): the smoke test never listens beyond this computer")
        if args.port != 0 and not (1024 <= args.port <= 65535):
            raise Refused("--port must be from 1024 to 65535")
        app = args.app
        if not app or app.startswith("-") or not os.path.isabs(app):
            raise Refused("--app must be the absolute path of the extracted archive's CampertronConsole executable")
        if os.path.basename(app) != ("CampertronConsole.exe" if WINDOWS else "CampertronConsole"):
            raise Refused("--app must name the release archive's CampertronConsole executable")
        if not os.path.isfile(app):
            raise Refused("--app does not exist")
        if not WINDOWS and not os.access(app, os.X_OK):
            raise Refused("--app is not executable")
        for neighbor in ("CampertronWeb.dll", "CampertronLibrary.dll"):
            if not os.path.isfile(os.path.join(os.path.dirname(app), neighbor)):
                raise Refused("--app's folder has no " + neighbor + ": it is not an extracted release archive")
    else:
        if not PLATFORM.startswith("linux"):
            raise Refused("container runs only on Linux")
        if not IMAGE_PATTERN.match(args.image):
            raise Refused("--image is not a local image name")
        if shutil.which("docker") is None:
            raise Refused("docker is not available")


# ---- processes ----

class WindowsJob:
    """A job object for the web UI's process on Windows: it shows whether a process of it remains, and it ends them all
    when the smoke test ends in any way (JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE)."""

    def __init__(self, process):
        import ctypes
        from ctypes import wintypes
        self.ctypes = ctypes
        kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
        kernel32.CreateJobObjectW.argtypes = [ctypes.c_void_p, wintypes.LPCWSTR]
        kernel32.CreateJobObjectW.restype = wintypes.HANDLE
        kernel32.SetInformationJobObject.argtypes = [wintypes.HANDLE, ctypes.c_int, ctypes.c_void_p, wintypes.DWORD]
        kernel32.SetInformationJobObject.restype = wintypes.BOOL
        kernel32.AssignProcessToJobObject.argtypes = [wintypes.HANDLE, wintypes.HANDLE]
        kernel32.AssignProcessToJobObject.restype = wintypes.BOOL
        kernel32.QueryInformationJobObject.argtypes = [wintypes.HANDLE, ctypes.c_int, ctypes.c_void_p, wintypes.DWORD, ctypes.c_void_p]
        kernel32.QueryInformationJobObject.restype = wintypes.BOOL
        kernel32.TerminateJobObject.argtypes = [wintypes.HANDLE, wintypes.UINT]
        kernel32.TerminateJobObject.restype = wintypes.BOOL
        kernel32.CloseHandle.argtypes = [wintypes.HANDLE]
        kernel32.CloseHandle.restype = wintypes.BOOL
        self.kernel32 = kernel32

        class Basic(ctypes.Structure):
            _fields_ = [("PerProcessUserTimeLimit", ctypes.c_int64), ("PerJobUserTimeLimit", ctypes.c_int64),
                        ("LimitFlags", wintypes.DWORD), ("MinimumWorkingSetSize", ctypes.c_size_t),
                        ("MaximumWorkingSetSize", ctypes.c_size_t), ("ActiveProcessLimit", wintypes.DWORD),
                        ("Affinity", ctypes.c_size_t), ("PriorityClass", wintypes.DWORD), ("SchedulingClass", wintypes.DWORD)]

        class Io(ctypes.Structure):
            _fields_ = [(n, ctypes.c_uint64) for n in ("ReadOperationCount", "WriteOperationCount", "OtherOperationCount",
                                                         "ReadTransferCount", "WriteTransferCount", "OtherTransferCount")]

        class Extended(ctypes.Structure):
            _fields_ = [("BasicLimitInformation", Basic), ("IoInfo", Io), ("ProcessMemoryLimit", ctypes.c_size_t),
                        ("JobMemoryLimit", ctypes.c_size_t), ("PeakProcessMemoryUsed", ctypes.c_size_t),
                        ("PeakJobMemoryUsed", ctypes.c_size_t)]

        class Accounting(ctypes.Structure):
            _fields_ = [("TotalUserTime", ctypes.c_int64), ("TotalKernelTime", ctypes.c_int64),
                        ("ThisPeriodTotalUserTime", ctypes.c_int64), ("ThisPeriodTotalKernelTime", ctypes.c_int64),
                        ("TotalPageFaultCount", wintypes.DWORD), ("TotalProcesses", wintypes.DWORD),
                        ("ActiveProcesses", wintypes.DWORD), ("TotalTerminatedProcesses", wintypes.DWORD)]

        self.Accounting = Accounting
        self.handle = kernel32.CreateJobObjectW(None, None)
        if not self.handle:
            raise Failed("the smoke test can't create a job object for the web UI's process (error %d)" % ctypes.get_last_error())
        limits = Extended()
        limits.BasicLimitInformation.LimitFlags = 0x2000  # JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
        if not kernel32.SetInformationJobObject(self.handle, 9, ctypes.byref(limits), ctypes.sizeof(limits)):
            raise Failed("the smoke test can't set its job object's limits (error %d)" % ctypes.get_last_error())
        if not kernel32.AssignProcessToJobObject(self.handle, int(process._handle)):
            raise Failed("the smoke test can't put the web UI's process in its job object (error %d)" % ctypes.get_last_error())

    def active(self):
        info = self.Accounting()
        if not self.kernel32.QueryInformationJobObject(self.handle, 1, self.ctypes.byref(info), self.ctypes.sizeof(info), None):
            raise Failed("the smoke test can't read its job object (error %d)" % self.ctypes.get_last_error())
        return int(info.ActiveProcesses)

    def terminate(self):
        self.kernel32.TerminateJobObject(self.handle, 1)

    def close(self):
        if self.handle:
            self.kernel32.CloseHandle(self.handle)
            self.handle = None


def linux_parent_death_signal():
    """In the child before exec (Linux): SIGTERM when the smoke test's process ends, so a killed smoke test leaves no web UI."""
    import ctypes
    libc = ctypes.CDLL(None, use_errno=True)
    libc.prctl(1, int(signal.SIGTERM), 0, 0, 0)  # PR_SET_PDEATHSIG


class Program:
    """The released program in web mode, started from an argument list (never a shell), its output in files of the smoke
    test's private folder (no pipe that could fill), in a process group of its own."""

    def __init__(self, argv, env, folder):
        self.out_path = os.path.join(folder, "stdout.txt")
        self.err_path = os.path.join(folder, "stderr.txt")
        self.job = None
        with open(self.out_path, "wb") as out, open(self.err_path, "wb") as err:
            options = dict(stdin=subprocess.DEVNULL, stdout=out, stderr=err, env=env, close_fds=True)
            if WINDOWS:
                options["creationflags"] = subprocess.CREATE_NEW_PROCESS_GROUP
            else:
                options["start_new_session"] = True
                if PLATFORM.startswith("linux"):
                    options["preexec_fn"] = linux_parent_death_signal
            self.process = subprocess.Popen(argv, **options)
        if WINDOWS:
            try:
                self.job = WindowsJob(self.process)
            except Failed:
                self.process.kill()
                self.process.wait(30)
                raise

    def output(self):
        return read_text(self.out_path), read_text(self.err_path)

    def running(self):
        return self.process.poll() is None

    def stop_gracefully(self):
        """What a service manager sends: SIGTERM (Linux, macOS) or CTRL_BREAK to its process group (Windows)."""
        if not self.running():
            return
        if WINDOWS:
            os.kill(self.process.pid, signal.CTRL_BREAK_EVENT)
        else:
            self.process.send_signal(signal.SIGTERM)

    def wait(self, timeout):
        try:
            return self.process.wait(timeout)
        except subprocess.TimeoutExpired:
            return None

    def remaining(self):
        """Whether a process of the web UI's process group (Unix) or job object (Windows) still runs after it ended."""
        if WINDOWS:
            return self.job is not None and self.job.active() > 0
        try:
            os.killpg(self.process.pid, 0)
            return True
        except ProcessLookupError:
            return False
        except PermissionError:
            return True

    def force(self):
        """The last resort: every process of it, at once."""
        if WINDOWS:
            if self.job is not None:
                self.job.terminate()
            if self.running():
                self.process.kill()
        else:
            try:
                os.killpg(self.process.pid, signal.SIGKILL)
            except ProcessLookupError:
                pass
        self.wait(30)

    def close(self):
        if self.job is not None:
            self.job.close()


def read_text(path):
    try:
        with open(path, "rb") as file:
            # without a byte order mark, in case a console writes one before its first line
            return file.read(OUTPUT_LIMIT + 1).decode("utf-8", "replace").lstrip("\ufeff")
    except FileNotFoundError:
        return ""


def run_briefly(argv, env, timeout=60.0):
    """A short run of the program (a refused command line, --healthcheck): its exit code and output."""
    try:
        done = subprocess.run(argv, stdin=subprocess.DEVNULL, capture_output=True, env=env, timeout=timeout)
    except subprocess.TimeoutExpired:
        raise Failed("%s did not end within %d seconds" % (" ".join(argv[1:]), timeout))
    return done.returncode, done.stdout.decode("utf-8", "replace").replace("\r\n", "\n"), done.stderr.decode("utf-8", "replace").replace("\r\n", "\n")


# ---- the network ----

def family(address):
    return socket.AF_INET6 if ":" in address else socket.AF_INET


def free_port(address):
    with socket.socket(family(address), socket.SOCK_STREAM) as probe:
        probe.bind((address, 0))
        return probe.getsockname()[1]


def port_open(address, port, timeout=10.0):
    if address not in LOOPBACK_ADDRESSES:
        raise Failed("the smoke test asks only this computer's loopback address")
    with socket.socket(family(address), socket.SOCK_STREAM) as probe:
        probe.settimeout(timeout)
        try:
            probe.connect((address, port))
            return True
        except (ConnectionRefusedError, ConnectionResetError):
            return False
        except socket.timeout:
            raise Failed("port %d did not answer or refuse within %d seconds" % (port, timeout))
        except OSError as error:
            if error.errno in (111, 61, 10061):
                return False
            raise


class Response:
    def __init__(self, status, headers, body):
        self.status, self.headers, self.body = status, headers, body

    def header(self, name):
        values = [v for (k, v) in self.headers if k.lower() == name.lower()]
        return values[0] if values else None

    def all(self, name):
        return [v for (k, v) in self.headers if k.lower() == name.lower()]


def request(address, port, method, path, headers=None, body=None):
    """One HTTP/1.1 request to the web UI, directly (no proxy, no redirect followed, no cookie kept), and only on this
    computer: the smoke test never talks to another."""
    if address not in LOOPBACK_ADDRESSES:
        raise Failed("the smoke test asks only this computer's loopback address")
    connection = http.client.HTTPConnection(address, port, timeout=REQUEST_TIMEOUT)
    try:
        connection.request(method, path, body=body, headers=headers or {})
        answer = connection.getresponse()
        data = answer.read(MAX_BODY + 1)
        if len(data) > MAX_BODY:
            raise Failed("%s %s answered with more than %d bytes" % (method, path, MAX_BODY))
        return Response(answer.status, answer.getheaders(), data)
    except (OSError, http.client.HTTPException) as error:
        raise Failed("%s %s got no answer (%s)" % (method, path, type(error).__name__))
    finally:
        connection.close()


# ---- the checks ----

def expect(condition, message):
    if not condition:
        raise Failed(message)


def check_lines(stdout, stderr, version, settings, listening):
    """The start's lines: exactly web mode's own (its identity and settings folder, where it listens, the setup code's
    two lines), nothing on standard error, no ASP.NET Core log line, no exception."""
    lines = stdout.replace("\r\n", "\n").splitlines()
    for line in lines + stderr.splitlines():
        expect(not FRAMEWORK_LOG.search(line), "ASP.NET Core wrote to the console (its logging must be off)")
        expect(not STACK_TRACE.search(line), "the output has an exception or a stack trace")
    expect(stderr.strip() == "", "the web UI wrote to standard error")
    expect(len(lines) >= 4, "the web UI did not write its four start lines")
    identity = re.match("^" + TIMESTAMP + r"CampertronConsole " + re.escape(version) + r"(\+[0-9a-f]+)? \| .+ \| settings (?P<settings>.+) \| web UI$", lines[0])
    expect(identity is not None, "the first line is not web mode's start line for CampertronConsole " + version)
    shown = identity.group("settings")
    same = shown == settings or (WINDOWS and os.path.normcase(os.path.normpath(shown)) == os.path.normcase(os.path.normpath(settings)))
    expect(same, "the web UI did not use the smoke test's own settings folder")
    expect(re.match("^" + TIMESTAMP + re.escape(listening) + "$", lines[1]) is not None,
           "the web UI does not say that it listens where it was told to (exactly: " + listening + ")")
    expect(re.match("^" + TIMESTAMP + re.escape(SETUP_CODE_INTRO) + "$", lines[2]) is not None, "the setup code's first line is missing")
    code = re.match("^" + TIMESTAMP + re.escape(SETUP_CODE_LINE.split("{code}")[0]) + "(" + CODE + ")" + re.escape(SETUP_CODE_LINE.split("{code}")[1]) + "$", lines[3])
    expect(code is not None, "the setup code's line does not have its shape (XXXX-XXXX-XXXX between >>> and <<<)")
    # the code itself stays in memory: it is compared with what the web UI serves, and never shown
    return lines, code.group(1)


def check_stopped(stdout, stderr, started_lines):
    lines = stdout.replace("\r\n", "\n").splitlines()
    for line in lines + stderr.splitlines():
        expect(not FRAMEWORK_LOG.search(line), "ASP.NET Core wrote to the console while the web UI stopped")
        expect(not STACK_TRACE.search(line), "the output has an exception or a stack trace")
    expect(stderr.strip() == "", "the web UI wrote to standard error")
    expect(lines[:len(started_lines)] == started_lines, "the start lines changed")
    rest = lines[len(started_lines):]
    expect(len(rest) == 1 and re.match("^" + TIMESTAMP + re.escape(STOPPED_LINE) + "$", rest[0]) is not None,
           "the web UI did not end with exactly one more line, \"" + STOPPED_LINE + "\"")


def check_headers(answer, what, cache="no-store"):
    """The security headers of every answer (WebSecurity.Headers). cache: a directive Cache-Control must have, or its
    whole value for an embedded file, or None."""
    expect(answer.header("Content-Security-Policy") == CONTENT_SECURITY_POLICY, what + " lacks the web UI's Content-Security-Policy")
    expect(answer.header("X-Content-Type-Options") == "nosniff", what + " lacks X-Content-Type-Options: nosniff")
    expect(answer.header("X-Frame-Options") == "DENY", what + " lacks X-Frame-Options: DENY")
    expect(answer.header("Referrer-Policy") == "same-origin", what + " lacks Referrer-Policy: same-origin")
    expect(answer.header("Server") is None, what + " names its server")
    if cache == "no-store":
        directives = [d.strip() for d in (answer.header("Cache-Control") or "").split(",")]
        expect("no-store" in directives, what + " may be stored (Cache-Control has no no-store)")
    elif cache is not None:
        expect(answer.header("Cache-Control") == cache, what + " lacks Cache-Control: " + cache)


def check_http(address, port, icon, setup_code):
    """The web UI as a browser and a stranger see it: the health answer, the request checks, the password's boundary, the
    setup page with only its own files, and the approved icon. Nothing is signed in, set or changed."""
    host = "[%s]:%d" % (address, port) if ":" in address else "%s:%d" % (address, port)
    origin = "http://" + host

    health = request(address, port, "GET", HEALTH_PATH)
    expect(health.status == 200 and health.body == HEALTH_TEXT.encode(), "GET /healthz did not answer 200 with \"ok\"")
    expect(health.header("Content-Type") == "text/plain; charset=utf-8", "GET /healthz is not text/plain; charset=utf-8")
    check_headers(health, "GET /healthz")
    head = request(address, port, "HEAD", HEALTH_PATH)
    expect(head.status == 200 and head.body == b"", "HEAD /healthz did not answer 200 without a body")
    say("health answer: ok (GET and HEAD /healthz, only \"ok\", the security headers)")

    expect(request(address, port, "GET", HEALTH_PATH, {"Host": "smoke.invalid"}).status == 400, "a request for another host name was not refused (400)")
    wrong = request(address, port, "PUT", HEALTH_PATH)
    expect(wrong.status == 405 and wrong.header("Allow") == "GET, HEAD, POST", "PUT was not refused (405, Allow: GET, HEAD, POST)")
    expect(request(address, port, "POST", "/monitoring", {"Content-Type": "application/x-www-form-urlencoded"}, b"x=1").status == 403,
           "a POST without the web UI's Origin was not refused (403)")
    say("request checks: ok (another host name 400, PUT 405, a POST from elsewhere 403)")

    for path in ("/", "/settings/general", "/searches", "/results", "/monitoring", "/settings/system", "/campgrounds/park",
                 "/guided-setup", "/Web.password", "/Web.keys/instance", "/General.yaml", "/CampertronConsole.dll"):
        answer = request(address, port, "GET", path)
        expect(answer.status == 302 and answer.header("Location") == "/setup",
               "GET " + path + " without a password did not lead to the setup (302 to /setup): its content may be exposed")
        expect(not any("campertron-session-" in c for c in answer.all("Set-Cookie")), "GET " + path + " set a session cookie without a sign-in")
    posted = request(address, port, "POST", "/monitoring?handler=start", {"Origin": origin, "Content-Type": "application/x-www-form-urlencoded"}, b"x=1")
    expect(posted.status == 302 and posted.header("Location") == "/setup", "a POST without a password was not led to the setup (302 to /setup)")
    for path in ("/assets/Web.password", "/assets/..%2FWeb.password", "/assets/CampertronConsole.dll", "/assets/General.yaml"):
        expect(request(address, port, "GET", path).status == 404, "GET " + path + " was not refused (404)")
    say("password boundary: ok (every page and file without a password leads to the setup; no other file is served)")

    setup = request(address, port, "GET", "/setup")
    expect(setup.status == 200 and setup.header("Content-Type") == "text/html; charset=utf-8", "GET /setup is not the setup page (200 text/html)")
    check_headers(setup, "GET /setup")
    page = setup.body.decode("utf-8", "replace")
    expect(SETUP_PAGE_TITLE in page, "the setup page is not the web UI's compiled page")
    expect(setup_code not in page, "the setup page shows the setup code (only the console may)")
    addresses = re.findall(r'\s(?:src|href|action|srcset|poster|data)="([^"]*)"', page)
    external = [a for a in addresses if not (a.startswith("#") or (a.startswith("/") and not a.startswith("//")))]
    expect(addresses and not external, "the setup page names an address outside the web UI")
    expect(re.search(r"(?i)(https?:)?//[a-z0-9\[]", page) is None, "the setup page names another site")
    style = re.search(r'<link rel="stylesheet" href="(/assets/site\.css\?v=[0-9a-f]{16})" />', page)
    linked_icon = re.search(r'<link rel="icon" href="(/assets/campertron\.ico\?v=[0-9a-f]{16})" />', page)
    expect(style is not None and linked_icon is not None, "the setup page does not link the embedded stylesheet and icon")
    css = request(address, port, "GET", style.group(1))
    expect(css.status == 200 and css.header("Content-Type") == "text/css; charset=utf-8" and len(css.body) > 0, "the embedded stylesheet is not served")
    expect(re.search(rb"(?i)@import|url\(\s*['\"]?\s*(https?:)?//", css.body) is None, "the stylesheet loads something from another site")
    check_headers(css, "the stylesheet", cache="public, max-age=31536000, immutable")
    say("setup page: ok (the compiled page, its own stylesheet and icon only)")

    expected = icon_bytes(icon)
    for path in (linked_icon.group(1), "/favicon.ico"):
        answer = request(address, port, "GET", path)
        expect(answer.status == 200, "GET " + path + " did not answer 200")
        expect(answer.header("Content-Type") == "image/x-icon", "GET " + path + " is not image/x-icon")
        expect(len(answer.body) > 0, "GET " + path + " is empty")
        expect(answer.body[:4] == b"\x00\x00\x01\x00", "GET " + path + " is not an icon file")
        expect(answer.body == expected, "GET " + path + " is not the approved campertron.ico")
        check_headers(answer, "GET " + path, cache=None)
    favicon_head = request(address, port, "HEAD", "/favicon.ico")
    expect(favicon_head.status == 200 and favicon_head.body == b"" and favicon_head.header("Content-Length") == str(len(expected)),
           "HEAD /favicon.ico did not answer 200 with the icon's length and no body")
    say("icon: ok (/favicon.ico and the page's icon are the approved campertron.ico, %d bytes)" % len(expected))


def icon_bytes(path):
    with open(path, "rb") as file:
        data = file.read()
    if data[:4] != b"\x00\x00\x01\x00" or int.from_bytes(data[4:6], "little") == 0:
        raise Refused("--icon is not an icon file")
    return data


# ---- the folders ----

def remove_tree(path):
    """Removes a folder the smoke test made; a read-only file is made writable first (Windows)."""
    def writable_then_again(function, name, _error):
        os.chmod(name, stat.S_IWRITE | stat.S_IREAD | stat.S_IEXEC)
        function(name)
    if os.path.lexists(path):
        if sys.version_info >= (3, 12):
            shutil.rmtree(path, onexc=writable_then_again)
        else:
            shutil.rmtree(path, onerror=writable_then_again)
    if os.path.lexists(path):
        raise Failed("the smoke test could not remove its folder")


def runner_data_folder():
    """On Windows and macOS: the hosted runner's own LocalApplicationData, which Campertron uses and nothing redirects."""
    if os.environ.get("GITHUB_ACTIONS") != "true" or os.environ.get("RUNNER_ENVIRONMENT") != "github-hosted":
        raise Refused("on Windows and macOS Campertron's data folder can't be redirected, so this smoke test runs only on a "
                      "fresh GitHub-hosted runner (it never runs in a user's own data folder)")
    if WINDOWS:
        folder = os.environ.get("LOCALAPPDATA", "")
    else:
        folder = os.path.join(os.path.expanduser("~"), "Library", "Application Support")
    if not folder or not os.path.isabs(folder) or not os.path.isdir(folder):
        raise Refused("the runner's local application data folder was not found")
    return folder


class Isolation:
    """Where the program keeps its data during the smoke test, and its removal afterwards."""

    def __init__(self, work):
        self.work = work
        self.home = None
        if PLATFORM.startswith("linux"):
            self.data = os.path.join(work, "data")
            self.home = os.path.join(work, "home")
            os.mkdir(self.data, 0o700)
            os.mkdir(self.home, 0o700)
            self.made_here = True
        elif WINDOWS or PLATFORM == "darwin":
            self.data = runner_data_folder()
            self.made_here = False
        else:
            raise Refused("this platform is not one the release workflow runs (Linux, Windows or macOS)")
        self.settings = os.path.join(self.data, "CampertronConfig")
        self.cache = os.path.join(self.data, "CampertronCache")
        if os.path.lexists(self.settings) or os.path.lexists(self.cache):
            raise Refused("CampertronConfig or CampertronCache exists before the smoke test: it runs only in a new data folder")

    def environment(self):
        env = {k: v for (k, v) in os.environ.items() if k not in ("DOTNET_RUNNING_IN_CONTAINER", "DOTNET_RUNNING_IN_CONTAINERS")}
        if self.home is not None:
            env["XDG_DATA_HOME"] = self.data
            env["HOME"] = self.home
        return env

    def paths(self):
        return [p for p in (self.work, self.data, self.settings) if p]

    def check_untouched_before_start(self):
        expect(not os.path.lexists(self.settings) and not os.path.lexists(self.cache),
               "a refused command line or the health check created Campertron's folders")

    def check_files(self):
        expect(os.path.isdir(self.settings), "the web UI did not use its settings folder")
        found = sorted(os.listdir(self.settings))
        expect(found == list(OWN_FILES), "the settings folder holds other files than the web UI's keys (Web.keys): " + ", ".join(found))
        keys = os.path.join(self.settings, "Web.keys")
        expect(os.path.isdir(keys) and len(os.listdir(keys)) >= 2, "Web.keys has no key")
        if not WINDOWS:
            expect(stat.S_IMODE(os.stat(keys).st_mode) == 0o700, "Web.keys is not for its owner only (0700)")
            for name in os.listdir(keys):
                expect(stat.S_IMODE(os.stat(os.path.join(keys, name)).st_mode) == 0o600, "a file in Web.keys is not for its owner only (0600)")
        expect(not os.path.isdir(self.cache) or os.listdir(self.cache) == [], "the cache folder is not empty")
        if self.home is not None:
            expect(os.listdir(self.home) == [], "the web UI wrote into its home folder (it may write only in its data folder)")

    def remove(self):
        if not self.made_here:
            for path in (self.settings, self.cache):
                remove_tree(path)


# ---- the two modes ----

def native(args):
    check_inputs(args)
    icon_bytes(args.icon)
    say("native: %s (Python %s, %s)" % (os.path.basename(args.app), sys.version.split()[0], PLATFORM))
    work = tempfile.mkdtemp(prefix="campertron-web-smoke-")
    paths = [work]
    program = None
    isolation = None
    failure = None
    try:
        isolation = Isolation(work)
        paths = isolation.paths()
        env = isolation.environment()

        code, out, err = run_briefly([args.app, "--web", "--listen", "smoke.invalid"], env)
        expect(code == 1 and out == "" and err.splitlines()[:1] == [LISTEN_PROBLEM] and USAGE_LINE in err,
               "a wrong --listen was not refused with web mode's fixed words and exit code 1")
        port = args.port or free_port(args.listen)
        code, out, err = run_briefly([args.app, "--healthcheck", "--listen", args.listen, "--port", str(port)], env)
        expect(code == 1 and out.strip() == UNHEALTHY_LINE and err == "", "--healthcheck with nothing listening did not say so with exit code 1")
        isolation.check_untouched_before_start()
        say("command line: ok (a wrong --listen refused, --healthcheck says nothing answers, nothing created)")

        listening = LISTENING_LOOPBACK.format(address="[%s]" % args.listen if ":" in args.listen else args.listen, port=port)
        attempts = 1 if args.port else 3
        for attempt in range(1, attempts + 1):
            program = Program([args.app, "--web", "--listen", args.listen, "--port", str(port)], env, work)
            state = wait_ready(lambda: program.output(), program.running, args.start_timeout)
            if state == "ready":
                break
            out, err = program.output()
            program.wait(10)
            if state == "exited" and "it can't listen on port" in err and attempt < attempts:
                say("port %d was taken before the web UI could listen; trying another (%d of %d)" % (port, attempt + 1, attempts))
                program.close()
                program = None
                port = free_port(args.listen)
                listening = LISTENING_LOOPBACK.format(address="[%s]" % args.listen if ":" in args.listen else args.listen, port=port)
                continue
            if state == "noisy":
                raise Failed("the web UI wrote more than %d KB to its console before it listened (its logging must be off)" % (OUTPUT_LIMIT // 1024))
            if state == "exited":
                if "it can't listen on port" in err:
                    raise Failed("the web UI could not listen on port %d (bind failed)" % port)
                raise Failed("the web UI ended with exit code %s before it listened" % program.process.returncode)
            raise Failed("the web UI did not say where it listens within %d seconds" % args.start_timeout)
        out, err = program.output()
        started, setup_code = check_lines(out, err, args.version, isolation.settings, listening)
        say("start: ok (web mode's four lines only, on %s port %d, this computer only)" % (args.listen, port))

        code, out, err = run_briefly([args.app, "--healthcheck", "--listen", args.listen, "--port", str(port)], env)
        expect(code == 0 and out.strip() == HEALTHY_LINE and err == "", "--healthcheck against the running web UI did not succeed")
        say("--healthcheck: ok")
        check_http(args.listen, port, args.icon, setup_code)
        expect(program.running(), "the web UI ended while it was checked")

        program.stop_gracefully()
        code = program.wait(args.stop_timeout)
        expect(code is not None, "the web UI did not stop within %d seconds of %s" % (args.stop_timeout, "CTRL_BREAK" if WINDOWS else "SIGTERM"))
        expect(code == 0, "the web UI stopped with exit code %s, not 0" % code)
        out, err = program.output()
        check_stopped(out, err, started)
        expect(not program.remaining(), "a process of the web UI remained after it stopped")
        expect(not port_open(args.listen, port), "port %d still listens after the web UI stopped" % port)
        code, out, err = run_briefly([args.app, "--healthcheck", "--listen", args.listen, "--port", str(port)], env)
        expect(code == 1 and out.strip() == UNHEALTHY_LINE, "--healthcheck still succeeds after the web UI stopped")
        isolation.check_files()
        say("stop: ok (%s, exit code 0, \"%s\", the port closed, no process left, only Web.keys written)" % ("CTRL_BREAK" if WINDOWS else "SIGTERM", STOPPED_LINE))
    except Failed as error:
        failure = error
    finally:
        failure = finish_native(program, work, isolation, paths, failure, args.stop_timeout)
    if failure is not None:
        raise failure
    say("passed")


def wait_ready(output, running, timeout, poll=POLL):
    """Polls the start until web mode said where it listens and gave the setup code: "ready", "exited", "noisy" (far
    more output than web mode's lines) or "timeout"."""
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        out, err = output()
        if len(out) + len(err) > OUTPUT_LIMIT:
            return "noisy"
        if re.search(re.escape(SETUP_CODE_LINE.split("{code}")[0]), out) and out.endswith("\n"):
            return "ready"
        if not running():
            out, _ = output()
            return "ready" if re.search(re.escape(SETUP_CODE_LINE.split("{code}")[0]), out) else "exited"
        time.sleep(poll)
    return "timeout"


def finish_native(program, work, isolation, paths, failure, stop_timeout=10.0):
    """Every way out: the program is stopped (gracefully, then by force), its output shown without secrets if a check
    failed, and the smoke test's folders removed. A failure here fails the smoke test too."""
    problems = []
    if program is not None:
        try:
            if program.running():
                program.stop_gracefully()
                if program.wait(min(10.0, stop_timeout)) is None:
                    problems.append("the web UI was stopped by force")
                    program.force()
            if program.remaining():
                problems.append("a process of the web UI remained; it was stopped by force")
                program.force()
            if program.running() or program.remaining():
                problems.append("a process of the web UI could not be stopped")
        except Exception as error:  # the cleanup goes on
            problems.append("stopping the web UI failed (%s)" % type(error).__name__)
        if failure is not None or problems:
            out, err = program.output()
            print("web-smoke: the web UI's standard output (setup code and folders removed):\n" + tail(out, paths), flush=True)
            print("web-smoke: its standard error:\n" + tail(err, paths), flush=True)
        program.close()
    try:
        if isolation is not None:
            isolation.remove()
        remove_tree(work)
    except (Failed, OSError) as error:
        problems.append("the cleanup failed (%s)" % (error if isinstance(error, Failed) else type(error).__name__))
    if problems and failure is None:
        failure = Failed("; ".join(problems))
    elif problems:
        failure = Failed(str(failure) + "; and " + "; ".join(problems))
    return failure


def docker(*argv, timeout=120.0, check=True):
    try:
        done = subprocess.run(["docker", *argv], stdin=subprocess.DEVNULL, capture_output=True, timeout=timeout)
    except subprocess.TimeoutExpired:
        raise Failed("docker %s did not end within %d seconds" % (argv[0], timeout))
    out = done.stdout.decode("utf-8", "replace")
    err = done.stderr.decode("utf-8", "replace")
    if check and done.returncode != 0:
        raise Failed("docker %s failed with exit code %d" % (argv[0], done.returncode))
    return done.returncode, out, err


def container(args):
    check_inputs(args)
    icon_bytes(args.icon)
    code, out, _ = docker("image", "inspect", "--format", "{{.Id}}", args.image, check=False)
    if code != 0:
        raise Refused("--image is not a local image (the smoke test never pulls one)")
    say("container: %s (Python %s)" % (args.image, sys.version.split()[0]))
    work = tempfile.mkdtemp(prefix="campertron-web-smoke-")
    config, cache = os.path.join(work, "config"), os.path.join(work, "cache")
    for folder in (config, cache):
        if "," in folder or ":" in folder:
            raise Refused("the temporary folder's path can't be mounted")
        os.mkdir(folder)
        os.chmod(folder, 0o777)  # for the image's user app (uid 1654), as the other container smoke tests do
    os.chmod(work, 0o755)
    name = "campertron-web-smoke-" + secrets.token_hex(6)
    mounts = ["--mount", "type=bind,source=%s,target=/config" % config, "--mount", "type=bind,source=%s,target=/cache" % cache]
    paths = [work]
    failure = None
    try:
        # the name may exist from here on, even if docker run fails after creating the container
        docker("run", "--detach", "--pull", "never", "--name", name, "--label", "campertron.web-smoke=1", *mounts,
               "--publish", "127.0.0.1::%d" % CONTAINER_PORT, args.image, "--web")
        _, published, _ = docker("port", name, "%d/tcp" % CONTAINER_PORT)
        bindings = [b.strip() for b in published.splitlines() if b.strip()]
        expect(len(bindings) == 1 and re.match(r"^127\.0\.0\.1:\d+$", bindings[0]) is not None,
               "the web UI's port is published beyond this computer's loopback address")
        port = int(bindings[0].split(":")[1])
        _, inspected, _ = docker("container", "inspect", "--format", "{{json .HostConfig.PortBindings}}", name)
        for binding in json.loads(inspected or "{}").get("%d/tcp" % CONTAINER_PORT, []):
            expect(binding.get("HostIp") == "127.0.0.1", "the web UI's port is published beyond this computer's loopback address")

        def logs():
            _, out, err = docker("logs", name, check=False)
            return out, err

        def running():
            _, state, _ = docker("container", "inspect", "--format", "{{.State.Running}}", name, check=False)
            return state.strip() == "true"

        state = wait_ready(logs, running, args.start_timeout, poll=0.5)
        if state != "ready":
            raise Failed("the containerized web UI %s" % {
                "exited": "ended before it listened",
                "noisy": "wrote more than %d KB to its console before it listened (its logging must be off)" % (OUTPUT_LIMIT // 1024),
            }.get(state, "did not say where it listens within %d seconds" % args.start_timeout))
        out, err = logs()
        lines = out.splitlines()
        address = "[::]" if len(lines) > 1 and "http://[::]:" in lines[1] else "0.0.0.0"
        started, setup_code = check_lines(out, err, args.version, "/config", LISTENING_EVERY_INTERFACE.format(address=address, port=CONTAINER_PORT))
        say("start: ok (web mode's four lines only, in the container on port %d, published on 127.0.0.1:%d only)" % (CONTAINER_PORT, port))
        code, out, err = docker("exec", name, "dotnet", "CampertronConsole.dll", "--healthcheck", check=False)
        expect(code == 0 and out.strip() == HEALTHY_LINE, "--healthcheck in the container did not succeed")
        say("--healthcheck in the container: ok")
        check_http("127.0.0.1", port, args.icon, setup_code)
        expect(running(), "the containerized web UI ended while it was checked")

        docker("stop", "--time", str(int(args.stop_timeout)), name, timeout=args.stop_timeout + 60)
        _, ended, _ = docker("container", "inspect", "--format", "{{.State.Running}} {{.State.ExitCode}} {{.State.OOMKilled}}", name)
        expect(ended.split() == ["false", "0", "false"], "the containerized web UI did not stop with exit code 0 (docker stop sends SIGTERM)")
        out, err = logs()
        check_stopped(out, err, started)
        expect(not port_open("127.0.0.1", port), "port %d still listens after the container stopped" % port)
        expect(sorted(os.listdir(config)) == list(OWN_FILES), "/config holds other files than the web UI's keys (Web.keys)")
        expect(os.listdir(cache) == [], "/cache is not empty")
        say("stop: ok (docker stop, exit code 0, \"%s\", the port closed, only Web.keys written)" % STOPPED_LINE)
    except Failed as error:
        failure = error
    finally:
        problems = []
        if docker("container", "inspect", name, check=False)[0] == 0:
            if failure is not None:
                out, err = docker("logs", name, check=False)[1:]
                print("web-smoke: the container's standard output (setup code and folders removed):\n" + tail(out, paths), flush=True)
                print("web-smoke: its standard error:\n" + tail(err, paths), flush=True)
            try:
                docker("rm", "--force", name, check=False)
                if docker("container", "inspect", name, check=False)[0] == 0:
                    problems.append("the smoke test's container could not be removed")
            except Failed as error:
                problems.append(str(error))
        try:
            # the files the container's user wrote, removed by that user (the host's user may not)
            docker("run", "--rm", "--pull", "never", "--network", "none", *mounts, "--entrypoint", "find", args.image,
                   "/config", "/cache", "-mindepth", "1", "-delete", check=False)
            remove_tree(work)
        except (Failed, OSError) as error:
            problems.append("the cleanup failed (%s)" % (error if isinstance(error, Failed) else type(error).__name__))
        if problems:
            failure = Failed(("%s; and " % failure if failure else "") + "; ".join(problems))
    if failure is not None:
        raise failure
    say("passed")


def main(argv=None, signals=True):
    """signals: a SIGTERM or SIGHUP to the smoke test (a canceled job) stops the web UI and removes its folders first."""
    def interrupted(_number, _frame):
        raise KeyboardInterrupt
    for name in ("SIGTERM", "SIGHUP"):
        if signals and hasattr(signal, name):
            signal.signal(getattr(signal, name), interrupted)
    try:
        args = parse(sys.argv[1:] if argv is None else argv)
        (native if args.mode == "native" else container)(args)
        return EXIT_PASSED
    except Refused as error:
        print("::error::Web mode smoke test refused: %s" % error, flush=True)
        return EXIT_REFUSED
    except Failed as error:
        print("::error::Web mode smoke test failed: %s" % redact(str(error)), flush=True)
        return EXIT_FAILED
    except KeyboardInterrupt:
        print("::error::Web mode smoke test was interrupted; the web UI was stopped and its folders removed", flush=True)
        return EXIT_FAILED


if __name__ == "__main__":
    sys.exit(main())
