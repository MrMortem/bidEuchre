"""Bounded local generation/training commands. No promotion or live champion swap."""
import argparse
from dataclasses import asdict, replace
import json
from pathlib import Path
import signal
import subprocess
import sys
import time
from .checkpoint import load_checkpoint, save_checkpoint
from .device import choose_device, benchmark_batch
from .replay import ReplayStore
from .trainer import TrainConfig, train_cycle, generate_burst, versions, _capacity, _state


def parser():
    result=argparse.ArgumentParser(description=__doc__)
    commands=result.add_subparsers(dest='command',required=True)
    for name in ('generate','train','cycle','benchmark-device','resume'):
        command=commands.add_parser(name)
        command.add_argument('--output',type=Path,default=Path('hybrid-run'))
        command.add_argument('--replay',type=Path)
        command.add_argument('--worker',type=Path,default=Path(__file__).resolve().parents[2]/'build/bideuchre-hybrid-worker')
        for key,kind,default in [('device',str,'auto'),('seed',int,1),('workers',int,2),
             ('max-memory-bytes',int,2<<30),('max-disk-bytes',int,5<<30),('duration-seconds',float,1800),
             ('checkpoint-seconds',float,300),('learning-rate',float,.0003),('batch-size',int,64),
             ('max-updates',int,1000),('generation-matches',int,1),('execution-mode',str,'seeded_timed')]:
            command.add_argument('--'+key,type=kind,default=default)
    return result


def main(argv=None):
    args=parser().parse_args(argv)
    signal.signal(signal.SIGTERM,lambda *_: (_ for _ in ()).throw(KeyboardInterrupt()))
    fields=TrainConfig.__dataclass_fields__
    config=TrainConfig(**{key:getattr(args,key) for key in fields if hasattr(args,key)})
    output=args.output.resolve(); started=time.monotonic()
    if args.command=='resume':
        state=load_checkpoint(output/'checkpoint.pt')
        # Restore semantic controls from the checkpoint. CLI resume overrides only
        # invocation budgets, never batch partitions, population or device/RNG.
        config=TrainConfig(**dict(state['config'],duration_seconds=args.duration_seconds,max_updates=args.max_updates,
                                  max_memory_bytes=args.max_memory_bytes,max_disk_bytes=args.max_disk_bytes,
                                  checkpoint_seconds=args.checkpoint_seconds))
    replay_path=args.replay or (Path(state['replay_root']) if args.command=='resume' and state.get('replay_root') else output/'replay')
    replay=ReplayStore(replay_path,config.max_disk_bytes)
    try:
        if not _capacity(config,replay,output):
            result={'status':'capacity','export_manifest':None,'rows_seen':0,'updates':0}
        elif args.command=='benchmark-device':
            batch=benchmark_batch(min(config.batch_size,64)); batch['_max_memory_bytes']=config.max_memory_bytes
            result=asdict(choose_device(config.device,batch))
            result['versions']=versions()
        else:
            output.mkdir(parents=True,exist_ok=True)
            lock=subprocess.check_output([sys.executable,'-m','pip','freeze','--all'],text=True,timeout=10)
            (output/'dependencies.lock').write_text(lock)
            (output/'versions.json').write_text(json.dumps(versions(),sort_keys=True,indent=2)+'\n')
            command=args.command
            if command=='resume':
                command='train' if state['stage'] in ('train','complete') else 'cycle'
            checkpoint=output/'checkpoint.pt'
            if command in ('cycle','generate') and checkpoint.exists():
                state=_state(config,output)
                if state['stage']=='complete':
                    state['generation']+=1; state['stage']='generate'; state['replay']=None; state['burst_remaining']=config.generation_matches
                    save_checkpoint(state,checkpoint)
            status='complete'
            if command in ('generate','cycle'):
                status=generate_burst(config,replay,output,args.worker.resolve())
            remaining=config.duration_seconds-(time.monotonic()-started)
            if status=='complete' and command!='generate' and remaining>0:
                result=asdict(train_cycle(replace(config,duration_seconds=remaining),replay,output))
            else:
                result=dict(status='duration' if remaining<=0 and status=='complete' else status,
                            export_manifest=None,rows_seen=0,updates=0,duration_seconds=time.monotonic()-started)
            result['duration_seconds']=time.monotonic()-started
            if result['status']!='capacity':
                (output/'report.json').write_text(json.dumps(result,sort_keys=True,indent=2)+'\n')
        print(json.dumps(result,sort_keys=True))
        return 0 if result.get('status','complete') in ('complete','duration') else 2
    except (ValueError,OSError,MemoryError) as exc:
        print(json.dumps({'status':'failed','reason':str(exc)}),file=sys.stderr)
        return 2


if __name__=='__main__':
    raise SystemExit(main())
