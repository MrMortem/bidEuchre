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
 const a=await browser.newContext({reducedMotion:'reduce'}),b=await browser.newContext({reducedMotion:'reduce'}),page=await a.newPage(),other=await b.newPage();
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
function ticketOffer(id='DevilishDeal',targetCount=1){return{id:'teller',category:'Tarot',featureId:'',price:3,sold:false,targetCards:[0,1,2,3,4,5].map(n=>card(n)),tickets:[{id,targetCount,keep:{enabled:true},use:{enabled:true}}],printOptions:[]};}
function fixture(overrides={}){
 const caps=Object.fromEntries(['new','delete','continue','commands','arrange-hand','items-use','items-discard','shop-leave','shop-reroll','shop-sell'].map(k=>[k,{enabled:true,reason:null}]));
 return{hasRun:true,version:'fixture:1',phase:'Shop',requiresContinue:false,saveStatus:'saved',run:{lives:3,ante:1,match:1,matchInAnte:1,boss:false,money:30,threshold:20,negativeThreshold:-20,handLimit:8,opponentBonus:0},
 deck:[card(0,'Wild','Bear'),card(1),card(23)],inventory:{jokers:[],tarot:[{slot:0,id:'DevilishDeal',targetCount:1}],sigils:[],gumballs:[],itemCount:1,itemSlots:4,jokerSlots:5},
 shop:{offers:[],rerollPrice:2,rerollCount:0,reroll:{enabled:true}},capabilities:caps,
 catalog:[{id:'tarot:devilish-deal',key:'DevilishDeal',category:'Tarot',name:'Devilish Deal',description:'Enhance a card.',targetCount:1,price:3}],...overrides};
}
function committed(state,ticketId){
 const offer=state.shop.offers.find(o=>o.tickets?.some(t=>t.id===ticketId)),ticket=offer.tickets.find(t=>t.id===ticketId);
 return {...state,version:state.version+':committed',capabilities:{...state.capabilities,'shop-finish-use':{enabled:false,reason:'Use your purchased item first.'}},shop:{...state.shop,
  offers:state.shop.offers.map(o=>o.id===offer.id?{...o,sold:true,tickets:[]}:o),useSession:{offerId:offer.id,category:'Tarot',featureId:ticket.id,targetCount:ticket.targetCount,targetCards:offer.targetCards,purchasedItemUsed:false}}};
}
test('Poker showdown waits through polls, cancels on navigation and advances a resumed reveal once',{skip},async()=>{
 const context=await browser.newContext({reducedMotion:'reduce'}),page=await context.newPage();
 const ranks={'9':'Nine',T:'Ten',J:'Jack',Q:'Queen',K:'King',A:'Ace'},suits={C:'Clubs',D:'Diamonds',H:'Hearts',S:'Spades'};
 const hands=[['AS','AC','9D','QC','TS','JD'],['9H','TH','KC','JH','QH','KH'],['AD','JS','TC','QD','9C','KS'],['AH','TD','JC','KD','QS','9S']];
 const players=['You','Opponent 1','Partner','Opponent 3'].map((name,seat)=>({seat,name,team:seat%2,cardCount:6,
  visibleCards:hands[seat].map((code,n)=>({...card(seat*6+n),face:{code,rank:ranks[code[0]],suit:suits[code[1]]}})),isSittingOut:false}));
 const pokerResult={winners:[{seat:1,category:'Straight Flush',cardIds:[6,7,9,10,11]}],teamPoints:[0,4]};
 const handHistory=[{handNumber:1,bidder:0,contract:null,pokerResult,reason:'Poker showdown',teamZeroDelta:0,teamOneDelta:4,
  scoreBreakdowns:[{team:0,pokerPoints:0,finalDelta:0},{team:1,pokerPoints:4,finalDelta:4}]}];
 let state=fixture({phase:'InMatch',shop:null,table:{phase:'PokerReveal',handNumber:1,dealer:3,currentSeat:0,scores:[0,4],revision:5,dealSerial:8,
  players,legalCommands:[{kind:'complete-poker'}],pokerResult,trick:[],lastTrick:[],handHistory,publicEvents:[],events:[],tricksByTeam:[0,0]}});
 const sent=[];
 await page.route('**/api/campaign',route=>route.fulfill({json:state}));
 await page.route('**/api/campaign/commands',async route=>{
  sent.push(route.request().postDataJSON());
  state={...state,version:'fixture:3',table:{...state.table,phase:'Bidding',handNumber:2,revision:6,dealSerial:9,
   players:players.map(p=>({...p,visibleCards:p.seat===0?p.visibleCards:[]})),legalCommands:[{kind:'pass'}],pokerResult:null}};
  await route.fulfill({json:state});
 });
 try{
  await page.clock.install({time:new Date('2026-11-17T12:00:00Z')});
  await page.clock.pauseAt(new Date('2026-11-17T12:00:01Z'));
  await open(page);
  await page.getByRole('heading',{name:'Poker showdown',exact:true}).waitFor();
  assert.equal(await page.locator('#campaign-content [data-card-id]').count(),24);
  assert.deepEqual(await page.locator('.campaign-poker-winning-card').evaluateAll(cards=>cards.map(c=>Number(c.dataset.cardId))),[6,7,9,10,11]);
  await screenshot(page,'poker-showdown');
  if(process.env.CAMPAIGN_SCREENSHOT_DIR){
   const desktop=page.viewportSize();
   await page.setViewportSize({width:390,height:844});
   await screenshot(page,'poker-showdown-mobile');
   await page.setViewportSize(desktop);
  }
  await page.clock.runFor(1200);
  await page.locator('.nav-item[data-view="lobby"]').click();
  await page.clock.runFor(3600);
  assert.equal(sent.length,0);
  await page.getByRole('button',{name:'Play campaign',exact:true}).click();
  await page.getByRole('heading',{name:'Poker showdown',exact:true}).waitFor();
  await page.clock.runFor(1000);
  state={...state,version:'fixture:2'};
  await page.getByRole('button',{name:'Refresh campaign',exact:true}).click();
  await settled(page);
  await page.clock.runFor(1600);
  assert.equal(sent.length,0);
  await page.clock.runFor(850);
  await page.getByRole('button',{name:'Pass',exact:true}).waitFor();
  assert.deepEqual(sent,[{expectedVersion:'fixture:2',expectedRevision:5,expectedDealSerial:8,command:{kind:'complete-poker'}}]);
  await page.clock.runFor(3600);
  assert.equal(sent.length,1);
 }finally{await context.close();}
});
test('committed picker ignores Escape and clears stale selection without replay',{skip},async()=>{
 const context=await browser.newContext({reducedMotion:'reduce'}),page=await context.newPage();let state=fixture({shop:{offers:[ticketOffer()],rerollPrice:2,rerollCount:0,reroll:{enabled:true}}}),submitted=[],releaseRead;
 await page.route('**/api/campaign',async route=>{if(submitted.length)await new Promise(resolve=>releaseRead=resolve);await route.fulfill({json:state});});
 await page.route('**/api/campaign/shop/begin-ticket-use',async route=>{state=committed(state,'DevilishDeal');await route.fulfill({json:state});});
 await page.route('**/api/campaign/shop/use-purchased',async route=>{submitted.push(route.request().postDataJSON());state={...state,version:'fixture:2'};await route.fulfill({status:409,json:{code:'stale_state',error:'The table changed; choose again.',refresh:true}});});
 try{
  await open(page);await page.getByRole('button',{name:'Use Devilish Deal now',exact:true}).click();await settled(page);
  const panel=page.locator('[data-shop-use-session]');assert.equal(await panel.locator('[data-card-id]').count(),6);
  await page.keyboard.press('Escape');assert.equal(await panel.count(),1);assert.equal(await page.getByRole('button',{name:'Done',exact:true}).isDisabled(),true);
  await panel.locator('[data-card-id="1"]').click();await panel.locator('[data-campaign-action="apply-session-item"]').click();
  await page.waitForFunction(()=>/choose again/.test(document.querySelector('#campaign-status').textContent));
  assert.equal(await panel.locator('[data-campaign-action="apply-session-item"]').isDisabled(),true,'Stale controls stay locked until the fresh read completes');releaseRead?.();await settled(page);assert.equal(submitted.length,1);assert.deepEqual(submitted[0].targetIds,[1]);assert.equal(submitted[0].expectedVersion,'fixture:1:committed');
  assert.equal(await panel.locator('[data-card-id][aria-pressed="true"]').count(),0);
 }finally{releaseRead?.();await context.close();}
});
test('all active command kinds preserve server payloads and live inventory works without enumerated item commands',{skip},async()=>{
 const context=await browser.newContext({reducedMotion:'reduce'}),page=await context.newPage();
 const commands=[{kind:'bid',level:'Two',variant:'Chaos'},{kind:'pass'},{kind:'play',cardId:0,effectiveSuit:'Hearts'},{kind:'exchange',cardId:1},{kind:'choose-contract',mode:'Trump',trump:'Spades'},{kind:'use-tarot',slot:0,targetIds:[1]},{kind:'use-sigil',slot:0,targetId:0},{kind:'swap-partner-card',cardId:0},{kind:'choose-reveal-seat',targetSeat:1},{kind:'reveal-partner-card',cardId:1},{kind:'select-opponent',targetSeat:3},{kind:'bid',level:'Two'},{kind:'bid',level:'PartnersBest'},{kind:'bid',level:'Alone'},{kind:'choose-contract',mode:'High'},{kind:'choose-contract',mode:'Low'},{kind:'choose-contract',mode:'Trump',trump:'Clubs'},{kind:'choose-contract',mode:'Trump',trump:'Diamonds'},{kind:'choose-contract',mode:'Trump',trump:'Hearts'}];
 let state=fixture({phase:'InMatch',shop:null,table:{phase:'Playing',handNumber:1,dealer:3,currentSeat:0,scores:[0,0],revision:5,dealSerial:8,players:[0,1,2,3].map(seat=>({seat,name:'Player '+(seat+1),team:seat%2,cardCount:2,visibleCards:seat===0?[card(0,'Wild'),card(1)]:[],isSittingOut:false})),legalCommands:commands,trick:[],lastTrick:[],handHistory:[],publicEvents:[],events:[],tricksByTeam:[0,0]}});
 const sent=[];
 await page.route('**/api/campaign',route=>route.fulfill({json:state}));
 await page.route('**/api/campaign/commands',async route=>{sent.push(route.request().postDataJSON());state={...state,version:'fixture:'+(sent.length+1)};await route.fulfill({json:state});});
 try{
  await open(page);
  for(let i=0;i<commands.length;i++){if(commands[i].variant)await page.locator('.campaign-moves summary').click();await page.locator('[data-campaign-command="'+i+'"]').click();await page.waitForFunction(()=>document.querySelector('#campaign-view').getAttribute('aria-busy')==='false');assert.deepEqual(sent[i].command,commands[i]);assert.equal(sent[i].expectedRevision,5);assert.equal(sent[i].expectedDealSerial,8);}
  await page.getByRole('button',{name:'Use Devilish Deal',exact:true}).click();assert.equal(await page.getByRole('dialog').locator('[data-card-id="23"]').count(),0);assert.equal(await page.getByRole('dialog').locator('[data-card-id]').count(),2);await page.getByRole('dialog').locator('[data-card-id="1"]').click();await page.getByRole('button',{name:'Confirm targets',exact:true}).click();await page.waitForFunction(()=>document.querySelector('#campaign-view').getAttribute('aria-busy')==='false');
  assert.deepEqual(sent.at(-1).command,{kind:'use-tarot',slot:0,targetIds:[1]});
 }finally{await context.close();}
});


const {randomBytes}=require('node:crypto');
const {spawnSync}=require('node:child_process');
async function seeded(kind,options={}) {
 const token=randomBytes(32).toString('base64url');
 const result=spawnSync(process.env.BIDEUCHRE_DOTNET||'dotnet',[path.join(root,'tests/BidEuchre.Server.Tests/bin/Debug/net10.0/BidEuchre.Server.Tests.dll'),'--campaign-fixture',directory,token,kind],{cwd:root,env:process.env,encoding:'utf8'});
 assert.equal(result.status,0,result.stderr+result.stdout);
 const context=await browser.newContext({reducedMotion:'reduce',...options});
 await context.addCookies([{name:'bideuchre.campaign',value:token,domain:'127.0.0.1',path:'/api/campaign',httpOnly:true,sameSite:'Strict'}]);
 return context;
}
async function stateOf(page){return(await page.request.get(base+'/api/campaign')).json();}
async function settled(page){await page.waitForFunction(()=>document.querySelector('#campaign-view').getAttribute('aria-busy')==='false');}
async function screenshot(page,name){
 if(!process.env.CAMPAIGN_SCREENSHOT_DIR)return;
 fs.mkdirSync(process.env.CAMPAIGN_SCREENSHOT_DIR,{recursive:true});
 // CSS background images do not participate in <img>.complete or screenshot readiness.
 await page.evaluate(async()=>{const atlas=new Image();atlas.src='/pixel/card-faces.png';await atlas.decode();await document.fonts.ready;});
 await page.screenshot({path:path.join(process.env.CAMPAIGN_SCREENSHOT_DIR,name+'.png'),fullPage:!name.includes('picker')});
}

test('enhancement artwork preserves transparent silhouettes, card faces and focus on light and dark surfaces',{skip},async()=>{
 const names=['Lucky','Golden','Wild','Cursed','Stone','Silver','Glass','Bouncy'];
 const context=await browser.newContext({reducedMotion:'reduce',viewport:{width:1280,height:1000}}),page=await context.newPage();
 const state=fixture({deck:[...names.map((name,index)=>({...card(index,name,index%2?'Duck':'Bear'),face:{code:index%2?'AH':'KS',rank:index%2?'Ace':'King',suit:index%2?'Hearts':'Spades'}})),card(8)]});
 await page.route('**/api/campaign',route=>route.fulfill({json:state}));
 try{
  await open(page);
  await page.getByRole('button',{name:'View shared deck',exact:true}).click();
  const dialog=page.getByRole('dialog');await dialog.waitFor();
  assert.equal(await dialog.locator('.campaign-card-enhanced').count(),8);
  await page.evaluate(async names=>{
   await Promise.all(names.map(async name=>{const image=new Image();image.src='/pixel/curated-enhancement-'+name.toLowerCase()+'.png';await image.decode();}));
   await Promise.all([...document.querySelectorAll('.campaign-dialog img')].map(image=>image.decode()));
  },names);
  const styles=await dialog.locator('.campaign-card').evaluateAll(cards=>cards.map(card=>{
   const style=getComputedStyle(card),face=card.querySelector('.card-face-sprite');
   return{enhanced:card.classList.contains('campaign-card-enhanced'),background:style.backgroundColor,repeat:style.backgroundRepeat,clip:style.backgroundClip,border:style.borderTopColor,radius:style.borderTopLeftRadius,face:!!face,halo:face?getComputedStyle(face).filter:''};
  }));
  for(const style of styles.slice(0,8)){
   assert.equal(style.background,'rgba(0, 0, 0, 0)');assert.equal(style.border,'rgba(0, 0, 0, 0)');assert.equal(style.radius,'0px');
   assert.equal(style.repeat,'no-repeat');assert.equal(style.clip,'padding-box');
   assert.ok(style.face);assert.match(style.halo,/drop-shadow/);
  }
  assert.equal(styles[8].background,'rgb(255, 255, 255)');assert.notEqual(styles[8].border,'rgba(0, 0, 0, 0)');
  assert.equal(await dialog.locator('.campaign-sigil').count(),8);
  await screenshot(page,'enhancement-cards-dark');
  await dialog.evaluate(element=>{element.style.background='#e7e1d4';element.style.color='#231d34';});
  await screenshot(page,'enhancement-cards-light');
  await dialog.locator('[data-modal-close]').click();
  await page.evaluate(cards=>CampaignCards.targets({title:'Enhancement target check',cards,count:1,onConfirm:()=>{}}),state.deck);
  const selectable=page.getByRole('dialog').locator('[data-card-id="0"]');await selectable.focus();
  assert.ok(await selectable.evaluate(card=>parseFloat(getComputedStyle(card).outlineWidth)>=3),'Legal/focus outline must remain visible');
  await selectable.click();assert.equal(await selectable.getAttribute('aria-pressed'),'true');
  assert.equal(await selectable.evaluate(card=>getComputedStyle(card).outlineColor),'rgb(101, 220, 253)');
  await page.setViewportSize({width:390,height:844});
  assert.ok(await page.getByRole('dialog').evaluate(element=>element.scrollWidth<=element.clientWidth+1),'Compact enhancement grid must fit its dialog');
  await screenshot(page,'enhancement-cards-compact-picker');
 }finally{await context.close();}
});

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
test('real shop: server prices, ticket targets, pull duplicate, sell and reroll',{skip},async()=>{
 const context=await seeded('shop',{viewport:{width:1280,height:900}}),page=await context.newPage();
 try {
  await open(page);let state=await stateOf(page);
  assert.equal(state.inventory.itemCount,3);
  for(const control of await page.locator('[data-campaign-action="use-item"]').all())assert.equal(await control.isDisabled(),true);
  assert.equal(state.shop.offers.some(o=>o.category==='Sigil'),false);
  assert.equal(await page.getByRole('button',{name:'Enter Arcade',exact:true}).count(),1);
  let teller=(await stateOf(page)).shop.offers.find(o=>o.category==='Tarot');
  await page.locator('[data-campaign-action="print-tickets"][data-count="3"]').click();await settled(page);
  state=await stateOf(page);teller=state.shop.offers.find(o=>o.id===teller.id);
  assert.equal(teller.tickets.length,3);assert.equal(teller.targetCards.length,6);
  await screenshot(page,'campaign-printed-tickets-desktop');
  assert.equal(await page.getByRole('button',{name:'Leave shop',exact:true}).isDisabled(),true);
  const ticket=teller.tickets.find(t=>t.targetCount>0);
  assert.ok(ticket);
  await page.locator('[data-campaign-action="use-ticket"][data-ticket-id="'+ticket.id+'"]').click();await settled(page);
  assert.deepEqual(await page.locator('[data-shop-use-session]').locator('[data-card-id]').evaluateAll(cards=>cards.map(c=>Number(c.dataset.cardId))),teller.targetCards.map(c=>c.id));
  await page.keyboard.press('Escape');
  assert.equal(await page.getByRole('button',{name:'Done',exact:true}).isDisabled(),true);
  assert.deepEqual(await page.locator('[data-shop-use-session]').locator('[data-card-id]').evaluateAll(cards=>cards.map(c=>Number(c.dataset.cardId))),teller.targetCards.map(c=>c.id),'Escape must preserve the committed opportunity');
  for(let i=0;i<ticket.targetCount;i++)await page.locator('[data-shop-use-session]').locator('button[data-card-id]').nth(i).click();
  await page.locator('[data-campaign-action="apply-session-item"]').click();await settled(page);await page.getByRole('button',{name:'Done',exact:true}).click();await settled(page);
  assert.equal((await stateOf(page)).inventory.itemCount,3);
  const pull=(await stateOf(page)).shop.offers.find(o=>o.category==='Gumball');
  await page.locator('[data-campaign-action="pull"][data-offer-id="'+pull.id+'"]').click();await settled(page);
  assert.match(await page.locator('#campaign-status').innerText(),/duplicate.*2 copies/);
  const before=await stateOf(page),price=before.shop.rerollPrice;
  await page.getByRole('button',{name:'Reroll · $'+price,exact:true}).click();await settled(page);
  const after=await stateOf(page);assert.equal(after.run.money,before.run.money-price);assert.equal(after.shop.rerollCount,before.shop.rerollCount+1);
  await page.locator('[data-campaign-action="print-tickets"][data-count="1"]').click();await settled(page);
  const freshTeller=(await stateOf(page)).shop.offers.find(o=>o.category==='Tarot');
  await page.locator('[data-campaign-action="keep-ticket"][data-ticket-id="'+freshTeller.tickets[0].id+'"]').click();await settled(page);
  assert.equal((await stateOf(page)).inventory.itemCount,4);
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
test('zero-target purchase explicitly confirms in its six-card session and unsaved success is announced',{skip},async()=>{
 const context=await browser.newContext({reducedMotion:'reduce'}),page=await context.newPage();let submitted;
 let state=fixture({shop:{offers:[ticketOffer('TaxCollector',0)],rerollPrice:2,rerollCount:0,reroll:{enabled:true}},catalog:[{id:'tarot:tax-collector',key:'TaxCollector',category:'Tarot',name:'Tax Collector',description:'Collect money.',targetCount:0}]});
 await page.route('**/api/campaign',route=>route.fulfill({json:state}));
 await page.route('**/api/campaign/shop/begin-ticket-use',async route=>{state=committed(state,'TaxCollector');await route.fulfill({json:state});});
 await page.route('**/api/campaign/shop/use-purchased',async route=>{submitted=route.request().postDataJSON();state={...state,version:'fixture:2',saveStatus:'failed',notice:'Could not save progress. Changes are retained in this session only.',shop:{...state.shop,useSession:{...state.shop.useSession,purchasedItemUsed:true}},capabilities:{...state.capabilities,'shop-finish-use':{enabled:true}}};await route.fulfill({json:state});});
 try{
  await open(page);await page.getByRole('button',{name:'Use Tax Collector now',exact:true}).click();await settled(page);
  const panel=page.locator('[data-shop-use-session]');assert.equal(await panel.locator('[data-card-id]').count(),6);
  assert.equal(await page.getByRole('button',{name:'Use Tax Collector',exact:true}).isEnabled(),true);
  assert.equal(await panel.locator('[data-card-id]:disabled').count(),6);
  assert.equal(await page.getByRole('button',{name:'Done',exact:true}).isDisabled(),true);
  await page.getByRole('button',{name:'Use Tax Collector',exact:true}).click();await settled(page);
  assert.deepEqual(submitted.targetIds,[]);assert.equal(await page.getByRole('button',{name:'Done',exact:true}).isEnabled(),true);
  assert.match(await page.locator('#campaign-status').innerText(),/not saved/);assert.equal(await page.locator('#campaign-save-warning').isVisible(),true);
 }finally{await context.close();}
});
test('Gambler selects two distinct cards and can switch to a held ticket within the committed menu',{skip},async()=>{
 const context=await browser.newContext({reducedMotion:'reduce'}),page=await context.newPage();let submitted;
 let state=fixture({shop:{offers:[ticketOffer('Gambler',2)],rerollPrice:2,rerollCount:0,reroll:{enabled:true}}});
 await page.route('**/api/campaign',route=>route.fulfill({json:state}));
 await page.route('**/api/campaign/shop/begin-ticket-use',async route=>{state=committed(state,'Gambler');await route.fulfill({json:state});});
 await page.route('**/api/campaign/shop/use-purchased',async route=>{submitted=route.request().postDataJSON();state={...state,version:'fixture:2',shop:{...state.shop,useSession:{...state.shop.useSession,purchasedItemUsed:true}},capabilities:{...state.capabilities,'shop-finish-use':{enabled:true}}};await route.fulfill({json:state});});
 try{
  await open(page);assert.equal(await page.getByRole('button',{name:'Use Devilish Deal',exact:true}).isDisabled(),true);
  await page.getByRole('button',{name:'Use Gambler now',exact:true}).click();await settled(page);
  const panel=page.locator('[data-shop-use-session]'),confirm=panel.locator('[data-campaign-action="apply-session-item"]');
  assert.equal(submitted,undefined);assert.deepEqual(await panel.locator('[data-card-id]').evaluateAll(cards=>cards.map(c=>Number(c.dataset.cardId))),[0,1,2,3,4,5]);
  assert.equal(await confirm.isDisabled(),true);await panel.locator('[data-card-id="4"]').click();assert.equal(await confirm.isDisabled(),true);
  await panel.locator('[data-card-id="1"]').click();assert.equal(await confirm.isEnabled(),true);assert.equal(await panel.locator('[data-card-id="0"]').isDisabled(),true);
  await panel.locator('[data-card-id="4"]').click();assert.equal(await confirm.isDisabled(),true);
  await page.getByRole('button',{name:'Choose Devilish Deal',exact:true}).click();assert.equal(await panel.locator('[data-card-id][aria-pressed="true"]').count(),0);
  await page.getByRole('button',{name:'Choose Gambler',exact:true}).click();await page.keyboard.press('Escape');assert.equal(await panel.count(),1);assert.equal(await page.getByRole('button',{name:'Done',exact:true}).isDisabled(),true);
  for(const id of [4,1])await panel.locator('[data-card-id="'+id+'"]').click();await confirm.click();await settled(page);
  assert.deepEqual(submitted.targetIds,[4,1]);assert.equal(await page.getByRole('button',{name:'Done',exact:true}).isEnabled(),true);
 }finally{await context.close();}
});
test('real paid Gambler spread preserves six offered cards and makes exactly the two selected cards Lucky',{skip},async()=>{
 const context=await seeded('gambler',{viewport:{width:1280,height:900}}),page=await context.newPage();
 try{
  await open(page);const before=await stateOf(page),offer=before.shop.offers.find(o=>o.tickets.some(t=>t.id==='Gambler'));
  const ids=offer.targetCards.map(c=>c.id),selected=[ids[4],ids[1]];
  assert.equal(offer.tickets.find(t=>t.id==='Gambler').targetCount,2);
  await page.locator('[data-campaign-action="use-ticket"][data-ticket-id="Gambler"]').click();await settled(page);
  assert.deepEqual(await page.locator('[data-shop-use-session]').locator('[data-card-id]').evaluateAll(cards=>cards.map(c=>Number(c.dataset.cardId))),ids);
  assert.equal(await page.locator('[data-shop-use-session]').locator('button[data-card-id]').count(),6);
  const confirm=page.locator('[data-campaign-action="apply-session-item"]');
  assert.equal(await confirm.isDisabled(),true);
  await page.locator('[data-shop-use-session]').locator('[data-card-id="'+selected[0]+'"]').click();
  assert.equal(await confirm.isDisabled(),true);
  await page.locator('[data-shop-use-session]').locator('[data-card-id="'+selected[1]+'"]').click();
  assert.equal(await confirm.isEnabled(),true);
  assert.equal(await page.locator('[data-shop-use-session]').locator('[data-card-id][aria-pressed="true"]').count(),2);
  await screenshot(page,'campaign-gambler-picker-desktop');await page.setViewportSize({width:390,height:844});await screenshot(page,'campaign-gambler-picker-compact');
  await page.keyboard.press('Escape');await page.reload();await page.getByRole('button',{name:'Play campaign',exact:true}).click();await settled(page);
  assert.deepEqual((await stateOf(page)).shop.useSession.targetCards,offer.targetCards);
  assert.equal((await stateOf(page)).shop.useSession.purchasedItemUsed,false);
  assert.deepEqual(await page.locator('[data-shop-use-session]').locator('[data-card-id]').evaluateAll(cards=>cards.map(c=>Number(c.dataset.cardId))),ids);
  assert.equal(await confirm.isDisabled(),true);
  for(const id of selected)await page.locator('[data-shop-use-session]').locator('[data-card-id="'+id+'"]').click();
  await confirm.click();await settled(page);
  const after=await stateOf(page),changed=after.deck.filter(c=>JSON.stringify(c)!==JSON.stringify(before.deck.find(old=>old.id===c.id)));
  assert.equal(after.run.money,before.run.money);assert.equal(after.inventory.itemCount,before.inventory.itemCount);
  assert.deepEqual(changed.map(c=>c.id).sort((a,b)=>a-b),[...selected].sort((a,b)=>a-b));
  assert.ok(changed.every(c=>c.enhancement==='Lucky'));
  assert.equal(after.shop.offers.find(o=>o.id===offer.id).sold,true);
 }finally{await context.close();}
});
test('real held Gambler selects two cards only from the human hand and consumes the ticket',{skip},async()=>{
 const context=await seeded('gambler-hand'),page=await context.newPage();
 try{
  await open(page);await page.getByRole('button',{name:'Continue run',exact:true}).click();await settled(page);
  const before=await stateOf(page),hand=before.table.players.find(p=>p.seat===0).visibleCards;
  const selected=[hand[4].id,hand[1].id];
  await page.getByRole('button',{name:'Use Gambler',exact:true}).click();
  assert.deepEqual(await page.getByRole('dialog').locator('button[data-card-id]').evaluateAll(cards=>cards.map(c=>Number(c.dataset.cardId))),hand.map(c=>c.id));
  const confirm=page.getByRole('button',{name:'Confirm targets',exact:true});
  assert.equal(await confirm.isDisabled(),true);
  await page.getByRole('dialog').locator('[data-card-id="'+selected[0]+'"]').click();
  assert.equal(await confirm.isDisabled(),true);
  await page.getByRole('dialog').locator('[data-card-id="'+selected[1]+'"]').click();
  assert.equal(await confirm.isEnabled(),true);
  await confirm.click();await settled(page);
  const after=await stateOf(page);
  assert.deepEqual(after.deck,before.deck.map(c=>selected.includes(c.id)?{...c,enhancement:'Lucky'}:c));
  assert.deepEqual(after.table.players.find(p=>p.seat===0).visibleCards,hand.map(c=>selected.includes(c.id)?{...c,enhancement:'Lucky'}:c));
  assert.equal(after.inventory.tarot.length,0);
 }finally{await context.close();}
});

test('a late campaign mutation cannot reopen a departed view, and one purchase is sent',{skip},async()=>{
 const context=await browser.newContext({reducedMotion:'reduce'}),page=await context.newPage();let release,requests=0;
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
 const context=await browser.newContext({reducedMotion:'reduce'}),page=await context.newPage();let command;
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
 const context=await browser.newContext({reducedMotion:'reduce'}),page=await context.newPage();let state=fixture({phase:'Recovery',hasRun:false,run:null,inventory:null}),mutations=0;
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

test('keyboard same-phase mutation restores its control after unlocking and removed actions use a heading fallback',{skip},async()=>{
 const context=await seeded('shop'),page=await context.newPage();
 try{
  await open(page);
  const reroll=page.locator('[data-campaign-action="shop-reroll"]');
  await reroll.focus();await page.keyboard.press('Enter');await settled(page);
  assert.equal(await reroll.evaluate(element=>element===document.activeElement),true,'Keyboard reroll retains its place after controls unlock');
  const sell=page.locator('[data-campaign-action="sell"]').first();
  await sell.focus();await page.keyboard.press('Enter');await settled(page);
  assert.equal(await page.locator('#campaign-content h1').evaluate(element=>element===document.activeElement),true,'A removed sale control falls back to the shop heading');
 }finally{await context.close();}
});
test('a stalled accepted POST has a bounded wait and reconciles one charge without replay',{skip},async()=>{
 const context=await seeded('shop'),page=await context.newPage();
 let releaseResponse,acceptedResolve,calls=0;
 const accepted=new Promise(resolve=>acceptedResolve=resolve);
 await page.route('**/api/campaign/shop/reroll',async route=>{
  calls++;const response=await route.fetch();assert.equal(response.status(),200);
  acceptedResolve();await new Promise(resolve=>releaseResponse=resolve);
  await route.fulfill({response}).catch(()=>{});
 });
 try{
  await open(page);const before=await stateOf(page);
  await page.clock.install();
  await page.locator('[data-campaign-action="shop-reroll"]').focus();await page.keyboard.press('Enter');
  await accepted;
  assert.equal(await page.locator('#campaign-view').getAttribute('aria-busy'),'true');
  await page.clock.runFor(9000);
  await page.waitForFunction(()=>document.querySelector('#campaign-view').getAttribute('aria-busy')==='false',{},{timeout:1500});
  const after=await stateOf(page);
  assert.equal(calls,1);assert.equal(after.run.money,before.run.money-before.shop.rerollPrice);
  assert.equal(after.shop.rerollCount,before.shop.rerollCount+1);
  assert.match(await page.locator('#campaign-status').innerText(),/may have been accepted/i);
  assert.equal(await page.locator('#campaign-refresh').isEnabled(),true);
  releaseResponse?.();await page.waitForTimeout(100);
  assert.equal(calls,1);assert.equal((await stateOf(page)).version,after.version);
 }finally{releaseResponse?.();await context.close();}
});
test('leaving a stalled accepted mutation recovers on reopen and a late response cannot replace the fresh view',{skip},async()=>{
 const context=await browser.newContext({reducedMotion:'reduce'}),page=await context.newPage();
 let state=fixture(),releaseResponse,acceptedResolve,calls=0;
 const accepted=new Promise(resolve=>acceptedResolve=resolve);
 await page.route('**/api/campaign',route=>route.fulfill({json:state}));
 await page.route('**/api/campaign/shop/reroll',async route=>{
  calls++;state={...state,version:'fixture:2',run:{...state.run,money:28},shop:{...state.shop,rerollCount:1}};
  const oldResponse=state;acceptedResolve();
  await new Promise(resolve=>releaseResponse=resolve);
  await route.fulfill({json:oldResponse}).catch(()=>{});
 });
 try{
  await open(page);
  await page.locator('[data-campaign-action="shop-reroll"]').click();await accepted;
  await page.locator('.nav-item[data-view="lobby"]').click();
  state={...state,version:'fixture:3',run:{...state.run,money:25},shop:{...state.shop,rerollCount:2}};
  await page.getByRole('button',{name:'Play campaign',exact:true}).click();
  await page.waitForFunction(()=>document.querySelector('#campaign-view').getAttribute('aria-busy')==='false',{},{timeout:1500});
  assert.match(await page.locator('.campaign-stats').innerText(),/\$25/);
  assert.equal(await page.locator('#campaign-refresh').isEnabled(),true);
  releaseResponse();await page.waitForTimeout(100);
  assert.match(await page.locator('.campaign-stats').innerText(),/\$25/);
  assert.equal(calls,1);
 }finally{releaseResponse?.();await context.close();}
});

test('real Arcade Collector\'s Pack menu uses held Gambler and zero-target tickets, survives restarts, and requires purchased item',{skip},async()=>{
 const context=await seeded('shop-use',{viewport:{width:1280,height:1000}}),page=await context.newPage();
 try{
  await open(page);const before=await stateOf(page);
  await page.getByRole('button',{name:'Enter Arcade',exact:true}).click();
  await page.getByRole('button',{name:'Drop ball · $3',exact:true}).click();await settled(page);
  const offer=(await stateOf(page)).shop.arcade.pending,ids=offer.targetCards.map(c=>c.id),chosen=[ids[4],ids[1]];
  assert.equal(offer.prizeCategory,'Sigil');assert.equal(offer.pack.length,3);
  assert.equal(await page.locator('[data-campaign-action="arcade-keep"],[data-campaign-action="arcade-use"]').count(),0);
  assert.equal(await page.getByRole('button',{name:'Back to shop',exact:true}).count(),0);
  const index=offer.pack.findIndex(c=>!c.legendary),pickedCard=offer.pack[index];
  await screenshot(page,'collector-pack-desktop');
  await page.getByRole('button',{name:pickedCard.title+' — '+pickedCard.name,exact:true}).click();await settled(page);
  let state=await stateOf(page);assert.equal(state.run.money,before.run.money-3);assert.equal(state.inventory.itemCount,4);
  const panel=page.locator('[data-shop-use-session]'),use=panel.locator('[data-campaign-action="apply-session-item"]');
  assert.deepEqual(await panel.locator('[data-card-id]').evaluateAll(cards=>cards.map(c=>Number(c.dataset.cardId))),ids);
  assert.equal(await page.getByRole('button',{name:'Done',exact:true}).isDisabled(),true);
  assert.equal(await page.getByRole('button',{name:/Cancel|Back to shop|Leave shop/}).count(),0);
  await screenshot(page,'committed-sigil-desktop');
  await page.getByRole('button',{name:'Choose Gambler',exact:true}).click();
  await panel.locator('[data-card-id="'+chosen[0]+'"]').click();assert.equal(await use.isDisabled(),true);
  await panel.locator('[data-card-id="'+chosen[1]+'"]').click();assert.equal(await use.isEnabled(),true);
  await screenshot(page,'committed-held-gambler-desktop');
  await page.setViewportSize({width:390,height:844});
  assert.ok(await page.evaluate(()=>Math.max(document.body.scrollWidth,document.documentElement.scrollWidth)<=innerWidth),'Committed menu fits compact viewport');
  await screenshot(page,'committed-held-gambler-compact');
  await page.keyboard.press('Escape');assert.equal(await panel.count(),1);assert.equal(await use.isEnabled(),true);
  await use.click();await settled(page);
  state=await stateOf(page);assert.equal(state.inventory.tarot.length,2);assert.equal(state.shop.useSession.purchasedItemUsed,false);
  assert.ok(state.shop.useSession.targetCards.filter(c=>chosen.includes(c.id)).every(c=>c.enhancement==='Lucky'));
  assert.equal(await page.getByRole('button',{name:'Done',exact:true}).isDisabled(),true);
  const paid=state;await stopServer();await startServer();await page.goto(base);await page.getByRole('button',{name:'Play campaign',exact:true}).click();await settled(page);
  state=await stateOf(page);assert.deepEqual(state.shop.useSession,paid.shop.useSession);assert.equal(state.run.money,paid.run.money);assert.equal(await panel.count(),1);
  await panel.locator('[data-card-id="'+chosen[0]+'"]').click();await use.click();await settled(page);
  state=await stateOf(page);assert.equal(state.shop.useSession.purchasedItemUsed,true);assert.equal(await page.getByRole('button',{name:'Done',exact:true}).isEnabled(),true);
  assert.equal(state.deck.find(c=>c.id===chosen[0]).sigil,pickedCard.key);assert.equal(state.deck.find(c=>c.id===chosen[0]).enhancement,'Lucky');
  await page.setViewportSize({width:1280,height:1000});await screenshot(page,'committed-purchase-used-desktop');
  const applied=state;await stopServer();await startServer();await page.goto(base);await page.getByRole('button',{name:'Play campaign',exact:true}).click();await settled(page);
  state=await stateOf(page);assert.deepEqual(state.shop.useSession,applied.shop.useSession);assert.equal(state.run.money,applied.run.money);
  await page.getByRole('button',{name:'Choose Taxes',exact:true}).click();assert.equal(await use.isEnabled(),true);assert.equal(await panel.locator('[data-card-id]:disabled').count(),6);
  await use.click();await settled(page);assert.equal((await stateOf(page)).run.money,paid.run.money+20);
  await page.getByRole('button',{name:'Choose Astronomer',exact:true}).click();await use.click();await settled(page);assert.equal((await stateOf(page)).inventory.tarot.length,0);
  await page.getByRole('button',{name:'Done',exact:true}).click();await settled(page);
  assert.equal(await panel.count(),0);assert.equal((await stateOf(page)).shop.useSession,null);
  assert.equal(await page.getByRole('button',{name:'Use Bear',exact:true}).isDisabled(),true);
  await page.getByRole('button',{name:'Leave shop',exact:true}).click();await settled(page);assert.equal((await stateOf(page)).phase,'Intro');
 }finally{await context.close();}
});

test('unchanged shop polls preserve chosen Tarot and ordered targets before and after inventory slots shift',{skip},async()=>{
 const context=await browser.newContext({reducedMotion:'reduce'}),page=await context.newPage();
 let state=fixture({inventory:{...fixture().inventory,tarot:[{slot:0,id:'Gambler',targetCount:2},{slot:1,id:'DevilishDeal',targetCount:3}],itemCount:2},
  shop:{offers:[ticketOffer('Chameleon',1)],rerollPrice:2,rerollCount:0,reroll:{enabled:true}}}),submitted;
 await page.route('**/api/campaign',route=>route.fulfill({json:{...state,feedback:null}}));
 await page.route('**/api/campaign/shop/begin-ticket-use',async route=>{
  state={...committed(state,'Chameleon'),feedback:{message:'Campaign updated.',grants:[]}};await route.fulfill({json:state});
 });
 await page.route('**/api/campaign/items/use',async route=>{
  submitted=route.request().postDataJSON();
  state={...state,version:'fixture:used-gambler',inventory:{...state.inventory,tarot:[{slot:0,id:'DevilishDeal',targetCount:3}],itemCount:1},feedback:{message:'Campaign updated.',grants:[]}};
  await route.fulfill({json:state});
 });
 const poll=async()=>{
  await page.waitForResponse(response=>response.request().method()==='GET'&&response.url().endsWith('/api/campaign'),{timeout:4500});
  await settled(page);await page.waitForTimeout(50);
 };
 try{
  await open(page);await page.getByRole('button',{name:'Use Chameleon now',exact:true}).click();await settled(page);
  const panel=page.locator('[data-shop-use-session]'),apply=panel.locator('[data-campaign-action="apply-session-item"]');
  await page.getByRole('button',{name:'Choose Gambler',exact:true}).click();await panel.locator('[data-card-id="4"]').click();
  await poll();
  assert.equal(await panel.locator('[data-session-item-name]').textContent(),'Use Gambler');
  assert.equal(await panel.locator('[data-session-selection-status]').innerText(),'1 / 2 selected');
  assert.equal(await panel.locator('[data-card-id="4"]').getAttribute('aria-pressed'),'true');
  await panel.locator('[data-card-id="1"]').click();await apply.click();await settled(page);
  assert.deepEqual(submitted.targetIds,[4,1]);assert.equal(submitted.slot,0);
  // A genuine inventory revision starts a fresh choice; the remaining ticket moved into slot zero.
  assert.equal(await panel.locator('[data-session-item-name]').textContent(),'Use Chameleon');
  assert.equal(await panel.locator('[data-card-id][aria-pressed="true"]').count(),0);
  await page.getByRole('button',{name:'Choose Devilish Deal',exact:true}).click();
  await panel.locator('[data-card-id="3"]').click();await panel.locator('[data-card-id="0"]').click();
  await poll();
  assert.equal(await panel.locator('[data-session-item-name]').textContent(),'Use Devilish Deal');
  assert.equal(await panel.locator('[data-session-selection-status]').innerText(),'2 / 3 selected');
  assert.match(await panel.locator('[data-card-id="3"]').getAttribute('aria-label'),/target 1$/);
  assert.match(await panel.locator('[data-card-id="0"]').getAttribute('aria-label'),/target 2$/);
  assert.equal(await panel.locator('[data-campaign-action="select-session-item"][data-slot="0"]').getAttribute('aria-pressed'),'true');
  // A save-status notice also rerenders without changing the authoritative game version.
  state={...state,saveStatus:'failed',notice:'Could not save progress.'};
  await page.getByRole('button',{name:'Refresh campaign',exact:true}).click();
  await page.locator('#campaign-save-warning').waitFor({state:'visible'});
  assert.equal(await panel.locator('[data-session-item-name]').textContent(),'Use Devilish Deal');
  assert.equal(await panel.locator('[data-session-selection-status]').innerText(),'2 / 3 selected');
  assert.equal(await page.locator('#campaign-save-warning').isVisible(),true);
  // A new authoritative version still clears all stale targets on the next read.
  state={...state,version:'fixture:external-change'};
  await page.getByRole('button',{name:'Refresh campaign',exact:true}).click();
  await page.waitForFunction(()=>document.querySelector('[data-session-item-name]').textContent==='Use Chameleon');
  assert.equal(await panel.locator('[data-session-item-name]').textContent(),'Use Chameleon');
  assert.equal(await panel.locator('[data-card-id][aria-pressed="true"]').count(),0);
 }finally{await context.close();}
});

test('Arcade keyboard choices survive polling, paid Plinko animates once and returns to used controls',{skip},async()=>{
 const context=await seeded('arcade-loss',{reducedMotion:'no-preference',viewport:{width:1280,height:1000}}),page=await context.newPage();
 await page.addInitScript(()=>{const animate=Element.prototype.animate;window.arcadeAnimations=0;Element.prototype.animate=function(...args){if(this.matches('[data-arcade-ball],[data-arcade-wheel]'))window.arcadeAnimations++;return animate.apply(this,args);};});
 try{
  await open(page);await page.getByRole('button',{name:'Enter Arcade',exact:true}).focus();await page.keyboard.press('Enter');
  await page.getByRole('button',{name:'Lane 3',exact:true}).focus();await page.keyboard.press('Enter');
  await page.getByRole('button',{name:'8 black',exact:true}).click();
  await page.waitForResponse(r=>r.request().method()==='GET'&&r.url().endsWith('/api/campaign'));
  assert.equal(await page.getByRole('button',{name:'Lane 3',exact:true}).getAttribute('aria-pressed'),'true');
  assert.equal(await page.getByRole('button',{name:'8 black',exact:true}).getAttribute('aria-pressed'),'true');
  await page.getByRole('button',{name:'Lane 5',exact:true}).click();const before=await stateOf(page);
  await screenshot(page,'arcade-games-desktop');await page.setViewportSize({width:390,height:844});
  assert.ok(await page.evaluate(()=>Math.max(document.body.scrollWidth,document.documentElement.scrollWidth)<=innerWidth),'Arcade fits compact viewport');
  await screenshot(page,'arcade-games-compact');
  await page.getByRole('button',{name:'Drop ball · $3',exact:true}).click();await settled(page);
  const paid=await stateOf(page),result=paid.shop.arcade.pending;assert.equal(paid.run.money,before.run.money-3);assert.equal(result.score,1);
  await page.waitForTimeout(3000);assert.equal(await page.evaluate(()=>window.arcadeAnimations),1);
  assert.ok(Math.abs(await page.locator('[data-arcade-ball]').evaluate(el=>parseFloat(el.style.left))-100*(112+98*result.landing)/1199)<.0001);
  assert.ok(Math.abs(await page.locator('[data-arcade-ball]').evaluate(el=>parseFloat(el.style.top))-100*1082/1312)<.0001);
  assert.equal(await page.getByRole('button',{name:'Back to shop',exact:true}).count(),0);
  await page.getByRole('button',{name:'Done',exact:true}).click();await settled(page);
  assert.equal(await page.getByRole('button',{name:'Drop ball · $3',exact:true}).isDisabled(),true);
  assert.equal(await page.getByRole('button',{name:'Spin wheel · $3',exact:true}).isEnabled(),true);
  await page.getByRole('button',{name:'Back to shop',exact:true}).click();assert.equal(await page.getByRole('heading',{name:'The shop.',exact:true}).count(),1);
 }finally{await context.close();}
});
test('saved roulette result uses exact pointer geometry and reduced motion without replaying polls',{skip},async()=>{
 const context=await browser.newContext({reducedMotion:'reduce'}),page=await context.newPage();
 const pending={playId:'arcade:1:roulette',game:'Roulette',choice:8,landing:8,score:100,path:[],prizeCategory:'Sigil',featureId:'Fish',targetCards:[0,1,2,3,4,5].map(id=>card(id)),keep:{enabled:true},use:{enabled:true},dismiss:{enabled:false}};
 const state=fixture({shop:{offers:[],rerollPrice:2,rerollCount:0,reroll:{enabled:false},arcade:{price:3,plinkoPlayed:false,roulettePlayed:true,plinko:{enabled:false},roulette:{enabled:false},pending}}});
 await page.route('**/api/campaign',route=>route.fulfill({json:state}));
 try{
  await open(page);assert.equal(await page.locator('[data-arcade-result]').getAttribute('data-landing'),'8');
  assert.equal(await page.locator('[data-arcade-wheel]').evaluate(el=>el.style.transform),'rotate(150deg)');
  assert.equal(await page.locator('[data-arcade-wheel]').evaluate(el=>el.getAnimations().length),0);
  await page.waitForResponse(r=>r.request().method()==='GET'&&r.url().endsWith('/api/campaign'));
  assert.equal(await page.locator('[data-arcade-wheel]').evaluate(el=>el.style.transform),'rotate(150deg)');
  assert.match(await page.locator('.arcade-result-copy').innerText(),/wheel landed on 8/);
  assert.equal(await page.getByRole('button',{name:'Back to shop',exact:true}).count(),0);
 }finally{await context.close();}
});

test('switching to reduced motion during an Arcade result settles immediately without changing its outcome',{skip},async()=>{
 const context=await browser.newContext({reducedMotion:'no-preference'}),page=await context.newPage();
 const pending={playId:'arcade:2:roulette',game:'Roulette',choice:3,landing:10,score:0,path:[],prizeCategory:null,featureId:null,targetCards:[],keep:{enabled:false},use:{enabled:false},dismiss:{enabled:true}};
 const state=fixture({shop:{offers:[],arcade:{price:3,plinko:{enabled:false},roulette:{enabled:false},pending}}});
 await page.route('**/api/campaign',route=>route.fulfill({json:state}));
 try{
  await open(page);assert.equal(await page.locator('[data-arcade-wheel]').evaluate(el=>el.getAnimations().length),1);
  await page.emulateMedia({reducedMotion:'reduce'});
  await page.waitForFunction(()=>document.querySelector('[data-arcade-wheel]').getAnimations().length===0);
  assert.equal(await page.locator('[data-arcade-wheel]').evaluate(el=>el.style.transform),'rotate(90deg)');
  assert.match(await page.locator('.arcade-result-copy').innerText(),/wheel landed on 10/);
  await page.reload();await page.getByRole('button',{name:'Play campaign',exact:true}).click();await settled(page);
  assert.equal(await page.locator('[data-arcade-wheel]').evaluate(el=>el.getAnimations().length),0);
 }finally{await context.close();}
});

test('real Legendary pack card fills Joker slots and names them once',{skip},async()=>{
 const context=await seeded('arcade-legendary',{viewport:{width:1280,height:1000}}),page=await context.newPage();
 try{
  await open(page);
  await page.getByRole('button',{name:'Enter Arcade',exact:true}).click();
  await page.getByRole('button',{name:'Drop ball · $3',exact:true}).click();await settled(page);
  const before=await stateOf(page);
  const free=5-before.inventory.jokers.length;
  await page.getByRole('button',{name:'Legendary — fills '+free+' empty Joker slot'+(free===1?'':'s'),exact:true}).click();await settled(page);
  const state=await stateOf(page);
  assert.equal(state.inventory.jokers.length,5);assert.equal(state.shop.useSession,null);assert.equal(state.shop.arcade.pending,null);
  const granted=state.inventory.jokers.map(j=>j.id).filter(id=>!before.inventory.jokers.some(j=>j.id===id));
  const note=await page.locator('.collector-granted').innerText();
  for(const id of granted)assert.match(note,new RegExp(state.catalog.find(f=>f.category==='Joker'&&f.key===id).name));
  await page.getByRole('button',{name:'Back to shop',exact:true}).click();
 }finally{await context.close();}
});
