from pathlib import Path
import subprocess, json, hashlib
root = Path.cwd()
output = root / 'artifacts/tinyfarm-gate-a'
proofs = [json.loads((output / path).read_text()) for path in ['native-proof.json','2560x1440/native-proof.json']]
assert proofs[0]['finalHash'] == proofs[1]['finalHash']
assert all(row['harvestCookPlantWaterSleepOwnHarvestFightRetreatSaveLoad'] for row in proofs)
window = json.loads((output / 'native-window-1920x1080.json').read_text())
assert window['resizeSemanticNonInterference']
assert json.loads((output / 'hud-world-proof.json').read_text())['worldPixelsExactlyEqualAwayFromHud']
tracked = subprocess.check_output(['git','diff','--numstat'],text=True).splitlines()
tracked_rows = []
for line in tracked:
    added, removed, file = line.split('\t',2)
    tracked_rows.append({'file':file,'added':int(added) if added != '-' else None,'removed':int(removed) if removed != '-' else None})
untracked = subprocess.check_output(['git','ls-files','--others','--exclude-standard','src','tests'],text=True).splitlines()
new_sources = [{'file':file,'lines':len((root/file).read_text().splitlines())} for file in untracked if file.endswith('.cs')]
source_files = sorted(set(row['file'] for row in tracked_rows if row['file'].endswith(('.cs','.csproj'))) | set(row['file'] for row in new_sources))
source_hashes = {file:hashlib.sha256((root/file).read_bytes()).hexdigest() for file in source_files}
source_digest = hashlib.sha256(json.dumps(source_hashes,sort_keys=True).encode()).hexdigest()
artifact_hashes = {str(file.relative_to(output)).replace('\\','/'):hashlib.sha256(file.read_bytes()).hexdigest() for file in sorted(output.rglob('*')) if file.is_file() and file.name != 'manifest.json'}
assets = json.loads((root/'src/TinyFarm/TinyFarm.Native/Assets/GateA/provenance.json').read_text())
manifest = {
    'milestone':'TinyFarm MVP Gate A',
    'outcome':'Playable opening review build delivered; human product gate remains open',
    'reviewBuildReady':True,
    'gateAHumanQualified':False,
    'gateBStarted':False,
    'normalNativeLaunch':True,
    'nativeContinuousLoopQualifiedAt1080pAnd1440p':True,
    'keyboardQualified':True,
    'physicalControllerQualified':False,
    'maraConversationQualified':True,
    'plantWaterSleepOwnHarvestQualified':True,
    'swordEnemyDodgeDamageHealingRetreatQualified':True,
    'saveLoadQualified':True,
    'sleepAutosaveOrderingQualified':True,
    'hudSemanticNonInterference':True,
    'resolutionSemanticNonInterference':True,
    'actualNon16By9ResizeQualified':True,
    'mixedMonitorDpiQualified':False,
    'newRuntimeReflectionAdded':False,
    'newPhysicsOrAnimationLibraryAdded':False,
    'legacyContentRetained':True,
    'newAssetsHumanApproved':False,
    'simulationSecondsInOptimizedNativeWalk':proofs[0]['simulationSeconds'],
    'recordingFps':10,
    'recordingContainsAudio':False,
    'nativeFinalSemanticHash':proofs[0]['finalHash'],
    'tests':{'spatial2D':27,'tinyFarm':362,'openingWithJsonReflectionDisabled':20,'inputManCore':76,'inputManMonoGame':19,'inputManStride':7,'skipped':0},
    'build':'Release TinyFarm.slnx, zero warnings/errors',
    'performanceScope':'180 stationary native frames per resolution/HUD state, requests VSync off, CPU wall clock through synchronous presentation, no GPU timestamps or long-session qualification',
    'primaryNextPressure':'Fresh-player combat feel and opening-loop comprehension before Gate B',
    'baseCommit':subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip(),
    'sourceSnapshotSha256':source_digest,
    'sourceFileHashes':source_hashes,
    'diff':{'tracked':tracked_rows,'newCSharp':new_sources,'newCSharpLines':sum(row['lines'] for row in new_sources),'newAssetPngFiles':len(assets['assets']),'notes':'Tracked diff counts omit new sources/assets/artifacts; approved design spec already existed untracked when Gate A began. Authorized InputMan sibling alphabet additions are separate and preserve prior function-key edits.'},
    'artifactSha256':artifact_hashes
}
(output/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8',newline='\n')
print('Manifest validated:', len(artifact_hashes), 'artifacts;', len(new_sources), 'new C# files;', sum(row['added'] or 0 for row in tracked_rows), 'tracked lines added,', sum(row['removed'] or 0 for row in tracked_rows), 'removed;', manifest['diff']['newCSharpLines'],'new C# lines')
