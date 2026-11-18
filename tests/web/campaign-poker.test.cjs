const {test}=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs'),path=require('node:path'),vm=require('node:vm');

function loadCampaign() {
  const scope=vm.createContext({window:{},escapeHtml:value=>String(value).replaceAll('&','&amp;').replaceAll('"','&quot;').replaceAll('<','&lt;'),
    splitWords:value=>String(value).replace(/([a-z])([A-Z])/g,'$1 $2'),cardFaceHtml:()=>'',cardRank:face=>face.rank});
  const root=path.resolve(__dirname,'../../src/BidEuchre.Server/wwwroot');
  for(const name of ['Cards','Screens','Table']){
    vm.runInContext(fs.readFileSync(path.join(root,'campaign-'+name.toLowerCase()+'.js'),'utf8'),scope);
    scope['Campaign'+name]=scope.window['Campaign'+name];
  }
  return scope;
}

const players=['You','Opponent 1','Partner','Opponent 3'].map((name,seat)=>({seat,name,team:seat%2,cardCount:6,
  visibleCards:Array.from({length:6},(_,index)=>({id:seat*6+index,face:{rank:'Ace',suit:'Clubs'}}))}));
const pokerResult={winners:[{seat:1,category:'Full House',cardIds:[6,7,8,9,10]},{seat:2,category:'Full House',cardIds:[12,13,14,15,16]}],teamPoints:[4,4]};
const pokerHand={handNumber:2,bidder:0,contract:null,pokerResult,reason:'Poker showdown',teamZeroDelta:6,teamOneDelta:4,
  scoreBreakdowns:[{team:0,pokerPoints:4,gumballBonuses:[{id:'Poker',points:2}],finalDelta:6},{team:1,pokerPoints:4,finalDelta:4}]};

test('Poker reveals all four hands and highlights only the winning physical cards',()=>{
  const {CampaignTable}=loadCampaign();
  const html=CampaignTable.render({phase:'InMatch',capabilities:{commands:{enabled:true},'arrange-hand':{enabled:false}},table:{phase:'PokerReveal',
    pokerResult,scores:[8,11],handNumber:2,players,legalCommands:[{kind:'complete-poker'}],handHistory:[pokerHand],tricksByTeam:[0,0]}});
  assert.match(html,/Poker showdown/i);
  assert.match(html,/Opponent 1[^<]*<\/[^>]+>[^]*Full House/);
  assert.match(html,/Partner[^<]*<\/[^>]+>[^]*Full House/);
  assert.match(html,/Your team \+6 · Opponents \+4 · Includes \+2 Poker copy bonus/);
  assert.equal((html.match(/data-card-id=/g)||[]).length,24);
  const ids=[...html.matchAll(/class="[^"\n]*campaign-poker-winning-card[^"\n]*" data-card-id="(\d+)"/g)].map(m=>Number(m[1]));
  assert.deepEqual(ids,[6,7,8,9,10,12,13,14,15,16]);
  assert.doesNotMatch(html,/Open auction|Current trick|Last trick|Tricks:|Arrange hand|data-campaign-command/);
});

test('Poker history describes the points with no trick contract',()=>{
  const {CampaignScreens}=loadCampaign();
  const html=CampaignScreens.history([pokerHand],players.map(p=>p.name));
  assert.match(html,/Poker showdown/);
  assert.match(html,/Your team[^<]*4 Poker points \+ 2 Poker copy bonus = \+6/);
  assert.match(html,/Opponents[^<]*4 Poker points = \+4/);
  assert.doesNotMatch(html,/\bcontract\b|undefined|MADE|SET/);
});
