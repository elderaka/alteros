import hashlib
import os
import sys
import tempfile
import unittest
from pathlib import Path
from subprocess import CompletedProcess
from unittest.mock import patch

TESTS = Path(__file__).resolve().parent
WORKBENCH = TESTS.parent
ROOT = WORKBENCH.parents[1]
sys.path.insert(0, str(TESTS))
import test_commands
import test_authoring_sqlite
import test_authoring_interchange


class WorkbenchTestBuildIsolationTests(unittest.TestCase):
    def test_legacy_command_classes_build_only_into_their_private_temp_directory(self):
        shared_bin = WORKBENCH / "CoreHost" / "bin"

        def shared_hashes():
            return {item.name: hashlib.sha256(item.read_bytes()).hexdigest()
                    for item in shared_bin.iterdir() if item.is_file()}

        before = shared_hashes()
        for module, case in (
            (test_commands, test_commands.WorkbenchCommandTests),
            (test_authoring_sqlite, test_authoring_sqlite.AuthoringSqliteTests),
            (test_authoring_interchange, test_authoring_interchange.AuthoringInterchangeTests),
        ):
            calls = []

            def fake_build(command, **kwargs):
                calls.append(command)
                output_index = command.index("-OutputDirectory") + 1 if "-OutputDirectory" in command else -1
                if output_index >= 0:
                    output = Path(command[output_index])
                    output.mkdir(parents=True, exist_ok=True)
                    (output / "AlterOS.ContentWorkbench.exe").write_bytes(b"synthetic isolated host")
                return CompletedProcess(command, 0, "synthetic build", "")

            with patch.object(module.subprocess, "run", side_effect=fake_build):
                try:
                    case.setUpClass()
                    self.assertTrue(calls, case.__name__ + " did not run a build")
                    self.assertIn("-OutputDirectory", calls[0], case.__name__ + " built into shared/default output")
                    build_dir = Path(calls[0][calls[0].index("-OutputDirectory") + 1]).resolve()
                    self.assertTrue(build_dir.is_relative_to(Path(tempfile.gettempdir()).resolve()))
                    self.assertFalse(build_dir.is_relative_to(shared_bin.resolve()))
                    if case is test_commands.WorkbenchCommandTests:
                        self.assertTrue(Path(case.host).is_relative_to(build_dir.parent))
                    else:
                        self.assertEqual(build_dir, Path(case.host_bin).resolve())
                        self.assertTrue((build_dir / "AlterOS.ContentWorkbench.exe").is_file())
                finally:
                    case.tearDownClass()
                    case.doClassCleanups()

        self.assertEqual(before, shared_hashes(), "legacy test setup modified shared Workbench binaries")


if __name__ == "__main__":
    unittest.main()
