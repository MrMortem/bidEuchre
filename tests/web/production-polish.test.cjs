const {test} = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');

function boot(savedMotion) {
  const elements = new Map();
  const make = () => ({disabled:false,textContent:'',value:'',dataset:{},events:{},attributes:{},
    classList:{toggle(){},add(){},remove(){},contains:()=>true},
    setAttribute(key,value){this.attributes[key]=value}, removeAttribute(key){delete this.attributes[key]},
    addEventListener(key,fn){this.events[key]=fn}, querySelectorAll(){return []},
    animate(){},getAnimations(){return []}});
  const get = selector => { if(!elements.has(selector)) elements.set(selector,make()); return elements.get(selector); };
  const legal = make();
  const row = {...make(),cleared:false, querySelector:()=>legal,
    replaceChildren(){this.cleared=true},parentElement:make()};
  get('#action-buttons').querySelectorAll=()=>[legal];
  const visibility={};
  const media={matches:false,addEventListener(key,fn){this.changed=fn}};
  const store=new Map(savedMotion == null ? [] : [['bideuchre.motion',savedMotion]]);
  const document={hidden:false,body:make(),querySelector:get,
    querySelectorAll:selector=> selector.includes('card-row') ? [row] : selector.includes('playing-card') ? [legal] : [],
    addEventListener(key,fn){visibility[key]=fn}};
  const scope=vm.createContext({document,matchMedia:()=>media,
    localStorage:{getItem:key=>store.get(key)??null,setItem:(key,value)=>store.set(key,value)},
    window:{setTimeout,clearTimeout,addEventListener(){}},setTimeout,clearTimeout,AbortController,console});
  const root=path.join(__dirname,'../../src/BidEuchre.Server/wwwroot');
  vm.runInContext(fs.readFileSync(path.join(root,'table-motion.js'),'utf8'),scope);
  vm.runInContext(fs.readFileSync(path.join(root,'app.js'),'utf8'),scope);
  return {scope,get,legal,row,document,media,store,visibility,run:code=>vm.runInContext(code,scope)};
}

test('saved motion preference survives startup and system reduced motion takes priority',()=>{
  const env=boot('off'); env.run('configureMotion()');
  assert.equal(env.get('#motion-toggle').textContent,'Motion: Off');
  env.get('#motion-toggle').events.click();
  assert.equal(env.store.get('bideuchre.motion'),'on');
  env.media.matches=true; env.media.changed({matches:true});
  assert.equal(env.get('#motion-toggle').textContent,'Motion: Off');
  assert.equal(env.get('#motion-toggle').disabled,true);
});

test('motion initialization tolerates unavailable browser storage',()=>{
  const env=boot(); env.run("localStorage.getItem=()=>{throw new Error('storage denied')}; configureMotion()");
  assert.equal(env.get('#motion-toggle').textContent,'Motion: On');
});

test('hidden documents never start an animation',()=>{
  const env=boot(); env.document.hidden=true; let calls=0;
  env.scope.probe={getAnimations:()=>[],animate:()=>calls++};
  env.run('runMotion(probe,[],{})');
  assert.equal(calls,0);
});

test('a failed background poll locks stale controls and reconnect resets motion history',async()=>{
  const env=boot();
  env.run("model.sessionId='a'; model.viewerSeat=0; api=async()=>{throw new Error('offline')}; toast=()=>{}");
  await env.run('refreshGame({notify:false})');
  assert.equal(env.run('model.connectionLost'),true);
  assert.equal(env.legal.disabled,true);
  assert.match(env.get('#connection-message').textContent,/reconnect/i);
  env.run(`const old={handNumber:1,currentSeat:0,scores:[0,0],currentTrick:[],completedTricks:[]};
    tableMotion.update(old,'a:0');
    const recovered={...old,currentTrick:[{seat:0,card:{code:'AH'}}]};
    api=async()=>({id:'a',started:true,seats:[],game:recovered});
    renderGame=()=>{model.motionCue=tableMotion.update(model.session.game,'a:0');syncInputState()};`);
  await env.run('refreshGame({notify:false})');
  assert.equal(env.run('model.connectionLost'),false);
  assert.equal(env.run('model.motionCue.addedPlays.size'),0);
  assert.equal(env.legal.disabled,false);
});

test('switching hand immediately clears private cards and blocks stale moves',async()=>{
  const env=boot(); let posts=0;
  env.scope.wait=new Promise(()=>{});
  env.scope.countPost=()=>posts++;
  env.run("model.sessionId='a';model.viewerSeat=0;api=async(url,options={})=>{if(options.method==='POST')countPost();return wait}");
  env.run('selectViewerSeat(1)');
  assert.equal(env.row.cleared,true);
  assert.equal(env.legal.disabled,true);
  await env.run("sendAction({type:'play',card:'AH'})");
  assert.equal(posts,0);
});

test('late completion of a move from an old view cannot unlock a new move',async()=>{
  const env=boot(); let finish;
  env.scope.pending=new Promise(resolve=>finish=resolve);
  env.run("model.sessionId='a';model.viewerSeat=0;api=async()=>pending;refreshGame=async()=>{};toast=()=>{}");
  const old=env.run("sendAction({type:'play',card:'AH'})");
  // The second view has its own pending action; the first request may still be accepted by the server.
  env.run("model.viewGeneration=(model.viewGeneration??0)+1;model.sessionId='b';model.actionPending=true");
  finish({}); await old;
  assert.equal(env.run('model.actionPending'),true);
});

test('resuming a background tab locks and clears the old hand before refreshing',()=>{
  const env=boot(); env.scope.wait=new Promise(()=>{});
  env.run("model.sessionId='a';model.viewerSeat=0;configurePageLifecycle();refreshGame=async()=>wait");
  env.visibility.visibilitychange();
  assert.equal(env.row.cleared,true);
  assert.equal(env.legal.disabled,true);
});

test('a lost move response is reconciled with a read before input is unlocked',async()=>{
  const env=boot(); let reads=0,posts=0;
  env.scope.request=async(url,options={})=>{
    if(options.method==='POST'){ posts++; throw new Error('response lost'); }
    reads++; return {id:'a',seats:[{seat:0,kind:'Human'}],started:true,game:{phase:'Playing'}};
  };
  env.run("model.sessionId='a';model.viewerSeat=0;api=request;toast=()=>{};renderGame=()=>syncInputState()");
  await env.run("sendAction({type:'play',card:'AH'})");
  assert.equal(posts,1,'Never retry a submitted move');
  assert.equal(reads,1,'Fetch authoritative state after an uncertain move');
  assert.equal(env.run('model.actionPending'),false);
  assert.equal(env.run('model.session.game.phase'),'Playing');
});

test('bid stamps stay on screen when mobile bidding controls scroll the table away',()=>{
  const env=boot();
  const geometry=env.run(`bidFlightGeometry(
    {left:0,top:-900,width:390,height:720},
    {left:12,top:-1100,width:160,height:100},260,120,390,844)`);
  assert.equal(geometry.x,195);
  assert.equal(geometry.y,422);
  assert.ok(geometry.targetX < 0,'An offscreen dock should slide out through the side');
});

test('all 24 card identities use distinct transparent face sprite cells',()=>{
  const env=boot();
  const cells=new Set();
  const suits=['Clubs','Diamonds','Hearts','Spades'];
  const ranks=['9','10','J','Q','K','A'];
  for(const [row,suit] of suits.entries()) for(const [column,rank] of ranks.entries()) {
    const html=env.run(`cardHtml(${JSON.stringify({rank,suit})},true)`);
    assert.match(html,new RegExp(`aria-label="${rank} of ${suit}"`));
    assert.match(html,/class="card-face-sprite"/);
    assert.match(html,new RegExp(`--card-column:${column};--card-row:${row}`));
    assert.match(html,/class="playing-card [^"]*legal/);
    assert.doesNotMatch(html,/disabled/);
    cells.add(`${column}:${row}`);
  }
  assert.equal(cells.size,24);
  assert.match(env.run(`cardHtml({code:'AS',suit:'Spades'},false)`),/disabled/);
});

test('card sprite assets expose a separate white background and complete atlas manifest',()=>{
  const root=path.join(__dirname,'../../assets/pixel');
  const manifest=JSON.parse(fs.readFileSync(path.join(root,'cards.json'),'utf8'));
  assert.deepEqual(manifest.ranks,['9','T','J','Q','K','A']);
  assert.deepEqual(manifest.suits,['C','D','H','S']);
  assert.equal(Object.keys(manifest.cards).length,24);
  assert.equal(manifest.background,'card-background-white.png');
  for(const [code,rect] of Object.entries(manifest.cards)) {
    assert.equal(rect.x,manifest.ranks.indexOf(code[0])*manifest.cardWidth);
    assert.equal(rect.y,manifest.suits.indexOf(code[1])*manifest.cardHeight);
    assert.ok(fs.existsSync(path.join(root,`cards/${code}.png`)));
  }
});

test('deal and table cancellation preserve the ambient hand wobble',()=>{
  const env=boot();
  const canceled=[];
  const ambient={animationName:'hand-float',cancel:()=>canceled.push('ambient')};
  const deal={cancel:()=>canceled.push('deal')};
  env.scope.probe={getAnimations:()=>[ambient,deal],animate:()=>({})};
  env.run('runMotion(probe,[],{})');
  assert.deepEqual(canceled,['deal']);
  canceled.length=0;
  env.get('#game-view').getAnimations=()=>[ambient,deal];
  env.run('cancelTableMotion()');
  assert.deepEqual(canceled,['deal']);
});

test('contract scenery reveals only trump, reveals all for High, and clears the viewport for Low',()=>{
  const env=boot();
  for(const [width,height] of [[1320,860],[390,844],[1920,600]]) {
    env.scope.viewport={width,height};
    for(const suit of ['Hearts','Clubs','Spades','Diamonds']) {
      env.scope.contract={mode:'Trump',trump:suit};
      const layout=env.run('suitBackdropLayout(viewport.width,viewport.height,contract)');
      assert.deepEqual(Array.from(layout,l=>l.active),['Hearts','Clubs','Spades','Diamonds'].map(s=>s===suit));
      const active=layout.find(l=>l.active);
      assert.ok(active.left+active.x >= 0 && active.left+active.x+active.width <= width);
      assert.ok(active.top+active.y >= 0 && active.top+active.y+active.height <= height);
    }
    const high=env.run("suitBackdropLayout(viewport.width,viewport.height,{mode:'High'})");
    assert.ok(high.every(l=>l.active));
    const low=env.run("suitBackdropLayout(viewport.width,viewport.height,{mode:'Low'})");
    assert.ok(low.every(l=>l.left+l.x+l.width < 0 || l.left+l.x > width));
    const idle=env.run('suitBackdropLayout(viewport.width,viewport.height,null)');
    assert.ok(idle.every(l=>l.x===0 && l.y===0 && !l.active));
  }
});

test('dealing sends two three-card packets per seat beginning left of every dealer',()=>{
  const env=boot();
  for(let dealer=0;dealer<4;dealer++) {
    const packets=env.run(`dealPackets(${dealer})`);
    assert.equal(packets.length,8);
    for(let index=0;index<8;index++) {
      assert.equal(packets[index].seat,(dealer+index%4+1)%4);
      assert.equal(packets[index].firstCard,Math.floor(index/4)*3);
      assert.equal(packets[index].count,3);
      if(index) assert.ok(packets[index].delay>packets[index-1].delay);
    }
  }
});

test('bidding stays locked until the server accepts deal completion',async()=>{
  const env=boot(); let finish;
  env.scope.pending=new Promise(resolve=>finish=resolve);
  env.run("model.sessionId='a'; model.session={id:'a',dealing:true,game:{handNumber:1}}; api=async()=>pending; refreshGame=async()=>{}");
  assert.equal(env.run('inputLocked()'),true);
  const completion=env.run("finishServerDeal('a',1)");
  assert.equal(env.run('inputLocked()'),true);
  finish({}); await completion;
  assert.equal(env.run('inputLocked()'),false);
});
