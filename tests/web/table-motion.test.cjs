const {test} = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const scope = vm.createContext({});
const source = path.join(__dirname, '../../src/BidEuchre.Server/wwwroot/table-motion.js');
if (fs.existsSync(source)) vm.runInContext(fs.readFileSync(source, 'utf8'), scope);
const factory = () => { assert.equal(typeof scope.TableMotion, 'function', 'Motion tracker must exist'); return new scope.TableMotion(); };
const game = () => ({handNumber:1,currentSeat:0,scores:[0,0],currentTrick:[],completedTricks:[]});
const play = {seat:0,card:{code:'AH'}};

test('opening and unchanged polling do not replay old events', () => {
  const tracker = factory(); const frame = game();
  for(let i=0;i<3;i++) {
    const cue=tracker.update(frame,'a:0');
    assert.equal(cue.deal,false); assert.equal(cue.addedPlays.size,0); assert.equal(cue.turnSeat,null);
  }
});
test('only a newly observed play arrives, then stays still through the next play', () => {
  const tracker=factory(); const frame=game(); tracker.update(frame,'a:0');
  frame.currentTrick=[play]; frame.currentSeat=1;
  let cue=tracker.update(frame,'a:0');
  assert.deepEqual([...cue.addedPlays],['0:AH']); assert.equal(cue.turnSeat,1);
  frame.currentTrick.push({seat:1,card:{code:'KH'}});
  cue=tracker.update(frame,'a:0'); assert.deepEqual([...cue.addedPlays],['1:KH']);
});
test('hot-seat switches and table changes suppress historical effects', () => {
  const tracker=factory(); const frame=game(); tracker.update(frame,'a:0'); frame.currentTrick=[play];
  assert.equal(tracker.update(frame,'a:1').addedPlays.size,0);
  assert.equal(tracker.update(frame,'b:1').addedPlays.size,0);
});
test('winner fires once and completed cards do not arrive twice', () => {
  const tracker=factory(); const frame=game(); tracker.update(frame,'a:0');
  frame.currentTrick=[play]; tracker.update(frame,'a:0');
  frame.currentTrick=[]; frame.completedTricks=[{number:1,winner:0,plays:[play]}];
  let cue=tracker.update(frame,'a:0'); assert.equal(cue.winnerSeat,0); assert.equal(cue.addedPlays.size,0);
  assert.equal(tracker.update(frame,'a:0').winnerSeat,null);
});
test('start deals once; new hands and real score changes get separate cues', () => {
  const tracker=factory(); tracker.update(null,'a:0'); const frame=game();
  assert.equal(tracker.update(frame,'a:0').deal,true); assert.equal(tracker.update(frame,'a:0').deal,false);
  frame.handNumber=2; frame.scores=[3,0];
  const cue=tracker.update(frame,'a:0'); assert.equal(cue.deal,true); assert.deepEqual([...cue.scoreTeams],[0]);
});

test('reopening the same table establishes a fresh baseline', async () => {
  const local=vm.createContext({document:{addEventListener(){},querySelector(){return {getAnimations(){return []}}}}});
  vm.runInContext(fs.readFileSync(source,'utf8'),local);
  vm.runInContext(fs.readFileSync(path.join(__dirname,'../../src/BidEuchre.Server/wwwroot/app.js'),'utf8'),local);
  await vm.runInContext(`(async () => {
    model.sessions=[{id:'a',seats:[{seat:0,kind:'Human'}]}];
    const first={handNumber:1,currentSeat:0,scores:[0,0],currentTrick:[],completedTricks:[]};
    tableMotion.update(first,'a:0');
    const latest={...first,currentTrick:[{seat:0,card:{code:'AH'}}]};
    stopPolling=()=>{}; startPolling=()=>{}; showView=()=>{};
    refreshGame=async()=>{ model.motionCue=tableMotion.update(latest,'a:0'); };
    await openSession('a');
  })()`,local);
  assert.equal(vm.runInContext('model.motionCue.addedPlays.size',local),0);
});

test('switching viewer cancels running effects before fetching another hand', async () => {
  let cancelled=false;
  const element={getAnimations:()=>[{cancel(){cancelled=true}}]};
  const local=vm.createContext({document:{addEventListener(){},querySelector(){return element}}});
  vm.runInContext(fs.readFileSync(source,'utf8'),local);
  vm.runInContext(fs.readFileSync(path.join(__dirname,'../../src/BidEuchre.Server/wwwroot/app.js'),'utf8'),local);
  local.checkCancelled=()=>assert.equal(cancelled,true,'Effects must stop before the next fetch');
  await vm.runInContext(`refreshGame=async()=>checkCancelled(); selectViewerSeat(1)`,local);
});
