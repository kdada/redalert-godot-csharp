#!/usr/bin/env python3
"""Build and validate the actual project, game inputs and ENet peers without opening windows."""
import argparse
import json
from pathlib import Path
import signal
import subprocess
import threading
import time

ROOT=Path(__file__).resolve().parents[1]


def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--quick',action='store_true')
    parser.add_argument('--skip-import',action='store_true')
    parser.add_argument('--with-assets',action='store_true',help='Also rebuild/retest the independent format CLIs')
    parser.add_argument('--baseline-replay',type=Path,help='Also verify a previously recorded replay with the current build')
    args=parser.parse_args()
    reports=ROOT/'Reports'/f'validation-{time.time_ns()}'
    reports.mkdir(parents=True)
    active=None

    def interrupted(_signal,_frame):
        raise KeyboardInterrupt
    signal.signal(signal.SIGTERM,interrupted)

    def run(label,command,seconds=180,engine=False):
        nonlocal active
        print(f'START {label}',flush=True)
        lines=[]
        errors=[]
        active=subprocess.Popen(command,cwd=ROOT,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True)
        def reader():
            with (reports/f'{label}.log').open('w') as output:
                for line in active.stdout:
                    lines.append(line);output.write(line);output.flush()
                    if engine and ('ERROR:' in line or 'FATAL:' in line or 'ObjectDB instances were leaked' in line):
                        errors.append(line.strip())
                    if line.startswith(('PASS ','FAIL ','SELF-TEST','TEST COMPLETE','Build succeeded','Build FAILED','ERROR:','VoxelBaker:','{')):
                        print(f'[{label}] {line.strip()}',flush=True)
        thread=threading.Thread(target=reader,daemon=True);thread.start()
        try:
            code=active.wait(timeout=seconds)
        finally:
            if active.poll() is None:
                active.terminate()
                try: active.wait(timeout=10)
                except subprocess.TimeoutExpired: active.kill();active.wait(timeout=5)
            thread.join(5)
            active=None
        if code!=0 or errors:
            raise RuntimeError(f'{label} failed (exit {code}, engine errors {errors}); inspect {reports/(label+".log")}')
        print(f'PASS STAGE {label}',flush=True)

    try:
        run('art-integrity',['python3','Tools/validate_art.py',f'--report={reports/"art-integrity.json"}'])
        run('core',['dotnet','run','--project','Tests/RedAlert.Tests.csproj','--',f'--report={reports/"core-tests.json"}',f'--stress={800 if args.quick else 1600}'])
        run('build',['dotnet','build','RedAlert.csproj','--nologo','-v','minimal'])
        if args.with_assets:
            run('shp-selftest',['zsh','Tools/AssetPipeline/build.zsh','--self-test'])
            run('voxel-selftest',['zsh','Tools/VoxelBaker/run.zsh','--self-test'])
        if not args.skip_import:
            run('import',['godot','--headless','--path',str(ROOT),'--editor','--import','--quit'],engine=True)
        run('ui',['godot','--headless','--path',str(ROOT),'--log-file',str(reports/'ui.godot.log'),'--','--ui-test',f'--report={reports/"ui-input.json"}'],engine=True)
        if args.baseline_replay:
            baseline=args.baseline_replay.resolve()
            run('baseline-replay',['godot','--headless','--path',str(ROOT),'--','--verify-replay='+str(baseline),f'--report={reports/"baseline-replay.json"}'],engine=True)
        run('network-lifecycle',['godot','--headless','--path',str(ROOT),'--log-file',str(reports/'network-lifecycle.godot.log'),'--','--network-tests',f'--report={reports/"network-lifecycle.json"}'],engine=True)
        cases=[('net-2',['--players=2','--ticks=600'])]
        if not args.quick:
            cases += [('net-8',['--players=8','--ticks=3600']),('net-8-impaired',['--players=8','--ticks=1200','--loss=0.02','--jitter-ms=40']),('net-8-dedicated',['--players=8','--ticks=800','--dedicated'])]
        else:
            cases += [('net-2-impaired',['--players=2','--ticks=400','--loss=0.02','--jitter-ms=40'])]
        network=[]
        for label,options in cases:
            folder=reports/label
            run(label,['python3','Tools/headless_match.py',*options,f'--report-dir={folder}'],seconds=200)
            network.append(json.loads((folder/'summary.json').read_text()))
        def read(name):
            result=json.loads((reports/name).read_text())
            assert result['passed'],name+' did not pass'
            return result
        ui=read('ui-input.json')
        assert ui['uiInputVerified'] and ui['drawCalls']>0,'input/drawing callbacks were not exercised'
        summary={'passed':True,'reportDirectory':str(reports),'quick':args.quick,'art':read('art-integrity.json'),'baselineReplay':read('baseline-replay.json') if args.baseline_replay else None,'core':read('core-tests.json'),'ui':ui,'networkLifecycle':read('network-lifecycle.json'),'network':network}
        (reports/'summary.json').write_text(json.dumps(summary,indent=2,ensure_ascii=False))
        print('HEADLESS VALIDATION PASSED '+str(reports/'summary.json'),flush=True)
        return 0
    finally:
        if active is not None and active.poll() is None:
            active.terminate()
            try: active.wait(timeout=10)
            except subprocess.TimeoutExpired: active.kill();active.wait(timeout=5)

if __name__=='__main__':
    raise SystemExit(main())
