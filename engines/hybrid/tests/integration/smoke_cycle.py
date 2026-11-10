"""Real bounded Mac workflow; retains evidence in a unique temporary directory."""
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import time

ROOT = Path(__file__).resolve().parents[4]
ENGINE = ROOT / 'engines/hybrid'


def main():
    assert (ENGINE / 'scripts/smoke_cycle.sh').is_file(), 'missing integrated smoke launcher'
    import numpy as np
    import torch
    from hybrid_training.checkpoint import load_checkpoint
    from hybrid_training.models import HybridNet, BoundaryNet, normalize_output
    from hybrid_training.registry import Registry, read, publish, file_hash
    from hybrid_training.promotion import create_evaluation, evaluate_and_report

    state = Path(tempfile.mkdtemp(prefix='bid euchre smoke ')).resolve()
    print('Evidence: ' + str(state), flush=True)
    started = time.monotonic()
    evidence = {'state': str(state), 'commands': [], 'status': 'running'}
    env = dict(os.environ, DOTNET_NOLOGO='1', DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1')
    dotnet = env['BIDEUCHRE_DOTNET']
    user_registry = Path(env.get('HYBRID_REGISTRY', ENGINE / 'state/registry')) / 'champion.json'
    user_before = user_registry.read_bytes() if user_registry.exists() else None

    def run(name, command, *, extra=None, input=None, timeout=60):
        began = time.monotonic()
        result = subprocess.run([str(x) for x in command], cwd=state, env={**env, **(extra or {})},
                                input=input, text=True, capture_output=True, timeout=timeout)
        (state / (name + '.log')).write_text(result.stdout + result.stderr)
        evidence['commands'].append({'name': name, 'argv': [str(x) for x in command],
                                    'seconds': time.monotonic()-began, 'returncode': result.returncode})
        assert result.returncode == 0, name + ': ' + result.stdout[-2000:] + result.stderr[-2000:]
        return result.stdout

    try:
        fixtures = state / 'authority.jsonl'
        run('authority', [dotnet, 'run', '--project', ROOT/'src/BidEuchre.Arena', '--no-build', '--',
                          'export-fixtures', '--output', fixtures])
        run('replay', [ENGINE/'build/bideuchre-hybrid-replay', '--fixtures', fixtures])
        evidence['authority_traces'] = len(fixtures.read_text().splitlines())
        training = state / 'training'
        common = [sys.executable, '-m', 'hybrid_training.cli']
        run('generate-initialize', [*common, 'cycle', '--no-registry', '--output', training, '--device', 'cpu',
            '--execution-mode', 'fixed_seeded', '--duration-seconds', '20', '--max-updates', '1', '--batch-size', '8'])
        before = load_checkpoint(training/'checkpoint.pt')
        run('optimizer', [*common, 'resume', '--output', training, '--duration-seconds', '15', '--max-updates', '4'])
        after = load_checkpoint(training/'checkpoint.pt')
        assert after['updates'] - before['updates'] == 4
        changed = [name for name in before['main'] if not torch.equal(before['main'][name], after['main'][name])]
        assert changed, 'optimizer did not change parameters'
        candidate_path = Path(after['export_manifest'])
        shard = next((training/'replay').glob('*/manifest.json'))
        replay = read(shard)
        assert replay['footer']['status'] == 'complete' and replay['footer']['winner'] in (0, 1)
        arrays = {k: np.load(shard.parent/v['file'], mmap_mode='r') for k,v in replay['arrays'].items()}
        row = {k: arrays[k][0].tolist() for k in ('features', 'legal', 'boundary_features')}
        inputs = state/'native-input.jsonl'; inputs.write_text(json.dumps(row)+'\n')
        native = json.loads(run('export-parity', [ENGINE/'build/bideuchre-hybrid-model-check',
                           '--manifest', candidate_path, '--inputs', inputs]))
        main_net, boundary = HybridNet().eval(), BoundaryNet().eval()
        main_net.load_state_dict(after['main']); boundary.load_state_dict(after['boundary'])
        with torch.no_grad():
            raw = main_net(torch.tensor([row['features']]))
            normalized = normalize_output(raw, torch.tensor([row['legal']]))
            expected = {**raw, **normalized, 'boundary_logit': boundary(torch.tensor([row['boundary_features']]))}
        errors = {}
        for key, value in expected.items():
            value = value.numpy().reshape(-1)
            np.testing.assert_allclose(native[key], value, rtol=1e-5, atol=1e-5)
            errors[key] = float(np.max(np.abs(np.asarray(native[key]).reshape(-1)-value)))

        # No Python executable is present in this PATH; the absolute dotnet host
        # and shell/native launchers exercise production EngineProcessClient.
        clean = state/'clean path'; clean.mkdir()
        for name, target in [('dirname', '/usr/bin/dirname')]: (clean/name).symlink_to(target)
        live_env = {'PATH': str(clean), 'HYBRID_MODEL': str(candidate_path),
                    'BIDEUCHRE_HYBRID_ENGINE': str(ENGINE/'run.sh'),
                    'BIDEUCHRE_HYBRID_ARGUMENTS': '--max-simulations 7'}
        host = run('four-production-seats', [dotnet, 'run', '--project', ROOT/'tests/BidEuchre.Tests',
                    '--no-build', '--', '--filter', 'Hybrid engine completes'], extra=live_env, timeout=60)
        assert '1/1 tests passed' in host and 'PASS' in host

        config = read(ENGINE/'config/screening.example.json')
        config['baseline'] = {'executable': str(ROOT/'engines/cpp-heuristic/build/bideuchre-cpp-heuristic'),
            'arguments': ['--samples','16','--play-ms','200','--search-depth','1','--search-nodes','1000','--seed','42'],
            'source_files': [str(p) for p in sorted((ROOT/'engines/cpp-heuristic/src').glob('*')) if p.suffix in ('.cpp','.hpp')]}
        config['hybrid'] = {'executable': str(ENGINE/'build/bideuchre-hybrid'),
            'arguments': ['--decision-ms','200','--stop-ms','250','--max-simulations','7','--seed','42']}
        config['arena'] = str(ROOT/'src/BidEuchre.Arena/bin/Debug/net10.0/BidEuchre.Arena')
        config['budget'] = {'DecisionMs':200, 'StopMs':250, 'ProcessMs':1000, 'StartupMs':5000}
        config['resources']['max_seconds'] = 60
        registry_root = state/'registry with spaces'; registry_root.mkdir()
        config.update(registry_root=str(registry_root), run_seed=14000)
        publish(registry_root/'config.json', config)
        registry = Registry(registry_root)
        champion_before = (registry_root/'champion.json').read_bytes()
        candidate = registry.register_candidate(candidate_path)
        # Explicit and environment-default registry launch preserve the baseline
        # champion's complete frozen descriptor, from a different working directory.
        for name, args, extra in [('explicit-registry', ['--registry', str(registry_root)], {}),
                                 ('default-registry', [], {'HYBRID_REGISTRY': str(registry_root)})]:
            output = run(name, [ENGINE/'run.sh', *args], extra={'PATH':str(clean), **extra}, input='beuci\nquit\n')
            assert 'C++ Heuristic Bot' in output and 'beuciok' in output
        seeds = state/'seeds.json'
        publish(seeds, {'registry_root':str(registry_root), 'run_seed':14000, 'start_index':0,
                        'primary_blocks':1, 'replacement_blocks':1, 'bootstrap_seed':271828})
        manifest = create_evaluation(candidate, registry.current()['id'], seeds, 'screening')
        report_path = evaluate_and_report(manifest, Path(config['arena']), 60)
        report = read(report_path)
        assert report['CanPromote'] is False
        assert report['Complete'] is True and report['CompletedLegs'] == 4 and report['GateErrors'] == []
        assert champion_before == (registry_root/'champion.json').read_bytes()
        assert user_before == (user_registry.read_bytes() if user_registry.exists() else None)
        evidence.update(status='passed', live_path=str(clean), live_path_entries=['dirname'], authority_sha256=file_hash(fixtures), training=read(training/'report.json'),
            replay=replay, changed_parameter_tensors=changed, native_parity_max_errors=errors,
            artifact=read(candidate_path), candidate_id=candidate, checkpoint_sha256=file_hash(training/'checkpoint.pt'),
            registry=registry.frozen, seeds=read(seeds), screening=report,
            invocation=read(manifest.parent/'invocation.json'), champion_unchanged=True, user_champion_unchanged=True)
    except BaseException as exc:
        evidence.update(status='failed', error=repr(exc))
        raise
    finally:
        evidence['seconds'] = time.monotonic()-started
        publish(state/'verification.json', evidence)
        print('Verification: '+str(state/'verification.json'), flush=True)


if __name__ == '__main__':
    main()
