#!/usr/bin/env python3
"""Loopback-only transport for the existing Content Workbench command host."""

import argparse
import ctypes
import html
import json
import os
import secrets
import signal
import subprocess
import threading
import time
import webbrowser
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path


PROJECT_ROOT = Path(__file__).resolve().parents[2]
WORKBENCH_ROOT = Path(__file__).resolve().parent
MAX_REQUEST_BYTES = 1024 * 1024
MAX_STDOUT_BYTES = 2 * 1024 * 1024
MAX_STDERR_BYTES = 64 * 1024
SHUTDOWN_GRACE_SECONDS = 1.0


class ManagedChild:
    def __init__(self, process):
        self.process = process
        self.job = None
        self.lock = threading.Lock()
        if os.name == "nt":
            self.job = self._attach_windows_job(process)
            try:
                self._resume_windows_process(process)
            except Exception:
                kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
                kernel32.TerminateJobObject.argtypes = [ctypes.c_void_p, ctypes.c_uint]
                kernel32.CloseHandle.argtypes = [ctypes.c_void_p]
                kernel32.TerminateJobObject(self.job, 1)
                kernel32.CloseHandle(self.job)
                self.job = None
                raise

    @staticmethod
    def _resume_windows_process(process):
        from ctypes import wintypes

        class ThreadEntry32(ctypes.Structure):
            _fields_ = [
                ("dwSize", wintypes.DWORD),
                ("cntUsage", wintypes.DWORD),
                ("th32ThreadID", wintypes.DWORD),
                ("th32OwnerProcessID", wintypes.DWORD),
                ("tpBasePri", wintypes.LONG),
                ("tpDeltaPri", wintypes.LONG),
                ("dwFlags", wintypes.DWORD),
            ]

        kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
        kernel32.CreateToolhelp32Snapshot.argtypes = [wintypes.DWORD, wintypes.DWORD]
        kernel32.CreateToolhelp32Snapshot.restype = wintypes.HANDLE
        kernel32.Thread32First.argtypes = [wintypes.HANDLE, ctypes.POINTER(ThreadEntry32)]
        kernel32.Thread32Next.argtypes = [wintypes.HANDLE, ctypes.POINTER(ThreadEntry32)]
        kernel32.OpenThread.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
        kernel32.OpenThread.restype = wintypes.HANDLE
        kernel32.ResumeThread.argtypes = [wintypes.HANDLE]
        kernel32.ResumeThread.restype = wintypes.DWORD
        kernel32.CloseHandle.argtypes = [wintypes.HANDLE]

        snapshot = kernel32.CreateToolhelp32Snapshot(0x00000004, 0)
        if snapshot == ctypes.c_void_p(-1).value:
            raise ctypes.WinError(ctypes.get_last_error())
        thread_handle = None
        try:
            entry = ThreadEntry32()
            entry.dwSize = ctypes.sizeof(entry)
            found = kernel32.Thread32First(snapshot, ctypes.byref(entry))
            while found:
                if entry.th32OwnerProcessID == process.pid:
                    thread_handle = kernel32.OpenThread(0x0002, False, entry.th32ThreadID)
                    if not thread_handle:
                        raise ctypes.WinError(ctypes.get_last_error())
                    if kernel32.ResumeThread(thread_handle) == 0xFFFFFFFF:
                        raise ctypes.WinError(ctypes.get_last_error())
                    return
                found = kernel32.Thread32Next(snapshot, ctypes.byref(entry))
            raise OSError("The suspended command host's primary thread was not found.")
        finally:
            if thread_handle:
                kernel32.CloseHandle(thread_handle)
            kernel32.CloseHandle(snapshot)

    @staticmethod
    def _attach_windows_job(process):
        from ctypes import wintypes

        class BasicLimitInformation(ctypes.Structure):
            _fields_ = [
                ("PerProcessUserTimeLimit", ctypes.c_longlong),
                ("PerJobUserTimeLimit", ctypes.c_longlong),
                ("LimitFlags", wintypes.DWORD),
                ("MinimumWorkingSetSize", ctypes.c_size_t),
                ("MaximumWorkingSetSize", ctypes.c_size_t),
                ("ActiveProcessLimit", wintypes.DWORD),
                ("Affinity", ctypes.c_size_t),
                ("PriorityClass", wintypes.DWORD),
                ("SchedulingClass", wintypes.DWORD),
            ]

        class IoCounters(ctypes.Structure):
            _fields_ = [(name, ctypes.c_ulonglong) for name in (
                "ReadOperationCount", "WriteOperationCount", "OtherOperationCount",
                "ReadTransferCount", "WriteTransferCount", "OtherTransferCount")]

        class ExtendedLimitInformation(ctypes.Structure):
            _fields_ = [
                ("BasicLimitInformation", BasicLimitInformation),
                ("IoInfo", IoCounters),
                ("ProcessMemoryLimit", ctypes.c_size_t),
                ("JobMemoryLimit", ctypes.c_size_t),
                ("PeakProcessMemoryUsed", ctypes.c_size_t),
                ("PeakJobMemoryUsed", ctypes.c_size_t),
            ]

        kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
        kernel32.CreateJobObjectW.restype = wintypes.HANDLE
        kernel32.SetInformationJobObject.argtypes = [wintypes.HANDLE, ctypes.c_int, ctypes.c_void_p, wintypes.DWORD]
        kernel32.AssignProcessToJobObject.argtypes = [wintypes.HANDLE, wintypes.HANDLE]
        kernel32.TerminateJobObject.argtypes = [wintypes.HANDLE, wintypes.UINT]
        kernel32.CloseHandle.argtypes = [wintypes.HANDLE]
        job = kernel32.CreateJobObjectW(None, None)
        if not job:
            raise ctypes.WinError(ctypes.get_last_error())
        limits = ExtendedLimitInformation()
        limits.BasicLimitInformation.LimitFlags = 0x2000  # KILL_ON_JOB_CLOSE
        ok = kernel32.SetInformationJobObject(job, 9, ctypes.byref(limits), ctypes.sizeof(limits))
        if not ok or not kernel32.AssignProcessToJobObject(job, wintypes.HANDLE(process._handle)):
            error = ctypes.get_last_error()
            kernel32.TerminateJobObject(job, 1)
            kernel32.CloseHandle(job)
            raise ctypes.WinError(error)
        return job

    def terminate(self):
        with self.lock:
            if os.name == "nt" and self.job:
                kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
                kernel32.TerminateJobObject.argtypes = [ctypes.c_void_p, ctypes.c_uint]
                kernel32.TerminateJobObject(self.job, 1)
            elif os.name != "nt":
                try:
                    os.killpg(self.process.pid, signal.SIGKILL)
                except ProcessLookupError:
                    pass
            elif self.process.poll() is None:
                self.process.kill()

    def close(self):
        with self.lock:
            if os.name == "nt" and self.job:
                kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
                kernel32.TerminateJobObject.argtypes = [ctypes.c_void_p, ctypes.c_uint]
                kernel32.CloseHandle.argtypes = [ctypes.c_void_p]
                kernel32.TerminateJobObject(self.job, 1)
                kernel32.CloseHandle(self.job)
                self.job = None
            elif os.name != "nt":
                try:
                    os.killpg(self.process.pid, signal.SIGKILL)
                except ProcessLookupError:
                    pass


class WorkbenchServer(ThreadingHTTPServer):
    daemon_threads = True
    allow_reuse_address = True

    def __init__(self, address, command, project_root, web_root, token,
                 request_timeout, max_concurrent):
        self.command = tuple(command)
        self.project_root = Path(project_root).resolve()
        self.web_root = Path(web_root).resolve()
        self.token = token
        self.request_timeout = request_timeout
        self.slots = threading.BoundedSemaphore(max_concurrent)
        self.active_condition = threading.Condition()
        self.active_children = set()
        self.closing = False
        self.closed = False
        super().__init__(address, WorkbenchRequestHandler)

    def start_child(self, command, timeout):
        with self.active_condition:
            if self.closing:
                return None
            options = {
                "cwd": str(self.project_root),
                "stdin": subprocess.PIPE,
                "stdout": subprocess.PIPE,
                "stderr": subprocess.PIPE,
                "shell": False,
                "close_fds": True,
            }
            if os.name == "nt":
                options["creationflags"] = subprocess.CREATE_NEW_PROCESS_GROUP | 0x00000004
            else:
                options["start_new_session"] = True
            process = subprocess.Popen(command, **options)
            try:
                child = ManagedChild(process)
            except Exception:
                process.kill()
                process.wait(timeout=timeout)
                for stream in (process.stdin, process.stdout, process.stderr):
                    stream.close()
                raise
            self.active_children.add(child)
            return child

    def finish_child(self, child):
        with self.active_condition:
            self.active_children.discard(child)
            self.active_condition.notify_all()

    def server_close(self):
        deadline = time.monotonic() + SHUTDOWN_GRACE_SECONDS
        with self.active_condition:
            self.closing = True
            children = list(self.active_children)
        for child in children:
            child.terminate()
        with self.active_condition:
            while self.active_children and time.monotonic() < deadline:
                self.active_condition.wait(deadline - time.monotonic())
                children = list(self.active_children)
                for child in children:
                    child.terminate()
            if not self.closed:
                self.closed = True
                super().server_close()


class WorkbenchRequestHandler(BaseHTTPRequestHandler):
    server_version = "AlterOS-Workbench"
    sys_version = ""

    def setup(self):
        super().setup()
        self.connection.settimeout(10)

    def log_message(self, format_string, *args):
        return

    def _expected_host(self):
        return "127.0.0.1:{}".format(self.server.server_port)

    def _send(self, status, body, content_type="application/json; charset=utf-8"):
        self.send_response(status)
        self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        self.send_header("X-Content-Type-Options", "nosniff")
        self.send_header("Referrer-Policy", "no-referrer")
        self.send_header("Content-Security-Policy",
                         "default-src 'self'; script-src 'self'; style-src 'self'; "
                         "connect-src 'self'; img-src 'self' data:; object-src 'none'; "
                         "base-uri 'none'; frame-ancestors 'none'")
        self.send_header("X-Frame-Options", "DENY")
        self.end_headers()
        self.wfile.write(body)

    def _error(self, status, code):
        body = json.dumps({"error": code}, separators=(",", ":")).encode("utf-8")
        self._send(status, body)

    def _host_allowed(self):
        if self.headers.get("Host", "") != self._expected_host():
            self._error(403, "host_rejected")
            return False
        return True

    def do_GET(self):
        if not self._host_allowed():
            return
        path = self.path.split("?", 1)[0]
        assets = {
            "/": ("index.html", "text/html; charset=utf-8"),
            "/app.js": ("app.js", "text/javascript; charset=utf-8"),
            "/style.css": ("style.css", "text/css; charset=utf-8"),
            "/workbench.js": ("workbench.js", "text/javascript; charset=utf-8"),
            "/graph-editor.js": ("graph-editor.js", "text/javascript; charset=utf-8"),
            "/draft-editor.js": ("draft-editor.js", "text/javascript; charset=utf-8"),
            "/activity-sequence-editor.js": ("activity-sequence-editor.js", "text/javascript; charset=utf-8"),
            "/playground.js": ("playground.js", "text/javascript; charset=utf-8"),
            "/editor-state.js": ("editor-state.js", "text/javascript; charset=utf-8"),
            "/lossless-json.js": ("lossless-json.js", "text/javascript; charset=utf-8"),
            "/workbench.css": ("workbench.css", "text/css; charset=utf-8"),
            "/preview.css": ("preview.css", "text/css; charset=utf-8"),
        }
        asset = assets.get(path)
        if asset is None:
            self._error(404, "not_found")
            return
        try:
            body = (self.server.web_root / asset[0]).read_bytes()
        except OSError:
            self._error(503, "web_asset_unavailable")
            return
        if path == "/":
            body = body.replace(b"@@WORKBENCH_TOKEN@@", html.escape(self.server.token).encode("ascii"))
        self._send(200, body, asset[1])

    def do_POST(self):
        if not self._host_allowed():
            return
        if self.path != "/api/command":
            self._error(404, "not_found")
            return
        expected_origin = "http://" + self._expected_host()
        if self.headers.get("Origin", "") != expected_origin:
            self._error(403, "origin_rejected")
            return
        supplied_token = self.headers.get("X-Workbench-Token", "")
        try:
            token_matches = secrets.compare_digest(supplied_token, self.server.token)
        except TypeError:
            token_matches = False
        if not token_matches:
            self._error(403, "token_rejected")
            return
        if self.headers.get("Transfer-Encoding"):
            self._error(400, "transfer_encoding_rejected")
            return
        if self.headers.get_content_type() != "application/json":
            self._error(415, "content_type_rejected")
            return
        try:
            length = int(self.headers.get("Content-Length", ""))
        except ValueError:
            self._error(400, "content_length_invalid")
            return
        if length < 0:
            self._error(400, "content_length_invalid")
            return
        if length > MAX_REQUEST_BYTES:
            self.close_connection = True
            self._error(413, "request_too_large")
            return
        body = self.rfile.read(length)
        if len(body) != length:
            self._error(400, "request_truncated")
            return
        try:
            body.decode("utf-8", errors="strict")
        except UnicodeDecodeError:
            self._error(400, "request_encoding_invalid")
            return
        if not self.server.slots.acquire(blocking=False):
            self._error(503, "host_busy")
            return
        try:
            status, output, error = self._run_host(body)
            if error:
                self._error(status, error)
                return
            try:
                json.loads(output.decode("utf-8", errors="strict"))
            except (UnicodeDecodeError, json.JSONDecodeError):
                self._error(502, "host_response_invalid")
                return
            self._send(200, output)
        finally:
            self.server.slots.release()

    def _run_host(self, request_bytes):
        deadline = time.monotonic() + self.server.request_timeout
        try:
            child = self.server.start_child(list(self.server.command), self.server.request_timeout)
        except OSError:
            return 502, b"", "host_start_failed"
        if child is None:
            return 503, b"", "host_stopping"
        process = child.process

        output = {"stdout": bytearray(), "stderr": bytearray()}
        overflow = threading.Event()
        limits = {"stdout": MAX_STDOUT_BYTES, "stderr": MAX_STDERR_BYTES}

        def drain(name, stream):
            while True:
                chunk = stream.read(8192)
                if not chunk:
                    return
                target = output[name]
                if len(target) + len(chunk) > limits[name]:
                    overflow.set()
                    child.terminate()
                elif not overflow.is_set():
                    target.extend(chunk)

        def write_input():
            try:
                process.stdin.write(request_bytes)
                process.stdin.flush()
            except (BrokenPipeError, OSError):
                pass
            finally:
                try:
                    process.stdin.close()
                except OSError:
                    pass

        readers = [
            threading.Thread(target=drain, args=("stdout", process.stdout), daemon=True),
            threading.Thread(target=drain, args=("stderr", process.stderr), daemon=True),
        ]
        writer = threading.Thread(target=write_input, daemon=True)
        for thread in readers:
            thread.start()
        writer.start()
        timed_out = False
        try:
            process.wait(timeout=max(0, deadline - time.monotonic()))
        except subprocess.TimeoutExpired:
            timed_out = True
        writer.join(timeout=max(0, deadline - time.monotonic()))
        for thread in readers:
            thread.join(timeout=max(0, deadline - time.monotonic()))
        io_failed = writer.is_alive() or any(thread.is_alive() for thread in readers)
        if timed_out or overflow.is_set() or io_failed or self.server.closing:
            child.terminate()
        cleanup_deadline = time.monotonic() + SHUTDOWN_GRACE_SECONDS
        if process.poll() is None:
            try:
                process.wait(timeout=max(0, cleanup_deadline - time.monotonic()))
            except subprocess.TimeoutExpired:
                child.terminate()
        writer.join(timeout=max(0, cleanup_deadline - time.monotonic()))
        for thread in readers:
            thread.join(timeout=max(0, cleanup_deadline - time.monotonic()))
        readers_done = not any(thread.is_alive() for thread in readers)
        if readers_done:
            process.stdout.close()
            process.stderr.close()
        if not writer.is_alive():
            process.stdin.close()
        child.close()
        self.server.finish_child(child)
        if self.server.closing:
            return 503, b"", "host_stopping"
        if timed_out:
            return 504, b"", "host_timeout"
        if overflow.is_set():
            return 502, b"", "host_output_too_large"
        if io_failed or not readers_done:
            return 502, b"", "host_io_failed"
        if process.returncode != 0:
            return 502, b"", "host_failed"
        return 200, bytes(output["stdout"]), ""


def create_server(command, project_root, web_root, token=None, port=8765,
                  request_timeout=20, max_concurrent=2):
    if not command or not all(isinstance(item, str) and item for item in command):
        raise ValueError("command must be a fixed non-empty argument list")
    if not 0 <= int(port) <= 65535 or request_timeout <= 0 or max_concurrent < 1:
        raise ValueError("invalid server limits")
    web_path = Path(web_root).resolve()
    if not web_path.is_dir():
        raise ValueError("web root must be an existing directory")
    return WorkbenchServer(
        ("127.0.0.1", int(port)), command, project_root, web_path,
        token or secrets.token_urlsafe(32), float(request_timeout), int(max_concurrent),
    )


def main():
    parser = argparse.ArgumentParser(description="Run the local Content Workbench transport.")
    parser.add_argument("--port", type=int, default=8765)
    args = parser.parse_args()
    host = WORKBENCH_ROOT / "CoreHost" / "bin" / "AlterOS.ContentWorkbench.exe"
    if not host.is_file():
        parser.error("command host is not built; run build-core.ps1 first")
    server = create_server(
        [str(host)], PROJECT_ROOT, WORKBENCH_ROOT / "web", port=args.port,
        request_timeout=20, max_concurrent=2,
    )
    url = "http://127.0.0.1:{}/".format(server.server_port)
    print("Content Workbench listening at {} (Ctrl+C to stop)".format(url), flush=True)
    webbrowser.open(url, new=2)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        server.server_close()


if __name__ == "__main__":
    main()
