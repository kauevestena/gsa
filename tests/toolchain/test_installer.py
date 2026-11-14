"""Behavior/security checks for local toolchain installs; no network or real tool downloads."""

import hashlib
import importlib.util
import io
import json
from pathlib import Path
import stat
import tarfile
import tempfile
import unittest
from unittest.mock import patch
import zipfile


ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("install_toolchain", ROOT / "scripts/install_toolchain.py")
installer = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(installer)


class InstallerTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)

    def artifact(self, path):
        return {
            "filename": path.name,
            "url": "https://example.invalid/fixture.zip",
            "sha512": hashlib.sha512(path.read_bytes()).hexdigest(),
        }

    def write_zip(self, name, entries):
        archive = self.root / name
        with zipfile.ZipFile(archive, "w") as output:
            for path, contents in entries:
                output.writestr(path, contents)
        return archive

    def test_corrupt_cache_fails_before_extraction_or_network(self):
        archive = self.write_zip("fixture.zip", [("editor", "trusted")])
        artifact = self.artifact(archive)
        archive.write_bytes(b"corrupted after manifest was produced")
        with patch.object(installer, "extract_zip") as extract, patch.object(
            installer.urllib.request, "urlopen"
        ) as network:
            with self.assertRaisesRegex(ValueError, "SHA512 mismatch"):
                installer.install(artifact, self.root, "installed")
            extract.assert_not_called()
            network.assert_not_called()
        self.assertFalse((self.root / "installed").exists())

    def test_corrupt_download_is_not_promoted_to_cache(self):
        artifact = {"filename": "fixture.zip", "url": "https://example.invalid/fixture.zip", "sha512": "0" * 128}
        with patch.object(installer.urllib.request, "urlopen", return_value=io.BytesIO(b"wrong bytes")):
            with self.assertRaisesRegex(ValueError, "SHA512 mismatch"):
                installer.download(artifact, self.root)
        self.assertFalse((self.root / "fixture.zip").exists())
        self.assertFalse((self.root / "fixture.zip.part").exists())

    def test_absolute_traversal_and_windows_paths_are_rejected(self):
        for path in ("../escape", "/escape", "C:/escape", "foo\\..\\escape", "\\\\server\\share", "ok/../../escape", "a/\x00b"):
            with self.subTest(path=path), self.assertRaises(ValueError):
                installer.safe_relative(path)

    def test_zip_traversal_cannot_write_outside_destination(self):
        archive = self.write_zip("traversal.zip", [("../escape", "unexpected")])
        with self.assertRaises(ValueError):
            installer.extract_zip(archive, self.root / "out")
        self.assertFalse((self.root / "escape").exists())

    def test_zip_symlink_is_rejected(self):
        archive = self.root / "symlink.zip"
        with zipfile.ZipFile(archive, "w") as output:
            link = zipfile.ZipInfo("link")
            link.create_system = 3
            link.external_attr = (stat.S_IFLNK | 0o777) << 16
            output.writestr(link, "../outside")
        with self.assertRaisesRegex(ValueError, "symlinks"):
            installer.extract_zip(archive, self.root / "out")
        self.assertFalse((self.root / "out/link").is_symlink())

    def test_tar_symlink_and_hardlink_are_rejected(self):
        for kind in (tarfile.SYMTYPE, tarfile.LNKTYPE):
            with self.subTest(kind=kind):
                archive = self.root / "links.tar.gz"
                with tarfile.open(archive, "w:gz") as output:
                    member = tarfile.TarInfo("link")
                    member.type = kind
                    member.linkname = "../outside"
                    output.addfile(member)
                with self.assertRaisesRegex(ValueError, "Unsupported archive member"):
                    installer.extract_tar(archive, self.root / "out")
                self.assertFalse((self.root / "out/link").exists())
                self.assertFalse((self.root / "out/link").is_symlink())

    def test_tar_regular_files_extract_with_content(self):
        archive = self.root / "files.tar.gz"
        with tarfile.open(archive, "w:gz") as output:
            member = tarfile.TarInfo("sdk/nested/file.txt")
            member.size = 7
            member.mode = 0o644
            output.addfile(member, io.BytesIO(b"fixture"))
        installer.extract_tar(archive, self.root / "out")
        self.assertEqual((self.root / "out/sdk/nested/file.txt").read_bytes(), b"fixture")

    def test_templates_keep_both_desktop_targets_and_support_data(self):
        desktop = {
            "linux_debug.x86_64", "linux_release.x86_64",
            "windows_debug_x86_64.exe", "windows_release_x86_64.exe",
            "windows_debug_x86_64_console.exe", "windows_release_x86_64_console.exe",
            "icudt_godot.dat", "version.txt",
        }
        entries = [(f"templates/{name}", f"contents:{name}") for name in desktop]
        entries += [("templates/android_source.zip", "not needed"), ("templates/linux_debug.arm64", "not needed")]
        archive = self.write_zip("templates.tpz", entries)
        installer.extract_zip(archive, self.root / "out", templates=True)
        self.assertEqual({p.name for p in (self.root / "out").iterdir()}, desktop)
        self.assertEqual((self.root / "out/icudt_godot.dat").read_text(), "contents:icudt_godot.dat")

    def test_missing_templates_do_not_create_completed_install(self):
        archive = self.write_zip("incomplete.tpz", [("templates/linux_release.x86_64", "incomplete")])
        with self.assertRaisesRegex(ValueError, "Missing desktop templates"):
            installer.install(self.artifact(archive), self.root, "templates", templates=True)
        self.assertFalse((self.root / "templates").exists())

    def test_second_install_reuses_completed_directory_but_rechecks_archive(self):
        archive = self.write_zip("fixture.zip", [("editor", "trusted")])
        artifact = self.artifact(archive)
        installed = installer.install(artifact, self.root, "installed")
        self.assertEqual((installed / "editor").read_text(), "trusted")
        self.assertEqual(json.loads((installed / ".gsa-install.json").read_text())["sha512"], artifact["sha512"])
        with patch.object(installer, "extract_zip") as extract:
            self.assertEqual(installer.install(artifact, self.root, "installed"), installed)
            extract.assert_not_called()
        archive.write_bytes(b"changed cached archive")
        with self.assertRaisesRegex(ValueError, "SHA512 mismatch"):
            installer.install(artifact, self.root, "installed")
        self.assertEqual((installed / "editor").read_text(), "trusted")


if __name__ == "__main__":
    unittest.main()
