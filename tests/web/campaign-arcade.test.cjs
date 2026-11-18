const {test}=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs'),path=require('node:path'),vm=require('node:vm');
function boot(){
 const scope=vm.createContext({window:{},escapeHtml:value=>String(value).replaceAll('&','&amp;').replaceAll('"','&quot;').replaceAll('<','&lt;'),splitWords:value=>String(value),cardFaceHtml:()=>'',cardRank:face=>face.rank});
 for(const [file,name] of [['campaign-cards.js','CampaignCards'],['campaign-screens.js','CampaignScreens'],['campaign-arcade.js','CampaignArcadeView']]){
  vm.runInContext(fs.readFileSync(path.join(__dirname,'../../src/BidEuchre.Server/wwwroot',file),'utf8'),scope);scope[name]=scope.window[name];
 }
 return scope;
}
const enabled={enabled:true},disabled={enabled:false,reason:'Already played this shop visit.'};
function state(pending=null){return{version:'fixture:2',phase:'Shop',shop:{arcade:{price:3,plinko:enabled,roulette:disabled,pending}},capabilities:{},inventory:{tarot:[],sigils:[],jokers:[],gumballs:[],itemCount:0,itemSlots:4,jokerSlots:5}};}
test('Arcade renders both games with their price, accessible choices, odds rules and play limit',()=>{
 const view=boot().CampaignArcadeView,html=view.render(state());
 assert.match(html,/Drop ball · \$3/);assert.match(html,/Spin wheel · \$3/);
 assert.equal((html.match(/data-campaign-action="arcade-lane"/g)||[]).length,8);
 assert.equal((html.match(/data-campaign-action="arcade-pocket"/g)||[]).length,12);
 assert.match(html,/arcade-play-roulette[^>]*disabled/);assert.match(html,/Exact match: 100/);assert.match(html,/Matching color: 50/);
 assert.match(html,/Back to shop/);assert.match(html,/arcade-plinko-board.png/);
});
test('all twelve wheel outcomes align the saved landing at the fixed top pointer',()=>{
 const view=boot().CampaignArcadeView;
 for(let landing=1;landing<=12;landing++){
  assert.equal(((landing-1)*30+view.wheelAngle(landing))%360,0);
  const html=view.render(state({playId:'test',game:'Roulette',choice:7,landing,score:0,path:[],prizeCategory:null,dismiss:enabled}));
  assert.match(html,new RegExp('transform:rotate\\('+view.wheelAngle(landing)+'deg\\)'));
  assert.match(html,new RegExp('wheel landed on '+landing+'\\.'));
 }
 for(let slot=0;slot<11;slot++)assert.equal(view.binPoint(slot).left,(100*(112+98*slot)/1199)+'%');
});
test('winning result cannot return to shop; full storage exposes Use Now and loss exposes Done',()=>{
 const view=boot().CampaignArcadeView;
 const prize={playId:'arcade:1:plinko',game:'Plinko',choice:4,landing:4,path:[8,9,10,9,8,7,6,7,8],score:100,prizeCategory:'Sigil',featureId:'Fish',keep:{enabled:false,reason:'Full'},use:enabled,dismiss:disabled};
 let html=view.render(state(prize));
 assert.match(html,/arcade-keep[^>]*disabled/);assert.match(html,/arcade-use[^>]*>Use Fish now/);
 assert.doesNotMatch(html,/Back to shop|arcade-dismiss/);
 html=view.render(state({...prize,score:0,prizeCategory:null,featureId:null,dismiss:enabled}));
 assert.match(html,/arcade-dismiss[^>]*>Done/);assert.doesNotMatch(html,/arcade-use|arcade-keep/);
});
const pack=[{key:'Bear',title:'Strength',name:'Bear Sigil',legendary:false},{key:'Legendary',title:'Legendary',name:'5 random Jokers',legendary:true},{key:'Fish',title:'Abundance',name:'Fish Sigil',legendary:false}];
const packResult={playId:'arcade:1:plinko',game:'Plinko',choice:4,landing:0,path:[14,13,12,11,10,9,8,7,6,5,4,3,2,1,0],score:6,prizeCategory:'Sigil',featureId:null,pack,keep:{enabled:false,reason:'Open your pack.'},use:{enabled:false},pick:enabled,dismiss:disabled};
test('pack result renders three pick buttons and no keep',()=>{
 const html=boot().CampaignArcadeView.render(state(packResult));
 assert.equal((html.match(/data-campaign-action="arcade-pick"/g)||[]).length,3);
 for(const [index,label] of ['Strength — Bear Sigil','Legendary — 5 random Jokers','Abundance — Fish Sigil'].entries())
  assert.match(html,new RegExp('data-index="'+index+'"[^>]*aria-label="'+label+'"'));
 assert.match(html,/src="\/pixel\/curated-collector-bear\.png"/);assert.match(html,/collector-card legendary/);
 assert.doesNotMatch(html,/arcade-keep|arcade-use|Back to shop/);
 assert.match(html,/Collector's Pack/);
});
function mountEnv(){
 const scope=boot();
 scope.document={hidden:false,body:{classList:{contains:()=>false}}};
 scope.matchMedia=()=>({matches:false,addEventListener(){},removeEventListener(){}});
 const cards=()=>[0,1,2].map(()=>{const classes=new Set();return {classes,style:{},classList:{add:c=>classes.add(c)}};});
 const container=current=>({querySelector:sel=>sel==='[data-arcade-result]'?{}:null,querySelectorAll:sel=>sel==='[data-collector-card]'?current:[]});
 return {view:scope.CampaignArcadeView,cards,container};
}
test('pack reveal does not replay on unchanged poll',()=>{
 const {view,cards,container}=mountEnv();
 const first=cards();view.mount(container(first),state(packResult));
 assert.ok(first.every(c=>c.classes.has('collector-flip')),'first presentation flips the cards');
 const again=cards();view.mount(container(again),state(packResult));
 assert.ok(again.every(c=>!c.classes.has('collector-flip')),'an unchanged poll must not replay the flip');
});
test('a Legendary pick names the granted Jokers once',()=>{
 const view=boot().CampaignArcadeView;
 view.noteGranted(['Alpha','Slick Willy']);
 assert.match(view.render(state()),/LEGENDARY! New Jokers: Alpha, Slick Willy/);
 assert.doesNotMatch(view.render(state()),/LEGENDARY!/);
});
