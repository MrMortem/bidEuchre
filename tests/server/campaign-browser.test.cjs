const {test,before,after}=require('node:test');
const assert=require('node:assert/strict');
const {spawn}=require('node:child_process');
const fs=require('node:fs'),os=require('node:os'),path=require('node:path');
let chromium;try{({chromium}=require('../browser/node_modules/playwright'));}catch{}
const skip=chromium?false:'Install tests/browser dependencies and Chromium to run browser journeys';
const root=path.resolve(__dirname,'../..');
let browser,server,base,directory,logs='';
before(async()=>{
 if(!chromium)return;
 directory=fs.mkdtempSync(path.join(os.tmpdir(),'bideuchre-campaign-browser-'));
 browser=await chromium.launch({headless:true});await startServer();
});
async function startServer(){
 logs='';
 server=spawn(process.env.BIDEUCHRE_DOTNET||'dotnet',[path.join(root,'src/BidEuchre.Server/bin/Debug/net10.0/BidEuchre.Server.dll'),'--urls=http://127.0.0.1:0','--WebCampaigns:DataDirectory='+directory],{cwd:path.join(root,'src/BidEuchre.Server'),env:process.env});
 server.stdout.on('data',d=>logs+=d);server.stderr.on('data',d=>logs+=d);
 for(let i=0;i<400;i++){const m=logs.match(/Now listening on: (http:\/\/127\.0\.0\.1:\d+)/);if(m){base=m[1];return;}if(server.exitCode!==null)break;await new Promise(r=>setTimeout(r,50));}throw Error(logs);
}
async function stopServer(){if(server?.exitCode===null){server.kill('SIGTERM');await new Promise(r=>server.once('exit',r));}}
after(async()=>{await browser?.close();if(server?.exitCode===null){server.kill('SIGTERM');await new Promise(r=>server.once('exit',r));}if(directory)fs.rmSync(directory,{recursive:true,force:true});});
async function open(page){page.setDefaultTimeout(3500);await page.goto(base);await page.getByRole('button',{name:'Play campaign',exact:true}).click();await page.locator('#campaign-content h1').waitFor();await settled(page);}
test('real API: keyboard new/start, isolated cookies and leave/continue preserve lives',{skip},async()=>{
 const a=await browser.newContext(),b=await browser.newContext(),page=await a.newPage(),other=await b.newPage();
 try{
  await open(page);await open(other);
  await page.getByRole('button',{name:'New run',exact:true}).focus();await page.keyboard.press('Enter');
  await page.getByRole('button',{name:'Start match',exact:true}).click();
  await page.locator('[data-campaign-command]').first().waitFor();
  await page.locator('[data-campaign-command]').first().focus();
  const focusKey=await page.locator(':focus').getAttribute('data-focus-key');
  await page.waitForTimeout(1200);assert.equal(await page.locator(':focus').getAttribute('data-focus-key'),focusKey);
  const live=await(await page.request.get(base+'/api/campaign')).json();
  assert.equal(live.run.lives,3);assert.equal(live.table.players.filter(p=>p.visibleCards.length).length,1);
  assert.equal((await(await other.request.get(base+'/api/campaign')).json()).hasRun,false);
  await other.getByRole('button',{name:'New run',exact:true}).click();await settled(other);
  const otherVersion=(await stateOf(other)).version;assert.notEqual(otherVersion,live.version);
  assert.equal(await page.locator('#campaign-content .viewer-select, #campaign-content #start-game, #campaign-content #close-table').count(),0);
  await page.locator('.nav-item[data-view="lobby"]').click();await page.reload();
  await page.getByRole('button',{name:'Play campaign',exact:true}).click();await page.locator('[data-campaign-command]').first().waitFor();
  assert.equal((await(await page.request.get(base+'/api/campaign')).json()).run.lives,3);
  page.once('dialog',dialog=>dialog.dismiss());await page.getByRole('button',{name:'Forfeit match',exact:true}).click();assert.equal((await stateOf(page)).run.lives,3);
  page.once('dialog',dialog=>dialog.accept());await page.getByRole('button',{name:'Forfeit match',exact:true}).click();await settled(page);assert.equal((await stateOf(page)).run.lives,2);
  assert.equal((await stateOf(other)).run.lives,3);assert.equal((await stateOf(other)).version,otherVersion);
 }finally{await a.close();await b.close();}
});
function card(id,enhancement=null,sigil=null){return{id,face:{code:'AC',suit:'Clubs',rank:'Ace'},enhancement,sigil};}
function fixture(overrides={}){
 const caps=Object.fromEntries(['new','delete','continue','commands','arrange-hand','items-use','items-discard','shop-leave','shop-reroll','shop-sell'].map(k=>[k,{enabled:true,reason:null}]));
 return{hasRun:true,version:'fixture:1',phase:'Shop',requiresContinue:false,saveStatus:'saved',run:{lives:3,ante:1,match:1,matchInAnte:1,boss:false,money:30,threshold:20,negativeThreshold:-20,handLimit:8,opponentBonus:0},
 deck:[card(0,'Wild','Bear'),card(1)],inventory:{jokers:[],tarot:[{slot:0,id:'DevilishDeal',targetCount:1}],sigils:[],gumballs:[],itemCount:1,itemSlots:4,jokerSlots:5},
 shop:{offers:[],rerollPrice:2,rerollCount:0,reroll:{enabled:true}},capabilities:caps,
 catalog:[{id:'tarot:devilish-deal',key:'DevilishDeal',category:'Tarot',name:'Devilish Deal',description:'Enhance a card.',targetCount:1,price:3}],...overrides};
}
test('target picker: duplicate physical IDs, Escape restores focus, stale selection cleared without replay',{skip},async()=>{
 const context=await browser.newContext(),page=await context.newPage();let state=fixture(),submitted=[],releaseRead;
 await page.route('**/api/campaign',async route=>{if(submitted.length)await new Promise(resolve=>releaseRead=resolve);await route.fulfill({json:state});});
 await page.route('**/api/campaign/items/use',async route=>{submitted.push(route.request().postDataJSON());state={...state,version:'fixture:2'};await route.fulfill({status:409,json:{code:'stale_state',error:'The table changed; choose again.',refresh:true}});});
 try{
  await open(page);const use=page.getByRole('button',{name:'Use Devilish Deal',exact:true});await use.click();
  assert.equal(await page.getByRole('dialog').locator('[data-card-id]').count(),2);
  await page.keyboard.press('Escape');assert.equal(await page.getByRole('dialog').count(),0);assert.equal(await use.evaluate(e=>e===document.activeElement),true);
  await use.click();await page.getByRole('dialog').locator('[data-card-id="1"]').click();await page.getByRole('button',{name:'Confirm targets',exact:true}).click();
  await page.waitForFunction(()=>/choose again/.test(document.querySelector('#campaign-status').textContent));
  assert.equal(await page.getByRole('dialog').count(),0);assert.equal(await page.getByRole('button',{name:'Reroll · $2',exact:true}).isDisabled(),true,'Stale controls stay locked until the fresh read completes');releaseRead?.();await settled(page);assert.equal(submitted.length,1);assert.deepEqual(submitted[0].targetIds,[1]);assert.equal(submitted[0].expectedVersion,'fixture:1');
 }finally{releaseRead?.();await context.close();}
});
test('all 12 command kinds preserve server payloads and live inventory works without enumerated item commands',{skip},async()=>{
 const context=await browser.newContext(),page=await context.newPage();
 const commands=[{kind:'bid',level:'Two',variant:'Chaos'},{kind:'pass'},{kind:'panic-button'},{kind:'play',cardId:0,effectiveSuit:'Hearts'},{kind:'exchange',cardId:1},{kind:'choose-contract',mode:'Trump',trump:'Spades'},{kind:'use-tarot',slot:0,targetIds:[1]},{kind:'use-sigil',slot:0,targetId:0},{kind:'swap-partner-card',cardId:0},{kind:'choose-reveal-seat',targetSeat:1},{kind:'reveal-partner-card',cardId:1},{kind:'select-opponent',targetSeat:3},{kind:'bid',level:'Two'},{kind:'bid',level:'PartnersBest'},{kind:'bid',level:'Alone'},{kind:'choose-contract',mode:'High'},{kind:'choose-contract',mode:'Low'},{kind:'choose-contract',mode:'Trump',trump:'Clubs'},{kind:'choose-contract',mode:'Trump',trump:'Diamonds'},{kind:'choose-contract',mode:'Trump',trump:'Hearts'}];
 let state=fixture({phase:'InMatch',shop:null,table:{phase:'Playing',handNumber:1,dealer:3,currentSeat:0,scores:[0,0],revision:5,dealSerial:8,players:[0,1,2,3].map(seat=>({seat,name:'Player '+(seat+1),team:seat%2,cardCount:2,visibleCards:seat===0?[card(0,'Wild'),card(1)]:[],isSittingOut:false})),legalCommands:commands,trick:[],lastTrick:[],handHistory:[],publicEvents:[],events:[],tricksByTeam:[0,0]}});
 const sent=[];
 await page.route('**/api/campaign',route=>route.fulfill({json:state}));
 await page.route('**/api/campaign/commands',async route=>{sent.push(route.request().postDataJSON());state={...state,version:'fixture:'+(sent.length+1)};await route.fulfill({json:state});});
 try{
  await open(page);
  for(let i=0;i<commands.length;i++){if(commands[i].variant)await page.locator('.campaign-moves summary').click();await page.locator('[data-campaign-command="'+i+'"]').click();await page.waitForFunction(()=>document.querySelector('#campaign-view').getAttribute('aria-busy')==='false');assert.deepEqual(sent[i].command,commands[i]);assert.equal(sent[i].expectedRevision,5);assert.equal(sent[i].expectedDealSerial,8);}
  await page.getByRole('button',{name:'Use Devilish Deal',exact:true}).click();await page.getByRole('dialog').locator('[data-card-id="1"]').click();await page.getByRole('button',{name:'Confirm targets',exact:true}).click();await page.waitForFunction(()=>document.querySelector('#campaign-view').getAttribute('aria-busy')==='false');
  assert.deepEqual(sent.at(-1).command,{kind:'use-tarot',slot:0,targetIds:[1]});
 }finally{await context.close();}
});


const {randomBytes}=require('node:crypto');
const {spawnSync}=require('node:child_process');
async function seeded(kind,options={}) {
 const token=randomBytes(32).toString('base64url');
 const result=spawnSync(process.env.BIDEUCHRE_DOTNET||'dotnet',[path.join(root,'tests/BidEuchre.Server.Tests/bin/Debug/net10.0/BidEuchre.Server.Tests.dll'),'--campaign-fixture',directory,token,kind],{cwd:root,env:process.env,encoding:'utf8'});
 assert.equal(result.status,0,result.stderr+result.stdout);
 const context=await browser.newContext(options);
 await context.addCookies([{name:'bideuchre.campaign',value:token,domain:'127.0.0.1',path:'/api/campaign',httpOnly:true,sameSite:'Strict'}]);
 return context;
}
async function stateOf(page){return(await page.request.get(base+'/api/campaign')).json();}
async function settled(page){await page.waitForFunction(()=>document.querySelector('#campaign-view').getAttribute('aria-busy')==='false');}
async function screenshot(page,name){
 if(!process.env.CAMPAIGN_SCREENSHOT_DIR)return;
 fs.mkdirSync(process.env.CAMPAIGN_SCREENSHOT_DIR,{recursive:true});
 await page.screenshot({path:path.join(process.env.CAMPAIGN_SCREENSHOT_DIR,name+'.png'),fullPage:!name.includes('picker')});
}
test('real Core match: Continue -> final card -> earned result/history -> shop -> next match',{skip},async()=>{
 const context=await seeded('match-end'),page=await context.newPage();
 try {
  await open(page);await page.getByRole('button',{name:'Continue run',exact:true}).click();
  await page.locator('[data-campaign-command]').first().click();
  await page.getByRole('heading',{name:'Match won.',exact:true}).waitFor();
  const result=await stateOf(page);
  assert.equal(result.phase,'Result');assert.equal(result.pendingResult.outcome.won,true);
  assert.ok(result.pendingResult.summary.handHistory.length>0);
  assert.ok(result.pendingResult.outcome.payout>0);
  await page.getByText(/Public score history/).click();
  assert.match(await page.locator('.campaign-history').first().innerText(),/contract.*cards.*Jokers/);
  await screenshot(page,'campaign-result-desktop');
  await page.getByRole('button',{name:'Continue',exact:true}).click();
  await page.getByRole('heading',{name:'The shop.',exact:true}).waitFor();
  assert.equal((await stateOf(page)).run.lives,3);
  await page.getByRole('button',{name:'Leave shop',exact:true}).click();
  await page.getByRole('button',{name:'Start match',exact:true}).click();
  await page.locator('.campaign-hand').waitFor();
  assert.equal((await stateOf(page)).phase,'InMatch');
 } finally {await context.close();}
});
test('real shop: server prices, full shared storage Buy & Use, multi/zero targets, pull duplicate, sell and reroll',{skip},async()=>{
 const context=await seeded('shop',{viewport:{width:1280,height:900}}),page=await context.newPage();
 try {
  await open(page);let state=await stateOf(page);
  assert.equal(state.inventory.itemCount,3);
  const itemOffer=state.shop.offers.find(o=>(o.category==='Tarot'||o.category==='Sigil')&&!o.sold);
  assert.ok(itemOffer);
  await page.locator('[data-campaign-action="buy"][data-offer-id="'+itemOffer.id+'"]').click();await settled(page);
  state=await stateOf(page);assert.equal(state.inventory.itemCount,4);
  assert.match(await page.locator('#campaign-content').innerText(),/4 \/ 4 shared Tarot \+ Sigil slots/);
  assert.equal(await page.getByRole('button',{name:'Sell Alpha · $2',exact:true}).count()>0,true);
  const second=state.shop.offers.find(o=>(o.category==='Tarot'||o.category==='Sigil')&&!o.sold);
  if(second){
   assert.equal(await page.locator('[data-campaign-action="buy"][data-offer-id="'+second.id+'"]').isDisabled(),true);
   await page.locator('[data-campaign-action="buy-use"][data-offer-id="'+second.id+'"]').click();
   for(let i=0;i<second.targetCount;i++)await page.getByRole('dialog').locator('button[data-card-id]').nth(i).click();
   await page.getByRole('button',{name:'Confirm targets',exact:true}).click();await settled(page);
   assert.equal((await stateOf(page)).inventory.itemCount,4);
  }
  // Devilish Deal uses three ordered, distinct physical targets from the server count.
  const devilish=state.inventory.tarot.find(x=>x.id==='DevilishDeal');
  assert.equal(devilish.targetCount,3);
  await page.getByRole('button',{name:'Use Devilish Deal',exact:true}).click();
  await page.getByRole('dialog').locator('button[data-card-id]').nth(0).click();
  assert.equal(await page.getByRole('button',{name:'Confirm targets',exact:true}).isDisabled(),true);
  await page.getByRole('dialog').locator('button[data-card-id]').nth(1).click();
  assert.equal(await page.getByRole('button',{name:'Confirm targets',exact:true}).isDisabled(),true);
  await page.getByRole('dialog').locator('button[data-card-id]').nth(2).click();
  await page.getByRole('button',{name:'Confirm targets',exact:true}).click();await settled(page);
  assert.equal((await stateOf(page)).inventory.itemCount,3);
  const pull=(await stateOf(page)).shop.offers.find(o=>o.category==='Gumball');
  await page.locator('[data-campaign-action="pull"][data-offer-id="'+pull.id+'"]').click();await settled(page);
  assert.match(await page.locator('#campaign-status').innerText(),/duplicate.*2 copies/);
  const before=await stateOf(page),price=before.shop.rerollPrice;
  await page.getByRole('button',{name:'Reroll · $'+price,exact:true}).click();await settled(page);
  const after=await stateOf(page);assert.equal(after.run.money,before.run.money-price);assert.equal(after.shop.rerollCount,before.shop.rerollCount+1);
  const joker=after.inventory.jokers[0];
  await page.locator('[data-campaign-action="sell"][data-joker-id="'+joker.id+'"]').click();await settled(page);
  assert.equal((await stateOf(page)).inventory.jokers.length,0);
  await page.locator('#campaign-view img').evaluateAll(images=>images.forEach(image=>image.loading='eager'));
  await page.waitForFunction(()=>[...document.querySelectorAll('#campaign-view img')].every(image=>image.complete));
  await screenshot(page,'campaign-shop-desktop');
  await page.setViewportSize({width:390,height:844});
  const metrics=await page.evaluate(()=>({inner:innerWidth,body:document.body.scrollWidth,html:document.documentElement.scrollWidth,overflow:[...document.querySelectorAll('body *')].filter(e=>e.getBoundingClientRect().right>innerWidth+1).slice(0,10).map(e=>({tag:e.tagName,cls:e.className,right:e.getBoundingClientRect().right,width:getComputedStyle(e).width,font:getComputedStyle(e).font,fontSize:getComputedStyle(e).fontSize}))}));
  assert.ok(Math.max(metrics.body,metrics.html)<=metrics.inner,'Compact shop must fit viewport: '+JSON.stringify(metrics));
  await screenshot(page,'campaign-shop-compact');
  const broken=await page.locator('#campaign-view img').evaluateAll(images=>images.filter(image=>!image.complete||!image.naturalWidth).map(image=>image.src));
  assert.deepEqual(broken,[],'Catalog artwork must load');
  await page.getByRole('button',{name:'View shared deck',exact:true}).click();
  await page.getByRole('dialog').waitFor();await screenshot(page,'campaign-picker-compact');
  assert.equal(await page.getByRole('dialog').locator('[data-card-id]').count(),24);
  await page.keyboard.press('Escape');
 } finally {await context.close();}
});
test('zero-target item confirms without selection, unsaved success is clearly announced',{skip},async()=>{
 const context=await browser.newContext(),page=await context.newPage();let submitted;
 const state=fixture({inventory:{jokers:[],tarot:[{id:'TaxCollector',slot:0,targetCount:0}],sigils:[],gumballs:[],itemCount:1,itemSlots:4,jokerSlots:5},catalog:[{id:'tarot:tax-collector',key:'TaxCollector',category:'Tarot',name:'Tax Collector',description:'Collect money.',targetCount:0}]});
 await page.route('**/api/campaign',route=>route.fulfill({json:state}));
 await page.route('**/api/campaign/items/use',async route=>{submitted=route.request().postDataJSON();await route.fulfill({json:{...state,version:'fixture:2',saveStatus:'failed',notice:'Could not save progress. Changes are retained in this session only.'}});});
 try{
  await open(page);await page.getByRole('button',{name:'Use Tax Collector',exact:true}).click();
  assert.equal(await page.getByRole('button',{name:'Confirm targets',exact:true}).isEnabled(),true);
  await page.getByRole('button',{name:'Confirm targets',exact:true}).click();await settled(page);
  assert.deepEqual(submitted.targetIds,[]);
  assert.match(await page.locator('#campaign-status').innerText(),/not saved/);
  assert.equal(await page.locator('#campaign-save-warning').isVisible(),true);
 }finally{await context.close();}
});
test('a late campaign mutation cannot reopen a departed view, and one purchase is sent',{skip},async()=>{
 const context=await browser.newContext(),page=await context.newPage();let release,requests=0;
 const state=fixture();
 await page.route('**/api/campaign',route=>route.fulfill({json:state}));
 await page.route('**/api/campaign/shop/reroll',async route=>{requests++;await new Promise(resolve=>release=resolve);await route.fulfill({json:{...state,version:'fixture:2'}});});
 try{
  await open(page);await page.getByRole('button',{name:'Reroll · $2',exact:true}).click();
  assert.equal(await page.getByRole('button',{name:'Reroll · $2',exact:true}).isDisabled(),true);
  await page.locator('.nav-item[data-view="lobby"]').click();
  release();await page.waitForTimeout(200);
  assert.equal(await page.locator('#lobby-view').evaluate(e=>e.classList.contains('active')),true);
  assert.equal(requests,1);
 }finally{release?.();await context.close();}
});


test('real auction/contract/play and keyboard hand arrangement survive server restart',{skip},async()=>{
 const context=await seeded('human-turn',{viewport:{width:1280,height:900}}),page=await context.newPage();
 try {
  await open(page);await page.getByRole('button',{name:'Continue run',exact:true}).click();
  await page.locator('.campaign-hand').waitFor();
  const initial=await stateOf(page),original=initial.table.players[0].visibleCards.map(c=>c.id);
  await page.getByRole('button',{name:'Arrange hand',exact:true}).click();
  await page.getByRole('button',{name:'Move right',exact:true}).focus();await page.keyboard.press('Enter');
  await page.getByRole('button',{name:'Save order',exact:true}).click();await settled(page);
  const expected=[original[1],original[0],...original.slice(2)];
  assert.deepEqual((await stateOf(page)).table.players[0].visibleCards.map(c=>c.id),expected);
  const state=await stateOf(page),bid=state.table.legalCommands.findIndex(c=>c.kind==='bid'&&c.level==='Six'&&!c.variant);
  assert.ok(bid>=0);
  await page.locator('[data-campaign-command="'+bid+'"]').click();
  await page.getByRole('button',{name:'Trump · Spades',exact:true}).waitFor({timeout:10000});
  await page.getByRole('button',{name:'Trump · Spades',exact:true}).focus();await page.keyboard.press('Enter');
  await page.locator('.campaign-hand button[data-card-id].legal').first().waitFor({timeout:10000});
  await page.locator('.campaign-hand button[data-card-id].legal').first().click();await settled(page);
  await page.waitForTimeout(1800);
  // Wait for the next human decision so restarting does not change a bot-in-flight state.
  for(let i=0;i<60;i++){const s=await stateOf(page);if(s.table.currentSeat===0)break;await page.waitForTimeout(100);}
  await page.getByRole('button',{name:'Refresh campaign',exact:true}).click();await settled(page);
  const before=await stateOf(page);
  await screenshot(page,'campaign-table-desktop');
  await page.setViewportSize({width:390,height:844});
  assert.ok(await page.evaluate(()=>Math.max(document.body.scrollWidth,document.documentElement.scrollWidth)<=innerWidth),'Compact table must fit viewport');
  await screenshot(page,'campaign-table-compact');
  await stopServer();await startServer();
  await page.goto(base);await page.getByRole('button',{name:'Play campaign',exact:true}).click();
  await page.getByRole('button',{name:'Continue run',exact:true}).click();await page.locator('.campaign-hand').waitFor();
  const resumed=await stateOf(page);
  assert.deepEqual(resumed.table.players[0].visibleCards,before.table.players[0].visibleCards);
  assert.deepEqual(resumed.table.trick,before.table.trick);
  assert.deepEqual(resumed.table.scores,before.table.scores);
  assert.equal(resumed.table.dealSerial,before.table.dealSerial);
  assert.equal(resumed.run.lives,3);
 }finally{await context.close();}
});


test('Wild choice is keyboard selectable; revealed and historical cards use only server snapshots',{skip},async()=>{
 const context=await browser.newContext(),page=await context.newPage();let command;
 const state=fixture({phase:'InMatch',shop:null,table:{phase:'Playing',handNumber:1,dealer:3,currentSeat:0,scores:[0,0],revision:1,dealSerial:1,
 players:[0,1,2,3].map(seat=>({seat,name:'Player '+(seat+1),cardCount:6,isSittingOut:false,visibleCards:seat===0?[card(0,'Wild'),card(1)]:seat===2?[{...card(2),face:{code:'KD',suit:'Diamonds',rank:'King'}}]:[]})),
 legalCommands:[{kind:'play',cardId:0,effectiveSuit:'Hearts'},{kind:'play',cardId:0,effectiveSuit:'Clubs'}],trick:[],
 lastTrick:[{seat:0,card:{...card(0),face:{code:'QH',suit:'Hearts',rank:'Queen'}},effectiveSuit:'Hearts'}],tricksByTeam:[0,0],publicEvents:[],handHistory:[]}});
 await page.route('**/api/campaign',route=>route.fulfill({json:state}));
 await page.route('**/api/campaign/commands',async route=>{command=route.request().postDataJSON().command;await route.fulfill({json:{...state,version:'fixture:2'}});});
 try {
  await open(page);
  assert.equal(await page.locator('.campaign-players [data-card-id]').count(),1,'Only the explicitly revealed partner card appears');
  const history=page.locator('section').filter({has:page.getByRole('heading',{name:'Last trick',exact:true})}).last();
  assert.match(await history.locator('[role="img"]').getAttribute('aria-label'),/Queen of Hearts/);
  const cardButton=page.locator('.campaign-hand button[data-card-id="0"]');
  await cardButton.focus();await page.keyboard.press('Enter');
  await page.getByRole('dialog').getByRole('button',{name:/as Hearts/}).focus();await page.keyboard.press('Enter');await settled(page);
  assert.deepEqual(command,{kind:'play',cardId:0,effectiveSuit:'Hearts'});
 } finally {await context.close();}
});
test('real accepted mutation with a lost response refreshes once without spending twice',{skip},async()=>{
 const context=await seeded('shop'),page=await context.newPage();let calls=0;
 await page.route('**/api/campaign/shop/reroll',async route=>{calls++;const accepted=await route.fetch();assert.equal(accepted.status(),200);await route.abort('failed');});
 try {
  await open(page);const before=await stateOf(page);
  await page.getByRole('button',{name:'Reroll · $'+before.shop.rerollPrice,exact:true}).click();await settled(page);
  await page.waitForFunction(()=>/accepted|check progress/.test(document.querySelector('#campaign-status').textContent));
  const after=await stateOf(page);
  assert.equal(calls,1);assert.equal(after.run.money,before.run.money-before.shop.rerollPrice);
  assert.equal(after.shop.rerollCount,before.shop.rerollCount+1);
  assert.equal(await page.getByRole('button',{name:'Reroll · $'+after.shop.rerollPrice,exact:true}).isEnabled(),true);
 } finally {await context.close();}
});
test('Recovery, Victory and RunOver remain actionable; replacement/deletion require explicit confirmation',{skip},async()=>{
 const context=await browser.newContext(),page=await context.newPage();let state=fixture({phase:'Recovery',hasRun:false,run:null,inventory:null}),mutations=0;
 await page.route('**/api/campaign',async route=>{
  if(route.request().method()==='DELETE'){mutations++;state=fixture({phase:'Empty',hasRun:false,run:null,inventory:null});}
  await route.fulfill({json:state});
 });
 await page.route('**/api/campaign/new',async route=>{mutations++;state=fixture({phase:'Intro',shop:null});await route.fulfill({json:state});});
 try{
  await open(page);assert.match(await page.locator('#campaign-content').innerText(),/preserved/);
  page.once('dialog',dialog=>dialog.dismiss());
  await page.getByRole('button',{name:'New run',exact:true}).click();
  assert.equal(mutations,0);
  page.once('dialog',dialog=>dialog.accept());
  await page.getByRole('button',{name:'New run',exact:true}).click();await settled(page);
  assert.equal(mutations,1);
  state={...state,version:'fixture:3',phase:'Victory',capabilities:{...state.capabilities,'victory-continue':{enabled:true}}};
  await page.getByRole('button',{name:'Refresh campaign',exact:true}).click();await settled(page);
  await page.getByRole('button',{name:'Keep playing',exact:true}).waitFor();
  state={...state,version:'fixture:4',phase:'RunOver'};
  await page.getByRole('button',{name:'Refresh campaign',exact:true}).click();await settled(page);
  await page.getByRole('heading',{name:'Run over.',exact:true}).waitFor();
  page.once('dialog',dialog=>dialog.dismiss());
  await page.getByRole('button',{name:'Delete saved run',exact:true}).click();assert.equal(mutations,1);
  page.once('dialog',dialog=>dialog.accept());
  await page.getByRole('button',{name:'Delete saved run',exact:true}).click();await settled(page);
  assert.equal(mutations,2);await page.getByRole('button',{name:'New run',exact:true}).waitFor();
 }finally{await context.close();}
});
