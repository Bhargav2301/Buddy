"""Coordination failures must not accept stale or unauthorized worker artifacts."""
import copy
import hashlib
import importlib.util
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location('team_ledger', Path(__file__).resolve().parents[1]/'scripts/team-ledger.py')
team = importlib.util.module_from_spec(spec)
spec.loader.exec_module(team)

class TeamLedgerTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.checkout = patch.object(team, 'check_checkout')
        self.checkout.start(); self.addCleanup(self.checkout.stop)
        self.build = patch.object(team, 'build_snapshot', return_value='f'*64)
        self.build.start(); self.addCleanup(self.build.stop)
        (self.root/'sample.cs').write_text('fixture', encoding='utf-8')
        self.task = dict(id='SP-43', attempt=2, owner='/root/speech43', baseRevision='a'*40,
                         status='running', writeScope=['sample.cs'], acceptance=['fixture passes'], deliverable='patch', dependencies={}, worktree='speech43')
        self.ledger = {'schema': 1, 'tasks': [self.task]}
        self.handoff = dict(task='SP-43', attempt=2, owner='/root/speech43', baseRevision='a'*40, dependencies={},
                            summary='Fixture change', limitations=[], files=[dict(path='sample.cs', sha256=hashlib.sha256(b'fixture').hexdigest())],
                            tests=[dict(status='passed', command='fixture', environment='owned fixture', snapshot='f'*64, exitCode=0, log='fixture.txt')])

    def test_current_handoff_is_review_only(self):
        result = team.verify_handoff(self.ledger, self.handoff, self.root)
        self.assertEqual(result['result'], 'verified_for_review')
        self.assertFalse(result['integrated'])
        self.assertFalse(result['authorizationGranted'])

    def test_stale_attempt_owner_and_revision_rejected(self):
        for field, value in [('attempt', 1), ('owner', '/root/old-worker'), ('baseRevision', 'b'*40)]:
            with self.subTest(field=field):
                bad = copy.deepcopy(self.handoff); bad[field] = value
                with self.assertRaises(ValueError): team.verify_handoff(self.ledger, bad, self.root)

    def test_accepted_output_cannot_be_overwritten_by_late_worker(self):
        self.task['status'] = 'accepted'
        with self.assertRaises(ValueError): team.verify_handoff(self.ledger, self.handoff, self.root)

    def test_changed_file_rejected(self):
        (self.root/'sample.cs').write_text('late edit', encoding='utf-8')
        with self.assertRaises(ValueError): team.verify_handoff(self.ledger, self.handoff, self.root)

    def test_path_escape_and_other_ownership_rejected(self):
        for name in ['../sample.cs', '/sample.cs', 'C:/sample.cs', 'other.cs', 'dir\\sample.cs']:
            with self.subTest(name=name):
                bad = copy.deepcopy(self.handoff); bad['files'][0]['path'] = name
                with self.assertRaises(ValueError): team.verify_handoff(self.ledger, bad, self.root)

    def test_dependency_cycle_and_stale_generation_rejected(self):
        dep = dict(self.task, id='UX-43', dependencies={'SP-43': 2})
        self.task['dependencies'] = {'UX-43': 2}; self.ledger['tasks'].append(dep)
        with self.assertRaises(ValueError): team.validate(self.ledger)
        dep['dependencies'] = {}; dep['attempt'] = 3
        with self.assertRaises(ValueError): team.validate(self.ledger)

    def test_unaccepted_dependency_blocks_review(self):
        dep = dict(self.task, id='UX-43', dependencies={})
        self.task['dependencies'] = {'UX-43': 2}; self.handoff['dependencies'] = {'UX-43': 2}; self.ledger['tasks'].append(dep)
        with self.assertRaises(ValueError): team.verify_handoff(self.ledger, self.handoff, self.root)
        dep['status'] = 'accepted'
        dep['acceptedSnapshot'] = 'd'*64
        self.handoff['dependencySnapshots'] = {'UX-43': 'd'*64}
        self.assertEqual(team.verify_handoff(self.ledger, self.handoff, self.root)['task'], 'SP-43')
        dep['acceptedSnapshot'] = 'e'*64
        with self.assertRaises(ValueError): team.verify_handoff(self.ledger, self.handoff, self.root)

    def test_failed_command_cannot_be_claimed_passed(self):
        self.handoff['tests'][0]['exitCode'] = 1
        with self.assertRaises(ValueError): team.verify_handoff(self.ledger, self.handoff, self.root)

    def test_pause_blocks_intake_and_resume_rechecks_source(self):
        self.ledger['executionMode'] = 'paused'
        with self.assertRaises(ValueError): team.verify_handoff(self.ledger, self.handoff, self.root)
        self.ledger['executionMode'] = 'active'
        self.assertEqual(team.verify_handoff(self.ledger, self.handoff, self.root)['task'], 'SP-43')
        (self.root/'sample.cs').write_text('changed during pause', encoding='utf-8')
        with self.assertRaises(ValueError): team.verify_handoff(self.ledger, self.handoff, self.root)

    def test_passed_evidence_cannot_survive_changed_build_inputs(self):
        self.handoff['tests'][0]['snapshot'] = 'old-snapshot'
        with self.assertRaises(ValueError): team.verify_handoff(self.ledger, self.handoff, self.root)

    def test_real_checkout_path_and_head_checked(self):
        self.checkout.stop()
        with self.assertRaises(ValueError): team.check_checkout(self.task, self.root, self.root/'integration')
        expected = self.root/'workers'/'speech43'
        with patch.object(team.subprocess, 'check_output', return_value=b'b' * 40):
            with self.assertRaises(ValueError): team.check_checkout(self.task, expected, self.root/'integration')
        with patch.object(team.subprocess, 'check_output', return_value=b'a' * 40):
            team.check_checkout(self.task, expected, self.root/'integration')
        self.checkout.start()

    def test_embedded_json_and_ocr_inputs_change_snapshot(self):
        self.build.stop()
        rows = [{'path': 'apps/windows/refine-apps.json', 'sha256': 'a'}, {'path': 'apps/windows/tessdata/eng.traineddata', 'sha256': 'b'}]
        with patch.object(team, 'snapshot', return_value={'files': rows}):
            before = team.build_snapshot(self.root)
            rows[0]['sha256'] = 'changed-json'
            self.assertNotEqual(before, team.build_snapshot(self.root))
            before = team.build_snapshot(self.root)
            rows[1]['sha256'] = 'changed-ocr'
            self.assertNotEqual(before, team.build_snapshot(self.root))
        self.build.start()

if __name__ == '__main__': unittest.main()
