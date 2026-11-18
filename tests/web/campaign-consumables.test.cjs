const {test}=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs'),path=require('node:path'),vm=require('node:vm');
function screens(){
 const scope=vm.createContext({window:{},escapeHtml:value=>String(value).replaceAll('&','&amp;').replaceAll('"','&quot;').replaceAll('<','&lt;'),splitWords:value=>String(value),cardFaceHtml:()=>'',cardRank:face=>face.rank});
 const root=path.join(__dirname,'../../src/BidEuchre.Server/wwwroot');
 vm.runInContext(fs.readFileSync(path.join(root,'campaign-cards.js'),'utf8'),scope);scope.CampaignCards=scope.window.CampaignCards;
 vm.runInContext(fs.readFileSync(path.join(root,'campaign-screens.js'),'utf8'),scope);return scope.window.CampaignScreens;
}
const enabled={enabled:true};
const inventory={tarot:[{id:'Chameleon',slot:0,targetCount:1}],sigils:[],jokers:[],gumballs:[],itemCount:1,itemSlots:4,jokerSlots:5};
test('held consumables stay disabled in Shop and Intro even with a stale enabled capability',()=>{
 for(const phase of ['Shop','Intro']){
  const html=screens().inventory({phase,capabilities:{'items-use':enabled,'items-discard':enabled},inventory});
  assert.match(html,/<button[^>]+data-campaign-action="use-item"[^>]+disabled/);
  assert.match(html,/during a match/i);
 }
});
test('Fortune Teller shows paid ticket choices and server print prices',()=>{
 const state={phase:'Shop',capabilities:{'shop-leave':enabled},shop:{rerollCount:0,rerollPrice:2,reroll:enabled,offers:[
  {id:'teller',category:'Tarot',featureId:'',price:3,sold:false,targetCards:[],tickets:[],printOptions:[{count:1,price:3,buy:enabled},{count:3,price:6,buy:enabled}]}
 ]}};
 let html=screens().render(state);
 assert.match(html,/Print 1 ticket · \$3/);assert.match(html,/Print 3 tickets · \$6/);
 assert.doesNotMatch(html,/data-campaign-action="buy"/);
 state.shop.offers[0].tickets=[{id:'Gambler',targetCount:2,keep:enabled,use:enabled}];
 html=screens().render(state);
 assert.match(html,/data-campaign-action="keep-ticket"[^>]+data-ticket-id="Gambler"/);
 assert.match(html,/data-campaign-action="use-ticket"[^>]+data-ticket-id="Gambler"/);
 assert.doesNotMatch(html,/data-campaign-action="print-tickets"/);
});
test('retired sold gumball stock remains readable and cannot offer another pull',()=>{
 const disabled={enabled:false,reason:'Sold out.'};
 const html=screens().render({phase:'Shop',capabilities:{'shop-leave':enabled},shop:{rerollCount:0,rerollPrice:2,reroll:enabled,offers:[
  {id:'gumball',category:'Gumball',featureId:'',price:3,sold:true,buy:disabled,buyUse:disabled,tickets:[],printOptions:[]}
 ]}});
 assert.match(html,/Gumball machine/);assert.match(html,/\$3 · Sold/);
 assert.match(html,/<button[^>]+data-campaign-action="pull"[^>]+disabled/);
 assert.doesNotMatch(html,/Panic Button|undefined/);
});
test('committed purchase screen offers held tickets and no path back before purchase is used',()=>{
 const state={phase:'Shop',inventory:{...inventory,sigils:[{id:'Bear',slot:0,targetCount:1}]},capabilities:{'items-use':enabled,'shop-finish-use':{enabled:false,reason:'Use your purchased item first.'}},
  shop:{useSession:{offerId:'sigil',category:'Sigil',featureId:'Fish',purchasedItemUsed:false,targetCount:1,targetCards:[0,1,2,3,4,5].map(id=>({id,face:{rank:'Ace',suit:'Clubs'}}))}}};
 let html=screens().render(state);
 assert.match(html,/data-shop-use-session/);
 assert.match(html,/data-campaign-action="select-session-item"[^>]*data-slot="0"/);
 assert.match(html,/data-campaign-action="finish-use"[^>]*disabled/);
 assert.doesNotMatch(html,/data-campaign-action="(?:shop-leave|keep-ticket|buy|shop-reroll|discard-item)"/);
 assert.doesNotMatch(html,/Use Bear|Cancel|Back to shop/);
 assert.equal((html.match(/data-card-id=/g)||[]).length,6);
 state.shop.useSession.purchasedItemUsed=true;state.capabilities['shop-finish-use']=enabled;
 html=screens().render(state);
 assert.match(html,/data-campaign-action="finish-use"[^>]*>Done</);
 assert.match(html,/Purchased item used/);
});
