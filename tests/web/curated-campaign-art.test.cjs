const {test} = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const {createHash} = require('node:crypto');

function boot() {
  const styles = new Map(), classes = new Set();
  const body = {
    style:{setProperty:(key,value)=>styles.set(key,value),removeProperty:key=>styles.delete(key)},
    classList:{toggle:(name,on)=>on?classes.add(name):classes.delete(name)}
  };
  const scope = vm.createContext({window:{},document:{body},
    escapeHtml:value=>String(value).replaceAll('&','&amp;').replaceAll('"','&quot;').replaceAll('<','&lt;'),
    splitWords:value=>String(value).replace(/([a-z])([A-Z])/g,'$1 $2'),
    cardRank:card=>card.rank,
    cardFaceHtml:()=>'<span class="card-face-sprite" aria-hidden="true"></span>'});
  const root = path.join(__dirname,'../../src/BidEuchre.Server/wwwroot');
  vm.runInContext(fs.readFileSync(path.join(root,'campaign-cards.js'),'utf8'),scope);
  scope.CampaignCards = scope.window.CampaignCards;
  vm.runInContext(fs.readFileSync(path.join(root,'campaign-screens.js'),'utf8'),scope);
  return {cards:scope.window.CampaignCards,screens:scope.window.CampaignScreens,styles,classes};
}

test('every active ticket uses its matching curated artwork, including legacy IDs',()=>{
  const {cards}=boot();
  for(const [key,slug] of [
    ['Chameleon','chameleon'],['Grindstone','grindstone'],['Prospector','prospector'],
    ['DevilishDeal','devilish-deal'],['Gambler','gambler'],['Traitor','traitor'],
    ['Astronomer','astronomer'],['SpringLoaded','spring-loaded'],['TaxCollector','tax-collector']
  ]) assert.equal(cards.art({category:'Tarot',key,id:'tarot:'+slug}),'/pixel/curated-tarot-'+slug+'.png');
});

test('all eight active gumballs use curated artwork',()=>{
  const {cards}=boot();
  for(const [key,slug] of [
    ['Chaos','chaos'],['HighLow','high-low'],['Alone','alone'],['Duelist','duelist'],
    ['Poker','poker'],['PartnersBest','partners-best'],['Lowballer','lowballer'],['Telepath','telepath']
  ]) assert.equal(cards.art({category:'Gumball',key,id:'gumball:'+slug}),'/pixel/curated-gumball-'+slug+'.png');
});

test('all 32 active Jokers use their curated portraits',()=>{
  const {cards}=boot();
  const root=path.join(__dirname,'../..');
  const pixelRoot=path.join(root,'assets/pixel');
  const imported=JSON.parse(fs.readFileSync(path.join(pixelRoot,'curated-art.json'),'utf8'));
  const manifest=JSON.parse(fs.readFileSync(path.resolve(pixelRoot,imported.sourceManifest),'utf8'));
  const catalog=fs.readFileSync(path.join(root,'src/BidEuchre.Core/FeatureCatalog.cs'),'utf8');
  const keys=new Map([...catalog.matchAll(/new\("(joker:[^"]+)", FeatureCategory\.Joker, "([^"]+)"/g)].map(match=>[match[1],match[2]]));
  const imports=manifest.entries.filter(entry=>entry.category==='joker'&&entry.runtimeId);
  assert.equal(imports.length,32);
  for(const entry of imports) {
    assert.ok(keys.has(entry.runtimeId),'Imported Joker must match an existing gameplay ID: '+entry.runtimeId);
    assert.equal(cards.art({category:'Joker',key:keys.get(entry.runtimeId),id:entry.runtimeId}),'/pixel/'+entry.resource+'.png');
  }
  const doubleDown=cards.art({category:'Joker',key:'DoubleDown',id:'joker:double-down'});
  assert.equal(doubleDown,'/pixel/curated-joker-double-down.png');
  assert.ok(fs.existsSync(path.join(root,'assets',doubleDown)), 'Double Down portrait must exist');
  for(const [key,slug] of [['Boomerang','boomerang'],['OverAchiever','over-achiever'],['SystemError','system-error']])
    assert.equal(cards.art({category:'Joker',key,id:'joker:'+slug}),'/pixel/campaign-placeholders/joker/'+slug+'.png');
});

test('all runtime Joker textures are the exact approved uniform-border exports',()=>{
  const root=path.join(__dirname,'../..');
  const pixelRoot=path.join(root,'assets/pixel');
  const sheetRoot=path.join(root,'assets/sheets/jokers');
  const sheet=JSON.parse(fs.readFileSync(path.join(sheetRoot,'jokers.json'),'utf8'));
  const imported=JSON.parse(fs.readFileSync(path.join(pixelRoot,'curated-art.json'),'utf8'));
  const sourceRoot=path.dirname(path.resolve(pixelRoot,imported.sourceManifest));
  const jokers=imported.entries.filter(entry=>entry.category==='joker'&&entry.runtimeId);
  assert.equal(jokers.length,32);
  const hash=file=>createHash('sha256').update(fs.readFileSync(file)).digest('hex');
  for(const entry of jokers){
    const approved=sheet.frames[entry.runtimeId];
    assert.ok(approved,entry.runtimeId);
    assert.equal(hash(path.join(pixelRoot,entry.file)),approved.sha256,entry.runtimeId+' runtime artwork');
    assert.equal(hash(path.join(sheetRoot,approved.file)),approved.sha256,entry.runtimeId+' approved artwork');
    assert.equal(entry.outputSha256,approved.sha256);
    assert.equal(entry.runtimeSourceSha256,approved.sha256);
    assert.equal(hash(path.resolve(sourceRoot,entry.runtimeSource)),approved.sha256);
    assert.equal(entry.transform,'approved-uniform-joker-frame');
    assert.equal(entry.hasTransparency,true);
  }
});

test('all eight runtime enhancement textures exactly match the approved transparent exports',()=>{
  const root=path.join(__dirname,'../..');
  const pixelRoot=path.join(root,'assets/pixel');
  const sheetRoot=path.join(root,'assets/sheets/enhancements');
  const sheet=JSON.parse(fs.readFileSync(path.join(sheetRoot,'enhancements.json'),'utf8'));
  const imported=JSON.parse(fs.readFileSync(path.join(pixelRoot,'curated-art.json'),'utf8'));
  const sourceRoot=path.dirname(path.resolve(pixelRoot,imported.sourceManifest));
  const enhancements=imported.entries.filter(entry=>entry.category==='enhancement'&&entry.runtimeId);
  const hash=file=>createHash('sha256').update(fs.readFileSync(file)).digest('hex');
  const {cards}=boot();
  assert.equal(enhancements.length,8);
  assert.deepEqual(new Set(enhancements.map(entry=>entry.runtimeId)),new Set(Object.keys(sheet.frames)));
  for(const entry of enhancements){
    const approved=sheet.frames[entry.runtimeId];
    assert.equal(hash(path.join(pixelRoot,entry.file)),approved.sha256,entry.runtimeId+' runtime artwork');
    assert.equal(hash(path.join(sheetRoot,approved.file)),approved.sha256,entry.runtimeId+' approved artwork');
    assert.equal(entry.outputSha256,approved.sha256);
    assert.equal(entry.runtimeSourceSha256,approved.sha256);
    assert.equal(hash(path.resolve(sourceRoot,entry.runtimeSource)),approved.sha256);
    assert.equal(entry.hasTransparency,true);
    assert.deepEqual([entry.width,entry.height],[124,176]);
    assert.equal(cards.art({category:'Enhancement',key:approved.key,id:entry.runtimeId}),'/pixel/'+entry.file);
  }
});

test('shop machine illustrations accompany existing ticket and gumball offers without changing actions',()=>{
  const {screens}=boot();
  const enabled={enabled:true};
  const html=screens.render({phase:'Shop',capabilities:{'shop-leave':enabled},
    shop:{rerollCount:0,rerollPrice:2,reroll:enabled,offers:[
      {id:'ticket-offer',category:'Tarot',featureId:'',price:3,tickets:[{id:'Gambler',targetCount:2,keep:enabled,use:enabled}]},
      {id:'gumball-offer',category:'Gumball',featureId:'Chaos',price:3,buy:enabled},
      {id:'joker-offer',category:'Joker',featureId:'LuckyDuck',price:4,buy:enabled},
      {id:'sigil-offer',category:'Sigil',featureId:'Bear',price:3,buy:enabled,buyUse:enabled}
    ]},catalog:[
      {category:'Tarot',key:'Gambler',id:'tarot:gambler',name:'Gambler',description:'Enhance a card.'},
      {category:'Gumball',key:'Chaos',id:'gumball:chaos',name:'Chaos',description:'Unlock Chaos.'},
      {category:'Joker',key:'LuckyDuck',id:'joker:lucky-duck',name:'Lucky Duck',description:'Lucky cards trigger twice.'},
      {category:'Sigil',key:'Bear',id:'sigil:bear',name:'Bear',description:'Increase a rank.'}
    ]});
  assert.match(html,/src="\/pixel\/machine-fortune-teller-ticket-dispenser\.png"/);
  assert.match(html,/src="\/pixel\/machine-gumball\.png"/);
  assert.equal((html.match(/class="campaign-machine"/g)||[]).length,2);
  assert.match(html,/src="\/pixel\/curated-tarot-gambler\.png"/);
  assert.match(html,/src="\/pixel\/curated-gumball-chaos\.png"/);
  assert.match(html,/data-campaign-action="keep-ticket"[^>]+data-offer-id="ticket-offer"[^>]*>Keep Gambler<\/button>/);
  assert.match(html,/data-campaign-action="use-ticket"[^>]+data-offer-id="ticket-offer"[^>]*>Use Gambler now<\/button>/);
  assert.match(html,/data-campaign-action="pull"[^>]+data-offer-id="gumball-offer"[^>]*>Pull gumball · \$3<\/button>/);
  assert.match(html,/data-campaign-action="buy"[^>]+data-offer-id="joker-offer"/);
  assert.match(html,/data-campaign-action="buy-use"[^>]+data-offer-id="sigil-offer"/);
});

test('physical cards preserve face and accessible identity over curated enhancements and sigils',()=>{
  const {cards}=boot();
  const face={rank:'Ace',suit:'Spades'};
  for(const enhancement of ['Lucky','Golden','Wild','Cursed','Stone','Silver','Glass','Bouncy']) {
    const html=cards.html({id:7,face,enhancement,sigil:'Bear'});
    assert.match(html,new RegExp('/pixel/curated-enhancement-'+enhancement.toLowerCase()+'\\.png'));
    assert.match(html,/campaign-card-enhanced/);
    assert.match(html,/card-face-sprite/);
    assert.match(html,/aria-label="Ace of Spades, .* enhancement, Bear sigil"/);
  }
  for(const sigil of ['Bear','Chameleon','Duck','Eagle','Fish','Frog','Spider','Vulture']) {
    const html=cards.html({id:7,face,sigil});
    assert.match(html,new RegExp('<img[^>]+src="/pixel/curated-sigil-'+sigil.toLowerCase()+'\\.png"'));
  }
  const plain=cards.html({id:8,face});
  assert.match(plain,/card-background-white\.png/);
  assert.doesNotMatch(plain,/campaign-card-enhanced|campaign-sigil/);
});

test('ticket inventory retains protocol identity and presents tickets to players',()=>{
  const {screens}=boot();
  const html=screens.inventory({phase:'Shop',capabilities:{'items-use':{enabled:true},'items-discard':{enabled:true}},
    inventory:{tarot:[{id:'Chameleon',slot:0,targetCount:1}],sigils:[],jokers:[],gumballs:[],itemCount:1,itemSlots:4,jokerSlots:5},
    catalog:[{category:'Tarot',key:'Chameleon',id:'tarot:chameleon',name:'Festival',description:'Make one card Wild.'}]});
  assert.match(html,/shared Tickets \+ Sigil slots/);
  assert.match(html,/<article class="campaign-feature" data-category="Tarot">/);
  assert.match(html,/>Arcade ticket<\/p>/);
  assert.match(html,/data-category="Tarot" data-slot="0"/);
});

test('boss arenas follow the ante and reset outside an active boss match',()=>{
  const {screens,styles,classes}=boot();
  assert.equal(typeof screens.backdrop,'function');
  for(const [ante,arena] of [[1,'club'],[2,'diamond'],[3,'heart'],[4,'spade'],[5,'cursed'],[6,'club']]) {
    screens.backdrop({phase:'InMatch',run:{boss:true,ante}});
    assert.equal(styles.get('--table-base'),'url("/pixel/boss-arena-'+arena+'.png")');
    assert.equal(classes.has('campaign-boss-arena'),true);
  }
  for(const state of [null,{phase:'Shop',run:{boss:true,ante:5}},{phase:'Intro',run:{boss:true,ante:5}},{phase:'InMatch',run:{boss:false,ante:2}},{phase:'InMatch',requiresContinue:true,run:{boss:true,ante:2}}]) {
    screens.backdrop(state);
    assert.equal(styles.has('--table-base'),false);
    assert.equal(classes.has('campaign-boss-arena'),false);
  }
});

test('collector pack cards use their supplied 2:3 collector artwork',()=>{
  const {cards}=boot();
  for(const key of ['Bear','Eagle','Fish','Spider','Frog','Duck','Vulture','Chameleon','Legendary','Pack']) {
    const url=cards.collectorArt(key);
    assert.equal(url,'/pixel/curated-collector-'+key.toLowerCase()+'.png');
    assert.ok(fs.existsSync(path.join(__dirname,'../../assets/pixel',path.basename(url))),url+' must be generated');
  }
});

test('card-shaped Sigil surfaces (use menu, inventory, collection) use the collector card',()=>{
  const {cards}=boot();
  for(const [key,slug] of [['Bear','bear'],['Vulture','vulture'],['Chameleon','chameleon']])
    assert.equal(cards.art({category:'Sigil',key,id:'sigil:'+slug}),'/pixel/curated-collector-'+slug+'.png');
});
