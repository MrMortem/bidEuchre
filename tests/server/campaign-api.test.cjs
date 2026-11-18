const {test,before,after}=require('node:test');
const assert=require('node:assert/strict');
const {spawn,spawnSync}=require('node:child_process');
const {randomBytes,createHash}=require('node:crypto');
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
 for(const route of ['/new','/continue','/start-match','/forfeit','/next-hand','/commands','/arrange-hand','/shop/arcade/plinko','/shop/arcade/roulette','/shop/arcade/keep','/shop/arcade/use','/shop/arcade/pick','/shop/arcade/tear','/shop/soda','/shop/booster/open','/shop/booster/tear','/shop/booster/pick','/shop/booster/skip','/shop/arcade/dismiss','/shop/begin-sigil-use','/shop/begin-ticket-use','/shop/use-purchased','/shop/finish-use','/shop/buy','/shop/buy-use','/shop/print','/shop/keep-ticket','/shop/use-ticket','/shop/pull','/shop/sell','/shop/reroll','/items/use','/items/discard','/result/continue','/victory/continue','/shop/leave','']){
 const method=route?'POST':'DELETE';assert.equal((await request(a.cookie,route,{expectedVersion:a.state.version},method,{Origin:'https://evil.example'})).status,403);
 assert.equal((await request(a.cookie,route,{expectedVersion:a.state.version},method,{'Content-Type':'text/plain'})).status,415);
 }
});
test('campaign rejects malformed command JSON without changing the live profile version',async()=>{
 const a=await bootstrap();let response=await request(a.cookie,'/new',{expectedVersion:a.state.version});let state=await response.json();response=await request(a.cookie,'/start-match',{expectedVersion:state.version});assert.equal(response.status,200);state=await response.json();
 for(let n=0;n<80&&state.table.currentSeat!==0;n++){await new Promise(r=>setTimeout(r,100));state=await get(a.cookie);}
 assert.equal(state.table.currentSeat,0);
 for(const command of [null,[],{}, {kind:'panic-button'},{kind:'bid',level:'Four',variant:'PanicButton'}, {kind:'pass',seat:0},{kind:'bid',level:4},{kind:'use-tarot',slot:0,targetIds:null},{kind:'play',cardId:24},{kind:'choose-contract',mode:'Trump',trump:99}]){
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

test('Gambler rejects invalid targets atomically and applies Lucky to the two chosen offered cards',async()=>{
 const token=randomBytes(32).toString('base64url'),cookie='bideuchre.campaign='+token;
 const seeded=spawnSync(process.env.BIDEUCHRE_DOTNET||'dotnet',[path.join(root,'tests/BidEuchre.Server.Tests/bin/Debug/net10.0/BidEuchre.Server.Tests.dll'),'--campaign-fixture',directory,token,'gambler'],{cwd:root,env:process.env,encoding:'utf8'});
 assert.equal(seeded.status,0,seeded.stderr+seeded.stdout);
 const state=await get(cookie),offer=state.shop.offers.find(o=>o.tickets.some(t=>t.id==='Gambler'));
 const ids=offer.targetCards.map(c=>c.id),outside=state.deck.find(c=>!ids.includes(c.id)).id,selected=[ids[4],ids[1]];
 assert.equal(ids.length,6);
 assert.equal(offer.tickets.find(t=>t.id==='Gambler').targetCount,2);
 for(const targetIds of [[],[ids[0]],ids.slice(0,3),[ids[0],ids[0]],[ids[0],outside]]){
  const response=await request(cookie,'/shop/use-ticket',{expectedVersion:state.version,offerId:offer.id,ticketId:'Gambler',targetIds});
  assert.equal(response.status,400,JSON.stringify(targetIds));
  assert.deepEqual(await get(cookie),state,'Rejected targets must preserve the paid spread, deck, inventory, wallet and version');
 }
 await stop();await start();
 assert.deepEqual(await get(cookie),state,'Reload must preserve the paid sample');
 const response=await request(cookie,'/shop/use-ticket',{expectedVersion:state.version,offerId:offer.id,ticketId:'Gambler',targetIds:selected});
 assert.equal(response.status,200);const after=await response.json();
 assert.equal(after.run.money,state.run.money);assert.deepEqual(after.inventory,state.inventory);
 assert.deepEqual(after.deck,state.deck.map(c=>selected.includes(c.id)?{...c,enhancement:'Lucky'}:c));
 assert.equal(after.shop.offers.find(o=>o.id===offer.id).sold,true);
});

test('new shop purchases use saved samples and held items cannot be used between matches',async()=>{
 const token=randomBytes(32).toString('base64url'),cookie='bideuchre.campaign='+token;
 const seeded=spawnSync(process.env.BIDEUCHRE_DOTNET||'dotnet',[path.join(root,'tests/BidEuchre.Server.Tests/bin/Debug/net10.0/BidEuchre.Server.Tests.dll'),'--campaign-fixture',directory,token,'shop'],{cwd:root,env:process.env,encoding:'utf8'});
 assert.equal(seeded.status,0,seeded.stderr+seeded.stdout);
 let state=await get(cookie);
 const failWithoutChange=async(route,fields)=>{
  const before=await get(cookie);const response=await request(cookie,route,{expectedVersion:before.version,...fields});
  assert.equal(response.status,400);assert.deepEqual(await get(cookie),before);
 };
 assert.equal(state.capabilities['items-use'].enabled,false);
 await failWithoutChange('/items/use',{category:'Tarot',slot:0,targetIds:[0]});
 await failWithoutChange('/items/use',{category:'Sigil',slot:0,targetIds:[0]});
 assert.equal(state.shop.offers.some(o=>o.category==='Sigil'),false);assert.equal(state.shop.arcade.price,12);
 let response;
 const teller=state.shop.offers.find(o=>o.category==='Tarot');assert.equal(teller.targetCards.length,0);
 response=await request(cookie,'/shop/print',{expectedVersion:state.version,offerId:teller.id,count:3});assert.equal(response.status,200);state=await response.json();
 const printed=state.shop.offers.find(o=>o.id===teller.id);assert.equal(printed.targetCards.length,6);assert.equal(printed.tickets.length,3);
 const ticket=printed.tickets.find(t=>t.targetCount>0),valid=printed.targetCards.slice(0,ticket.targetCount).map(c=>c.id);
 const bad=state.deck.find(c=>!printed.targetCards.some(t=>t.id===c.id));
 await failWithoutChange('/shop/use-ticket',{offerId:teller.id,ticketId:ticket.id,targetIds:[bad.id,...valid.slice(1)]});
 response=await request(cookie,'/shop/use-ticket',{expectedVersion:state.version,offerId:teller.id,ticketId:ticket.id,targetIds:valid});assert.equal(response.status,200);state=await response.json();
 response=await request(cookie,'/shop/leave',{expectedVersion:state.version});assert.equal(response.status,200);state=await response.json();assert.equal(state.phase,'Intro');assert.equal(state.capabilities['items-use'].enabled,false);
 await failWithoutChange('/items/use',{category:'Tarot',slot:0,targetIds:[0]});
});

async function seedShop(kind='shop-use'){
 const token=randomBytes(32).toString('base64url'),cookie='bideuchre.campaign='+token;
 const seeded=spawnSync(process.env.BIDEUCHRE_DOTNET||'dotnet',[path.join(root,'tests/BidEuchre.Server.Tests/bin/Debug/net10.0/BidEuchre.Server.Tests.dll'),'--campaign-fixture',directory,token,kind],{cwd:root,env:process.env,encoding:'utf8'});
 assert.equal(seeded.status,0,seeded.stderr+seeded.stdout);return cookie;
}
test('Arcade Spectral Pack pick allows held Tarots before and after; restart preserves obligation and charge',async()=>{
 const cookie=await seedShop();let state=await get(cookie);
 const before=state;
 const change=async(route,fields={})=>{const response=await request(cookie,route,{expectedVersion:state.version,...fields});assert.equal(response.status,200,await response.clone().text());state=await response.json();return state;};
 const reject=async(route,fields={})=>{const before=await get(cookie);const response=await request(cookie,route,{expectedVersion:state.version,...fields});assert.equal(response.status,400,route);assert.deepEqual(await get(cookie),before);};
 await change('/shop/arcade/plinko',{lane:4});
 const sealed=state.shop.arcade.pending;
 assert.equal(sealed.awaitingOpen,true);assert.equal(sealed.pack,null);
 await reject('/shop/arcade/pick',{playId:sealed.playId,index:0});
 await change('/shop/arcade/tear',{playId:sealed.playId});
 await reject('/shop/arcade/tear',{playId:sealed.playId});
 const offer=state.shop.arcade.pending,ids=offer.targetCards.map(c=>c.id);
 assert.equal(offer.prizeCategory,'Sigil');assert.equal(offer.pack.length,3);assert.equal(offer.keep.enabled,false);assert.equal(offer.use.enabled,false);assert.equal(offer.pick.enabled,true);
 await reject('/shop/arcade/keep',{playId:offer.playId});await reject('/shop/arcade/dismiss',{playId:offer.playId});await reject('/shop/arcade/use',{playId:offer.playId});
 const index=offer.pack.findIndex(c=>!c.legendary);
 await change('/shop/arcade/pick',{playId:offer.playId,index});assert.equal(state.shop.useSession.featureId,offer.pack[index].key);
 assert.equal(state.run.money,before.run.money-12);assert.equal(state.inventory.itemCount,4);
 assert.equal(state.shop.useSession.purchasedItemUsed,false);assert.deepEqual(state.shop.useSession.targetCards,offer.targetCards);
 assert.equal(state.capabilities['items-use'].enabled,true);assert.equal(state.capabilities['shop-leave'].enabled,false);
 const paid=state;
 for(const [route,fields] of [['/shop/finish-use',{}],['/shop/leave',{}],['/shop/reroll',{}],['/shop/arcade/pick',{playId:offer.playId,index}],['/items/discard',{category:'Tarot',slot:0}],['/items/use',{category:'Sigil',slot:0,targetIds:[ids[0]]}],['/shop/buy',{offerId:before.shop.offers.find(o=>o.category==='Joker').id}]])await reject(route,fields);
 const outside=state.deck.find(c=>!ids.includes(c.id)).id;
 await reject('/shop/use-purchased',{targetIds:[outside]});await reject('/items/use',{category:'Tarot',slot:0,targetIds:[ids[0],outside]});
 assert.equal((await request(cookie,'/shop/use-purchased',{expectedVersion:before.version,targetIds:[ids[0]]})).status,409);
 await stop();await start();state=await get(cookie);assert.equal(state.version,paid.version);assert.deepEqual(state.shop.useSession,paid.shop.useSession);assert.equal(state.run.money,paid.run.money);
 await change('/items/use',{category:'Tarot',slot:0,targetIds:[ids[4],ids[1]]});
 assert.equal(state.inventory.tarot.length,2);assert.equal(state.shop.useSession.purchasedItemUsed,false);
 assert.ok(state.shop.useSession.targetCards.filter(c=>[ids[4],ids[1]].includes(c.id)).every(c=>c.enhancement==='Lucky'));
 await reject('/shop/finish-use');
 await change('/shop/use-purchased',{targetIds:[ids[4]]});assert.equal(state.shop.useSession.purchasedItemUsed,true);
 assert.equal(state.deck.find(c=>c.id===ids[4]).sigil,offer.pack[index].key);assert.equal(state.deck.find(c=>c.id===ids[4]).enhancement,'Lucky');
 await reject('/shop/use-purchased',{targetIds:[ids[0]]});await reject('/shop/leave');
 const applied=state;await stop();await start();state=await get(cookie);assert.equal(state.version,applied.version);assert.deepEqual(state.shop.useSession,applied.shop.useSession);
 await change('/items/use',{category:'Tarot',slot:0,targetIds:[]});assert.equal(state.run.money,paid.run.money+20);
 const total=state.inventory.gumballs.reduce((sum,g)=>sum+g.copies,0);
 await change('/items/use',{category:'Tarot',slot:0,targetIds:[]});assert.equal(state.inventory.tarot.length,0);assert.equal(state.inventory.gumballs.reduce((sum,g)=>sum+g.copies,0),total+1);
 await change('/shop/finish-use');assert.equal(state.shop.useSession,null);assert.equal(state.capabilities['items-use'].enabled,false);
 await change('/shop/leave');assert.equal(state.phase,'Intro');
});
test('choosing a printed ticket commits it without a second charge or changing the six cards',async()=>{
 const cookie=await seedShop('gambler');let state=await get(cookie);
 const offer=state.shop.offers.find(o=>o.tickets.some(t=>t.id==='Gambler')),money=state.run.money;
 const response=await request(cookie,'/shop/begin-ticket-use',{expectedVersion:state.version,offerId:offer.id,ticketId:'Gambler'});
 assert.equal(response.status,200);state=await response.json();assert.equal(state.run.money,money);
 assert.equal(state.shop.useSession.category,'Tarot');assert.equal(state.shop.useSession.featureId,'Gambler');assert.equal(state.shop.useSession.targetCount,2);assert.deepEqual(state.shop.useSession.targetCards,offer.targetCards);
 const sold=state.shop.offers.find(o=>o.id===offer.id);assert.equal(sold.sold,true);assert.equal(sold.tickets.length,0);
 for(const route of ['/shop/finish-use','/shop/leave','/shop/keep-ticket']){
  const body={expectedVersion:state.version,...(route.endsWith('keep-ticket')?{offerId:offer.id,ticketId:'Gambler'}:{})};assert.equal((await request(cookie,route,body)).status,400);
 }
 const used=await request(cookie,'/shop/use-purchased',{expectedVersion:state.version,targetIds:offer.targetCards.slice(0,2).map(c=>c.id)});assert.equal(used.status,200);state=await used.json();assert.equal(state.run.money,money);assert.equal(state.shop.useSession.purchasedItemUsed,true);
});

test('Arcade HTTP rejects injected outcomes, commits payment before presentation, and persists limits across restart and reroll',async()=>{
 const cookie=await seedShop('arcade-loss');let state=await get(cookie),before=state;
 for(const [route,fields] of [['plinko',{lane:-1}],['plinko',{lane:8}],['plinko',{lane:4,score:100}],['plinko',{lane:4,path:[8]}],['roulette',{pocket:0}],['roulette',{pocket:13}],['roulette',{pocket:2,landing:2}]]){
  const response=await request(cookie,'/shop/arcade/'+route,{expectedVersion:state.version,...fields});assert.equal(response.status,400);assert.deepEqual(await get(cookie),before);
 }
 const responses=await Promise.all([1,2].map(()=>request(cookie,'/shop/arcade/plinko',{expectedVersion:state.version,lane:4})));
 assert.deepEqual(responses.map(r=>r.status).sort(),[200,409]);state=await get(cookie);
 assert.equal(state.run.money,before.run.money-12);assert.equal(state.shop.arcade.plinkoPlayed,true);
 const result=state.shop.arcade.pending;assert.equal(result.score,1);assert.equal(result.path.length,15);assert.equal(result.path[0],14);assert.equal(Math.max(0,Math.min(10,Math.floor((78+40*result.path.at(-1)-64)/98))),result.landing);
 assert.equal(state.capabilities['shop-leave'].enabled,false);assert.equal(state.shop.reroll.enabled,false);
 for(const [route,fields] of [['/shop/leave',{}],['/shop/reroll',{}],['/shop/arcade/roulette',{pocket:1}],['/shop/arcade/keep',{playId:result.playId}],['/shop/arcade/dismiss',{playId:'wrong'}]])assert.equal((await request(cookie,route,{expectedVersion:state.version,...fields})).status,400);
 await stop();await start();assert.deepEqual(await get(cookie),state);
 let response=await request(cookie,'/shop/arcade/dismiss',{expectedVersion:state.version,playId:result.playId});assert.equal(response.status,200);state=await response.json();
 response=await request(cookie,'/shop/reroll',{expectedVersion:state.version});assert.equal(response.status,200);state=await response.json();
 assert.equal(state.shop.arcade.plinko.enabled,false);assert.equal(state.shop.arcade.roulette.enabled,true);
 assert.equal(state.shop.arcade.price,0);assert.equal(state.shop.arcade.entryPaid,true);
 assert.equal((await request(cookie,'/shop/arcade/plinko',{expectedVersion:state.version,lane:4})).status,400);
 const balance=state.run.money;
 response=await request(cookie,'/shop/arcade/roulette',{expectedVersion:state.version,pocket:2});assert.equal(response.status,200);state=await response.json();
 assert.equal(state.run.money,balance);
 const wheel=state.shop.arcade.pending;assert.equal(wheel.game,'Roulette');assert.ok(wheel.landing>=1&&wheel.landing<=12);assert.equal(wheel.score,wheel.landing===2?100:wheel.landing%2===0?50:0);
});

test('Arcade ticket can be kept once after freeing shared storage without another charge',async()=>{
 const cookie=await seedShop('arcade-ticket');let state=await get(cookie);
 async function change(route,fields={}){const response=await request(cookie,route,{expectedVersion:state.version,...fields});assert.equal(response.status,200,await response.clone().text());state=await response.json();}
 await change('/shop/arcade/plinko',{lane:4});const prize=state.shop.arcade.pending;
 assert.equal(prize.prizeCategory,'Tarot');assert.equal(prize.keep.enabled,false);
 await change('/items/discard',{category:'Sigil',slot:0});assert.equal(state.shop.arcade.pending.keep.enabled,true);
 assert.deepEqual(state.shop.arcade.pending.targetCards,prize.targetCards);
 await change('/shop/arcade/keep',{playId:prize.playId});
 assert.equal(state.run.money,88);assert.equal(state.shop.arcade.pending,null);assert.equal(state.inventory.itemCount,4);
 assert.equal(state.inventory.tarot.at(-1).id,prize.featureId);
 assert.equal((await request(cookie,'/shop/arcade/keep',{expectedVersion:state.version,playId:prize.playId})).status,400);
 await stop();await start();const restored=await get(cookie);assert.equal(restored.inventory.itemCount,4);assert.equal(restored.run.money,88);assert.equal(restored.shop.arcade.plinkoPlayed,true);
});

test('Arcade failed writes return retryable HTTP503 without revealing or charging an outcome',async()=>{
 const cookie=await seedShop('arcade-loss'),before=await get(cookie),token=cookie.slice(cookie.indexOf('=')+1);
 const savePath=path.join(directory,createHash('sha256').update(token).digest('hex'),'campaign.json'),backup=savePath+'.blocked-write-backup';
 fs.renameSync(savePath,backup);fs.mkdirSync(savePath);
 try{
  const response=await request(cookie,'/shop/arcade/plinko',{expectedVersion:before.version,lane:4});
  assert.equal(response.status,503);const error=await response.json();assert.equal(error.code,'save_unavailable');assert.match(error.error,/Nothing was charged or changed/);
  assert.deepEqual(await get(cookie),before);
 }finally{fs.rmdirSync(savePath);fs.renameSync(backup,savePath);}
 await stop();await start();assert.deepEqual(await get(cookie),before);
 const response=await request(cookie,'/shop/arcade/plinko',{expectedVersion:before.version,lane:4});assert.equal(response.status,200);
 const after=await response.json();assert.equal(after.run.money,before.run.money-12);assert.equal(after.shop.arcade.plinkoPlayed,true);assert.equal(after.shop.arcade.pending.score,1);
});
