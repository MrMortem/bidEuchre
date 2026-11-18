window.CampaignScreens = (() => {
  const e=value=>escapeHtml(value);
  const words=value=>splitWords(value);
  function button(action,label,capability,style='quiet',extra='') {
    return '<span class="campaign-control"><button type="button" class="button '+style+'" data-campaign-action="'+action+'" data-focus-key="'+e(action+':'+extra)+'" '+extra+' '+(capability?.enabled?'':'disabled')+'>'+e(label)+'</button>'
      +(!capability?.enabled&&capability?.reason?'<small class="disabled-reason">'+e(capability.reason)+'</small>':'')+'</span>';
  }
  function heading(title,subtitle='') {return '<header class="page-header"><div><p class="eyebrow">Campaign</p><h1 tabindex="-1">'+e(title)+'</h1><p>'+e(subtitle)+'</p></div></header>';}
  function stats(state) {
    const r=state.run;
    if(!r)return '';
    return '<dl class="campaign-stats">'+[['Lives',r.lives],['Ante',r.ante],['Match',r.matchInAnte+' / 3'+(r.boss?' · Boss':'')],['Money','$'+r.money],['Target',r.threshold],['Lower limit',r.negativeThreshold],['Hand limit',r.handLimit],['Opponent bid bonus','+'+r.opponentBonus]].map(([key,value])=>'<div><dt>'+e(key)+'</dt><dd>'+e(value)+'</dd></div>').join('')+'</dl>';
  }
  function featureCard(state,category,key,body) {
    const f=CampaignCards.feature(state,category,key);
    const art=CampaignCards.art(f);
    return '<article class="campaign-feature" data-category="'+e(category)+'">'+(art?'<img src="'+e(art)+'" alt="" loading="lazy">':'')+'<div><p class="eyebrow">'+e(category==='Tarot'?'Arcade ticket':category)+'</p><h3>'+e(f.name)+'</h3><p>'+e(f.description)+'</p>'+body+'</div></article>';
  }
  function inventory(state) {
    const inv=state.inventory;if(!inv)return '';
    const live=state.phase==='InMatch'&&!state.requiresContinue;
    const useCap=live?state.capabilities.commands:{enabled:false,reason:'Held items can only be used during a match.'};
    const itemCapability=item=>live&&item.targetCount>(state.table?.players.find(p=>p.seat===0)?.visibleCards.length||0)?{enabled:false,reason:'Needs '+item.targetCount+' cards in your hand.'}:useCap;
    const items=[...(inv.tarot||[]).map(x=>({...x,category:'Tarot'})),...(inv.sigils||[]).map(x=>({...x,category:'Sigil'}))];
    return '<section class="panel campaign-inventory"><h2>Your collection</h2><p class="campaign-prose">Items: '+inv.itemCount+' / '+inv.itemSlots+' shared Tickets + Sigil slots · Jokers: '+inv.jokers.length+' / '+inv.jokerSlots+'</p><div class="campaign-feature-grid">'
      +inv.jokers.map(j=>featureCard(state,'Joker',j.id,'<p>Value: '+j.value+' · Sell value: $'+j.sellValue+'</p>'+button('sell','Sell '+CampaignCards.feature(state,'Joker',j.id).name+' · $'+j.sellValue,j.sell,'quiet','data-joker-id="'+e(j.id)+'"'))).join('')
      +items.map(item=>featureCard(state,item.category,item.id,button('use-item','Use '+CampaignCards.feature(state,item.category,item.id).name,itemCapability(item),'primary','data-category="'+item.category+'" data-slot="'+item.slot+'"')
        +button('discard-item','Discard '+CampaignCards.feature(state,item.category,item.id).name,state.capabilities['items-discard'],'quiet','data-category="'+item.category+'" data-slot="'+item.slot+'"'))).join('')
      +(!inv.jokers.length&&!items.length?'<p class="campaign-prose">No held items yet. Visit the shop after a match.</p>':'')+'</div>'
      +'<details class="campaign-gumballs"><summary>Gumballs · '+inv.gumballs.length+' unlocked</summary><div class="campaign-feature-grid">'+inv.gumballs.map(g=>featureCard(state,'Gumball',g.id,'<p>'+g.copies+' '+(g.copies===1?'copy':'copies')+' · '+(g.bonus?'Successful bid bonus +'+g.bonus:'No extra bid bonus')+'</p>')).join('')+'</div></details></section>';
  }
  function shop(state) {
    const shop=state.shop;
    return heading('The shop.','Buy, use or sell before the next match.')+stats(state)+'<section class="panel"><div class="panel-heading"><h2>Available now</h2><span>Rerolls: '+shop.rerollCount+'</span></div><div class="campaign-feature-grid">'
      +(shop.arcade?CampaignArcadeView.tile(state):'')+shop.offers.map(offer=>{
        const f=CampaignCards.feature(state,offer.category,offer.featureId);
        if(offer.category==='Tarot'){
          const tickets=offer.tickets||[];
          const choices=tickets.length?tickets.map(ticket=>featureCard(state,'Tarot',ticket.id,
            button('keep-ticket','Keep '+CampaignCards.feature(state,'Tarot',ticket.id).name,ticket.keep,'quiet','data-offer-id="'+e(offer.id)+'" data-ticket-id="'+e(ticket.id)+'"')
            +button('use-ticket','Use '+CampaignCards.feature(state,'Tarot',ticket.id).name+' now',ticket.use,'primary','data-offer-id="'+e(offer.id)+'" data-ticket-id="'+e(ticket.id)+'"'))).join('')
            :(offer.printOptions||[]).map(option=>button('print-tickets','Print '+option.count+' '+(option.count===1?'ticket':'tickets')+' · $'+option.price,option.buy,'primary','data-offer-id="'+e(offer.id)+'" data-count="'+option.count+'"')).join('');
          return '<div class="campaign-shop-offer"><figure class="campaign-machine"><img src="/pixel/machine-fortune-teller-ticket-dispenser.png" alt="" loading="lazy"><figcaption>Fortune Teller · Ticket dispenser</figcaption></figure><div><h3>Fortune Teller</h3><p>'+(offer.sold?'Already printed this visit.':tickets.length?'Keep or use one printed ticket. The other tickets are lost.':'Print a blind ticket, or print three and choose one.')+'</p>'+choices+'</div></div>';
        }
        const card=featureCard(state,offer.category,offer.featureId,'<p class="campaign-price">$'+offer.price+(offer.sold?' · Sold':'')+'</p>'
          +button(offer.category==='Gumball'?'pull':'buy',offer.category==='Gumball'?'Pull gumball · $'+offer.price:'Buy '+f.name+' · $'+offer.price,offer.buy,'primary','data-offer-id="'+e(offer.id)+'"')
          +(offer.category==='Sigil'?button('buy-use','Buy & use '+f.name+' · $'+offer.price,offer.buyUse,'quiet','data-offer-id="'+e(offer.id)+'"'):''));
        const machine=offer.category==='Gumball'?['gumball','Gumball machine']
          :offer.category==='Tarot'?['fortune-teller-ticket-dispenser','Fortune Teller · Ticket dispenser']:null;
        return machine?'<div class="campaign-shop-offer"><figure class="campaign-machine"><img src="/pixel/machine-'+machine[0]+'.png" alt="" loading="lazy"><figcaption>'+machine[1]+'</figcaption></figure>'+card+'</div>':card;
      }).join('')+'</div><div class="campaign-actions">'+button('shop-reroll','Reroll · $'+shop.rerollPrice,shop.reroll)+button('shop-leave','Leave shop',state.capabilities['shop-leave'],'primary')+'</div></section>'+inventory(state);
  }
  function useSession(state) {
    const session=state.shop.useSession,purchased=CampaignCards.feature(state,session.category,session.featureId);
    const choice=(category,key,body)=>{
      const feature=CampaignCards.feature(state,category,key),art=CampaignCards.art(feature);
      return '<article class="campaign-session-item">'+(art?'<img src="'+e(art)+'" alt="">':'')+'<div><h3>'+e(feature.name)+'</h3>'+body+'</div></article>';
    };
    const choices=choice(session.category,session.featureId,
      '<p class="campaign-purchase-status">'+(session.purchasedItemUsed?'Purchased item used.':'Purchased · must be used before Done.')+'</p>'
      +button('select-session-item',session.purchasedItemUsed?'Already used':'Choose '+purchased.name,{enabled:!session.purchasedItemUsed},'primary','data-purchased="true"'));
    const held=(state.inventory?.tarot||[]).map(item=>choice('Tarot',item.id,
      button('select-session-item','Choose '+CampaignCards.feature(state,'Tarot',item.id).name,state.capabilities['items-use'],'quiet','data-slot="'+item.slot+'"'))).join('');
    return heading('Use your purchase.','Your purchase is paid. Use it before leaving these six offered cards.')+stats(state)
      +'<section class="panel campaign-use-session" data-shop-use-session><h2>Six-card opportunity</h2><p class="campaign-prose">You may also use any tickets from your inventory on these same cards, before or after your purchase.</p>'
      +'<div class="campaign-session-items">'+choices+held+'</div>'
      +'<h3 data-session-item-name tabindex="-1"></h3><p class="campaign-prose" data-session-description></p><p class="campaign-prose" data-session-selection-status role="status"></p>'
      +'<div class="campaign-deck-grid campaign-session-cards">'+session.targetCards.map((card,index)=>'<div>'+CampaignCards.html(card,{interactive:true,index,focusKey:'shop-card:'+card.id})+'<small>'+e(CampaignCards.label(card))+'</small></div>').join('')+'</div>'
      +'<div class="campaign-actions">'+button('apply-session-item','Use selected item',{enabled:false},'primary')
      +button('finish-use','Done',state.capabilities['shop-finish-use'],'quiet')+'</div></section>';
  }
  function history(history,players=[]) {
    return '<details class="panel campaign-history"><summary>Public score history · '+(history?.length||0)+' hands</summary>'+(history||[]).map(hand=>{
      const contract=hand.contract;
      if(hand.pokerResult) {
        return '<article><h3>Hand '+hand.handNumber+' · Poker showdown</h3>'
          +hand.pokerResult.winners.map(w=>'<p>'+e(players[w.seat]||'Player '+(w.seat+1))+' · '+e(w.category)+'</p>').join('')
          +'<p>Your team '+signed(hand.teamZeroDelta)+' · Opponents '+signed(hand.teamOneDelta)+'</p>'
          +(hand.scoreBreakdowns||[]).map(b=>'<p>'+(b.team===0?'Your team':'Opponents')+' · '+b.pokerPoints+' Poker points'
            +(b.gumballBonuses||[]).map(g=>' + '+g.points+' '+e(words(g.id||g.gumball||'Gumball'))+' copy bonus').join('')+' = '+signed(b.finalDelta)+'</p>').join('')+'</article>';
      }
      return '<article><h3>Hand '+hand.handNumber+' · '+e(players[hand.bidder]||'Player '+(hand.bidder+1))+'</h3><p>'+e(contract?words(contract.bid)+' · '+words(contract.mode)+(contract.trump?' '+contract.trump:''):'')+' · '+e(hand.reason)+'</p>'
        +'<p>Your team '+signed(hand.teamZeroDelta)+' · Opponents '+signed(hand.teamOneDelta)+'</p>'
        +(hand.scoreBreakdowns||[]).map(b=>'<p>'+(b.team===0?'Your team':'Opponents')+': ('+b.contractPoints+' contract + '+b.cardPoints+' cards + '+b.jokerPoints+' Jokers + '+b.trickModifierPoints+' trick effects) × '+b.handMultiplier
          +' + '+b.opponentBidBonus+' opponent bid bonus'+(b.gumballBonuses||[]).map(g=>' + '+g.points+' '+e(words(g.id||g.gumball||'Gumball'))).join('')+' = '+signed(b.finalDelta)+'</p>').join('')+'</article>';
    }).join('')+'</details>';
  }
  function signed(value){return value>0?'+'+value:String(value??0);}
  function result(state){
    const result=state.pendingResult,o=result.outcome,summary=result.summary;
    return heading(o.forfeited?'Match forfeited.':o.won?'Match won.':'Match lost.','Review the result before continuing.')+stats(state)
      +'<section class="panel"><h2>Match result</h2>'+(result.scores?.length?'<p class="campaign-result-score">Your team '+result.scores[0]+' · Opponents '+result.scores[1]+'</p>':'')
      +'<dl class="campaign-stats">'+[['Lives lost',o.livesLost],['Base reward','$'+o.baseReward],['Margin bonus','$'+o.marginBonus],['Payout','$'+o.payout],['Joker income','$'+o.jokerIncome],['Interest','$'+o.interest],['Balance','$'+o.money]].map(([k,v])=>'<div><dt>'+e(k)+'</dt><dd>'+e(v)+'</dd></div>').join('')+'</dl>'
      +(summary?.completionReason?'<p>'+e(summary.completionReason)+'</p>':'')
      +button('result-continue','Continue',state.capabilities['result-continue'],'primary')+'</section>'
      +(summary?history(summary.handHistory,summary.playerNames)+'<details class="panel campaign-history"><summary>Public match log</summary><ol>'+(summary.publicEvents||[]).map(x=>'<li>'+e(x)+'</li>').join('')+'</ol></details>':'');
  }
  function render(state) {
    if(state.requiresContinue)return heading('Continue your campaign.','Your match is saved. Continue to resume the same hand without losing a life.')+stats(state)+button('continue','Continue run',state.capabilities.continue,'primary');
    switch(state.phase){
      case 'Empty':return heading('Start a campaign.','Build your shared deck and collection across a run. Progress saves in this browser.')+button('new','New run',state.capabilities.new,'primary');
      case 'Recovery':return heading('Saved run needs recovery.','The saved file could not be read. It is preserved until you explicitly replace or delete it.')+button('new','New run',state.capabilities.new,'primary');
      case 'Shop':return state.shop?.useSession?useSession(state):shop(state);
      case 'Result':return result(state);
      case 'Victory':return heading('Victory!','You cleared the campaign. Continue with your collection to keep playing.')+stats(state)+button('victory-continue','Keep playing',state.capabilities['victory-continue'],'primary');
      case 'RunOver':return heading('Run over.','Your final collection and deck are below.')+stats(state)+button('new','New run',state.capabilities.new,'primary')+inventory(state);
      case 'Intro':return heading('Ante '+state.run.ante+' · '+(state.run.boss?'Boss match.':'Match '+state.run.matchInAnte+'.'),'Meet the target before the hand limit. Your shared card effects carry into each match.')+stats(state)+button('start-match','Start match',state.capabilities['start-match'],'primary')+inventory(state);
      case 'InMatch':return CampaignTable.render(state);
      default:return heading('Campaign unavailable.','Refresh the campaign to try again.');
    }
  }
  function backdrop(state) {
    const ante=state?.run?.ante;
    const arena=state?.phase==='InMatch'&&!state.requiresContinue&&state.run?.boss&&Number.isInteger(ante)&&ante>0
      ? ['club','diamond','heart','spade','cursed'][(ante-1)%5] : null;
    document.body.classList.toggle('campaign-boss-arena',!!arena);
    if(arena)document.body.style.setProperty('--table-base','url("/pixel/boss-arena-'+arena+'.png")');
    else document.body.style.removeProperty('--table-base');
  }
  return {render,button,heading,stats,inventory,history,backdrop};
})();
