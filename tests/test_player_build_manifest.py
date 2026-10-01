"""Receipt/provenance tests with tiny synthetic files; never launch Unity."""
import hashlib
import importlib.util
import json
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest
from unittest import mock
import warnings
import zipfile

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('player_build_manifest', ROOT / 'tools/player_build_manifest.py')
manifest = importlib.util.module_from_spec(spec)
spec.loader.exec_module(manifest)


class PlayerBuildManifestTests(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.root = Path(temp.name)
        self.repo = self.root / 'repo'
        self.repo.mkdir()
        for name, content in {
            'Assets/Game.cs': b'public class Game {}\n',
            'Assets/Stone.mat': b'color: gray\n',
            'Assets/Torch.mat': b'color: red\n',
            'Packages/manifest.json': b'{"dependencies":{}}\n',
            'ProjectSettings/ProjectVersion.txt': b'm_EditorVersion: 6000.6.3f1\n',
        }.items():
            p = self.repo / name
            p.parent.mkdir(parents=True, exist_ok=True)
            p.write_bytes(content)
        for command in [('init', '-q'), ('config', 'user.email', 'test@example.invalid'),
                        ('config', 'user.name', 'Receipt fixture'), ('add', '.'), ('commit', '-qm', 'Fixture')]:
            subprocess.run(['git', '-C', str(self.repo), *command], check=True, capture_output=True)
        self.commit = manifest.git(self.repo, 'rev-parse', 'HEAD').decode().strip()
        self.original = manifest.committed_inputs(self.repo, self.commit)
        self.project = self.root / 'project'
        for folder in manifest.SOURCE_ROOTS:
            shutil.copytree(self.repo / folder, self.project / folder)
        self.source = self.root / 'source.zip'
        self.imported = self.root / 'imported.zip'
        self.zip_source(self.repo, self.source)
        self.zip_source(self.project, self.imported)
        self.player = self.root / 'player'
        (self.player / 'Game_Data').mkdir(parents=True)
        for name, raw in [('Game.exe', b'engine-exe'), ('UnityPlayer.dll', b'engine-dll'), ('Game_Data/data.bin', b'data')]:
            (self.player / name).write_bytes(raw)
        self.runner = self.root / 'run-build.ps1'
        self.runner.write_text('# retained fixture runner\n')
        self.log = self.root / 'editor.log'
        self.log.write_text('Castle Windows build: Succeeded; errors=0; warnings=0; bytes=24; duration=00:00:01\n')
        self.execution = self.root / 'execution.json'
        self.write_execution()
        self.completion = self.root / 'completion.json'
        self.capture()

    def zip_source(self, folder, output):
        with zipfile.ZipFile(output, 'w') as archive:
            for source in manifest.SOURCE_ROOTS:
                for p in sorted((folder / source).rglob('*')):
                    if p.is_file():
                        archive.write(p, p.relative_to(folder).as_posix())

    def write_execution(self, **changes):
        data = dict(mode='build', exit_code=0, stopped=None, source_sha256=self.original,
                    runner_sha256=manifest.digest(self.runner), player_output=str(self.player / 'Game.exe'),
                    command=['unity', 'run', str(self.project), '--editor-path', '/Unity/6000.6.3f1/Editor/Unity.exe',
                             '--log-file', str(self.log), '--', '-executeMethod', 'CastlePlayerBuild.BuildForBatch'])
        data.update(changes)
        self.execution.write_text(json.dumps(data))

    def capture(self):
        value = manifest.capture(self.execution, self.log, self.runner, self.player, self.project, self.imported)
        self.completion.write_text(json.dumps(value))
        return value

    def create(self, review=None):
        return manifest.create(self.repo, self.commit, self.source, self.imported, review, self.completion,
                               self.execution, self.log, self.runner, self.player)

    def publish(self):
        return manifest.write_once(self.player / manifest.RECEIPT, self.create())

    def review(self, name='Assets/Stone.mat'):
        imported = manifest.zip_inputs(self.imported)
        names = (name,) if isinstance(name, str) else name
        path = self.root / 'review.json'
        path.write_text(json.dumps(dict(schema_version=1, source_commit=self.commit,
            source_archive_sha256=manifest.digest(self.source), imported_archive_sha256=manifest.digest(self.imported),
            unreviewed_pairs=0, unresolved_findings=0, differences=[dict(path=name,
                source_sha256=self.original.get(name), imported_sha256=imported[name], reason='Reviewed import serialization')
                for name in names])) )
        return path

    def mixed_imports(self):
        changes = {
            'Assets/Stone.mat': b'color: gray\nnormalized: true\n',
            'Assets/Torch.mat': b'color: red\nnormalized: true\n',
            'ProjectSettings/SceneTemplateSettings.json': b'{"templates":[]}\n',
        }
        for name, content in changes.items():
            (self.project / name).write_bytes(content)
        self.zip_source(self.project, self.imported)
        imported = manifest.zip_inputs(self.imported)
        before = dict(self.original)
        # One asset is already imported, another still has its committed bytes.
        # The generated settings file exists before this incremental build.
        before['Assets/Stone.mat'] = imported['Assets/Stone.mat']
        before['ProjectSettings/SceneTemplateSettings.json'] = imported['ProjectSettings/SceneTemplateSettings.json']
        return before, self.review(tuple(changes))

    def test_create_verify_and_idempotent_creation_exclude_receipt(self):
        receipt = self.create()
        self.assertEqual(self.commit, receipt['source_commit'])
        self.assertEqual(manifest.digest(self.source), receipt['source_archive_sha256'])
        self.assertEqual(3, receipt['engine_file_count'])
        self.assertEqual(24, receipt['engine_bytes'])
        hashed = self.publish()
        self.assertEqual(hashed, manifest.write_once(self.player / manifest.RECEIPT, self.create()))
        self.assertEqual('verified', manifest.verify(self.player, hashed, self.source, self.commit)['status'])
        self.assertNotIn(manifest.RECEIPT, [x['path'] for x in receipt['engine_files']])

    def test_rejects_wrong_commit(self):
        self.commit = 'f' * 40
        with self.assertRaisesRegex(ValueError, 'HEAD differs'):
            self.create()

    def test_rejects_dirty_missing_and_extra_source(self):
        for case in ('dirty', 'missing', 'extra'):
            with self.subTest(case=case):
                path = self.repo / ('Assets/Extra.cs' if case == 'extra' else 'Assets/Game.cs')
                original = path.read_bytes() if path.exists() else None
                if case == 'missing':
                    path.unlink()
                else:
                    path.write_bytes(b'changed source')
                with self.assertRaisesRegex(ValueError, 'Dirty, missing or extra'):
                    self.create()
                if original is None:
                    path.unlink()
                else:
                    path.write_bytes(original)

    def test_rejects_staged_different_source_even_when_worktree_is_restored(self):
        source = self.repo / 'Assets/Game.cs'
        raw = source.read_bytes()
        source.write_bytes(b'staged difference')
        manifest.git(self.repo, 'add', 'Assets/Game.cs')
        source.write_bytes(raw)
        with self.assertRaisesRegex(ValueError, 'Staged Unity'):
            self.create()

    def test_rejects_unrelated_source_archive(self):
        with zipfile.ZipFile(self.source, 'w') as archive:
            archive.writestr('Assets/Other.cs', b'different')
        with self.assertRaisesRegex(ValueError, 'archive differs'):
            self.create()

    def test_rejects_same_size_player_substitution(self):
        (self.player / 'Game.exe').write_bytes(b'other--exe')
        with self.assertRaisesRegex(ValueError, 'Player files differ'):
            self.create()

    def test_rejects_missing_extra_and_renamed_player_file(self):
        path = self.player / 'Game_Data/data.bin'
        path.rename(path.with_name('other.bin'))
        with self.assertRaisesRegex(ValueError, 'Player files differ'):
            self.create()

    def test_rejects_failed_or_interrupted_execution(self):
        for changes in ({'exit_code': 1}, {'stopped': 'Disk reserve reached'}, {'mode': 'playmode'}):
            self.write_execution(**changes)
            with self.assertRaisesRegex(ValueError, 'did not finish'):
                self.capture()

    def test_rejects_wrong_player_directory_and_log(self):
        self.write_execution(player_output=str(self.root / 'other/Game.exe'))
        with self.assertRaisesRegex(ValueError, 'output directory'):
            self.capture()
        self.write_execution()
        other = self.root / 'other.log'
        other.write_text(self.log.read_text())
        with self.assertRaisesRegex(ValueError, 'log path'):
            manifest.capture(self.execution, other, self.runner, self.player, self.project, self.imported)

    def test_rejects_changed_runner_or_execution_after_completion(self):
        self.runner.write_text('modified runner')
        with self.assertRaisesRegex(ValueError, 'runner hash'):
            self.create()

    def test_rejects_duplicate_success_summary_or_wrong_total(self):
        original = self.log.read_text()
        for text in (original + original, original.replace('bytes=24', 'bytes=25')):
            self.log.write_text(text)
            with self.assertRaises(ValueError):
                self.capture()

    def test_rejects_frozen_source_not_matching_completed_project(self):
        (self.project / 'Assets/Stone.mat').write_bytes(b'new import')
        with self.assertRaisesRegex(ValueError, 'actual completed project'):
            self.capture()

    def test_accepts_exact_reviewed_import_pair(self):
        (self.project / 'Assets/Stone.mat').write_bytes(b'color: gray\nnormalized: true\n')
        self.zip_source(self.project, self.imported)
        self.capture()
        with self.assertRaisesRegex(ValueError, 'reviewed hash pairs'):
            self.create()
        receipt = self.create(self.review())
        self.assertEqual(1, len(receipt['import_review']['differences']))

    def test_accepts_mixed_exact_reviewed_prebuild_hashes_and_records_the_map(self):
        before, review = self.mixed_imports()
        imported = manifest.zip_inputs(self.imported)
        for generated_already_present in (False, True):
            with self.subTest(generated_already_present=generated_already_present):
                pre_build = dict(before)
                if not generated_already_present:
                    del pre_build['ProjectSettings/SceneTemplateSettings.json']
                self.assertNotEqual(self.original, pre_build)
                self.assertNotEqual(imported, pre_build)
                self.write_execution(source_sha256=pre_build)
                self.capture()
                receipt = self.create(review)
                self.assertEqual(pre_build, receipt['pre_build_source_sha256'])
                self.assertEqual(imported, receipt['imported_source_sha256'])
                self.assertEqual(3, len(receipt['import_review']['differences']))

    def test_rejects_third_prebuild_hash_for_reviewed_or_unchanged_file(self):
        before, review = self.mixed_imports()
        for name in ('Assets/Stone.mat', 'Assets/Game.cs'):
            with self.subTest(path=name):
                pre_build = dict(before)
                pre_build[name] = hashlib.sha256(b'unreviewed intermediate payload').hexdigest()
                self.write_execution(source_sha256=pre_build)
                self.capture()
                with self.assertRaisesRegex(ValueError, 'pre-build inputs'):
                    self.create(review)
                self.assertFalse((self.player / manifest.RECEIPT).exists())

    def test_rejects_missing_committed_prebuild_file(self):
        before, review = self.mixed_imports()
        del before['Assets/Torch.mat']
        self.write_execution(source_sha256=before)
        self.capture()
        with self.assertRaisesRegex(ValueError, 'pre-build inputs'):
            self.create(review)

    def test_rejects_extra_prebuild_path_absent_from_reviewed_import(self):
        before, review = self.mixed_imports()
        before['Assets/Unknown.meta'] = hashlib.sha256(b'unexpected metadata').hexdigest()
        self.write_execution(source_sha256=before)
        self.capture()
        with self.assertRaisesRegex(ValueError, 'pre-build inputs'):
            self.create(review)

    def test_rejects_null_prebuild_hash_including_import_only_addition(self):
        before, review = self.mixed_imports()
        for name in ('Assets/Stone.mat', 'ProjectSettings/SceneTemplateSettings.json'):
            with self.subTest(path=name):
                pre_build = dict(before)
                pre_build[name] = None
                self.write_execution(source_sha256=pre_build)
                self.capture()
                with self.assertRaisesRegex(ValueError, 'pre-build inputs'):
                    self.create(review)

    def test_rejects_completion_prebuild_map_different_from_execution(self):
        before, review = self.mixed_imports()
        self.write_execution(source_sha256=before)
        captured = self.capture()
        captured['source_sha256'] = self.original
        self.completion.write_text(json.dumps(captured))
        with self.assertRaisesRegex(ValueError, 'pre-build inputs'):
            self.create(review)

    def test_rejects_non_mapping_prebuild_inputs(self):
        _, review = self.mixed_imports()
        for malformed in (None, [], 'not a source map'):
            with self.subTest(value=malformed):
                self.write_execution(source_sha256=malformed)
                self.capture()
                with self.assertRaisesRegex(ValueError, 'pre-build inputs'):
                    self.create(review)

    def test_rejects_mismatched_or_incomplete_import_review(self):
        (self.project / 'Assets/Stone.mat').write_bytes(b'normalized')
        self.zip_source(self.project, self.imported)
        self.capture()
        path = self.review()
        original = json.loads(path.read_text())
        for key, value in [('source_commit', 'f' * 40), ('imported_archive_sha256', '0' * 64),
                           ('unresolved_findings', 1), ('differences', [])]:
            broken = dict(original)
            broken[key] = value
            path.write_text(json.dumps(broken))
            with self.assertRaises(ValueError):
                self.create(path)

    def test_rejects_reviewed_csharp_drift(self):
        (self.project / 'Assets/Game.cs').write_bytes(b'different gameplay')
        self.zip_source(self.project, self.imported)
        self.capture()
        with self.assertRaisesRegex(ValueError, 'code/config/removal'):
            self.create(self.review('Assets/Game.cs'))

    def test_rejects_completion_from_another_frozen_archive(self):
        self.zip_source(self.repo, self.imported)
        with zipfile.ZipFile(self.imported, 'a') as archive:
            archive.comment = b'changed archive identity'
        with self.assertRaisesRegex(ValueError, 'not captured'):
            self.create()

    def test_partial_or_conflicting_receipt_is_never_overwritten(self):
        path = self.player / manifest.RECEIPT
        for raw in (b'{"source_commit":', b'{"source_commit":"another-build"}\n'):
            path.write_bytes(raw)
            with self.assertRaisesRegex(ValueError, 'refusing overwrite'):
                manifest.write_once(path, self.create())
            self.assertEqual(raw, path.read_bytes())

    def test_portable_verify_rejects_receipt_and_output_tampering(self):
        hashed = self.publish()
        with self.assertRaisesRegex(ValueError, 'trusted hash'):
            manifest.verify(self.player, '0' * 64)
        with self.assertRaisesRegex(ValueError, 'source commit'):
            manifest.verify(self.player, hashed, commit='f' * 40)
        (self.player / 'Game_Data/data.bin').write_bytes(b'DATA')
        with self.assertRaisesRegex(ValueError, 'Engine files differ'):
            manifest.verify(self.player, hashed)

    def test_rejects_archive_traversal_and_duplicate_entries(self):
        for names in (['../Assets/Escape.cs'], ['Assets/Game.cs', 'Assets/Game.cs']):
            path = self.root / 'bad.zip'
            with warnings.catch_warnings():
                warnings.simplefilter('ignore', UserWarning)
                with zipfile.ZipFile(path, 'w') as archive:
                    for name in names:
                        archive.writestr(name, b'data')
            with self.assertRaises(ValueError):
                manifest.zip_inputs(path)

    def test_rejects_archive_container_changed_after_payload_read(self):
        original_digest = manifest.digest
        calls = 0
        def racing_digest(path):
            nonlocal calls
            if Path(path) == self.source:
                calls += 1
                if calls == 2:
                    with zipfile.ZipFile(path, 'a') as archive:
                        archive.comment = b'container replaced after payload validation'
            return original_digest(path)
        with mock.patch.object(manifest, 'digest', side_effect=racing_digest):
            with self.assertRaisesRegex(ValueError, 'Archive changed'):
                self.create()
        self.assertFalse((self.player / manifest.RECEIPT).exists())

    def test_reviewed_settings_import_is_narrowly_scoped(self):
        original = {'ProjectSettings/ProjectSettings.asset': 'a' * 64}
        imported = {'ProjectSettings/ProjectSettings.asset': 'b' * 64}
        path = self.root / 'settings-review.json'
        data = dict(schema_version=1, source_commit=self.commit, source_archive_sha256='c' * 64,
                    imported_archive_sha256='d' * 64, unreviewed_pairs=0, unresolved_findings=0,
                    differences=[dict(path='ProjectSettings/ProjectSettings.asset', source_sha256='a' * 64,
                                      imported_sha256='b' * 64, reason='Reviewed importer version normalization')])
        path.write_text(json.dumps(data))
        self.assertIsNotNone(manifest.import_review(path, self.commit, original, imported, 'c' * 64, 'd' * 64))
        data['differences'][0]['path'] = 'Packages/manifest.json'
        path.write_text(json.dumps(data))
        with self.assertRaisesRegex(ValueError, 'code/config/removal'):
            manifest.import_review(path, self.commit, {'Packages/manifest.json': 'a' * 64},
                                   {'Packages/manifest.json': 'b' * 64}, 'c' * 64, 'd' * 64)

    def test_accepts_only_exact_reviewed_added_scene_template_settings(self):
        name = 'ProjectSettings/SceneTemplateSettings.json'
        (self.project / name).write_bytes(b'{"templates":[]}\n')
        self.zip_source(self.project, self.imported)
        self.capture()
        receipt = self.create(self.review(name))
        self.assertIsNone(receipt['import_review']['differences'][0]['source_sha256'])
        self.assertIn(name, receipt['imported_source_sha256'])
        (self.project / name).unlink()
        unknown = 'ProjectSettings/UnreviewedSettings.json'
        (self.project / unknown).write_bytes(b'{}\n')
        self.zip_source(self.project, self.imported)
        self.capture()
        with self.assertRaisesRegex(ValueError, 'code/config/removal'):
            self.create(self.review(unknown))

    def test_rejects_source_and_player_symlinks(self):
        linked = self.player / 'linked.dll'
        try:
            linked.symlink_to(self.runner)
        except OSError:
            self.skipTest('Symlinks unavailable for this platform account')
        with self.assertRaisesRegex(ValueError, 'Linked entry'):
            self.create()

    def test_cli_verify_and_optimized_python_keep_validation(self):
        hashed = self.publish()
        command = [sys.executable, '-B', '-O', str(ROOT / 'tools/player_build_manifest.py'), 'verify',
                   '--player-dir', str(self.player), '--receipt-sha256', hashed]
        passed = subprocess.run(command, capture_output=True, text=True, timeout=15)
        self.assertEqual(0, passed.returncode, passed.stderr)
        (self.player / 'Game.exe').write_bytes(b'other--exe')
        failed = subprocess.run(command, capture_output=True, text=True, timeout=15)
        self.assertNotEqual(0, failed.returncode)
        self.assertIn('Engine files differ', failed.stderr)


if __name__ == '__main__':
    unittest.main()
