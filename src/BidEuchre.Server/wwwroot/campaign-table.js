window.CampaignTable = (() => {
  const e=value=>escapeHtml(value),words=value=>splitWords(value);
  function commandLabel(command,state) {
    const t=state.table;
    const cards=t.players.flatMap(p=>p.visibleCards);
    const card=cards.find(c=>c.id===(command.cardId??command.targetId));
    const own=t.players.find(p=>p.seat===0)?.visibleCards||[];
    const cardName=card?CampaignCards.label(card,own.findIndex(c=>c.id===card.id)>=0?own.findIndex(c=>c.id===card.id):null):'chosen card';
    const person=t.players.find(p=>p.seat===command.targetSeat)?.name||'Player '+(command.targetSeat+1);
    const item=category=>{const inv=category==='Tarot'?state.inventory.tarot:state.inventory.sigils;return CampaignCards.feature(state,category,inv.find(i=>i.slot===command.slot)?.id).name;};
    switch(command.kind){
      case 'bid':return 'Bid '+({Two:'2',Three:'3',Four:'4',Five:'5',Six:'6',PartnersBest:'Partner’s Best',Alone:'Alone'}[command.level]||words(command.level))+(command.variant?' · '+CampaignCards.feature(state,'Gumball',command.variant).name:'');
      case 'pass':return 'Pass';
      case 'panic-button':return 'Panic Button';
      case 'choose-contract':return command.mode==='Trump'?'Trump · '+command.trump:command.mode;
      case 'play':return 'Play '+cardName+(command.effectiveSuit?' · as '+command.effectiveSuit:'');
      case 'exchange':return 'Exchange '+cardName;
      case 'swap-partner-card':return 'Swap with partner · '+cardName;
      case 'reveal-partner-card':return 'Reveal to partner · '+cardName;
      case 'choose-reveal-seat':return 'Reveal '+person+'’s hand';
      case 'select-opponent':return 'Duel '+person;
      case 'use-tarot':return 'Use '+item('Tarot')+' · '+command.targetIds.length+' targets';
      case 'use-sigil':return 'Use '+item('Sigil')+' · '+cardName;
      default:return 'Unavailable command';
    }
  }
  function render(state) {
    const t=state.table;if(!t)return CampaignScreens.heading('Loading your table.');
    const commands=t.legalCommands||[],own=t.players.find(p=>p.seat===0);
    const contract=t.contract?words(t.contract.bid)+' · '+t.contract.mode+(t.contract.trump?' · '+t.contract.trump:''):t.highBid?words(t.highBid)+' high bid':'Open auction';
    const actionButtons=commands.map((c,index)=>'<button type="button" class="button '+(c.kind==='pass'?'quiet':'primary')+'" data-campaign-command="'+index+'" data-focus-key="'+e('command:'+JSON.stringify(c))+'" '+(state.capabilities.commands.enabled?'':'disabled')+'>'+e(commandLabel(c,state))+'</button>');
    const bids=commands.map((c,i)=>c.kind==='bid'&&c.variant?i:null).filter(i=>i!==null);
    return CampaignScreens.heading('Campaign table.','You and your partner face the opponents. Only your visible cards are shown.')+CampaignScreens.stats(state)
      +'<section class="campaign-score panel"><strong>Your team '+t.scores[0]+'</strong><span>Hand '+t.handNumber+' · '+e(words(t.phase))+' · '+e(contract)+'</span><strong>Opponents '+t.scores[1]+'</strong></section>'
      +'<section class="panel campaign-players" aria-label="Players">'+t.players.map(p=>'<article class="'+(p.seat===t.currentSeat?'current':'')+'"><h3>'+e(p.name)+(p.seat===0?' · You':p.seat===2?' · Partner':' · Opponent')+'</h3><p>'+p.cardCount+' cards'+(p.isSittingOut?' · Sitting out':'')+(p.seat===t.dealer?' · Dealer':'')+'</p>'
        +(p.seat!==0&&p.visibleCards.length?'<div class="campaign-card-row">'+p.visibleCards.map(card=>CampaignCards.html(card)).join('')+'</div>':'')+'</article>').join('')+'</section>'
      +'<div class="campaign-table-grid"><section class="panel campaign-hand"><div class="panel-heading"><h2>Your hand</h2>'+CampaignScreens.button('arrange','Arrange hand',{enabled:state.capabilities['arrange-hand'].enabled&&own.visibleCards.length>1,reason:state.capabilities['arrange-hand'].reason})+'</div><div class="campaign-card-row">'
        +own.visibleCards.map((card,index)=>CampaignCards.html(card,{interactive:commands.some(c=>c.cardId===card.id)&&state.capabilities.commands.enabled,index})).join('')+'</div><p class="campaign-prose">Gold outlines mark available card actions. Card position distinguishes matching faces.</p></section>'
      +'<section class="panel campaign-moves"><p class="eyebrow">'+(t.currentSeat===0?'Your move':'Waiting')+'</p><h2 aria-live="polite">'+e(t.currentSeat===0?words(t.pendingChoice||t.phase):t.phase==='HandComplete'?'Hand complete':t.currentSeat==null?'Review the score':t.players.find(p=>p.seat===t.currentSeat)?.name+' is choosing')+'</h2>'
        +(t.error?'<p class="alert">'+e(t.error)+'</p>':'')+'<div class="campaign-actions">'+actionButtons.filter((_,i)=>!bids.includes(i)).join('')+'</div>'
        +(bids.length?'<details><summary>Gumball bid variants · '+bids.length+' available</summary><div class="campaign-actions">'+bids.map(i=>actionButtons[i]).join('')+'</div></details>':'')
        +CampaignScreens.button('next-hand','Next hand',state.capabilities['next-hand'],'primary')+'</section></div>'
      +trick('Current trick',t.trick,t)+trick('Last trick',t.lastTrick,t)
      +'<section class="panel"><h2>Public table log</h2><p>Tricks: Your team '+t.tricksByTeam[0]+' · Opponents '+t.tricksByTeam[1]+'</p><ol class="event-list">'+(t.publicEvents||[]).slice(-15).map(x=>'<li>'+e(x)+'</li>').join('')+'</ol>'
        +(t.auction?.length?'<details><summary>Auction history</summary><ol>'+t.auction.map(a=>'<li>'+e(t.players.find(p=>p.seat===a.seat)?.name||'Player')+': '+e(a.level?words(a.level):a.bid?words(a.bid):'Pass')+'</li>').join('')+'</ol></details>':'')+'</section>'
      +CampaignScreens.history(t.handHistory,t.players.map(p=>p.name))+CampaignScreens.inventory(state);
  }
  function trick(title,plays,t) {
    if(!plays?.length)return '';
    return '<section class="panel"><h2>'+title+'</h2><div class="campaign-trick">'+plays.map(p=>'<figure>'+CampaignCards.html(p.card)+'<figcaption>'+e(t.players.find(x=>x.seat===p.seat)?.name)+'<br>'+e(p.effectiveSuit||p.card.face.suit)+'</figcaption></figure>').join('')+'</div></section>';
  }
  return {render,commandLabel};
})();
