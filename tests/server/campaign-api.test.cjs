const {test,before,after}=require('node:test');
const assert=require('node:assert/strict');
const {spawn}=require('node:child_process');
const fs=require('node:fs'),os=require('node:os'),path=require('node:path');
const root=path.resolve(__dirname,'../..');
let server,base,logs='',directory;
async function start(){
 logs=''; server=spawn(process.env.BIDEUCHRE_DOTNET||'dotnet',[path.join(root,'src/BidEuchre.Server/bin/Debug/net10.0/BidEuchre.Server.dll'),'--urls=http://127.0.0.1:0',`--WebCampaigns:DataDirectory=${directory}`],{cwd:path.join(root,'src/BidEuchre.Server'),env:process.env});
 server.stdout.on('data',d=>logs+=d);server.stderr.on('data',d=>logs+=d);
 for(let n=0;n<400;n++){let m=logs.match(/Now listening on: (http:\/\/127\.0\.0\.1:\d+)/);if(m){base=m[1];return;}if(server.exitCode!==null)break;await new Promise(r=>setTimeout(r,50));}throw Error(logs);
}
async function stop(){if(server&&server.exitCode===null){server.kill('SIGTERM');await new Promise(r=>server.once('exit',r));}}
before(async()=>{directory=fs.mkdtempSync(path.join(os.tmpdir(),'bideuchre-campaign-http-'));await start();});
after(async()=>{await stop();fs.rmSync(directory,{recursive:true,force:true});});
async function bootstrap(){let response=await fetch(`${base}/api/campaign`);assert.match(response.headers.get('content-type'),/application\/json/);assert.equal(response.headers.get('cache-control'),'no-store');return {cookie:response.headers.get('set-cookie').split(';')[0],state:await response.json(),flags:response.headers.get('set-cookie')};}
async function request(cookie,route,body,method='POST',headers={}){return fetch(`${base}/api/campaign${route}`,{method,headers:{'Content-Type':'application/json',Cookie:cookie,...headers},body:JSON.stringify(body)});}
async function get(cookie){return (await fetch(`${base}/api/campaign`,{headers:{Cookie:cookie}})).json();}
test('campaign bootstrap issues private persistent cookie without a save, and profiles stay isolated',async()=>{
 const a=await bootstrap(),b=await bootstrap();assert.notEqual(a.cookie,b.cookie);assert.equal(a.state.hasRun,false);assert.match(a.flags,/httponly/i);assert.match(a.flags,/samesite=strict/i);assert.match(a.flags,/path=\/api\/campaign/i);assert.match(a.flags,/max-age=/i);
 assert.equal(fs.readdirSync(directory).filter(x=>x!=='.owner.lock').length,0);
 let response=await request(a.cookie,'/new',{expectedVersion:a.state.version});assert.equal(response.status,200);let created=await response.json();assert.equal(created.phase,'Intro');assert.equal(created.deck.length,24);assert.equal((await get(b.cookie)).hasRun,false);assert.equal((await get(a.cookie)).version,created.version);
 assert.deepEqual(await (await fetch(`${base}/api/sessions`)).json(),[]);
 assert.doesNotMatch(JSON.stringify(created),/randomState|wallets|liveMatch|profile|campaign\.json/i);
 const stale=await request(a.cookie,'/new',{expectedVersion:a.state.version});assert.equal(stale.status,409);assert.equal((await stale.json()).code,'stale_state');
});
test('campaign rejects override fields, missing versions, malformed cookies, and cross-origin mutations',async()=>{
 const a=await bootstrap();
 for(const extra of [{seat:0},{profile:'../escape'},{sessionId:'other'},{seed:2}])assert.equal((await request(a.cookie,'/new',{expectedVersion:a.state.version,...extra})).status,400);
 assert.equal((await request(a.cookie,'/new',{})).status,400);
 assert.equal((await request('bideuchre.campaign=../escape','/new',{expectedVersion:a.state.version})).status,409);
 for(const route of ['/new','/continue','/start-match','/forfeit','/next-hand','/commands','/arrange-hand','/shop/buy','/shop/buy-use','/shop/pull','/shop/sell','/shop/reroll','/items/use','/items/discard','/result/continue','/victory/continue','/shop/leave','']){
 const method=route?'POST':'DELETE';assert.equal((await request(a.cookie,route,{expectedVersion:a.state.version},method,{Origin:'https://evil.example'})).status,403);
 assert.equal((await request(a.cookie,route,{expectedVersion:a.state.version},method,{'Content-Type':'text/plain'})).status,415);
 }
});
test('campaign rejects malformed command JSON without changing the live profile version',async()=>{
 const a=await bootstrap();let response=await request(a.cookie,'/new',{expectedVersion:a.state.version});let state=await response.json();response=await request(a.cookie,'/start-match',{expectedVersion:state.version});assert.equal(response.status,200);state=await response.json();
 for(let n=0;n<80&&state.table.currentSeat!==0;n++){await new Promise(r=>setTimeout(r,100));state=await get(a.cookie);}
 assert.equal(state.table.currentSeat,0);
 for(const command of [null,[],{}, {kind:'pass',seat:0},{kind:'bid',level:4},{kind:'use-tarot',slot:0,targetIds:null},{kind:'play',cardId:24},{kind:'choose-contract',mode:'Trump',trump:99}]){
  response=await request(a.cookie,'/commands',{expectedVersion:state.version,expectedRevision:state.table.revision,expectedDealSerial:state.table.dealSerial,command});assert.equal(response.status,400);assert.equal((await response.json()).code,'invalid_request');
 }
 assert.equal((await get(a.cookie)).version,state.version);
 response=await fetch(`${base}/api/campaign/new`,{method:'POST',headers:{'Content-Type':'application/json',Cookie:a.cookie},body:`{"expectedVersion":"${state.version}","expectedVersion":"${state.version}"}`});assert.equal(response.status,400);
 response=await request(a.cookie,'/new',{expectedVersion:'x'.repeat(18000)});assert.equal(response.status,400);
 response=await fetch(`${base}/api/campaign?seat=0`,{headers:{Cookie:a.cookie}});assert.equal(response.status,400);
 response=await request(a.cookie,'',{expectedVersion:state.version},'DELETE');assert.equal(response.status,200);assert.equal((await response.json()).hasRun,false);
});
test('saved cookie identity resumes the same live hand across a server restart',async()=>{
 const a=await bootstrap();let state=await (await request(a.cookie,'/new',{expectedVersion:a.state.version})).json();state=await (await request(a.cookie,'/start-match',{expectedVersion:state.version})).json();
 for(let n=0;n<80&&state.table.currentSeat!==0;n++){await new Promise(r=>setTimeout(r,100));state=await get(a.cookie);}assert.equal(state.table.currentSeat,0);
 const hand=state.table.players[0].visibleCards, revision=state.table.revision,serial=state.table.dealSerial;
 await stop();await start();state=await get(a.cookie);assert.equal(state.requiresContinue,true);assert.equal(state.table,null);
 state=await (await request(a.cookie,'/continue',{expectedVersion:state.version})).json();assert.deepEqual(state.table.players[0].visibleCards,hand);assert.equal(state.table.revision,revision);assert.equal(state.table.dealSerial,serial);assert.equal(state.run.lives,3);
 const result=await (await request(a.cookie,'/forfeit',{expectedVersion:state.version})).json();assert.equal(result.phase,'Result');assert.equal(result.run.lives,2);
 await stop();await start();state=await get(a.cookie);assert.deepEqual(state.pendingResult,result.pendingResult);assert.equal(state.run.lives,2);
});
test('a second server process cannot acquire the same campaign data root',async()=>{
 let output='';const second=spawn(process.env.BIDEUCHRE_DOTNET||'dotnet',[path.join(root,'src/BidEuchre.Server/bin/Debug/net10.0/BidEuchre.Server.dll'),'--urls=http://127.0.0.1:0',`--WebCampaigns:DataDirectory=${directory}`],{cwd:path.join(root,'src/BidEuchre.Server'),env:process.env});
 second.stdout.on('data',d=>output+=d);second.stderr.on('data',d=>output+=d);
 try { await Promise.race([new Promise(r=>second.once('exit',r)),new Promise((_,reject)=>setTimeout(()=>reject(Error('second process retained ownership')),5000))]);assert.notEqual(second.exitCode,0);assert.match(output,/already owned by another server/); }
 finally {if(second.exitCode===null)second.kill('SIGTERM');}
});
