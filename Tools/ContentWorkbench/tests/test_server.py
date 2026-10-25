import json
import ctypes
import http.client
import os
import subprocess
import sys
import tempfile
import threading
import time
import unittest
import urllib.error
import urllib.request
from pathlib import Path
from unittest import mock

WORKBENCH = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(WORKBENCH))
import server as workbench_server
from server import create_server


class WorkbenchServerTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="alteros-workbench-server-")
        self.root = Path(self.temp.name)
        self.log = self.root / "starts.log"
        self.fake_host = self.root / "fake_host.py"
        self.fake_host.write_text(
            "import json, os, sys, time\n"
            "import subprocess\n"
            "data = sys.stdin.buffer.read()\n"
            "with open({}, 'a', encoding='utf-8') as log: log.write('started\\n')\n".format(json.dumps(str(self.log)))
            + "request = json.loads(data.decode('utf-8'))\n"
            "mode = request.get('mode')\n"
            "if mode == 'sleep': time.sleep(2)\n"
            "if mode in ('spawn-retained', 'sleep-with-child'):\n"
            "    with open({!r}, 'w', encoding='ascii') as parent_log: parent_log.write(str(os.getpid()))\n".format(str(self.root / "parent.pid"))
            + "    child = subprocess.Popen([sys.executable, '-c', 'import time; time.sleep(4)'], stdout=sys.stdout, stderr=sys.stderr)\n"
            "    with open({!r}, 'w', encoding='ascii') as child_log: child_log.write(str(child.pid))\n".format(str(self.root / "child.pid"))
            + "    if mode == 'spawn-retained':\n"
            "        sys.stdout.buffer.write(b'{}\\n'); sys.exit(0)\n"
            "    time.sleep(4)\n"
            "if mode == 'stdout-large': sys.stdout.write('x' * (3 * 1024 * 1024)); sys.exit(0)\n"
            "if mode == 'stderr-large': sys.stderr.write('x' * (128 * 1024)); sys.exit(0)\n"
            "sys.stdout.buffer.write(json.dumps({'status': 'ok', 'request': request}, ensure_ascii=False).encode('utf-8') + b'\\n')\n",
            encoding="utf-8",
        )
        smoke = subprocess.run([sys.executable, str(self.fake_host)], cwd=str(self.root), input=b"{}", capture_output=True)
        self.assertEqual(0, smoke.returncode, smoke.stderr.decode("utf-8", errors="replace"))
        self.log.unlink()
        self.token = "test-token-not-secret"
        self.server = create_server(
            [sys.executable, str(self.fake_host)],
            self.root,
            web_root=WORKBENCH / "web",
            token=self.token,
            port=0,
            request_timeout=0.6,
            max_concurrent=1,
        )
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True)
        self.thread.start()
        self.base = "http://127.0.0.1:{}".format(self.server.server_port)
        self.origin = self.base

    def tearDown(self):
        self.server.shutdown()
        self.server.server_close()
        self.thread.join(timeout=2)
        self.temp.cleanup()

    def post(self, payload, token=None, origin=None, host=None):
        data = json.dumps(payload, ensure_ascii=False).encode("utf-8")
        request = urllib.request.Request(self.base + "/api/command", data=data, method="POST")
        request.add_header("Content-Type", "application/json; charset=utf-8")
        request.add_header("Origin", self.origin if origin is None else origin)
        request.add_header("X-Workbench-Token", self.token if token is None else token)
        if host is not None:
            request.add_header("Host", host)
        try:
            with urllib.request.urlopen(request, timeout=4) as response:
                return response.status, response.read()
        except urllib.error.HTTPError as error:
            return error.code, error.read()

    def wait_for_child(self, timeout=2):
        child_file = self.root / "child.pid"
        deadline = time.monotonic() + timeout
        while not child_file.exists() and time.monotonic() < deadline:
            time.sleep(0.01)
        self.assertTrue(child_file.exists(), "fake child process did not start")
        return int(child_file.read_text(encoding="ascii"))

    @staticmethod
    def is_process_running(pid):
        if os.name == "nt":
            from ctypes import wintypes

            kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
            kernel32.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
            kernel32.OpenProcess.restype = wintypes.HANDLE
            kernel32.GetExitCodeProcess.argtypes = [wintypes.HANDLE, ctypes.POINTER(wintypes.DWORD)]
            kernel32.GetExitCodeProcess.restype = wintypes.BOOL
            kernel32.CloseHandle.argtypes = [wintypes.HANDLE]
            handle = kernel32.OpenProcess(0x1000, False, pid)  # PROCESS_QUERY_LIMITED_INFORMATION
            if not handle:
                error = ctypes.get_last_error()
                if error == 87:
                    return False
                if error == 5:
                    return True
                raise ctypes.WinError(error)
            try:
                exit_code = wintypes.DWORD()
                if not kernel32.GetExitCodeProcess(handle, ctypes.byref(exit_code)):
                    raise ctypes.WinError(ctypes.get_last_error())
                return exit_code.value == 259  # STILL_ACTIVE
            finally:
                kernel32.CloseHandle(handle)
        try:
            os.kill(pid, 0)
            return True
        except ProcessLookupError:
            return False
        except PermissionError:
            return True
        except OSError as error:
            if os.name == "nt" and error.winerror == 87:
                return False
            raise

    @staticmethod
    def kill_test_process(pid):
        try:
            os.kill(pid, 9)
        except (OSError, ProcessLookupError):
            pass

    def kill_logged_test_processes(self):
        for name in ("parent.pid", "child.pid"):
            path = self.root / name
            if path.exists():
                self.kill_test_process(int(path.read_text(encoding="ascii")))

    def test_forwards_utf8_json_to_fixed_host_and_returns_its_output(self):
        payload = {"protocolVersion": 1, "requestId": "fixture", "text": "お茶を飲もう"}
        status, body = self.post(payload)
        self.assertEqual(200, status, body.decode("utf-8"))
        self.assertEqual(payload, json.loads(body.decode("utf-8"))["request"])
        self.assertEqual("started\n", self.log.read_text(encoding="utf-8"))

    def test_listener_is_bound_to_ipv4_loopback_only(self):
        self.assertEqual("127.0.0.1", self.server.server_address[0])

    def test_rejects_wrong_host_origin_and_token_without_starting_host(self):
        cases = [
            ("host", 403, {"host": "localhost:{}".format(self.server.server_port)}),
            ("origin", 403, {"origin": "http://evil.example"}),
            ("token", 403, {"token": "wrong"}),
        ]
        for _, expected, options in cases:
            status, _ = self.post({"protocolVersion": 1}, **options)
            self.assertEqual(expected, status)
        self.assertFalse(self.log.exists())

    def test_rejects_request_over_one_mib_before_starting_host(self):
        connection = http.client.HTTPConnection("127.0.0.1", self.server.server_port, timeout=4)
        connection.putrequest("POST", "/api/command")
        connection.putheader("Host", "127.0.0.1:{}".format(self.server.server_port))
        connection.putheader("Content-Type", "application/json")
        connection.putheader("Origin", self.origin)
        connection.putheader("X-Workbench-Token", self.token)
        connection.putheader("Content-Length", str(1024 * 1024 + 1))
        connection.endheaders()
        response = connection.getresponse()
        status = response.status
        response.read()
        connection.close()
        self.assertEqual(413, status)
        self.assertFalse(self.log.exists())

    def test_times_out_host_and_reports_gateway_timeout(self):
        status, body = self.post({"mode": "sleep"})
        self.assertEqual(504, status)
        self.assertEqual("host_timeout", json.loads(body.decode("utf-8"))["error"])

    def test_retained_descendant_pipe_does_not_block_request_deadline_or_slot(self):
        self.server.request_timeout = 0.6
        started = time.monotonic()
        status, body = self.post({"mode": "spawn-retained"})
        elapsed = time.monotonic() - started
        child_pid = int((self.root / "child.pid").read_text(encoding="ascii"))
        try:
            self.assertLess(elapsed, 2.0, "retained descendant pipe exceeded bounded cleanup")
            self.assertEqual(502, status)
            self.assertEqual("host_io_failed", json.loads(body.decode("utf-8"))["error"])
            self.assertFalse(self.is_process_running(child_pid), "retained-pipe descendant survived cleanup")
            self.server.request_timeout = 1
            next_status, _ = self.post({"protocolVersion": 1})
            self.assertEqual(200, next_status, "request slot was not released after pipe cleanup")
        finally:
            self.kill_logged_test_processes()

    def test_server_close_terminates_active_command_and_its_descendant(self):
        self.server.request_timeout = 10
        result = []
        request_thread = threading.Thread(target=lambda: result.append(self.post({"mode": "sleep-with-child"})))
        request_thread.start()
        child_pid = self.wait_for_child()
        started = time.monotonic()
        self.server.shutdown()
        self.server.server_close()
        elapsed = time.monotonic() - started
        try:
            self.assertLess(elapsed, 1.5, "server close exceeded bounded child cleanup")
            self.assertFalse(self.is_process_running(child_pid), "command descendant survived server close")
            parent_pid = int((self.root / "parent.pid").read_text(encoding="ascii"))
            self.assertFalse(self.is_process_running(parent_pid), "active command survived server close")
            request_thread.join(timeout=1)
            self.assertFalse(request_thread.is_alive(), "active HTTP request did not stop with the server")
        finally:
            self.kill_logged_test_processes()
            request_thread.join(timeout=1)

    @unittest.skipUnless(os.name == "nt", "Windows Job Object launch ordering")
    def test_windows_host_does_not_execute_before_job_attachment(self):
        marker = self.root / "host-started.txt"
        script = self.root / "startup_marker_host.py"
        script.write_text(
            "from pathlib import Path\n"
            "import sys\n"
            "Path({!r}).write_text('started', encoding='ascii')\n".format(str(marker))
            + "sys.stdin.buffer.read()\n"
            "sys.stdout.buffer.write(b'{\\\"status\\\":\\\"ok\\\"}\\n')\n",
            encoding="utf-8",
        )
        self.server.command = (sys.executable, str(script))
        self.server.request_timeout = 3
        attach_entered = threading.Event()
        allow_attach = threading.Event()
        marker_during_attach = []
        attach = workbench_server.ManagedChild._attach_windows_job

        def delayed_attach(process):
            attach_entered.set()
            if not allow_attach.wait(timeout=2):
                raise TimeoutError("test did not release Job attachment")
            marker_during_attach.append(marker.exists())
            return attach(process)

        result = []
        try:
            with mock.patch.object(workbench_server.ManagedChild, "_attach_windows_job", staticmethod(delayed_attach)):
                request_thread = threading.Thread(target=lambda: result.append(self.post({"protocolVersion": 1})))
                request_thread.start()
                self.assertTrue(attach_entered.wait(timeout=2), "host did not reach Job attachment")
                time.sleep(0.25)
                allow_attach.set()
                request_thread.join(timeout=4)
                self.assertFalse(request_thread.is_alive(), "startup-marker command did not finish")
            self.assertEqual([False], marker_during_attach, "host executed before Job membership was established")
            self.assertTrue(marker.exists(), "host did not resume after successful Job attachment")
            self.assertEqual(200, result[0][0])
        finally:
            allow_attach.set()

    @unittest.skipUnless(os.name == "nt", "Windows Job Object launch failure cleanup")
    def test_windows_job_attachment_failure_kills_suspended_host_without_running_it(self):
        marker = self.root / "host-started-on-failure.txt"
        script = self.root / "startup_marker_failure_host.py"
        script.write_text(
            "from pathlib import Path\n"
            "Path({!r}).write_text('started', encoding='ascii')\n".format(str(marker))
            + "import sys\n"
            "sys.stdin.buffer.read()\n"
            "sys.stdout.buffer.write(b'{\\\"status\\\":\\\"ok\\\"}\\n')\n",
            encoding="utf-8",
        )
        self.server.command = (sys.executable, str(script))
        process_ids = []
        ran_before_failure = []

        def fail_attachment(process):
            process_ids.append(process.pid)
            ran_before_failure.append(marker.exists())
            raise OSError("synthetic Job assignment failure")

        with mock.patch.object(workbench_server.ManagedChild, "_attach_windows_job", staticmethod(fail_attachment)):
            status, body = self.post({"protocolVersion": 1})
        self.assertEqual(502, status)
        self.assertEqual("host_start_failed", json.loads(body.decode("utf-8"))["error"])
        self.assertEqual([False], ran_before_failure)
        self.assertFalse(marker.exists(), "unowned host executed after Job assignment failed")
        self.assertEqual(1, len(process_ids))
        self.assertFalse(self.is_process_running(process_ids[0]), "suspended host survived assignment failure")

    def test_kills_host_when_stdout_or_stderr_exceeds_bound(self):
        for mode in ("stdout-large", "stderr-large"):
            status, body = self.post({"mode": mode})
            self.assertEqual(502, status)
            self.assertEqual("host_output_too_large", json.loads(body.decode("utf-8"))["error"])

    def test_rejects_concurrent_request_when_host_slot_is_busy(self):
        first_result = []
        first = threading.Thread(target=lambda: first_result.append(self.post({"mode": "sleep"})))
        first.start()
        deadline = time.monotonic() + 2
        while not self.log.exists() and time.monotonic() < deadline:
            time.sleep(0.01)
        status, body = self.post({"protocolVersion": 1})
        self.assertEqual(503, status)
        self.assertEqual("host_busy", json.loads(body.decode("utf-8"))["error"])
        first.join(timeout=4)
        self.assertEqual(1, len(first_result))
        self.assertEqual(504, first_result[0][0])

    def test_serves_same_origin_static_shell_with_session_token_and_security_headers(self):
        with urllib.request.urlopen(self.base + "/", timeout=4) as response:
            html = response.read().decode("utf-8")
            headers = response.headers
        self.assertIn('content="{}"'.format(self.token), html)
        self.assertEqual("nosniff", headers.get("X-Content-Type-Options"))
        self.assertIn("default-src 'self'", headers.get("Content-Security-Policy", ""))
        with urllib.request.urlopen(self.base + "/app.js", timeout=4) as response:
            self.assertEqual("text/javascript", response.headers.get_content_type())

    def test_serves_all_workbench_editor_modules_from_the_static_allowlist(self):
        expected = ("/workbench.js", "/graph-editor.js", "/draft-editor.js", "/activity-sequence-editor.js", "/playground.js", "/editor-state.js", "/lossless-json.js", "/workbench.css", "/preview.css")
        for path in expected:
            with self.subTest(path=path), urllib.request.urlopen(self.base + path, timeout=4) as response:
                self.assertEqual(200, response.status)
                self.assertIn(response.headers.get_content_type(), ("text/javascript", "text/css"))
                self.assertGreater(len(response.read()), 20)
        with urllib.request.urlopen(self.base + "/", timeout=4) as response:
            shell = response.read().decode("utf-8")
        self.assertIn('type="module" src="/app.js"', shell)
        self.assertIn("Reference picker", shell)


if __name__ == "__main__":
    unittest.main()
